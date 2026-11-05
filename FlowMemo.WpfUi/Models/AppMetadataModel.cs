namespace FlowMemo.WpfUi.Models;

public class AppMetadataModel : BaseModel
{
    public long? LastUpdateCheck { get; set => SetProperty(ref field, value); }
    public string? ColourScheme { get => field; set => SetProperty(ref field, value); } = "";
}
