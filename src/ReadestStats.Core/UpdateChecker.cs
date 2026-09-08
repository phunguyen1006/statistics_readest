using System.Net.Http.Headers;
using System.Text.Json;

namespace ReadestStats.Core;

public sealed class UpdateChecker(HttpClient? client = null)
{
    private readonly HttpClient _client = client ?? new HttpClient();
    private const string LatestReleaseApi = "https://api.github.com/repos/phunguyen1006/statistics_readest/releases/latest";

    public async Task<AppUpdateInfo> CheckAsync(Version currentVersion, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ReadestStats", currentVersion.ToString(3)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(content, cancellationToken: token);
        var tag = json.RootElement.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("The release has no version tag.");
        var url = json.RootElement.GetProperty("html_url").GetString() ?? "https://github.com/phunguyen1006/statistics_readest/releases/latest";
        var latest = ParseVersion(tag) ?? throw new InvalidDataException("The release version is not recognized.");
        return new(latest, tag, url, latest > currentVersion);
    }

    public static Version? ParseVersion(string? tag)
    {
        var value = tag?.Trim().TrimStart('v', 'V');
        return Version.TryParse(value, out var version) ? version : null;
    }
}
