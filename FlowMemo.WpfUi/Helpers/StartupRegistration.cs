using System.IO;
using System.Security;
using System.Security.Principal;

using Microsoft.Win32;

namespace FlowMemo.WpfUi.Helpers;

/// <summary>
/// Windows에 로그온하면 FlowMemo를 실행한다. 켜져 있는지는 작업·레지스트리가 기준이라 DB에 따로 저장하지 않는다.
/// <para>
/// 작업 스케줄러의 '로그온할 때' 작업으로 등록한다. 레지스트리 Run 키로 등록한 앱은 Windows가 바탕 화면을 다 띄우고
/// 시스템이 한가해질 때까지 기다렸다가 실행해서, 메모가 한참 뒤에야 떴다. 작업을 만들 수 없는 환경(정책 등)에서만
/// 예전처럼 Run 키에 쓴다. 둘 다 사용자 단위라 관리자 권한이 필요 없다.
/// </para>
/// </summary>
/// <param name="runValueName">Run 키의 값 이름 (예전 버전이 쓰던 이름, 대신 쓸 때도 같은 이름).</param>
/// <param name="taskName">작업 스케줄러 작업 이름. 작업 이름은 PC 전체에서 하나이므로 사용자 이름을 붙인다.</param>
public sealed class StartupRegistration(
    string runValueName,
    string taskName,
    string executablePath,
    IStartupTaskClient tasks,
    string runKeyPath = StartupRegistration.RunKeyPath,
    bool enableTask = true)
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>자동 실행으로 시작했다는 표시 (시작 기록에 쓴다).</summary>
    public const string AutostartArgument = "--autostart";

    /// <summary>Debug 빌드는 이름을 달리해서 테스트가 평소 쓰는 등록을 건드리지 않게 한다.</summary>
    public static StartupRegistration Current { get; } = new(
        App.IsDebugMode ? "FlowMemo-Debug" : "FlowMemo",
        TaskNameFor(App.IsDebugMode ? "FlowMemo-Debug" : "FlowMemo", Environment.UserName),
        Environment.ProcessPath ?? "",
        new SchtasksClient()
    );

    /// <summary>설치 프로그램(제거할 때 작업 삭제)도 같은 규칙을 쓴다: "FlowMemo (사용자 이름)".</summary>
    public static string TaskNameFor(string baseName, string userName)
        => $"{baseName} ({userName})";

    /// <summary>마지막으로 작업을 만들지 못한 이유 (Run 키로 대신 등록했을 때).</summary>
    public string? LastTaskError { get; private set; }

    private string RunCommand => $"\"{executablePath}\" {AutostartArgument}";

    public bool IsEnabled => tasks.Exists(taskName) || ReadRunValue() is not null;

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (TryRegisterTask())
                DeleteRunValue();
            else
                WriteRunValue();
        }
        else
        {
            tasks.Delete(taskName);
            DeleteRunValue();
        }
    }

    /// <summary>
    /// 앱을 시작할 때 부른다. 켜져 있으면 지금 실행 파일 위치로 다시 등록하고(설치 위치를 옮긴 경우),
    /// 예전 버전이 Run 키에 등록해 두었다면 작업으로 옮긴다. 꺼져 있으면 아무것도 하지 않는다.
    /// </summary>
    public void Refresh()
    {
        bool hasRunValue = ReadRunValue() is not null;
        if (!hasRunValue && !tasks.Exists(taskName))
            return;

        if (TryRegisterTask())
            DeleteRunValue();
        else if (hasRunValue)
            WriteRunValue();
    }

    private bool TryRegisterTask()
    {
        string? sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null)
        {
            LastTaskError = "현재 사용자 SID를 알 수 없음";
            return false;
        }

        if (tasks.Register(taskName, BuildTaskXml(sid, executablePath, enableTask), out string error))
        {
            LastTaskError = null;
            return true;
        }

        LastTaskError = error;
        return false;
    }

    /// <summary>
    /// 작업 스케줄러 작업 정의. 기본값 그대로 두면 안 되는 것들을 모두 적는다:
    /// 배터리일 때 실행 안 함(노트북), 72시간 뒤 강제 종료, 낮은 우선순위(7).
    /// </summary>
    public static string BuildTaskXml(string userSid, string executablePath, bool enabled = true)
    {
        string user = SecurityElement.Escape(userSid);
        string command = SecurityElement.Escape(executablePath);
        string workingDirectory = SecurityElement.Escape(Path.GetDirectoryName(executablePath) ?? "");

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>FlowMemo: Windows에 로그온하면 메모를 띄웁니다. FlowMemo 설정에서 끌 수 있습니다.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <Enabled>{(enabled ? "true" : "false")}</Enabled>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{command}</Command>
                  <Arguments>{AutostartArgument}</Arguments>
                  <WorkingDirectory>{workingDirectory}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private string? ReadRunValue()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKeyPath);
        return key?.GetValue(runValueName) as string;
    }

    private void WriteRunValue()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(runKeyPath);
        key.SetValue(runValueName, RunCommand);
    }

    private void DeleteRunValue()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
        key?.DeleteValue(runValueName, throwOnMissingValue: false);
    }
}
