using System.Diagnostics;
using System.Text.Json;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V25NotesDiscoveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryEligibleNoteAppearsBeforeTheNextCycle()
    {
        var engine = new NoteDiscoveryEngine(new Random(17));
        var ids = new[] { "a", "b", "c", "d", "e" };
        engine.Update(ids.Select(id => KeyValuePair.Create(id, id is "a" or "b" ? "first-book" : "second-book")));
        var firstCycle = Enumerable.Range(0, ids.Length).Select(_ => engine.Next(engine.CurrentId, ids, "pool")).ToArray();
        var secondCycle = Enumerable.Range(0, ids.Length).Select(_ => engine.Next(engine.CurrentId, ids, "pool")).ToArray();
        Assert.Equal(ids.Order(), firstCycle.Order());
        Assert.Equal(ids.Order(), secondCycle.Order());
        Assert.NotEqual(firstCycle[^1], secondCycle[0]);
    }

    [Fact]
    public void RestartRetainsCurrentPendingCycleAndNavigationHistory()
    {
        var ids = new[] { "a", "b", "c", "d" };
        var engine = new NoteDiscoveryEngine(new Random(12));
        engine.Update(ids);
        var first = engine.Next(null, ids, "Favorites");
        var second = engine.Next(first, ids, "Favorites");
        var saved = JsonSerializer.Deserialize<NoteDiscoveryState>(JsonSerializer.Serialize(engine.CaptureState("Favorites")))!;
        var restarted = new NoteDiscoveryEngine(new Random(999));
        restarted.RestoreState(saved);
        restarted.Update(ids);
        Assert.Equal(second, restarted.CurrentId);
        Assert.Equal("Favorites", restarted.CaptureState("Favorites").SelectedPool);
        Assert.Equal(engine.RecentIds, restarted.RecentIds);
        var third = restarted.Next(restarted.CurrentId, ids, "Favorites");
        var fourth = restarted.Next(third, ids, "Favorites");
        Assert.Equal(4, new[] { first, second, third, fourth }.Distinct().Count());
        Assert.Equal(third, restarted.Previous());
    }

    [Fact]
    public void NewNotesJoinTheRemainingCycleWithoutRepeatingEarlierChoices()
    {
        var engine = new NoteDiscoveryEngine(new Random(4));
        engine.Update(["a", "b", "c"]);
        var first = engine.Next(null, ["a", "b", "c"], "pool");
        engine.Update(["a", "b", "c", "new"]);
        var rest = Enumerable.Range(0, 3).Select(_ => engine.Next(engine.CurrentId, ["a", "b", "c", "new"], "pool")).ToArray();
        Assert.DoesNotContain(first, rest);
        Assert.Contains("new", rest);
        Assert.Equal(3, rest.Distinct().Count());
    }

    [Fact]
    public void RemovedNotesArePrunedFromCurrentHistoryAndPendingChoices()
    {
        var engine = new NoteDiscoveryEngine(new Random(2));
        engine.Update(["a", "b", "c"]);
        engine.Next(null);
        engine.Next(engine.CurrentId);
        engine.Update(["remaining"]);
        var state = engine.CaptureState();
        Assert.Null(engine.CurrentId);
        Assert.False(engine.CanGoBack);
        Assert.Empty(state.RecentIds);
        Assert.Empty(state.PendingIds);
        Assert.Equal("remaining", engine.Next(null));
    }

    [Fact]
    public void SameScopeEligibilityChangesDoNotRestartACompletedPartOfTheCycle()
    {
        var engine = new NoteDiscoveryEngine(new Random(8));
        var ids = new[] { "a", "b", "c", "d" };
        engine.Update(ids);
        var first = engine.Next(null, ids, "pool");
        var remaining = ids.Where(id => id != first).ToArray();
        var second = engine.Next(first, remaining, "pool");
        var third = engine.Next(second, ids, "pool");
        var fourth = engine.Next(third, ids, "pool");
        Assert.Equal(4, new[] { first, second, third, fourth }.Distinct().Count());
    }

    [Fact]
    public void EmptyPoolClearsCurrentWithoutFallingBackToOtherNotes()
    {
        var engine = new NoteDiscoveryEngine(new Random(1));
        engine.Update(["a", "b"]);
        engine.Next(null);
        Assert.Null(engine.Next(engine.CurrentId, [], "Favorites"));
        Assert.Null(engine.CurrentId);
        Assert.Single(engine.RecentIds);
    }

    [Fact]
    public void SingleEligibleNoteRemainsUsable()
    {
        var engine = new NoteDiscoveryEngine();
        engine.Update(["a", "excluded"]);
        Assert.Equal("a", engine.Next(null, ["a"], "pool"));
        Assert.Equal("a", engine.Next("a", ["a"], "pool"));
        Assert.False(engine.CanGoBack);
    }

    [Fact]
    public void BackDoesNotConsumePendingChoicesOrChangeRecentRediscoveries()
    {
        var engine = new NoteDiscoveryEngine(new Random(3));
        engine.Update(["a", "b", "c"]);
        var first = engine.Next(null);
        engine.Next(first);
        var before = engine.CaptureState();
        Assert.Equal(first, engine.Previous());
        var after = engine.CaptureState();
        Assert.Equal(before.PendingIds, after.PendingIds);
        Assert.Equal(before.SeenInCycleIds, after.SeenInCycleIds);
        Assert.Equal(before.RecentIds, after.RecentIds);
    }

    [Fact]
    public void ExplicitHistoryNavigationDoesNotConsumeAShuffleSlot()
    {
        var engine = new NoteDiscoveryEngine(new Random(3));
        engine.Update(["a", "b", "c"]);
        var first = engine.Next(null)!;
        engine.Next(first);
        var before = engine.CaptureState();
        Assert.True(engine.NavigateTo(first));
        Assert.False(engine.NavigateTo("missing"));
        var after = engine.CaptureState();
        Assert.Equal(before.PendingIds, after.PendingIds);
        Assert.Equal(before.SeenInCycleIds, after.SeenInCycleIds);
        Assert.Equal(before.RecentIds, after.RecentIds);
    }

    [Fact]
    public void EligibilityAlwaysAppliesExclusionsSnoozeAndHiddenFlags()
    {
        var candidates = new[]
        {
            Candidate("eligible"),
            Candidate("excluded") with { ExcludeFromRandom = true },
            Candidate("snoozed") with { SnoozedUntilUtc = Now.AddDays(1) },
            Candidate("hidden") with { IsHidden = true },
            Candidate("expired") with { SnoozedUntilUtc = Now.AddSeconds(-1) }
        };
        Assert.Equal(new[] { "eligible", "expired" }, NoteDiscoveryEligibility.Select(candidates, NoteDiscoveryPools.AllEligible, Now).Select(note => note.Id));
    }

    [Theory]
    [InlineData("Favorites", "favorite")]
    [InlineData("Unseen", "new")]
    [InlineData("Not seen recently", "old")]
    [InlineData("Physical books", "physical")]
    [InlineData("Readest", "digital")]
    public void PoolAndSameBookUseTheSameEligibilityRules(string pool, string expected)
    {
        var target = Candidate(expected) with
        {
            IsFavorite = pool == "Favorites",
            TimesSeen = pool == "Unseen" ? 0 : 1,
            LastSeenUtc = pool == "Not seen recently" ? Now.AddDays(-31) : Now,
            IsManual = pool == "Physical books"
        };
        var candidates = new[] { target, target with { Id = "excluded", ExcludeFromRandom = true }, target with { Id = "other-book", BookHash = "other" } };
        Assert.Equal(expected, Assert.Single(NoteDiscoveryEligibility.Select(candidates, pool, Now, "book")).Id);
    }

    [Fact]
    public void CurrentBookPoolRequiresAnExplicitBookContext()
    {
        Assert.Empty(NoteDiscoveryEligibility.Select([Candidate("a")], "Current book", Now));
        Assert.Single(NoteDiscoveryEligibility.Select([Candidate("a")], "Current book", Now, "book"));
    }

    [Fact]
    public void PersistedStateIsBoundedDeduplicatedAndToleratesNullLegacyLists()
    {
        var state = NotesDiscoveryPersistence.Normalize(new()
        {
            SelectedPool = "unsupported", ScopeKey = new string('x', 2049), CurrentId = "",
            CurrentBookHash = "book",
            PendingIds = ["a", "A", "seen", new string('x', 513)], SeenInCycleIds = ["seen", "SEEN"],
            HistoryIds = Enumerable.Range(0, 100).Select(index => $"id-{index}").ToList(),
            RecentIds = null!
        });
        Assert.Equal(NoteDiscoveryPools.AllEligible, state.SelectedPool);
        Assert.Empty(state.ScopeKey);
        Assert.Null(state.CurrentId);
        Assert.Equal("book", state.CurrentBookHash);
        Assert.Equal(["a"], state.PendingIds);
        Assert.Equal(["seen"], state.SeenInCycleIds);
        Assert.Equal(NotesDiscoveryPersistence.MaxHistory, state.HistoryIds.Count);
        Assert.Empty(state.RecentIds);
        var many = Enumerable.Range(0, NotesDiscoveryPersistence.MaxCycleIds + 100).Select(index => $"id-{index}").ToList();
        Assert.Equal(NotesDiscoveryPersistence.MaxCycleIds, NotesDiscoveryPersistence.Normalize(new() { PendingIds = many }).PendingIds.Count);
    }

    [Fact]
    public void OfflineDigitalBookmarksSurviveAlongsideUsableManualDiscovery()
    {
        var saved = new NoteDiscoveryState
        {
            ScopeKey = "pool", CurrentId = "digital-current", PendingIds = ["digital-next"],
            SeenInCycleIds = ["digital-current"], HistoryIds = ["digital-earlier"], RecentIds = ["digital-current"]
        };
        var missing = NotesDiscoveryPersistence.MissingDigital(saved, ["manual:one", "manual:two"]);
        var engine = new NoteDiscoveryEngine(new Random(4));
        engine.Update(["manual:one", "manual:two"]);
        Assert.StartsWith("manual:", engine.Next(null, ["manual:one", "manual:two"], "pool"));
        var live = engine.CaptureState();
        var persisted = NotesDiscoveryPersistence.MergeUnresolvedDigital(live, missing);
        Assert.Equal(engine.CurrentId, persisted.CurrentId);
        Assert.Contains("digital-next", persisted.PendingIds);
        Assert.Contains("digital-current", persisted.SeenInCycleIds);
        Assert.Contains("digital-current", persisted.HistoryIds);
        Assert.Contains("digital-earlier", persisted.HistoryIds);
        Assert.Contains("digital-current", persisted.RecentIds);
        Assert.DoesNotContain(engine.RecentIds, id => id.StartsWith("digital"));
    }

    [Fact]
    public void MissingDigitalCurrentIsRetainedEvenWhenNoLiveRandomPoolExists()
    {
        var saved = new NoteDiscoveryState { CurrentId = "digital", PendingIds = ["missing", "manual:removed"], HistoryIds = ["manual:removed", "digital-history"] };
        var missing = NotesDiscoveryPersistence.MissingDigital(saved, []);
        var persisted = NotesDiscoveryPersistence.MergeUnresolvedDigital(new(), missing);
        Assert.Equal("digital", persisted.CurrentId);
        Assert.Equal(["missing"], missing.PendingIds);
        Assert.Equal(["digital-history"], missing.HistoryIds);
    }

    [Fact]
    public void PartialSnapshotsRetainOnlyUnknownDigitalIdentifiers()
    {
        var saved = new NoteDiscoveryState
        {
            CurrentId = "missing-digital", PendingIds = ["present-digital", "missing-digital", "manual:missing"],
            HistoryIds = ["present-digital", "missing-history", "manual:missing"]
        };
        var unresolved = NotesDiscoveryPersistence.MissingDigital(saved, ["present-digital"]);
        Assert.Equal(["missing-digital"], unresolved.PendingIds);
        Assert.Equal(["missing-history"], unresolved.HistoryIds);
        Assert.Equal("missing-digital", unresolved.CurrentId);
    }

    [Fact]
    public void ReconnectRehydratesHistoryThenAuthoritativeDeletionPrunesIt()
    {
        var unresolved = new NoteDiscoveryState { ScopeKey = "pool", CurrentId = "digital-current", PendingIds = ["deleted"], HistoryIds = ["digital-history"] };
        var live = new NoteDiscoveryState { ScopeKey = "pool", CurrentId = "manual:current", PendingIds = ["manual:next"] };
        var combined = NotesDiscoveryPersistence.MergeUnresolvedDigital(live, unresolved);
        var engine = new NoteDiscoveryEngine();
        engine.RestoreState(combined);
        engine.Update(["manual:current", "manual:next", "digital-current", "digital-history"]);
        Assert.Equal("manual:current", engine.CurrentId);
        Assert.Equal("digital-current", engine.Previous());
        Assert.Equal("digital-history", engine.Previous());
        Assert.DoesNotContain("deleted", engine.CaptureState().PendingIds);
    }

    [Fact]
    public void ChangingPoolsOfflineDoesNotInjectAnOldPoolsPendingCycle()
    {
        var oldPool = new NoteDiscoveryState { SelectedPool = "Favorites", ScopeKey = "Favorites|", CurrentId = "digital-old", PendingIds = ["digital-pending"], SeenInCycleIds = ["digital-seen"] };
        var newPool = new NoteDiscoveryState { ScopeKey = "All eligible notes|", CurrentId = "manual:current", PendingIds = ["manual:next"] };
        var merged = NotesDiscoveryPersistence.MergeUnresolvedDigital(newPool, oldPool);
        Assert.Equal(["manual:next"], merged.PendingIds);
        Assert.DoesNotContain("digital-seen", merged.SeenInCycleIds);
        Assert.Contains("digital-old", merged.HistoryIds);
        Assert.Equal(NoteDiscoveryPools.AllEligible, merged.SelectedPool);
    }

    [Fact]
    public void LargeImmutablePoolKeepsAThousandCycleChoicesInteractive()
    {
        var engine = new NoteDiscoveryEngine(new Random(42));
        var ids = Enumerable.Range(0, 10_000).Select(index => $"note-{index}").ToArray();
        engine.Update(ids.Select((id, index) => KeyValuePair.Create(id, $"book-{index % 1_000}")));
        var snapshot = new NoteDiscoveryPoolSnapshot(ids);
        var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var timer = Stopwatch.StartNew();
        for (var index = 0; index < 1_000; index++) chosen.Add(engine.NextFromSnapshot(engine.CurrentId, snapshot, "pool")!);
        timer.Stop();
        Assert.Equal(1_000, chosen.Count);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"Large-pool cycle choices took {timer.Elapsed}.");
    }

    [Fact]
    public void ImmutablePoolCopiesItsInputAndReconcilesAfterLibraryUpdates()
    {
        var input = new[] { "a", "b", "c" };
        var snapshot = new NoteDiscoveryPoolSnapshot(input);
        input[0] = "changed-after-snapshot";
        var engine = new NoteDiscoveryEngine(new Random(1));
        engine.Update(["a", "b", "c", "changed-after-snapshot"]);
        var firstCycle = Enumerable.Range(0, 3).Select(_ => engine.NextFromSnapshot(engine.CurrentId, snapshot, "pool")).ToArray();
        Assert.Equal(new[] { "a", "b", "c" }, firstCycle.Order());
        engine.Update(["b", "c"]);
        Assert.DoesNotContain("a", Enumerable.Range(0, 2).Select(_ => engine.NextFromSnapshot(engine.CurrentId, snapshot, "pool")));
        var onlyOne = new NoteDiscoveryPoolSnapshot(["b"]);
        Assert.Equal("b", engine.NextFromSnapshot(engine.CurrentId, onlyOne, "pool"));
    }

    private static NoteDiscoveryCandidate Candidate(string id) => new(id, "book", false, 1, Now, null, false, false);
}
