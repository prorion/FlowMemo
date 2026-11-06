using System.Windows;

using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Services;
using FlowMemo.WpfUi.ViewModels;

namespace FlowMemo.WpfUi.Views;

public partial class SettingsWindow : Window
{
    private readonly MessengerService _messengerService;
    private readonly SettingsViewModel _viewModel;

    private bool _positioned;

    public SettingsWindow(MessengerService messengerService, SettingsViewModel viewModel)
    {
        _messengerService = messengerService;
        _messengerService.Subscribe<WindowActionMessage>(OnWindowActionMessage);

        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        Closed += Window_Closed;
    }

    /// <summary>
    /// 소유자(설정을 연 메모)를 지정하고 위치를 잡는다.
    /// 예전에는 창이 활성화될 때마다 화면 가운데로 되돌아갔다 (옮겨 놓아도 다시 클릭하면 튐).
    /// 이제는 처음 열 때와, 다른 메모에서 다시 열 때만 위치를 잡는다.
    /// </summary>
    public void SetOwnerAndPosition(Window? owner)
    {
        bool ownerChanged = !ReferenceEquals(Owner, owner);
        if (owner is not null && !owner.IsLoaded)
            owner = null;

        Owner = owner;

        if (_positioned && !ownerChanged)
            return;

        Rect workArea;
        Point position;
        if (owner is null)
        {
            workArea = ScreenHelper.GetPrimaryScreenBounds();
            position = new(
                workArea.Left + (workArea.Width - Width) / 2,
                workArea.Top + (workArea.Height - Height) / 2
            );
        }
        else
        {
            workArea = ScreenHelper.GetCurrentScreenBounds(ScreenHelper.GetWindowHandle(owner));
            position = new(
                (owner.Left + owner.Width / 2) - Width / 2,
                (owner.Top + owner.Height / 2) - Height / 2
            );
        }

        // 화면 밖으로 나가지 않게 작업 영역 안에 맞춘다.
        position.X = Math.Max(workArea.Left, Math.Min(position.X, workArea.Right - Width));
        position.Y = Math.Max(workArea.Top, Math.Min(position.Y, workArea.Bottom - Height));

        Left = position.X;
        Top = position.Y;
        _positioned = true;
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        _messengerService.Unsubscribe<WindowActionMessage>(OnWindowActionMessage);
        _viewModel.Detach();
    }

    private void OnWindowActionMessage(WindowActionMessage message)
    {
        if (message.Action == WindowAction.Activate)
        {
            WindowState = WindowState.Normal;
            Activate();
        }
    }
}
