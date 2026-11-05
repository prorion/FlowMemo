using System.Runtime.InteropServices;

namespace FlowMemo.WpfUi.Interop.Structures;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
