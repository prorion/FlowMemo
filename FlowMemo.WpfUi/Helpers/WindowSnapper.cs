using FlowMemo.WpfUi.Interop.Structures;

namespace FlowMemo.WpfUi.Helpers;

/// <summary>끌어서 옮기는 창을 화면 끝이나 다른 창 가장자리에 붙인다. 모두 실제 픽셀 단위다.</summary>
public static class WindowSnapper
{
    /// <summary>
    /// 가로·세로를 따로 계산해 distance 안에서 가장 가까운 가장자리로 옮긴 사각형을 돌려준다.
    /// 다른 창은 맞닿는 쪽(왼쪽↔오른쪽)과 나란한 쪽(왼쪽↔왼쪽)에 붙고, 반대 축으로 가까이 있을 때만 대상이 된다.
    /// </summary>
    public static RECT Snap(RECT moving, RECT workArea, IEnumerable<RECT> others, int distance)
    {
        int? deltaX = null;
        int? deltaY = null;

        void ConsiderX(int from, int to) => Consider(ref deltaX, from, to, distance);
        void ConsiderY(int from, int to) => Consider(ref deltaY, from, to, distance);

        ConsiderX(moving.Left, workArea.Left);
        ConsiderX(moving.Right, workArea.Right);
        ConsiderY(moving.Top, workArea.Top);
        ConsiderY(moving.Bottom, workArea.Bottom);

        foreach (RECT other in others)
        {
            if (moving.Top <= other.Bottom + distance && moving.Bottom >= other.Top - distance)
            {
                ConsiderX(moving.Right, other.Left);
                ConsiderX(moving.Left, other.Right);
                ConsiderX(moving.Left, other.Left);
                ConsiderX(moving.Right, other.Right);
            }

            if (moving.Left <= other.Right + distance && moving.Right >= other.Left - distance)
            {
                ConsiderY(moving.Bottom, other.Top);
                ConsiderY(moving.Top, other.Bottom);
                ConsiderY(moving.Top, other.Top);
                ConsiderY(moving.Bottom, other.Bottom);
            }
        }

        int dx = deltaX ?? 0;
        int dy = deltaY ?? 0;
        return new RECT
        {
            Left = moving.Left + dx,
            Top = moving.Top + dy,
            Right = moving.Right + dx,
            Bottom = moving.Bottom + dy
        };
    }

    private static void Consider(ref int? best, int from, int to, int distance)
    {
        int delta = to - from;
        if (Math.Abs(delta) <= distance && (best is null || Math.Abs(delta) < Math.Abs(best.Value)))
            best = delta;
    }
}
