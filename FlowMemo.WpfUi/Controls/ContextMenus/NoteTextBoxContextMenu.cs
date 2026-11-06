using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls.BackgroundSpellCheck;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Resources;
using FlowMemo.WpfUi.Tools;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FlowMemo.WpfUi.Controls.ContextMenus;

public class NoteTextBoxContextMenu : ContextMenu
{
    private readonly NoteTextBoxControl _noteTextBox;

    private readonly ITool[] _tools;
    private readonly Dictionary<ITool, int> _toolGroup = [];

    private readonly MenuItem _copyMenuItem;
    private readonly MenuItem _cutMenuItem;
    private readonly MenuItem _pasteMenuItem;
    private readonly MenuItem _selectAllMenuItem;
    private readonly MenuItem _clearMenuItem;
    private readonly MenuItem _lockedMenuItem;
    private readonly MenuItem _countsMenuItem;
    private readonly MenuItem _lineCountMenuItem;
    private readonly MenuItem _wordCountMenuItem;
    private readonly MenuItem _charCountMenuItem;
    private readonly Separator _toolsSeparator = new();
    private readonly MenuItem _toolsMenuItem;

    private readonly List<Control> _spellingErrorMenuItems = [];
    private readonly List<Control> _toolMenuItems = [];

    public NoteTextBoxContextMenu(NoteTextBoxControl noteTextBox)
    {
        _noteTextBox = noteTextBox;

        Style = Application.Current.FindResource(typeof(ContextMenu)) as Style;

        // 예전에는 영어 이름 알파벳 순서라 한국어로는 순서가 뒤섞여 보였다 (Base64, 괄호, 대소문자, 색상 코드...).
        // 용도별로 묶고, 메뉴에서는 묶음 사이에 구분선을 넣는다.
        ITool[][] groups =
        [
            // 정리
            [new TrimTool(_noteTextBox), new RemoveTool(_noteTextBox), new JoinTool(_noteTextBox), new SplitTool(_noteTextBox),
             new SortTool(_noteTextBox), new ListTool(_noteTextBox), new IndentTool(_noteTextBox)],
            // 감싸기·바꾸기
            [new BracketTool(_noteTextBox), new QuoteTool(_noteTextBox), new SlashTool(_noteTextBox), new CaseTool(_noteTextBox)],
            // 인코딩·변환
            [new Base64Tool(_noteTextBox), new UrlTool(_noteTextBox), new HtmlEntityTool(_noteTextBox), new JsonTool(_noteTextBox),
             new ColourTool(_noteTextBox), new HashTool(_noteTextBox)],
            // 넣기
            [new DateTimeTool(_noteTextBox), new GuidTool(_noteTextBox), new GibberishTool(_noteTextBox)],
        ];
        _tools = [.. groups.SelectMany(g => g)];
        for (int g = 0; g < groups.Length; g++)
            foreach (ITool tool in groups[g])
                _toolGroup[tool] = g;

        _copyMenuItem = new()
        {
            Header = Strings.TextMenu_Copy,
            Command = _noteTextBox.CopyCommand,
            InputGestureText = "Ctrl+C"
        };
        _cutMenuItem = new()
        {
            Header = Strings.TextMenu_Cut,
            Command = _noteTextBox.CutCommand,
            InputGestureText = "Ctrl+X"
        };
        _pasteMenuItem = new()
        {
            Header = Strings.TextMenu_Paste,
            Command = _noteTextBox.PasteCommand,
            InputGestureText = "Ctrl+V"
        };

        _selectAllMenuItem = new()
        {
            Header = Strings.TextMenu_SelectAll,
            Command = new RelayCommand(_noteTextBox.SelectAll)
        };
        _clearMenuItem = new()
        {
            Header = Strings.TextMenu_Clear,
            Command = _noteTextBox.ClearCommand
        };

        _lockedMenuItem = new()
        {
            Header = Strings.TextMenu_Locked,
            IsCheckable = true,
            Command = _noteTextBox.SetReadOnlyCommand,
            CommandParameter = true // Will bind IsChecked to this below
        };
        _lockedMenuItem.SetBinding(
            MenuItem.CommandParameterProperty,
            new Binding(nameof(MenuItem.IsChecked))
            {
                Source = _lockedMenuItem
            }
        );

        _countsMenuItem = new()
        {
            Header = Strings.TextMenu_Counts
        };
        _lineCountMenuItem = new()
        {
            IsEnabled = false
        };
        _wordCountMenuItem = new()
        {
            IsEnabled = false
        };
        _charCountMenuItem = new()
        {
            IsEnabled = false
        };

        _toolsMenuItem = new()
        {
            Header = Strings.TextMenu_Tools
        };

        MenuHelper.AlignSubmenuHeader(_countsMenuItem);
        MenuHelper.AlignSubmenuHeader(_toolsMenuItem);

        Populate();
    }

