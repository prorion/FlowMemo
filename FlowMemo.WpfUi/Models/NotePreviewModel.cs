using System.Windows.Media;

using FlowMemo.Core.DataTransferObjects;
using FlowMemo.WpfUi.Themes;

namespace FlowMemo.WpfUi.Models;

public class NotePreviewModel : BaseModel
{
    private const int MaxPreviewLength = 100;

    public NotePreviewModel(NoteDto noteDto)
    {
        Id = noteDto.Id;

        ContentPreview = noteDto.Content;

        ThemeColourScheme = noteDto.ThemeColourScheme;

        IsOpen = noteDto.IsOpen;
    }

    public int Id { get; set => SetProperty(ref field, value); }

    public string ContentPreview
    {
        get;
        set
        {
            string trimmedText = value.Trim();
            int previewLength = Math.Min(trimmedText.Length, MaxPreviewLength);
            string previewText = trimmedText[..previewLength];

            SetProperty(ref field, previewText);
        }
    } = "";

    public string ThemeColourScheme { get; set => SetProperty(ref field, value); }

    public Brush BackgroundBrush { get; set => SetProperty(ref field, value); } = Brushes.LightGray;
    public Brush BorderBrush { get; set => SetProperty(ref field, value); } = Brushes.DarkGray;
    public Brush TitleBrush { get; set => SetProperty(ref field, value); } = Brushes.Gray;
    public Brush TextBrush { get; set => SetProperty(ref field, value); } = Brushes.Black;

    public bool IsSelected { get; set => SetProperty(ref field, value); }

    /// <summary>메모 창이 열려 있는지. 관리 창에서 닫힌 메모를 흐리게 보여 준다.</summary>
    public bool IsOpen { get; set => SetProperty(ref field, value); }

    public void UpdateBrushes(Palette palette)
    {
        BackgroundBrush = new SolidColorBrush(palette.Background);
        BorderBrush = new SolidColorBrush(palette.Border);
        TitleBrush = new SolidColorBrush(palette.Title);
        TextBrush = new SolidColorBrush(palette.Text);
    }
}
