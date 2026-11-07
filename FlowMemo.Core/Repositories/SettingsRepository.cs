using Microsoft.Data.Sqlite;

using FlowMemo.Core.Configurations;

namespace FlowMemo.Core.Repositories;

/// <summary>
/// 설정을 키/값으로 저장한다 (스키마 v5).
/// 예전(v4)에는 열 62개짜리 한 행이라, 설정 하나를 추가할 때마다 마이그레이션과
/// DTO·저장소·서비스를 함께 고쳐야 했다. 이제 키가 없으면 기본값을 쓰므로 마이그레이션이 필요 없다.
/// 키는 "구역.속성이름" (예: "Notes.TransparencyMode")이고, 값 형식은 <see cref="SettingValueConverter"/>를 따른다.
/// </summary>
public class SettingsRepository(DatabaseConfiguration databaseConfiguration) : BaseRepository(databaseConfiguration)
{
    public static readonly string TableName = "AppSettings";

    public static readonly string TableSchema = @"
        (
            Key     TEXT PRIMARY KEY NOT NULL,
            Value   TEXT
        )
    ";

    public async Task<IReadOnlyDictionary<string, string>> GetAll()
    {
        Dictionary<string, string> settings = new(StringComparer.Ordinal);

        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        using SqliteDataReader reader = await ExecuteReader(
            connection,
            @"
                SELECT Key, Value
                FROM AppSettings
                WHERE Value IS NOT NULL;
            "
        );

        while (reader.Read())
            settings[reader.GetString(0)] = reader.GetString(1);

        return settings;
    }

    /// <summary>주어진 키만 덮어쓴다. 모르는 키(새 버전에서 추가한 설정 등)는 지우지 않고 그대로 둔다.</summary>
    public async Task SaveAll(IEnumerable<KeyValuePair<string, string>> settings)
    {
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();

        using SqliteTransaction transaction = connection.BeginTransaction();

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO AppSettings (Key, Value)
            VALUES (@key, @value)
            ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value;
        ";
        SqliteParameter key = command.Parameters.Add("@key", SqliteType.Text);
        SqliteParameter value = command.Parameters.Add("@value", SqliteType.Text);

        foreach (KeyValuePair<string, string> setting in settings)
        {
            key.Value = setting.Key;
            value.Value = setting.Value;
            await command.ExecuteNonQueryAsync();
        }

        transaction.Commit();
    }
}
