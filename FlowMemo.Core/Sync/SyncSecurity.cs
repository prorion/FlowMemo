using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FlowMemo.Core.Sync;

/// <summary>메모 내용을 AES-256-GCM으로 암호화한다. 키는 PC에만 있고 서버로 보내지 않는다.</summary>
public sealed class SyncCrypto
{
    public const int KeySize = 32;

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public SyncCrypto(byte[] key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException("암호화 키는 32바이트여야 합니다.", nameof(key));
        _key = key;
    }

    public static byte[] GenerateKey() => RandomNumberGenerator.GetBytes(KeySize);

    /// <param name="syncId">메모 ID를 인증에 포함한다. 서버가 다른 메모의 암호문으로 바꿔치기하면 복호화가 실패한다.</param>
    public string Encrypt(string plaintext, string syncId)
    {
        byte[] plain = Encoding.UTF8.GetBytes(plaintext);
        byte[] output = new byte[NonceSize + TagSize + plain.Length];
        Span<byte> nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using AesGcm aes = new(_key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), Encoding.UTF8.GetBytes(syncId));

        return Convert.ToBase64String(output);
    }

    /// <exception cref="CryptographicException">키가 다르거나 내용이 변조되었을 때</exception>
    /// <exception cref="FormatException">암호문 형식이 잘못되었을 때</exception>
    public string Decrypt(string blob, string syncId)
    {
        byte[] input = Convert.FromBase64String(blob);
        if (input.Length < NonceSize + TagSize)
            throw new CryptographicException("암호문이 너무 짧습니다.");

        byte[] plain = new byte[input.Length - NonceSize - TagSize];
        using AesGcm aes = new(_key, TagSize);
        aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize + TagSize), input.AsSpan(NonceSize, TagSize), plain, Encoding.UTF8.GetBytes(syncId));

        return Encoding.UTF8.GetString(plain);
    }
}

public static class SyncSecrets
{
    /// <summary>256비트 무작위 값.</summary>
    public static string NewSecret() => Base64Url.Encode(RandomNumberGenerator.GetBytes(32));

    public static string Sha256Hex(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

internal static class Base64Url
{
    public static string Encode(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static byte[] Decode(string text)
    {
        string normal = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normal.PadRight(normal.Length + (4 - normal.Length % 4) % 4, '='));
    }
}

public enum SyncCodeKind
{
    /// <summary>첫 PC 등록 (서버를 배포할 때 만들어진다).</summary>
    Setup,
    /// <summary>다른 PC 연결 (10분, 1회).</summary>
    Pair,
    /// <summary>모든 PC를 잃었을 때의 복구.</summary>
    Recovery
}

/// <summary>사용자가 복사해서 붙여넣는 코드 하나에 연결에 필요한 값을 모두 담는다.</summary>
public sealed record SyncConnectionCode(SyncCodeKind Kind, string ServerUrl, string Secret, string? VaultKey)
{
    private const string Prefix = "FMSYNC1.";

    private sealed record Wire(string K, string U, string S, string? V);

    public override string ToString()
        => Prefix + Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new Wire(Kind.ToString(), ServerUrl, Secret, VaultKey)));

    public static bool TryParse(string? text, out SyncConnectionCode code)
    {
        code = null!;
        string trimmed = (text ?? "").Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        try
        {
            Wire? wire = JsonSerializer.Deserialize<Wire>(Base64Url.Decode(trimmed[Prefix.Length..]));
            if (wire is null || !Enum.TryParse(wire.K, out SyncCodeKind kind) || !Enum.IsDefined(kind)
                || string.IsNullOrEmpty(wire.S) || !IsAllowedServerUrl(wire.U))
                return false;
            if (kind != SyncCodeKind.Setup && !IsValidVaultKey(wire.V))
                return false;

            code = new SyncConnectionCode(kind, wire.U.TrimEnd('/'), wire.S, wire.V);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }
    }

    /// <summary>토큰이 평문으로 나가지 않도록 HTTPS만 허용한다 (로컬 테스트 서버는 예외).</summary>
    public static bool IsAllowedServerUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    private static bool IsValidVaultKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return false;
        try
        {
            return Convert.FromBase64String(key).Length == SyncCrypto.KeySize;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
