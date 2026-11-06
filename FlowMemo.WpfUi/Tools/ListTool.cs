using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using System.Text.RegularExpressions;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Tools;

public partial class ListTool : BaseTool, ITool
{
    private enum ToolActions
    {
        ListEnumerate,
        ListDash,
        ListRemove
    }

    public ListTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            Strings.Tool_List,
            [
                new ToolMenuAction(Strings.Tool_List_Enumerate, new RelayCommand(() => MenuAction(ToolActions.ListEnumerate))),
                new ToolMenuAction(Strings.Tool_List_Dash, new RelayCommand(() => MenuAction(ToolActions.ListDash))),
                new ToolMenuAction(Strings.Tool_List_Remove, new RelayCommand(() => MenuAction(ToolActions.ListRemove)))
            ]
        );
    }

    public ToolState State => ToolSettings.ListToolState;

    // "1. ", "2) ", "- ", "* ", "• " 같은 목록 기호.
    [GeneratedRegex(@"^(\s*)(?:\d+[.)]|[-*•·])\s+")]
    private static partial Regex ListMarkerRegex();

    private void MenuAction(ToolActions action)
    {
        ApplyFunctionToEachLine(ModifyLineCallback, action);
    }

    private string? ModifyLineCallback(string line, int index, Enum action)
    {
        return action switch
        {
            ToolActions.ListEnumerate => $"{index + 1}. {line}",
            ToolActions.ListDash => $"- {line}",
            // 예전에는 첫 공백까지 무조건 잘라서 "안녕 하세요" -> "하세요"가 됐다. 이제 목록 기호만 지운다 (들여쓰기는 유지).
            ToolActions.ListRemove => ListMarkerRegex().Replace(line, "$1", 1),
            _ => line,
        };
    }
}
