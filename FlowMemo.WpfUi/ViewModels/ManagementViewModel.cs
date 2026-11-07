using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

using FlowMemo.Core.DataTransferObjects;
using FlowMemo.Core.Enums;
using FlowMemo.Core.Repositories;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.Resources;
using FlowMemo.WpfUi.Services;
using FlowMemo.WpfUi.Themes;

namespace FlowMemo.WpfUi.ViewModels;

/// <summary>
/// 메모 관리 창. 메모 목록과 휴지통 두 가지 보기를 오간다.
/// - 메모 목록: Delete는 휴지통으로 옮긴다 (확인 없이, 휴지통에서 되살릴 수 있으므로).
/// - 휴지통: Enter는 복원, Delete는 영구 삭제 (확인 후). 30일이 지난 메모는 시작할 때 지운다.
/// </summary>
public class ManagementViewModel : BaseViewModel, INotifyPropertyChanged
{
    private readonly NoteRepository _noteRepository;
    private readonly ThemeService _themeService;
    private readonly WindowService _windowService;

    private readonly Dictionary<int, int> _notePreviewIdIndexMap = []; // Use this since we don't have a Observable Dictionary
    private readonly Dictionary<int, NotePreviewModel> _selectedNotePreviews = [];

    public ManagementViewModel(
        NoteRepository noteRepository,
        AppMetadataService appMetadataService,
        SettingsService settingsService,
        MessengerService messengerService,
        ThemeService themeService,
        WindowService windowService
    ) : base(appMetadataService, settingsService, messengerService)
    {
        _noteRepository = noteRepository;
        _themeService = themeService;
        _windowService = windowService;

        NewNoteCommand = new RelayCommand(OnNewNoteCommand);
        OpenSettingsCommand = new RelayCommand(OnOpenSettingsCommand);

        OpenNotesCommand = new RelayCommand(OnOpenNotesCommand);
        CloseNotesCommand = new RelayCommand(OnCloseNotesCommand);
        // 선택한 메모가 없으면 삭제·복원을 비활성으로 둔다 (예전에는 눌러도 아무 일도 없었다).
        DeleteNotesCommand = new RelayCommand(OnDeleteNotesCommand, () => _selectedNotePreviews.Count > 0);
        RestoreNotesCommand = new RelayCommand(OnRestoreNotesCommand, () => IsTrashMode && _selectedNotePreviews.Count > 0);
        EmptyTrashCommand = new RelayCommand(OnEmptyTrashCommand, () => TrashCount > 0);
        ShowTrashCommand = new RelayCommand(() => SwitchMode(trashMode: true));
        ShowNotesCommand = new RelayCommand(() => SwitchMode(trashMode: false));

        // 명령을 만든 뒤에 불러온다 (불러오는 중에 명령의 활성 상태를 갱신한다).
        ErrorReporter.FireAndForget(LoadNotes(), "Management load");

        MessengerService.Subscribe<NoteActionMessage>(OnNoteActionMessage);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand NewNoteCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    /// <summary>메모 목록에서는 열기, 휴지통에서는 복원 (Enter·더블클릭).</summary>
    public ICommand OpenNotesCommand { get; }
    public ICommand CloseNotesCommand { get; }
    /// <summary>메모 목록에서는 휴지통으로, 휴지통에서는 영구 삭제 (Delete).</summary>
    public RelayCommand DeleteNotesCommand { get; }
    public RelayCommand RestoreNotesCommand { get; }
    public RelayCommand EmptyTrashCommand { get; }
    public ICommand ShowTrashCommand { get; }
    public ICommand ShowNotesCommand { get; }

    public ObservableCollection<NotePreviewModel> NotePreviews { get; } = [];

    public bool IsTrashMode
    {
        get;
        private set
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(EmptyText));
            OnPropertyChanged(nameof(HintText));
        }
    }

    public int TrashCount
    {
        get;
        private set
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TrashButtonText));
            EmptyTrashCommand.RaiseCanExecuteChanged();
        }
    }

    public string WindowTitle => IsTrashMode ? Strings.Management_TrashTitle : Strings.Management_Title;
    public string TrashButtonText => string.Format(Strings.Management_Trash, TrashCount);
    public string EmptyText => IsTrashMode ? Strings.Management_TrashEmpty : Strings.Management_Empty;
    public string HintText => IsTrashMode ? Strings.Management_TrashHint : Strings.Management_Hint;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private async Task LoadNotes()
    {
        ColourMode colourMode = SettingsService.NoteSettings.ColourMode;

        ClearNotePreviews();

        IEnumerable<NoteDto> noteDtos = IsTrashMode
            ? await _noteRepository.GetDeleted()
            : await _noteRepository.GetAll();
        foreach (NoteDto noteDto in noteDtos)
        {
            AddNotePreview(noteDto, colourMode);
            // DB의 IsOpen은 '다음 시작 때 다시 열지'라서, 지금 실제로 창이 열려 있는지로 바꿔 보여 준다.
            NotePreviews[^1].IsOpen = !IsTrashMode && _windowService.IsNoteWindowOpen(noteDto.Id);
        }

        await RefreshTrashCount();
        RaiseSelectionCommandsChanged();
    }

    private async Task RefreshTrashCount()
        => TrashCount = await _noteRepository.CountDeleted();

    private void SwitchMode(bool trashMode)
    {
        if (IsTrashMode == trashMode)
            return;

        IsTrashMode = trashMode;
        ErrorReporter.FireAndForget(LoadNotes(), "Management switch view");
    }

    private void OnNoteActionMessage(NoteActionMessage message)
    {
        switch (message.Action)
        {
            case NoteAction.Created:
                if (!IsTrashMode)
                    AddNotePreview(message.NoteDto);
                break;
            case NoteAction.Updated:
                UpdatedNotePreview(message.NoteDto);
                break;
            case NoteAction.Closed:
                if (_notePreviewIdIndexMap.TryGetValue(message.NoteDto.Id, out int closedIndex))
                    NotePreviews[closedIndex].IsOpen = message.NoteDto.IsOpen;
                break;
            case NoteAction.Deleted:
                RemoveNotePreview(message.NoteDto.Id);
                break;
        }
    }

    /// <summary>관리 창이 닫힐 때 호출한다. 구독을 풀지 않으면 닫힌 창의 뷰모델이 계속 메시지를 받는다.</summary>
    public void Detach()
        => MessengerService.Unsubscribe<NoteActionMessage>(OnNoteActionMessage);

    private void UpdatedNotePreview(NoteDto dto)
    {
        if (!_notePreviewIdIndexMap.TryGetValue(dto.Id, out int index))
            return;

        NotePreviewModel notePreview = NotePreviews[index];

        notePreview.ContentPreview = dto.Content;
        notePreview.ThemeColourScheme = dto.ThemeColourScheme;
        // 메모를 여는 중(창 등록 전)에도 저장이 일어나므로 창 목록 대신 저장한 값을 쓴다.
        notePreview.IsOpen = dto.IsOpen;

        UpdateNotePreviewBrushes(notePreview);
    }

    private void UpdateNotePreviewBrushes(NotePreviewModel notePreview, ColourMode? colourMode = null)
    {
        Palette palette = _themeService.GetPalette(
            notePreview.ThemeColourScheme,
            colourMode ?? SettingsService.NoteSettings.ColourMode
        );

        notePreview.UpdateBrushes(palette);
    }

    private void OnNewNoteCommand()
    {
        SwitchMode(trashMode: false);
        MessengerService.Publish(new OpenNoteWindowMessage(isManagementWindowParent: true));
    }

    private void OnOpenSettingsCommand()
    {
        MessengerService.Publish(new OpenSettingsWindowMessage());
    }

    private void OnOpenNotesCommand()
    {
        if (IsTrashMode)
        {
            OnRestoreNotesCommand();
            return;
        }

        List<int> selectedNoteIds = [.._selectedNotePreviews.Keys]; // Selected
        if (selectedNoteIds.Count == 0)
            selectedNoteIds = [.._notePreviewIdIndexMap.Keys]; // All

        MessengerService.Publish(new MultipleNoteWindowActionMessage(selectedNoteIds, NoteWindowAction.Open));
    }

    private void OnCloseNotesCommand()
    {
        if (IsTrashMode)
            return;

        List<int> selectedNoteIds = [.._selectedNotePreviews.Keys]; // Selected
        if (selectedNoteIds.Count == 0)
            selectedNoteIds = [.._notePreviewIdIndexMap.Keys]; // All

        MessengerService.Publish(new MultipleNoteWindowActionMessage(selectedNoteIds, NoteWindowAction.Close));
    }

    private void OnDeleteNotesCommand()
    {
        if (IsTrashMode)
            ErrorReporter.FireAndForget(DeleteSelectedPermanently(), "Management delete permanently");
        else
            ErrorReporter.FireAndForget(MoveSelectedToTrash(), "Management move to trash");
    }

    private async Task MoveSelectedToTrash()
    {
        List<int> selectedNoteIds = [.._selectedNotePreviews.Keys];
        if (selectedNoteIds.Count == 0)
            return; // 전체 삭제는 없다

        // 열린 창은 먼저 닫는다 (닫으면서 마지막 내용을 저장한다).
        MessengerService.Publish(new MultipleNoteWindowActionMessage(selectedNoteIds, NoteWindowAction.Close));

        DateTime now = DateTime.UtcNow;
        foreach (int noteId in selectedNoteIds)
        {
            if (!_notePreviewIdIndexMap.ContainsKey(noteId))
                continue; // 빈 메모는 위에서 닫을 때 이미 지워졌다

            RemoveNotePreview(noteId);
            await _noteRepository.MoveToTrash(noteId, now);
        }

        await RefreshTrashCount();
    }

    private async Task DeleteSelectedPermanently()
    {
        List<int> selectedNoteIds = [.._selectedNotePreviews.Keys];
        if (selectedNoteIds.Count == 0)
            return;

        if (!Confirm(string.Format(Strings.Management_DeletePermanentlyConfirm_Message, selectedNoteIds.Count)))
            return;

        foreach (int noteId in selectedNoteIds)
        {
            RemoveNotePreview(noteId);
            await _noteRepository.Delete(noteId);
        }

        await RefreshTrashCount();
    }

    private void OnRestoreNotesCommand()
        => ErrorReporter.FireAndForget(RestoreSelected(), "Management restore");

    private async Task RestoreSelected()
    {
        if (!IsTrashMode)
            return;

        List<int> selectedNoteIds = [.._selectedNotePreviews.Keys];
        foreach (int noteId in selectedNoteIds)
        {
            RemoveNotePreview(noteId);
            await _noteRepository.Restore(noteId);
        }

        await RefreshTrashCount();
    }

    private void OnEmptyTrashCommand()
        => ErrorReporter.FireAndForget(EmptyTrash(), "Management empty trash");

    private async Task EmptyTrash()
    {
        int count = await _noteRepository.CountDeleted();
        if (count == 0)
            return;

        if (!Confirm(string.Format(Strings.Management_EmptyTrashConfirm_Message, count)))
            return;

        await _noteRepository.EmptyTrash();

        if (IsTrashMode)
            ClearNotePreviews();

        await RefreshTrashCount();
        RaiseSelectionCommandsChanged();
    }

    /// <summary>되돌릴 수 없는 작업만 묻는다. 기본 선택은 '아니요'.</summary>
    private static bool Confirm(string message)
        => MessageBox.Show(
            message,
            Strings.Management_DeletePermanentlyConfirm_Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No
        ) == MessageBoxResult.Yes;

    private void RaiseSelectionCommandsChanged()
    {
        DeleteNotesCommand.RaiseCanExecuteChanged();
        RestoreNotesCommand.RaiseCanExecuteChanged();
    }

    private void NotePreview_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NotePreviewModel notePreview)
            return;

        switch (e.PropertyName)
        {
            case nameof(NotePreviewModel.IsSelected):

                if (notePreview.IsSelected && !_selectedNotePreviews.ContainsKey(notePreview.Id))
                    _selectedNotePreviews[notePreview.Id] = notePreview;
                else if (!notePreview.IsSelected && _selectedNotePreviews.ContainsKey(notePreview.Id))
                    _selectedNotePreviews.Remove(notePreview.Id);

                RaiseSelectionCommandsChanged();

                break;
        }
    }

    private void ClearNotePreviews()
    {
        foreach (NotePreviewModel notePreview in NotePreviews)
            notePreview.PropertyChanged -= NotePreview_PropertyChanged;

        NotePreviews.Clear();
        _notePreviewIdIndexMap.Clear();
        _selectedNotePreviews.Clear();
    }

    private void AddNotePreview(NoteDto dto, ColourMode? colourMode = null)
    {
        NotePreviewModel notePreview = new(dto);

        UpdateNotePreviewBrushes(notePreview, colourMode);

        notePreview.PropertyChanged += NotePreview_PropertyChanged;

        NotePreviews.Add(notePreview);
        _notePreviewIdIndexMap[dto.Id] = NotePreviews.Count - 1;
        if (notePreview.IsSelected)
            _selectedNotePreviews[notePreview.Id] = notePreview;
    }

    private void RemoveNotePreview(int noteId)
    {
        if (!_notePreviewIdIndexMap.TryGetValue(noteId, out int index))
            return;

        NotePreviewModel notePreview = NotePreviews[index];

        notePreview.PropertyChanged -= NotePreview_PropertyChanged;

        NotePreviews.Remove(notePreview);
        _selectedNotePreviews.Remove(notePreview.Id);
        RaiseSelectionCommandsChanged();

        // Rebuild map not indexes will have changed
        _notePreviewIdIndexMap.Clear();
        for (int i = 0; i < NotePreviews.Count; i++)
            _notePreviewIdIndexMap[NotePreviews[i].Id] = i;
    }
}
