using System.Security.Cryptography;
using System.Text;

namespace ReadestStats.Core;

public sealed class CoverCacheService
{
    private readonly string _directory;
    private readonly HttpClient _http;

    public CoverCacheService(string directory, HttpClient? httpClient = null)
    {
        _directory = Path.GetFullPath(directory);
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ReadestStats/1.8");
    }

    public async Task<string?> CacheAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (File.Exists(url)) return Path.GetFullPath(url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return url;
        Directory.CreateDirectory(_directory);
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp")) extension = ".jpg";
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant() + extension;
        var destination = Path.Combine(_directory, name);
        if (File.Exists(destination) && new FileInfo(destination).Length > 0) return destination;
        var temp = destination + ".tmp";
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = File.Create(temp)) await source.CopyToAsync(target, cancellationToken);
            File.Move(temp, destination, true);
            return destination;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            return url;
        }
    }

    public int RemoveUnreferenced(IEnumerable<string?> referencedPaths)
    {
        if (!Directory.Exists(_directory)) return 0;
        var referenced = referencedPaths.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).Select(path => Path.GetFullPath(path!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(_directory))
        {
            if (referenced.Contains(Path.GetFullPath(file))) continue;
            try { File.Delete(file); removed++; } catch { }
        }
        return removed;
    }
}
