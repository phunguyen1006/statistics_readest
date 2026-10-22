using System.Text;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class LocalSecretProtectionTests
{
    [Theory]
    [InlineData("test-api-key-not-a-real-credential")]
    [InlineData("Khóa tiếng Việt 🔐")]
    [InlineData("")]
    public void Protect_RoundtripsForCurrentUser_WithoutStoringPlaintext(string original)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<PlatformNotSupportedException>(() => LocalSecretProtection.Protect(original));
            return;
        }

        var protectedValue = LocalSecretProtection.Protect(original);
        Assert.NotEqual(original, protectedValue);
        Assert.NotEqual(Convert.ToBase64String(Encoding.UTF8.GetBytes(original)), protectedValue);
        Assert.True(LocalSecretProtection.TryUnprotect(protectedValue, out var recovered));
        Assert.Equal(original, recovered);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AA==")]
    [InlineData("cGxhaW50ZXh0LWtleQ==")]
    public void TryUnprotect_RejectsMalformedOrPlaintextInput(string? input)
    {
        Assert.False(LocalSecretProtection.TryUnprotect(input, out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TryUnprotect_RejectsOversizedInputBeforeDecoding()
    {
        Assert.False(LocalSecretProtection.TryUnprotect(new string('A', 100_000), out var value));
        Assert.Null(value);
    }

    [Fact]
    public void Protect_RejectsOversizedPlaintext()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentException>(() => LocalSecretProtection.Protect(new string('A', 16 * 1024 + 1)));
        Assert.Throws<ArgumentException>(() => LocalSecretProtection.Protect(new string('ắ', 6000)));
    }

    [Fact]
    public void TryUnprotect_RejectsDamagedProtectedBlob()
    {
        if (!OperatingSystem.IsWindows()) return;
        var bytes = Convert.FromBase64String(LocalSecretProtection.Protect("test-key"));
        bytes[0] ^= 0xff;
        Assert.False(LocalSecretProtection.TryUnprotect(Convert.ToBase64String(bytes), out var value));
        Assert.Null(value);
    }
}
