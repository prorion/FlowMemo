using Microsoft.Win32;

namespace FlowMemo.WpfUi.Helpers;

/// <summary>
/// Windows 시작 시 자동 실행. 사용자 레지스트리(HKCU)의 Run 키만 쓰므로 관리자 권한이 필요 없다.
/// 켜져 있는지는 레지스트리가 기준이라 DB에 따로 저장하지 않는다.
/// </summary>
public sealed class StartupRegistration(string valueName, string executablePath, string keyPath = StartupRegistration.RunKeyPath)
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Debug 빌드는 이름을 달리해서 테스트가 평소 쓰는 등록을 건드리지 않게 한다.</summary>
    public static StartupRegistration Current { get; } = new(
        App.IsDebugMode ? "FlowMemo-Debug" : "FlowMemo",
        Environment.ProcessPath ?? ""
    );

    private string Command => $"\"{executablePath}\"";

    public bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(valueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (enabled)
            key.SetValue(valueName, Command);
        else
            key.DeleteValue(valueName, throwOnMissingValue: false);
    }

    /// <summary>이미 등록돼 있는데 실행 파일을 옮겼다면 경로를 지금 위치로 고친다.</summary>
    public void RefreshPath()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        if (key?.GetValue(valueName) is string current && current != Command)
            key.SetValue(valueName, Command);
    }
}
