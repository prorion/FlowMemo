using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Microsoft.Extensions.DependencyInjection;

using FlowMemo.Core.Configurations;
using FlowMemo.Core.Enums;
using FlowMemo.Core.Sync;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Resources;
using FlowMemo.WpfUi.Services;
using FlowMemo.WpfUi.ViewModels;

namespace FlowMemo.WpfUi.Views;

public partial class SettingsWindow : Window
{
    private readonly MessengerService _messengerService;
    private readonly SettingsViewModel _viewModel;
    private readonly GlobalHotkeyService _globalHotkeyService;
    private readonly SyncService _syncService;

    private bool _positioned;

    public SettingsWindow(MessengerService messengerService, SettingsViewModel viewModel)
    {
        _messengerService = messengerService;
        _messengerService.Subscribe<WindowActionMessage>(OnWindowActionMessage);

        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        DataFolderPathText.Text = DataFolderPath;
        DataFolderPathText.ToolTip = DataFolderPath;

        _globalHotkeyService = App.Services.GetRequiredService<GlobalHotkeyService>();
        _globalHotkeyService.StatusChanged += OnHotkeyStatusChanged;
        ShowHotkeyRegistrationStatus();

        _syncService = App.Services.GetRequiredService<SyncService>();
        _syncService.StatusChanged += OnSyncStatusChanged;
        SyncDeviceNameBox.Text = Environment.MachineName;
        UpdateSyncPanels();
        ErrorReporter.FireAndForget(LoadSyncDevices(), "Sync devices");

        Closed += Window_Closed;
    }

    // ---------------------------------------------------------------- 동기화 탭

    private void OnSyncStatusChanged(object? sender, EventArgs e)
        => UpdateSyncPanels();

    private void UpdateSyncPanels()
    {
        bool connected = _syncService.IsConnected;
        SyncConnectGroup.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        SyncConnectedPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        SyncRecoveryGroup.Visibility = _syncService.HasRecoveryCode ? Visibility.Visible : Visibility.Collapsed;

        if (!connected)
            return;

        string status = string.Format(Strings.Settings_Sync_Connected, _syncService.DeviceName, _syncService.ServerUrl);
        if (_syncService.LastSyncAt is DateTime lastSync)
            status += Environment.NewLine + string.Format(Strings.Settings_Sync_LastSync, lastSync);
        if (_syncService.LastError is string error)
            status += Environment.NewLine + error;
        SyncStatusText.Text = status;
    }

    private async void SyncConnectButton_Click(object sender, RoutedEventArgs e)
    {
        string deviceName = SyncDeviceNameBox.Text.Trim();
        if (deviceName.Length == 0)
            deviceName = Environment.MachineName;

        SyncConnectButton.IsEnabled = false;
        SyncConnectStatus.Text = Strings.Settings_Sync_Connecting;
        try
        {
            await _syncService.Connect(SyncCodeBox.Text, deviceName);
            SyncCodeBox.Clear();
            SyncConnectStatus.Text = "";
            await LoadSyncDevices();
        }
        catch (Exception ex) when (ex is ArgumentException or SyncApiException or HttpRequestException or TaskCanceledException)
        {
            ErrorReporter.Log(ex, "Sync connect");
            SyncConnectStatus.Text = ex is ArgumentException
                ? ex.Message
                : string.Format(Strings.Settings_Sync_ConnectFailed, ex is SyncApiException { StatusCode: 403 } ? Strings.Settings_Sync_CodeRejected : ex.Message);
        }
        finally
        {
            SyncConnectButton.IsEnabled = true;
        }
    }

    private void SyncNowButton_Click(object sender, RoutedEventArgs e)
        => _syncService.RequestSync();

    private async void SyncCreatePairButton_Click(object sender, RoutedEventArgs e)
    {
        SyncCreatePairButton.IsEnabled = false;
        try
        {
            SyncPairCodeBox.Text = await _syncService.CreatePairingCode();
            SyncCopyPairButton.IsEnabled = true;
        }
        catch (Exception ex) when (ex is SyncApiException or HttpRequestException or TaskCanceledException)
        {
            ErrorReporter.Report(ex, "Sync pairing");
        }
        finally
        {
            SyncCreatePairButton.IsEnabled = true;
        }
    }

    private void SyncCopyPairButton_Click(object sender, RoutedEventArgs e)
        => ClipboardHelper.TrySetText(SyncPairCodeBox.Text);

