namespace FlowMemo.Core.Sync;

/// <summary>사용자가 결정해야 하는 상태. 메모 상단 안내에 그대로 쓴다.</summary>
public enum SyncNoteStatus
{
    None,
    /// <summary>다른 PC에서 고쳤고, 이 PC는 고치지 않았다.</summary>
    Incoming,
    /// <summary>양쪽에서 고쳤다.</summary>
    Conflict,
    /// <summary>다른 PC에서 지웠다.</summary>
    RemoteDeleted
}

public sealed record NoteSyncRow(
    int NoteId,
    string SyncId,
    long BaseRevision,
    string? BaseContent,
    string? BaseColour,
    bool BaseDeleted,
    long PendingRevision,
    string? PendingContent,
    string? PendingColour,
    bool PendingDeleted,
    string? PendingDevice,
    DateTime? PendingAt,
    long IgnoredRevision,
    bool HasNote,
    string Content,
    string? Colour,
    bool InTrash
)
{
    public bool HasPending => PendingRevision > BaseRevision;

    /// <summary>마지막으로 서버와 맞춘 뒤 이 PC에서 내용을 고쳤는지.</summary>
    public bool IsContentDirty => Content != (BaseContent ?? "");

    public SyncNoteStatus Status
    {
        get
        {
            if (!HasPending || !HasNote || InTrash)
                return SyncNoteStatus.None;

            bool ignored = IgnoredRevision == PendingRevision;
            if (PendingDeleted)
                return ignored && !IsContentDirty ? SyncNoteStatus.None : SyncNoteStatus.RemoteDeleted;
            if (IsContentDirty)
                return SyncNoteStatus.Conflict;
            return ignored ? SyncNoteStatus.None : SyncNoteStatus.Incoming;
        }
    }
}

/// <summary>서버에는 이 내용이 암호화되어 저장된다. 창 위치처럼 PC마다 다른 값은 넣지 않는다.</summary>
public sealed record SyncNotePayload(string Content, string Colour, DateTime? CreatedAt, DateTime? UpdatedAt);

public sealed record SyncRemoteNote(
    string Id,
    long Revision,
    string Blob,
    bool Deleted,
    string DeviceId,
    string DeviceName,
    DateTime? UpdatedAt
);

public sealed record SyncPresence(string Id, string DeviceName);

public sealed record SyncChangesPage(IReadOnlyList<SyncRemoteNote> Changes, bool More, IReadOnlyList<SyncPresence> Presence);

/// <summary>저장 결과. Conflict가 true면 다른 PC가 먼저 고친 것이고 Current에 서버의 현재 내용이 있다.</summary>
public sealed record SyncWriteResult(long Revision, bool Conflict, SyncRemoteNote? Current);

public sealed record SyncDevice(string Id, string Name, DateTime? CreatedAt);

public sealed record SyncDeviceList(string CurrentDeviceId, IReadOnlyList<SyncDevice> Devices);

/// <summary>이 PC에 저장하는 연결 정보. DPAPI로 암호화해서 파일에 둔다.</summary>
public sealed record SyncCredentials(
    string ServerUrl,
    string DeviceId,
    string DeviceName,
    string Token,
    string VaultKey,
    string? RecoverySecret
);

public interface ISyncApi
{
    Task<SyncChangesPage> GetChanges(long since);
    Task<SyncWriteResult> PutNote(string id, long baseRevision, string blob);
    Task<SyncWriteResult> DeleteNote(string id, long baseRevision);
}

public sealed class SyncCycleResult
{
    /// <summary>상태가 바뀌어 안내를 다시 그려야 하는 메모.</summary>
    public HashSet<int> ChangedNotes { get; } = [];

    /// <summary>색만 바뀌어 자동으로 적용한 메모 (열린 창에도 반영한다).</summary>
    public Dictionary<int, string> ColourChanges { get; } = [];

    /// <summary>다른 PC에서 새로 만들었거나 되살아난 메모 (관리 창 목록을 다시 읽는다).</summary>
    public HashSet<int> AddedNotes { get; } = [];

    public IReadOnlyList<SyncPresence> Presence { get; set; } = [];

    public int TooLargeNotes { get; set; }

    public int UnreadableChanges { get; set; }
}

public sealed class SyncApiException(int statusCode, string code) : Exception($"동기화 서버 오류 {statusCode}: {code}")
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;

    public bool IsUnauthorized => StatusCode == 401;
}
