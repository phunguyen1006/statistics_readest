using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.1.0", 1, 1, 0)]
    public void ParseVersion_AcceptsReleaseTags(string tag, int major, int minor, int build)
    {
        var version = UpdateChecker.ParseVersion(tag);
        Assert.NotNull(version);
        Assert.Equal(new Version(major, minor, build), version);
    }
}
