using FlowMemo.Core;
using FlowMemo.Core.Configurations;
using FlowMemo.Core.DataTransferObjects;
using FlowMemo.Core.Repositories;
using FlowMemo.Core.Sync;

using static Program;

/// <summary>동기화. Units는 서버 없이, Run은 로컬 Worker(wrangler dev)에 PC 두 대를 연결해 검사한다.</summary>
internal static class SyncTests
{
    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();
    private static void Wait(Task task) => task.GetAwaiter().GetResult();

    public static void Units()
    {
        Console.WriteLine("== 동기화 단위 테스트 (비교 · 합치기 · 암호화 · 연결 코드)");

        // 비교
        IReadOnlyList<DiffRow> rows = TextDiff.Compute("하나\r\n둘\r\n셋", "하나\n둘 고침\n셋\n넷");
        Check("비교: 같은 줄·바뀐 줄·추가된 줄", string.Join(",", rows.Select(r => r.Kind)) == "Same,Modified,Same,Added", string.Join(",", rows.Select(r => r.Kind)));
        Check("비교: 삭제된 줄", TextDiff.Compute("a\nb\nc", "a\nc").Count(r => r.Kind == DiffKind.Removed && r.Left == "b") == 1);
        Check("비교: 바뀐 줄 안에서 달라진 부분", TextDiff.ChangedSpan("안녕하세요", "안녕히가세요") == (2, 2));
        Check("비교: 줄바꿈 형식 차이는 무시", TextDiff.Compute("a\r\nb", "a\nb").All(r => r.Kind == DiffKind.Same));

        // 합치기
        MergeResult auto = ThreeWayMerge.Merge("a\nb\nc\nd", "a 이PC\nb\nc\nd", "a\nb\nc\nd 다른PC", "이 PC", "다른 PC");
        Check("합치기: 서로 다른 줄을 고치면 자동으로 합침", auto.ConflictCount == 0 && auto.Text == "a 이PC\r\nb\r\nc\r\nd 다른PC", Esc(auto.Text));
        MergeResult same = ThreeWayMerge.Merge("a\nb", "a\nb 같이", "a\nb 같이", "이 PC", "다른 PC");
        Check("합치기: 양쪽이 똑같이 고치면 충돌 아님", same.ConflictCount == 0 && same.Text == "a\r\nb 같이", Esc(same.Text));
        MergeResult conflict = ThreeWayMerge.Merge("a\nb\nc", "a\nb 이PC\nc", "a\nb 다른PC\nc", "이 PC", "다른 PC");
        Check("합치기: 같은 줄을 다르게 고치면 양쪽을 표시", conflict.ConflictCount == 1 && ThreeWayMerge.HasConflictMarkers(conflict.Text)
            && conflict.Text.Contains("b 이PC") && conflict.Text.Contains("b 다른PC"), Esc(conflict.Text));
        MergeResult append = ThreeWayMerge.Merge("a", "a\nb", "a\nc", "이 PC", "다른 PC");
        Check("합치기: 끝에 서로 다른 줄을 추가하면 충돌", append.ConflictCount == 1, Esc(append.Text));
        Check("합치기: 표시 줄이 없으면 그대로", !ThreeWayMerge.HasConflictMarkers("평범한 메모\r\n=== 제목"));

        // 암호화
        SyncCrypto crypto = new(SyncCrypto.GenerateKey());
        string id = new('a', 32);
        string blob = crypto.Encrypt("비밀 메모", id);
        Check("암호화: 되돌리면 원문, 암호문에 원문 없음", crypto.Decrypt(blob, id) == "비밀 메모" && !blob.Contains("비밀"));
        Check("암호화: 같은 내용도 매번 다른 암호문", crypto.Encrypt("비밀 메모", id) != blob);
        Check("암호화: 다른 키로는 풀 수 없음", Throws(() => new SyncCrypto(SyncCrypto.GenerateKey()).Decrypt(blob, id)));
        Check("암호화: 다른 메모의 암호문으로 바꿔치기하면 실패", Throws(() => crypto.Decrypt(blob, new string('b', 32))));
        byte[] tampered = Convert.FromBase64String(blob);
        tampered[^1] ^= 1;
        Check("암호화: 변조하면 실패", Throws(() => crypto.Decrypt(Convert.ToBase64String(tampered), id)));

        // 연결 코드
        SyncConnectionCode code = new(SyncCodeKind.Pair, "https://example.workers.dev", "secret-value", Convert.ToBase64String(SyncCrypto.GenerateKey()));
        Check("연결 코드: 왕복", SyncConnectionCode.TryParse(" " + code + "\n", out SyncConnectionCode parsed) && parsed == code);
        Check("연결 코드: HTTP 서버 주소는 거부 (로컬 제외)",
            !SyncConnectionCode.IsAllowedServerUrl("http://example.com") && SyncConnectionCode.IsAllowedServerUrl("http://127.0.0.1:8799"));
        Check("연결 코드: 잘못된 값은 거부", !SyncConnectionCode.TryParse("FMSYNC1.@@@", out _) && !SyncConnectionCode.TryParse("hello", out _) && !SyncConnectionCode.TryParse(null, out _));
        Check("연결 코드: 다른 PC 코드에 암호화 키가 없으면 거부",
            !SyncConnectionCode.TryParse(new SyncConnectionCode(SyncCodeKind.Pair, "https://example.workers.dev", "s", null).ToString(), out _));
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (Exception) { return true; }
    }

