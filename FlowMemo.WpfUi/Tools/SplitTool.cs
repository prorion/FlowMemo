using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Tools;

public class SplitTool : BaseTool, ITool
{
    private enum ToolActions
    {
        SplitComma,
        SplitSpace,
        SplitTab,
        SplitSelected
    }

    private string? _selectedText = null;

    public SplitTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            Strings.Tool_Split,
            [
                new ToolMenuAction(Strings.Tool_Split_Comma, new RelayCommand(() => MenuAction(ToolActions.SplitComma))),
                new ToolMenuAction(Strings.Tool_Split_Space, new RelayCommand(() => MenuAction(ToolActions.SplitSpace))),
                new ToolMenuAction(Strings.Tool_Split_Tab, new RelayCommand(() => MenuAction(ToolActions.SplitTab))),
                new ToolMenuAction("-"),
                new ToolMenuAction(Strings.Tool_Split_Selected, new RelayCommand(() => MenuAction(ToolActions.SplitSelected)))
            ]
        );
    }

    public ToolState State => ToolSettings.SplitToolState;

    private void MenuAction(ToolActions action)
    {
        if (action != ToolActions.SplitSelected)
        {
            _selectedText = null;
        }
        else
        {
            _selectedText = NoteTextBox.SelectedText;
            NoteTextBox.SelectionLength = 0;
        }

        ApplyFunctionToNoteText(ModifyTextCallback, action);
    }

    private string ModifyTextCallback(string text, Enum action)
    {
        switch (action)
        {
            case ToolActions.SplitComma:
                return text.Replace(",", Environment.NewLine);
            case ToolActions.SplitSpace:
                return text.Replace(" ", Environment.NewLine);
            case ToolActions.SplitTab:
                return text.Replace("\t", Environment.NewLine);
            case ToolActions.SplitSelected:
                if (!string.IsNullOrEmpty(_selectedText))
                    return text.Replace(_selectedText, Environment.NewLine);
                break;
        }

        return text;
    }
}
