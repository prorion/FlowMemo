using System.Net;

using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using FlowMemo.WpfUi.Resources;

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
            Strings.Tool_HtmlEntity,
            [
                new ToolMenuAction(Strings.Tool_HtmlEntity_Encode, new RelayCommand(() => MenuAction(ToolActions.EntityEncode))),
                new ToolMenuAction(Strings.Tool_HtmlEntity_Decode, new RelayCommand(() => MenuAction(ToolActions.EntityDecode)))
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
