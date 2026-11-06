using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Windows;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Helpers;

public static class VersionHelper
{
    private static Version CurrentVersion
        => Assembly.GetExecutingAssembly().GetName().Version ?? new();

    public static async Task<bool> CheckForNewRelease(long? lastUpdateCheck, DateTimeOffset date)
    {
        // 예전에는 lastUpdateCheck가 null(한 번도 확인 안 함)이면 비교가 항상 false라 업데이트 확인이 영영 돌지 않았다.
        if (lastUpdateCheck is null || lastUpdateCheck < date.AddDays(-7).ToUnixTimeSeconds())
        {
            if (CurrentVersion < await GetLatestGitHubReleaseVersion())
                MessageBox.Show(
                    string.Format(Strings.Update_Available, "https://github.com/prorion/FlowMemo/releases/latest"),
                    Strings.Update_Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

            return true;
        }

        return false;
    }

    private static async Task<Version?> GetLatestGitHubReleaseVersion()
    {
        using HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FlowMemo", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            HttpResponseMessage response = await client.GetAsync("https://api.github.com/repos/prorion/FlowMemo/releases/latest");
            if (!response.IsSuccessStatusCode)
                return null;

            using JsonDocument responseData = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!responseData.RootElement.TryGetProperty("tag_name", out JsonElement tagNameElement))
                return null;

            string? releaseVersion = tagNameElement.GetString();
            if (string.IsNullOrWhiteSpace(releaseVersion))
                return null;

            // "v1.2.3", "1.2.3", "v1.2" 모두 받는다. 앞의 v는 빼고, 빠진 자리는 0으로 채운다.
            string versionText = releaseVersion.TrimStart('v', 'V').Split('-', '+')[0];
            if (Version.TryParse(versionText, out Version? parsedVersion))
                return new Version(parsedVersion.Major, Math.Max(parsedVersion.Minor, 0), Math.Max(parsedVersion.Build, 0), Math.Max(parsedVersion.Revision, 0));
        }
        catch
        {
        }

        return null;
    }
}
