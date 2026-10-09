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

    /// <summary>
    /// Processes deadlines up to and including <paramref name="target"/>, one instant at a time.
    /// Returns true if it stopped because <paramref name="stopAfter"/> completed.
    /// </summary>
    public bool ProcessUntil(GameTime target, ActionId? stopAfter)
    {
        while (World.Scheduler.NextDue is { } due && due <= target)
        {
            World.Now = due;
            var entries = World.Scheduler.TakeDueAt(due);

            // Phase 1: action completions, in (due, sequence) order.
            var completedAwaited = false;
            foreach (var entry in entries)
            {
                if (entry.Job is not CompleteAction complete)
                    continue;
                CompleteActionJob(complete.Action);
                completedAwaited |= complete.Action == stopAfter;
            }

            // Phase 2 (perception) arrives with increment 3.

            // Phase 3: faction jobs due now, in scheduling order.
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

            // Phase 4: free NPCs decide what to do next.
            DecideForFreeNpcs();

            if (completedAwaited)
                return true;
        }
        return false;
    }
}
