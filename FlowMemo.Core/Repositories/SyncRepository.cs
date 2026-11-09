using Microsoft.Data.Sqlite;

using FlowMemo.Core.Configurations;
using FlowMemo.Core.Sync;

namespace FlowMemo.Core.Repositories;

/// <summary>
/// 메모별 동기화 상태.
/// - Base*: 마지막으로 서버와 맞춘 내용. 지금 메모와 다르면 이 PC에서 고친 것이다 (합치기의 기준이기도 하다).
/// - Pending*: 다른 PC에서 받은 변경. 사용자가 결정할 때까지 메모 내용은 바꾸지 않고 여기에만 둔다.
/// </summary>
public class SyncRepository(DatabaseConfiguration databaseConfiguration) : BaseRepository(databaseConfiguration)
{
    public const string NewSyncIdSql = "lower(hex(randomblob(16)))";

    public const string CreateTablesSql = $@"
        CREATE TABLE NoteSync (
            NoteId          INTEGER PRIMARY KEY,
            SyncId          TEXT    NOT NULL UNIQUE,

            BaseRevision    INTEGER NOT NULL DEFAULT 0,
            BaseContent     TEXT,
            BaseColour      TEXT,
            BaseDeleted     INTEGER NOT NULL DEFAULT 0,

            PendingRevision INTEGER NOT NULL DEFAULT 0,
            PendingContent  TEXT,
            PendingColour   TEXT,
            PendingDeleted  INTEGER NOT NULL DEFAULT 0,
            PendingDevice   TEXT,
            PendingAt       TEXT,

            IgnoredRevision INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE SyncState (
            Key     TEXT PRIMARY KEY NOT NULL,
            Value   TEXT NOT NULL
        );

        CREATE TRIGGER Notes_CreateSyncRow AFTER INSERT ON Notes
        BEGIN
            INSERT INTO NoteSync (NoteId, SyncId) VALUES (new.Id, {NewSyncIdSql});
        END;
    ";

    private const string CursorKey = "Cursor";

    private const string SelectRows = @"
        SELECT s.*, n.Id AS NoteRowId, n.Content, n.ThemeColourScheme, n.DeletedAt
        FROM NoteSync s
        LEFT JOIN Notes n ON n.Id = s.NoteId
    ";

    public async Task<IReadOnlyList<NoteSyncRow>> GetAll()
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        using SqliteDataReader reader = await ExecuteReader(connection, SelectRows + " ORDER BY s.NoteId;");

        List<NoteSyncRow> rows = [];
        while (reader.Read())
            rows.Add(ReadRow(reader));
        return rows;
    }

    public Task<NoteSyncRow?> GetByNoteId(int noteId)
        => GetOne(SelectRows + " WHERE s.NoteId = @value;", noteId);

    public Task<NoteSyncRow?> GetBySyncId(string syncId)
        => GetOne(SelectRows + " WHERE s.SyncId = @value;", syncId);

