using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

using FlowMemo.Core.DataTransferObjects;
using FlowMemo.Core.Enums;
using FlowMemo.Core.Repositories;
using FlowMemo.Core.Sync;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Interop;
using FlowMemo.WpfUi.Interop.Constants;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.Resources;
using FlowMemo.WpfUi.Services;
using FlowMemo.WpfUi.Themes;

namespace FlowMemo.WpfUi.ViewModels;

public class NoteViewModel : BaseViewModel
{
    private readonly NoteRepository _noteRepository;
    private readonly ThemeService _themeService;
    private readonly SyncService _syncService;

    private readonly DispatcherTimer _saveTimer;

    public NoteViewModel(
        NoteRepository noteRepository,
        AppMetadataService appMetadataService,
        SettingsService settingsService,
        MessengerService messengerService,
        ThemeService themeService,
        SyncService syncService
    ) : base(appMetadataService, settingsService, messengerService)
    {
        _noteRepository = noteRepository;
        _themeService = themeService;
        _syncService = syncService;

        ChangeThemeColourCommand = new RelayCommand<string>(ChangeThemeColour);

        NoteSettings = SettingsService.NoteSettings;
        NoteSettings.PropertyChanged += OnNoteSettingsChanged;
        EditorSettings = SettingsService.EditorSettings;
        SettingsService.ApplicationSettings.PropertyChanged += OnApplicationSettingsChanged;

        _saveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _saveTimer.Tick += OnSaveTimerTick;
    }

    public RelayCommand<string> ChangeThemeColourCommand { get; }

    public NoteSettingsModel NoteSettings { get; set; }
    public EditorSettingsModel EditorSettings { get; set; }

    public NoteModel Note { get; set; } = null!;

    public async Task Initialize(int? noteId = null, NoteModel? parent = null, nint? managementWindowHandle = null)
    {
        if (noteId is null)
            await CreateNewNote(parent, managementWindowHandle);
        else
            await LoadNote((int)noteId);
    }

    public void OnWindowLoaded(nint windowHandle)
    {
        Note.WindowHandle = windowHandle;
        UpdateVisibility();

        _saveTimer.Start();

        Note.PropertyChanged += OnNotePropertyChangedForSync;
        MessengerService.Subscribe<NoteSyncChangedMessage>(OnNoteSyncChanged);
        ErrorReporter.FireAndForget(RefreshSyncBanner(), "Sync banner");
    }

    /// <summary>메모에 포커스가 왔을 때. 고치기 전에 다른 PC의 변경을 먼저 받는다.</summary>
    public void OnActivated()
        => _syncService.OnNoteFocused();

