using FlowMemo.Core.Enums;

namespace FlowMemo.WpfUi.Models;

/// <summary>
/// 앱 설정. 속성 이름이 곧 DB 키("Application.속성이름")이므로 이름을 바꾸면 저장된 값을 잃는다.
/// 초기값이 기본값이다 (DB에 키가 없을 때 쓴다).
/// </summary>
public class ApplicationSettingsModel : BaseModel
{
    // General
    public StartupBehaviour StartupBehaviour { get; set => SetProperty(ref field, value); } = StartupBehaviour.RestoreOpenNotes;
    public NewInstanceBehaviour NewInstanceBehaviour { get; set => SetProperty(ref field, value); } = NewInstanceBehaviour.CreateNewNote;
    public bool ShowNotifyIcon
    {
        get;
        set
        {
            // 트레이 아이콘이 없으면 작업 표시줄까지 숨긴 앱을 열 방법이 없으므로 트레이 전용도 끈다.
            if (SetProperty(ref field, value) && !value)
                TrayOnly = false;
        }
    } = true;

    /// <summary>모든 창을 작업 표시줄에서 빼고 트레이 아이콘으로만 관리한다.</summary>
    public bool TrayOnly
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && value)
                ShowNotifyIcon = true;
        }
    } = true;

    public bool CheckForUpdates { get; set => SetProperty(ref field, value); } = false;

    /// <summary>모든 메모 보기/숨기기 전역 단축키 ("Ctrl+Alt+H" 형식, 빈 문자열이면 사용 안 함).</summary>
    public string ToggleNotesHotkey { get; set => SetProperty(ref field, value); } = "Ctrl+Alt+H";
}
