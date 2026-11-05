using FlowMemo.Core.Enums;

namespace FlowMemo.WpfUi.Models;

public class ToolSettingsModel : BaseModel
{
    public ToolState Base64ToolState { get; set => SetProperty(ref field, value); }
    public ToolState BracketToolState { get; set => SetProperty(ref field, value); }
    public ToolState CaseToolState { get; set => SetProperty(ref field, value); }
    public ToolState ColourToolState { get; set => SetProperty(ref field, value); }
    public ToolState DateTimeToolState { get; set => SetProperty(ref field, value); }
    public ToolState GibberishToolState { get; set => SetProperty(ref field, value); }
    public ToolState GuidToolState { get; set => SetProperty(ref field, value); }
    public ToolState HashToolState { get; set => SetProperty(ref field, value); }
    public ToolState HtmlEntityToolState { get; set => SetProperty(ref field, value); }
    public ToolState IndentToolState { get; set => SetProperty(ref field, value); }
    public ToolState JoinToolState { get; set => SetProperty(ref field, value); }
    public ToolState JsonToolState { get; set => SetProperty(ref field, value); }
    public ToolState ListToolState { get; set => SetProperty(ref field, value); }
    public ToolState QuoteToolState { get; set => SetProperty(ref field, value); }
    public ToolState RemoveToolState { get; set => SetProperty(ref field, value); }
    public ToolState SlashToolState { get; set => SetProperty(ref field, value); }
    public ToolState SortToolState { get; set => SetProperty(ref field, value); }
    public ToolState SplitToolState { get; set => SetProperty(ref field, value); }
    public ToolState TrimToolState { get; set => SetProperty(ref field, value); }
    public ToolState UrlToolState { get; set => SetProperty(ref field, value); }
}