    private void OnNotePropertyChangedForSync(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteModel.Content))
            _syncService.ReportEditing(Note.Id);
    }

    private void OnNoteSyncChanged(NoteSyncChangedMessage message)
    {
        if (message.NoteId != Note.Id)
            return;

        if (message.NewColour is not null)
            ChangeThemeColour(message.NewColour);

        ErrorReporter.FireAndForget(RefreshSyncBanner(), "Sync banner");
    }

    private async Task RefreshSyncBanner()
    {
        if (!_syncService.IsConnected)
        {
            Note.SetSyncBanner(SyncNoteStatus.None, null);
            return;
        }

        NoteSyncRow? row = await _syncService.GetRow(Note.Id);
        SyncNoteStatus status = row?.Status ?? SyncNoteStatus.None;
        string device = row?.PendingDevice ?? "";

        string? text = status switch
        {
            SyncNoteStatus.Incoming => string.Format(Strings.Sync_Banner_Incoming, device, row?.PendingAt?.ToLocalTime()),
            SyncNoteStatus.Conflict => string.Format(Strings.Sync_Banner_Conflict, device),
            SyncNoteStatus.RemoteDeleted => string.Format(Strings.Sync_Banner_RemoteDeleted, device),
            _ => _syncService.GetEditingDevice(Note.Id) is string editing ? string.Format(Strings.Sync_Banner_Editing, editing) : null
        };

        Note.SetSyncBanner(status, text);
    }

    /// <summary>비교 창에 보여 줄 상태. 고치던 내용을 먼저 저장해서 최신 내용으로 비교한다.</summary>
    public async Task<NoteSyncRow?> PrepareCompare()
    {
        await SaveNote();
        NoteSyncRow? row = await _syncService.GetRow(Note.Id);
        return row is { HasPending: true, PendingDeleted: false } ? row : null;
    }

    /// <summary>메모 상단의 [가져오기]. 그 사이 이 PC에서 고쳤다면 false를 돌려준다 (비교 창으로 넘긴다).</summary>
    public async Task<bool> AcceptRemote()
    {
        NoteSyncRow? row = await PrepareCompare();
        if (row is null)
            return true;
        if (row.Status == SyncNoteStatus.Conflict)
            return false;

        await ApplyDecision(row, SyncDecision.TakeRemote, null);
        return true;
    }

    public Task SkipRemote()
        => _syncService.Ignore(Note.Id);

    /// <summary>다른 PC에서 지운 메모를 이 PC에서도 휴지통으로 보낸다.</summary>
    public async Task AcceptRemoteDeletion()
    {
        await SaveNote();
        await _noteRepository.MoveToTrash(Note.Id, DateTime.UtcNow);
        await _syncService.CommitPending(Note.Id);
    }

    /// <summary>다른 PC에서 지웠지만 이 PC 메모를 남긴다 (다른 PC에도 다시 나타난다).</summary>
    public async Task KeepAfterRemoteDeletion()
    {
        await SaveNote();
        await _syncService.CommitPending(Note.Id);
    }

    public async Task ApplyDecision(NoteSyncRow compared, SyncDecision decision, string? mergedText)
    {
        if (decision is SyncDecision.None)
            return;

        // 비교하는 동안 더 새로운 변경이 왔다면 본 적 없는 내용을 적용하지 않는다.
        NoteSyncRow? latest = await _syncService.GetRow(Note.Id);
        if (latest is null || latest.PendingRevision != compared.PendingRevision)
        {
            await RefreshSyncBanner();
            return;
        }

        switch (decision)
        {
            case SyncDecision.Skip:
                await _syncService.Ignore(Note.Id);
                return;

            case SyncDecision.TakeRemote:
                Note.Content = latest.PendingContent ?? "";
                if (latest.PendingColour is not null)
                    ChangeThemeColour(latest.PendingColour);
                break;

            case SyncDecision.Merge:
                Note.Content = mergedText ?? Note.Content;
                break;

            case SyncDecision.KeepBoth:
                await CreateCopyOfRemote(latest);
                break;

            case SyncDecision.KeepLocal:
                break;
        }

        await SaveNote();
        await _syncService.CommitPending(Note.Id);
    }

    /// <summary>다른 PC 내용을 새 메모로 만들어 연다. 원래 메모는 이 PC 내용을 유지한다.</summary>
    private async Task CreateCopyOfRemote(NoteSyncRow row)
    {
        const double offset = 45;
        DateTime now = DateTime.UtcNow;

        int copyId = await _noteRepository.Create(new NoteDto(
            0,
            row.PendingContent ?? "",
            Note.X + offset,
            Note.Y + offset,
            Note.Width,
            Note.Height,
            Note.GravityX,
            Note.GravityY,
            row.PendingColour ?? Note.ThemeColourScheme,
            false,
            true,
            false,
            now,
            now,
            null
        ));

        MessengerService.Publish(new OpenNoteWindowMessage(NoteId: copyId));
    }

    public void OnWindowMoved(double left, double top)
    {
        // Reset gravity depending what position the note was moved to.
        // This does not effect the saved start up setting, only what
        // direction new child notes will go towards.
        Note.X = left;
        Note.Y = top;

        Rect screenBounds = ScreenHelper.GetCurrentScreenBounds(Note.WindowHandle);
        Note.GravityX = (left - screenBounds.X < screenBounds.Width / 2) ? 1 : -1;
        Note.GravityY = (top - screenBounds.Y < screenBounds.Height / 2) ? 1 : -1;
    }

    public void UpdateOpacity()
    {
        TransparencyMode transparentMode = NoteSettings.TransparencyMode;
        if (transparentMode == TransparencyMode.Disabled)
        {
            Note.Opacity = 1.0;
            return;
        }

        bool opaqueWhenFocused = NoteSettings.OpaqueWhenFocused;

        double opaqueOpacity = NoteSettings.OpaqueValue;
        double transparentOpacity = NoteSettings.TransparentValue;

        if ((opaqueWhenFocused && Note.IsFocused) || (transparentMode == TransparencyMode.WhenPinned && !Note.IsPinned))
            Note.Opacity = opaqueOpacity;
        else
            Note.Opacity = transparentOpacity;
    }

    public async Task SaveNote()
    {
        if (Note.IsSaved)
            return;

        await _noteRepository.Update(
            Note.ToDto()
        );

        Note.IsSaved = true;

        MessengerService.Publish<NoteActionMessage>(new(NoteAction.Updated, Note.ToDto()));
    }

    /// <summary>창이 닫힐 때 호출한다. 앱 전체에서 살아 있는 설정 모델에 대한 구독을 푼다.</summary>
    public void Detach()
    {
        _saveTimer.Stop();
        NoteSettings.PropertyChanged -= OnNoteSettingsChanged;
        SettingsService.ApplicationSettings.PropertyChanged -= OnApplicationSettingsChanged;
        MessengerService.Unsubscribe<NoteSyncChangedMessage>(OnNoteSyncChanged);
        Note.PropertyChanged -= OnNotePropertyChangedForSync;
    }

    public async Task<bool> CloseNote()
    {
        _saveTimer.Stop();

        MessengerService.Publish<NoteActionMessage>(new(NoteAction.Closed, Note.ToDto()));

        // 공백·줄바꿈만 남은 메모도 빈 메모로 보고 지운다 ('끝에 빈 줄 유지' 때문에 줄바꿈만 남는 경우가 있다).
        if (string.IsNullOrWhiteSpace(Note.Content))
        {
            // Delete note if empty, TO DO: Add setting for this behaviour
            await _noteRepository.Delete(Note.Id);
            MessengerService.Publish<NoteActionMessage>(new(NoteAction.Deleted, Note.ToDto()));
            return false;
        }

        // IsOpen is updated when users closes note via close button,
        // it's left true if windows is closed by exiting app so it will re-open with app.
        await SaveNote();

        return false;
    }

    private async Task CreateNewNote(NoteModel? parent = null, nint? managementWindowHandle = null)
    {
        Note = new(NoteSettings, _themeService.GetNewNoteColourSchemeName(parent?.ThemeColourScheme));

        InitNotePosition(parent, managementWindowHandle);
        UpdateBrushes();
        UpdateOpacity();

        Note.IsOpen = true;

        Note.Id = await _noteRepository.Create(Note.ToDto());

        Note.IsSaved = true;

        MessengerService.Publish<NoteActionMessage>(new(NoteAction.Created, Note.ToDto()));
    }

    private async Task LoadNote(int noteId)
    {
        Note = new(await _noteRepository.GetById((int)noteId), NoteSettings);

        EnsureSavedPositionIsOnScreen();
        UpdateBrushes();
        UpdateOpacity();

        Note.IsOpen = true;

        await SaveNote();
    }

    private DateTime _lastSaveErrorReport = DateTime.MinValue;

    private async void OnSaveTimerTick(object? sender, EventArgs e)
    {
        try
        {
            await SaveNote();
        }
        catch (Exception ex)
        {
            // 5초마다 실패해도 대화 상자가 계속 뜨지 않게 1분에 한 번만 알린다.
            if (DateTime.Now - _lastSaveErrorReport > TimeSpan.FromMinutes(1))
            {
                _lastSaveErrorReport = DateTime.Now;
                ErrorReporter.Report(ex, "SaveNote (timer)");
            }
            else
            {
                ErrorReporter.Log(ex, "SaveNote (timer)");
            }
        }
    }

    /// <summary>
    /// 모니터를 떼어내거나 배치·배율을 바꾸면 저장된 위치가 화면 밖일 수 있다.
    /// 창을 만들기 전에 확인해서, 화면 밖이면 주 모니터 왼쪽 위로 옮긴다.
    /// </summary>
    private void EnsureSavedPositionIsOnScreen()
    {
        Rect titleArea = new(Note.X, Note.Y, Math.Max(Note.Width, 50), 30);
        if (ScreenHelper.IsDipRectOnAnyScreen(titleArea))
            return;

        const int screenMargin = 78;
        Rect workArea = ScreenHelper.GetPrimaryScreenBounds();
        Note.X = workArea.Left + screenMargin;
        Note.Y = workArea.Top + screenMargin;
        Note.GravityX = 1;
        Note.GravityY = 1;
    }

    private void InitNotePosition(NoteModel? parent = null, nint? managementWindowHandle = null)
    {
        int noteMargin = 45;

        Point position = new(0, 0);
        Rect screenBounds;

        if (parent != null)
        {
            screenBounds = ScreenHelper.GetCurrentScreenBounds(parent.WindowHandle);

            Note.GravityX = parent.GravityX;
            Note.GravityY = parent.GravityY;

            position.X = parent.X + (noteMargin * Note.GravityX);
            position.Y = parent.Y + (noteMargin * Note.GravityY);
        }
        else
        {
            int screenMargin = 78;
            if (managementWindowHandle is not null)
                screenBounds = ScreenHelper.GetCurrentScreenBounds((nint)managementWindowHandle);
            else
                screenBounds = ScreenHelper.GetPrimaryScreenBounds();

            switch (NoteSettings.StartupPosition)
            {
                case StartupPosition.TopLeft:
                case StartupPosition.MiddleLeft:
                case StartupPosition.BottomLeft:
                    position.X = screenBounds.Left + screenMargin;
                    Note.GravityX = 1;
                    break;
                case StartupPosition.TopCentre:
                case StartupPosition.MiddleCentre:
                case StartupPosition.BottomCentre:
                    position.X = screenBounds.Left + screenBounds.Width / 2 - Note.Width / 2;
                    Note.GravityX = 1;
                    break;
                case StartupPosition.TopRight:
                case StartupPosition.MiddleRight:
                case StartupPosition.BottomRight:
                    position.X = screenBounds.Left + screenBounds.Width - screenMargin - Note.Width;
                    Note.GravityX = -1;
                    break;
            }

            switch (NoteSettings.StartupPosition)
            {
                case StartupPosition.TopLeft:
                case StartupPosition.TopCentre:
                case StartupPosition.TopRight:
                    position.Y = screenBounds.Top + screenMargin;
                    Note.GravityY = 1;
                    break;
                case StartupPosition.MiddleLeft:
                case StartupPosition.MiddleCentre:
                case StartupPosition.MiddleRight:
                    position.Y = screenBounds.Top + screenBounds.Height / 2 - Note.Height / 2;
                    Note.GravityY = -1;
                    break;
                case StartupPosition.BottomLeft:
                case StartupPosition.BottomCentre:
                case StartupPosition.BottomRight:
                    position.Y = screenBounds.Top + screenBounds.Height - screenMargin - Note.Height;
                    Note.GravityY = -1;
                    break;
            }
        }

        // Apply noteMargin if another note is already in that position
        if (Application.Current.Windows.Count > 1)
        {
            Window[] otherWindows = new Window[Application.Current.Windows.Count];
            Application.Current.Windows.CopyTo(otherWindows, 0);

            // 예전에는 Left/Top을 ==로 비교했다. 125% 배율 등에서는 창 위치가 픽셀→DIP 변환을 거쳐
            // 78이 78.4가 되므로 같은 자리로 보지 않았고, 새 메모가 기존 메모와 정확히 겹쳐 열렸다
            // (기존 메모가 고정돼 있으면 새 메모가 그 뒤에 완전히 가려졌다 — 실제 사용 중 발견).
            const double sameSpotTolerance = 8;
            bool IsOccupied(Point p) => otherWindows.Any(w =>
                Math.Abs(w.Left - p.X) < sameSpotTolerance && Math.Abs(w.Top - p.Y) < sameSpotTolerance);

            while (IsOccupied(position))
            {
                double newX = position.X + (noteMargin * Note.GravityX);
                if (newX < screenBounds.Left)
                    newX = screenBounds.Left;
                else if (newX + Note.Width > screenBounds.Right)
                    newX = screenBounds.Right - Note.Width;

                double newY = position.Y + (noteMargin * Note.GravityY);
                if (newY < screenBounds.Top)
                    newY = screenBounds.Top;
                else if (newY + Note.Height > screenBounds.Bottom)
                    newY = screenBounds.Bottom - Note.Height;

                if (position.X == newX && position.Y == newY)
                    break;

                position.X = newX;
                position.Y = newY;
            }
        }

        Note.X = position.X;
        Note.Y = position.Y;
    }

    private void UpdateBrushes()
    {
        AppMetadataService.Metadata.ColourScheme = Note.ThemeColourScheme;

        Palette palette = _themeService.GetPalette(Note.ThemeColourScheme, NoteSettings.ColourMode);
        Note.UpdateBrushes(palette);
    }

    private void ChangeThemeColour(string colourScheme)
    {
        Note.ThemeColourScheme = colourScheme;
        UpdateBrushes();
    }

    private void OnNoteSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NoteSettingsModel.TransparencyMode):
            case nameof(NoteSettingsModel.OpaqueWhenFocused):
            case nameof(NoteSettingsModel.OpaqueValue):
            case nameof(NoteSettingsModel.TransparentValue):
                UpdateOpacity();
                break;
            case nameof(NoteSettingsModel.ColourMode):
                UpdateBrushes();
                break;
            case nameof(NoteSettingsModel.VisibilityMode):
                UpdateVisibility();
                break;
        }
    }

    private void OnApplicationSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationSettingsModel.TrayOnly))
            UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (Note.WindowHandle == 0)
            return;

        nint exStyle = User32.GetWindowLongPtrW(Note.WindowHandle, GWL.EXSTYLE);

        VisibilityMode visibilityMode = NoteSettings.VisibilityMode;
        if (SettingsService.ApplicationSettings.TrayOnly && visibilityMode == VisibilityMode.ShowInTaskbar)
            visibilityMode = VisibilityMode.HideInTaskbar;

        switch (visibilityMode)
        {
            default:
            case VisibilityMode.ShowInTaskbar:
                exStyle &= ~WS_EX.TOOLWINDOW;
                Note.ShowInTaskbar = true;
                break;
            case VisibilityMode.HideInTaskbar:
                exStyle &= ~WS_EX.TOOLWINDOW;
                Note.ShowInTaskbar = false;
                break;
            case VisibilityMode.HideInTaskbarAndTaskSwitcher:
                exStyle |= WS_EX.TOOLWINDOW;
                Note.ShowInTaskbar = false;
                break;
        }

        _ = User32.SetWindowLongPtrW(Note.WindowHandle, GWL.EXSTYLE, exStyle);
    }
}
