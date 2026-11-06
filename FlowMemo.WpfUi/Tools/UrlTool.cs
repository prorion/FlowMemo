using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using FlowMemo.WpfUi.Resources;

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
            Strings.Tool_Url,
            [
                new ToolMenuAction(Strings.Tool_Url_Encode, new RelayCommand(() => MenuAction(ToolActions.Encode))),
                new ToolMenuAction(Strings.Tool_Url_Decode, new RelayCommand(() => MenuAction(ToolActions.Decode)))
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
