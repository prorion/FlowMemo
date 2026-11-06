using System.Text;

using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Tools;

public class SlashTool : BaseTool, ITool
{
    private enum ToolActions
    {
        SlashAllForward,
        SlashAllBack,
        SlashSwap
    }

    public SlashTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            Strings.Tool_Slash,
            [
                new ToolMenuAction(Strings.Tool_Slash_AllForward, new RelayCommand(() => MenuAction(ToolActions.SlashAllForward))),
                new ToolMenuAction(Strings.Tool_Slash_AllBack, new RelayCommand(() => MenuAction(ToolActions.SlashAllBack))),
                new ToolMenuAction(Strings.Tool_Slash_Swap, new RelayCommand(() => MenuAction(ToolActions.SlashSwap)))
            ]
        );
    }

    public ToolState State => ToolSettings.SlashToolState;

    private void MenuAction(ToolActions action)
    {
        ApplyFunctionToNoteText(ModifyTextCallback, action);
    }

    private string ModifyTextCallback(string text, Enum action)
    {
        return action switch
        {
            ToolActions.SlashAllForward => text.Replace('\\', '/'),
            ToolActions.SlashAllBack => text.Replace('/', '\\'),
            ToolActions.SlashSwap => SwapCharacters(text, '\\', '/'),
            _ => text,
        };
    }

    private static string SwapCharacters(string text, char character1, char? character2 = null)
    {
        StringBuilder stringBuilder = new(text.Length);

        foreach (char currentChar in text)
        {
            if (currentChar == character1)
                stringBuilder.Append(character2);
            else if (currentChar == character2)
                stringBuilder.Append(character1);
            else
                stringBuilder.Append(currentChar);
        }

        return stringBuilder.ToString();
    }
}
