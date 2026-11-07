using System.Text;

namespace FlowMemo.Core.Migrations;

/// <summary>
/// v4 → v5 (FlowMemo 2.0.0)
/// - 설정: 열 62개짜리 Settings 한 행 → AppSettings 키/값 테이블. 옮긴 뒤 Settings는 지운다.
/// - 메모: 잠금(IsLocked), 만든·고친 시각(CreatedAt, UpdatedAt), 휴지통(DeletedAt) 열 추가.
///   예전 메모의 시각은 알 수 없으므로 null로 둔다.
/// </summary>
public class Schema4To5Migration : SchemaMigration
{
    public override int TargetSchemaVersion => 4;
    public override int ResultingSchemaVersion => 5;

    /// <summary>새 키 ← v4 열. 키 이름은 WpfUi 설정 모델의 "구역.속성이름"과 같아야 한다.</summary>
    public static readonly IReadOnlyList<(string Key, string Column)> SettingsColumnMap =
    [
        ("Application.StartupBehaviour", "Application_StartupBehaviour"),
        ("Application.NewInstanceBehaviour", "Application_NewInstanceBehaviour"),
        ("Application.ShowNotifyIcon", "Application_ShowTrayIcon"),
        ("Application.CheckForUpdates", "Application_CheckForUpdates"),

        ("Notes.DefaultWidth", "Notes_DefaultWidth"),
        ("Notes.DefaultHeight", "Notes_DefaultHeight"),
        ("Notes.StartupPosition", "Notes_StartupPosition"),
        ("Notes.PinnedByDefault", "Notes_PinnedByDefault"),
        ("Notes.MinimizeMode", "Notes_MinimizeMode"),
        ("Notes.VisibilityMode", "Notes_VisibilityMode"),
        ("Notes.HideTitleBar", "Notes_HideTitleBar"),
        ("Notes.CycleColours", "Notes_CycleColours"),
        ("Notes.ColourMode", "Notes_ColourMode"),
        ("Notes.TransparencyMode", "Notes_TransparencyMode"),
        ("Notes.OpaqueWhenFocused", "Notes_OpaqueWhenFocused"),
        ("Notes.OpaqueValue", "Notes_OpaqueOpacity"),
        ("Notes.TransparentValue", "Notes_TransparentOpacity"),

        ("Editor.CheckSpelling", "Editor_SpellCheck"),
        ("Editor.AutoIndent", "Editor_AutoIndent"),
        ("Editor.NewLineAtEnd", "Editor_NewLineAtEnd"),
        ("Editor.KeepNewLineVisible", "Editor_KeepNewLineVisible"),
        ("Editor.WrapText", "Editor_WrapText"),
        ("Editor.StandardFontFamily", "Editor_StandardFontFamily"),
        ("Editor.MonoFontFamily", "Editor_MonoFontFamily"),
        ("Editor.UseMonoFont", "Editor_UseMonoFont"),
        ("Editor.UseSpacesForTab", "Editor_TabUsesSpaces"),
        ("Editor.ConvertIndentationOnPaste", "Editor_ConvertIndentationOnPaste"),
        ("Editor.TabSpacesWidth", "Editor_TabWidth"),
        ("Editor.CopyAction", "Editor_CopyAction"),
        ("Editor.TrimTextOnCopy", "Editor_TrimTextOnCopy"),
        ("Editor.CopyAltAction", "Editor_CopyAltAction"),
        ("Editor.TrimTextOnAltCopy", "Editor_TrimTextOnAltCopy"),
        ("Editor.CopyFallbackAction", "Editor_CopyFallbackAction"),
        ("Editor.TrimTextOnFallbackCopy", "Editor_TrimTextOnFallbackCopy"),
        ("Editor.CopyAltFallbackAction", "Editor_CopyAltFallbackAction"),
        ("Editor.TrimTextOnAltFallbackCopy", "Editor_TrimTextOnAltFallbackCopy"),
        ("Editor.CopyOnSelect", "Editor_CopyTextOnHighlight"),
        ("Editor.PasteAction", "Editor_PasteAction"),
        ("Editor.TrimTextOnPaste", "Editor_TrimTextOnPaste"),
        ("Editor.PasteAltAction", "Editor_PasteAltAction"),
        ("Editor.TrimTextOnAltPaste", "Editor_TrimTextOnAltPaste"),
        ("Editor.MiddleClickPaste", "Editor_MiddleClickPaste"),

        ("Tools.Base64ToolState", "Tool_Base64State"),
        ("Tools.BracketToolState", "Tool_BracketState"),
        ("Tools.CaseToolState", "Tool_CaseState"),
        ("Tools.ColourToolState", "Tool_ColourState"),
        ("Tools.DateTimeToolState", "Tool_DateTimeState"),
        ("Tools.GibberishToolState", "Tool_GibberishState"),
        ("Tools.GuidToolState", "Tool_GuidState"),
        ("Tools.HashToolState", "Tool_HashState"),
        ("Tools.HtmlEntityToolState", "Tool_HTMLEntityState"),
        ("Tools.IndentToolState", "Tool_IndentState"),
        ("Tools.JoinToolState", "Tool_JoinState"),
        ("Tools.JsonToolState", "Tool_JSONState"),
        ("Tools.ListToolState", "Tool_ListState"),
        ("Tools.QuoteToolState", "Tool_QuoteState"),
        ("Tools.RemoveToolState", "Tool_RemoveState"),
        ("Tools.SlashToolState", "Tool_SlashState"),
        ("Tools.SortToolState", "Tool_SortState"),
        ("Tools.SplitToolState", "Tool_SplitState"),
        ("Tools.TrimToolState", "Tool_TrimState"),
        ("Tools.UrlToolState", "Tool_UrlState"),
    ];

    public override string UpdateQuery => BuildUpdateQuery();

    private string BuildUpdateQuery()
    {
        StringBuilder sql = new();

        sql.AppendLine(@"
            -- 설정: 키/값 테이블
            CREATE TABLE AppSettings (
                Key     TEXT PRIMARY KEY NOT NULL,
                Value   TEXT
            );");

        // 설정 행은 처음 만들 때 DEFAULT VALUES로 넣어서 Id가 1이다 (SettingsService도 1을 읽었다).
        // 값이 NULL인 열은 옮기지 않는다. 그러면 새 버전의 기본값을 쓴다.
        foreach ((string key, string column) in SettingsColumnMap)
        {
            sql.AppendLine($@"
            INSERT INTO AppSettings (Key, Value)
            SELECT '{key}', CAST({column} AS TEXT) FROM Settings WHERE Id = 1 AND {column} IS NOT NULL;");
        }

        sql.AppendLine($@"
            DROP TABLE Settings;

            -- 메모: 잠금, 시각, 휴지통
            ALTER TABLE Notes ADD COLUMN IsLocked INTEGER DEFAULT 0;
            ALTER TABLE Notes ADD COLUMN CreatedAt TEXT DEFAULT NULL;
            ALTER TABLE Notes ADD COLUMN UpdatedAt TEXT DEFAULT NULL;
            ALTER TABLE Notes ADD COLUMN DeletedAt TEXT DEFAULT NULL;

            UPDATE SchemaInfo
            SET Version = {ResultingSchemaVersion}
            WHERE Id = 0;
        ");

        return sql.ToString();
    }
}