    /// <summary>임시 DB를 가진 가상 PC.</summary>
    internal sealed class Pc
    {
        public readonly NoteRepository Notes;
        public readonly SyncRepository Sync;
        public SyncCredentials Credentials = null!;
        public SyncApi Api = null!;
        private SyncEngine _engine = null!;

        public Pc(string dir)
        {
            DatabaseConfiguration config = new(dir);
            Wait(DatabaseInitialiser.Initialise(config.ConnectionString));
            Notes = new NoteRepository(config);
            Sync = new SyncRepository(config);
        }

        public void Connect(SyncConnectionCode code, string name)
            => Use(Wait(SyncConnector.Connect(code, name)));

        public void Use(SyncCredentials credentials)
        {
            Credentials = credentials;
            Api = new SyncApi(credentials.ServerUrl, credentials.Token);
            _engine = new SyncEngine(Api, Sync, new SyncCrypto(Convert.FromBase64String(credentials.VaultKey)));
        }

        public SyncCycleResult Cycle() => Wait(_engine.RunCycle());

        public int Create(string content, string colour = "Yellow")
            => Wait(Notes.Create(new NoteDto(0, content, 10, 20, 300, 300, 1, 1, colour, false, true, false, DateTime.UtcNow, DateTime.UtcNow, null)));

        public NoteDto Note(int id) => Wait(Notes.GetById(id));

        public void Edit(int id, string content, string? colour = null)
        {
            NoteDto note = Note(id);
            Wait(Notes.Update(note with { Content = content, ThemeColourScheme = colour ?? note.ThemeColourScheme, UpdatedAt = DateTime.UtcNow }));
        }

        public NoteSyncRow Row(int id) => Wait(Sync.GetByNoteId(id))!;
        public NoteSyncRow? RowBySyncId(string syncId) => Wait(Sync.GetBySyncId(syncId));
        public int IdOf(string syncId) => RowBySyncId(syncId)!.NoteId;

        public void Accept(int id)
        {
            NoteSyncRow row = Row(id);
            Edit(id, row.PendingContent!, row.PendingColour);
            Wait(Sync.CommitPending(id));
        }

        public void KeepLocal(int id) => Wait(Sync.CommitPending(id));
        public void Ignore(int id) => Wait(Sync.Ignore(id));
    }

