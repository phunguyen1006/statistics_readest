using System.Security.Cryptography;
using System.Text;

namespace ReadestStats.Core;

public sealed class NoteDiscoveryEngine
{
    private readonly Random _random;
    private readonly Queue<string> _bag = new();
    private readonly Stack<string> _history = new();
    private string[] _ids = [];
    private Dictionary<string, string> _groups = new(StringComparer.OrdinalIgnoreCase);

    public NoteDiscoveryEngine(Random? random = null) => _random = random ?? Random.Shared;
    public bool CanGoBack => _history.Count > 0;

    public void Update(IEnumerable<string> ids)
        => Update(ids.Select(id => KeyValuePair.Create(id, id)));

    public void Update(IEnumerable<KeyValuePair<string, string>> notes)
    {
        _groups = notes.Where(item => !string.IsNullOrWhiteSpace(item.Key)).GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Last().Value ?? "", StringComparer.OrdinalIgnoreCase);
        _ids = _groups.Keys.ToArray();
        var valid = _ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = _bag.Where(valid.Contains).ToArray(); _bag.Clear(); foreach (var id in pending) _bag.Enqueue(id);
        var history = _history.Where(valid.Contains).Reverse().ToArray(); _history.Clear(); foreach (var id in history) _history.Push(id);
    }

    public string? Daily(DateOnly date)
    {
        var day = date.ToString("yyyy-MM-dd");
        return _ids.MinBy(id => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{day}:{id}"))), StringComparer.Ordinal);
    }

    public string? Next(string? currentId)
    {
        if (_ids.Length == 0) return null;
        if (!string.IsNullOrWhiteSpace(currentId))
        {
            _history.Push(currentId);
            if (_history.Count > 10) { var recent = _history.Take(10).Reverse().ToArray(); _history.Clear(); foreach (var id in recent) _history.Push(id); }
        }
        if (_bag.Count == 0)
        {
            var shuffled = _ids.OrderBy(_ => _random.Next()).ToList();
            if (shuffled.Count > 1 && shuffled[0].Equals(currentId, StringComparison.OrdinalIgnoreCase)) (shuffled[0], shuffled[1]) = (shuffled[1], shuffled[0]);
            foreach (var id in shuffled) _bag.Enqueue(id);
        }
        if (!string.IsNullOrWhiteSpace(currentId) && _bag.Count > 1 && _groups.GetValueOrDefault(_bag.Peek()) == _groups.GetValueOrDefault(currentId))
        {
            var pending = _bag.ToList(); var different = pending.FindIndex(id => _groups.GetValueOrDefault(id) != _groups.GetValueOrDefault(currentId));
            if (different > 0) (pending[0], pending[different]) = (pending[different], pending[0]);
            _bag.Clear(); foreach (var id in pending) _bag.Enqueue(id);
        }
        return _bag.Dequeue();
    }

    public string? Previous() => _history.TryPop(out var id) ? id : null;
}
