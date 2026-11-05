using System.Net;

using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

namespace FlowMemo.WpfUi.Tools;

public class HtmlEntityTool : BaseTool, ITool
{
    private enum ToolActions
    {
        EntityEncode,
        EntityDecode
    }

    public HtmlEntityTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            "HTML Entity",
            [
                new ToolMenuAction("Encode", new RelayCommand(() => MenuAction(ToolActions.EntityEncode))),
                new ToolMenuAction("Decode", new RelayCommand(() => MenuAction(ToolActions.EntityDecode)))
            ]
        );
    }

    public ToolState State => ToolSettings.HtmlEntityToolState;

    private void MenuAction(ToolActions action)
    {
        ApplyFunctionToNoteText(ModifyTextCallback, action);
    }

    private string ModifyTextCallback(string text, Enum action)
    {
        return action switch
        {
            ToolActions.EntityEncode => WebUtility.HtmlEncode(text),
            ToolActions.EntityDecode => WebUtility.HtmlDecode(text),
            _ => text,
        };
    }
}
