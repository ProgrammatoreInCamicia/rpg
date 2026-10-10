using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

// T6b: sight and hearing on area maps (ADAPTATION, see the contract). Off the maps, perception stays place-based.
internal sealed partial class Simulation
{
    /// <summary>Light on a square: the better of daylight and the map's light sources (lamps, torches).</summary>
    internal Light LightOn(GridMap map, GridPos square) => Max(Perception.DaylightAt(World.Now), map.SourceLight(square));

    /// <summary>The best light on a square over a stretch of time: sources are steady, daylight changes on the hour.</summary>
    private Light BestLightOn(GridMap map, GridPos square, GameTime from, GameTime to) =>
        Max(Perception.BestDaylightDuring(from, to), map.SourceLight(square));

    private static Light Max(Light a, Light b) => a > b ? a : b;

    /// <summary>Whether <paramref name="watcher"/> can see what stands on <paramref name="square"/> right now.</summary>
    internal bool CanSee(GridMap map, GridPos watcher, GridPos square) =>
        map.HasLineOfSight(watcher, square) && LightOn(map, square) >= Light.Dim;

    internal static bool WithinEarshot(GridMap map, GridPos listener, GridPos source) =>
        map.SoundSteps(listener, source, Tuning.HearingRadius) is not null;

    /// <summary>
    /// Where an actor stood at an earlier instant, if it can be told from its current action: the square on a walk
    /// in progress, or its square during an activity begun before then. Otherwise its current square (approximation).
    /// </summary>
    private GridPos? PositionAt(Actor actor, GameTime t) =>
        actor.CurrentAction is MoveAction move && move.StartedAt <= t ? move.PositionAt(t) : CurrentPosition(actor);

    /// <summary>
    /// Perception of a theft on a mapped area: anyone on the same map may notice it by sight (line of sight and light on
    /// the thief's square, best light over the stretch watched) or by hearing (within earshot). Recognising the thief
    /// takes seeing them both now and from where the witness stood when the theft began.
    /// </summary>
    private void PerceiveTheftOnMap(Actor thief, TakeFoodAction take, Store store, int amount, FactId fact, GridMap map)
    {
        var stealth = take.StealthTotal ?? 0;
        var thiefSquare = CurrentPosition(thief)!.Value;
        foreach (var witness in World.Actors.Values.Where(a => a.Id != thief.Id && a.MapArea == map.Area).ToList())
        {
            var here = CurrentPosition(witness)!.Value;
            var watchedFrom = witness.ArrivedAt > take.StartedAt ? witness.ArrivedAt : take.StartedAt;
            var light = BestLightOn(map, thiefSquare, watchedFrom, World.Now);
            var senses = Perception.Witness(witness.Sheet, light, stealth);
            var seen = senses.SawActor && map.HasLineOfSight(here, thiefSquare);
            var heard = senses.HearingPerception >= stealth && WithinEarshot(map, here, thiefSquare);
            if (!seen && !heard)
            {
                World.RecordFact("FoodTheftUnnoticed",
                    $"{witness.Name} non si accorge di nulla ({Perception.Describe(light)}, " +
                    $"{(map.HasLineOfSight(here, thiefSquare) ? "in vista" : "fuori vista")}, " +
                    $"{(WithinEarshot(map, here, thiefSquare) ? "a portata d'orecchio" : "troppo lontano per sentire")}).");
                continue;
            }

            var startSquare = PositionAt(witness, take.StartedAt);
            var recognised = seen && witness.ArrivedAt <= take.StartedAt
                             && startSquare is { } s && map.HasLineOfSight(s, thiefSquare);
            Witnessed(witness, thief, store, amount, fact, recognised, seen ? PerceptionMode.Seen : PerceptionMode.Heard, light);
        }
    }
}
