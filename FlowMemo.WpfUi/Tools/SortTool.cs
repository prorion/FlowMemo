using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Tools;

public class SortTool : BaseTool, ITool
{
    private enum ToolActions
    {
        SortAscending,
        SortDescending
    }

    public SortTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            Strings.Tool_Sort,
            [
                new ToolMenuAction(Strings.Tool_Sort_Ascending, new RelayCommand(() => MenuAction(ToolActions.SortAscending))),
                new ToolMenuAction(Strings.Tool_Sort_Descending, new RelayCommand(() => MenuAction(ToolActions.SortDescending)))
            ]
        );
    }

    public ToolState State => ToolSettings.SortToolState;

    private void MenuAction(ToolActions action)
    {
        ApplyFunctionToNoteText(ModifyTextCallback, action);
    }

    private string ModifyTextCallback(string text, Enum action)
    {
        return action switch
        {
            ToolActions.SortAscending => SortNoteText(text),
            ToolActions.SortDescending => SortNoteText(text, true),
            _ => text,
        };
    }

    private static string SortNoteText(string text, bool reverse = false)
    {
        string[] lines = text.Split(Environment.NewLine);
        Array.Sort(lines);
        if (reverse)
            Array.Reverse(lines);
        return string.Join(Environment.NewLine, lines);
    }
}
