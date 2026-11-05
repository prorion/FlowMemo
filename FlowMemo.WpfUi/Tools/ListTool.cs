using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

namespace FlowMemo.WpfUi.Tools;

public class ListTool : BaseTool, ITool
{
    private enum ToolActions
    {
        ListEnumerate,
        ListDash,
        ListRemove
    }

    public ListTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            "List",
            [
                new ToolMenuAction("Enumerate", new RelayCommand(() => MenuAction(ToolActions.ListEnumerate))),
                new ToolMenuAction("Dash", new RelayCommand(() => MenuAction(ToolActions.ListDash))),
                new ToolMenuAction("Remove", new RelayCommand(() => MenuAction(ToolActions.ListRemove)))
            ]
        );
    }

    public ToolState State => ToolSettings.ListToolState;

    private void MenuAction(ToolActions action)
    {
        ApplyFunctionToEachLine(ModifyLineCallback, action);
    }

    private string? ModifyLineCallback(string line, int index, Enum action)
    {
        return action switch
        {
            ToolActions.ListEnumerate => $"{index + 1}. {line}",
            ToolActions.ListDash => $"- {line}",
            ToolActions.ListRemove => line[(line.IndexOf(' ') + 1)..],
            _ => line,
        };
    }
}