    public static void Run(string dir, string serverUrl, string setupCode, string appDir, string outDir)
    {
        Console.WriteLine("== 동기화 테스트 (로컬 Worker에 PC 두 대 연결)");
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        Pc pc1 = new(Path.Combine(dir, "pc1"));
        Pc pc2 = new(Path.Combine(dir, "pc2"));

        // 연결과 보안
        SyncConnectionCode setup = new(SyncCodeKind.Setup, serverUrl, setupCode, null);
        Check("연결: 잘못된 등록 코드는 거부", Throws(() => Wait(SyncConnector.Connect(new SyncConnectionCode(SyncCodeKind.Setup, serverUrl, "wrong-code", null), "나쁜 PC"))));
        pc1.Connect(setup, "회사 PC");
        Check("연결: 첫 PC 등록, 키와 복구 비밀값 생성", pc1.Credentials.RecoverySecret is not null && Convert.FromBase64String(pc1.Credentials.VaultKey).Length == 32);
        Check("연결: 등록 코드는 한 번만 쓸 수 있음", Throws(() => Wait(SyncConnector.Connect(setup, "다른 사람 PC"))));

        string pairSecret = SyncSecrets.NewSecret();
        Wait(pc1.Api.CreatePairing(SyncSecrets.Sha256Hex(pairSecret)));
        SyncConnectionCode pair = new(SyncCodeKind.Pair, serverUrl, pairSecret, pc1.Credentials.VaultKey);
        pc2.Connect(pair, "노트북");
        Check("연결: 연결 코드로 두 번째 PC 등록", pc2.Credentials.DeviceId != pc1.Credentials.DeviceId);
        Check("연결: 연결 코드는 재사용 불가", Throws(() => Wait(SyncConnector.Connect(pair, "다른 사람 PC"))));
        Check("보안: 토큰 없이는 접근 불가", Unauthorized(new SyncApi(serverUrl)));
        Check("보안: 위조 토큰은 거부", Unauthorized(new SyncApi(serverUrl, new string('a', 32) + "." + new string('A', 43))));

        // 새 메모
        int blank = pc1.Create("   ");
        pc1.Cycle();
        Check("빈 새 메모는 올리지 않음", pc1.Row(blank).BaseRevision == 0);

        int a1 = pc1.Create("첫 줄\r\n둘째 줄\r\n셋째 줄", "Blue");
        string syncId = pc1.Row(a1).SyncId;
        Check("메모마다 고유 ID", syncId.Length == 32 && syncId != pc1.Row(blank).SyncId, syncId);
        pc1.Cycle();
        Check("서버에는 암호문만 저장", Wait(pc1.Api.GetChanges(0)).Changes.All(c => !c.Blob.Contains("첫 줄")));

        pc2.Cycle();
        int a2 = pc2.IdOf(syncId);
        Check("다른 PC에서 만든 메모는 닫힌 메모로 들어옴", pc2.Note(a2).Content.StartsWith("첫 줄") && !pc2.Note(a2).IsOpen && pc2.Note(a2).ThemeColourScheme == "Blue");
        Check("같은 메모는 양쪽에서 같은 ID", pc2.Row(a2).SyncId == syncId && pc2.Row(a2).Status == SyncNoteStatus.None);
        Check("창 위치는 동기화하지 않음", pc2.Note(a2).X != pc1.Note(a1).X);

        // 가져오기
        string text1 = "첫 줄 고침\r\n둘째 줄\r\n셋째 줄";
        pc1.Edit(a1, text1);
        pc1.Cycle();
        pc2.Cycle();
        Check("다른 PC 변경: 묻기 전에는 메모를 바꾸지 않음", pc2.Row(a2).Status == SyncNoteStatus.Incoming && pc2.Note(a2).Content.StartsWith("첫 줄\r\n"));
        Check("다른 PC 변경: 어느 PC에서 고쳤는지 표시", pc2.Row(a2).PendingDevice == "회사 PC");
        pc2.Accept(a2);
        Check("가져오기: 내용이 바뀌고 안내가 사라짐", pc2.Note(a2).Content == text1 && pc2.Row(a2).Status == SyncNoteStatus.None);
        long revisionAfterAccept = pc2.Row(a2).BaseRevision;
        pc2.Cycle();
        Check("가져오기: 다시 올리지 않음", pc2.Row(a2).BaseRevision == revisionAfterAccept);

        // 건너뛰기
        pc1.Edit(a1, text1 + "\r\n넷째 줄");
        pc1.Cycle();
        pc2.Cycle();
        pc2.Ignore(a2);
        Check("건너뛰기: 안내만 사라지고 메모는 그대로", pc2.Row(a2).Status == SyncNoteStatus.None && pc2.Note(a2).Content == text1);
        pc2.Cycle();
        Check("건너뛰기: 다시 묻지 않음", pc2.Row(a2).Status == SyncNoteStatus.None);
        string text2 = text1 + "\r\n넷째 줄\r\n다섯째 줄";
        pc1.Edit(a1, text2);
        pc1.Cycle();
        pc2.Cycle();
        Check("건너뛰기: 더 새로운 변경이 오면 다시 알림", pc2.Row(a2).Status == SyncNoteStatus.Incoming);
        pc2.Accept(a2);

        // 충돌: 합치기
        pc1.Edit(a1, text2.Replace("첫 줄 고침", "첫 줄 회사에서"));
        pc2.Edit(a2, text2.Replace("다섯째 줄", "다섯째 줄 노트북에서"));
        pc1.Cycle();
        pc2.Cycle();
        NoteSyncRow conflictRow = pc2.Row(a2);
        Check("충돌: 양쪽에서 고치면 덮어쓰지 않고 충돌로 표시", conflictRow.Status == SyncNoteStatus.Conflict && pc2.Note(a2).Content.Contains("노트북에서"));
        MergeResult merged = ThreeWayMerge.Merge(conflictRow.BaseContent, conflictRow.Content, conflictRow.PendingContent, "이 PC", "다른 PC");
        Check("충돌: 서로 다른 줄이면 자동으로 합침", merged.ConflictCount == 0 && merged.Text.Contains("회사에서") && merged.Text.Contains("노트북에서"), Esc(merged.Text));
        pc2.Edit(a2, merged.Text);
        pc2.KeepLocal(a2);
        pc2.Cycle();
        pc1.Cycle();
        Check("충돌 해결: 합친 내용이 상대 PC에 도착", pc1.Row(a1).Status == SyncNoteStatus.Incoming && pc1.Row(a1).PendingContent == merged.Text);
        pc1.Accept(a1);
        Check("충돌 해결: 양쪽 내용이 같아짐", pc1.Note(a1).Content == pc2.Note(a2).Content);

        // 충돌: 이 PC 유지
        pc1.Edit(a1, "회사 내용");
        pc2.Edit(a2, "노트북 내용");
        pc1.Cycle();
        pc2.Cycle();
        pc2.KeepLocal(a2);
        pc2.Cycle();
        pc1.Cycle();
        Check("이 PC 유지: 상대 PC에 변경으로 알려짐", pc2.Note(a2).Content == "노트북 내용" && pc1.Row(a1).PendingContent == "노트북 내용");
        pc1.Accept(a1);

        // 고치다 똑같아진 경우
        pc1.Edit(a1, "똑같은 내용");
        pc1.Cycle();
        pc2.Cycle();
        pc2.Edit(a2, "똑같은 내용");
        pc2.Cycle();
        Check("직접 고쳐서 똑같아지면 안내가 사라짐", pc2.Row(a2).Status == SyncNoteStatus.None);

        // 색만 변경
        pc1.Edit(a1, "똑같은 내용", "Pink");
        pc1.Cycle();
        SyncCycleResult colourResult = pc2.Cycle();
        Check("색만 바뀌면 묻지 않고 적용", pc2.Row(a2).Status == SyncNoteStatus.None && pc2.Note(a2).ThemeColourScheme == "Pink" && colourResult.ColourChanges.ContainsKey(a2));

        // 편집 중 표시
        Wait(pc1.Api.SendPresence(syncId));
        Check("편집 중 표시: 다른 PC에 보임", pc2.Cycle().Presence.Any(p => p.Id == syncId && p.DeviceName == "회사 PC"));
        Check("편집 중 표시: 자기 자신은 안 보임", pc1.Cycle().Presence.Count == 0);

        // 삭제
        int b1 = pc1.Create("지울 메모");
        string bId = pc1.Row(b1).SyncId;
        pc1.Cycle();
        pc2.Cycle();
        int b2 = pc2.IdOf(bId);
        Wait(pc1.Notes.MoveToTrash(b1, DateTime.UtcNow));
        pc1.Cycle();
        pc2.Cycle();
        Check("다른 PC 삭제: 묻기 전에는 지우지 않음", pc2.Row(b2).Status == SyncNoteStatus.RemoteDeleted && pc2.Note(b2).DeletedAt is null);
        Wait(pc2.Notes.MoveToTrash(b2, DateTime.UtcNow));
        pc2.KeepLocal(b2);
        long afterDelete = pc2.Row(b2).BaseRevision;
        pc2.Cycle();
        Check("삭제 받기: 휴지통으로, 다시 올리지 않음", pc2.Note(b2).DeletedAt is not null && pc2.Row(b2).BaseRevision == afterDelete);

        // 삭제를 거절하면 되살아남
        int c1 = pc1.Create("지웠다 살릴 메모");
        string cId = pc1.Row(c1).SyncId;
        pc1.Cycle();
        pc2.Cycle();
        int c2 = pc2.IdOf(cId);
        Wait(pc1.Notes.MoveToTrash(c1, DateTime.UtcNow));
        pc1.Cycle();
        pc2.Cycle();
        pc2.KeepLocal(c2);
        pc2.Cycle();
        pc1.Cycle();
        Check("삭제 거절: 지웠던 PC의 휴지통에서 닫힌 메모로 되살아남", pc1.Note(c1).DeletedAt is null && !pc1.Note(c1).IsOpen);

        // 수정이 삭제보다 우선
        int d1 = pc1.Create("수정과 삭제");
        string dId = pc1.Row(d1).SyncId;
        pc1.Cycle();
        pc2.Cycle();
        int d2 = pc2.IdOf(dId);
        pc2.Edit(d2, "삭제 전에 고침");
        pc2.Cycle();
        Wait(pc1.Notes.MoveToTrash(d1, DateTime.UtcNow));
        pc1.Cycle();
        Check("수정이 삭제보다 우선: 메모를 잃지 않음", pc1.Note(d1).DeletedAt is null && pc1.Note(d1).Content == "삭제 전에 고침");

        // 완전 삭제
        int e1 = pc1.Create("완전히 지울 메모");
        string eId = pc1.Row(e1).SyncId;
        pc1.Cycle();
        pc2.Cycle();
        int e2 = pc2.IdOf(eId);
        Wait(pc1.Notes.Delete(e1));
        pc1.Cycle();
        Check("완전 삭제: 알린 뒤 기록도 정리", pc1.RowBySyncId(eId) is null);
        pc2.Cycle();
        Check("완전 삭제: 다른 PC에 삭제로 알려짐", pc2.Row(e2).Status == SyncNoteStatus.RemoteDeleted);

        // 복구 코드
        Pc pc3 = new(Path.Combine(dir, "pc3"));
        pc3.Connect(new SyncConnectionCode(SyncCodeKind.Recovery, serverUrl, pc1.Credentials.RecoverySecret!, pc1.Credentials.VaultKey), "새 PC");
        pc3.Cycle();
        Check("복구 코드: 새 PC에서 메모를 읽음", pc3.Note(pc3.IdOf(syncId)).Content == "똑같은 내용");

        // 기기 해제
        SyncDeviceList devices = Wait(pc1.Api.GetDevices());
        Check("기기 목록", devices.Devices.Count == 3 && devices.CurrentDeviceId == pc1.Credentials.DeviceId, $"count={devices.Devices.Count}");
        Wait(pc1.Api.RevokeDevice(pc3.Credentials.DeviceId));
        Check("기기 해제: 접속이 끊김", Unauthorized(pc3.Api));

        // 연결 해제
        Wait(pc2.Sync.ResetSyncState());
        Check("연결 해제: 메모는 남고 기록만 지움", pc2.Note(a2).Content == "똑같은 내용" && pc2.Row(a2).BaseRevision == 0);
        pc2.Cycle();
        Check("다시 연결: 내용이 같으면 충돌 없이 맞춰짐", pc2.Row(a2).Status == SyncNoteStatus.None && pc2.Row(a2).BaseRevision > 0);

        // 실제 앱 화면
        string appPairSecret = SyncSecrets.NewSecret();
        Wait(pc1.Api.CreatePairing(SyncSecrets.Sha256Hex(appPairSecret)));
        SyncCredentials appCredentials = Wait(SyncConnector.Connect(new SyncConnectionCode(SyncCodeKind.Pair, serverUrl, appPairSecret, pc1.Credentials.VaultKey), "앱 PC"));
        Ui.SyncScenario(appDir, outDir, appCredentials, pc1);
    }

    private static bool Unauthorized(SyncApi api)
    {
        try
        {
            Wait(api.GetChanges(0));
            return false;
        }
        catch (SyncApiException ex)
        {
            return ex.IsUnauthorized;
        }
    }
}
