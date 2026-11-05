using Microsoft.Win32;

namespace FlowMemo.WpfUi.Helpers;

public static class SystemThemeHelper
{
    public static bool IsDarkMode()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        if (key is null)
            return false;

        object? value = key.GetValue("AppsUseLightTheme");

        return value is int i && i == 0;
    }
}
