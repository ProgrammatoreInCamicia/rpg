using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>Something that must happen at a given instant.</summary>
internal abstract record ScheduledJob;

/// <summary>An actor's action reaches its completion time.</summary>
internal sealed record CompleteAction(ActionId Action) : ScheduledJob;

/// <summary>A faction consumes its daily food.</summary>
internal sealed record FactionUpkeep(FactionId Faction) : ScheduledJob;

/// <summary>A faction re-evaluates its policy.</summary>
internal sealed record EvaluateFaction(FactionId Faction) : ScheduledJob;

/// <summary>
/// Explicit deadlines ordered by (due time, scheduling sequence). Simulation time jumps from one
/// deadline to the next instead of stepping second by second. Ties resolve in scheduling order.
/// </summary>
internal sealed class Scheduler
{
    private readonly SortedSet<Entry> _entries = new();

    internal readonly record struct Entry(GameTime Due, long Sequence, ScheduledJob Job) : IComparable<Entry>
    {
        public int CompareTo(Entry other)
        {
            var byTime = Due.CompareTo(other.Due);
            return byTime != 0 ? byTime : Sequence.CompareTo(other.Sequence);
        }
    }

    /// <summary>Next sequence number to assign; persisted so a loaded game breaks ties exactly as before.</summary>
    public long NextSequence { get; private set; } = 1;

    public IEnumerable<Entry> Entries => _entries;

    public void Schedule(GameTime due, ScheduledJob job) =>
        _entries.Add(new Entry(due, NextSequence++, job));

    public bool Contains(ActionId action) => _entries.Any(e => e.Job is CompleteAction c && c.Action == action);

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

    /// <summary>Rebuilds a scheduler from a save, keeping the original sequence numbers.</summary>
    public static Scheduler Restore(IEnumerable<Entry> entries, long nextSequence)
    {
        var scheduler = new Scheduler { NextSequence = nextSequence };
        foreach (var entry in entries)
        {
            if (entry.Sequence >= nextSequence || !scheduler._entries.Add(entry))
                throw new InvalidDataException($"Invalid scheduler entry sequence {entry.Sequence}.");
        }
        return scheduler;
    }
}
