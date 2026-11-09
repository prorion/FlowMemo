namespace FlowMemo.Core.Sync;

public enum DiffKind
{
    Same,
    Modified,
    Added,
    Removed
}

/// <summary>나란히 보기의 한 줄. 왼쪽이나 오른쪽이 없으면 해당 값이 null이다.</summary>
public sealed record DiffRow(DiffKind Kind, int? LeftNumber, string? Left, int? RightNumber, string? Right);

/// <summary>줄 단위 비교. 메모는 크지 않으므로 단순한 LCS로 충분하다.</summary>
public static class TextDiff
{
    private const long MaxComparisons = 4_000_000; // 넘으면 메모리를 아끼려고 앞뒤 공통 부분만 맞춘다.

    public static string[] SplitLines(string? text)
        => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    public static IReadOnlyList<DiffRow> Compute(string? left, string? right)
    {
        string[] a = SplitLines(left);
        string[] b = SplitLines(right);
        List<DiffRow> rows = [];

        int i = 0, j = 0;
        foreach ((int matchA, int matchB) in Matches(a, b).Append((a.Length, b.Length)))
        {
            int changedA = matchA - i;
            int changedB = matchB - j;
            int paired = Math.Min(changedA, changedB);

            for (int k = 0; k < paired; k++)
                rows.Add(new(DiffKind.Modified, i + k + 1, a[i + k], j + k + 1, b[j + k]));
            for (int k = paired; k < changedA; k++)
                rows.Add(new(DiffKind.Removed, i + k + 1, a[i + k], null, null));
            for (int k = paired; k < changedB; k++)
                rows.Add(new(DiffKind.Added, null, null, j + k + 1, b[j + k]));

            if (matchA < a.Length)
                rows.Add(new(DiffKind.Same, matchA + 1, a[matchA], matchB + 1, b[matchB]));

            i = matchA + 1;
            j = matchB + 1;
        }

        return rows;
    }

    /// <summary>바뀐 줄에서 실제로 달라진 부분. 앞뒤 공통 글자 수를 돌려준다.</summary>
    public static (int Prefix, int Suffix) ChangedSpan(string left, string right)
    {
        int prefix = 0;
        int limit = Math.Min(left.Length, right.Length);
        while (prefix < limit && left[prefix] == right[prefix])
            prefix++;

        int suffix = 0;
        while (suffix < limit - prefix && left[left.Length - 1 - suffix] == right[right.Length - 1 - suffix])
            suffix++;

        return (prefix, suffix);
    }

    /// <summary>두 줄 목록에서 순서를 유지하며 일치하는 줄의 위치 (최장 공통 부분 수열).</summary>
    internal static List<(int A, int B)> Matches(string[] a, string[] b)
    {
        List<(int A, int B)> matches = [];

        int start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start])
        {
            matches.Add((start, start));
            start++;
        }

        int endA = a.Length, endB = b.Length;
        List<(int A, int B)> tail = [];
        while (endA > start && endB > start && a[endA - 1] == b[endB - 1])
        {
            endA--;
            endB--;
            tail.Add((endA, endB));
        }

        int n = endA - start, m = endB - start;
        if (n > 0 && m > 0 && (long)n * m <= MaxComparisons)
        {
            int[,] lengths = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lengths[i, j] = a[start + i] == b[start + j]
                        ? lengths[i + 1, j + 1] + 1
                        : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[start + x] == b[start + y])
                {
                    matches.Add((start + x, start + y));
                    x++;
                    y++;
                }
                else if (lengths[x + 1, y] >= lengths[x, y + 1])
                    x++;
                else
                    y++;
            }
        }

        tail.Reverse();
        matches.AddRange(tail);
        return matches;
    }
}
