using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls.BackgroundSpellCheck;
using FlowMemo.WpfUi.Controls.ContextMenus;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Resources;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace FlowMemo.WpfUi.Controls;

public partial class NoteTextBoxControl : TextBox
{
    private readonly BackgroundSpellChecker _spellChecker;

    private DispatcherTimer? _updateTimer;
    private readonly DispatcherTimer _newLineTimer;
    private BackgroundSpellingErrorAdorner? _adorner;
    private ScrollViewer? _scrollViewer;
    private IEnumerable<BackgroundSpellingError> _currentErrors = [];

    private CancellationTokenSource? _checkCancellationToken;

    private readonly NoteTextBoxContextMenu _contextMenu = null!;

    // 진행 중인 한글 IME 조합 수. 조합 중에 프로그램이 텍스트를 고치면
    // (끝에 줄바꿈 붙이기 등) 조합이 끊겨 글자가 자모로 흩어진다 (실제 입력으로 확인: "한글" -> "한ㅡㄹ").
    // - 한국어 IME는 앞 글자의 조합 완료보다 다음 글자의 조합 시작을 먼저 알리므로 참/거짓 하나로는 부족하다.
    // - 완료 이벤트는 시작 때와 다른 TextComposition 객체로 와서 객체로 짝을 맞출 수도 없다. 그래서 수를 센다.
    // - TSF 한국어 IME에서는 조합 중인 키도 Key.ImeProcessed로 오지 않으므로 키 종류로 판단하면 안 된다
    //   (그렇게 했다가 "한ㅡㄹ"이 다시 생겼다). 포커스를 잃으면 0으로 되돌린다.
    private int _imeCompositionDepth;

    private bool IsImeComposing => _imeCompositionDepth > 0;

    private const int MaxDropFileBytes = 5 * 1024 * 1024;

