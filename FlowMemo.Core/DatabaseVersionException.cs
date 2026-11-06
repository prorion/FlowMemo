namespace FlowMemo.Core;

/// <summary>DB 스키마가 이 FlowMemo보다 새 버전일 때 던진다. UI에서 안내 문구를 보여 주기 위해 따로 구분한다.</summary>
public class DatabaseVersionException(int databaseVersion, int supportedVersion)
    : Exception($"Database schema version {databaseVersion} is newer than the supported version {supportedVersion}.")
{
    public int DatabaseVersion { get; } = databaseVersion;
    public int SupportedVersion { get; } = supportedVersion;
}
