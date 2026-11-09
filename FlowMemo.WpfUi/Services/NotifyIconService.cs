using H.NotifyIcon;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Services;

public class NotifyIconService : IDisposable
{
    private readonly MessengerService _messengerService;
    private readonly ApplicationSettingsModel _applicationSettings;
    private readonly WindowService _windowService;

    private TaskbarIcon? _notifyIcon;
    private MenuItem? _toggleNotesItem;

    private bool _disposed;

    public NotifyIconService(MessengerService messengerService, SettingsService settingsService, WindowService windowService)
    {
        _messengerService = messengerService;
        _windowService = windowService;
        _windowService.NotesVisibilityChanged += OnNotesVisibilityChanged;

        _applicationSettings = settingsService.ApplicationSettings;
        _applicationSettings.PropertyChanged += OnApplicationSettingsChanged;

        if (_applicationSettings.ShowNotifyIcon)
            InitializeNotifyIcon();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _applicationSettings.PropertyChanged -= OnApplicationSettingsChanged;
        _windowService.NotesVisibilityChanged -= OnNotesVisibilityChanged;

        DisposeNotifyIcon();

        _disposed = true;
    }

    private void OnApplicationSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationSettingsModel.ShowNotifyIcon))
        {
            if (_applicationSettings.ShowNotifyIcon && _notifyIcon is null)
                InitializeNotifyIcon();
            else if (!_applicationSettings.ShowNotifyIcon && _notifyIcon != null)
                DisposeNotifyIcon();
        }
    }

    private void InitializeNotifyIcon()
    {
        if (_notifyIcon != null)
            return;

        _notifyIcon = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Images/icon.ico")
            ),
            ToolTipText = Strings.App_Name,
            Visibility = Visibility.Visible
        };

        _notifyIcon.TrayLeftMouseDown += NotifyIcon_TrayMouseLeftButtonDown;
        _notifyIcon.TrayLeftMouseDoubleClick += NotifyIcon_TrayLeftMouseDoubleClick;

        MenuItem newNoteItem = new()
        {
            Header = Strings.Tray_NewNote
        };
        newNoteItem.Click += NewNote_Click;

        _toggleNotesItem = new();
        _toggleNotesItem.Click += ToggleNotes_Click;
        UpdateToggleNotesItem();

        MenuItem managementWindowItem = new()
        {
            Header = Strings.Tray_Management
        };
        managementWindowItem.Click += ManagementWindow_Click;

        MenuItem settingsItem = new()
        {
            Header = Strings.Tray_Settings
        };
        settingsItem.Click += Settings_Click;

        MenuItem exitItem = new()
        {
            Header = Strings.Tray_Exit
        };
        exitItem.Click += Exit_Click;

        ContextMenu contextMenu = new();
        contextMenu.Items.Add(newNoteItem);
        contextMenu.Items.Add(_toggleNotesItem);
        contextMenu.Items.Add(managementWindowItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenu = contextMenu;

        _notifyIcon.ForceCreate();
    }

    private void DisposeNotifyIcon()
    {
        if (_notifyIcon is null)
            return;

        _notifyIcon.TrayLeftMouseDown -= NotifyIcon_TrayMouseLeftButtonDown;
        _notifyIcon.TrayLeftMouseDoubleClick -= NotifyIcon_TrayLeftMouseDoubleClick;

        _notifyIcon.Dispose();
        _notifyIcon = null;
        _toggleNotesItem = null;
    }

    private void OnNotesVisibilityChanged(object? sender, EventArgs e)
        => UpdateToggleNotesItem();

    private void UpdateToggleNotesItem()
    {
        if (_toggleNotesItem is not null)
            _toggleNotesItem.Header = _windowService.AreNotesHidden ? Strings.Tray_ShowAllNotes : Strings.Tray_HideAllNotes;
    }

    private void ToggleNotes_Click(object? sender, EventArgs e)
    {
        _messengerService.Publish(new ApplicationActionMessage(ApplicationAction.ToggleNotesVisibility));
    }

    private void NotifyIcon_TrayMouseLeftButtonDown(object? sender, RoutedEventArgs e)
    {
        _messengerService.Publish(new WindowActionMessage(WindowAction.Activate));
    }

    private void NotifyIcon_TrayLeftMouseDoubleClick(object? sender, RoutedEventArgs e)
    {
        NewNote_Click(null, e);
    }

    private void NewNote_Click(object? sender, EventArgs e)
    {
        _messengerService.Publish(new OpenNoteWindowMessage());
    }

    private void ManagementWindow_Click(object? sender, EventArgs e)
    {
        _messengerService.Publish(new OpenManagementWindowMessage());
    }

    private void Settings_Click(object? sender, EventArgs e)
    {
        _messengerService.Publish(new OpenSettingsWindowMessage());
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        _messengerService.Publish(new ApplicationActionMessage(ApplicationAction.Close));
    }
}
