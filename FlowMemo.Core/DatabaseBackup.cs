using System.Globalization;

using Microsoft.Data.Sqlite;

namespace FlowMemo.Core;

/// <summary>
/// DB 백업. 모든 메모가 SQLite 파일 하나에 있으므로, 파일이 깨지거나 실수로 지웠을 때를 대비한다.
/// - 하루 백업: backups\flowmemo-yyyyMMdd.sqlite, 하루에 하나, 최근 <see cref="DailyBackupsToKeep"/>개만 남긴다.
/// - 마이그레이션 직전 백업: backups\flowmemo-before-v{버전}-yyyyMMdd-HHmmss.sqlite, 최근 3개.
/// SQLite 온라인 백업 API를 써서, 앱이 DB를 쓰는 중에도 일관된 사본을 만든다.
/// 되돌리기: FlowMemo를 끝내고 백업 파일을 flowmemo.sqlite로 이름을 바꿔 덮어쓴다.
/// </summary>
public static class DatabaseBackup
{
    public const string FolderName = "backups";
    public const int DailyBackupsToKeep = 7;
    public const int MigrationBackupsToKeep = 3;

    private const string DailyPrefix = "flowmemo-";
    private const string MigrationPrefix = "flowmemo-before-v";

    /// <summary>오늘 백업이 없으면 만든다. 만들었으면 그 경로를, 이미 있거나 DB가 없으면 null을 돌려준다.</summary>
    public static string? CreateDailyBackup(string databasePath, string backupDirectory, DateTime now)
    {
        if (!File.Exists(databasePath))
            return null;

        Directory.CreateDirectory(backupDirectory);

        string path = Path.Combine(backupDirectory, $"{DailyPrefix}{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.sqlite");
        if (File.Exists(path))
            return null;

        Copy(databasePath, path);
        DeleteOldest(backupDirectory, IsDailyBackup, DailyBackupsToKeep);

        return path;
    }

    /// <summary>스키마를 올리기 직전에 부른다. 마이그레이션이 실패하거나 새 버전이 마음에 안 들 때 되돌릴 수 있다.</summary>
    public static string CreateMigrationBackup(string databasePath, string backupDirectory, int fromVersion, DateTime now)
    {
        Directory.CreateDirectory(backupDirectory);

        string path = Path.Combine(
            backupDirectory,
            $"{MigrationPrefix}{fromVersion}-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.sqlite"
        );

        Copy(databasePath, path);
        DeleteOldest(backupDirectory, name => name.StartsWith(MigrationPrefix, StringComparison.OrdinalIgnoreCase), MigrationBackupsToKeep);

        return path;
    }

    // flowmemo-20261107.sqlite (날짜 8자리만)
    private static bool IsDailyBackup(string fileName)
        => fileName.Length == DailyPrefix.Length + 8 + ".sqlite".Length
        && fileName.StartsWith(DailyPrefix, StringComparison.OrdinalIgnoreCase)
        && fileName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
        && fileName.AsSpan(DailyPrefix.Length, 8).ToString().All(char.IsAsciiDigit);

    private static void Copy(string sourcePath, string destinationPath)
    {
        string temporaryPath = destinationPath + ".tmp";
        if (File.Exists(temporaryPath))
            File.Delete(temporaryPath);

        // 풀링을 끄지 않으면 백업 파일 핸들이 열린 채로 남아 이름 바꾸기·삭제가 실패한다.
        using (SqliteConnection source = new($"Data Source={sourcePath};Mode=ReadOnly;Pooling=False"))
        using (SqliteConnection destination = new($"Data Source={temporaryPath};Pooling=False"))
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }

        // 다 쓴 뒤에 이름을 바꿔서, 백업 도중에 꺼져도 반쯤 쓴 파일이 백업처럼 보이지 않게 한다.
        File.Move(temporaryPath, destinationPath, overwrite: true);
    }

    private static void DeleteOldest(string directory, Func<string, bool> match, int keep)
    {
        // 파일 이름에 날짜가 들어 있어서 이름순이 곧 시간순이다.
        string[] old = [.. Directory.GetFiles(directory, "*.sqlite")
            .Where(path => match(Path.GetFileName(path)))
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Skip(keep)];

        foreach (string path in old)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // 다른 프로그램이 열고 있으면 다음에 지운다.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
