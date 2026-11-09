using FlowMemo.Core.Enums;

namespace FlowMemo.WpfUi.Models;

/// <summary>
/// 편집기 설정. 속성 이름이 곧 DB 키("Editor.속성이름")이므로 이름을 바꾸면 저장된 값을 잃는다.
/// 초기값이 기본값이다 (DB에 키가 없을 때 쓴다).
/// </summary>
public class EditorSettingsModel : BaseModel
{
    // General
    public bool CheckSpelling { get; set => SetProperty(ref field, value); } = true;
    public bool NewLineAtEnd { get; set => SetProperty(ref field, value); } = true;
    public bool KeepNewLineVisible { get; set => SetProperty(ref field, value); } = true;
    public bool WrapText { get; set => SetProperty(ref field, value); } = true;

    // Fonts
    public string StandardFontFamily { get; set => SetProperty(ref field, value); } = "Segoe UI";
    public string MonoFontFamily { get; set => SetProperty(ref field, value); } = "Consolas";
    public bool UseMonoFont { get; set => SetProperty(ref field, value); } = false;
    public double FontSize { get; set => SetProperty(ref field, value); } = 16;

    // Indentation
    public bool AutoIndent { get; set => SetProperty(ref field, value); } = true;
    public bool UseSpacesForTab { get; set => SetProperty(ref field, value); } = false;
    public int TabSpacesWidth { get; set => SetProperty(ref field, value); } = 4;
    public bool ConvertIndentationOnPaste { get; set => SetProperty(ref field, value); } = false;

    // Clipboard
    public CopyAction CopyAction { get; set => SetProperty(ref field, value); } = CopyAction.CopySelected;
    public bool TrimTextOnCopy { get; set => SetProperty(ref field, value); } = false;
    public CopyAction CopyAltAction { get; set => SetProperty(ref field, value); } = CopyAction.CopySelected;
    public bool TrimTextOnAltCopy { get; set => SetProperty(ref field, value); } = true;
    public CopyFallbackAction CopyFallbackAction { get; set => SetProperty(ref field, value); } = CopyFallbackAction.CopyLine;
    public bool TrimTextOnFallbackCopy { get; set => SetProperty(ref field, value); } = false;
    public CopyFallbackAction CopyAltFallbackAction { get; set => SetProperty(ref field, value); } = CopyFallbackAction.CopyNote;
    public bool TrimTextOnAltFallbackCopy { get; set => SetProperty(ref field, value); } = false;
    public bool CopyOnSelect { get; set => SetProperty(ref field, value); } = false;
    public PasteAction PasteAction { get; set => SetProperty(ref field, value); } = PasteAction.Paste;
    public bool TrimTextOnPaste { get; set => SetProperty(ref field, value); } = false;
    public PasteAction PasteAltAction { get; set => SetProperty(ref field, value); } = PasteAction.Paste;
    public bool TrimTextOnAltPaste { get; set => SetProperty(ref field, value); } = true;
    public bool MiddleClickPaste { get; set => SetProperty(ref field, value); } = true;
}
