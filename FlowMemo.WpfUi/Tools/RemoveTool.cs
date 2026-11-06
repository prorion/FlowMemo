using System.Text;

using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Tools;

public class RemoveTool : BaseTool, ITool
{
    private enum ToolActions
    {
        RemoveSpaces,
        RemoveTabs,
        RemoveNewLines,
        RemoveForwardSlashes,
        RemoveBackSlashes,
        RemoveAllSlashes,
        RemoveSelected
    }

    private string? _selectedText = null;

    public RemoveTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            Strings.Tool_Remove,
            [
                new ToolMenuAction(Strings.Tool_Remove_Spaces, new RelayCommand(() => MenuAction(ToolActions.RemoveSpaces))),
                new ToolMenuAction(Strings.Tool_Remove_Tabs, new RelayCommand(() => MenuAction(ToolActions.RemoveTabs))),
                new ToolMenuAction(Strings.Tool_Remove_NewLines, new RelayCommand(() => MenuAction(ToolActions.RemoveNewLines))),
                new ToolMenuAction("-"),
                new ToolMenuAction(Strings.Tool_Remove_ForwardSlashes, new RelayCommand(() => MenuAction(ToolActions.RemoveForwardSlashes))),
                new ToolMenuAction(Strings.Tool_Remove_BackSlashes, new RelayCommand(() => MenuAction(ToolActions.RemoveBackSlashes))),
                new ToolMenuAction(Strings.Tool_Remove_AllSlashes, new RelayCommand(() => MenuAction(ToolActions.RemoveAllSlashes))),
                new ToolMenuAction("-"),
                new ToolMenuAction(Strings.Tool_Remove_Selected, new RelayCommand(() => MenuAction(ToolActions.RemoveSelected)))
            ]
        );
    }

    public ToolState State => ToolSettings.RemoveToolState;

    private void MenuAction(ToolActions action)
    {
        if (action != ToolActions.RemoveSelected)
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
            case ToolActions.RemoveSpaces:
                return RemoveCharacters(text, [' ']);
            case ToolActions.RemoveTabs:
                return RemoveCharacters(text, ['\t']);
            case ToolActions.RemoveNewLines:
                return RemoveCharacters(text, ['\r', '\n']);
            case ToolActions.RemoveForwardSlashes:
                return RemoveCharacters(text, ['/']);
            case ToolActions.RemoveBackSlashes:
                return RemoveCharacters(text, ['\\']);
            case ToolActions.RemoveAllSlashes:
                return RemoveCharacters(text, ['\\', '/']);
            case ToolActions.RemoveSelected:
                if (!string.IsNullOrEmpty(_selectedText))
                    return RemoveString(text, _selectedText);
                break;
        }

        return text;
    }

    private static string RemoveCharacters(string text, HashSet<char> character)
    {
        StringBuilder stringBuilder = new(text.Length);

        foreach (char currentChar in text)
        {
            if (!character.Contains(currentChar))
                stringBuilder.Append(currentChar);
        }

        return stringBuilder.ToString();
    }

    private static string RemoveString(string text, string toRemove)
    {
        return text.Replace(toRemove, string.Empty);
    }
}
