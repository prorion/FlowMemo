using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Controls;
using FlowMemo.WpfUi.Commands;

namespace FlowMemo.WpfUi.Tools;

public class JoinTool : BaseTool, ITool
{
    private enum ToolActions
    {
        JoinComma,
        JoinSpace,
        JoinTab
    }

    public JoinTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            "Join",
            [
                new ToolMenuAction("Comma", new RelayCommand(() => MenuAction(ToolActions.JoinComma))),
                new ToolMenuAction("Space", new RelayCommand(() => MenuAction(ToolActions.JoinSpace))),
                new ToolMenuAction("Tab", new RelayCommand(() => MenuAction(ToolActions.JoinTab)))
            ]
        );
    }

    public ToolState State => ToolSettings.JoinToolState;

    private void MenuAction(ToolActions action)
    {
        ApplyFunctionToNoteText(ModifyTextCallback, action);
    }

    private string ModifyTextCallback(string text, Enum action)
    {
        return action switch
        {
            ToolActions.JoinComma => text.Replace(Environment.NewLine, ","),
            ToolActions.JoinSpace => text.Replace(Environment.NewLine, " "),
            ToolActions.JoinTab => text.Replace(Environment.NewLine, "\t"),
            _ => text,
        };
    }
}
