namespace ReadestStats.Core;

public static class NoteDiscoveryPools
{
    public const string AllEligible = "All eligible notes";
    public static readonly string[] All = [AllEligible, "Favorites", "Unseen", "Not seen recently", "Readest", "Physical books", "Current book"];
    public static string Normalize(string? pool) => All.Contains(pool, StringComparer.Ordinal) ? pool! : AllEligible;
}

/// <summary>Only local note identifiers are persisted, never copies of source note content.</summary>
public sealed class NoteDiscoveryState
{
    public string SelectedPool { get; set; } = NoteDiscoveryPools.AllEligible;
    public string ScopeKey { get; set; } = "";
    public string? CurrentBookHash { get; set; }
    public string? CurrentId { get; set; }
    public List<string> PendingIds { get; set; } = [];
    public List<string> SeenInCycleIds { get; set; } = [];
    /// <summary>Newest prior choice first.</summary>
    public List<string> HistoryIds { get; set; } = [];
    /// <summary>Newest rediscovery first, without duplicate IDs.</summary>
    public List<string> RecentIds { get; set; } = [];
}

public static class NotesDiscoveryPersistence
{
    public const int MaxHistory = 20;
    public const int MaxRecent = 8;
    public const int MaxCycleIds = 20_000;
    public const int MaxIdLength = 512;

    public static NoteDiscoveryState Normalize(NoteDiscoveryState? state)
    {
        state ??= new();
        var seen = Clean(state.SeenInCycleIds, MaxCycleIds);
        var seenSet = seen.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new()
        {
            SelectedPool = NoteDiscoveryPools.Normalize(state.SelectedPool),
            ScopeKey = state.ScopeKey is { Length: <= 2048 } ? state.ScopeKey : "",
            CurrentBookHash = ValidId(state.CurrentBookHash) ? state.CurrentBookHash : null,
            CurrentId = ValidId(state.CurrentId) ? state.CurrentId : null,
            PendingIds = Clean(state.PendingIds, MaxCycleIds).Where(id => !seenSet.Contains(id)).ToList(),
            SeenInCycleIds = seen,
            // Back may contain the same note twice at different points in the navigation trail.
            HistoryIds = (state.HistoryIds ?? []).Where(ValidId).Take(MaxHistory).ToList(),
            RecentIds = Clean(state.RecentIds, MaxRecent)
        };
    }

    /// <summary>Keep unresolved Readest identifiers, not missing physical-session notes.</summary>
    public static NoteDiscoveryState MissingDigital(NoteDiscoveryState? state, IEnumerable<string> availableIds)
    {
        var normalized = Normalize(state);
        var available = availableIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Missing(string id) => !id.StartsWith("manual:", StringComparison.OrdinalIgnoreCase) && !available.Contains(id);
        return Normalize(new()
        {
            SelectedPool = normalized.SelectedPool,
            ScopeKey = normalized.ScopeKey,
            CurrentBookHash = normalized.CurrentBookHash,
            CurrentId = normalized.CurrentId is { } current && Missing(current) ? current : null,
            PendingIds = normalized.PendingIds.Where(Missing).ToList(),
            SeenInCycleIds = normalized.SeenInCycleIds.Where(Missing).ToList(),
            HistoryIds = normalized.HistoryIds.Where(Missing).ToList(),
            RecentIds = normalized.RecentIds.Where(Missing).ToList()
        });
    }

    /// <summary>A temporarily unavailable Readest source must not erase rediscovery bookmarks.</summary>
    public static NoteDiscoveryState MergeUnresolvedDigital(NoteDiscoveryState? live, NoteDiscoveryState? unresolved)
    {
        var current = Normalize(live);
        var missing = Normalize(unresolved);
        var sameScope = current.ScopeKey.Equals(missing.ScopeKey, StringComparison.Ordinal)
            && current.SelectedPool.Equals(missing.SelectedPool, StringComparison.Ordinal);
        var olderCurrent = missing.CurrentId is { } id && !id.Equals(current.CurrentId, StringComparison.OrdinalIgnoreCase)
            ? new[] { id } : [];
        return Normalize(new()
        {
            SelectedPool = current.SelectedPool,
            ScopeKey = current.ScopeKey,
            CurrentBookHash = current.CurrentBookHash,
            CurrentId = current.CurrentId ?? missing.CurrentId,
            PendingIds = sameScope ? current.PendingIds.Concat(missing.PendingIds).ToList() : current.PendingIds,
            SeenInCycleIds = sameScope ? current.SeenInCycleIds.Concat(missing.SeenInCycleIds).ToList() : current.SeenInCycleIds,
            HistoryIds = current.HistoryIds.Concat(olderCurrent).Concat(missing.HistoryIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RecentIds = current.RecentIds.Concat(olderCurrent).Concat(missing.RecentIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        });
    }

    private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= MaxIdLength;
    private static List<string> Clean(IEnumerable<string>? ids, int limit) => (ids ?? []).Where(ValidId).Distinct(StringComparer.OrdinalIgnoreCase).Take(limit).ToList();
}

public sealed record NoteDiscoveryCandidate(string Id, string BookHash, bool IsFavorite, int TimesSeen,
    DateTimeOffset? LastSeenUtc, DateTimeOffset? SnoozedUntilUtc, bool ExcludeFromRandom, bool IsManual, bool IsHidden = false);

public static class NoteDiscoveryEligibility
{
    public static IReadOnlyList<NoteDiscoveryCandidate> Select(IEnumerable<NoteDiscoveryCandidate> notes, string pool,
        DateTimeOffset nowUtc, string? bookHash = null)
    {
        var eligible = notes.Where(note => !note.ExcludeFromRandom && !note.IsHidden && !(note.SnoozedUntilUtc > nowUtc));
        eligible = NoteDiscoveryPools.Normalize(pool) switch
        {
            "Favorites" => eligible.Where(note => note.IsFavorite),
            "Unseen" => eligible.Where(note => note.TimesSeen == 0),
            "Not seen recently" => eligible.Where(note => note.LastSeenUtc is null || note.LastSeenUtc < nowUtc.AddDays(-30)),
            "Readest" => eligible.Where(note => !note.IsManual),
            "Physical books" => eligible.Where(note => note.IsManual),
            "Current book" when string.IsNullOrWhiteSpace(bookHash) => [],
            _ => eligible
        };
        if (!string.IsNullOrWhiteSpace(bookHash)) eligible = eligible.Where(note => note.BookHash.Equals(bookHash, StringComparison.OrdinalIgnoreCase));
        return eligible.GroupBy(note => note.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
    }
}
