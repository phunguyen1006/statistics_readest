using System.Net;
using System.Security.Cryptography;
using System.Text;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V25CoverCacheTests : IDisposable
{
    private static readonly byte[] Image = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReadestCoverRace-" + Guid.NewGuid().ToString("N"));

    public V25CoverCacheTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Concurrent_downloads_reuse_the_winner_even_while_a_preview_holds_it_open()
    {
        const int count = 8;
        var directory = Path.Combine(_root, "remote");
        using var handler = new GatedHandler(count);
        using var http = new HttpClient(handler);
        var caches = Enumerable.Range(0, count).Select(_ => new CoverCacheService(directory, http)).ToArray();
        var requests = caches.Select(cache => cache.CacheAsync("https://covers.example.invalid/shared.png")).ToArray();
        try
        {
            var winner = await requests[0].WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(winner);
            using var preview = new FileStream(winner, FileMode.Open, FileAccess.Read, FileShare.Read);
            handler.ReleaseFollowers();
            var paths = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.All(paths, path => Assert.Equal(winner, path));
            Assert.Equal(Image, await File.ReadAllBytesAsync(winner));
            Assert.Equal(winner, Assert.Single(Directory.EnumerateFiles(directory)));
        }
        finally
        {
            handler.ReleaseFollowers();
            await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task An_invalid_old_cover_is_repaired_without_holding_its_file_open()
    {
        const string url = "https://covers.example.invalid/repair.png";
        var directory = Path.Combine(_root, "repair");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant() + ".png");
        await File.WriteAllTextAsync(destination, "invalid old cover");
        using var http = new HttpClient(new ImageHandler());
        var result = await new CoverCacheService(directory, http).CacheAsync(url);
        Assert.Equal(destination, result);
        Assert.Equal(Image, await File.ReadAllBytesAsync(destination));
        Assert.Single(Directory.EnumerateFiles(directory));
    }

    [Fact]
    public async Task Local_imports_are_atomic_and_reuse_a_cover_held_by_a_preview()
    {
        var source = Path.Combine(_root, "source.png");
        var directory = Path.Combine(_root, "local");
        await File.WriteAllBytesAsync(source, Image);
        var caches = Enumerable.Range(0, 16).Select(_ => new CoverCacheService(directory)).ToArray();
        var paths = await Task.WhenAll(caches.Select(cache => cache.ImportLocalAsync(source)));
        var winner = paths[0];
        Assert.All(paths, path => Assert.Equal(winner, path));
        using var preview = new FileStream(winner, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Equal(winner, await caches[0].ImportLocalAsync(source));
        Assert.Equal(Image, await File.ReadAllBytesAsync(winner));
        Assert.Single(Directory.EnumerateFiles(directory));
    }

    [Fact]
    public void Cleanup_does_not_delete_an_in_flight_cover_download()
    {
        var directory = Path.Combine(_root, "cleanup");
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, "cover.png.active.tmp");
        File.WriteAllBytes(staging, Image);
        File.WriteAllBytes(Path.Combine(directory, "unused.png"), Image);
        Assert.Equal(1, new CoverCacheService(directory).RemoveUnreferenced([]));
        Assert.Equal(staging, Assert.Single(Directory.EnumerateFiles(directory)));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private sealed class ImageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(Image) });
    }

    private sealed class GatedHandler(int participants) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _followers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;

        public void ReleaseFollowers() => _followers.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _started) - 1;
            if (index == participants - 1) _allStarted.TrySetResult();
            await _allStarted.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = index == 0 ? new ByteArrayContent(Image) : new StreamContent(new GatedReadStream(_followers.Task))
            };
        }
    }

    private sealed class GatedReadStream(Task ready) : MemoryStream(Image, writable: false)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await ready.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
