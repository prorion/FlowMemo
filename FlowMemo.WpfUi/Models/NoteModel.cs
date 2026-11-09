using System.Windows.Media;

using FlowMemo.Core.DataTransferObjects;
using FlowMemo.Core.Enums;
using FlowMemo.Core.Sync;
using FlowMemo.WpfUi.Themes;

namespace FlowMemo.WpfUi.Models;

public class NoteModel : BaseModel
{
    /// <summary>DB에 저장하는 속성. 나머지(포커스, 투명도, 브러시 등)는 화면용이라 바뀌어도 다시 저장하지 않는다.</summary>
    public static readonly IReadOnlySet<string> PersistedProperties = new HashSet<string>
    {
        nameof(Id),
        nameof(Content),
        nameof(X),
        nameof(Y),
        nameof(Width),
        nameof(Height),
        nameof(GravityX),
        nameof(GravityY),
        nameof(ThemeColourScheme),
        nameof(IsPinned),
        nameof(IsOpen),
        nameof(IsLocked),
        nameof(CreatedAt),
        nameof(UpdatedAt)
    };

    protected override bool IsPersistedProperty(string propertyName)
        => PersistedProperties.Contains(propertyName);

    public NoteModel(NoteSettingsModel NoteSettings, string themeColourScheme)
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;

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
        IsLocked = noteDto.IsLocked;

        // Content를 넣으면 UpdatedAt이 지금으로 바뀌므로, 저장된 시각은 그 뒤에 넣는다.
        CreatedAt = noteDto.CreatedAt;
        UpdatedAt = noteDto.UpdatedAt;

        TransparencyEnabled = (NoteSettings.TransparencyMode != TransparencyMode.Disabled);
    }

    public int Id { get; set => SetProperty(ref field, value); }

    public string Content
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                UpdatedAt = DateTime.UtcNow;
            }
        }
    } = "";

    /// <summary>만든 시각 (UTC). 스키마 v5 이전에 만든 메모는 알 수 없어서 null.</summary>
    public DateTime? CreatedAt { get; set => SetProperty(ref field, value); }

    /// <summary>내용을 마지막으로 고친 시각 (UTC). 위치·크기·색을 바꾼 것은 넣지 않는다.</summary>
    public DateTime? UpdatedAt { get; set => SetProperty(ref field, value); }

    /// <summary>잠금 (읽기 전용). 다시 실행해도 유지된다.</summary>
    public bool IsLocked { get; set => SetProperty(ref field, value); }

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

    // 메모 상단의 동기화 안내. 화면용이라 저장하지 않는다.
    public bool SyncBannerVisible { get; private set => SetProperty(ref field, value); }
    public string SyncBannerText { get; private set => SetProperty(ref field, value); } = "";
    public bool SyncShowIncomingActions { get; private set => SetProperty(ref field, value); }
    public bool SyncShowConflictActions { get; private set => SetProperty(ref field, value); }
    public bool SyncShowDeletedActions { get; private set => SetProperty(ref field, value); }

    public void SetSyncBanner(SyncNoteStatus status, string? text)
    {
        SyncBannerText = text ?? "";
        SyncBannerVisible = text is not null;
        SyncShowIncomingActions = status == SyncNoteStatus.Incoming;
        SyncShowConflictActions = status == SyncNoteStatus.Conflict;
        SyncShowDeletedActions = status == SyncNoteStatus.RemoteDeleted;
    }

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
            IsOpen,

            IsLocked,
            CreatedAt,
            UpdatedAt,
            DeletedAt: null // 휴지통은 관리 창에서만 다룬다 (NoteRepository.MoveToTrash)
        );

        return noteDto;
    }
}
