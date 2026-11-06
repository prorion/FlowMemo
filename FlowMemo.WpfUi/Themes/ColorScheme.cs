using System.Windows.Media;
using System.Windows.Shapes;

namespace FlowMemo.WpfUi.Themes;

public class ColourScheme(string name, string displayName, string iconColourHex, Palette light, Palette dark)
{
    private const int IconSize = 16;

    /// <summary>DB에 저장하는 키 (영어, 바꾸지 말 것).</summary>
    public string Name { get; set; } = name;

    /// <summary>메뉴에 보여 주는 이름.</summary>
    public string DisplayName { get; set; } = displayName;
    public Color IconColour { get; set; } = (Color)ColorConverter.ConvertFromString(iconColourHex);
    public Palette Light { get; set; } = light;
    public Palette Dark { get; set; } = dark;

    public Rectangle Icon
    {
        get
        {
            _icon ??= new()
            {
                Width = IconSize,
                Height = IconSize,
                Fill = new SolidColorBrush(IconColour)
            };

            return _icon;
        }
    }
    private Rectangle? _icon = null;
}
