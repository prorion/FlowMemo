using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Tools;

public class TrimTool : BaseTool, ITool
{
    private enum ToolActions
    {
        TrimStart,
        TrimEnd,
        TrimBoth,
        TrimLines
    }

    public TrimTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            Strings.Tool_Trim,
            [
                new ToolMenuAction(Strings.Tool_Trim_Start, new RelayCommand(() => MenuAction(ToolActions.TrimStart))),
                new ToolMenuAction(Strings.Tool_Trim_End, new RelayCommand(() => MenuAction(ToolActions.TrimEnd))),
                new ToolMenuAction(Strings.Tool_Trim_Both, new RelayCommand(() => MenuAction(ToolActions.TrimBoth))),
                new ToolMenuAction(Strings.Tool_Trim_EmptyLines, new RelayCommand(() => MenuAction(ToolActions.TrimLines)))
            ]
        );
    }

    public ToolState State => ToolSettings.TrimToolState;

    private void MenuAction(ToolActions action)
    {
        ApplyFunctionToEachLine(ModifyLineCallback, action);
    }

    private string? ModifyLineCallback(string line, int index, Enum action)
    {
        switch (action)
        {
            case ToolActions.TrimStart:
                return line.TrimStart();
            case ToolActions.TrimEnd:
                return line.TrimEnd();
            case ToolActions.TrimBoth:
                return line.Trim();
            case ToolActions.TrimLines:
                if (string.IsNullOrEmpty(line))
                    return null;
                else
                    return line;
        }

        return line;
    }
}
