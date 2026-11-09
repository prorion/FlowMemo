using System.Diagnostics;
using System.Windows.Automation;
using Microsoft.Data.Sqlite;

using FlowMemo.Core;

/// <summary>
/// 실제 FlowMemo.exe(복사본, 포터블, 새 DB)를 띄우고 UI 자동화(UIA)와 Win32로 검사한다.
/// 마우스·키보드 입력은 보내지 않는다.
/// </summary>
internal static class Ui
{
    private static string _appDir = "", _out = "", _exe = "", _conn = "";

    private static void Check(string n, bool ok, string d = "") => Program.Check(n, ok, d);

    public static void Run(string appDir, string outDir)
    {
        _appDir = appDir;
        _out = outDir;
        _exe = Path.Combine(appDir, "FlowMemo.exe");
        _conn = $"Data Source={Path.Combine(appDir, "flowmemo.sqlite")};Pooling=False";

        (string Name, Action Run)[] scenarios =
        [
            ("A", ScenarioRestoreAndNotes),
            ("B", ScenarioManagementAndSettings),
            ("C", ScenarioExitWithoutTray),
            ("D", ScenarioEmptyManagement),
            ("E", ScenarioTrash),
            ("F", ScenarioLockRestoredAndDataFolder),
        ];
        foreach ((string name, Action run) in scenarios)
        {
            try
            {
                run();
            }
            catch (Exception ex)
            {
                Check($"[{name}] 시나리오가 예외 없이 끝남", false, ex.ToString());
            }
            finally
            {
                KillTestInstances();
            }
        }
    }

    /// <summary>실제 앱을 로컬 동기화 서버에 연결해 메모 상단 버튼, 비교 창, 충돌 해결을 검사한다.</summary>
    public static void SyncScenario(string appDir, string outDir, FlowMemo.Core.Sync.SyncCredentials credentials, SyncTests.Pc other)
    {
        Console.WriteLine("== UI 시나리오 G: 동기화 (메모 상단 안내 · 비교 · 충돌 해결)");
        _appDir = appDir;
        _out = outDir;
        _exe = Path.Combine(appDir, "FlowMemo.exe");
        _conn = $"Data Source={Path.Combine(appDir, "flowmemo.sqlite")};Pooling=False";

        string credentialsPath = Path.Combine(appDir, FlowMemo.WpfUi.Helpers.SyncCredentialStore.FileName);
        try
        {
            ResetDb(c => InsertNote(c, "첫째 줄\r\n둘째 줄\r\n셋째 줄\r\n", 200, 200, "Yellow", false, true));
            new FlowMemo.WpfUi.Helpers.SyncCredentialStore(credentialsPath).Save(credentials);
            string syncId = (string)Scalar("SELECT SyncId FROM NoteSync")!;

            Process p = Launch();
            IntPtr note = IntPtr.Zero;
            Wait(() => (note = NoteWindows(p.Id).FirstOrDefault()) != IntPtr.Zero);
            Check("동기화 앱: 메모 복원", note != IntPtr.Zero);
            AutomationElement ae = AE(note);
            ValuePattern Text() => (ValuePattern)ById(ae, "NoteTextBox")!.GetCurrentPattern(ValuePattern.Pattern);

            bool uploaded = Wait(() => { other.Cycle(); return other.RowBySyncId(syncId) is not null; }, 15000);
            Check("동기화 앱: 시작하면 메모를 올림", uploaded);
            int otherId = other.IdOf(syncId);

            // 다른 PC 변경: 메모 상단 버튼
            other.Edit(otherId, "첫째 줄 다른 PC\r\n둘째 줄\r\n셋째 줄\r\n");
            other.Cycle();
            Check("메모 상단: 다른 PC 변경이 오면 [가져오기] 표시", Wait(() => ById(ae, "SyncAcceptButton") is not null, 40000));
            Check("메모 상단: [비교]·[건너뛰기] 표시", ById(ae, "SyncCompareButton") is not null && ById(ae, "SyncSkipButton") is not null);
            Check("메모 상단: 누르기 전에는 내용이 그대로", Text().Current.Value.StartsWith("첫째 줄\r\n"));
            Thread.Sleep(300);
            Capture(note, "G1_note_incoming.png");

            Invoke(ById(ae, "SyncCompareButton"));
            IntPtr diff = WaitWindow(p.Id, t => t == "FlowMemo - 변경 비교");
            Check("비교 창이 열림", diff != IntPtr.Zero);
            Thread.Sleep(500);
            Capture(diff, "G2_diff_incoming.png");
            AutomationElement dae = AE(diff);
            Check("비교 창: 양쪽 내용이 보임", HasText(dae, "첫째 줄") || All(dae, ControlType.Text).Cast<AutomationElement>().Any(e => e.Current.Name.Contains("첫째 줄")));
            Invoke(ById(dae, "LaterButton"));
            Check("비교 창 [나중에]: 안내가 그대로 남음", Wait(() => !Native.VisibleWindows(p.Id).Contains(diff), 3000) && ById(ae, "SyncAcceptButton") is not null);

            Invoke(ById(ae, "SyncAcceptButton"));
            Check("[가져오기]: 내용이 바뀌고 버튼이 사라짐",
                Wait(() => Text().Current.Value.StartsWith("첫째 줄 다른 PC") && ById(ae, "SyncAcceptButton") is null, 5000), Program.Esc(Text().Current.Value));

            // 충돌
            other.Edit(otherId, "첫째 줄 다른 PC 또\r\n둘째 줄\r\n셋째 줄\r\n");
            other.Cycle();
            Text().SetValue("첫째 줄 다른 PC\r\n둘째 줄\r\n셋째 줄 이 앱에서\r\n");
            Check("충돌: 양쪽에서 고치면 [비교하고 해결] 표시", Wait(() => ById(ae, "SyncResolveButton") is not null, 45000));
            Thread.Sleep(300);
            Capture(note, "G3_note_conflict.png");

            Invoke(ById(ae, "SyncResolveButton"));
            diff = WaitWindow(p.Id, t => t == "FlowMemo - 변경 비교");
            Thread.Sleep(500);
            Capture(diff, "G4_diff_conflict.png");
            dae = AE(diff);
            ValuePattern merged = (ValuePattern)ById(dae, "MergeTextBox")!.GetCurrentPattern(ValuePattern.Pattern);
            Check("충돌 창: 서로 다른 줄은 자동으로 합쳐 보여 줌", merged.Current.Value.Contains("다른 PC 또") && merged.Current.Value.Contains("이 앱에서"), Program.Esc(merged.Current.Value));
            Check("충돌 창: 유지·바꾸기·둘 다 보관 버튼",
                ById(dae, "KeepLocalButton") is not null && ById(dae, "TakeRemoteButton") is not null && ById(dae, "KeepBothButton") is not null);

            Invoke(ById(dae, "SaveMergedButton"));
            Check("합친 내용 저장: 메모에 반영, 안내 사라짐",
                Wait(() => Text().Current.Value.Contains("다른 PC 또") && Text().Current.Value.Contains("이 앱에서") && ById(ae, "SyncResolveButton") is null, 5000));
            Check("합친 내용 저장: 다른 PC에 도착", Wait(() =>
            {
                other.Cycle();
                return other.Row(otherId).PendingContent?.Contains("이 앱에서") == true;
            }, 15000));

            CheckLog("G");
        }
        catch (Exception ex)
        {
            Check("[G] 시나리오가 예외 없이 끝남", false, ex.ToString());
        }
        finally
        {
            KillTestInstances();
            File.Delete(credentialsPath);
        }
    }

