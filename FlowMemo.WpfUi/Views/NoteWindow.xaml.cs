using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using FlowMemo.Core.Enums;
using FlowMemo.Core.Sync;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Interop;
using FlowMemo.WpfUi.Interop.Constants;
using FlowMemo.WpfUi.Interop.Structures;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.Resources;
using FlowMemo.WpfUi.Services;
using FlowMemo.WpfUi.Themes;
using FlowMemo.WpfUi.ViewModels;

namespace FlowMemo.WpfUi.Views;

public partial class NoteWindow : Window
{
    private readonly NoteSettingsModel _noteSettings;
    private readonly MessengerService _messengerService;
    private readonly ThemeService _themeService;

    private readonly NoteViewModel _viewModel;

    // 고정(핀) 상태와 실제 창의 '항상 위' 상태가 어긋나면 바로잡는다.
    // 원본 Pinny Notes는 v1.15~v1.17에 걸쳐 "고정을 풀어도 계속 위에 남는" 문제를 고쳐 왔지만 완전히 해결하지 못했다.
    private readonly DispatcherTimer _topmostGuardTimer;

    private bool _restoringFromMinimize;

    private const int WmMoving = 0x0216;
    private const double SnapDistanceDips = 12;

    private HwndSource? _hwndSource;

    #region NoteWindow

    public NoteWindow(SettingsService settingsService, MessengerService messengerService, ThemeService themeService, NoteViewModel viewModel)
    {
        _noteSettings = settingsService.NoteSettings;
        _messengerService = messengerService;
        _messengerService.Subscribe<WindowActionMessage>(OnWindowActionMessage);
        _themeService = themeService;

        _viewModel = viewModel;
        _viewModel.Note.PropertyChanged += OnNotePropertyChanged;

        DataContext = _viewModel;

        InitializeComponent();

        _topmostGuardTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _topmostGuardTimer.Tick += (s, e) => SyncTopmostState();

        Activated += Window_Activated;
        Closing += Window_Closing;
        Closed += Window_Closed;
        Deactivated += Window_Deactivated;
        MouseDown += NoteWindow_MouseDown;
        MouseEnter += Window_MouseEnter;
        MouseLeave += Window_MouseLeave;
        Loaded += Window_Loaded;
        StateChanged += NoteWindow_StateChanged;

        TitleBarGrid.MouseDown += TitleBar_MouseDown;
        NewButton.Click += NewButton_Click;
        MoreButton.Click += MoreButton_Click;
        CloseButton.Click += CloseButton_Click;
        // '더 보기'로 열 때 정한 위치를 지워서, 우클릭으로 열면 다시 마우스 위치에 뜨게 한다.
        TitleBarContextMenu.Closed += (s, e) =>
        {
            TitleBarContextMenu.ClearValue(ContextMenu.PlacementTargetProperty);
            TitleBarContextMenu.ClearValue(ContextMenu.PlacementProperty);
        };

        // Windows 스티커 메모와 같은 단축키
        InputBindings.Add(new KeyBinding(new Commands.RelayCommand(OpenChildNote), Key.N, ModifierKeys.Control));

        PopulateTitleBarContextMenu();
        UpdatePinButtonState();
    }

    private readonly Dictionary<string, MenuItem> _colourMenuItems = [];