    private void SyncShowRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        SyncRecoveryCodeBox.Text = _syncService.GetRecoveryCode() ?? "";
        SyncCopyRecoveryButton.IsEnabled = SyncRecoveryCodeBox.Text.Length > 0;
    }

    private void SyncCopyRecoveryButton_Click(object sender, RoutedEventArgs e)
        => ClipboardHelper.TrySetText(SyncRecoveryCodeBox.Text);

    private async void SyncDisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult answer = MessageBox.Show(
            this,
            Strings.Settings_Sync_DisconnectConfirm,
            Strings.Settings_Sync_Disconnect,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No
        );
        if (answer != MessageBoxResult.Yes)
            return;

        SyncPairCodeBox.Clear();
        SyncRecoveryCodeBox.Clear();
        await _syncService.Disconnect();
    }

    private async Task LoadSyncDevices()
    {
        SyncDevicesPanel.Children.Clear();
        if (!_syncService.IsConnected)
            return;

        try
        {
            SyncDeviceList list = await _syncService.GetDevices();
            foreach (SyncDevice device in list.Devices)
                SyncDevicesPanel.Children.Add(BuildSyncDeviceRow(device, device.Id == list.CurrentDeviceId));
        }
        catch (Exception ex) when (ex is SyncApiException or HttpRequestException or TaskCanceledException)
        {
            ErrorReporter.Log(ex, "Sync devices");
        }
    }

    private Border BuildSyncDeviceRow(SyncDevice device, bool isCurrent)
    {
        DockPanel row = new() { LastChildFill = true };

        if (!isCurrent)
        {
            Button revoke = new() { Content = Strings.Settings_Sync_Revoke, Padding = new Thickness(12, 4, 12, 4) };
            revoke.Click += (_, _) => ErrorReporter.FireAndForget(RevokeSyncDevice(device), "Sync revoke");
            DockPanel.SetDock(revoke, Dock.Right);
            row.Children.Add(revoke);
        }

        row.Children.Add(new TextBlock { Text = isCurrent ? $"{device.Name} {Strings.Settings_Sync_ThisDevice}" : device.Name });
        return new Border { Child = row };
    }

    private async Task RevokeSyncDevice(SyncDevice device)
    {
        MessageBoxResult answer = MessageBox.Show(
            this,
            string.Format(Strings.Settings_Sync_RevokeConfirm, device.Name),
            Strings.Settings_Sync_Revoke,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No
        );
        if (answer != MessageBoxResult.Yes)
            return;

        await _syncService.RevokeDevice(device.Id);
        await LoadSyncDevices();
    }

    private void ToggleNotesHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key
        };
        ModifierKeys modifiers = Keyboard.Modifiers;

        // 수정 키 없는 Tab·Esc는 평소처럼 포커스 이동과 창 닫기에 쓴다.
        if (modifiers == ModifierKeys.None && key is Key.Tab or Key.Escape)
            return;

        e.Handled = true;

        if (HotkeyGesture.IsModifierKey(key))
            return;

        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            _viewModel.ApplicationSettings.ToggleNotesHotkey = "";
            return;
        }

        HotkeyGesture gesture = new(modifiers, key);
        if (gesture.IsValid)
            _viewModel.ApplicationSettings.ToggleNotesHotkey = gesture.ToString();
        else
            ShowHotkeyStatus(Strings.Settings_Hotkey_NeedModifier);
    }

    private void ClearToggleNotesHotkey_Click(object sender, RoutedEventArgs e)
        => _viewModel.ApplicationSettings.ToggleNotesHotkey = "";

    private void OnHotkeyStatusChanged(object? sender, EventArgs e)
        => ShowHotkeyRegistrationStatus();

    private void ShowHotkeyRegistrationStatus()
        => ShowHotkeyStatus(_globalHotkeyService.HasConflict ? Strings.Settings_Hotkey_InUse : null);

    private void ShowHotkeyStatus(string? message)
    {
        ToggleNotesHotkeyStatus.Text = message ?? "";
        ToggleNotesHotkeyStatus.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
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

    // 끝의 \는 뗀다 ("C:\폴더\"처럼 따옴표 앞에 \가 오면 명령줄에서 따옴표로 읽힌다).
    private static string DataFolderPath
        => System.IO.Path.TrimEndingDirectorySeparator(App.Services.GetRequiredService<DatabaseConfiguration>().DataPath);

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataFolderPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Open data folder");
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _messengerService.Unsubscribe<WindowActionMessage>(OnWindowActionMessage);
        _globalHotkeyService.StatusChanged -= OnHotkeyStatusChanged;
        _syncService.StatusChanged -= OnSyncStatusChanged;
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
