using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;
using System.Globalization;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Tools;

public class DateTimeTool : BaseTool, ITool
{
    private enum ToolActions
    {
        DateTimeSortableDateTime,
        DateTimeWeekNumber
    }

    public DateTimeTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            Strings.Tool_DateTime,
            [
                new ToolMenuAction(Strings.Tool_DateTime_Sortable, new RelayCommand(() => MenuAction(ToolActions.DateTimeSortableDateTime))),
                new ToolMenuAction(Strings.Tool_DateTime_WeekNumber, new RelayCommand(() => MenuAction(ToolActions.DateTimeWeekNumber)))
            ]
        );
    }

    public ToolState State => ToolSettings.DateTimeToolState;

    private void MenuAction(ToolActions action)
    {
        switch (action)
        {
            case ToolActions.DateTimeSortableDateTime:
                InsertIntoNoteText(GetSortableDateTime());
                break;
            case ToolActions.DateTimeWeekNumber:
                InsertIntoNoteText(GetWeekNumber());
                break;
        }
    }

    private string GetSortableDateTime()
    {
        string selectedText = NoteTextBox.SelectedText;
        return GetDateTime(selectedText).ToString("s");
    }

    private string GetWeekNumber()
    {
        string selectedText = NoteTextBox.SelectedText;
        return ISOWeek.GetWeekOfYear(GetDateTime(selectedText)).ToString();
    }

    private static DateTime GetDateTime(string? dateString = null)
    {
        if (!string.IsNullOrEmpty(dateString) && DateTime.TryParse(dateString, out DateTime parsedDateTime))
            return parsedDateTime;

        // 예전에는 UtcNow라서 한국에서는 9시간 이른 시각이 들어갔다 (시간대 표시도 없이).
        return DateTime.Now;
    }
}