    /// <summary>테스트용 복사본(_appDir)에서 띄운 FlowMemo만 끈다.</summary>
    private static void KillTestInstances()
    {
        foreach (Process proc in Process.GetProcessesByName("FlowMemo"))
        {
            try
            {
                if (proc.MainModule?.FileName?.StartsWith(_appDir, StringComparison.OrdinalIgnoreCase) == true)
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>지정한 exe로 실행 중인 FlowMemo의 메모 창을 캡처한다 (text를 주면 먼저 내용을 넣는다).</summary>
    public static void CaptureNotes(string exePath, string outDir, string? text)
    {
        _out = outDir;
        Process? proc = Process.GetProcessesByName("FlowMemo")
            .FirstOrDefault(p => { try { return string.Equals(p.MainModule?.FileName, exePath, StringComparison.OrdinalIgnoreCase); } catch { return false; } });
        if (proc is null) { Console.WriteLine("not running: " + exePath); return; }
        int i = 0;
        foreach (IntPtr h in NoteWindows(proc.Id))
        {
            if (text is not null)
            {
                AutomationElement? edit = ById(AE(h), "NoteTextBox");
                ((ValuePattern)edit!.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text.Replace("\\n", "\r\n"));
                Thread.Sleep(600);
            }
            Native.RECT r = Native.Rect(h);
            Console.WriteLine($"note {i}: {r} ({r.Right - r.Left}x{r.Bottom - r.Top}px)");
            Capture(h, $"note_{i++}.png");
        }
    }

    /// <summary>메모 창의 크기·DPI가 시간에 따라 어떻게 바뀌는지 본다.</summary>
    public static void DpiProbe(string appDir)
    {
        _appDir = appDir;
        _exe = Path.Combine(appDir, "FlowMemo.exe");
        _conn = $"Data Source={Path.Combine(appDir, "flowmemo.sqlite")};Pooling=False";
        ResetDb(c => InsertNote(c, "DPI 확인\r\n", 120, 120, "Yellow", false, true));
        Process p = Launch();
        Stopwatch sw = Stopwatch.StartNew();
        string last = "";
        while (sw.ElapsedMilliseconds < 8000)
        {
            IntPtr h = NoteWindows(p.Id).FirstOrDefault();
            if (h != IntPtr.Zero)
            {
                string now = $"{Native.Rect(h)} dpi={Native.GetDpiForWindow(h)}";
                if (now != last)
                {
                    Console.WriteLine($"t={sw.ElapsedMilliseconds,5}ms  {now}");
                    last = now;
                }
            }
            Thread.Sleep(50);
        }
        KillTestInstances();
    }

    private static void ScenarioEmptyManagement()
    {
        Console.WriteLine("== UI 시나리오 D: 메모가 없을 때 관리 창 안내");
        ResetDb(c => SetSetting(c, "Application.StartupBehaviour", "ShowManagementWindow"));
        Process p = Launch();
        IntPtr management = WaitWindow(p.Id, t => t == "FlowMemo - 메모 관리");
        Check("관리 창 열림", management != IntPtr.Zero);
        Thread.Sleep(800);
        AutomationElement? empty = AE(management).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
        bool hasEmptyText = All(AE(management), ControlType.Text).Cast<AutomationElement>().Any(e => e.Current.Name.StartsWith("메모가 없습니다."));
        Check("메모가 없으면 '새 메모를 눌러 시작하세요' 안내", hasEmptyText);
        Capture(management, "D1_management_empty.png");
        CheckLog("D");
        Kill(p);
    }

    // ---------------------------------------------------------------- helpers

    private static void ResetDb(Action<SqliteConnection>? seed = null)
    {
        SqliteConnection.ClearAllPools();
        foreach (string f in Directory.GetFiles(_appDir, "flowmemo.sqlite*"))
            File.Delete(f);
        string backups = Path.Combine(_appDir, DatabaseBackup.FolderName);
        if (Directory.Exists(backups))
            Directory.Delete(backups, recursive: true);
        DatabaseInitialiser.Initialise(_conn).GetAwaiter().GetResult();
        using SqliteConnection c = new(_conn);
        c.Open();
        seed?.Invoke(c);
    }

    /// <summary>설정 하나를 DB에 넣는다 (키 "구역.속성이름", 값은 DB 저장 형식).</summary>
    private static void SetSetting(SqliteConnection c, string key, string value)
    {
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES (@k, @v) ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    private static string? Setting(string key)
        => Scalar($"SELECT Value FROM AppSettings WHERE Key = '{key}'") as string;

    private static void Sql(SqliteConnection c, string sql)
    {
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(string sql)
    {
        using SqliteConnection c = new(_conn);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static int NoteCount() => Convert.ToInt32(Scalar("SELECT COUNT(*) FROM Notes"));
    private static int TrashCountInDb() => Convert.ToInt32(Scalar("SELECT COUNT(*) FROM Notes WHERE DeletedAt IS NOT NULL"));

    private static void InsertNote(SqliteConnection c, string content, double x, double y, string colour, bool pinned, bool open, bool locked = false)
    {
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO Notes (Content, X, Y, Width, Height, GravityX, GravityY, ThemeColourScheme, IsPinned, IsOpen, IsLocked)
                            VALUES (@c, @x, @y, 300, 300, 1, 1, @t, @p, @o, @l)";
        cmd.Parameters.AddWithValue("@c", content);
        cmd.Parameters.AddWithValue("@x", x);
        cmd.Parameters.AddWithValue("@y", y);
        cmd.Parameters.AddWithValue("@t", colour);
        cmd.Parameters.AddWithValue("@p", pinned);
        cmd.Parameters.AddWithValue("@o", open);
        cmd.Parameters.AddWithValue("@l", locked);
        cmd.ExecuteNonQuery();
    }

    // UseShellExecute = true: 콘솔 핸들을 물려주지 않는다 (물려주면 앱이 남아 있는 동안 출력 파이프가 안 닫혀 셸이 멈춘다).
    private static Process Launch()
        => Process.Start(new ProcessStartInfo(_exe) { WorkingDirectory = _appDir, UseShellExecute = true })!;

    private static bool Wait(Func<bool> condition, int ms = 8000)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            try { if (condition()) return true; } catch { }
            Thread.Sleep(100);
        }
        try { return condition(); } catch { return false; }
    }

    private static List<IntPtr> NoteWindows(int pid)
        => [.. Native.VisibleWindows(pid).Where(h => { string t = Native.Title(h); return t == "FlowMemo" || t.EndsWith(" - FlowMemo"); })];

    private static IntPtr WaitWindow(int pid, Func<string, bool> titleMatch, int ms = 8000)
    {
        IntPtr found = IntPtr.Zero;
        Wait(() => (found = Native.VisibleWindows(pid).FirstOrDefault(h => titleMatch(Native.Title(h)))) != IntPtr.Zero, ms);
        return found;
    }

    private static AutomationElement AE(IntPtr h) => AutomationElement.FromHandle(h);

    private static AutomationElement? ById(AutomationElement root, string id)
        => root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));

    private static AutomationElementCollection All(AutomationElement root, ControlType type)
        => root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, type));

