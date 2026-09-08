using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class ReadestLibraryLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReadestLocatorTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void FindBookFile_UsesHashDirectoryAndSupportedBookFile()
    {
        var directory = Path.Combine(_root, "Books", "abc");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "config.json"), "{}");
        var expected = Path.Combine(directory, "A book.epub");
        File.WriteAllText(expected, "fixture");

        var actual = ReadestLibraryLocator.FindBookFile(Path.Combine(_root, "statistics.db"), "abc");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FindBookFile_ReturnsNullForUnknownHash() => Assert.Null(ReadestLibraryLocator.FindBookFile(Path.Combine(_root, "statistics.db"), "unknown"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