    private async Task<NoteSyncRow?> GetOne(string query, object value)
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        using SqliteDataReader reader = await ExecuteReader(connection, query, [new("@value", value)]);
        return reader.Read() ? ReadRow(reader) : null;
    }

    private static NoteSyncRow ReadRow(SqliteDataReader reader)
        => new(
            GetInt(reader, "NoteId"),
            GetString(reader, "SyncId"),
            GetLong(reader, "BaseRevision"),
            GetStringNullable(reader, "BaseContent"),
            GetStringNullable(reader, "BaseColour"),
            GetBool(reader, "BaseDeleted"),
            GetLong(reader, "PendingRevision"),
            GetStringNullable(reader, "PendingContent"),
            GetStringNullable(reader, "PendingColour"),
            GetBool(reader, "PendingDeleted"),
            GetStringNullable(reader, "PendingDevice"),
            GetUtcDateTimeNullable(reader, "PendingAt"),
            GetLong(reader, "IgnoredRevision"),
            GetIntNullable(reader, "NoteRowId") is not null,
            GetStringNullable(reader, "Content") ?? "",
            GetStringNullable(reader, "ThemeColourScheme"),
            GetStringNullable(reader, "DeletedAt") is not null
        );

    /// <summary>서버와 맞춘 상태를 기록하고 대기 중인 변경을 지운다.</summary>
    public Task SetBase(int noteId, long revision, string? content, string? colour, bool deleted)
        => Execute(
            @"
                UPDATE NoteSync
                SET BaseRevision = @revision, BaseContent = @content, BaseColour = @colour, BaseDeleted = @deleted,
                    PendingRevision = 0, PendingContent = NULL, PendingColour = NULL, PendingDeleted = 0,
                    PendingDevice = NULL, PendingAt = NULL, IgnoredRevision = 0
                WHERE NoteId = @noteId;
            ",
            [new("@revision", revision), new("@content", content), new("@colour", colour), new("@deleted", deleted), new("@noteId", noteId)]
        );

    public Task SetPending(int noteId, long revision, string? content, string? colour, bool deleted, string device, DateTime? at)
        => Execute(
            @"
                UPDATE NoteSync
                SET PendingRevision = @revision, PendingContent = @content, PendingColour = @colour,
                    PendingDeleted = @deleted, PendingDevice = @device, PendingAt = @at
                WHERE NoteId = @noteId;
            ",
            [new("@revision", revision), new("@content", content), new("@colour", colour), new("@deleted", deleted),
             new("@device", device), new("@at", FormatUtc(at)), new("@noteId", noteId)]
        );

    /// <summary>
    /// 대기 중인 변경을 '기준'으로 삼는다. 메모를 받은 내용으로 바꾼 뒤 부르면 가져오기,
    /// 메모를 그대로 두고 부르면 이 PC 내용으로 덮어쓰기(다음 동기화 때 올라간다)가 된다.
    /// </summary>
    public Task CommitPending(int noteId)
        => Execute(
            @"
                UPDATE NoteSync
                SET BaseRevision = PendingRevision, BaseContent = PendingContent, BaseColour = PendingColour,
                    BaseDeleted = PendingDeleted,
                    PendingRevision = 0, PendingContent = NULL, PendingColour = NULL, PendingDeleted = 0,
                    PendingDevice = NULL, PendingAt = NULL, IgnoredRevision = 0
                WHERE NoteId = @noteId AND PendingRevision > 0;
            ",
            [new("@noteId", noteId)]
        );

    /// <summary>이번 변경은 건너뛴다. 이 PC 메모도 서버도 바뀌지 않고, 더 새로운 변경이 오면 다시 알린다.</summary>
    public Task Ignore(int noteId)
        => Execute(
            "UPDATE NoteSync SET IgnoredRevision = PendingRevision WHERE NoteId = @noteId;",
            [new("@noteId", noteId)]
        );

    public Task UpdateColour(int noteId, string colour)
        => Execute(
            "UPDATE Notes SET ThemeColourScheme = @colour WHERE Id = @noteId;",
            [new("@colour", colour), new("@noteId", noteId)]
        );

    public Task RemoveRow(int noteId)
        => Execute("DELETE FROM NoteSync WHERE NoteId = @noteId;", [new("@noteId", noteId)]);

    /// <summary>다른 PC에서 만든 메모를 닫힌 메모로 추가한다.</summary>
    public async Task<int> InsertRemoteNote(string syncId, long revision, SyncNotePayload payload, double x, double y, double width, double height)
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        await ExecuteNonQuery(
            connection,
            @"
                INSERT INTO Notes (Content, X, Y, Width, Height, GravityX, GravityY, ThemeColourScheme, IsPinned, IsOpen, IsLocked, CreatedAt, UpdatedAt)
                VALUES (@content, @x, @y, @width, @height, 1, 1, @colour, 0, 0, 0, @createdAt, @updatedAt);
            ",
            [new("@content", payload.Content), new("@x", x), new("@y", y), new("@width", width), new("@height", height),
             new("@colour", payload.Colour), new("@createdAt", FormatUtc(payload.CreatedAt)), new("@updatedAt", FormatUtc(payload.UpdatedAt))]
        );
        int noteId = await GetLastInsertRowId(connection);

        // 트리거가 만든 임시 ID를 다른 PC와 같은 ID로 바꾼다.
        await ExecuteNonQuery(
            connection,
            @"
                UPDATE NoteSync
                SET SyncId = @syncId, BaseRevision = @revision, BaseContent = @content, BaseColour = @colour
                WHERE NoteId = @noteId;
            ",
            [new("@syncId", syncId), new("@revision", revision), new("@content", payload.Content), new("@colour", payload.Colour), new("@noteId", noteId)]
        );

        transaction.Commit();
        return noteId;
    }

    /// <summary>휴지통의 메모를 다른 PC에서 고쳤을 때: 수정이 삭제보다 우선이므로 닫힌 메모로 되살린다.</summary>
    public async Task RestoreFromRemote(int noteId, long revision, SyncNotePayload payload)
    {
        await Execute(
            @"
                UPDATE Notes
                SET Content = @content, ThemeColourScheme = @colour, UpdatedAt = @updatedAt, DeletedAt = NULL, IsOpen = 0
                WHERE Id = @noteId;
            ",
            [new("@content", payload.Content), new("@colour", payload.Colour), new("@updatedAt", FormatUtc(payload.UpdatedAt)), new("@noteId", noteId)]
        );
        await SetBase(noteId, revision, payload.Content, payload.Colour, deleted: false);
    }

    public async Task<long> GetCursor()
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        object? value = await ExecuteScalar(connection, $"SELECT Value FROM SyncState WHERE Key = '{CursorKey}';");
        return value is string text && long.TryParse(text, out long cursor) ? cursor : 0;
    }

    public Task SetCursor(long cursor)
        => Execute(
            $@"
                INSERT INTO SyncState (Key, Value) VALUES ('{CursorKey}', @value)
                ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value;
            ",
            [new("@value", cursor.ToString(System.Globalization.CultureInfo.InvariantCulture))]
        );

    /// <summary>연결을 끊을 때: 메모는 그대로 두고 서버와 맞춘 기록만 지운다.</summary>
    public Task ResetSyncState()
        => Execute(
            @"
                DELETE FROM NoteSync WHERE NoteId NOT IN (SELECT Id FROM Notes);
                UPDATE NoteSync
                SET BaseRevision = 0, BaseContent = NULL, BaseColour = NULL, BaseDeleted = 0,
                    PendingRevision = 0, PendingContent = NULL, PendingColour = NULL, PendingDeleted = 0,
                    PendingDevice = NULL, PendingAt = NULL, IgnoredRevision = 0;
                DELETE FROM SyncState;
            "
        );

    private async Task Execute(string query, IEnumerable<KeyValuePair<string, object?>>? parameters = null)
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        await ExecuteNonQuery(connection, query, parameters);
    }
}
