using System.IO;
using System.Net.Http;
using System.Windows.Threading;

using FlowMemo.Core.Configurations;
using FlowMemo.Core.Enums;
using FlowMemo.Core.Repositories;
using FlowMemo.Core.Sync;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Services;

/// <summary>
/// 여러 PC 사이의 메모 동기화. 충돌을 줄이려고 고친 내용은 몇 초 안에 올리고,
/// 메모에 포커스가 오면 바로 확인하며, 다른 PC에서 편집 중인지 알려 준다.
/// 서버에 닿지 못해도 메모는 그대로 동작하고, 다음 주기에 다시 시도한다.
/// </summary>
public sealed class SyncService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PushDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FocusSyncThrottle = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PresenceThrottle = TimeSpan.FromSeconds(20);

    private readonly SyncRepository _repository;
    private readonly MessengerService _messengerService;
    private readonly SyncCredentialStore _store;

    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _pushTimer;

    private readonly Dictionary<int, DateTime> _lastPresenceSent = [];
    private Dictionary<int, string> _editing = [];

    private SyncCredentials? _credentials;
    private SyncApi? _api;
    private SyncEngine? _engine;

    private bool _running;
    private bool _runAgain;
    private DateTime _lastFocusSync = DateTime.MinValue;

    public SyncService(SyncRepository repository, MessengerService messengerService, DatabaseConfiguration databaseConfiguration)
    {
        _repository = repository;
        _messengerService = messengerService;
        _store = new SyncCredentialStore(Path.Combine(databaseConfiguration.DataPath, SyncCredentialStore.FileName));

        _pollTimer = new DispatcherTimer { Interval = PollInterval };
        _pollTimer.Tick += (_, _) => RequestSync();

        _pushTimer = new DispatcherTimer { Interval = PushDelay };
        _pushTimer.Tick += (_, _) =>
        {
            _pushTimer.Stop();
            RequestSync();
        };

        _messengerService.Subscribe<NoteActionMessage>(OnNoteActionMessage);
    }

    public event EventHandler? StatusChanged;

    public bool IsConnected => _credentials is not null;
    public string? DeviceName => _credentials?.DeviceName;
    public string? ServerUrl => _credentials?.ServerUrl;
    public bool HasRecoveryCode => _credentials?.RecoverySecret is not null;
    public DateTime? LastSyncAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>저장된 연결 정보가 있으면 동기화를 시작한다.</summary>
    public void Start()
    {
        SyncCredentials? credentials = _store.Load();
        if (credentials is not null)
            Activate(credentials);
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pushTimer.Stop();
        _messengerService.Unsubscribe<NoteActionMessage>(OnNoteActionMessage);
        _api?.Dispose();
    }

    private void Activate(SyncCredentials credentials)
    {
        _api?.Dispose();

        _credentials = credentials;
        _api = new SyncApi(credentials.ServerUrl, credentials.Token);
        _engine = new SyncEngine(_api, _repository, new SyncCrypto(Convert.FromBase64String(credentials.VaultKey)));

        _pollTimer.Start();
        RequestSync();
    }

    private void Deactivate()
    {
        _pollTimer.Stop();
        _pushTimer.Stop();
        _api?.Dispose();
        _api = null;
        _engine = null;
        _credentials = null;
        _editing = [];
        LastSyncAt = null;
    }

    // ---------------------------------------------------------------- 연결

    /// <summary>연결 코드로 이 PC를 등록한다.</summary>
    /// <exception cref="ArgumentException">코드 형식이 잘못되었을 때</exception>
    public async Task Connect(string codeText, string deviceName)
    {
        if (!SyncConnectionCode.TryParse(codeText, out SyncConnectionCode code))
            throw new ArgumentException(Strings.Settings_Sync_InvalidCode);

        SyncCredentials credentials = await SyncConnector.Connect(code, deviceName);

        // 다른 서버와 맞췄던 기록이 섞이지 않게 지운다.
        await _repository.ResetSyncState();
        _store.Save(credentials);
        LastError = null;
        Activate(credentials);
        RaiseStatusChanged();
    }

    /// <summary>이 PC의 연결을 끊는다. 메모는 그대로 남는다.</summary>
    public async Task Disconnect()
    {
        if (_api is not null && _credentials is not null)
        {
            try
            {
                await _api.RevokeDevice(_credentials.DeviceId);
            }
            catch (Exception ex) when (ex is SyncApiException or HttpRequestException or TaskCanceledException)
            {
                // 서버에 닿지 않아도 이 PC의 연결은 끊는다. 다른 PC의 기기 목록에서 해제할 수 있다.
                ErrorReporter.Log(ex, "Sync disconnect");
            }
        }

        Deactivate();
        _store.Delete();
        await _repository.ResetSyncState();
        LastError = null;

        foreach (NoteSyncRow row in await _repository.GetAll())
            _messengerService.Publish(new NoteSyncChangedMessage(row.NoteId));
        RaiseStatusChanged();
    }

    /// <summary>다른 PC를 연결할 코드를 만든다. 10분 동안 한 번만 쓸 수 있다.</summary>
    public async Task<string> CreatePairingCode()
    {
        (SyncApi api, SyncCredentials credentials) = RequireConnection();

        string secret = SyncSecrets.NewSecret();
        await api.CreatePairing(SyncSecrets.Sha256Hex(secret));
        return new SyncConnectionCode(SyncCodeKind.Pair, credentials.ServerUrl, secret, credentials.VaultKey).ToString();
    }

    public string? GetRecoveryCode()
        => _credentials?.RecoverySecret is string secret
            ? new SyncConnectionCode(SyncCodeKind.Recovery, _credentials.ServerUrl, secret, _credentials.VaultKey).ToString()
            : null;

    public Task<SyncDeviceList> GetDevices()
        => RequireConnection().Api.GetDevices();

    public Task RevokeDevice(string deviceId)
        => RequireConnection().Api.RevokeDevice(deviceId);

    private (SyncApi Api, SyncCredentials Credentials) RequireConnection()
        => _api is not null && _credentials is not null
            ? (_api, _credentials)
            : throw new InvalidOperationException("동기화가 연결되어 있지 않습니다.");

    // ---------------------------------------------------------------- 동기화

    public void RequestSync()
    {
        if (_engine is not null)
            ErrorReporter.FireAndForget(RunSync(), "Sync");
    }

    /// <summary>메모에 포커스가 왔을 때: 고치기 전에 다른 PC의 변경을 먼저 받아 충돌을 줄인다.</summary>
    public void OnNoteFocused()
    {
        if (_engine is null || DateTime.UtcNow - _lastFocusSync < FocusSyncThrottle)
            return;

        _lastFocusSync = DateTime.UtcNow;
        RequestSync();
    }

    public async Task RunSync()
    {
        if (_engine is null)
            return;

        if (_running)
        {
            _runAgain = true;
            return;
        }

        _running = true;
        try
        {
            do
            {
                _runAgain = false;
                SyncEngine? engine = _engine;
                if (engine is null)
                    break;

                await ProcessResult(await engine.RunCycle());
                LastSyncAt = DateTime.Now;
            }
            while (_runAgain);
        }
        catch (SyncApiException ex) when (ex.IsUnauthorized)
        {
            LastError = Strings.Settings_Sync_Unauthorized;
            _pollTimer.Stop();
        }
        catch (Exception ex) when (ex is SyncApiException or HttpRequestException or TaskCanceledException)
        {
            // 오프라인이거나 그날의 무료 한도를 다 썼을 때. 메모는 안전하고 다음 주기에 다시 시도한다.
            LastError = Strings.Settings_Sync_Offline;
            ErrorReporter.Log(ex, "Sync cycle");
        }
        finally
        {
            _running = false;
            RaiseStatusChanged();
        }
    }

    private async Task ProcessResult(SyncCycleResult result)
    {
        LastError = result.TooLargeNotes > 0
            ? string.Format(Strings.Settings_Sync_TooLarge, result.TooLargeNotes)
            : result.UnreadableChanges > 0
                ? string.Format(Strings.Settings_Sync_Unreadable, result.UnreadableChanges)
                : null;

        foreach (int noteId in result.ChangedNotes)
        {
            result.ColourChanges.TryGetValue(noteId, out string? colour);
            _messengerService.Publish(new NoteSyncChangedMessage(noteId, colour));
        }

        if (result.AddedNotes.Count > 0)
            _messengerService.Publish(new SyncNotesAddedMessage());

        await UpdatePresence(result.Presence);
    }

    private async Task UpdatePresence(IReadOnlyList<SyncPresence> presence)
    {
        Dictionary<int, string> editing = [];
        foreach (SyncPresence item in presence)
        {
            NoteSyncRow? row = await _repository.GetBySyncId(item.Id);
            if (row is not null)
                editing[row.NoteId] = item.DeviceName;
        }

        Dictionary<int, string> previous = _editing;
        _editing = editing;

        foreach (int noteId in previous.Keys.Union(editing.Keys).ToList())
        {
            previous.TryGetValue(noteId, out string? before);
            editing.TryGetValue(noteId, out string? after);
            if (before != after)
                _messengerService.Publish(new NoteSyncChangedMessage(noteId));
        }
    }

    public string? GetEditingDevice(int noteId)
        => _editing.GetValueOrDefault(noteId);

    /// <summary>이 PC에서 메모를 고치는 중임을 다른 PC에 알린다.</summary>
    public void ReportEditing(int noteId)
    {
        if (_api is null)
            return;

        DateTime now = DateTime.UtcNow;
        if (_lastPresenceSent.TryGetValue(noteId, out DateTime last) && now - last < PresenceThrottle)
            return;

        _lastPresenceSent[noteId] = now;
        ErrorReporter.FireAndForget(SendPresence(noteId), "Sync presence");
    }

    private async Task SendPresence(int noteId)
    {
        try
        {
            if (_api is not null && await _repository.GetByNoteId(noteId) is NoteSyncRow row)
                await _api.SendPresence(row.SyncId);
        }
        catch (Exception ex) when (ex is SyncApiException or HttpRequestException or TaskCanceledException)
        {
            ErrorReporter.Log(ex, "Sync presence");
        }
    }

    private void OnNoteActionMessage(NoteActionMessage message)
    {
        if (_engine is null)
            return;

        if (message.Action is NoteAction.Updated or NoteAction.Closed or NoteAction.Deleted)
        {
            _pushTimer.Stop();
            _pushTimer.Start();
        }

        // 받은 변경이 있는 메모를 고치면 '변경 있음'이 '충돌'로 바뀐다.
        if (message.Action == NoteAction.Updated)
            _messengerService.Publish(new NoteSyncChangedMessage(message.NoteDto.Id));
    }

    // ---------------------------------------------------------------- 사용자 결정

    public Task<NoteSyncRow?> GetRow(int noteId)
        => _repository.GetByNoteId(noteId);

    public async Task<IReadOnlyList<NoteSyncRow>> GetAllRows()
        => await _repository.GetAll();

    /// <summary>받은 변경을 기준으로 삼는다 (가져오기, 이 PC 유지, 합치기의 마지막 단계).</summary>
    public async Task CommitPending(int noteId)
    {
        await _repository.CommitPending(noteId);
        _messengerService.Publish(new NoteSyncChangedMessage(noteId));
        RequestSync();
    }

    public async Task Ignore(int noteId)
    {
        await _repository.Ignore(noteId);
        _messengerService.Publish(new NoteSyncChangedMessage(noteId));
    }

    private void RaiseStatusChanged()
        => StatusChanged?.Invoke(this, EventArgs.Empty);
}
