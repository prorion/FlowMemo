using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

using FlowMemo.WpfUi.Controls;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.Resources;
using FlowMemo.WpfUi.Services;

namespace FlowMemo.WpfUi.Tools;

public abstract class BaseTool
{
    protected ToolSettingsModel ToolSettings;

    protected NoteTextBoxControl NoteTextBox;

    private MenuItem? _menuItem;
    private string _toolName = "";

    public BaseTool(NoteTextBoxControl noteTextBox)
    {
        NoteTextBox = noteTextBox;

        SettingsService settingsService = App.Services.GetRequiredService<SettingsService>();
        ToolSettings = settingsService.ToolSettings;
    }

    public MenuItem MenuItem => _menuItem
        ?? throw new Exception("Tools menu has not been initialised.");

    protected void InitializeMenuItem(string header, ToolMenuAction[] menuActions)
    {
        _toolName = header;
        _menuItem = new()
        {
            Header = header,
        };
        // 하위 메뉴 화살표(>)가 글자에 붙지 않게 폭을 넉넉히 주고, 다른 메뉴 항목과 정렬을 맞춘다.
        MenuHelper.AlignSubmenuHeader(_menuItem, minWidth: 180);

        foreach (ToolMenuAction menuAction in menuActions)
        {
            if (menuAction.Name == "-" && menuAction.Command is null && menuAction.Action is null)
            {
                _menuItem.Items.Add(new Separator());
                continue;
            }

            MenuItem actionMenuItem = new()
            {
                Header = menuAction.Name
            };
            if (menuAction.Command != null)
                actionMenuItem.Command = menuAction.Command;
            if (menuAction.Action != null)
                actionMenuItem.CommandParameter = menuAction.Action;

            _menuItem.Items.Add(actionMenuItem);
        }
    }

    protected void ApplyFunctionToNoteText(Func<string, Enum, string> function, Enum action)
    {
        if (NoteTextBox.HasSelectedText)
        {
            if (!TryRun(() => function(NoteTextBox.SelectedText, action), out string result))
                return;

            NoteTextBox.SelectedText = result;
        }
        else
        {
            string noteText = NoteTextBox.Text;
            // Ignore trailing new line if it was automatically added
            if (NoteTextBox.NewLineAtEnd && NoteTextBox.Text.EndsWith(Environment.NewLine))
                noteText = noteText[..^Environment.NewLine.Length];

            if (!TryRun(() => function(noteText, action), out string result))
                return;

            NoteTextBox.SelectAll();
            NoteTextBox.SelectedText = result;
            NoteTextBox.SelectionLength = 0;
            if (NoteTextBox.Text.Length > 0)
                NoteTextBox.CaretIndex = NoteTextBox.Text.Length - 1;
        }
    }

    protected void ApplyFunctionToEachLine(Func<string, int, Enum, string?> function, Enum action)
    {
        string noteText = (NoteTextBox.HasSelectedText) ? NoteTextBox.SelectedText : NoteTextBox.Text;

        string[] lines = noteText.Split(Environment.NewLine);
        // Ignore trailing new line if it was automatically added
        if (NoteTextBox.NewLineAtEnd && lines[^1] == "")
            lines = lines[..^1];

        bool succeeded = TryRun(
            () =>
            {
                List<string> newLines = [];
                for (int i = 0; i < lines.Length; i++)
                {
                    string? line = function(lines[i], i, action);
                    if (line != null)
                        newLines.Add(line);
                }
                return string.Join(Environment.NewLine, newLines);
            },
            out string result
        );
        if (!succeeded)
            return;

        if (NoteTextBox.HasSelectedText)
            NoteTextBox.SelectedText = result;
        else
        {
            NoteTextBox.SelectAll();
            NoteTextBox.SelectedText = result;
            NoteTextBox.SelectionLength = 0;
            if (NoteTextBox.Text.Length > 0)
                NoteTextBox.CaretIndex = NoteTextBox.Text.Length - 1;
        }
    }

    protected void InsertIntoNoteText(string text)
    {
        int caretIndex = (NoteTextBox.HasSelectedText) ? NoteTextBox.SelectionStart : NoteTextBox.CaretIndex;
        bool caretAtEnd = (caretIndex == NoteTextBox.Text.Length);

        NoteTextBox.SelectedText = text;

        NoteTextBox.CaretIndex = caretIndex + text.Length;
        if (!NoteTextBox.HasSelectedText && NoteTextBox.KeepNewLineAtEndVisible && caretAtEnd)
            NoteTextBox.ScrollToEnd();
    }

    /// <summary>
    /// 도구 함수를 실행한다. 실패하면(예: Base64가 아닌 텍스트를 디코딩) 텍스트는 그대로 두고 안내한다.
    /// 예전에는 이런 예외가 처리되지 않아 앱이 바로 종료됐다.
    /// </summary>
    private bool TryRun(Func<string> function, out string result)
    {
        try
        {
            result = function();
            return true;
        }
        catch (Exception ex)
        {
            ErrorReporter.Log(ex, $"Tool '{_toolName}'");
            MessageBox.Show(
                Window.GetWindow(NoteTextBox),
                string.Format(Strings.Error_ToolFailed, _toolName),
                Strings.App_Name,
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            result = "";
            return false;
        }
    }
}
