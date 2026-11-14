using System.Diagnostics;
using System.IO;

namespace FlowMemo.WpfUi.Helpers;

/// <summary>
/// 시작할 때마다 logs\startup.log에 한 줄을 남긴다 (최근 <see cref="MaxLines"/>줄).
/// '메모가 늦게 뜬다'가 Windows가 앱을 늦게 실행해서인지 앱이 느려서인지,
/// '메모가 다 안 뜬다'가 닫힌 메모라서인지 겹쳐서인지 구분하려고 둔다. 메모 내용은 남기지 않는다.
/// </summary>
public static class StartupLog
{
    public const string FileName = "startup.log";
    private const int MaxLines = 50;

    public static string FilePath
        => Path.Combine(Path.GetDirectoryName(ErrorReporter.LogFilePath)!, FileName);

    public static void Record(int openedNotes, int totalNotes, int movedOnScreen)
    {
        try
        {
            DateTime started = Process.GetCurrentProcess().StartTime;
            double toNotes = (DateTime.Now - started).TotalSeconds;
            bool autostart = Environment.GetCommandLineArgs().Contains(StartupRegistration.AutostartArgument);

            string how = autostart ? $"자동 실행 ({DescribeSinceLogon(started)})" : "직접 실행";
            Write(
                $"{started:yyyy-MM-dd HH:mm:ss}  {how}  실행 {toNotes:0.0}초 뒤 메모 표시  " +
                $"열린 메모 {openedNotes}/{totalNotes}개  화면 밖이라 옮긴 메모 {movedOnScreen}개"
            );
        }
        catch
        {
            // 기록을 못 남겨도 앱은 계속 돌아가야 한다.
        }
    }

    /// <summary>로그온한 때(이 세션의 Explorer가 시작한 때)부터 이 프로세스가 시작할 때까지.</summary>
    private static string DescribeSinceLogon(DateTime processStarted)
    {
        int sessionId = Process.GetCurrentProcess().SessionId;
        DateTime? logon = null;
        foreach (Process explorer in Process.GetProcessesByName("explorer"))
        {
            using (explorer)
            {
                try
                {
                    if (explorer.SessionId == sessionId && (logon is null || explorer.StartTime < logon))
                        logon = explorer.StartTime;
                }
                catch
                {
                }
            }
        }

        if (logon is null)
            return "바탕 화면보다 먼저 시작";

        double seconds = (processStarted - logon.Value).TotalSeconds;
        return seconds <= 0 ? "바탕 화면보다 먼저 시작" : $"로그온 {seconds:0.0}초 뒤 시작";
    }

    private static void Write(string line)
    {
        string path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        List<string> lines = File.Exists(path) ? [.. File.ReadAllLines(path)] : [];
        lines.Add(line);
        File.WriteAllLines(path, lines.Skip(Math.Max(0, lines.Count - MaxLines)));
    }
}
