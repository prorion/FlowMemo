using System.Windows.Media;
using System.Windows.Shapes;

namespace FlowMemo.WpfUi.Themes;

public class ColourScheme(string name, string iconColourHex, Palette light, Palette dark)
{
    private const int IconSize = 16;

    public string Name { get; set; } = name;
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
