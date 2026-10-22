using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V25ReportScopeTests
{
    [Fact]
    public void NoteCounts_DefaultMatchesWrittenHighlights_AndOmitsPrivateOnlyPhysicalRows()
    {
        NoteExportScopeItem[] rows = [new(false, true), new(false, false), new(true, true), new(true, false)];
        var share = ExportScopeCounter.Notes(rows, includePrivate: false);
        Assert.Equal(new ExportScopeCounts(Notes: 1, Omitted: 3), share);
        Assert.True(share.CanExport);
        Assert.Equal(new ExportScopeCounts(Notes: 4), ExportScopeCounter.Notes(rows, includePrivate: true));
        Assert.Equal(4, rows.Length);
    }

    [Fact]
    public void NoteCounts_DefaultEmptyAndPrivateOnlyScopesCannotExport()
    {
        Assert.False(ExportScopeCounter.Notes([], includePrivate: false).CanExport);
        NoteExportScopeItem[] privateRows = [new(true, true), new(false, false)];
        Assert.False(ExportScopeCounter.Notes(privateRows, includePrivate: false).CanExport);
        Assert.True(ExportScopeCounter.Notes(privateRows, includePrivate: true).CanExport);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, true)]
    public void InclusionPredicate_IsSharedByCountsAndWriters(bool manual, bool highlight, bool includePrivate, bool expected)
    {
        Assert.Equal(expected, ExportScopeCounter.IncludesNote(manual, highlight, includePrivate));
        Assert.Equal(expected ? 1 : 0, ExportScopeCounter.Notes([new(manual, highlight)], includePrivate).Notes);
    }

    [Fact]
    public void PhysicalCounts_CurrentFiltersIncludesOnlyReferencedBooks()
    {
        ManualBook[] books = [new() { Id = -1 }, new() { Id = -2 }, new() { Id = -3, Archived = true }];
        ManualReadingSession[] selected = [new() { BookId = -1 }, new() { BookId = -1 }];
        Assert.Equal(new ExportScopeCounts(Books: 1, Sessions: 2), ExportScopeCounter.Physical(books, selected, entireLibrary: false));
        Assert.Equal(new ExportScopeCounts(Books: 3, Sessions: 2), ExportScopeCounter.Physical(books, selected, entireLibrary: true));
        Assert.Equal(3, books.Length);
        Assert.True(books[2].Archived);
    }

    [Fact]
    public void PhysicalCounts_AllItemsCanExportUnloggedBooks_ButEmptyFilteredScopeCannot()
    {
        ManualBook[] books = [new() { Id = -1 }];
        var all = ExportScopeCounter.Physical(books, [], entireLibrary: true);
        Assert.Equal(1, all.Books);
        Assert.True(all.CanExport);
        Assert.False(ExportScopeCounter.Physical(books, [], entireLibrary: false).CanExport);
        Assert.False(ExportScopeCounter.Physical([], [], entireLibrary: true).CanExport);
    }

    [Fact]
    public void PhysicalCounts_OrphanedSessionStillCountsAsWrittenRecord()
    {
        var counts = ExportScopeCounter.Physical([], [new() { BookId = -99 }], entireLibrary: false);
        Assert.Equal(new ExportScopeCounts(Sessions: 1), counts);
        Assert.True(counts.CanExport);
    }

    [Theory]
    [InlineData(true, false, true, true, ReadestCoverageState.AvailableSnapshot)]
    [InlineData(true, true, true, true, ReadestCoverageState.LastKnownSnapshot)]
    [InlineData(false, false, true, false, ReadestCoverageState.LastKnownSnapshot)]
    [InlineData(true, true, false, false, ReadestCoverageState.Unconfirmed)]
    [InlineData(true, false, false, false, ReadestCoverageState.Unconfirmed)]
    [InlineData(false, false, false, false, ReadestCoverageState.Unavailable)]
    public void CoverageState_UsesSourceEvidence_NotZeroTotals(bool connected, bool failed, bool snapshot, bool lastSuccess, ReadestCoverageState expected)
    {
        var stamp = MonthlySourceCoverage.Create("All sources", connected, failed, snapshot,
            lastSuccess ? new DateTimeOffset(2026, 10, 4, 8, 0, 0, TimeSpan.Zero) : null);
        Assert.Equal(expected, stamp.ReadestState);
        Assert.Contains("Recorded activity only", stamp.ToText());
        Assert.Contains("not zero reading", stamp.ToText());
    }

    [Fact]
    public void Coverage_ManualOnlyDoesNotWarnAboutUnrelatedMissingReadest()
    {
        var stamp = MonthlySourceCoverage.Create("Manual", false, false, false, null);
        Assert.Contains("Physical reading: app-owned local records", stamp.ToText());
        Assert.DoesNotContain("Readest:", stamp.ToText());
        Assert.DoesNotContain("digital reading may be missing", stamp.ToText());
        Assert.Contains("dữ liệu cục bộ", stamp.ToText(vietnamese: true));
    }

    [Fact]
    public void Coverage_RefreshTimeIsExplicitUtc_AndReadestOnlyOmitsPhysicalSource()
    {
        var stamp = MonthlySourceCoverage.Create("Readest", true, false, true,
            new DateTimeOffset(2026, 10, 4, 15, 30, 0, TimeSpan.FromHours(7)));
        Assert.Contains("2026-10-04 08:30:00 UTC", stamp.ToText());
        Assert.DoesNotContain("Physical reading:", stamp.ToText());
        Assert.Equal("All sources", MonthlySourceCoverage.Create("unknown", false, false, false, null).SourceScope);
    }

    [Fact]
    public void MonthlyMarkdown_CarriesSameSourceCoverageWithoutChangingReportValues()
    {
        var plain = MonthlyReviewEngine.Build([], [], 2026, 9,
            now: new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero), zone: TimeZoneInfo.Utc);
        var stamped = plain with { SourceCoverage = MonthlySourceCoverage.Create("All sources", false, false, false, null) };
        var markdown = MonthlyReviewEngine.ToMarkdown(stamped);
        Assert.Contains("## Source coverage", markdown);
        Assert.Contains("source unavailable; digital reading may be missing", markdown);
        Assert.Contains("not zero reading", markdown);
        Assert.Equal(plain.TotalSeconds, stamped.TotalSeconds);
        Assert.Equal(plain.Sessions, stamped.Sessions);
        Assert.Null(plain.SourceCoverage);
        Assert.DoesNotContain("## Source coverage", MonthlyReviewEngine.ToMarkdown(plain));
    }
}
