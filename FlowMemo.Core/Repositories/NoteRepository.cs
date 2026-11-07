using Microsoft.Data.Sqlite;

using FlowMemo.Core.Configurations;
using FlowMemo.Core.DataTransferObjects;

namespace FlowMemo.Core.Repositories;

public class NoteRepository(DatabaseConfiguration databaseConfiguration) : BaseRepository(databaseConfiguration)
{
    public static readonly string TableName = "Notes";

    /// <summary>휴지통에 넣은 메모를 이 기간이 지나면 시작할 때 완전히 지운다.</summary>
    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(30);

    public static readonly string TableSchema = @"
        (
            Id                  INTEGER PRIMARY KEY AUTOINCREMENT,

            Content             TEXT,

            X                   REAL,
            Y                   REAL,
            Width               REAL,
            Height              REAL,

            GravityX            INTEGER,
            GravityY            INTEGER,

            ThemeColourScheme   TEXT,

            IsPinned            INTEGER,
            IsOpen              INTEGER,

            IsLocked            INTEGER DEFAULT 0,
            CreatedAt           TEXT    DEFAULT NULL,
            UpdatedAt           TEXT    DEFAULT NULL,
            DeletedAt           TEXT    DEFAULT NULL
        )
    ";

    public async Task<int> Create(NoteDto note)
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        await ExecuteNonQuery(
            connection,
            @"
                INSERT INTO Notes
                (
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
                    UpdatedAt
                )
                VALUES
                (
                    @content,

                    @x,
                    @y,
                    @width,
                    @height,

                    @gravityX,
                    @gravityY,

                    @themeColourScheme,

                    @isPinned,
                    @isOpen,

                    @isLocked,
                    @createdAt,
                    @updatedAt
                );
            ",
            parameters: [
                new("@content", note.Content),

                new("@x", note.X),
                new("@y", note.Y),
                new("@width", note.Width),
                new("@height", note.Height),

                new("@gravityX", note.GravityX),
                new("@gravityY", note.GravityY),

                new("@themeColourScheme", note.ThemeColourScheme),

                new("@isPinned", note.IsPinned),
                new("@isOpen", note.IsOpen),

                new("@isLocked", note.IsLocked),
                new("@createdAt", FormatUtc(note.CreatedAt)),
                new("@updatedAt", FormatUtc(note.UpdatedAt))
            ]
        );

        int newId = await GetLastInsertRowId(connection);

