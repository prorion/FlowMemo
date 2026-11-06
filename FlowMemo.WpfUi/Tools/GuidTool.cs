using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Tools;

public class GuidTool : BaseTool, ITool
{
    private enum ToolActions
    {
        GuidV4,
        GuidV7
    }

    public GuidTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            Strings.Tool_Guid,
            [
                new ToolMenuAction(Strings.Tool_Guid_V4, new RelayCommand(() => MenuAction(ToolActions.GuidV4))),
                new ToolMenuAction(Strings.Tool_Guid_V7, new RelayCommand(() => MenuAction(ToolActions.GuidV7)))
            ]
        );
    }

    public ToolState State => ToolSettings.GuidToolState;

    private void MenuAction(ToolActions action)
    {
        Guid newGuid;
        switch (action)
        {
            case ToolActions.GuidV4:
                newGuid = Guid.NewGuid();
                break;
            case ToolActions.GuidV7:
                newGuid = Guid.CreateVersion7();
                break;
            default:
                return;
        }

        InsertIntoNoteText(newGuid.ToString().ToUpper());
    }
}
