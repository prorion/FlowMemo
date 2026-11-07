namespace FlowMemo.Core.Configurations;

public class DatabaseConfiguration
{
    public readonly string ConnectionString;

    /// <summary>DB와 로그가 저장되는 폴더.</summary>
    public readonly string DataPath;

    public DatabaseConfiguration()
    {
        // Use exe dir for database if in Debug mode or is portable.
        if (System.Diagnostics.Debugger.IsAttached || File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt")))
            DataPath = AppContext.BaseDirectory;
        else
            DataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FlowMemo"
            );

        if (!Path.Exists(DataPath))
            Directory.CreateDirectory(DataPath);

        DatabasePath = Path.Combine(DataPath, DatabaseFileName);
        ConnectionString = $"Data Source={DatabasePath}";
    }

    /// <summary>지정한 폴더를 쓴다 (테스트용).</summary>
    public DatabaseConfiguration(string dataPath)
    {
        DataPath = dataPath;
        Directory.CreateDirectory(DataPath);

        DatabasePath = Path.Combine(DataPath, DatabaseFileName);
        ConnectionString = $"Data Source={DatabasePath};Pooling=False";
    }

    public const string DatabaseFileName = "flowmemo.sqlite";

    public readonly string DatabasePath;

    /// <summary>자동 백업 폴더.</summary>
    public string BackupPath => Path.Combine(DataPath, DatabaseBackup.FolderName);
}