        return newId;
    }

    /// <summary>휴지통에 있는 메모도 돌려준다.</summary>
    public async Task<NoteDto> GetById(int id)
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        using SqliteDataReader reader = await ExecuteReader(
            connection,
            @"
                SELECT *
                FROM Notes
                WHERE Id = @id;
            ",
            parameters: [
                new("@id", id)
            ]
        );
        if (!reader.Read())
            throw new Exception($"Could not find note with id: {id}.");

        NoteDto dto = GetNoteDtoFromReader(reader);

        return dto;
    }

    /// <summary>휴지통에 없는 메모.</summary>
    public Task<IEnumerable<NoteDto>> GetAll()
        => Query(@"
            SELECT *
            FROM Notes
            WHERE DeletedAt IS NULL
            ORDER BY Id;
        ");

    /// <summary>휴지통에 있는 메모. 최근에 지운 것부터.</summary>
    public Task<IEnumerable<NoteDto>> GetDeleted()
        => Query(@"
            SELECT *
            FROM Notes
            WHERE DeletedAt IS NOT NULL
            ORDER BY DeletedAt DESC, Id DESC;
        ");

    /// <summary>다음 시작 때 다시 열 메모.</summary>
    public Task<IEnumerable<NoteDto>> GetOpen()
        => Query(@"
            SELECT *
            FROM Notes
            WHERE IsOpen = 1 AND DeletedAt IS NULL
            ORDER BY Id;
        ");

    public async Task<int> CountDeleted()
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        return Convert.ToInt32(
            await ExecuteScalar(connection, "SELECT COUNT(*) FROM Notes WHERE DeletedAt IS NOT NULL;")
        );
    }

    /// <summary>
    /// 메모 창에서 바뀌는 값만 저장한다. 만든 시각과 휴지통 상태(DeletedAt)는 건드리지 않는다
    /// (휴지통으로 옮긴 직후에 창이 저장해도 다시 살아나지 않게).
    /// </summary>
    public async Task Update(NoteDto note)
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        int affectedRows = await ExecuteNonQuery(
            connection,
            @"
                UPDATE Notes
                SET
                    Content = @content,

                    X = @x,
                    Y = @y,
                    Width = @width,
                    Height = @height,

                    GravityX = @gravityX,
                    GravityY = @gravityY,

                    ThemeColourScheme = @themeColourScheme,

                    IsPinned = @isPinned,
                    IsOpen = @isOpen,

                    IsLocked = @isLocked,
                    UpdatedAt = @updatedAt
                WHERE Id = @id;
            ",
            parameters: [
                new("@content", note.Content),

                new("@x", note.X),
                new("@y", note.Y),
                new("@width", note.Width),
                new("@height", note.Height),

                new("@gravityX", note.GravityX),
                new("@gravityY", note.GravityY),

                new("@themeColourScheme", note.ThemeColourScheme),

                new("@isPinned", note.IsPinned),
                new("@isOpen", note.IsOpen),

                new("@isLocked", note.IsLocked),
                new("@updatedAt", FormatUtc(note.UpdatedAt)),

                new("@id", note.Id)
            ]
        );

        if (affectedRows == 0)
            throw new Exception($"Error updating note width id {note.Id}.");
    }

    /// <summary>휴지통으로 옮긴다. 다음 시작 때 다시 열리지 않게 IsOpen도 끈다.</summary>
    public async Task MoveToTrash(int id, DateTime deletedAtUtc)
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        await ExecuteNonQuery(
            connection,
            @"
                UPDATE Notes
                SET DeletedAt = @deletedAt, IsOpen = 0
                WHERE Id = @id;
            ",
            parameters: [
                new("@deletedAt", FormatUtc(deletedAtUtc)),
                new("@id", id)
            ]
        );
    }

    public async Task Restore(int id)
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        await ExecuteNonQuery(
            connection,
            @"
                UPDATE Notes
                SET DeletedAt = NULL
                WHERE Id = @id;
            ",
            parameters: [
                new("@id", id)
            ]
        );
    }

    /// <summary>완전히 지운다. 되돌릴 수 없다.</summary>
    public async Task Delete(int id)
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        await ExecuteNonQuery(
            connection,
            @"
                DELETE FROM Notes
                WHERE Id = @id;
            ",
            parameters: [
                new("@id", id)
            ]
        );
    }

    /// <summary>휴지통을 비운다. 지운 메모 수를 돌려준다.</summary>
    public async Task<int> EmptyTrash()
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        return await ExecuteNonQuery(connection, "DELETE FROM Notes WHERE DeletedAt IS NOT NULL;");
    }

    /// <summary>휴지통에 넣은 지 <paramref name="retention"/>이 지난 메모를 완전히 지운다.</summary>
    public async Task<int> PurgeTrash(DateTime nowUtc, TimeSpan retention)
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        return await ExecuteNonQuery(
            connection,
            @"
                DELETE FROM Notes
                WHERE DeletedAt IS NOT NULL AND DeletedAt < @cutoff;
            ",
            parameters: [
                new("@cutoff", FormatUtc(nowUtc - retention))
            ]
        );
    }

    private async Task<IEnumerable<NoteDto>> Query(string query)
    {
        List<NoteDto> notes = [];

        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        using SqliteDataReader reader = await ExecuteReader(connection, query);

        while (reader.Read())
            notes.Add(GetNoteDtoFromReader(reader));

        return notes;
    }

    private static NoteDto GetNoteDtoFromReader(SqliteDataReader reader)
    {
        NoteDto noteDto = new(
            GetInt(reader, "Id"),

            GetStringNullable(reader, "Content") ?? "",

            GetDouble(reader, "X"),
            GetDouble(reader, "Y"),
            GetDouble(reader, "Width"),
            GetDouble(reader, "Height"),

            GetInt(reader, "GravityX"),
            GetInt(reader, "GravityY"),

            GetString(reader, "ThemeColourScheme"),

            GetBool(reader, "IsPinned"),
            GetBool(reader, "IsOpen"),

            GetBoolNullable(reader, "IsLocked") ?? false,
            GetUtcDateTimeNullable(reader, "CreatedAt"),
            GetUtcDateTimeNullable(reader, "UpdatedAt"),
            GetUtcDateTimeNullable(reader, "DeletedAt")
        );

        return noteDto;
    }
}
