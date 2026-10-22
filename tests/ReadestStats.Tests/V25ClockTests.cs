using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V25ClockTests
{
    private static readonly TimeZoneInfo PlusSeven = TimeZoneInfo.CreateCustomTimeZone("Test +7", TimeSpan.FromHours(7), "Test +7", "Test +7");

    [Fact]
    public void Local_midnight_is_not_utc_midnight()
    {
        var now = new DateTimeOffset(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);
        var result = LocalReadingClock.Observe(new(2026, 10, 4), now, PlusSeven, []);
        Assert.Equal(new DateOnly(2026, 10, 5), result.Date);
        Assert.True(result.DateChanged); Assert.False(result.EligibilityExpired);
    }

    [Fact]
    public void No_due_boundary_means_no_refresh()
    {
        var now = DateTimeOffset.UtcNow;
        var result = LocalReadingClock.Observe(DateOnly.FromDateTime(now.UtcDateTime), now, TimeZoneInfo.Utc,
            [now.AddSeconds(1), DateTimeOffset.MaxValue]);
        Assert.False(result.DateChanged); Assert.False(result.EligibilityExpired);
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)]
    public void Expired_and_exact_boundaries_refresh_eligibility_without_date_change(int seconds)
    {
        var now = DateTimeOffset.UtcNow;
        var result = LocalReadingClock.Observe(DateOnly.FromDateTime(now.UtcDateTime), now, TimeZoneInfo.Utc,
            [now.AddMinutes(1), now.AddSeconds(seconds)]);
        Assert.False(result.DateChanged); Assert.True(result.EligibilityExpired);
    }

    [Fact]
    public void Backward_clock_and_year_rollover_rebuild_relative_dates()
    {
        Assert.True(LocalReadingClock.Observe(new(2027, 1, 1), new(2026, 12, 31, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc, []).DateChanged);
        Assert.True(LocalReadingClock.Observe(new(2026, 12, 31), new(2027, 1, 1, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc, []).DateChanged);
    }
}
