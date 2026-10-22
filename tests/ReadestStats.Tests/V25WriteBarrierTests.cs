using System.Reflection;
using System.Text.Json;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V25WriteBarrierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReadestStats-v25-write-barrier-" + Guid.NewGuid().ToString("N"));
    public V25WriteBarrierTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task SettingsBarrier_DrainsEarlierQueuedSave_WithoutCapturingCallerContext()
    {
        var path = Path.Combine(_root, "settings.json");
        var store = new SettingsStore(path);
        var gate = WriteGate(store);
        await gate.WaitAsync();
        Task save;
        Task barrier;
        var context = new RecordingContext();
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            save = store.SaveAsync(new AppSettings { Theme = "Dark", BackupRetentionDays = 14 });
            barrier = store.WaitForWritesAsync();
            Assert.False(save.IsCompleted);
            Assert.False(barrier.IsCompleted);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
            gate.Release();
        }
        await Task.WhenAll(save, barrier).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, context.Posts);
        Assert.Equal(1, gate.CurrentCount);
        var settings = await store.LoadAsync();
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal(14, settings.BackupRetentionDays);
    }

    [Fact]
    public async Task ManualBarrier_DrainsEarlierQueuedDatabaseAndMirrorWrite()
    {
        var path = Path.Combine(_root, "manual-reading.json");
        var store = new ManualReadingStore(path);
        var gate = WriteGate(store);
        await gate.WaitAsync();
        Task save;
        Task barrier;
        var context = new RecordingContext();
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            save = store.SaveAsync(new ManualReadingData
            {
                Books = [new() { Id = -1, Title = "Saved before restore" }],
                Sessions = [new() { Id = "session", BookId = -1, Note = "Saved note" }]
            });
            barrier = store.WaitForWritesAsync();
            Assert.False(save.IsCompleted);
            Assert.False(barrier.IsCompleted);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
            gate.Release();
        }
        await Task.WhenAll(save, barrier).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, context.Posts);
        Assert.Equal(1, gate.CurrentCount);
        Assert.Equal("Saved before restore", Assert.Single((await store.LoadAsync()).Books).Title);
        var mirror = JsonSerializer.Deserialize<ManualReadingData>(await File.ReadAllTextAsync(path))!;
        Assert.Equal("Saved note", Assert.Single(mirror.Sessions).Note);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledBarrier_DoesNotUnlockTheActiveWriter(bool manual)
    {
        object store = manual ? new ManualReadingStore(Path.Combine(_root, "manual-reading.json"))
            : new SettingsStore(Path.Combine(_root, "settings.json"));
        var gate = WriteGate(store);
        await gate.WaitAsync();
        try
        {
            using var cancellation = new CancellationTokenSource();
            var barrier = Barrier(store, cancellation.Token);
            Assert.False(barrier.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => barrier);
            Assert.Equal(0, gate.CurrentCount);
        }
        finally { gate.Release(); }
        await Barrier(store).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, gate.CurrentCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCancelledBarrier_DoesNotConsumeWritePermit(bool manual)
    {
        object store = manual ? new ManualReadingStore(Path.Combine(_root, "manual-reading.json"))
            : new SettingsStore(Path.Combine(_root, "settings.json"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Barrier(store, cancellation.Token));
        Assert.Equal(1, WriteGate(store).CurrentCount);
    }

    [Fact]
    public async Task SettingsFailure_ReleasesWriteGateForRestoreDrain()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, """{"Theme":"Dark"}""");
        Directory.CreateDirectory(path + ".tmp");
        var store = new SettingsStore(path);
        var error = await Record.ExceptionAsync(() => store.SaveAsync(new AppSettings { Theme = "Light" }));
        Assert.True(error is IOException or UnauthorizedAccessException);
        await store.WaitForWritesAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, WriteGate(store).CurrentCount);
        Assert.Equal("Dark", (await store.LoadAsync()).Theme);
    }

    [Fact]
    public async Task MirrorFailure_DrainStillWaitsForAuthoritativeSaveAndPreservesData()
    {
        var path = Path.Combine(_root, "manual-reading.json");
        Directory.CreateDirectory(path);
        var store = new ManualReadingStore(path);
        var save = store.SaveAsync(new ManualReadingData { Books = [new() { Id = -1, Title = "Database survived" }] });
        await store.WaitForWritesAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await save;
        Assert.NotNull(store.LastMirrorError);
        Assert.Equal("Database survived", Assert.Single((await store.LoadAsync()).Books).Title);
        Assert.Equal(1, WriteGate(store).CurrentCount);
    }

    private static SemaphoreSlim WriteGate(object store) => (SemaphoreSlim)store.GetType()
        .GetField("_writeLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
    private static Task Barrier(object store, CancellationToken token = default) => store switch
    {
        SettingsStore settings => settings.WaitForWritesAsync(token),
        ManualReadingStore manual => manual.WaitForWritesAsync(token),
        _ => throw new ArgumentException("Unsupported store.", nameof(store))
    };
    private sealed class RecordingContext : SynchronizationContext
    {
        private int _posts;
        public int Posts => Volatile.Read(ref _posts);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _posts);
            _ = Task.Run(() => callback(state));
        }
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
