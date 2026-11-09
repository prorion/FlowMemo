namespace FlowMemo.Core.Sync;

/// <param name="Text">합친 결과. 겹치는 부분은 표시 줄로 양쪽을 모두 넣는다.</param>
/// <param name="ConflictCount">자동으로 합치지 못한 곳의 수.</param>
public sealed record MergeResult(string Text, int ConflictCount);

/// <summary>
/// 3-way 합치기 (diff3). 마지막으로 맞춘 내용(기준)과 비교해서, 서로 다른 줄을 고쳤으면 자동으로 합친다.
/// 같은 줄을 서로 다르게 고쳤을 때만 표시 줄을 넣는다.
/// </summary>
public static class ThreeWayMerge
{
    public const string LocalMarker = "<<<<<<<";
    public const string SeparatorMarker = "=======";
    public const string RemoteMarker = ">>>>>>>";

    public static bool HasConflictMarkers(string text)
        => TextDiff.SplitLines(text).Any(line =>
            line.StartsWith(LocalMarker, StringComparison.Ordinal)
            || line == SeparatorMarker
            || line.StartsWith(RemoteMarker, StringComparison.Ordinal));

    public static MergeResult Merge(string? baseText, string? local, string? remote, string localLabel, string remoteLabel)
    {
        string[] o = TextDiff.SplitLines(baseText);
        string[] a = TextDiff.SplitLines(local);
        string[] b = TextDiff.SplitLines(remote);

        Dictionary<int, int> mapA = TextDiff.Matches(o, a).ToDictionary(m => m.A, m => m.B);
        Dictionary<int, int> mapB = TextDiff.Matches(o, b).ToDictionary(m => m.A, m => m.B);

        List<string> output = [];
        int conflicts = 0;
        int oStart = 0, aStart = 0, bStart = 0;

        // 기준의 줄 중 양쪽 모두에 그대로 남은 줄이 안정 지점이다. 그 사이 구간만 합치면 된다.
        for (int k = 0; k <= o.Length; k++)
        {
            bool atEnd = k == o.Length;
            if (!atEnd && !(mapA.ContainsKey(k) && mapB.ContainsKey(k)))
                continue;

            int aEnd = atEnd ? a.Length : mapA[k];
            int bEnd = atEnd ? b.Length : mapB[k];

            string[] chunkO = o[oStart..k];
            string[] chunkA = a[aStart..aEnd];
            string[] chunkB = b[bStart..bEnd];

            if (chunkA.SequenceEqual(chunkB) || chunkB.SequenceEqual(chunkO))
                output.AddRange(chunkA);
            else if (chunkA.SequenceEqual(chunkO))
                output.AddRange(chunkB);
            else
            {
                conflicts++;
                output.Add($"{LocalMarker} {localLabel}");
                output.AddRange(chunkA);
                output.Add(SeparatorMarker);
                output.AddRange(chunkB);
                output.Add($"{RemoteMarker} {remoteLabel}");
            }

            if (!atEnd)
                output.Add(o[k]);

            oStart = k + 1;
            aStart = aEnd + 1;
            bStart = bEnd + 1;
        }

        return new MergeResult(string.Join("\r\n", output), conflicts);
    }
}
