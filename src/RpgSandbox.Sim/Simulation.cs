using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>
/// The simulation engine: owns the world state and is its only writer. Split in partial files:
/// time (this file), commands and completions, factions, NPC decisions.
/// </summary>
internal sealed partial class Simulation
{
    public Simulation(WorldState world) => World = world;

    public WorldState World { get; }

    /// <summary>
    /// First instant of a new game: schedules faction upkeep, evaluates factions and lets NPCs decide,
    /// exactly as if the scenario's start were a processed instant.
    /// </summary>
    public void Bootstrap()
    {
        foreach (var faction in World.Factions.Values)
        {
            if (faction.DailyUpkeep > 0)
                World.Scheduler.Schedule(NextUpkeepAfter(World.Now, faction), new FactionUpkeep(faction.Id));
            if (faction.Policy is not null)
                EvaluateFactionPolicy(faction);
        }
        DecideForFreeNpcs();
    }

    public GameTime TargetAfter(Duration duration)
    {
        if (duration.Seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Time cannot go backwards.");
        if (duration.Seconds > long.MaxValue - World.Now.Seconds)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Time overflow.");
        return World.Now.Plus(duration);
    }

    /// <summary>The walker a waypoint or a walk completion belongs to (null for any other job, or a stale one).</summary>
    private Actor? WalkerOf(ScheduledJob job) => job switch
    {
        MoveWaypoint w => World.Actors.Values.FirstOrDefault(a => a.CurrentAction is MoveAction m && m.Id == w.Action),
        CompleteAction c => World.Actors.Values.FirstOrDefault(a => a.CurrentAction is MoveAction m && m.Id == c.Action),
        _ => null,
    };

    public bool IsActive(ActionId action) => World.Actors.Values.Any(a => a.CurrentAction?.Id == action);

    /// <summary>
    /// Processes deadlines up to and including <paramref name="target"/>, one instant at a time.
    /// If <paramref name="stopAfter"/> is given, stops at the end of the instant in which that action
    /// completed or was cancelled, and says which. Returns null when <paramref name="target"/> was reached.
    /// </summary>
    public AdvanceOutcome? ProcessUntil(GameTime target, ActionId? stopAfter)
    {
        while (World.Scheduler.NextDue is { } due && due <= target)
        {
            World.Now = due;
            var entries = World.Scheduler.TakeDueAt(due);

            // Phase 0 (T6c-1): positions. Waypoints and walk completions due now, in actor ID order, so every position of
            // the instant is known before anything is judged from it; then the contacts, once.
            var completedAwaited = false;
            var walking = entries
                .Select(e => (Entry: e, Walker: WalkerOf(e.Job)))
                .Where(x => x.Walker is not null)
                .OrderBy(x => x.Walker!.Id.Value, StringComparer.Ordinal)
                // For one walker: its waypoints step by step, then the completion (a fast walker may reach several
                // squares and the end within the same second; the completion was scheduled first, so it must not go first).
                .ThenBy(x => x.Entry.Job is CompleteAction ? 1 : 0)
                .ThenBy(x => (x.Entry.Job as MoveWaypoint)?.Step ?? 0)
                .ToList();
            foreach (var (entry, _) in walking)
            {
                if (entry.Job is MoveWaypoint waypoint)
                    RunWaypoint(waypoint);
                else if (entry.Job is CompleteAction complete)
                    completedAwaited |= CompleteActionJob(complete.Action) && complete.Action == stopAfter;
            }
            EvaluateContacts();
            var positional = walking.Select(x => x.Entry.Sequence).ToHashSet();

            // Phase 1: the other action completions, in (due, sequence) order, perception included.
            // An entry whose action was cancelled earlier in this same batch completes nothing.
            foreach (var entry in entries)
            {
                if (positional.Contains(entry.Sequence) || entry.Job is not CompleteAction complete)
                    continue;
                var completed = CompleteActionJob(complete.Action);
                completedAwaited |= completed && complete.Action == stopAfter;
            }
            EvaluateContacts(); // arrivals from a journey onto a map

            // Phase 2: faction jobs due now, in scheduling order.
            foreach (var entry in entries)
            {
                switch (entry.Job)
                {
                    case FactionUpkeep upkeep:
                        RunUpkeep(World.Factions[upkeep.Faction]);
                        break;
                    case EvaluateFaction evaluate:
                        EvaluateFactionPolicy(World.Factions[evaluate.Faction]);
                        break;
                }
            }

            // Phase 3: free NPCs decide what to do next.
            DecideForFreeNpcs();
            EvaluateContacts();

            if (completedAwaited)
                return AdvanceOutcome.Completed;
            // Not completed yet no longer active: it was cancelled during this instant.
            if (stopAfter is { } awaited && !IsActive(awaited))
                return AdvanceOutcome.Cancelled;
        }
        return null;
    }
}
