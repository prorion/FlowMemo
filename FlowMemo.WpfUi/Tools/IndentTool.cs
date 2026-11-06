using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Tools;

public class IndentTool : BaseTool, ITool
{
    private enum ToolActions
    {
        Indent2Spaces,
        Indent4Spaces,
        IndentTab
    }

    public IndentTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            Strings.Tool_Indent,
            [
                new ToolMenuAction(Strings.Tool_Indent_2Spaces, new RelayCommand(() => MenuAction(ToolActions.Indent2Spaces))),
                new ToolMenuAction(Strings.Tool_Indent_4Spaces, new RelayCommand(() => MenuAction(ToolActions.Indent4Spaces))),
                new ToolMenuAction(Strings.Tool_Indent_Tab, new RelayCommand(() => MenuAction(ToolActions.IndentTab)))
            ]
        );
    }

    public ToolState State => ToolSettings.IndentToolState;

    private void MenuAction(ToolActions action)
    {
        ApplyFunctionToEachLine(ModifyLineCallback, action);
    }

    private string? ModifyLineCallback(string line, int index, Enum action)
    {
        return action switch
        {
            ToolActions.Indent2Spaces => $"  {line}",
            ToolActions.Indent4Spaces => $"    {line}",
            ToolActions.IndentTab => $"\t{line}",
            _ => line,
        };
    }
}
