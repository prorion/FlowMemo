using System.Globalization;

using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

namespace FlowMemo.WpfUi.Tools;

public class CaseTool : BaseTool, ITool
{
    private enum ToolActions
    {
        CaseLower,
        CaseUpper,
        CaseTitle
    }

    public CaseTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            "Case",
            [
                new ToolMenuAction("Lower", new RelayCommand(() => MenuAction(ToolActions.CaseLower))),
                new ToolMenuAction("Upper", new RelayCommand(() => MenuAction(ToolActions.CaseUpper))),
                new ToolMenuAction("Title", new RelayCommand(() => MenuAction(ToolActions.CaseTitle)))
            ]
        );
    }

    public ToolState State => ToolSettings.CaseToolState;

    private void MenuAction(ToolActions action)
    {
        ApplyFunctionToNoteText(ModifyTextCallback, action);
    }

    private string ModifyTextCallback(string text, Enum action)
    {
        TextInfo textInfo = CultureInfo.CurrentCulture.TextInfo;

        return action switch
        {
            ToolActions.CaseLower => textInfo.ToLower(text),
            ToolActions.CaseUpper => textInfo.ToUpper(text),
            ToolActions.CaseTitle => textInfo.ToTitleCase(textInfo.ToLower(text)),
            _ => text,
        };
    }
}
