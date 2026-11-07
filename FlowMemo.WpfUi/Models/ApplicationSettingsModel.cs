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
    public bool ShowNotifyIcon { get; set => SetProperty(ref field, value); } = true;
    public bool CheckForUpdates { get; set => SetProperty(ref field, value); } = false;
}
