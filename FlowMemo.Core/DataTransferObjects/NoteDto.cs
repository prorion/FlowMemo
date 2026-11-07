namespace FlowMemo.Core.DataTransferObjects;

public record NoteDto(
    int Id,

    string Content,

    double X,
    double Y,
    double Width,
    double Height,

    int GravityX,
    int GravityY,

    string ThemeColourScheme,

    bool IsPinned,
    bool IsOpen,

    // 스키마 v5
    bool IsLocked,
    DateTime? CreatedAt,    // UTC. v5 이전에 만든 메모는 null
    DateTime? UpdatedAt,    // UTC. 내용을 마지막으로 고친 시각
    DateTime? DeletedAt     // UTC. null이 아니면 휴지통에 있는 메모
);
