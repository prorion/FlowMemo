using System.Windows.Input;

namespace FlowMemo.WpfUi.Tools;

public class ToolMenuAction(string name, ICommand? command = null, Enum? action = null)
{
    public string Name { get; set; } = name;
    public ICommand? Command { get; set; } = command;
    public Enum? Action { get; set; } = action;
}
