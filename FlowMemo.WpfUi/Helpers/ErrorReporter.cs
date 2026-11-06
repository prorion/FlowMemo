using System.IO;
using System.Windows;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Helpers;

/// <summary>
/// 처리되지 않은 예외를 로그 파일에 남기고 사용자에게 알린다.
/// 예전에는 이런 예외가 앱을 바로 종료시키거나(도구 메뉴, 클립보드),
/// fire-and-forget 호출 안에서 조용히 사라졌다(노트 창이 안 열리던 문제).
/// </summary>
public static class ErrorReporter
{
    private const long MaxLogBytes = 1024 * 1024;

    private static readonly Lock _lock = new();
    private static string? _logFilePath;
    private static bool _isShowingDialog;

    public static string LogFilePath
        => _logFilePath ?? Path.Combine(AppContext.BaseDirectory, "logs", "error.log");

    public static void Initialize(string dataDirectory)
        => _logFilePath = Path.Combine(dataDirectory, "logs", "error.log");

    /// <summary>로그만 남긴다 (사용자에게 보여 줄 필요가 없는 오류).</summary>
    public static void Log(Exception exception, string context = "")
    {
        try
        {
            lock (_lock)
            {
                string path = LogFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                FileInfo info = new(path);
                if (info.Exists && info.Length > MaxLogBytes)
                    File.Move(path, path + ".old", overwrite: true);

                File.AppendAllText(
                    path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}"
                );
            }
        }
        catch
        {
            // 로그를 못 남겨도 앱은 계속 돌아가야 한다.
        }
    }

    /// <summary>로그를 남기고 오류 대화 상자를 보여 준다 (한 번에 하나만).</summary>
    public static void Report(Exception exception, string context = "")
    {
        Log(exception, context);

        Application? app = Application.Current;
        if (app is null)
            return;

        void Show()
        {
            if (_isShowingDialog)
                return;

            _isShowingDialog = true;
            try
            {
                MessageBox.Show(
                    string.Format(Strings.Error_Unexpected, exception.Message, LogFilePath),
                    Strings.Error_Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                _isShowingDialog = false;
            }
        }

        if (app.Dispatcher.CheckAccess())
            Show();
        else
            app.Dispatcher.BeginInvoke(Show);
    }

    /// <summary>
    /// 결과를 기다리지 않는 비동기 작업을 실행하고, 실패하면 보고한다.
    /// <c>_ = SomethingAsync();</c> 대신 쓴다.
    /// </summary>
    public static async void FireAndForget(Task task, string context = "")
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            Report(ex, context);
        }
    }
}
