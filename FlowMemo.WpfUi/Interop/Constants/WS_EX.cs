namespace FlowMemo.WpfUi.Interop.Constants;

internal static class WS_EX
{
    public const int TOPMOST = 0x00000008;
    public const int TOOLWINDOW = 0x00000080;
}

internal static class SWP
{
    public const uint NOSIZE = 0x0001;
    public const uint NOMOVE = 0x0002;
    public const uint NOACTIVATE = 0x0010;
    public const uint NOOWNERZORDER = 0x0200;
}

internal static class HWND
{
    public static readonly nint TOPMOST = -1;
    public static readonly nint NOTOPMOST = -2;
}
