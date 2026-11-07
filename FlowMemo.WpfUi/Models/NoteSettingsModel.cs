using FlowMemo.Core.Enums;

namespace FlowMemo.WpfUi.Models;

/// <summary>
/// 메모 설정. 속성 이름이 곧 DB 키("Notes.속성이름")이므로 이름을 바꾸면 저장된 값을 잃는다.
/// 초기값이 기본값이다 (DB에 키가 없을 때 쓴다).
/// </summary>
public class NoteSettingsModel : BaseModel
{
    // General
    public double DefaultWidth { get; set => SetProperty(ref field, value); } = 300;
    public double DefaultHeight { get; set => SetProperty(ref field, value); } = 300;
    public StartupPosition StartupPosition { get; set => SetProperty(ref field, value); } = StartupPosition.TopLeft;
    public bool PinnedByDefault { get; set => SetProperty(ref field, value); } = false;
    public MinimizeMode MinimizeMode { get; set => SetProperty(ref field, value); } = MinimizeMode.Allow;
    public VisibilityMode VisibilityMode { get; set => SetProperty(ref field, value); } = VisibilityMode.ShowInTaskbar;
    public bool HideTitleBar { get; set => SetProperty(ref field, value); } = false;

    // Theme
    public bool CycleColours { get; set => SetProperty(ref field, value); } = true;
    public ColourMode ColourMode { get; set => SetProperty(ref field, value); } = ColourMode.Light;

    // Transparency
    public TransparencyMode TransparencyMode { get; set => SetProperty(ref field, value); } = TransparencyMode.WhenPinned;
    public bool OpaqueWhenFocused { get; set => SetProperty(ref field, value); } = true;
    public double OpaqueValue { get; set => SetProperty(ref field, value); } = 1.0;
    public double TransparentValue { get; set => SetProperty(ref field, value); } = 0.8;
}
