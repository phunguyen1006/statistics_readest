using System.Security.Cryptography;
using System.Text;

namespace ReadestStats.Core;

public sealed class CoverCacheService
{
    public const long MaxCoverBytes = 15_000_000;
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
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        Directory.CreateDirectory(_directory);
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp")) extension = ".jpg";
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant() + extension;
        var destination = Path.Combine(_directory, name);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (IsValidCachedCover(destination)) return destination;
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps) return null;
            if (response.Content.Headers.ContentLength > MaxCoverBytes) return null;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = File.Create(temp))
            {
                var buffer = new byte[81920];
                long size = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    size += read;
                    if (size > MaxCoverBytes) throw new InvalidDataException("The remote cover is too large.");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            var signature = new byte[12];
            using (var image = File.OpenRead(temp)) _ = image.Read(signature);
            if (!IsSupportedImageSignature(signature)) throw new InvalidDataException("The remote cover is not a supported image.");
            return PublishCover(temp, destination);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or InvalidDataException)
        {
            return null;
        }
        finally { DeleteTemporaryCover(temp); }
    }

    public async Task<string> ImportLocalAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp")) throw new InvalidDataException("Choose a JPG, PNG, or WebP cover.");
        if (!File.Exists(source) || new FileInfo(source).Length is <= 0 or > MaxCoverBytes) throw new InvalidDataException("Cover image must be 1–15 MB.");
        Directory.CreateDirectory(_directory);
        var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
        var valid = HasImageSignature(bytes, extension);
        if (!valid) throw new InvalidDataException("The selected file is not a supported image.");
        var destination = Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + extension);
        if (IsValidCachedCover(destination)) return destination;
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
            return PublishCover(temp, destination);
        }
        finally { DeleteTemporaryCover(temp); }
    }

    private static string PublishCover(string temp, string destination)
    {
        // Valid cache files are immutable. Another request or preview may be
        // reading the winner; replacing it would cause Windows sharing errors.
        if (IsValidCachedCover(destination)) return destination;
        try { File.Move(temp, destination); }
        catch (IOException) when (File.Exists(destination))
        {
            if (IsValidCachedCover(destination)) return destination;
            try { File.Move(temp, destination, true); } // Repair an invalid old cache atomically.
            catch (IOException) when (IsValidCachedCover(destination)) { return destination; }
        }
        return destination;
    }

    private static bool IsValidCachedCover(string path)
    {
        try
        {
            using var cached = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (cached.Length is <= 0 or > MaxCoverBytes) return false;
            Span<byte> signature = stackalloc byte[12];
            var read = cached.Read(signature);
            return IsSupportedImageSignature(signature[..read]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void DeleteTemporaryCover(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool HasImageSignature(ReadOnlySpan<byte> bytes, string extension) => extension switch
    {
        ".png" => bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        ".jpg" or ".jpeg" => bytes.StartsWith(new byte[] { 255, 216, 255 }),
        ".webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
        _ => false
    };

    private static bool IsSupportedImageSignature(ReadOnlySpan<byte> bytes) =>
        HasImageSignature(bytes, ".png") || HasImageSignature(bytes, ".jpg") || HasImageSignature(bytes, ".webp");

    public int RemoveUnreferenced(IEnumerable<string?> referencedPaths)
    {
        if (!Directory.Exists(_directory)) return 0;
        var referenced = referencedPaths.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).Select(path => Path.GetFullPath(path!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(_directory))
        {
            if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            if (referenced.Contains(Path.GetFullPath(file))) continue;
            try { File.Delete(file); removed++; } catch { }
        }
        return removed;
    }
}
