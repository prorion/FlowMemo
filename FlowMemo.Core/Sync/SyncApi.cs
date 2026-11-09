using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowMemo.Core.Sync;

/// <summary>동기화 서버 HTTP 클라이언트.</summary>
public sealed class SyncApi : ISyncApi, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public SyncApi(string serverUrl, string? token = null)
    {
        if (!SyncConnectionCode.IsAllowedServerUrl(serverUrl))
            throw new ArgumentException("동기화 서버는 HTTPS 주소만 쓸 수 있습니다.", nameof(serverUrl));

        _client = new HttpClient
        {
            BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(20)
        };
        if (token is not null)
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void Dispose() => _client.Dispose();

    private sealed record DeviceRegistration(string DeviceId, string Token);
    private sealed record WriteResponse(long Revision);
    private sealed record ConflictResponse(SyncRemoteNote? Current);
    private sealed record ErrorResponse(string? Error);

    public async Task<(string DeviceId, string Token)> Setup(string code, string deviceName, string recoveryHash)
    {
        DeviceRegistration registration = await Post<DeviceRegistration>("v1/setup", new { code, deviceName, recoveryHash });
        return (registration.DeviceId, registration.Token);
    }

    public async Task<(string DeviceId, string Token)> Redeem(string secret, string deviceName)
    {
        DeviceRegistration registration = await Post<DeviceRegistration>("v1/devices/redeem", new { secret, deviceName });
        return (registration.DeviceId, registration.Token);
    }

    public async Task CreatePairing(string secretHash)
        => await Post<JsonElement>("v1/pairings", new { secretHash });

    public async Task SendPresence(string id)
        => await Post<JsonElement>("v1/presence", new { id });

    public async Task<SyncDeviceList> GetDevices()
    {
        using HttpResponseMessage response = await _client.GetAsync("v1/devices");
        await EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<SyncDeviceList>(JsonOptions))!;
    }

    public async Task RevokeDevice(string deviceId)
    {
        using HttpResponseMessage response = await _client.DeleteAsync($"v1/devices/{deviceId}");
        await EnsureSuccess(response);
    }

    public async Task<SyncChangesPage> GetChanges(long since)
    {
        using HttpResponseMessage response = await _client.GetAsync($"v1/changes?since={since}");
        await EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<SyncChangesPage>(JsonOptions))!;
    }

    public async Task<SyncWriteResult> PutNote(string id, long baseRevision, string blob)
    {
        using HttpResponseMessage response = await _client.PutAsJsonAsync($"v1/notes/{id}", new { baseRevision, blob }, JsonOptions);
        return await ReadWriteResult(response);
    }

    public async Task<SyncWriteResult> DeleteNote(string id, long baseRevision)
    {
        using HttpResponseMessage response = await _client.DeleteAsync($"v1/notes/{id}?baseRevision={baseRevision}");
        return await ReadWriteResult(response);
    }

    private static async Task<SyncWriteResult> ReadWriteResult(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            ConflictResponse conflict = (await response.Content.ReadFromJsonAsync<ConflictResponse>(JsonOptions))!;
            return new SyncWriteResult(0, true, conflict.Current);
        }

        await EnsureSuccess(response);
        WriteResponse result = (await response.Content.ReadFromJsonAsync<WriteResponse>(JsonOptions))!;
        return new SyncWriteResult(result.Revision, false, null);
    }

    private async Task<T> Post<T>(string path, object body)
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(path, body, JsonOptions);
        await EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        string code = "unknown";
        try
        {
            code = (await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions))?.Error ?? code;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Cloudflare 오류 페이지처럼 JSON이 아닌 응답
        }

        throw new SyncApiException((int)response.StatusCode, code);
    }
}

public static class SyncConnector
{
    /// <summary>연결 코드로 이 PC를 등록한다.</summary>
    public static async Task<SyncCredentials> Connect(SyncConnectionCode code, string deviceName)
    {
        using SyncApi api = new(code.ServerUrl);

        if (code.Kind == SyncCodeKind.Setup)
        {
            // 첫 PC가 암호화 키를 만든다. 서버에는 복구 비밀값의 해시만 보낸다.
            string vaultKey = Convert.ToBase64String(SyncCrypto.GenerateKey());
            string recoverySecret = SyncSecrets.NewSecret();
            (string deviceId, string token) = await api.Setup(code.Secret, deviceName, SyncSecrets.Sha256Hex(recoverySecret));
            return new SyncCredentials(code.ServerUrl, deviceId, deviceName, token, vaultKey, recoverySecret);
        }

        (string id, string deviceToken) = await api.Redeem(code.Secret, deviceName);
        return new SyncCredentials(code.ServerUrl, id, deviceName, deviceToken, code.VaultKey!, code.Kind == SyncCodeKind.Recovery ? code.Secret : null);
    }
}
