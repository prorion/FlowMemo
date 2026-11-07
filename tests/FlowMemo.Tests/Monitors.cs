using System.Runtime.InteropServices;

internal static class Monitors
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    private delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMon, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);

    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();

    public static void Print()
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2
        Console.WriteLine($"system DPI (로그인 때 값) = {GetDpiForSystem()}");
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) =>
        {
            MONITORINFO mi = new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(h, ref mi);
            GetDpiForMonitor(h, 0, out uint dpi, out _);
            Console.WriteLine($"monitor primary={(mi.dwFlags & 1) == 1} rc=({mi.rcMonitor.L},{mi.rcMonitor.T})-({mi.rcMonitor.R},{mi.rcMonitor.B}) work=({mi.rcWork.L},{mi.rcWork.T})-({mi.rcWork.R},{mi.rcWork.B}) dpi={dpi} scale={dpi / 96.0:0.##}");
            return true;
        }, IntPtr.Zero);
    }
}
