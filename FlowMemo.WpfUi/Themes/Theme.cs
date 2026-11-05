namespace FlowMemo.WpfUi.Themes;

public abstract class Theme
{
    public abstract string Name { get; }
    public abstract Dictionary<string, ColourScheme> ColourSchemes { get; }
    public abstract string DefaultColourSchemeName { get; }
}
