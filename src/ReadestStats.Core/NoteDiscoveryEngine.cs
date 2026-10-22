using System.Security.Cryptography;
using System.Text;

namespace ReadestStats.Core;

/// <summary>Local rediscovery with a reconciled shuffle cycle and navigation-only history.</summary>
public sealed class NoteDiscoveryEngine
{
    private readonly Random _random;
    private readonly List<string> _bag = [];
    private readonly HashSet<string> _seenInCycle = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _history = [];
    private readonly List<string> _recent = [];
    private Dictionary<string, string> _groups = new(StringComparer.OrdinalIgnoreCase);
    private NoteDiscoveryPoolSnapshot _allNotes = new([]);
    private NoteDiscoveryPoolSnapshot? _preparedSnapshot;
    private string[] _preparedEligibleIds = [];
    private bool _eligibilityDirty = true;
    private string _scopeKey = "";
    private string? _currentId;

    public NoteDiscoveryEngine(Random? random = null) => _random = random ?? Random.Shared;
    public bool CanGoBack => _history.Count > 0;
    public string? CurrentId => _currentId;
    public IReadOnlyList<string> RecentIds => _recent;

    public void Update(IEnumerable<string> ids)
        => Update(ids.Select(id => KeyValuePair.Create(id, id)));

    public void Update(IEnumerable<KeyValuePair<string, string>> notes)
    {
        _groups = notes.Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value ?? "", StringComparer.OrdinalIgnoreCase);
        _allNotes = new(_groups.Keys);
        _eligibilityDirty = true;
        _bag.RemoveAll(id => !_groups.ContainsKey(id));
        _seenInCycle.RemoveWhere(id => !_groups.ContainsKey(id));
        _history.RemoveAll(id => !_groups.ContainsKey(id));
        _recent.RemoveAll(id => !_groups.ContainsKey(id));
        if (_currentId is not null && !_groups.ContainsKey(_currentId)) _currentId = null;
    }

    public string? Daily(DateOnly date)
    {
        var day = date.ToString("yyyy-MM-dd");
        return _groups.Keys.MinBy(id => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{day}:{id}"))), StringComparer.Ordinal);
    }

    public string? Next(string? currentId) => NextFromSnapshot(currentId, _allNotes, NoteDiscoveryPools.AllEligible);

    public string? Next(string? currentId, IEnumerable<string> eligibleIds, string scopeKey)
        => NextFromSnapshot(currentId, new NoteDiscoveryPoolSnapshot(eligibleIds), scopeKey);

    /// <summary>Reuse an immutable pool snapshot to avoid re-indexing a large pool on every pick.</summary>
    public string? NextFromSnapshot(string? currentId, NoteDiscoveryPoolSnapshot snapshot, string scopeKey)
    {
        if (_eligibilityDirty || !ReferenceEquals(_preparedSnapshot, snapshot) || !string.Equals(_scopeKey, scopeKey, StringComparison.Ordinal))
        {
            var eligible = snapshot.Ids.Where(_groups.ContainsKey).ToArray();
            if (!string.Equals(_scopeKey, scopeKey, StringComparison.Ordinal))
            {
                _scopeKey = scopeKey;
                _bag.Clear();
                _seenInCycle.Clear();
            }
            var eligibleSet = eligible.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _bag.RemoveAll(id => !eligibleSet.Contains(id) || _seenInCycle.Contains(id));
            // New notes enter the current cycle; a removed or snoozed note cannot invalidate it.
            var pendingSet = _bag.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var additions = eligible.Where(id => !_seenInCycle.Contains(id) && !pendingSet.Contains(id)).ToList();
            Shuffle(additions);
            _bag.AddRange(additions);
            _preparedEligibleIds = eligible;
            _preparedSnapshot = snapshot;
            _eligibilityDirty = false;
        }
        if (_preparedEligibleIds.Length == 0) { _currentId = null; return null; }
        if (_bag.Count == 0)
        {
            _seenInCycle.Clear();
            _bag.AddRange(_preparedEligibleIds);
            Shuffle(_bag);
        }

        // Prefer another book while preserving every pending note in the cycle.
        var previousId = currentId ?? _currentId;
        if (_bag.Count > 1 && previousId is not null)
        {
            var previousBook = _groups.GetValueOrDefault(previousId);
            var different = _bag.FindIndex(id => !string.Equals(_groups.GetValueOrDefault(id), previousBook, StringComparison.OrdinalIgnoreCase));
            if (different < 0) different = _bag.FindIndex(id => !id.Equals(previousId, StringComparison.OrdinalIgnoreCase));
            if (different > 0) (_bag[0], _bag[different]) = (_bag[different], _bag[0]);
        }
        var next = _bag[0];
        _bag.RemoveAt(0);
        _seenInCycle.Add(next);
        if (previousId is not null && _groups.ContainsKey(previousId) && !next.Equals(previousId, StringComparison.OrdinalIgnoreCase))
        {
            if (_history.Count == 0 || !_history[0].Equals(previousId, StringComparison.OrdinalIgnoreCase)) _history.Insert(0, previousId);
            if (_history.Count > NotesDiscoveryPersistence.MaxHistory) _history.RemoveRange(NotesDiscoveryPersistence.MaxHistory, _history.Count - NotesDiscoveryPersistence.MaxHistory);
        }
        _currentId = next;
        _recent.RemoveAll(id => id.Equals(next, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, next);
        if (_recent.Count > NotesDiscoveryPersistence.MaxRecent) _recent.RemoveRange(NotesDiscoveryPersistence.MaxRecent, _recent.Count - NotesDiscoveryPersistence.MaxRecent);
        return next;
    }

    /// <summary>Back navigates only; it never consumes a shuffle slot or marks a note seen.</summary>
    public string? Previous()
    {
        if (_history.Count == 0) return null;
        var previous = _history[0];
        _history.RemoveAt(0);
        _currentId = previous;
        return previous;
    }

    public bool NavigateTo(string id)
    {
        if (!_groups.ContainsKey(id)) return false;
        if (_currentId is not null && !_currentId.Equals(id, StringComparison.OrdinalIgnoreCase))
        {
            _history.Insert(0, _currentId);
            if (_history.Count > NotesDiscoveryPersistence.MaxHistory) _history.RemoveRange(NotesDiscoveryPersistence.MaxHistory, _history.Count - NotesDiscoveryPersistence.MaxHistory);
        }
        _currentId = id;
        return true;
    }

    public NoteDiscoveryState CaptureState(string selectedPool = NoteDiscoveryPools.AllEligible) => NotesDiscoveryPersistence.Normalize(new()
    {
        SelectedPool = selectedPool,
        ScopeKey = _scopeKey,
        CurrentId = _currentId,
        PendingIds = [.. _bag],
        SeenInCycleIds = [.. _seenInCycle],
        HistoryIds = [.. _history],
        RecentIds = [.. _recent]
    });

    /// <summary>Restore before Update; the following library refresh prunes missing IDs.</summary>
    public void RestoreState(NoteDiscoveryState? state)
    {
        var normalized = NotesDiscoveryPersistence.Normalize(state);
        _scopeKey = normalized.ScopeKey;
        _currentId = normalized.CurrentId;
        _bag.Clear(); _bag.AddRange(normalized.PendingIds);
        _seenInCycle.Clear(); _seenInCycle.UnionWith(normalized.SeenInCycleIds);
        _history.Clear(); _history.AddRange(normalized.HistoryIds);
        _recent.Clear(); _recent.AddRange(normalized.RecentIds);
        _eligibilityDirty = true;
    }

    private void Shuffle(List<string> ids)
    {
        for (var index = ids.Count - 1; index > 0; index--)
        {
            var other = _random.Next(index + 1);
            (ids[index], ids[other]) = (ids[other], ids[index]);
        }
    }
}

/// <summary>An owned, immutable eligible-ID snapshot; constructing a new one signals a pool change.</summary>
public sealed class NoteDiscoveryPoolSnapshot
{
    internal string[] Ids { get; }
    public NoteDiscoveryPoolSnapshot(IEnumerable<string> ids) => Ids = ids
        .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
