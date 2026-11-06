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

        ConnectionString = $"Data Source={Path.Combine(DataPath, "flowmemo.sqlite")}";
    }
}