    public void Update()
    {
        bool hasText = (_noteTextBox.Text.Length > 0);

        UpdateSpellingErrorMenuItems();

        _copyMenuItem.IsEnabled = _noteTextBox.HasSelectedText;
        _cutMenuItem.IsEnabled = _noteTextBox.HasSelectedText;
        _pasteMenuItem.IsEnabled = ClipboardHelper.ContainsText();

        _selectAllMenuItem.IsEnabled = hasText;
        _clearMenuItem.IsEnabled = hasText;

        _lineCountMenuItem.Header = string.Format(Strings.TextMenu_LineCount, _noteTextBox.LineCount());
        _wordCountMenuItem.Header = string.Format(Strings.TextMenu_WordCount, _noteTextBox.WordCount());
        _charCountMenuItem.Header = string.Format(Strings.TextMenu_CharCount, _noteTextBox.CharCount());

        UpdateToolContextMenus();
    }

    private void Populate()
    {
        Items.Add(_copyMenuItem);
        Items.Add(_cutMenuItem);
        Items.Add(_pasteMenuItem);

        Items.Add(new Separator());

        Items.Add(_selectAllMenuItem);
        Items.Add(_clearMenuItem);

        Items.Add(new Separator());

        Items.Add(_lockedMenuItem);

        Items.Add(new Separator());

        _countsMenuItem.Items.Add(_lineCountMenuItem);
        _countsMenuItem.Items.Add(_wordCountMenuItem);
        _countsMenuItem.Items.Add(_charCountMenuItem);
        Items.Add(_countsMenuItem);
    }

    private void UpdateSpellingErrorMenuItems()
    {
        foreach (Control spellingErrorMenuItem in _spellingErrorMenuItems)
            Items.Remove(spellingErrorMenuItem);

        _spellingErrorMenuItems.Clear();

        int caretIndex = _noteTextBox.CaretIndex;
        BackgroundSpellingError? spellingError = _noteTextBox.GetBackgroundSpellingError(caretIndex);
        if (spellingError is not null)
        {
            List<string> suggestions = _noteTextBox.GetSpellingSuggestions(spellingError.Word);
            if (suggestions.Count == 0)
                _spellingErrorMenuItems.Add(
                    new MenuItem()
                    {
                        Header = Strings.TextMenu_NoSpellingSuggestions,
                        IsEnabled = false
                    }
                );
            else
                foreach (string spellingSuggestion in suggestions)
                {
                    BackgroundSpellingSuggestion backgroundSpellingSuggestion = new()
                    {
                        Word = spellingSuggestion,
                        Error = spellingError
                    };

                    _spellingErrorMenuItems.Add(
                        new MenuItem()
                        {
                            Header = spellingSuggestion,
                            FontWeight = FontWeights.Bold,
                            Command = _noteTextBox.ApplySpellingSuggestionCommand,
                            CommandParameter = backgroundSpellingSuggestion
                        }
                    );
                }
            _spellingErrorMenuItems.Add(new Separator());
        }

        _spellingErrorMenuItems.Reverse();
        foreach (Control spellingErrorMenuItem in _spellingErrorMenuItems)
            Items.Insert(0, spellingErrorMenuItem);
    }

    private void UpdateToolContextMenus()
    {
        foreach (Control toolMenuItem in _toolMenuItems)
            Items.Remove(toolMenuItem);
        _toolsMenuItem.Items.Clear();

        _toolMenuItems.Clear();

        IEnumerable<ITool> activeTools = _tools.Where(t => t.State != ToolState.Disabled);
        if (!activeTools.Any())
            return;

        _toolMenuItems.Add(_toolsSeparator);

        bool hasEnabledTools = false;
        int lastEnabledGroup = -1;
        foreach (ITool tool in activeTools)
        {
            switch (tool.State)
            {
                case ToolState.Favourite:
                    _toolMenuItems.Add(tool.MenuItem);
                    break;
                case ToolState.Enabled:
                    int group = _toolGroup[tool];
                    if (hasEnabledTools && group != lastEnabledGroup)
                        _toolsMenuItem.Items.Add(new Separator());
                    lastEnabledGroup = group;
                    _toolsMenuItem.Items.Add(tool.MenuItem);
                    hasEnabledTools = true;
                    break;
            }
        }

        if (hasEnabledTools)
            _toolMenuItems.Add(_toolsMenuItem);

        foreach (Control toolMenuItem in _toolMenuItems)
            Items.Add(toolMenuItem);
    }
}
