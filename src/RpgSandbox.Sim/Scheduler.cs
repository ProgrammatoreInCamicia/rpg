using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>
/// Explicit deadlines ordered by (due time, scheduling sequence). Simulation time jumps from one
/// deadline to the next instead of stepping second by second. Ties resolve in scheduling order.
/// </summary>
internal sealed class Scheduler
{
    private readonly SortedSet<Entry> _entries = new();
    private long _nextSequence = 1;

    internal readonly record struct Entry(GameTime Due, long Sequence, ActionId Action) : IComparable<Entry>
    {
        public int CompareTo(Entry other)
        {
            var byTime = Due.CompareTo(other.Due);
            return byTime != 0 ? byTime : Sequence.CompareTo(other.Sequence);
        }
    }

    public int Count => _entries.Count;

    public void Schedule(GameTime due, ActionId action) =>
        _entries.Add(new Entry(due, _nextSequence++, action));

    public bool Contains(ActionId action) => _entries.Any(e => e.Action == action);

    public GameTime? NextDue => _entries.Count == 0 ? null : _entries.Min.Due;

    /// <summary>Removes and returns, in order, every entry due exactly at <paramref name="instant"/>.</summary>
    public List<Entry> TakeDueAt(GameTime instant)
    {
        var due = new List<Entry>();
        while (_entries.Count > 0 && _entries.Min.Due == instant)
        {
            var entry = _entries.Min;
            _entries.Remove(entry);
            due.Add(entry);
        }
        return due;
    }
}
