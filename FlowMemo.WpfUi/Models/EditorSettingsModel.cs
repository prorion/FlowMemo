using FlowMemo.Core.Enums;

namespace FlowMemo.WpfUi.Models;

public class EditorSettingsModel : BaseModel
{
    // General
    public bool CheckSpelling { get; set => SetProperty(ref field, value); }
    public bool NewLineAtEnd { get; set => SetProperty(ref field, value); }
    public bool KeepNewLineVisible { get; set => SetProperty(ref field, value); }
    public bool WrapText { get; set => SetProperty(ref field, value); }

    // Fonts
    public string StandardFontFamily { get; set => SetProperty(ref field, value); } = string.Empty;
    public string MonoFontFamily { get; set => SetProperty(ref field, value); } = string.Empty;
    public bool UseMonoFont { get; set => SetProperty(ref field, value); }

    // Indentation
    public bool AutoIndent { get; set => SetProperty(ref field, value); }
    public bool UseSpacesForTab { get; set => SetProperty(ref field, value); }
    public int TabSpacesWidth { get; set => SetProperty(ref field, value); }
    public bool ConvertIndentationOnPaste { get; set => SetProperty(ref field, value); }

    // Clipboard
    public CopyAction CopyAction { get; set => SetProperty(ref field, value); }
    public bool TrimTextOnCopy { get; set => SetProperty(ref field, value); }
    public CopyAction CopyAltAction { get; set => SetProperty(ref field, value); }
    public bool TrimTextOnAltCopy { get; set => SetProperty(ref field, value); }
    public CopyFallbackAction CopyFallbackAction { get; set => SetProperty(ref field, value); }
    public bool TrimTextOnFallbackCopy { get; set => SetProperty(ref field, value); }
    public CopyFallbackAction CopyAltFallbackAction { get; set => SetProperty(ref field, value); }
    public bool TrimTextOnAltFallbackCopy { get; set => SetProperty(ref field, value); }
    public bool CopyOnSelect { get; set => SetProperty(ref field, value); }
    public PasteAction PasteAction { get; set => SetProperty(ref field, value); }
    public bool TrimTextOnPaste { get; set => SetProperty(ref field, value); }
    public PasteAction PasteAltAction { get; set => SetProperty(ref field, value); }
    public bool TrimTextOnAltPaste { get; set => SetProperty(ref field, value); }
    public bool MiddleClickPaste { get; set => SetProperty(ref field, value); }
}