    private static void Invoke(AutomationElement? e)
        => ((InvokePattern)e!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    private static void Capture(IntPtr h, string name)
    {
        string path = Path.Combine(_out, name);
        Native.Capture(h, path);
        Console.WriteLine($"info  캡처: {path}");
    }

    private static void CheckLog(string scenario)
    {
        string log = Path.Combine(_appDir, "logs", "error.log");
        bool exists = File.Exists(log);
        string text = exists ? File.ReadAllText(log) : "";
        Check($"[{scenario}] 오류 로그 없음", !exists, text.Length > 1200 ? text[..1200] : text);
        if (exists)
            File.Delete(log);
    }

    private static void Kill(Process p)
    {
        if (!p.HasExited) { p.Kill(); p.WaitForExit(5000); }
        Thread.Sleep(300);
    }

    // ---------------------------------------------------------------- scenarios

    private static void ScenarioRestoreAndNotes()
    {
        Console.WriteLine("== UI 시나리오 A: 복원 · 창 제목 · 고정 · 최소화 · 편집/저장 · 새 메모 · 다시 실행");
        ResetDb(c =>
        {
            InsertNote(c, "회의 메모\r\n- 3시 회의실\r\n", 120, 120, "Yellow", false, true);
            InsertNote(c, "화면 밖 메모\r\n", -6000, -6000, "Blue", false, true);
        });

        Process p = Launch();
        Check("열려 있던 메모 2개 복원", Wait(() => NoteWindows(p.Id).Count == 2), $"windows={NoteWindows(p.Id).Count}");
        string backupsDir = Path.Combine(_appDir, DatabaseBackup.FolderName);
        Check("시작할 때 오늘 백업을 만듦 (backups\\flowmemo-날짜.sqlite)",
            Wait(() => Directory.Exists(backupsDir) && Directory.GetFiles(backupsDir, $"flowmemo-{DateTime.Now:yyyyMMdd}.sqlite").Length == 1, 3000));
        List<IntPtr> notes = NoteWindows(p.Id);
        IntPtr meeting = notes.FirstOrDefault(h => Native.Title(h).StartsWith("회의 메모"));
        IntPtr offscreen = notes.FirstOrDefault(h => Native.Title(h).StartsWith("화면 밖 메모"));
        Check("창 제목 = 메모 첫 줄 + FlowMemo", meeting != IntPtr.Zero && Native.Title(meeting) == "회의 메모 - FlowMemo", Native.Title(meeting));
        Check("화면 밖에 저장된 메모를 화면 안으로 옮김", offscreen != IntPtr.Zero && Native.OnScreen(offscreen), Native.Rect(offscreen).ToString());
        Thread.Sleep(1500);
        int meetingWidth = Native.Rect(meeting).Right - Native.Rect(meeting).Left;
        int offscreenWidth = Native.Rect(offscreen).Right - Native.Rect(offscreen).Left;
        Check("옮긴 메모도 다른 메모와 같은 배율(DPI)로 그려짐", meetingWidth == offscreenWidth, $"meeting={meetingWidth}px moved={offscreenWidth}px");
        Thread.Sleep(500);
        Capture(meeting, "A1_note.png");

        AutomationElement ae = AE(meeting);
        AutomationElement? pinButton = ById(ae, "PinButton");
        Check("아이콘 버튼 접근성 이름 (고정/새 메모/닫기)",
            pinButton?.Current.Name == "항상 위에 고정" && ById(ae, "NewButton")?.Current.Name == "새 메모" && ById(ae, "CloseButton")?.Current.Name == "닫기",
            $"pin='{pinButton?.Current.Name}'");

        TogglePattern pin = (TogglePattern)pinButton!.GetCurrentPattern(TogglePattern.Pattern);
        pin.Toggle();
        Check("고정을 켜면 항상 위", Wait(() => Native.IsTopmost(meeting), 3000));
        pin.Toggle();
        Check("고정을 끄면 항상 위 해제", Wait(() => !Native.IsTopmost(meeting), 3000));

        // 가드: 다른 원인으로 상태가 어긋나도 고정 설정에 맞춰 되돌린다.
        Native.SetWindowPos(meeting, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Check("(가드) 고정 안 한 메모가 '항상 위'로 남으면 자동으로 해제", Wait(() => !Native.IsTopmost(meeting), 4000));
        pin.Toggle();
        Wait(() => Native.IsTopmost(meeting), 3000);
        Native.SetWindowPos(meeting, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Check("(가드) 고정한 메모의 '항상 위'가 풀리면 자동으로 복구", Wait(() => Native.IsTopmost(meeting), 4000));
        pin.Toggle();
        Wait(() => !Native.IsTopmost(meeting), 3000);

        // 다른 프로그램을 쓰는 중에도, 모두 보기를 하면 메모가 그 창 앞으로 온다.
        // Windows는 뒤쪽 프로그램이 새로 띄운 창을 활성 창 뒤에 두므로, 활성 창을 기준으로 검사한다.
        Native.PostToggleNotesHotkey(p.Id);
        Check("단축키로 모든 메모 숨김", Wait(() => !Native.IsWindowVisible(meeting), 3000));
        Thread.Sleep(500);
        IntPtr foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out uint foregroundOwner);
        bool canCompare = foreground != IntPtr.Zero && foregroundOwner != p.Id && !Native.IsTopmost(foreground);
        Native.PostToggleNotesHotkey(p.Id);
        bool shown = Wait(() => Native.IsWindowVisible(meeting), 3000);
        if (canCompare)
            Check("모두 보기: 활성 창이던 다른 프로그램보다 앞으로 옴", shown && Wait(() => Native.IsAbove(meeting, foreground), 3000));
        else
            Console.WriteLine("info  활성 창이 없거나 항상 위 창이라 쌓임 순서 검사를 건너뜀");
        Check("모두 보기: 맨 앞 고정은 아님 (항상 위가 아님)", !Native.IsTopmost(meeting));

        // '더 보기' 버튼 → 제목 표시줄 메뉴 → 설정
        AutomationElement? more = ById(ae, "MoreButton");
        Check("'더 보기' 버튼 (접근성 이름)", more?.Current.Name == "더 보기", more?.Current.Name ?? "없음");
        Invoke(more);
        AutomationElement? popupMenu = null;
        IntPtr popupHandle = IntPtr.Zero;
        Wait(() =>
        {
            foreach (IntPtr h in Native.VisibleWindows(p.Id).Where(h => Native.Title(h) == ""))
            {
                AutomationElement? found = AE(h).FindFirst(TreeScope.Subtree, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu));
                if (found is not null) { popupMenu = found; popupHandle = h; return true; }
            }
            return false;
        }, 3000);
        string[] menuNames = popupMenu is null ? [] : [.. popupMenu.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)).Cast<AutomationElement>().Select(e => e.Current.Name)];
        Check("'더 보기' 메뉴에 저장·색·메모 관리·설정",
            menuNames.Contains("텍스트 파일로 저장...") && menuNames.Contains("노랑") && menuNames.Contains("메모 관리") && menuNames.Contains("설정"),
            string.Join(",", menuNames));
        if (popupHandle != IntPtr.Zero)
            Capture(popupHandle, "A2_more_menu.png");
        AutomationElement? settingsItem = popupMenu?.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty, "설정"));
        if (settingsItem is not null)
        {
            Invoke(settingsItem);
            IntPtr settingsFromNote = WaitWindow(p.Id, t => t == "FlowMemo 설정", 4000);
            Check("메모의 '더 보기 → 설정'으로 설정 창 열림", settingsFromNote != IntPtr.Zero);
            if (settingsFromNote != IntPtr.Zero)
            {
                Check("설정 창이 화면 안에 열림", Native.OnScreen(settingsFromNote), Native.Rect(settingsFromNote).ToString());
                Invoke(ById(AE(settingsFromNote), "CloseSettingsButton"));
                Check("설정 창 [닫기] 버튼", Wait(() => !Native.VisibleWindows(p.Id).Any(h => Native.Title(h) == "FlowMemo 설정"), 3000));
            }
        }

        // 기본값(최소화 허용)
        Native.ShowWindow(meeting, Native.SW_SHOWMINNOACTIVE);
        Check("최소화 허용(기본값)이면 최소화됨", Wait(() => Native.IsIconic(meeting), 3000));
        Native.ShowWindow(meeting, Native.SW_SHOWNOACTIVATE);
        Wait(() => !Native.IsIconic(meeting), 3000);

        // 편집 → 창 제목, 끝 줄바꿈, 자동 저장
        AutomationElement? edit = ById(ae, "NoteTextBox");
        ValuePattern value = (ValuePattern)edit!.GetCurrentPattern(ValuePattern.Pattern);
        value.SetValue("장보기\r\n우유\r\n계란");
        Check("내용을 바꾸면 창 제목도 바뀜", Wait(() => Native.Title(meeting) == "장보기 - FlowMemo", 3000), Native.Title(meeting));
        Check("'끝에 빈 줄 유지' 적용", Wait(() => value.Current.Value.EndsWith("\r\n"), 3000), Program.Esc(value.Current.Value));
        Check("몇 초 안에 자동 저장", Wait(() => (Scalar("SELECT Content FROM Notes WHERE Content LIKE '장보기%'") as string)?.Contains("계란") == true, 9000));

        // 새 메모 → 위치 → 빈 메모 닫으면 삭제
        List<IntPtr> before = NoteWindows(p.Id);
        Invoke(ById(ae, "NewButton"));
        IntPtr child = IntPtr.Zero;
        Wait(() => (child = NoteWindows(p.Id).Except(before).FirstOrDefault()) != IntPtr.Zero, 4000);
        Check("새 메모 버튼으로 새 메모", child != IntPtr.Zero);
        Native.RECT parentRect = Native.Rect(meeting), childRect = Native.Rect(child);
        double scale = (parentRect.Right - parentRect.Left) / 300.0; // 메모 기본 너비 300 DIP
        double expectedOffset = 45 * scale;
        Check($"새 메모는 부모 메모 옆(45 DIP = {expectedOffset:0.#}px @ {scale:0.##}배) 아래·오른쪽에 열림",
            Math.Abs(childRect.Left - parentRect.Left - expectedOffset) <= 1.5 && Math.Abs(childRect.Top - parentRect.Top - expectedOffset) <= 1.5,
            $"parent={parentRect} child={childRect}");
        Check("새 메모는 화면 안", Native.OnScreen(child));
        int countWithChild = NoteCount();
        Invoke(ById(AE(child), "CloseButton"));
        Check("빈 메모를 닫으면 DB에서 삭제", Wait(() => NoteCount() == countWithChild - 1 && !NoteWindows(p.Id).Contains(child), 4000), $"db={NoteCount()}");

        before = NoteWindows(p.Id);
        Invoke(ById(ae, "NewButton"));
        IntPtr blank = IntPtr.Zero;
        Wait(() => (blank = NoteWindows(p.Id).Except(before).FirstOrDefault()) != IntPtr.Zero, 4000);
        ((ValuePattern)ById(AE(blank), "NoteTextBox")!.GetCurrentPattern(ValuePattern.Pattern)).SetValue("   \r\n  ");
        int countWithBlank = NoteCount();
        Invoke(ById(AE(blank), "CloseButton"));
        Check("공백·줄바꿈만 있는 메모도 닫으면 삭제", Wait(() => NoteCount() == countWithBlank - 1, 4000), $"db={NoteCount()}");

        // 다시 실행
        int windowsBefore = NoteWindows(p.Id).Count;
        Process second = Launch();
        bool exited = second.WaitForExit(10000);
        Check("실행 중에 다시 실행: 두 번째 프로세스는 오류 없이 끝남", exited && second.ExitCode == 0, exited ? $"exit={second.ExitCode}" : "not exited");
        Check("실행 중에 다시 실행: 새 메모가 열림", Wait(() => NoteWindows(p.Id).Count == windowsBefore + 1, 5000), $"windows={NoteWindows(p.Id).Count}");

        CheckLog("A");
        Kill(p);
    }

    private static void ScenarioManagementAndSettings()
    {
        Console.WriteLine("== UI 시나리오 B: 메모 관리 창 · 최소화 방지 · 설정 창");
        ResetDb(c =>
        {
            SetSetting(c, "Application.StartupBehaviour", "ShowManagementWindow");
            SetSetting(c, "Notes.MinimizeMode", "Prevent");
            InsertNote(c, "첫 번째 메모\r\n관리 창 미리 보기", 200, 200, "Green", false, false);
            InsertNote(c, "두 번째 메모 - 분홍 (DB에는 열림)", 260, 260, "Pink", true, true);
            InsertNote(c, "세 번째 메모\r\n- 할 일 1\r\n- 할 일 2", 320, 320, "Purple", false, false);
        });

        Process p = Launch();
        IntPtr management = WaitWindow(p.Id, t => t == "FlowMemo - 메모 관리");
        Check("시작할 때 '메모 관리 창 열기' 설정 동작", management != IntPtr.Zero);
        Thread.Sleep(800);
        Capture(management, "B1_management.png");

        AutomationElement mae = AE(management);
        AutomationElement? newButton = ById(mae, "NewNoteButton");
        Check("관리 창 툴바 버튼 접근성 이름", newButton?.Current.Name == "새 메모" && ById(mae, "SettingsButton")?.Current.Name == "설정");
        int listItems = All(mae, ControlType.ListItem).Count;
        Check("관리 창에 메모 3개 표시", listItems == 3, $"items={listItems}");

        List<IntPtr> before = NoteWindows(p.Id);
        Invoke(newButton);
        IntPtr note = IntPtr.Zero;
        Wait(() => (note = NoteWindows(p.Id).Except(before).FirstOrDefault()) != IntPtr.Zero, 5000);
        Check("관리 창에서 새 메모", note != IntPtr.Zero);
        Check("관리 창에 새 메모가 바로 추가됨", Wait(() => All(mae, ControlType.ListItem).Count == 4, 3000));
        bool badgeShown = Wait(() => All(mae, ControlType.Text).Cast<AutomationElement>().Count(e => e.Current.Name == "● 열림") == 1, 3000);
        Check("관리 창: 지금 열려 있는 메모에만 '● 열림' 표시 (DB의 IsOpen=1인 닫힌 메모는 표시 안 함)", badgeShown,
            $"badges={All(mae, ControlType.Text).Cast<AutomationElement>().Count(e => e.Current.Name == "● 열림")}");
        Thread.Sleep(300);
        Capture(management, "B1b_management_with_open_note.png");

        Native.ShowWindow(note, Native.SW_SHOWMINNOACTIVE);
        Thread.Sleep(150);
        Check("'최소화 허용 안 함'이면 최소화해도 바로 복원", Wait(() => !Native.IsIconic(note), 3000));
        Check("복원 뒤 고정 안 한 메모는 '항상 위'가 아님", Wait(() => !Native.IsTopmost(note), 3000));

        // 설정 창
        Invoke(ById(mae, "SettingsButton"));
        IntPtr settings = WaitWindow(p.Id, t => t == "FlowMemo 설정");
        Check("설정 창 열림 (제목 한국어)", settings != IntPtr.Zero);
        Thread.Sleep(600);
        AutomationElement sae = AE(settings);

        string[] tabNames = [.. All(sae, ControlType.TabItem).Cast<AutomationElement>().Select(e => e.Current.Name)];
        Check("설정 탭 이름", string.Join(",", tabNames) == "앱,메모,편집기,도구,동기화", string.Join(",", tabNames));
        Check("설정 창: 트레이 전용·시작 프로그램·단축키 항목 (기본 단축키 Ctrl+Alt+H)",
            ById(sae, "TrayOnlyCheckBox") is not null && ById(sae, "RunAtStartupCheckBox") is not null
            && ((ValuePattern)ById(sae, "ToggleNotesHotkeyBox")!.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "Ctrl+Alt+H");

        int index = 0;
        foreach (AutomationElement tab in All(sae, ControlType.TabItem))
        {
            index++;
            string name = tab.Current.Name;
            ((SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Thread.Sleep(500);
            Capture(settings, $"B2_settings_{index}_{name}.png");

            AutomationElement? scroller = sae.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true))
                .Cast<AutomationElement>()
                .FirstOrDefault(e => ((ScrollPattern)e.GetCurrentPattern(ScrollPattern.Pattern)).Current.VerticallyScrollable);
            if (scroller is not null)
            {
                ((ScrollPattern)scroller.GetCurrentPattern(ScrollPattern.Pattern)).SetScrollPercent(ScrollPattern.NoScroll, 100);
                Thread.Sleep(400);
                Capture(settings, $"B2_settings_{index}_{name}_bottom.png");
                ((ScrollPattern)scroller.GetCurrentPattern(ScrollPattern.Pattern)).SetScrollPercent(ScrollPattern.NoScroll, 0);
            }
        }

        // 메모 탭: 투명도 모드 → 아래 항목 활성 상태 + 자동 저장
        AutomationElement notesTab = All(sae, ControlType.TabItem).Cast<AutomationElement>().First(e => e.Current.Name == "메모");
        ((SelectionItemPattern)notesTab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Thread.Sleep(500);
        AutomationElement[] combos = [.. All(sae, ControlType.ComboBox).Cast<AutomationElement>()];
        Check("메모 탭 콤보 상자 5개", combos.Length == 5, $"combos={combos.Length}");
        if (combos.Length == 5)
        {
            // 위치 콤보 펼친 모습 캡처 (한글 선택지)
            ExpandCollapsePattern positionExpand = (ExpandCollapsePattern)combos[0].GetCurrentPattern(ExpandCollapsePattern.Pattern);
            positionExpand.Expand();
            Thread.Sleep(500);
            IntPtr popup = Native.VisibleWindows(p.Id).FirstOrDefault(h => Native.Title(h) == "" && Native.Rect(h).Bottom - Native.Rect(h).Top > 60);
            if (popup != IntPtr.Zero)
                Capture(popup, "B3_position_dropdown.png");
            positionExpand.Collapse();
            Thread.Sleep(300);

            SelectComboIndex(combos[4], 0); // 사용 안 함
            bool allDisabled = All(sae, ControlType.Slider).Cast<AutomationElement>().All(s => !s.Current.IsEnabled)
                && !All(sae, ControlType.CheckBox).Cast<AutomationElement>().Last().Current.IsEnabled;
            Check("투명도 '사용 안 함' → 아래 항목이 바로 비활성", allDisabled);
            SelectComboIndex(combos[4], 2); // 고정한 메모만
            bool allEnabled = All(sae, ControlType.Slider).Cast<AutomationElement>().All(s => s.Current.IsEnabled)
                && All(sae, ControlType.CheckBox).Cast<AutomationElement>().Last().Current.IsEnabled;
            Check("투명도 '고정한 메모만' → 아래 항목이 바로 활성", allEnabled);
            Check("설정을 바꾸면 곧바로 DB에 저장 (종료를 기다리지 않음)",
                Wait(() => Setting("Notes.TransparencyMode") == "WhenPinned", 4000), Setting("Notes.TransparencyMode") ?? "null");
        }

        // 설정 창 위치: 다시 활성화해도 그대로
        TransformPattern move = (TransformPattern)sae.GetCurrentPattern(TransformPattern.Pattern);
        move.Move(60, 70);
        Thread.Sleep(400);
        Native.RECT r1 = Native.Rect(settings);
        mae.SetFocus();
        Thread.Sleep(400);
        sae.SetFocus();
        Thread.Sleep(600);
        Native.RECT r2 = Native.Rect(settings);
        Invoke(ById(mae, "SettingsButton"));
        Thread.Sleep(600);
        Native.RECT r3 = Native.Rect(settings);
        Check("설정 창을 옮긴 뒤 다시 활성화·다시 열어도 제자리 (예전: 가운데로 튐)",
            r1.Left == r2.Left && r1.Top == r2.Top && r1.Left == r3.Left && r1.Top == r3.Top, $"{r1} / {r2} / {r3}");
        Check("설정 창은 하나만 열림", Native.VisibleWindows(p.Id).Count(h => Native.Title(h) == "FlowMemo 설정") == 1);

        Invoke(ById(sae, "CloseSettingsButton"));
        Check("설정 창 [닫기] 버튼으로 닫기", Wait(() => !Native.VisibleWindows(p.Id).Any(h => Native.Title(h) == "FlowMemo 설정"), 3000));

        // 관리 창 닫고 다시 열기 (예전: 닫힌 창 참조가 남아 설정 창 참조를 지움)
        ((WindowPattern)mae.GetCurrentPattern(WindowPattern.Pattern)).Close();
        Check("관리 창 닫기", Wait(() => !Native.VisibleWindows(p.Id).Any(h => Native.Title(h) == "FlowMemo - 메모 관리"), 3000));

        CheckLog("B");
        Kill(p);
    }

    private static void SelectComboIndex(AutomationElement combo, int index)
    {
        ExpandCollapsePattern ec = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        ec.Expand();
        Thread.Sleep(400);
        AutomationElement[] items = [.. combo.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Cast<AutomationElement>()];
        ((SelectionItemPattern)items[index].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Thread.Sleep(300);
        if (ec.Current.ExpandCollapseState == ExpandCollapseState.Expanded)
            ec.Collapse();
        Thread.Sleep(300);
    }

    private static void ScenarioExitWithoutTray()
    {
        Console.WriteLine("== UI 시나리오 C: 알림 영역 아이콘 없이 마지막 메모를 닫으면 정상 종료·저장");
        ResetDb(c =>
        {
            SetSetting(c, "Application.ShowNotifyIcon", "false");
            InsertNote(c, "종료 테스트\r\n", 150, 150, "Orange", false, true);
        });

        Process p = Launch();
        IntPtr note = IntPtr.Zero;
        Wait(() => (note = NoteWindows(p.Id).FirstOrDefault()) != IntPtr.Zero, 8000);
        Check("메모 열림", note != IntPtr.Zero);
        AutomationElement ae = AE(note);
        ((ValuePattern)ById(ae, "NoteTextBox")!.GetCurrentPattern(ValuePattern.Pattern)).SetValue("닫기 직전에 쓴 내용");
        Thread.Sleep(300);
        Invoke(ById(ae, "CloseButton"));
        bool exited = p.WaitForExit(10000);
        Check("마지막 메모를 닫으면 오류 없이 종료", exited && p.ExitCode == 0, exited ? $"exit={p.ExitCode}" : "still running");
        Check("닫기 직전에 쓴 내용이 저장됨", (Scalar("SELECT Content FROM Notes") as string)?.StartsWith("닫기 직전에 쓴 내용") == true, Program.Esc(Scalar("SELECT Content FROM Notes") as string));
        CheckLog("C");
        Kill(p);
    }

    // ---------------------------------------------------------------- 휴지통 · 잠금

    private static AutomationElement[] ListItems(AutomationElement window)
        => [.. All(window, ControlType.ListItem).Cast<AutomationElement>()];

    private static AutomationElement? ListItem(AutomationElement window, string namePrefix)
        => ListItems(window).FirstOrDefault(e => e.Current.Name.StartsWith(namePrefix));

    private static void Select(AutomationElement? item, bool add = false)
    {
        SelectionItemPattern pattern = (SelectionItemPattern)item!.GetCurrentPattern(SelectionItemPattern.Pattern);
        if (add) pattern.AddToSelection(); else pattern.Select();
        Thread.Sleep(200);
    }

    private static bool HasText(AutomationElement window, string text)
        => All(window, ControlType.Text).Cast<AutomationElement>().Any(e => e.Current.Name == text);

    private static IntPtr WaitDialog(int pid, int ms = 4000)
        => WaitWindowByClass(pid, "#32770", ms);

    private static IntPtr WaitWindowByClass(int pid, string className, int ms)
    {
        IntPtr found = IntPtr.Zero;
        Wait(() => (found = Native.VisibleWindows(pid).FirstOrDefault(h => Native.ClassName(h) == className)) != IntPtr.Zero, ms);
        return found;
    }

    /// <summary>메시지 상자에서 이름이 <paramref name="buttonPrefix"/>로 시작하는 버튼을 누른다 (한국어 Windows: "예(Y)", "아니요(N)").</summary>
    private static string PressDialogButton(IntPtr dialog, string buttonPrefix)
    {
        // 메시지 상자는 Win32 창이라 UIA Invoke가 막 뜬 직후에는 실패할 수 있다. 버튼 창에 BM_CLICK을 보낸다.
        string text = "";
        IntPtr buttonHandle = IntPtr.Zero;
        Wait(() =>
        {
            AutomationElement root = AE(dialog);
            text = string.Join(" / ", All(root, ControlType.Text).Cast<AutomationElement>().Select(e => e.Current.Name));
            AutomationElement? button = All(root, ControlType.Button).Cast<AutomationElement>().FirstOrDefault(b => b.Current.Name.StartsWith(buttonPrefix));
            buttonHandle = button is null ? IntPtr.Zero : new IntPtr(button.Current.NativeWindowHandle);
            return buttonHandle != IntPtr.Zero;
        }, 3000);
        if (buttonHandle == IntPtr.Zero)
            throw new InvalidOperationException($"메시지 상자에서 '{buttonPrefix}' 버튼을 찾지 못함 ({text})");
        Native.PostMessage(buttonHandle, 0x00F5 /* BM_CLICK */, IntPtr.Zero, IntPtr.Zero);
        return text;
    }

    private static void ScenarioTrash()
    {
        Console.WriteLine("== UI 시나리오 E: 휴지통 (삭제 → 휴지통 → 복원 → 영구 삭제 → 비우기)");
        ResetDb(c =>
        {
            InsertNote(c, "열린 메모\r\n지우면 창도 닫혀야 함", 160, 160, "Yellow", false, true);
            InsertNote(c, "닫힌 메모 하나", 220, 220, "Blue", false, false);
            InsertNote(c, "닫힌 메모 둘", 280, 280, "Green", false, false);
        });

        Process p = Launch();
        IntPtr note = IntPtr.Zero;
        Wait(() => (note = NoteWindows(p.Id).FirstOrDefault()) != IntPtr.Zero);
        Check("열린 메모 복원", note != IntPtr.Zero);

        // 메모의 '더 보기' → 메모 관리
        Invoke(ById(AE(note), "MoreButton"));
        AutomationElement? managementItem = null;
        Wait(() => (managementItem = Native.VisibleWindows(p.Id).Where(h => Native.Title(h) == "")
            .Select(h => AE(h).FindFirst(TreeScope.Subtree, new PropertyCondition(AutomationElement.NameProperty, "메모 관리")))
            .FirstOrDefault(e => e is not null)) is not null, 3000);
        Invoke(managementItem);
        IntPtr management = WaitWindow(p.Id, t => t == "FlowMemo - 메모 관리");
        Check("메모 관리 창 열림", management != IntPtr.Zero);
        Thread.Sleep(600);
        AutomationElement mae = AE(management);

        Check("메모 목록: 3개, 휴지통 (0)", ListItems(mae).Length == 3 && HasText(mae, "휴지통 (0)"), $"items={ListItems(mae).Length}");
        Check("선택한 메모가 없으면 [삭제] 비활성", ById(mae, "DeleteButton")?.Current.IsEnabled == false);

        // 열린 메모를 휴지통으로: 확인 없이, 창도 닫힘
        Select(ListItem(mae, "열린 메모"));
        Check("메모를 선택하면 [삭제] 활성", Wait(() => ById(mae, "DeleteButton")?.Current.IsEnabled == true, 2000));
        Invoke(ById(mae, "DeleteButton"));
        Check("삭제: 목록에서 빠지고 휴지통 (1)", Wait(() => ListItems(mae).Length == 2 && HasText(mae, "휴지통 (1)"), 3000), $"items={ListItems(mae).Length}");
        Check("삭제: 확인 창 없이 바로 (휴지통에서 되살릴 수 있으므로)", WaitDialog(p.Id, 800) == IntPtr.Zero);
        Check("삭제: 열려 있던 메모 창이 닫힘", Wait(() => !NoteWindows(p.Id).Contains(note), 3000));
        Check("삭제: DB에서는 지우지 않고 휴지통 표시 + 다음 시작 때 안 열림",
            NoteCount() == 3 && TrashCountInDb() == 1
            && Convert.ToInt32(Scalar("SELECT IsOpen FROM Notes WHERE Content LIKE '열린 메모%'")) == 0);
        Thread.Sleep(300);
        Capture(management, "E1_management_after_delete.png");

        // 휴지통 보기
        Invoke(ById(mae, "TrashButton"));
        Check("휴지통 보기: 창 제목", Wait(() => Native.Title(management) == "FlowMemo - 휴지통", 3000), Native.Title(management));
        Check("휴지통 보기: 지운 메모 1개", Wait(() => ListItems(mae).Length == 1, 3000), $"items={ListItems(mae).Length}");
        Check("휴지통 카드: 완전히 지워지기까지 남은 날 (30일 남음)", HasText(mae, "30일 남음"));
        Check("휴지통 보기: [복원]·[영구 삭제]·[휴지통 비우기], [새 메모]는 숨김",
            ById(mae, "RestoreButton") is not null && ById(mae, "DeletePermanentlyButton") is not null
            && ById(mae, "EmptyTrashButton")?.Current.IsEnabled == true && ById(mae, "NewNoteButton") is null);
        Thread.Sleep(300);
        Capture(management, "E2_trash_view.png");

        Select(ListItems(mae).First());
        Invoke(ById(mae, "RestoreButton"));
        Check("복원: 휴지통에서 빠짐", Wait(() => ListItems(mae).Length == 0, 3000));
        Check("복원: 휴지통이 비었다는 안내", Wait(() => All(mae, ControlType.Text).Cast<AutomationElement>().Any(e => e.Current.Name.StartsWith("휴지통이 비어 있습니다.")), 2000));
        Check("복원: DB에서 휴지통 표시 해제", TrashCountInDb() == 0);
        Thread.Sleep(300);
        Capture(management, "E3_trash_empty.png");

        Invoke(ById(mae, "BackToNotesButton"));
        Check("메모 목록으로 돌아오면 3개", Wait(() => Native.Title(management) == "FlowMemo - 메모 관리" && ListItems(mae).Length == 3, 3000));

        // 두 개를 지운 뒤 하나는 영구 삭제, 나머지는 비우기
        Select(ListItem(mae, "닫힌 메모 하나"));
        Select(ListItem(mae, "닫힌 메모 둘"), add: true);
        Invoke(ById(mae, "DeleteButton"));
        Check("여러 개 삭제: 휴지통 (2)", Wait(() => HasText(mae, "휴지통 (2)") && ListItems(mae).Length == 1, 3000));
        Invoke(ById(mae, "TrashButton"));
        Wait(() => ListItems(mae).Length == 2, 3000);

        Select(ListItem(mae, "닫힌 메모 하나"));
        Invoke(ById(mae, "DeletePermanentlyButton"));
        IntPtr dialog = WaitDialog(p.Id);
        Check("영구 삭제는 확인 창을 띄움", dialog != IntPtr.Zero);
        if (dialog != IntPtr.Zero)
        {
            string message = PressDialogButton(dialog, "아니요");
            Check("확인 창 문구: 몇 개를 지우는지 + 되돌릴 수 없음", message.Contains("1개") && message.Contains("되돌릴 수 없습니다"), message);
            Check("영구 삭제 '아니요': 그대로", Wait(() => WaitDialog(p.Id, 200) == IntPtr.Zero, 2000) && NoteCount() == 3 && ListItems(mae).Length == 2);

            Invoke(ById(mae, "DeletePermanentlyButton"));
            dialog = WaitDialog(p.Id);
            if (dialog != IntPtr.Zero)
                PressDialogButton(dialog, "예");
            Check("영구 삭제 '예': DB에서 지움", Wait(() => NoteCount() == 2 && ListItems(mae).Length == 1, 3000), $"db={NoteCount()} items={ListItems(mae).Length}");
        }

        Invoke(ById(mae, "EmptyTrashButton"));
        dialog = WaitDialog(p.Id);
        Check("휴지통 비우기도 확인 창을 띄움", dialog != IntPtr.Zero);
        if (dialog != IntPtr.Zero)
        {
            PressDialogButton(dialog, "예");
            Check("휴지통 비우기: 휴지통 (0), DB에는 지우지 않은 메모 1개만",
                Wait(() => ListItems(mae).Length == 0 && TrashCountInDb() == 0 && NoteCount() == 1, 3000), $"db={NoteCount()}");
            Check("휴지통이 비면 [휴지통 비우기] 비활성", Wait(() => ById(mae, "EmptyTrashButton")?.Current.IsEnabled == false, 2000));
        }

        CheckLog("E");
        Kill(p);
    }

    private static void ScenarioLockRestoredAndDataFolder()
    {
        Console.WriteLine("== UI 시나리오 F: 잠금 상태 복원 · 설정 창 데이터 폴더");
        ResetDb(c => InsertNote(c, "잠가 둔 메모\r\n", 150, 150, "Purple", false, true, locked: true));

        Process p = Launch();
        IntPtr note = IntPtr.Zero;
        Wait(() => (note = NoteWindows(p.Id).FirstOrDefault()) != IntPtr.Zero);
        Check("잠긴 메모 열림", note != IntPtr.Zero);
        Thread.Sleep(800);
        AutomationElement ae = AE(note);
        ValuePattern value = (ValuePattern)ById(ae, "NoteTextBox")!.GetCurrentPattern(ValuePattern.Pattern);
        Check("다시 실행해도 잠금 유지 (읽기 전용)", value.Current.IsReadOnly);
        AutomationElement? lockIndicator = ById(ae, "LockIndicator");
        Check("잠금 표시가 보임", lockIndicator is not null && !lockIndicator.Current.IsOffscreen);
        Capture(note, "F1_locked_note.png");

        // 설정 창: 데이터 폴더
        Invoke(ById(ae, "MoreButton"));
        AutomationElement? settingsItem = null;
        Wait(() => (settingsItem = Native.VisibleWindows(p.Id).Where(h => Native.Title(h) == "")
            .Select(h => AE(h).FindFirst(TreeScope.Subtree, new PropertyCondition(AutomationElement.NameProperty, "설정")))
            .FirstOrDefault(e => e is not null)) is not null, 3000);
        Invoke(settingsItem);
        IntPtr settings = WaitWindow(p.Id, t => t == "FlowMemo 설정");
        Check("설정 창 열림", settings != IntPtr.Zero);
        Thread.Sleep(600);
        AutomationElement sae = AE(settings);
        string shownPath = ById(sae, "DataFolderPathText")?.Current.Name ?? "";
        Check("설정 '앱' 탭에 데이터 폴더 경로 (포터블이면 앱 폴더)",
            string.Equals(shownPath, Path.TrimEndingDirectorySeparator(_appDir), StringComparison.OrdinalIgnoreCase), shownPath);
        Check("[데이터 폴더 열기] 버튼", ById(sae, "OpenDataFolderButton")?.Current.IsEnabled == true);
        Capture(settings, "F2_settings_data.png");

        CheckLog("F");
        Kill(p);
    }
}
