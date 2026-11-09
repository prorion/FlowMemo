using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Interop;

using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Interop;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Models;

namespace FlowMemo.WpfUi.Services;

/// <summary>다른 앱을 쓰는 중에도 동작하는 단축키 (모든 메모 보기/숨기기).</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int ToggleNotesHotkeyId = 1;
    private const uint ModNoRepeat = 0x4000;
    private const int HwndMessage = -3; // 화면에 보이지 않고 메시지만 받는 창

    private readonly MessengerService _messengerService;
    private readonly ApplicationSettingsModel _applicationSettings;
    private readonly HwndSource _source;

    private bool _registered;
    private bool _disposed;

    public GlobalHotkeyService(MessengerService messengerService, SettingsService settingsService)
    {
        _messengerService = messengerService;
        _applicationSettings = settingsService.ApplicationSettings;

        _source = new HwndSource(new HwndSourceParameters("FlowMemo hotkeys")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
            Width = 0,
            Height = 0
        });
        _source.AddHook(WndProc);

        _applicationSettings.PropertyChanged += OnApplicationSettingsChanged;
        Register();
    }

    /// <summary>등록 결과가 바뀔 때 (설정 창의 안내문을 갱신한다).</summary>
    public event EventHandler? StatusChanged;

    /// <summary>다른 프로그램이 같은 단축키를 쓰고 있어 등록하지 못했는지.</summary>
    public bool HasConflict { get; private set; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _applicationSettings.PropertyChanged -= OnApplicationSettingsChanged;
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();

        _disposed = true;
    }

    private void OnApplicationSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationSettingsModel.ToggleNotesHotkey))
            Register();
    }

    private void Register()
    {
        Unregister();

        HasConflict = false;
        if (HotkeyGesture.TryParse(_applicationSettings.ToggleNotesHotkey, out HotkeyGesture gesture))
        {
            // ModifierKeys의 값은 RegisterHotKey의 MOD_ALT·MOD_CONTROL·MOD_SHIFT·MOD_WIN과 같다.
            _registered = User32.RegisterHotKey(
                _source.Handle,
                ToggleNotesHotkeyId,
                (uint)gesture.Modifiers | ModNoRepeat,
                (uint)KeyInterop.VirtualKeyFromKey(gesture.Key)
            );
            HasConflict = !_registered;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Unregister()
    {
        if (!_registered)
            return;

        User32.UnregisterHotKey(_source.Handle, ToggleNotesHotkeyId);
        _registered = false;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam == ToggleNotesHotkeyId)
        {
            _messengerService.Publish(new ApplicationActionMessage(ApplicationAction.ToggleNotesVisibility));
            handled = true;
        }

        return 0;
    }
}