    /// <summary>메모를 앞으로 가져온다 (최소화돼 있으면 복원).</summary>
    public void ActivateNote()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        RaiseAboveOtherWindows();
        Activate();
    }

    /// <summary>
    /// 다른 프로그램 창 앞으로 한 번 올린다. Windows는 뒤쪽 프로그램의 Activate()를 거부할 수 있어서
    /// (그러면 메모가 활성 창 뒤에 남는다) '항상 위'를 잠깐 켰다가 바로 끈다. 고정한 메모는 그대로 둔다.
    /// </summary>
    private void RaiseAboveOtherWindows()
    {
        nint handle = ScreenHelper.GetWindowHandle(this);
        if (handle == 0)
            return;

        const uint flags = SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE;
        User32.SetWindowPos(handle, HWND.TOPMOST, 0, 0, 0, 0, flags);
        if (!_viewModel.Note.IsPinned)
            User32.SetWindowPos(handle, HWND.NOTOPMOST, 0, 0, 0, 0, flags);
    }

    /// <summary>바뀐 내용을 바로 저장한다 (앱 종료, Windows 종료 때).</summary>
    public void SaveNow()
        => _viewModel.SaveNote().GetAwaiter().GetResult();

    private void PopulateTitleBarContextMenu()
    {
        int insertIndex = TitleBarContextMenu.Items.IndexOf(ThemeMenuSeparator);
        foreach (ColourScheme colourScheme in _themeService.CurrentTheme.ColourSchemes.Values)
        {
            MenuItem menuItem = new()
            {
                Header = colourScheme.DisplayName,
                Command = _viewModel.ChangeThemeColourCommand,
                CommandParameter = colourScheme.Name, // DB에는 영어 키(Yellow 등)를 그대로 저장한다.
                // 예전에는 ColourScheme.Icon 하나를 모든 메모 창이 같이 써서, 메모가 여러 개면
                // 한 메모의 메뉴를 열 때 다른 메모 메뉴의 색 견본이 사라졌다 (화면 요소는 부모가 하나뿐).
                Icon = new System.Windows.Shapes.Rectangle
                {
                    Width = 16,
                    Height = 16,
                    Fill = new System.Windows.Media.SolidColorBrush(colourScheme.IconColour)
                }
            };

            TitleBarContextMenu.Items.Insert(insertIndex, menuItem);
            _colourMenuItems[colourScheme.Name] = menuItem;

            insertIndex++;
        }

        UpdateColourChecks();
    }

    /// <summary>
    /// 지금 메모 색에 선택 표시를 한다 (예전에는 현재 색을 알 수 없었다).
    /// Fluent 테마는 아이콘이 있는 항목에 IsChecked를 켜도 체크를 그리지 않아서, 오른쪽에 ✓를 붙이고 굵게 한다.
    /// </summary>
    private void UpdateColourChecks()
    {
        foreach ((string name, MenuItem item) in _colourMenuItems)
        {
            bool isCurrent = name == _viewModel.Note.ThemeColourScheme;
            item.InputGestureText = isCurrent ? "✓" : "";
            item.FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    /// <summary>고정 상태에 따라 고정 버튼의 툴팁과 진하기를 바꾼다 (예전에는 아이콘 각도만 달라 알아보기 어려웠다).</summary>
    private void UpdatePinButtonState()
    {
        bool pinned = _viewModel.Note.IsPinned;
        PinButton.ToolTip = pinned ? Strings.Note_Pinned_ToolTip : Strings.Note_Pin_ToolTip;
        if (pinned)
            PinButton.Opacity = 1.0;
        else
            PinButton.ClearValue(OpacityProperty);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel.OnWindowLoaded(
            ScreenHelper.GetWindowHandle(this)
        );

        EnsureOnScreen();

        SyncTopmostState();
        _topmostGuardTimer.Start();

        _hwndSource = HwndSource.FromHwnd(ScreenHelper.GetWindowHandle(this));
        _hwndSource?.AddHook(SnapWhileMoving);
    }

    /// <summary>
    /// 끌어서 옮기는 동안 Windows가 보내는 다음 위치를 화면 끝이나 다른 메모에 맞춘다.
    /// 다음 위치는 마우스 기준으로 다시 계산되므로, 붙인 뒤에도 더 끌면 자연스럽게 떨어진다.
    /// </summary>
    private nint SnapWhileMoving(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmMoving || !_noteSettings.SnapToEdges)
            return 0;

        RECT moving = Marshal.PtrToStructure<RECT>(lParam);
        (RECT workArea, double scale) = ScreenHelper.GetWorkAreaInPixels(moving);
        RECT snapped = WindowSnapper.Snap(moving, workArea, GetOtherNoteRects(), (int)Math.Round(SnapDistanceDips * scale));
        Marshal.StructureToPtr(snapped, lParam, false);

        handled = true;
        return 1;
    }

    private List<RECT> GetOtherNoteRects()
    {
        List<RECT> rects = [];
        foreach (NoteWindow note in Application.Current.Windows.OfType<NoteWindow>())
        {
            if (!ReferenceEquals(note, this) && note.IsVisible && note.WindowState == WindowState.Normal
                && User32.GetWindowRect(ScreenHelper.GetWindowHandle(note), out RECT rect))
                rects.Add(rect);
        }
        return rects;
    }

    /// <summary>
    /// 모니터를 떼어내거나 배치를 바꾸면 저장된 위치가 화면 밖일 수 있다.
    /// 그대로 두면 열려 있는데 보이지 않는 메모가 생기므로 주 모니터로 옮긴다.
    /// </summary>
    private void EnsureOnScreen()
    {
        nint handle = ScreenHelper.GetWindowHandle(this);
        if (ScreenHelper.IsTitleAreaOnAnyScreen(handle))
            return;

        const int screenMargin = 78;
        Rect workArea = ScreenHelper.GetPrimaryScreenBounds();
        Left = workArea.Left + screenMargin;
        Top = workArea.Top + screenMargin;

        _viewModel.OnWindowMoved(Left, Top);
    }

    private void NoteWindow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Check mouse button is pressed as a missed click of a button
        // can cause issues with DragMove().
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        DragMove();

        _viewModel.OnWindowMoved(Left, Top);
    }

    private void NoteWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized || _restoringFromMinimize)
            return;

        bool preventMinimize = _noteSettings.MinimizeMode == MinimizeMode.Prevent
            || (_noteSettings.MinimizeMode == MinimizeMode.PreventIfPinned && _viewModel.Note.IsPinned);
        if (!preventMinimize)
            return;

        // StateChanged 안에서 바로 Normal로 되돌리면 무시되는 경우가 있다
        // (고정하지 않은 메모가 '바탕 화면 보기'로 계속 최소화되던 문제).
        // 메시지 처리가 끝난 뒤 복원하고, 잠깐 맨 위로 올려 바탕 화면 뒤에 묻히지 않게 한다.
        _restoringFromMinimize = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            WindowState = WindowState.Normal;

            nint handle = ScreenHelper.GetWindowHandle(this);
            if (handle != 0)
                User32.SetWindowPos(handle, HWND.TOPMOST, 0, 0, 0, 0, SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE);

            DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(150) };
            settle.Tick += (s, args) =>
            {
                settle.Stop();
                _restoringFromMinimize = false;
                SyncTopmostState(); // 고정하지 않은 메모는 다시 '항상 위'를 끈다.
            };
            settle.Start();
        });
    }

    private void OnNotePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteModel.IsPinned))
        {
            UpdatePinButtonState();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, SyncTopmostState);
        }
        else if (e.PropertyName == nameof(NoteModel.ThemeColourScheme))
        {
            UpdateColourChecks();
        }
    }

    private void SyncTopmostState()
    {
        if (!IsLoaded || _restoringFromMinimize || WindowState == WindowState.Minimized)
            return;

        nint handle = ScreenHelper.GetWindowHandle(this);
        if (handle == 0 || !User32.IsWindow(handle))
            return;

        bool shouldBeTopmost = _viewModel.Note.IsPinned;
        bool isTopmost = (User32.GetWindowLongPtrW(handle, GWL.EXSTYLE) & WS_EX.TOPMOST) != 0;
        if (isTopmost == shouldBeTopmost)
            return;

        User32.SetWindowPos(
            handle,
            shouldBeTopmost ? HWND.TOPMOST : HWND.NOTOPMOST,
            0, 0, 0, 0,
            SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE
        );
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        ShowTitleBar();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!IsActive)
            HideTitleBar();
    }

    private void SyncAcceptButton_Click(object sender, RoutedEventArgs e)
        => ErrorReporter.FireAndForget(AcceptRemoteChange(), "Sync accept");

    private async Task AcceptRemoteChange()
    {
        // 안내가 뜬 뒤 이 메모를 고쳤다면 그냥 덮어쓰지 않고 비교 창을 연다.
        if (!await _viewModel.AcceptRemote())
            await CompareRemoteChange();
    }

    private void SyncCompareButton_Click(object sender, RoutedEventArgs e)
        => ErrorReporter.FireAndForget(CompareRemoteChange(), "Sync compare");

    private async Task CompareRemoteChange()
    {
        NoteSyncRow? row = await _viewModel.PrepareCompare();
        if (row is null)
            return;

        SyncDiffWindow window = new(row, _viewModel.Note.Content) { Owner = this };
        window.ShowDialog();

        await _viewModel.ApplyDecision(row, window.Decision, window.MergedText);
    }

    private void SyncSkipButton_Click(object sender, RoutedEventArgs e)
        => ErrorReporter.FireAndForget(_viewModel.SkipRemote(), "Sync skip");

    private void SyncKeepButton_Click(object sender, RoutedEventArgs e)
        => ErrorReporter.FireAndForget(_viewModel.KeepAfterRemoteDeletion(), "Sync keep");

    private void SyncTrashButton_Click(object sender, RoutedEventArgs e)
        => ErrorReporter.FireAndForget(MoveToTrashFromRemote(), "Sync trash");

    private async Task MoveToTrashFromRemote()
    {
        await _viewModel.AcceptRemoteDeletion();
        CloseByUser();
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        _viewModel.OnActivated();
        _viewModel.Note.IsFocused = true;
        _viewModel.UpdateOpacity();
        ShowTitleBar();
    }

    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_viewModel.Note.IsOpen)
            return;

        _viewModel.Note.IsFocused = false;
        _viewModel.UpdateOpacity();
        HideTitleBar();

        try
        {
            await _viewModel.SaveNote();
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "SaveNote (deactivated)");
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        // CloseNote는 창 닫기를 막지 않는다. (예전 코드의 e.Cancel = await ...는 await 뒤라 효과가 없었다.)
        try
        {
            await _viewModel.CloseNote();
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "CloseNote");
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        // 메신저와 설정 모델은 앱 전체에서 살아 있으므로, 구독을 풀지 않으면 닫힌 창이 메모리에 계속 남는다.
        _topmostGuardTimer.Stop();
        _hwndSource?.RemoveHook(SnapWhileMoving);
        _messengerService.Unsubscribe<WindowActionMessage>(OnWindowActionMessage);
        _viewModel.Note.PropertyChanged -= OnNotePropertyChanged;
        _viewModel.Detach();
    }

    private void OnWindowActionMessage(WindowActionMessage message)
    {
        if (message.Action == WindowAction.Activate)
            ActivateNote();
    }

    #endregion

    #region TitleBar

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            if (WindowState == WindowState.Normal)
                WindowState = WindowState.Maximized;
            else
                WindowState = WindowState.Normal;
        }
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
        => OpenChildNote();

    private void OpenChildNote()
    {
        _messengerService.Publish(
            new OpenNoteWindowMessage(ParentNote: _viewModel.Note)
        );
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        // 예전에는 저장·색·메모 관리·설정이 제목 표시줄 우클릭에만 있어서 찾기 어려웠다.
        TitleBarContextMenu.PlacementTarget = MoreButton;
        TitleBarContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        TitleBarContextMenu.IsOpen = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => CloseByUser();

    /// <summary>사용자가 메모를 닫는다. 앱을 끝낼 때와 달리 다음 시작 때 다시 열리지 않는다.</summary>
    public void CloseByUser()
    {
        _viewModel.Note.IsOpen = false;
        Close();
    }

    private void HideTitleBar()
    {
        if (_noteSettings.HideTitleBar)
            BeginStoryboard("HideTitleBarAnimation");
    }

    private void ShowTitleBar()
    {
        BeginStoryboard("ShowTitleBarAnimation");
    }

    private void BeginStoryboard(string resourceKey)
    {
        Storyboard hideTitleBar = (Storyboard)FindResource(resourceKey);
        hideTitleBar.Begin();
    }

    private void SaveMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog saveFileDialog = new()
        {
            Filter = Strings.Note_SaveDialog_Filter,
            FileName = SuggestFileName(NoteTextBox.Text),
            DefaultExt = ".txt",
            AddExtension = true
        };

        if (saveFileDialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllText(saveFileDialog.FileName, NoteTextBox.Text);
        }
        catch (Exception ex)
        {
            ErrorReporter.Log(ex, "Save note to file");
            MessageBox.Show(
                this,
                string.Format(Strings.Error_SaveFile, ex.Message),
                Strings.Error_Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }
    }

    /// <summary>메모 첫 줄로 파일 이름을 제안한다 (파일 이름에 못 쓰는 문자는 뺀다).</summary>
    private static string SuggestFileName(string text)
    {
        string firstLine = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        char[] invalid = Path.GetInvalidFileNameChars();
        string name = new([.. firstLine.Where(c => !invalid.Contains(c))]);
        if (name.Length > 50)
            name = name[..50].Trim();
        return name.Length > 0 ? name : "FlowMemo";
    }

    private void ResetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Width = _noteSettings.DefaultWidth;
        Height = _noteSettings.DefaultHeight;
    }

    private void ManagementMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _messengerService.Publish(new OpenManagementWindowMessage());
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _messengerService.Publish(new OpenSettingsWindowMessage(this));
    }

    #endregion
}
