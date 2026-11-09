using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

using FlowMemo.Core.Sync;

namespace FlowMemo.WpfUi.Helpers;

/// <summary>
/// 접속 토큰과 암호화 키를 Windows DPAPI로 암호화해 파일에 둔다.
/// 지금 Windows 사용자만 풀 수 있고, DB 백업에는 들어가지 않는다.
/// </summary>
public sealed class SyncCredentialStore(string path)
{
    public const string FileName = "sync.dat";

    private static readonly byte[] Entropy = "FlowMemo.Sync.v1"u8.ToArray();

    public SyncCredentials? Load()
    {
        if (!File.Exists(path))
            return null;

        try
        {
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SyncCredentials>(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            // 다른 PC나 다른 사용자에서 복사해 온 파일이면 풀 수 없다. 연결 안 된 상태로 시작한다.
            ErrorReporter.Log(ex, "Sync credentials");
            return null;
        }
    }

    public void Save(SyncCredentials credentials)
    {
        byte[] protectedData = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(credentials), Entropy, DataProtectionScope.CurrentUser);

        string temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedData);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
