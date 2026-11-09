using System.Globalization;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

using FlowMemo.Core;
using FlowMemo.Core.Configurations;
using FlowMemo.Core.DataTransferObjects;
using FlowMemo.Core.Enums;
using FlowMemo.Core.Migrations;
using FlowMemo.Core.Repositories;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.Services;

/// <summary>
/// DB 생성, v4 → v5 마이그레이션, 설정 키/값 저장, 휴지통, 백업, '변경됨' 표시를 임시 DB로 검사한다.
/// 창을 띄우지 않는다.
/// </summary>
internal static class Data
{
    private static void Check(string n, bool ok, string d = "") => Program.Check(n, ok, d);

    public static void Run(string workDir)
    {
        Console.WriteLine("== 데이터 테스트 (DB 생성 · v4→v5 마이그레이션 · 설정 · 휴지통 · 백업 · 저장 표시)");

        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);
        Directory.CreateDirectory(workDir);
        ErrorReporter.Initialize(workDir);

        FreshDatabase(Path.Combine(workDir, "fresh"));
        MigrateDefaultsFromV4(Path.Combine(workDir, "v4-defaults"));
        MigrateCustomFromV4(Path.Combine(workDir, "v4-custom"));
        SettingsRoundTrip(Path.Combine(workDir, "settings"));
        NotesAndTrash(Path.Combine(workDir, "notes"));
        Backups(Path.Combine(workDir, "backup"));
        NewerDatabaseIsRefused(Path.Combine(workDir, "newer"));
        DirtyTracking();
    }

    /// <summary>
    /// 실제로 쓰던 DB의 사본으로 마이그레이션을 확인한다 (원본은 읽기 전용으로만 연다).
    /// 메모 내용은 출력하지 않고, 마이그레이션 전후의 해시만 비교한다.
    /// 사용: FlowMemo.Tests.exe migrate-copy "%APPDATA%\FlowMemo\flowmemo.sqlite" 작업폴더
    /// </summary>
    public static void MigrateCopy(string sourceDatabase, string workDir)
    {
        Console.WriteLine("== 실제 DB 사본으로 마이그레이션 확인 (내용은 출력하지 않음)");
        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);
        Directory.CreateDirectory(workDir);
        ErrorReporter.Initialize(workDir);

        DatabaseConfiguration config = new(workDir);
        using (SqliteConnection source = new($"Data Source={sourceDatabase};Mode=ReadOnly;Pooling=False"))
        using (SqliteConnection copy = new($"Data Source={config.DatabasePath};Pooling=False"))
        {
            source.Open();
            copy.Open();
            source.BackupDatabase(copy);
        }

        int version = Convert.ToInt32(Scalar(config.ConnectionString, "SELECT Version FROM SchemaInfo WHERE Id = 0"));
        Console.WriteLine($"info  원본 스키마 v{version}");

        Dictionary<string, string?> before = [];
        if (version == 4)
        {
            foreach ((string key, string column) in Schema4To5Migration.SettingsColumnMap)
                before[key] = Scalar(config.ConnectionString, $"SELECT CAST({column} AS TEXT) FROM Settings WHERE Id = 1") as string;
        }
        (int count, string hash) notesBefore = NotesFingerprint(config.ConnectionString);
        Console.WriteLine($"info  메모 {notesBefore.count}개");

        Run(DatabaseInitialiser.Initialise(config.ConnectionString, config.BackupPath));
        Check("사본: 최신 스키마로 올라감", Convert.ToInt32(Scalar(config.ConnectionString, "SELECT Version FROM SchemaInfo WHERE Id = 0")) == DatabaseInitialiser.SchemaVersion);
        Check("사본: 메모 내용·위치·색·고정·열림이 그대로 (해시 비교)", NotesFingerprint(config.ConnectionString) == notesBefore);

        SettingsService settings = LoadSettings(config);
        Dictionary<string, string> after = Snapshot(settings);
        List<string> mismatched = [];
        int changedFromDefault = 0;
        Dictionary<string, string> defaults = Snapshot(new SettingsService(null!));
        foreach ((string key, BaseModel _, System.Reflection.PropertyInfo property) in settings.GetSettingProperties())
        {
            if (after[key] != defaults[key])
                changedFromDefault++;
            if (!before.TryGetValue(key, out string? raw) || raw is null)
                continue;
            string expected = SettingValueConverter.TryParse(raw, property.PropertyType, out object? value)
                ? SettingValueConverter.Format(value!)
                : defaults[key];
            if (after[key] != expected)
                mismatched.Add($"{key}: {expected} → {after[key]}");
        }
        Check($"사본: 설정 {before.Count}개 값이 그대로 (기본값과 다른 설정 {changedFromDefault}개)", version != 4 || (before.Count == 62 && mismatched.Count == 0), string.Join(", ", mismatched));
        Check("사본: 마이그레이션 직전 백업", version == DatabaseInitialiser.SchemaVersion || (Directory.Exists(config.BackupPath) && Directory.GetFiles(config.BackupPath, "flowmemo-before-v*.sqlite").Length == 1));
        Check("사본: 오류 기록 없음", !File.Exists(ErrorReporter.LogFilePath), File.Exists(ErrorReporter.LogFilePath) ? File.ReadAllText(ErrorReporter.LogFilePath) : "");
    }

    private static (int count, string hash) NotesFingerprint(string connectionString)
    {
        using SqliteConnection c = new(connectionString);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Content, X, Y, Width, Height, GravityX, GravityY, ThemeColourScheme, IsPinned, IsOpen FROM Notes ORDER BY Id";
        using SqliteDataReader r = cmd.ExecuteReader();
        using System.Security.Cryptography.IncrementalHash sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        int count = 0;
        while (r.Read())
        {
            count++;
            for (int i = 0; i < r.FieldCount; i++)
                sha.AppendData(System.Text.Encoding.UTF8.GetBytes((r.IsDBNull(i) ? "<null>" : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)) + "\u001f"));
        }
        return (count, Convert.ToHexString(sha.GetHashAndReset()));
    }

    // ---------------------------------------------------------------- 도우미

    private static T Run<T>(Task<T> task) => task.GetAwaiter().GetResult();
    private static void Run(Task task) => task.GetAwaiter().GetResult();

    private static object? Scalar(string connectionString, string sql)
    {
        using SqliteConnection c = new(connectionString);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static void Exec(string connectionString, string sql)
    {
        using SqliteConnection c = new(connectionString);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static bool TableExists(string connectionString, string table)
        => Scalar(connectionString, $"SELECT name FROM sqlite_master WHERE type = 'table' AND name = '{table}'") is not null;

    private static HashSet<string> Columns(string connectionString, string table)
    {
        HashSet<string> columns = [];
        using SqliteConnection c = new(connectionString);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
            columns.Add(r.GetString(1));
        return columns;
    }

    private static SettingsService LoadSettings(DatabaseConfiguration config)
    {
        SettingsService service = new(new SettingsRepository(config));
        Run(service.Load());
        return service;
    }

    /// <summary>설정 전체를 "키=값" 사전으로 (DB에 저장하는 형식).</summary>
    private static Dictionary<string, string> Snapshot(SettingsService service)
        => service.GetSettingProperties().ToDictionary(
            s => s.Key,
            s => SettingValueConverter.Format(s.Property.GetValue(s.Model)!));

    private static string Diff(Dictionary<string, string> a, Dictionary<string, string> b)
        => string.Join(", ", a.Keys.Union(b.Keys)
            .Where(k => !a.TryGetValue(k, out string? va) || !b.TryGetValue(k, out string? vb) || va != vb)
            .Select(k => $"{k}: {a.GetValueOrDefault(k, "<없음>")} / {b.GetValueOrDefault(k, "<없음>")}"));

    private static NoteDto NewNote(string content, bool isOpen = true, bool isLocked = false, DateTime? at = null)
    {
        DateTime now = at ?? DateTime.UtcNow;
        return new NoteDto(0, content, 100, 100, 300, 300, 1, 1, "Yellow", false, isOpen, isLocked, now, now, null);
    }

    /// <summary>
    /// FlowMemo 1.x(Pinny Notes 1.17) / 2.0 개발 중의 스키마 v4. 마이그레이션 시험용으로 그대로 옮겨 둔다.
    /// </summary>
    private const string SchemaV4 = @"
        CREATE TABLE SchemaInfo (Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL);
        INSERT INTO SchemaInfo (Id, Version) VALUES (0, 4);

        CREATE TABLE ApplicationData (Id INTEGER PRIMARY KEY AUTOINCREMENT, LastUpdateCheck INTEGER DEFAULT NULL, ColourScheme TEXT DEFAULT NULL);
        INSERT INTO ApplicationData DEFAULT VALUES;

        CREATE TABLE Settings (
            Id  INTEGER PRIMARY KEY AUTOINCREMENT,
            Application_StartupBehaviour        INTEGER DEFAULT 0,
            Application_NewInstanceBehaviour    INTEGER DEFAULT 0,
            Application_ShowTrayIcon            INTEGER DEFAULT 1,
            Application_CheckForUpdates         INTEGER DEFAULT 0,
            Notes_DefaultWidth                  INTEGER DEFAULT 300,
            Notes_DefaultHeight                 INTEGER DEFAULT 300,
            Notes_StartupPosition               INTEGER DEFAULT 0,
            Notes_PinnedByDefault               INTEGER DEFAULT 0,
            Notes_MinimizeMode                  INTEGER DEFAULT 0,
            Notes_VisibilityMode                INTEGER DEFAULT 0,
            Notes_HideTitleBar                  INTEGER DEFAULT 0,
            Notes_CycleColours                  INTEGER DEFAULT 1,
            Notes_ColourMode                    INTEGER DEFAULT 0,
            Notes_TransparencyMode              INTEGER DEFAULT 2,
            Notes_OpaqueWhenFocused             INTEGER DEFAULT 1,
            Notes_OpaqueOpacity                 REAL    DEFAULT 1.0,
            Notes_TransparentOpacity            REAL    DEFAULT 0.8,
            Editor_SpellCheck                   INTEGER DEFAULT 1,
            Editor_AutoIndent                   INTEGER DEFAULT 1,
            Editor_NewLineAtEnd                 INTEGER DEFAULT 1,
            Editor_KeepNewLineVisible           INTEGER DEFAULT 1,
            Editor_WrapText                     INTEGER DEFAULT 1,
            Editor_StandardFontFamily           TEXT    DEFAULT 'Segoe UI',
            Editor_MonoFontFamily               TEXT    DEFAULT 'Consolas',
            Editor_UseMonoFont                  INTEGER DEFAULT 0,
            Editor_TabUsesSpaces                INTEGER DEFAULT 0,
            Editor_ConvertIndentationOnPaste    INTEGER DEFAULT 0,
            Editor_TabWidth                     INTEGER DEFAULT 4,
            Editor_CopyAction                   INTEGER DEFAULT 1,
            Editor_TrimTextOnCopy               INTEGER DEFAULT 0,
            Editor_CopyAltAction                INTEGER DEFAULT 1,
            Editor_TrimTextOnAltCopy            INTEGER DEFAULT 1,
            Editor_CopyFallbackAction           INTEGER DEFAULT 1,
            Editor_TrimTextOnFallbackCopy       INTEGER DEFAULT 0,
            Editor_CopyAltFallbackAction        INTEGER DEFAULT 2,
            Editor_TrimTextOnAltFallbackCopy    INTEGER DEFAULT 0,
            Editor_CopyTextOnHighlight          INTEGER DEFAULT 0,
            Editor_PasteAction                  INTEGER DEFAULT 1,
            Editor_TrimTextOnPaste              INTEGER DEFAULT 0,
            Editor_PasteAltAction               INTEGER DEFAULT 1,
            Editor_TrimTextOnAltPaste           INTEGER DEFAULT 1,
            Editor_MiddleClickPaste             INTEGER DEFAULT 1,
            Tool_Base64State                    INTEGER DEFAULT 1,
            Tool_BracketState                   INTEGER DEFAULT 1,
            Tool_CaseState                      INTEGER DEFAULT 1,
            Tool_ColourState                    INTEGER DEFAULT 1,
            Tool_DateTimeState                  INTEGER DEFAULT 1,
            Tool_GibberishState                 INTEGER DEFAULT 1,
            Tool_GuidState                      INTEGER DEFAULT 1,
            Tool_HashState                      INTEGER DEFAULT 1,
            Tool_HTMLEntityState                INTEGER DEFAULT 1,
            Tool_IndentState                    INTEGER DEFAULT 1,
            Tool_JoinState                      INTEGER DEFAULT 1,
            Tool_JSONState                      INTEGER DEFAULT 1,
            Tool_ListState                      INTEGER DEFAULT 1,
            Tool_QuoteState                     INTEGER DEFAULT 1,
            Tool_RemoveState                    INTEGER DEFAULT 1,
            Tool_SlashState                     INTEGER DEFAULT 1,
            Tool_SortState                      INTEGER DEFAULT 1,
            Tool_SplitState                     INTEGER DEFAULT 1,
            Tool_TrimState                      INTEGER DEFAULT 1,
            Tool_UrlState                       INTEGER DEFAULT 1
        );
        INSERT INTO Settings DEFAULT VALUES;

        CREATE TABLE Notes (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Content TEXT,
            X REAL, Y REAL, Width REAL, Height REAL,
            GravityX INTEGER, GravityY INTEGER,
            ThemeColourScheme TEXT,
            IsPinned INTEGER, IsOpen INTEGER
        );
        INSERT INTO Notes (Content, X, Y, Width, Height, GravityX, GravityY, ThemeColourScheme, IsPinned, IsOpen) VALUES
            ('회의 메모' || char(13) || char(10) || '- 3시', 120, 130, 300, 280, 1, -1, 'Green', 1, 1),
            ('닫힌 메모', 400, 200, 250, 250, -1, 1, 'Pink', 0, 0),
            (NULL, 10, 10, 300, 300, 1, 1, 'Yellow', 0, 0);
    ";

    private static DatabaseConfiguration CreateV4(string dir)
    {
        DatabaseConfiguration config = new(dir);
        Exec(config.ConnectionString, SchemaV4);
        return config;
    }

    // ---------------------------------------------------------------- 시나리오

    private static void FreshDatabase(string dir)
    {
        DatabaseConfiguration config = new(dir);
        Run(DatabaseInitialiser.Initialise(config.ConnectionString, config.BackupPath));

        Check("새 DB: 최신 스키마", Convert.ToInt32(Scalar(config.ConnectionString, "SELECT Version FROM SchemaInfo WHERE Id = 0")) == DatabaseInitialiser.SchemaVersion);
        Check("새 DB: 설정은 AppSettings 키/값 테이블 (예전 Settings 테이블 없음)",
            TableExists(config.ConnectionString, "AppSettings") && !TableExists(config.ConnectionString, "Settings"));
        HashSet<string> noteColumns = Columns(config.ConnectionString, "Notes");
        Check("새 DB: 메모에 잠금·시각·휴지통 열", noteColumns.IsSupersetOf(["IsLocked", "CreatedAt", "UpdatedAt", "DeletedAt"]), string.Join(",", noteColumns));
        Check("새 DB: 처음에는 설정 행이 없음 (없으면 기본값)", Convert.ToInt32(Scalar(config.ConnectionString, "SELECT COUNT(*) FROM AppSettings")) == 0);
        Check("새 DB: 백업 폴더를 만들지 않음 (마이그레이션이 없었으므로)", !Directory.Exists(config.BackupPath));
    }

    /// <summary>v5 이후에 추가한 설정. v4 DB에 없으므로 마이그레이션 표에도 없다 (키가 없으면 기본값을 쓴다).</summary>
    private static readonly string[] SettingsAddedAfterV4 = ["Application.TrayOnly", "Application.ToggleNotesHotkey", "Notes.SnapToEdges", "Editor.FontSize"];

    private static void MigrateDefaultsFromV4(string dir)
    {
        DatabaseConfiguration v4 = CreateV4(dir);
        Run(DatabaseInitialiser.Initialise(v4.ConnectionString, v4.BackupPath));

        Check("v4→최신: 스키마 버전", Convert.ToInt32(Scalar(v4.ConnectionString, "SELECT Version FROM SchemaInfo WHERE Id = 0")) == DatabaseInitialiser.SchemaVersion);
        Check("v5→v6: 기존 메모마다 서로 다른 동기화 ID",
            Convert.ToInt32(Scalar(v4.ConnectionString, "SELECT COUNT(DISTINCT SyncId) FROM NoteSync WHERE length(SyncId) = 32")) == Convert.ToInt32(Scalar(v4.ConnectionString, "SELECT COUNT(*) FROM Notes"))
            && Convert.ToInt32(Scalar(v4.ConnectionString, "SELECT COUNT(*) FROM Notes")) > 0);
        Check("v4→v5: 예전 Settings 테이블은 지움", !TableExists(v4.ConnectionString, "Settings"));
        int migratedKeys = Convert.ToInt32(Scalar(v4.ConnectionString, "SELECT COUNT(*) FROM AppSettings"));
        Check("v4→v5: 설정 62개를 키/값으로 옮김", migratedKeys == 62, $"keys={migratedKeys}");

        // 기본값이 예전 DB 열의 DEFAULT에서 모델 초기값으로 옮겨졌다. 둘이 같아야 한다.
        SettingsService migrated = LoadSettings(v4);
        DatabaseConfiguration fresh = new(Path.Combine(dir, "fresh"));
        Run(DatabaseInitialiser.Initialise(fresh.ConnectionString));
        SettingsService defaults = LoadSettings(fresh);
        Dictionary<string, string> a = Snapshot(migrated), b = Snapshot(defaults);
        Check("v4 기본값 = 새 버전 기본값 (설정 62개 모두)", a.Count == 62 + SettingsAddedAfterV4.Length && Diff(a, b) == "", Diff(a, b));

        string[] mapKeys = [.. Schema4To5Migration.SettingsColumnMap.Select(m => m.Key).Order()];
        string[] modelKeys = [.. defaults.GetSettingProperties().Select(s => s.Key).Except(SettingsAddedAfterV4).Order()];
        Check("마이그레이션 표의 키 = 설정 모델 속성 (빠지거나 이름이 다른 것 없음)", mapKeys.SequenceEqual(modelKeys),
            string.Join(",", mapKeys.Except(modelKeys).Concat(modelKeys.Except(mapKeys))));

        NoteRepository notes = new(v4);
        NoteDto[] all = [.. Run(notes.GetAll())];
        NoteDto meeting = all.First(n => n.Content.StartsWith("회의 메모"));
        Check("v4→v5: 메모 3개 그대로", all.Length == 3, $"notes={all.Length}");
        Check("v4→v5: 메모 내용·위치·색·고정·열림 유지",
            meeting.Content == "회의 메모\r\n- 3시" && meeting.X == 120 && meeting.Y == 130 && meeting.Height == 280
            && meeting.GravityY == -1 && meeting.ThemeColourScheme == "Green" && meeting.IsPinned && meeting.IsOpen);
        Check("v4→v5: 새 열 기본값 (잠금 해제, 시각 모름, 휴지통 아님)",
            all.All(n => !n.IsLocked && n.CreatedAt is null && n.UpdatedAt is null && n.DeletedAt is null));
        Check("v4→v5: 내용이 NULL인 메모도 읽힘 (빈 문자열)", all.Any(n => n.Content == ""));

        string[] backups = Directory.Exists(v4.BackupPath) ? Directory.GetFiles(v4.BackupPath, "flowmemo-before-v4-*.sqlite") : [];
        Check("마이그레이션 직전 백업 1개", backups.Length == 1, string.Join(",", backups.Select(Path.GetFileName)));
        if (backups.Length == 1)
        {
            string backupConnection = $"Data Source={backups[0]};Mode=ReadOnly;Pooling=False";
            Check("마이그레이션 직전 백업은 v4 그대로 (Settings 테이블, 버전 4, 메모 3개)",
                Convert.ToInt32(Scalar(backupConnection, "SELECT Version FROM SchemaInfo")) == 4
                && TableExists(backupConnection, "Settings")
                && Convert.ToInt32(Scalar(backupConnection, "SELECT COUNT(*) FROM Notes")) == 3);
        }
    }

    private static void MigrateCustomFromV4(string dir)
    {
        DatabaseConfiguration v4 = CreateV4(dir);
        Exec(v4.ConnectionString, @"
            UPDATE Settings SET
                Application_StartupBehaviour = 2,
                Application_ShowTrayIcon = 0,
                Notes_DefaultWidth = 350.5,
                Notes_TransparencyMode = 0,
                Notes_TransparentOpacity = 0.55,
                Notes_ColourMode = 2,
                Editor_StandardFontFamily = '맑은 고딕',
                Editor_TabWidth = 2,
                Editor_CopyAltFallbackAction = 0,
                Tool_HashState = 0,
                Tool_JSONState = 2,
                Notes_HideTitleBar = NULL
            WHERE Id = 1;");
        Run(DatabaseInitialiser.Initialise(v4.ConnectionString, v4.BackupPath));

        SettingsService s = LoadSettings(v4);
        Check("v4→v5: 바꿔 둔 설정 값 유지 (열거형·bool·실수·문자열)",
            s.ApplicationSettings.StartupBehaviour == StartupBehaviour.ShowManagementWindow
            && !s.ApplicationSettings.ShowNotifyIcon
            && s.NoteSettings.DefaultWidth == 350.5
            && s.NoteSettings.TransparencyMode == TransparencyMode.Disabled
            && s.NoteSettings.TransparentValue == 0.55
            && s.NoteSettings.ColourMode == ColourMode.System
            && s.EditorSettings.StandardFontFamily == "맑은 고딕"
            && s.EditorSettings.TabSpacesWidth == 2
            && s.EditorSettings.CopyAltFallbackAction == CopyFallbackAction.None
            && s.ToolSettings.HashToolState == ToolState.Disabled
            && s.ToolSettings.JsonToolState == ToolState.Favourite);
        Check("v4→v5: NULL이던 설정은 옮기지 않고 기본값", !s.NoteSettings.HideTitleBar
            && Scalar(v4.ConnectionString, "SELECT COUNT(*) FROM AppSettings WHERE Key = 'Notes.HideTitleBar'") is long n && n == 0);
    }

    private static void SettingsRoundTrip(string dir)
    {
        DatabaseConfiguration config = new(dir);
        Run(DatabaseInitialiser.Initialise(config.ConnectionString));

        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // 소수점이 쉼표인 문화권에서도 DB에는 같은 형식으로 저장해야 한다.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            SettingsService first = LoadSettings(config);
            first.NoteSettings.TransparentValue = 0.65;
            first.EditorSettings.StandardFontFamily = "맑은 고딕";
            first.ToolSettings.HashToolState = ToolState.Favourite;
            first.ApplicationSettings.StartupBehaviour = StartupBehaviour.ShowManagementWindow;
            Run(first.Save());

            Check("설정 저장: 실수는 문화권과 무관한 형식", (string?)Scalar(config.ConnectionString, "SELECT Value FROM AppSettings WHERE Key = 'Notes.TransparentValue'") == "0.65");
            Check("설정 저장: 열거형은 이름으로 (순서를 바꿔도 안전)", (string?)Scalar(config.ConnectionString, "SELECT Value FROM AppSettings WHERE Key = 'Tools.HashToolState'") == "Favourite");
            Check("설정 저장: bool은 true/false", (string?)Scalar(config.ConnectionString, "SELECT Value FROM AppSettings WHERE Key = 'Application.ShowNotifyIcon'") == "true");

            Exec(config.ConnectionString, @"
                INSERT INTO AppSettings (Key, Value) VALUES ('Future.NewSetting', 'keep-me');
                UPDATE AppSettings SET Value = 'abc' WHERE Key = 'Editor.TabSpacesWidth';
                UPDATE AppSettings SET Value = 'NoSuchMode' WHERE Key = 'Notes.ColourMode';");

            SettingsService second = LoadSettings(config);
            Check("설정 다시 읽기: 저장한 값 그대로",
                second.NoteSettings.TransparentValue == 0.65
                && second.EditorSettings.StandardFontFamily == "맑은 고딕"
                && second.ToolSettings.HashToolState == ToolState.Favourite
                && second.ApplicationSettings.StartupBehaviour == StartupBehaviour.ShowManagementWindow);
            Check("설정 다시 읽기: 읽을 수 없는 값은 기본값 (앱이 죽지 않음)",
                second.EditorSettings.TabSpacesWidth == 4 && second.NoteSettings.ColourMode == ColourMode.Light);
            Check("설정 다시 읽기: 읽을 수 없는 값은 오류 기록에 남김",
                File.Exists(ErrorReporter.LogFilePath) && File.ReadAllText(ErrorReporter.LogFilePath).Contains("Editor.TabSpacesWidth"));

            Run(second.Save());
            Check("설정 저장: 모르는 키(새 버전의 설정)는 지우지 않음",
                (string?)Scalar(config.ConnectionString, "SELECT Value FROM AppSettings WHERE Key = 'Future.NewSetting'") == "keep-me");
            Check("설정 저장: 읽을 수 없던 값은 기본값으로 고쳐 씀",
                (string?)Scalar(config.ConnectionString, "SELECT Value FROM AppSettings WHERE Key = 'Editor.TabSpacesWidth'") == "4");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Check("변환: v4의 숫자 bool/열거형도 읽음",
            SettingValueConverter.TryParse("1", typeof(bool), out object? b) && (bool)b!
            && SettingValueConverter.TryParse("2", typeof(TransparencyMode), out object? e) && (TransparencyMode)e! == TransparencyMode.WhenPinned
            && !SettingValueConverter.TryParse("7", typeof(TransparencyMode), out _)
            && SettingValueConverter.TryParse("300.0", typeof(int), out object? i) && (int)i! == 300);
    }

    private static void NotesAndTrash(string dir)
    {
        DatabaseConfiguration config = new(dir);
        Run(DatabaseInitialiser.Initialise(config.ConnectionString));
        NoteRepository repo = new(config);

        DateTime created = new(2026, 11, 7, 3, 4, 5, 678, DateTimeKind.Utc);
        int id = Run(repo.Create(NewNote("잠긴 메모", isLocked: true, at: created)));
        NoteDto saved = Run(repo.GetById(id));
        Check("메모 저장: 잠금 유지", saved.IsLocked);
        Check("메모 저장: 만든·고친 시각 (UTC, 밀리초까지)", saved.CreatedAt == created && saved.UpdatedAt == created && saved.CreatedAt?.Kind == DateTimeKind.Utc,
            $"{saved.CreatedAt:o}");
        Check("메모 저장: 시각은 읽을 수 있는 UTC 문자열",
            (string?)Scalar(config.ConnectionString, $"SELECT CreatedAt FROM Notes WHERE Id = {id}") == "2026-11-07T03:04:05.678Z");

        DateTime edited = created.AddMinutes(5);
        Run(repo.Update(saved with { Content = "고친 내용", IsLocked = false, UpdatedAt = edited, CreatedAt = DateTime.UtcNow }));
        saved = Run(repo.GetById(id));
        Check("메모 고치기: 내용·잠금·고친 시각 저장, 만든 시각은 그대로",
            saved.Content == "고친 내용" && !saved.IsLocked && saved.UpdatedAt == edited && saved.CreatedAt == created);

        DateTime now = DateTime.UtcNow;
        Run(repo.MoveToTrash(id, now));
        Check("휴지통: 메모 목록·다음 시작 목록에서 빠짐",
            !Run(repo.GetAll()).Any() && !Run(repo.GetOpen()).Any());
        NoteDto trashed = Run(repo.GetDeleted()).Single();
        Check("휴지통: 휴지통 목록에 보이고 삭제 시각이 있음, 다시 열리지 않게 IsOpen 끔",
            trashed.Id == id && trashed.DeletedAt is not null && !trashed.IsOpen && Run(repo.CountDeleted()) == 1);

        // 휴지통으로 옮긴 직후에 메모 창이 늦게 저장해도 되살아나면 안 된다.
        Run(repo.Update(trashed with { Content = "늦게 저장", DeletedAt = null }));
        Check("휴지통: 메모 창이 늦게 저장해도 휴지통에 남음", Run(repo.CountDeleted()) == 1 && !Run(repo.GetAll()).Any());

        Run(repo.Restore(id));
        Check("복원: 메모 목록으로 돌아옴", Run(repo.GetAll()).Single().Id == id && Run(repo.CountDeleted()) == 0);

        int old = Run(repo.Create(NewNote("31일 전에 지움")));
        int recent = Run(repo.Create(NewNote("어제 지움")));
        Run(repo.MoveToTrash(old, now.AddDays(-31)));
        Run(repo.MoveToTrash(recent, now.AddDays(-1)));
        int purged = Run(repo.PurgeTrash(now, NoteRepository.TrashRetention));
        Check("휴지통 정리: 30일 지난 메모만 완전히 지움", purged == 1 && Run(repo.GetDeleted()).Single().Id == recent, $"purged={purged}");

        int emptied = Run(repo.EmptyTrash());
        Check("휴지통 비우기", emptied == 1 && Run(repo.CountDeleted()) == 0 && Run(repo.GetAll()).Count() == 1);
    }

    private static void Backups(string dir)
    {
        DatabaseConfiguration config = new(dir);
        Run(DatabaseInitialiser.Initialise(config.ConnectionString));
        Run(new NoteRepository(config).Create(NewNote("백업할 메모")));
        string backupDir = config.BackupPath;

        Check("백업: DB 파일이 없으면 만들지 않음", DatabaseBackup.CreateDailyBackup(Path.Combine(dir, "missing.sqlite"), backupDir, DateTime.Now) is null && !Directory.Exists(backupDir));

        DateTime day = new(2026, 11, 7, 9, 0, 0);
        string? first = DatabaseBackup.CreateDailyBackup(config.DatabasePath, backupDir, day);
        Check("백업: 오늘 백업 파일 이름", first is not null && Path.GetFileName(first) == "flowmemo-20261107.sqlite", first ?? "null");
        if (first is not null)
            Check("백업: 백업 파일에 메모가 들어 있음",
                (string?)Scalar($"Data Source={first};Mode=ReadOnly;Pooling=False", "SELECT Content FROM Notes") == "백업할 메모");
        Check("백업: 같은 날 다시 부르면 만들지 않음", DatabaseBackup.CreateDailyBackup(config.DatabasePath, backupDir, day.AddHours(5)) is null);
        Check("백업: 임시 파일이 남지 않음", Directory.GetFiles(backupDir, "*.tmp").Length == 0);

        // 예전 날짜 백업 8개 + 하루 백업이 아닌 파일 → 새 백업 뒤 최근 7개만 남고 다른 파일은 그대로
        for (int i = 1; i <= 8; i++)
            File.WriteAllText(Path.Combine(backupDir, $"flowmemo-202610{20 + i:00}.sqlite"), "");
        File.WriteAllText(Path.Combine(backupDir, "flowmemo-before-v4-20261001-120000.sqlite"), "");
        File.WriteAllText(Path.Combine(backupDir, "내 사본.sqlite"), "");
        DatabaseBackup.CreateDailyBackup(config.DatabasePath, backupDir, day.AddDays(1));

        string[] daily = [.. Directory.GetFiles(backupDir, "flowmemo-2*.sqlite").Select(Path.GetFileName).Order()!];
        Check("백업: 하루 백업은 최근 7개만 남김", daily.Length == 7 && daily[^1] == "flowmemo-20261108.sqlite" && daily[0] == "flowmemo-20261024.sqlite",
            string.Join(",", daily));
        Check("백업: 마이그레이션 백업과 다른 파일은 건드리지 않음",
            File.Exists(Path.Combine(backupDir, "flowmemo-before-v4-20261001-120000.sqlite")) && File.Exists(Path.Combine(backupDir, "내 사본.sqlite")));
    }

    private static void NewerDatabaseIsRefused(string dir)
    {
        DatabaseConfiguration config = new(dir);
        Run(DatabaseInitialiser.Initialise(config.ConnectionString));
        Exec(config.ConnectionString, "UPDATE SchemaInfo SET Version = 99 WHERE Id = 0");
        bool refused = false;
        try { Run(DatabaseInitialiser.Initialise(config.ConnectionString, config.BackupPath)); }
        catch (DatabaseVersionException) { refused = true; }
        Check("새 버전이 만든 DB는 열지 않음 (덮어쓰지 않음)", refused);
    }

    private static void DirtyTracking()
    {
        DateTime updated = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        NoteDto dto = NewNote("내용") with { Id = 7, UpdatedAt = updated, CreatedAt = updated.AddDays(-1) };
        NoteModel note = new(dto, new NoteSettingsModel());
        Check("메모 불러오기: 저장된 고친 시각 유지 (내용을 넣었다고 지금으로 바뀌지 않음)", note.UpdatedAt == updated && note.CreatedAt == updated.AddDays(-1));

        note.IsSaved = true;
        note.IsFocused = true;
        note.Opacity = 0.5;
        note.TransparencyEnabled = !note.TransparencyEnabled;
        note.ShowInTaskbar = !note.ShowInTaskbar;
        note.Background = Brushes.Red;
        Check("화면용 속성(포커스·투명도·브러시)이 바뀌어도 다시 저장하지 않음", note.IsSaved);

        note.X += 10;
        Check("위치가 바뀌면 저장 대상", !note.IsSaved && note.UpdatedAt == updated);
        note.IsSaved = true;
        note.IsLocked = true;
        Check("잠금이 바뀌면 저장 대상", !note.IsSaved);
        note.IsSaved = true;
        note.Content = "고친 내용";
        Check("내용이 바뀌면 저장 대상이고 고친 시각이 지금으로", !note.IsSaved && note.UpdatedAt > DateTime.UtcNow.AddMinutes(-1));

        string[] dtoFields = [.. typeof(NoteDto).GetProperties().Select(p => p.Name).Where(n => n is not nameof(NoteDto.DeletedAt) and not "EqualityContract")];
        string[] missing = [.. dtoFields.Where(f => !NoteModel.PersistedProperties.Contains(f))];
        Check("DB에 저장하는 메모 필드는 모두 '저장 대상' 속성", missing.Length == 0, string.Join(",", missing));

        NoteModel fresh = new(new NoteSettingsModel(), "Yellow");
        Check("새 메모: 만든 시각 = 고친 시각 = 지금", fresh.CreatedAt is not null && fresh.CreatedAt == fresh.UpdatedAt && fresh.CreatedAt > DateTime.UtcNow.AddMinutes(-1));
    }
}
