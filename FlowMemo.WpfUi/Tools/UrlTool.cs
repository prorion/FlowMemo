using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

namespace FlowMemo.WpfUi.Tools;

public class UrlTool : BaseTool, ITool
{
    private enum ToolActions
    {
        Encode,
        Decode
    }

    public UrlTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            "URL",
            [
                new ToolMenuAction("Encode", new RelayCommand(() => MenuAction(ToolActions.Encode))),
                new ToolMenuAction("Decode", new RelayCommand(() => MenuAction(ToolActions.Decode)))
            ]
        );
    }

    public ToolState State => ToolSettings.UrlToolState;

    private void MenuAction(ToolActions action)
    {
        ApplyFunctionToNoteText(ModifyTextCallback, action);
    }

    private string ModifyTextCallback(string text, Enum action)
    {
        return action switch
        {
            ToolActions.Encode => Uri.EscapeDataString(text),
            ToolActions.Decode => Uri.UnescapeDataString(text),
            _ => text,
        };
    }
}
