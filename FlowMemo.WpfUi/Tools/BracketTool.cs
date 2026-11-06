using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Tools;

public class BracketTool : BaseTool, ITool
{
    private enum ToolActions
    {
        BracketParentheses,
        BracketCurly,
        BracketSquare,
        BracketTrimOnce,
        BracketTrimAll,
    }

    private readonly char[] _openingBrackets = ['(', '{', '['];
    private readonly char[] _closingBrackets = [')', '}', ']'];

    public BracketTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            Strings.Tool_Bracket,
            [
                new ToolMenuAction(Strings.Tool_Bracket_Parentheses, new RelayCommand(() => MenuAction(ToolActions.BracketParentheses))),
                new ToolMenuAction(Strings.Tool_Bracket_Curly, new RelayCommand(() => MenuAction(ToolActions.BracketCurly))),
                new ToolMenuAction(Strings.Tool_Bracket_Square, new RelayCommand(() => MenuAction(ToolActions.BracketSquare))),
                new ToolMenuAction("-"),
                new ToolMenuAction(Strings.Tool_Bracket_TrimOnce, new RelayCommand(() => MenuAction(ToolActions.BracketTrimOnce))),
                new ToolMenuAction(Strings.Tool_Bracket_TrimAll, new RelayCommand(() => MenuAction(ToolActions.BracketTrimAll)))
            ]
        );
    }

    public ToolState State => ToolSettings.BracketToolState;

    private void MenuAction(ToolActions action)
    {
        ApplyFunctionToEachLine(ModifyLineCallback, action);
    }

    private string? ModifyLineCallback(string line, int index, Enum action)
    {
        switch (action)
        {
            case ToolActions.BracketParentheses:
                return $"({line})";
            case ToolActions.BracketCurly:
                return $"{{{line}}}";
            case ToolActions.BracketSquare:
                return $"[{line}]";
            case ToolActions.BracketTrimOnce:
            {
                if (line.Length > 0 && _openingBrackets.Contains(line[0]) && _closingBrackets.Contains(line[^1]))
                    return line[1..^1];
                else
                    return line;
            }
            case ToolActions.BracketTrimAll:
            {
                string newLine = line;
                while (newLine.Length > 0 && _openingBrackets.Contains(newLine[0]) && _closingBrackets.Contains(newLine[^1]))
                    newLine = newLine[1..^1];
                return newLine;
            }
        }

        return line;
    }
}
