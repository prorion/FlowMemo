using FlowMemo.Core.Enums;

namespace FlowMemo.WpfUi.Models;

/// <summary>
/// 도구 메뉴 설정. 속성 이름이 곧 DB 키("Tools.속성이름")이므로 이름을 바꾸면 저장된 값을 잃는다.
/// 초기값이 기본값이다 (DB에 키가 없을 때 쓴다).
/// </summary>
public class ToolSettingsModel : BaseModel
{
    public ToolState Base64ToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState BracketToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState CaseToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState ColourToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState DateTimeToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState GibberishToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState GuidToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState HashToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState HtmlEntityToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState IndentToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState JoinToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState JsonToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState ListToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState QuoteToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState RemoveToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState SlashToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState SortToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState SplitToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState TrimToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
    public ToolState UrlToolState { get; set => SetProperty(ref field, value); } = ToolState.Enabled;
}
