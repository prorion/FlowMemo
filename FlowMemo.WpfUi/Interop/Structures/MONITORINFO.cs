using System.Runtime.InteropServices;

namespace FlowMemo.WpfUi.Interop.Structures;

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}
