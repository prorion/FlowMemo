using System.Diagnostics;
using System.IO;
using System.Text;

namespace FlowMemo.WpfUi.Helpers;

/// <summary>자동 실행 작업을 만들고 지우는 곳. 테스트에서는 가짜로 바꾼다.</summary>
public interface IStartupTaskClient
{
    bool Exists(string taskName);

    /// <summary>같은 이름이 있으면 덮어쓴다. 실패하면 false와 이유를 돌려준다.</summary>
    bool Register(string taskName, string taskXml, out string error);

    void Delete(string taskName);
}

/// <summary>
/// Windows에 들어 있는 schtasks.exe로 작업 스케줄러를 다룬다 (COM이나 추가 패키지 없이).
/// 작업 정의는 경로에 한글이 있어도 깨지지 않게 UTF-16 XML 파일로 넘긴다.
/// </summary>
public sealed class SchtasksClient : IStartupTaskClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public bool Exists(string taskName)
        => Run(["/Query", "/TN", taskName], out _) == 0;

    public bool Register(string taskName, string taskXml, out string error)
    {
        string xmlPath = Path.Combine(Path.GetTempPath(), $"FlowMemo-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, taskXml, Encoding.Unicode);
            int exitCode = Run(["/Create", "/TN", taskName, "/XML", xmlPath, "/F"], out string output);
            error = exitCode == 0 ? "" : $"schtasks 종료 코드 {exitCode}: {output}";
            return exitCode == 0;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch
            {
            }
        }
    }

    public void Delete(string taskName)
    {
        if (Exists(taskName))
            Run(["/Delete", "/TN", taskName, "/F"], out _);
    }

    private static int Run(string[] arguments, out string output)
    {
        ProcessStartInfo startInfo = new(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)!;
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        string standardOutput = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(Timeout))
        {
            try
            {
                process.Kill();
            }
            catch
            {
            }
            output = "시간 초과";
            return -1;
        }

        output = (standardError.GetAwaiter().GetResult() + standardOutput).Trim();
        return process.ExitCode;
    }
}