    public NoteTextBoxControl() : base()
    {
        AcceptsReturn = true;
        AcceptsTab = true;
        AllowDrop = true;
        SpellCheck.IsEnabled = false;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;

        _spellChecker = new(CultureInfo.CurrentUICulture.Name);

        _updateTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _updateTimer.Tick += OnUpdateTimerTick;

        // '끝에 빈 줄 유지'는 입력이 잠깐 멈춘 뒤에 적용한다 (아래 RequestNewLineAtEnd 설명 참고).
        _newLineTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _newLineTimer.Tick += (s, e) =>
        {
            _newLineTimer.Stop();
            EnforceNewLineAtEnd();
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        TextChanged += OnTextChanged;
        SelectionChanged += OnSelectionChanged;
        PreviewDragOver += OnPreviewDragOver;
        PreviewDrop += OnPreviewDrop;
        MouseDoubleClick += OnMouseDoubleClick;
        // TextBox가 MouseDown을 먼저 처리(Handled)해서 예전 MouseDown 핸들러는 세 번/네 번 클릭 때 호출되지 않았다.
        PreviewMouseDown += OnPreviewMouseDown;
        MouseUp += OnMouseUp;
        PreviewKeyDown += OnPreviewKeyDown;
        ContextMenuOpening += OnContextMenuOpening;
        LostKeyboardFocus += (s, e) => _imeCompositionDepth = 0;

        TextCompositionManager.AddPreviewTextInputStartHandler(this, OnPreviewTextInputStart);
        TextCompositionManager.AddPreviewTextInputHandler(this, OnPreviewTextInputCompleted);

        ApplySpellingSuggestionCommand = new(ApplySpellingSuggestion);
        IgnoreSpellingErrorCommand = new(IgnoreSpellingError);
        AddWordToDictionaryCommand = new(AddWordToDictionary);
        CopyCommand = new(Copy);
        CutCommand = new(Cut);
        PasteCommand = new(Paste);
        ClearCommand = new(Clear);
        SetReadOnlyCommand = new(SetReadOnly);

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopyExecuted));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, OnCutExecuted));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, OnPasteExecuted));

        InputBindings.Add(new InputBinding(CopyCommand, new KeyGesture(Key.C, ModifierKeys.Control)));
        InputBindings.Add(new InputBinding(CopyCommand, new KeyGesture(Key.C, ModifierKeys.Control | ModifierKeys.Shift)));
        InputBindings.Add(new InputBinding(CutCommand, new KeyGesture(Key.X, ModifierKeys.Control)));
        InputBindings.Add(new InputBinding(CutCommand, new KeyGesture(Key.X, ModifierKeys.Control | ModifierKeys.Shift)));
        InputBindings.Add(new InputBinding(PasteCommand, new KeyGesture(Key.V, ModifierKeys.Control)));
        InputBindings.Add(new InputBinding(PasteCommand, new KeyGesture(Key.V, ModifierKeys.Control | ModifierKeys.Shift)));

        if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
        {
            _contextMenu = new NoteTextBoxContextMenu(this);
            ContextMenu = _contextMenu;
        }
    }

    // General
    public bool BackgroundSpellCheck
    {
        get => (bool)GetValue(BackgroundSpellCheckProperty);
        set => SetValue(BackgroundSpellCheckProperty, value);
    }
    public static readonly DependencyProperty BackgroundSpellCheckProperty = DependencyProperty.Register(nameof(BackgroundSpellCheck), typeof(bool), typeof(NoteTextBoxControl), new PropertyMetadata(false, OnBackgroundSpellCheckChanged));

    public bool AutoIndent
    {
        get => (bool)GetValue(AutoIndentProperty);
        set => SetValue(AutoIndentProperty, value);
    }
    public static readonly DependencyProperty AutoIndentProperty = DependencyProperty.Register(nameof(AutoIndent), typeof(bool), typeof(NoteTextBoxControl));

    public bool NewLineAtEnd
    {
        get => (bool)GetValue(NewLineAtEndProperty);
        set => SetValue(NewLineAtEndProperty, value);
    }
    public static readonly DependencyProperty NewLineAtEndProperty = DependencyProperty.Register(nameof(NewLineAtEnd), typeof(bool), typeof(NoteTextBoxControl));

    public bool KeepNewLineAtEndVisible
    {
        get => (bool)GetValue(KeepNewLineAtEndVisibleProperty);
        set => SetValue(KeepNewLineAtEndVisibleProperty, value);
    }
    public static readonly DependencyProperty KeepNewLineAtEndVisibleProperty = DependencyProperty.Register(nameof(KeepNewLineAtEndVisible), typeof(bool), typeof(NoteTextBoxControl));

    public bool WrapText
    {
        get => (bool)GetValue(WrapTextProperty);
        set => SetValue(WrapTextProperty, value);
    }
    public static readonly DependencyProperty WrapTextProperty = DependencyProperty.Register(nameof(WrapText), typeof(bool), typeof(NoteTextBoxControl), new PropertyMetadata(OnWrapTextChanged));

    // Fonts
    public string StandardFontFamily
    {
        get => (string)GetValue(StandardFontFamilyProperty);
        set => SetValue(StandardFontFamilyProperty, value);
    }
    public static readonly DependencyProperty StandardFontFamilyProperty = DependencyProperty.Register(nameof(StandardFontFamily), typeof(string), typeof(NoteTextBoxControl), new PropertyMetadata(OnFontPropertyChanged));

    public string MonoFontFamily
    {
        get => (string)GetValue(MonoFontFamilyProperty);
        set => SetValue(MonoFontFamilyProperty, value);
    }
    public static readonly DependencyProperty MonoFontFamilyProperty = DependencyProperty.Register(nameof(MonoFontFamily), typeof(string), typeof(NoteTextBoxControl), new PropertyMetadata(OnFontPropertyChanged));

    public bool UseMonoFont
    {
        get => (bool)GetValue(UseMonoFontProperty);
        set => SetValue(UseMonoFontProperty, value);
    }
    public static readonly DependencyProperty UseMonoFontProperty = DependencyProperty.Register(nameof(UseMonoFont), typeof(bool), typeof(NoteTextBoxControl), new PropertyMetadata(OnFontPropertyChanged));

    // Indentation
    public bool TabSpaces
    {
        get => (bool)GetValue(TabSpacesProperty);
        set => SetValue(TabSpacesProperty, value);
    }
    public static readonly DependencyProperty TabSpacesProperty = DependencyProperty.Register(nameof(TabSpaces), typeof(bool), typeof(NoteTextBoxControl));

    public bool ConvertIndentation
    {
        get => (bool)GetValue(ConvertIndentationProperty);
        set => SetValue(ConvertIndentationProperty, value);
    }
    public static readonly DependencyProperty ConvertIndentationProperty = DependencyProperty.Register(nameof(ConvertIndentation), typeof(bool), typeof(NoteTextBoxControl));

    public int TabWidth
    {
        get => (int)GetValue(TabWidthProperty);
        set => SetValue(TabWidthProperty, value);
    }
    public static readonly DependencyProperty TabWidthProperty = DependencyProperty.Register(nameof(TabWidth), typeof(int), typeof(NoteTextBoxControl));

    // 설정에서 0이나 음수를 넣으면 0으로 나누기/PadLeft 예외로 앱이 종료됐다.
    private int EffectiveTabWidth => Math.Clamp(TabWidth, 1, 16);

    // Copy and Paste
    public CopyAction CopyAction
    {
        get => (CopyAction)GetValue(CopyActionProperty);
        set => SetValue(CopyActionProperty, value);
    }
    public static readonly DependencyProperty CopyActionProperty = DependencyProperty.Register(nameof(CopyAction), typeof(CopyAction), typeof(NoteTextBoxControl));

    public bool TrimTextOnCopy
    {
        get => (bool)GetValue(TrimTextOnCopyProperty);
        set => SetValue(TrimTextOnCopyProperty, value);
    }
    public static readonly DependencyProperty TrimTextOnCopyProperty = DependencyProperty.Register(nameof(TrimTextOnCopy), typeof(bool), typeof(NoteTextBoxControl));

    public CopyAction CopyAltAction
    {
        get => (CopyAction)GetValue(CopyAltActionProperty);
        set => SetValue(CopyAltActionProperty, value);
    }
    public static readonly DependencyProperty CopyAltActionProperty = DependencyProperty.Register(nameof(CopyAltAction), typeof(CopyAction), typeof(NoteTextBoxControl));

    public bool TrimTextOnAltCopy
    {
        get => (bool)GetValue(TrimTextOnAltCopyProperty);
        set => SetValue(TrimTextOnAltCopyProperty, value);
    }
    public static readonly DependencyProperty TrimTextOnAltCopyProperty = DependencyProperty.Register(nameof(TrimTextOnAltCopy), typeof(bool), typeof(NoteTextBoxControl));

    public CopyFallbackAction CopyFallbackAction
    {
        get => (CopyFallbackAction)GetValue(CopyFallbackActionProperty);
        set => SetValue(CopyFallbackActionProperty, value);
    }
    public static readonly DependencyProperty CopyFallbackActionProperty = DependencyProperty.Register(nameof(CopyFallbackAction), typeof(CopyFallbackAction), typeof(NoteTextBoxControl));

    public bool TrimTextOnFallbackCopy
    {
        get => (bool)GetValue(TrimTextOnFallbackCopyProperty);
        set => SetValue(TrimTextOnFallbackCopyProperty, value);
    }
    public static readonly DependencyProperty TrimTextOnFallbackCopyProperty = DependencyProperty.Register(nameof(TrimTextOnFallbackCopy), typeof(bool), typeof(NoteTextBoxControl));

    public CopyFallbackAction CopyAltFallbackAction
    {
        get => (CopyFallbackAction)GetValue(CopyAltFallbackActionProperty);
        set => SetValue(CopyAltFallbackActionProperty, value);
    }
    public static readonly DependencyProperty CopyAltFallbackActionProperty = DependencyProperty.Register(nameof(CopyAltFallbackAction), typeof(CopyFallbackAction), typeof(NoteTextBoxControl));

    public bool TrimTextOnAltFallbackCopy
    {
        get => (bool)GetValue(TrimTextOnAltFallbackCopyProperty);
        set => SetValue(TrimTextOnAltFallbackCopyProperty, value);
    }
    public static readonly DependencyProperty TrimTextOnAltFallbackCopyProperty = DependencyProperty.Register(nameof(TrimTextOnAltFallbackCopy), typeof(bool), typeof(NoteTextBoxControl));

    public bool AutoCopy
    {
        get => (bool)GetValue(AutoCopyProperty);
        set => SetValue(AutoCopyProperty, value);
    }
    public static readonly DependencyProperty AutoCopyProperty = DependencyProperty.Register(nameof(AutoCopy), typeof(bool), typeof(NoteTextBoxControl));

    public PasteAction PasteAction
    {
        get => (PasteAction)GetValue(PasteActionProperty);
        set => SetValue(PasteActionProperty, value);
    }
    public static readonly DependencyProperty PasteActionProperty = DependencyProperty.Register(nameof(PasteAction), typeof(PasteAction), typeof(NoteTextBoxControl));

    public bool TrimTextOnPaste
    {
        get => (bool)GetValue(TrimTextOnPasteProperty);
        set => SetValue(TrimTextOnPasteProperty, value);
    }
    public static readonly DependencyProperty TrimTextOnPasteProperty = DependencyProperty.Register(nameof(TrimTextOnPaste), typeof(bool), typeof(NoteTextBoxControl));

    public PasteAction PasteAltAction
    {
        get => (PasteAction)GetValue(PasteAltActionProperty);
        set => SetValue(PasteAltActionProperty, value);
    }
    public static readonly DependencyProperty PasteAltActionProperty = DependencyProperty.Register(nameof(PasteAltAction), typeof(PasteAction), typeof(NoteTextBoxControl));

    public bool TrimTextOnAltPaste
    {
        get => (bool)GetValue(TrimTextOnAltPasteProperty);
        set => SetValue(TrimTextOnAltPasteProperty, value);
    }
    public static readonly DependencyProperty TrimTextOnAltPasteProperty = DependencyProperty.Register(nameof(TrimTextOnAltPaste), typeof(bool), typeof(NoteTextBoxControl));

    public bool MiddleClickPaste
    {
        get => (bool)GetValue(MiddleClickPasteProperty);
        set => SetValue(MiddleClickPasteProperty, value);
    }
    public static readonly DependencyProperty MiddleClickPasteProperty = DependencyProperty.Register(nameof(MiddleClickPaste), typeof(bool), typeof(NoteTextBoxControl));

    public RelayCommand<BackgroundSpellingSuggestion> ApplySpellingSuggestionCommand;
    public RelayCommand<string> IgnoreSpellingErrorCommand;
    public RelayCommand<string> AddWordToDictionaryCommand;
    public RelayCommand CopyCommand;
    public RelayCommand CutCommand;
    public RelayCommand PasteCommand;
    public RelayCommand ClearCommand;
    public RelayCommand<bool> SetReadOnlyCommand;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_scrollViewer != null)
            return;

        _scrollViewer = GetTemplateChild("PART_ContentHost") as ScrollViewer;

        _scrollViewer?.ScrollChanged += OnScrollChanged;
    }

    public new int LineCount()
    {
        int count;
        if (HasSelectedText)
            count = GetLineIndexFromCharacterIndex(SelectionStart + SelectionLength)
                - GetLineIndexFromCharacterIndex(SelectionStart)
                + 1;
        else
            count = GetLineIndexFromCharacterIndex(Text.Length) + ((NewLineAtEnd) ? 0 : 1);
        return count;
    }

    public int WordCount()
    {
        string text = (HasSelectedText) ? SelectedText : Text;
        if (text.Length == 0)
            return 0;
        return text.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public int CharCount()
    {
        string text = (HasSelectedText) ? SelectedText : Text;
        if (text.Length == 0)
            return 0;
        return text.Length - text.Count(c => c == '\n' || c == '\r'); // Subtract new lines from count.
    }

    public bool HasSelectedText
        => (SelectionLength > 0);

    public string GetCurrentLineText()
        => GetLineText(GetLineIndexFromCharacterIndex(CaretIndex));

    public BackgroundSpellingError? GetBackgroundSpellingError(int caretIndex)
        => _currentErrors.FirstOrDefault(e => caretIndex >= e.StartIndex && caretIndex < e.StartIndex + e.Length);

    public List<string> GetSpellingSuggestions(string word)
        => _spellChecker.GetSuggestions(word);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _adorner = new BackgroundSpellingErrorAdorner(this);

        AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(this);
        adornerLayer.Add(_adorner);

        UpdateSpellCheckAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        TextChanged -= OnTextChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        ContextMenuService.RemoveContextMenuOpeningHandler(this, OnContextMenuOpening);

        _scrollViewer?.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;

        _updateTimer?.Stop();
        _updateTimer?.Tick -= OnUpdateTimerTick;
        _newLineTimer.Stop();
        _updateTimer = null;

        _checkCancellationToken?.Cancel();
        _checkCancellationToken?.Dispose();
        _checkCancellationToken = null;

        _spellChecker.Dispose();

        if (_adorner is null)
            return;

        AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(this);
        adornerLayer?.Remove(_adorner);
        _adorner = null;
    }

    private void OnUpdateTimerTick(object? sender, EventArgs e)
    {
        _updateTimer?.Stop();
        UpdateSpellCheckAsync();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        _adorner?.InvalidateVisual();
    }

    private void ApplySpellingSuggestion(BackgroundSpellingSuggestion suggestion)
    {
        // Replace the misspelled word with the suggestion
        int startIndex = suggestion.Error.StartIndex;
        int length = suggestion.Error.Length;

        if (startIndex >= 0 && startIndex + length <= Text.Length)
        {
            string newText = Text.Remove(startIndex, length).Insert(startIndex, suggestion.Word);
            Text = newText;

            // Set caret position after the replaced word
            CaretIndex = startIndex + suggestion.Word.Length;
            Focus();
        }
    }

    private void IgnoreSpellingError(string word)
    {
        _spellChecker.IgnoreWord(word);

        // Trigger immediate recheck
        UpdateSpellCheckAsync();
    }

    private void AddWordToDictionary(string word)
    {
        _spellChecker.AddToDictionary(word);

        // Trigger immediate recheck
        UpdateSpellCheckAsync();
    }

    private static bool IsShiftPressed(bool exclusive = false)
        => (exclusive) ? (Keyboard.Modifiers == ModifierKeys.Shift) : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

    private void OnCopyExecuted(object sender, ExecutedRoutedEventArgs e)
        => Copy();
    private void OnCutExecuted(object sender, ExecutedRoutedEventArgs e)
        => Cut();
    private void OnPasteExecuted(object sender, ExecutedRoutedEventArgs e)
        => Paste();

    private new void Copy()
        => CopyText();

    private new void Cut()
        => CopyText(true);

    private void CopyText(bool cut = false)
    {
        bool useAlt = IsShiftPressed();

        string GetText(bool removeText)
            => useAlt
                ? GetTextForCopyAction(CopyAltAction, CopyAltFallbackAction, TrimTextOnAltCopy, TrimTextOnAltFallbackCopy, removeText)
                : GetTextForCopyAction(CopyAction, CopyFallbackAction, TrimTextOnCopy, TrimTextOnFallbackCopy, removeText);

        string copiedText = GetText(removeText: false);
        if (string.IsNullOrEmpty(copiedText))
            return;

        // 예전에는 잘라내기가 텍스트를 먼저 지우고 클립보드에 넣어서,
        // 클립보드가 잠겨 있으면 잘라낸 내용이 그대로 사라졌다. 이제 복사가 성공한 뒤에만 지운다.
        if (!ClipboardHelper.TrySetText(copiedText))
            return;

        if (cut)
            _ = GetText(removeText: true);
    }

    private string GetTextForCopyAction(CopyAction action, CopyFallbackAction fallbackAction, bool trim, bool fallbackTrim, bool cut)
    {
        string text;

        if (!HasSelectedText)
        {
            text = fallbackAction switch
            {
                CopyFallbackAction.CopyLine => HandleLineCopyOrCut(fallbackTrim, cut),
                CopyFallbackAction.CopyNote => HandleNoteCopyOrCut(fallbackTrim, cut),
                _ => string.Empty // Default, CopyFallbackActions.None
            };
            return text;
        }

        text = action switch
        {
            CopyAction.CopySelected => HandleSelectedTextCopyOrCut(trim, cut),
            CopyAction.CopyLine => HandleLineCopyOrCut(trim, cut),
            CopyAction.CopyAll => HandleNoteCopyOrCut(trim, cut),
            _ => string.Empty // Default, CopyActions.None
        };

        return text;
    }

    private string HandleSelectedTextCopyOrCut(bool trim, bool cut)
    {
        string text = SelectedText;

        if (trim)
            text = text.Trim();

        if (cut)
            SelectedText = string.Empty;

        return text;
    }

    private string HandleLineCopyOrCut(bool trim, bool cut)
    {
        int lineIndex = GetLineIndexFromCharacterIndex(CaretIndex);
        string lineText = GetLineText(lineIndex);

        string text = (trim) ? lineText.Trim() : lineText;

        if (cut)
        {
            int lineStart = GetCharacterIndexFromLineIndex(lineIndex);
            int lineLength = GetLineLength(lineIndex);

            Select(lineStart, lineLength);
            SelectedText = string.Empty;
        }

        return text;
    }

    private string HandleNoteCopyOrCut(bool trim, bool cut)
    {
        string text = (trim) ? Text.Trim() : Text;

        if (cut)
        {
            SelectAll();
            SelectedText = string.Empty;
        }

        return text;
    }

    private new void Paste()
    {
        bool trimText;
        PasteAction action;
        if (IsShiftPressed())
        {
            trimText = TrimTextOnAltPaste;
            action = PasteAltAction;
        }
        else
        {
            trimText = TrimTextOnPaste;
            action = PasteAction;
        }

        // Do nothing if action is None or clipboard does not contain text.
        if (action == PasteAction.None)
            return;

        string? clipboardString = ClipboardHelper.GetText();
        if (clipboardString is null)
            return;

        if (trimText)
            clipboardString = clipboardString.Trim();

        if (string.IsNullOrEmpty(clipboardString))
            return;

        // Conversion indentation
        if (ConvertIndentation)
        {
            string spaces = string.Empty.PadLeft(EffectiveTabWidth, ' ');
            if (TabSpaces)
                clipboardString = clipboardString.Replace("\t", spaces);
            else
                clipboardString = clipboardString.Replace(spaces, "\t");
        }

        switch (action)
        {
            case PasteAction.Paste:
                HandelPaste(clipboardString);
                break;
            case PasteAction.PasteAndReplaceAll:
                HandelPasteAndReplaceAll(clipboardString);
                break;
            case PasteAction.PasteAtEnd:
                HandelPasteAtEnd(clipboardString);
                break;
        }
    }

    private void HandelPaste(string pasteText)
    {
        int originalCaretIndex = SelectionStart;

        SelectedText = pasteText;
        SelectionLength = 0;
        CaretIndex = originalCaretIndex + pasteText.Length;

        EnforceNewLineAtEnd();
    }

    private void HandelPasteAndReplaceAll(string pasteText)
    {
        SelectAll();
        SelectedText = pasteText;
        SelectionLength = 0;
        CaretIndex = pasteText.Length;

        EnforceNewLineAtEnd();
    }

    private void HandelPasteAtEnd(string pasteText)
    {
        // Find the position before trailing newline if NewLineAtEnd is active
        int insertPosition = Text.Length;
        if (NewLineAtEnd && Text.EndsWith(Environment.NewLine))
            insertPosition -= Environment.NewLine.Length;

        CaretIndex = insertPosition;
        SelectionLength = 0;
        SelectedText = pasteText;
        CaretIndex = insertPosition + pasteText.Length;

        EnforceNewLineAtEnd();
    }

    private void EnforceNewLineAtEnd()
    {
        // 한글 조합 중에 끝에 줄바꿈을 붙이면 조합이 끊긴다. 조합이 끝나면 RequestNewLineAtEnd로 다시 온다.
        if (IsImeComposing || !NewLineAtEnd || Text.Length == 0 || Text.EndsWith(Environment.NewLine))
            return;

        // Store caret position
        int caretPosition = CaretIndex;
        int selectionStart = SelectionStart;
        int selectionLength = SelectionLength;

        AppendText(Environment.NewLine);

        CaretIndex = caretPosition;
        SelectionStart = selectionStart;
        SelectionLength = selectionLength;
    }

    /// <summary>
    /// 텍스트가 바뀌는 그 순간에 끝에 줄바꿈을 붙이지 않고, 입력이 0.3초 멈춘 뒤에 붙인다.
    /// 한국어 IME는 새 글자의 조합 텍스트를 먼저 넣고(TextChanged) 조합 시작을 나중에 알리기 때문에,
    /// TextChanged 안에서 바로 고치면 조합 중 여부를 알 수 없어 조합이 끊겼다 (실제 입력으로 확인: "한글" -> "한ㅡㄹ").
    /// 입력을 멈춰도 한국어 IME는 마지막 글자를 계속 조합 중으로 두므로, 그때도 조합 중이면 건드리지 않는다.
    /// </summary>
    private void RequestNewLineAtEnd()
    {
        if (!NewLineAtEnd || Text.Length == 0 || Text.EndsWith(Environment.NewLine))
            return;

        _newLineTimer.Stop();
        _newLineTimer.Start();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _updateTimer?.Stop();

        RequestNewLineAtEnd();

        if (!BackgroundSpellCheck || Text.Length == 0 || IsImeComposing)
            return;

        // 예전에는 Text[^1](맨 끝 글자)을 봤는데, '끝에 빈 줄 유지'가 켜져 있으면 맨 끝은 항상 줄바꿈이라
        // 글자를 칠 때마다 맞춤법 검사를 바로 돌렸다. 방금 입력한 글자(커서 앞)를 본다.
        int lastIndex = Math.Min(CaretIndex, Text.Length) - 1;
        char lastChar = lastIndex >= 0 ? Text[lastIndex] : ' ';

        if (!char.IsLetterOrDigit(lastChar)) // Is space, period, etc.
        {
            _updateTimer?.Stop();
            UpdateSpellCheckAsync();
            return;
        }

        _updateTimer?.Start();
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (AutoCopy && HasSelectedText)
            Copy();
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnPreviewTextInputStart(object sender, TextCompositionEventArgs e)
    {
        // IME 조합 시작. (일반 키 입력은 시작과 완료가 바로 이어서 온다.)
        _imeCompositionDepth++;
    }

    private void OnPreviewTextInputCompleted(object sender, TextCompositionEventArgs e)
    {
        if (_imeCompositionDepth > 0)
            _imeCompositionDepth--;
        if (_imeCompositionDepth > 0)
            return; // 다음 글자의 조합이 이미 시작됐다.

        // 조합 중에 미뤄 둔 '끝에 빈 줄 유지'를 조합이 끝나고 입력이 멈춘 뒤에 적용한다.
        RequestNewLineAtEnd();
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
            return;

        e.Handled = true;

        try
        {
            FileInfo file = new(files[0]);
            if (!file.Exists)
                return; // 폴더 등

            if (file.Length > MaxDropFileBytes)
            {
                ShowDropMessage(string.Format(Strings.Error_FileTooLarge, MaxDropFileBytes / 1024 / 1024));
                return;
            }

            string? content = DecodeTextFile(File.ReadAllBytes(file.FullName));
            if (content is null)
            {
                ShowDropMessage(Strings.Error_NotTextFile);
                return;
            }

            // 예전에는 메모 전체를 파일 내용으로 덮어써서, 실수로 파일을 끌어다 놓으면 메모가 사라졌다.
            // 이제 놓은 위치에 넣는다 (Ctrl+Z로 되돌릴 수 있다).
            int dropIndex = GetCharacterIndexFromPoint(e.GetPosition(this), true);
            if (dropIndex < 0 || dropIndex > Text.Length)
                dropIndex = CaretIndex;

            Focus();
            Select(dropIndex, 0);
            SelectedText = content;
            CaretIndex = dropIndex + content.Length;
        }
        catch (Exception ex)
        {
            ErrorReporter.Log(ex, "Drop file");
            ShowDropMessage(string.Format(Strings.Error_OpenFile, ex.Message));
        }
    }

    private void ShowDropMessage(string message)
        => MessageBox.Show(Window.GetWindow(this), message, Strings.App_Name, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>
    /// 텍스트 파일 내용을 읽는다. BOM이 있으면 따르고, 없으면 UTF-8로 읽되
    /// UTF-8이 아니면 한국어 Windows 기본 인코딩(CP949, 예전 메모장의 'ANSI')으로 읽는다.
    /// 바이너리 파일이면 null.
    /// </summary>
    private static string? DecodeTextFile(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        if (Array.IndexOf(bytes, (byte)0) >= 0)
            return null;

        try
        {
            return new System.Text.UTF8Encoding(false, true).GetString(bytes);
        }
        catch (System.Text.DecoderFallbackException)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(949).GetString(bytes);
        }
    }

    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HasSelectedText && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            Copy();
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Triple click to select line, quadruple click to select entire wrapped line
        if (e.ChangedButton != MouseButton.Left || e.ClickCount < 3 || e.ClickCount > 4)
            return;

        TextBox textBox = this;

        int clickIndex = textBox.GetCharacterIndexFromPoint(e.GetPosition(textBox), true);
        if (clickIndex < 0)
            clickIndex = textBox.CaretIndex;

        int lineIndex = textBox.GetLineIndexFromCharacterIndex(clickIndex);
        if (lineIndex < 0)
            return;
        int lineLength = textBox.GetLineLength(lineIndex);

        // If there was no new line and is not the last row, line must be wrapped.
        if (e.ClickCount == 4 && lineIndex < textBox.LineCount - 1 && !textBox.GetLineText(lineIndex).EndsWith(Environment.NewLine))
        {
            // Expand length until new line or last line found
            int nextLineIndex = lineIndex;
            do
            {
                nextLineIndex++;
                lineLength += textBox.GetLineLength(nextLineIndex);
            } while (nextLineIndex < textBox.LineCount - 1 && !textBox.GetLineText(nextLineIndex).EndsWith(Environment.NewLine));
        }

        textBox.SelectionStart = textBox.GetCharacterIndexFromLineIndex(lineIndex);
        textBox.SelectionLength = lineLength;

        // Don't select new line char(s)
        if (textBox.SelectedText.EndsWith(Environment.NewLine))
            textBox.SelectionLength -= Environment.NewLine.Length;

        // TextBox가 이 클릭을 다시 처리하면 방금 만든 선택이 풀린다.
        e.Handled = true;
        Focus();

        OnMouseDoubleClick(sender, e);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && MiddleClickPaste)
            Paste();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            e.Handled = HandledTabPressed();
        }
        else if (e.Key == Key.Return && AutoIndent && !IsImeComposing)
        {
            // 커서(또는 선택 영역 시작)가 있는 '실제' 줄의 앞쪽 공백을 새 줄에 이어 붙인다.
            // - 예전에는 화면에 보이는 줄(GetLineText)을 써서, 자동 줄 바꿈으로 꺾인 줄에서는 들여쓰기가 틀렸다.
            // - 예전에는 Text 전체를 다시 넣어서(Text = Text.Insert...) 조합 중인 글자와 부딪힐 수 있었다.
            //   이제 커서 위치만 바꾼다 (선택 영역이 있으면 함께 바뀌고, Ctrl+Z 한 번에 되돌릴 수 있다).
            int start = SelectionStart;
            int lineStart = start == 0 ? 0 : Text.LastIndexOf('\n', start - 1) + 1;
            int indentEnd = lineStart;
            while (indentEnd < start && (Text[indentEnd] == ' ' || Text[indentEnd] == '\t'))
                indentEnd++;

            string indent = Environment.NewLine + Text[lineStart..indentEnd];

            SelectedText = indent;
            CaretIndex = start + indent.Length;

            e.Handled = true;
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _contextMenu.Update();
    }

    private bool HandledTabPressed()
    {
        if ((!HasSelectedText || !SelectedText.Contains(Environment.NewLine)) && !IsShiftPressed() && TabSpaces)
        {
            int spaceCount = EffectiveTabWidth;
            int caretIndex = (HasSelectedText) ? SelectionStart : CaretIndex;

            int lineStart = GetCharacterIndexFromLineIndex(
                GetLineIndexFromCharacterIndex(caretIndex)
            );
            if (lineStart != caretIndex)
            {
                int lineCaretIndex = caretIndex - lineStart;
                int tabWidth = lineCaretIndex % spaceCount;
                if (tabWidth > 0)
                    spaceCount -= tabWidth;
            }
            string spaces = "".PadLeft(spaceCount, ' ');

            if (!HasSelectedText)
            {
                Select(caretIndex, 0);
                SelectedText = spaces;
                CaretIndex = caretIndex + spaceCount;
            }
            else
            {
                SelectedText = spaces;
                SelectionLength = 0;
                CaretIndex = caretIndex + spaceCount;
            }
            return true;
        }
        else if (!HasSelectedText && IsShiftPressed(true))
        {
            int caretIndex = CaretIndex;
            if (caretIndex > 0)
            {
                int tabLength = 0;
                int charIndex = caretIndex - 1; 

                if (Text[charIndex] == '\t')
                {
                    tabLength = 1;
                }
                else if (Text[charIndex] == ' ')
                {
                    while (charIndex >= 0 && Text[charIndex] == ' ' && tabLength < EffectiveTabWidth)
                    {
                        tabLength++;
                        charIndex--;
                    }
                }

                if (tabLength > 0)
                {
                    Select(caretIndex - tabLength, tabLength);
                    SelectedText = string.Empty;
                    CaretIndex = caretIndex - tabLength;
                }
            }
            return true;
        }
        else if (HasSelectedText && SelectedText.Contains(Environment.NewLine))
        {
            int selectionStart = SelectionStart;
            int selectionEnd = SelectionStart + SelectionLength;

            // Fix the selection so full lines, not wrapped lines, are selected
            // Find the starting line by making sure the previous line ends with a new line
            int startLineIndex = GetLineIndexFromCharacterIndex(selectionStart);
            while (startLineIndex > 0 && !GetLineText(startLineIndex - 1).Contains(Environment.NewLine))
                startLineIndex--;
            selectionStart = GetCharacterIndexFromLineIndex(startLineIndex);

            // Find the end line by making sure it ends with a new line
            int endLineIndex = GetLineIndexFromCharacterIndex(selectionEnd);
            int lineCount = LineCount();
            while (endLineIndex < lineCount - 1 && !GetLineText(endLineIndex).Contains(Environment.NewLine))
                endLineIndex++;
            selectionEnd = GetCharacterIndexFromLineIndex(endLineIndex) + GetLineLength(endLineIndex) - Environment.NewLine.Length;

            // Select the full lines so we can now easily get the required selected text
            Select(selectionStart, selectionEnd - selectionStart);

            // Loop though each line adding or removing the indentation where required
            string indentation = (TabSpaces) ? "".PadLeft(EffectiveTabWidth, ' ') : "\t";
            string[] lines = SelectedText.Split(Environment.NewLine);
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsShiftPressed(true))
                {
                    if (lines[i].Length > 0)
                    {
                        if (lines[i][0] == '\t')
                            lines[i] = lines[i][1..];
                        else if (lines[i][0] == ' ')
                        {
                            int concurrentSpaces = 0;
                            foreach (char character in lines[i])
                            {
                                if (character != ' ')
                                    break;
                                concurrentSpaces++;
                            }

                            switch (concurrentSpaces)
                            {
                                case >= 4:
                                    lines[i] = lines[i][4..];
                                    break;
                                case 1:
                                    lines[i] = lines[i][concurrentSpaces..];
                                    break;
                            }
                        }
                    }
                }
                else
                {
                    lines[i] = $"{indentation}{lines[i]}";
                }
            }

            SelectedText = string.Join(Environment.NewLine, lines);

            return true;
        }

        return false;
    }

    private static void OnFontPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NoteTextBoxControl control)
            control.UpdateFont();
    }

    private void UpdateFont()
    {
        this.FontFamily = new(
            (UseMonoFont) ? MonoFontFamily : StandardFontFamily
        );
    }

    private static void OnWrapTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NoteTextBoxControl control)
            control.UpdateTextWrapping();
    }

    private void UpdateTextWrapping()
    {
        TextWrapping = (WrapText) ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    private void SetReadOnly(bool isReadOnly)
    {
        // SetCurrentValue: 메모 창이 IsReadOnly를 Note.IsLocked에 양방향으로 묶어 두었으므로 바인딩을 지우지 않는다.
        SetCurrentValue(IsReadOnlyProperty, isReadOnly);
    }

    private async void UpdateSpellCheckAsync()
    {
        if (_adorner == null || !BackgroundSpellCheck)
            return;

        // Cancel any previous running check
        _checkCancellationToken?.Cancel();
        _checkCancellationToken?.Dispose();
        _checkCancellationToken = new CancellationTokenSource();
        CancellationToken token = _checkCancellationToken.Token;

        string text = Text; // Cant access .Text from another thread, so add to variable.

        try
        {
            List<BackgroundSpellingError> errors = await Task.Run(
                () => FindSpellingErrors(text), token
            );

            _currentErrors = errors;
            _adorner.UpdateErrors(errors);
        }
        catch (OperationCanceledException)
        {
            // If new check was started Task will throw this exception to discard this result
        }
    }

    private List<BackgroundSpellingError> FindSpellingErrors(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        return _spellChecker.CheckText(text);
    }

    private static void OnBackgroundSpellCheckChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NoteTextBoxControl control)
            control.UpdateSpellCheckState();
    }

    private void UpdateSpellCheckState()
    {
        if (BackgroundSpellCheck)
        {
            UpdateSpellCheckAsync();
        }
        else
        {
            _updateTimer?.Stop();
            _checkCancellationToken?.Cancel();
            _currentErrors = [];
            _adorner?.UpdateErrors([]);
        }
    }
}
