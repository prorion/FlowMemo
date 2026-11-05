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

    private Window _lastOwner;

    public SettingsWindow(MessengerService messengerService, SettingsViewModel viewModel)
    {
        _messengerService = messengerService;
        _messengerService.Subscribe<WindowActionMessage>(OnWindowActionMessage);

        DataContext = viewModel;

        InitializeComponent();

        Activated += Window_Activated;

        _lastOwner = Owner;
    }

    private void Window_Activated(object? sender, System.EventArgs e)
    {
        if (Owner is null)
        {
            Left = SystemParameters.PrimaryScreenWidth / 2 - Width / 2;
            Top = SystemParameters.PrimaryScreenHeight / 2 - Height / 2;
        }
        else
        {
            if (Owner == _lastOwner)
                return;
            _lastOwner = Owner;

            Point position = new(
                (Owner.Left + Owner.Width / 2) - Width / 2,
                (Owner.Top + Owner.Height / 2) - Height / 2
            );
            Rect currentScreenBounds = ScreenHelper.GetCurrentScreenBounds(
                ScreenHelper.GetWindowHandle(Owner)
            );

            if (position.X < currentScreenBounds.Left)
                position.X = currentScreenBounds.Left;
            else if (position.X + Width > currentScreenBounds.Right)
                position.X = currentScreenBounds.Right - Width;

            if (position.Y < currentScreenBounds.Top)
                position.Y = currentScreenBounds.Top;
            else if (position.Y + Height > currentScreenBounds.Bottom)
                position.Y = currentScreenBounds.Bottom - Height;

            Left = position.X;
            Top = position.Y;
        }
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
