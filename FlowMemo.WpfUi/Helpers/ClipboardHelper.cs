using System.Runtime.InteropServices;
using System.Windows;

namespace FlowMemo.WpfUi.Helpers;

/// <summary>
/// 클립보드는 다른 프로그램(클립보드 관리자, 원격 데스크톱 등)이 잠깐 잡고 있으면
/// COMException(CLIPBRD_E_CANT_OPEN)을 던진다. 예전 코드는 이 예외로 앱이 종료됐다.
/// </summary>
public static class ClipboardHelper
{
    private const int Attempts = 5;
    private const int DelayMilliseconds = 40;

    public static bool TrySetText(string text)
    {
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                // copy: true -> FlowMemo를 종료해도 복사한 내용이 남는다.
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            {
                if (attempt == Attempts)
                {
                    ErrorReporter.Log(ex, "Clipboard.SetDataObject");
                    return false;
                }
                Thread.Sleep(DelayMilliseconds);
            }
        }
        return false;
    }

    public static bool ContainsText()
    {
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                return Clipboard.ContainsText();
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            {
                if (attempt == Attempts)
                    return false;
                Thread.Sleep(DelayMilliseconds);
            }
        }
        return false;
    }

    public static string? GetText()
    {
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            {
                if (attempt == Attempts)
                {
                    ErrorReporter.Log(ex, "Clipboard.GetText");
                    return null;
                }
                Thread.Sleep(DelayMilliseconds);
            }
        }
        return null;
    }
}
