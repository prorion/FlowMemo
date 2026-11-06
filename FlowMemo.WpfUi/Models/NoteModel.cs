using System.Windows.Media;

using FlowMemo.Core.DataTransferObjects;
using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Themes;

namespace FlowMemo.WpfUi.Models;

public class NoteModel : BaseModel
{
    public NoteModel(NoteSettingsModel NoteSettings, string themeColourScheme)
    {

        Width = NoteSettings.DefaultWidth;
        Height = NoteSettings.DefaultHeight;

        IsPinned = NoteSettings.PinnedByDefault;

        ThemeColourScheme = themeColourScheme;

        TransparencyEnabled = (NoteSettings.TransparencyMode != TransparencyMode.Disabled);
    }

    public NoteModel(NoteDto noteDto, NoteSettingsModel NoteSettings)
    {
        Id = noteDto.Id;

        Content = noteDto.Content;

        X = noteDto.X;
        Y = noteDto.Y;
        Width = noteDto.Width;
        Height = noteDto.Height;

        GravityX = noteDto.GravityX;
        GravityY = noteDto.GravityY;

        ThemeColourScheme = noteDto.ThemeColourScheme;

        IsPinned = noteDto.IsPinned;
        IsOpen = noteDto.IsOpen;

        TransparencyEnabled = (NoteSettings.TransparencyMode != TransparencyMode.Disabled);
    }

    public int Id { get; set => SetProperty(ref field, value); }

    public string Content
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                OnPropertyChanged(nameof(WindowTitle));
        }
    } = "";

    /// <summary>
    /// 작업 표시줄·Alt+Tab에 보이는 창 제목. 메모 첫 줄 + " - FlowMemo".
    /// (예전에는 제목이 비어 있어서 작업 표시줄에서 메모를 구분할 수 없었다.)
    /// </summary>
    public string WindowTitle
    {
        get
        {
            const int maxLength = 40;
            string firstLine = Content.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            if (firstLine.Length > maxLength)
                firstLine = firstLine[..maxLength].TrimEnd() + "…";
            return firstLine.Length > 0 ? $"{firstLine} - FlowMemo" : "FlowMemo";
        }
    }

    public double X { get; set => SetProperty(ref field, value); }
    public double Y { get; set => SetProperty(ref field, value); }

    public double Width { get; set => SetProperty(ref field, value); }
    public double Height { get; set => SetProperty(ref field, value); }

    public int GravityX { get; set => SetProperty(ref field, value); }
    public int GravityY { get; set => SetProperty(ref field, value); }

    public string ThemeColourScheme { get; set => SetProperty(ref field, value); }

    public bool IsPinned { get; set => SetProperty(ref field, value); }
    public bool IsOpen { get; set => SetProperty(ref field, value); }


    public nint WindowHandle { get; set; }

    public bool IsFocused { get; set => SetProperty(ref field, value); }

    public bool TransparencyEnabled { get; set => SetProperty(ref field, value); }
    public double Opacity { get; set => SetProperty(ref field, value); }
    public bool ShowInTaskbar { get; set => SetProperty(ref field, value); }

    public Brush Background { get; set => SetProperty(ref field, value); } = Brushes.LightGray;
    public Brush BorderBrush { get; set => SetProperty(ref field, value); } = Brushes.DarkGray;
    public Brush TitleGridBackground { get; set => SetProperty(ref field, value); } = Brushes.Gray;
    public Brush TitleGridButtonForeground { get; set => SetProperty(ref field, value); } = Brushes.DarkGray;
    public Brush ContentTextBoxForeground { get; set => SetProperty(ref field, value); } = Brushes.Black;


    public void UpdateBrushes(Palette palette)
    {
        Background = new SolidColorBrush(palette.Background);
        BorderBrush = new SolidColorBrush(palette.Border);
        TitleGridBackground = new SolidColorBrush(palette.Title);
        TitleGridButtonForeground = new SolidColorBrush(palette.Button);
        ContentTextBoxForeground = new SolidColorBrush(palette.Text);
    }

    public NoteDto ToDto()
    {
        NoteDto noteDto = new(
            Id,

            Content,

            X,
            Y,
            Width,
            Height,

            GravityX,
            GravityY,

            ThemeColourScheme,

            IsPinned,
            IsOpen
        );

        return noteDto;
    }
}
