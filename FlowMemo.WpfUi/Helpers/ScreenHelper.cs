using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

using FlowMemo.WpfUi.Interop;
using FlowMemo.WpfUi.Interop.Constants;
using FlowMemo.WpfUi.Interop.Structures;

namespace FlowMemo.WpfUi.Helpers;

/// <summary>
/// 모니터 영역을 WPF 좌표(DIP)로 돌려준다.
/// Win32는 실제 픽셀을, WPF의 Left/Top/Width/Height는 DIP를 쓰므로
/// 배율이 100%가 아니면 그대로 섞어 쓰면 안 된다 (예전 코드는 125%+에서 위치가 어긋났다).
/// </summary>
public static class ScreenHelper
{
    public static nint GetWindowHandle(Window window)
    {
        return new WindowInteropHelper(window).Handle;
    }

    /// <summary>창이 있는(또는 가장 가까운) 모니터의 작업 영역(작업 표시줄 제외), DIP 단위.</summary>
    public static Rect GetCurrentScreenBounds(nint windowHandle)
    {
        nint hMonitor = User32.MonitorFromWindow(windowHandle, MONITOR.DEFAULTTONEAREST);
        return GetWorkAreaInDips(hMonitor);
    }

    /// <summary>주 모니터의 작업 영역(작업 표시줄 제외), DIP 단위.</summary>
    public static Rect GetPrimaryScreenBounds()
    {
        POINT pt = new() { x = 0, y = 0 };
        nint hMonitor = User32.MonitorFromPoint(pt, MONITOR.DEFAULTTOPRIMARY);
        return GetWorkAreaInDips(hMonitor);
    }

    /// <summary>
    /// 창의 위쪽(제목 표시줄 부분)이 어느 모니터에든 걸쳐 있으면 true.
    /// 모니터를 떼어내거나 배치를 바꾸면 저장된 위치가 화면 밖이 될 수 있다.
    /// </summary>
    public static bool IsTitleAreaOnAnyScreen(nint windowHandle, int titleHeightPixels = 30)
    {
        if (windowHandle == 0 || !User32.GetWindowRect(windowHandle, out RECT rect))
            return true; // 알 수 없으면 건드리지 않는다.

        RECT titleArea = new()
        {
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = Math.Min(rect.Bottom, rect.Top + titleHeightPixels)
        };

        return User32.MonitorFromRect(ref titleArea, MONITOR.DEFAULTTONULL) != 0;
    }

    /// <summary>
    /// DIP 좌표의 사각형이 어느 모니터에든 걸치는지. 창을 만들기 전에 저장된 위치를 검사할 때 쓴다.
    /// (화면 밖 좌표로 창을 먼저 만들면 Windows가 엉뚱한 DPI를 주어, 화면 안으로 옮긴 뒤에도 크기가 틀어진다.)
    /// </summary>
    public static bool IsDipRectOnAnyScreen(Rect dipRect)
    {
        bool found = false;
        User32.EnumDisplayMonitors(0, 0, (nint hMonitor, nint hdc, ref RECT monitorRect, nint data) =>
        {
            double scale = GetMonitorScale(hMonitor);
            Rect monitorDips = new(
                monitorRect.Left / scale,
                monitorRect.Top / scale,
                (monitorRect.Right - monitorRect.Left) / scale,
                (monitorRect.Bottom - monitorRect.Top) / scale
            );
            if (monitorDips.IntersectsWith(dipRect))
            {
                found = true;
                return false; // 그만 찾는다.
            }
            return true;
        }, 0);
        return found;
    }

    private static Rect GetWorkAreaInDips(nint hMonitor)
    {
        MONITORINFO monitorInfo = new();
        monitorInfo.cbSize = Marshal.SizeOf(monitorInfo);

        if (hMonitor == 0 || !User32.GetMonitorInfoW(hMonitor, ref monitorInfo))
            return SystemParameters.WorkArea;

        double scale = GetMonitorScale(hMonitor);
        RECT work = monitorInfo.rcWork;

        return new Rect(
            work.Left / scale,
            work.Top / scale,
            (work.Right - work.Left) / scale,
            (work.Bottom - work.Top) / scale
        );
    }

    private static double GetMonitorScale(nint hMonitor)
    {
        try
        {
            if (Shcore.GetDpiForMonitor(hMonitor, Shcore.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96.0;
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return 1.0;
    }
}
