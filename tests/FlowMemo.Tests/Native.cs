using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public override string ToString() => $"({Left},{Top})-({Right},{Bottom})"; }

    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, ref RECT l);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT r, int flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;
    public const int SW_SHOWNOACTIVATE = 4, SW_SHOWMINNOACTIVE = 7;

    public static List<IntPtr> VisibleWindows(int pid)
    {
        List<IntPtr> result = [];
        EnumWindows((h, l) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p == pid && IsWindowVisible(h))
                result.Add(h);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static string Title(IntPtr h)
    {
        StringBuilder sb = new(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ClassName(IntPtr h)
    {
        StringBuilder sb = new(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsTopmost(IntPtr h) => (GetWindowLongPtr(h, -20).ToInt64() & 0x8) != 0;

    /// <summary>전역 단축키를 받는 창(메시지 전용)에 단축키가 눌린 것처럼 알린다. 키 입력은 보내지 않는다.</summary>
    public static void PostToggleNotesHotkey(int pid)
    {
        const uint WM_HOTKEY = 0x0312;
        IntPtr HWND_MESSAGE = new(-3);
        for (IntPtr h = FindWindowEx(HWND_MESSAGE, IntPtr.Zero, null, null); h != IntPtr.Zero; h = FindWindowEx(HWND_MESSAGE, h, null, null))
        {
            GetWindowThreadProcessId(h, out uint owner);
            if (owner == pid)
                PostMessage(h, WM_HOTKEY, new IntPtr(1), IntPtr.Zero);
        }
    }

    /// <summary>창 쌓임 순서에서 h가 other보다 위에 있는지.</summary>
    public static bool IsAbove(IntPtr h, IntPtr other)
    {
        const uint GW_HWNDPREV = 3;
        for (IntPtr current = GetWindow(other, GW_HWNDPREV); current != IntPtr.Zero; current = GetWindow(current, GW_HWNDPREV))
        {
            if (current == h)
                return true;
        }
        return false;
    }

    public static RECT Rect(IntPtr h) { GetWindowRect(h, out RECT r); return r; }

    public static bool OnScreen(IntPtr h)
    {
        RECT r = Rect(h);
        RECT title = new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Top + 30 };
        return MonitorFromRect(ref title, 0) != IntPtr.Zero;
    }

    /// <summary>창 하나만 PNG로 저장한다 (다른 앱이나 바탕 화면은 찍지 않는다).</summary>
    public static void Capture(IntPtr h, string path)
    {
        RECT r = Rect(h);
        int w = Math.Max(1, r.Right - r.Left), hgt = Math.Max(1, r.Bottom - r.Top);
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr dc = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, hgt);
        IntPtr old = SelectObject(dc, bmp);
        PrintWindow(h, dc, 2 /* PW_RENDERFULLCONTENT */);
        SelectObject(dc, old);
        BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        PngBitmapEncoder enc = new();
        enc.Frames.Add(BitmapFrame.Create(src));
        using (FileStream fs = File.Create(path))
            enc.Save(fs);
        DeleteObject(bmp);
        DeleteDC(dc);
        ReleaseDC(IntPtr.Zero, screen);
    }
}
