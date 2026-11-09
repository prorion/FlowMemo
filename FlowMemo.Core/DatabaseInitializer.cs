using Microsoft.Data.Sqlite;

using FlowMemo.Core.Migrations;
using FlowMemo.Core.Repositories;

namespace FlowMemo.Core;

public class DatabaseInitialiser
{
    public const int SchemaVersion = 6;

    /// <param name="migrationBackupDirectory">
    /// 지정하면 스키마를 올리기 직전에 이 폴더에 DB 사본을 만든다.
    /// 사본을 만들지 못하면 마이그레이션도 하지 않는다 (되돌릴 방법 없이 바꾸지 않는다).
    /// </param>
    public static async Task Initialise(string connectionString, string? migrationBackupDirectory = null)
    {
        using SqliteConnection connection = new(connectionString);
        connection.Open();

        if (!SchemaTableExists(connection))
        {
            await CreateDatabase(connection);
            return;
        }

        int databaseSchemaVersion = GetSchemaVersion(connection);
        if (databaseSchemaVersion > SchemaVersion)
            throw new DatabaseVersionException(databaseSchemaVersion, SchemaVersion);
        else if (databaseSchemaVersion < SchemaVersion)
        {
            if (migrationBackupDirectory is not null)
            {
                string databasePath = new SqliteConnectionStringBuilder(connectionString).DataSource;
                DatabaseBackup.CreateMigrationBackup(databasePath, migrationBackupDirectory, databaseSchemaVersion, DateTime.Now);
            }

            await UpdateDatabase(connection, databaseSchemaVersion);
        }
    }

    private static bool SchemaTableExists(SqliteConnection connection)
    {
        string query = @"
            SELECT name
            FROM sqlite_master
            WHERE
                type = 'table'
                AND name = 'SchemaInfo';
        ";

        using SqliteCommand command = new(query, connection);
        if (command.ExecuteScalar() == null)
            return false;

        return true;
    }

    private static int GetSchemaVersion(SqliteConnection connection)
    {
        string query = @"
            SELECT Version
            FROM SchemaInfo
            WHERE Id = 0;
        ";

        using SqliteCommand command = new(query, connection);
        object result = command.ExecuteScalar()
            ?? throw new Exception("Invalid database schema version.");

        int schemaVersion = Convert.ToInt32(result);

        return schemaVersion;
    }

    private static async Task CreateDatabase(SqliteConnection connection)
    {
        await BaseRepository.ExecuteNonQuery(
            connection,
            $@"
                CREATE TABLE IF NOT EXISTS SchemaInfo (
                    Id      INTEGER PRIMARY KEY AUTOINCREMENT,
                    Version INTEGER NOT NULL
                );
                INSERT INTO SchemaInfo (Id, Version) VALUES
                    (0, @schemaVersion);

                CREATE TABLE IF NOT EXISTS {AppMetadataRepository.TableName}
                    {AppMetadataRepository.TableSchema};
                INSERT INTO {AppMetadataRepository.TableName} DEFAULT VALUES;

                -- 설정은 키가 없으면 기본값을 쓰므로 행을 미리 넣지 않는다.
                CREATE TABLE IF NOT EXISTS {SettingsRepository.TableName}
                    {SettingsRepository.TableSchema};

                CREATE TABLE IF NOT EXISTS {NoteRepository.TableName}
                    {NoteRepository.TableSchema};

                {SyncRepository.CreateTablesSql}
            ",
            parameters: [
                new("@schemaVersion", SchemaVersion)
            ]
        );
    }

    private static async Task UpdateDatabase(SqliteConnection connection, int databaseSchemaVersion)
    {
        Schema1To2Migration schema1To2Migration = new();
        Schema2To3Migration schema2To3Migration = new();
        Schema3To4Migration schema3To4Migration = new();
        Schema4To5Migration schema4To5Migration = new();
        Schema5To6Migration schema5To6Migration = new();

        Dictionary<int, SchemaMigration> migrations = new()
        {
            {schema1To2Migration.TargetSchemaVersion, schema1To2Migration},
            {schema2To3Migration.TargetSchemaVersion, schema2To3Migration},
            {schema3To4Migration.TargetSchemaVersion, schema3To4Migration},
            {schema4To5Migration.TargetSchemaVersion, schema4To5Migration},
            {schema5To6Migration.TargetSchemaVersion, schema5To6Migration}
        };

        using SqliteTransaction transaction = connection.BeginTransaction();
        try
        {
            int currentSchemaVersion = databaseSchemaVersion;
            while (currentSchemaVersion < SchemaVersion)
            {
                SchemaMigration migration = migrations[currentSchemaVersion];
                await BaseRepository.ExecuteNonQuery(connection, migration.UpdateQuery);
                currentSchemaVersion = migration.ResultingSchemaVersion;
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
