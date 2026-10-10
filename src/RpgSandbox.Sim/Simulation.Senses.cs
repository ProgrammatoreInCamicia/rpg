using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

// T6b: sight and hearing on area maps (ADAPTATION, see the contract). Off the maps, perception stays place-based.
internal sealed partial class Simulation
{
    /// <summary>Light on a square: the better of daylight and the map's light sources (lamps, torches).</summary>
    internal Light LightOn(GridMap map, GridPos square) => LightOnAt(map, square, World.Now);

    /// <summary>
    /// Light on a square at an instant: daylight then, fixed sources, and the torches burning now (approximation: who
    /// carries a torch is not recorded over time).
    /// </summary>
    private Light LightOnAt(GridMap map, GridPos square, GameTime t) =>
        Max(Perception.DaylightAt(t), map.SourceLight(square, CarriedLights(map)));

    /// <summary>A torch burning in someone's hand (SRD: 1 hour from when it was lit).</summary>
    internal bool TorchLit(Actor actor) => actor.TorchLitUntil is { } until && World.Now < until;

    /// <summary>Torches burning on this map, each on its bearer's square (SRD Torch: Bright 20 ft, Dim 20 ft more).</summary>
    private List<LightSource> CarriedLights(GridMap map) =>
        World.Actors.Values
            .Where(a => a.MapArea == map.Area && TorchLit(a) && CurrentPosition(a) is not null)
            .Select(a => new LightSource(CurrentPosition(a)!.Value, Tuning.TorchBrightFeet, Tuning.TorchDimFeet))
            .ToList();

    private CommandResult UseTorch(TorchCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (TorchLit(actor) == command.Lit)
            return CommandResult.Rejected(RejectionReason.AlreadyDone, command.Lit ? "La torcia è già accesa." : "Non hai una torcia accesa.");
        if (!command.Lit)
        {
            actor.TorchLitUntil = null;
            World.RecordFact("TorchOut", $"{actor.Name} spegne la torcia.", actor.Id);
            return new CommandResult { Message = "Spegni la torcia." };
        }
        if (actor.Torches == 0)
            return CommandResult.Rejected(RejectionReason.NoTorch, "Non hai torce.");
        actor.Torches--;
        actor.TorchLitUntil = World.Now.Plus(Tuning.TorchBurns);
        World.RecordFact("TorchLit", $"{actor.Name} accende una torcia.", actor.Id);
        return new CommandResult { Message = $"Accendi una torcia: brucerà per un'ora. Te ne restano {actor.Torches}." };
    }

    /// <summary>The best light on a square over a stretch of time: sources are steady, daylight changes on the hour.</summary>
    private Light BestLightOn(GridMap map, GridPos square, GameTime from, GameTime to) =>
        Max(Perception.BestDaylightDuring(from, to), map.SourceLight(square, CarriedLights(map)));

    private static Light Max(Light a, Light b) => a > b ? a : b;

    /// <summary>Whether <paramref name="watcher"/> can see what stands on <paramref name="square"/> right now.</summary>
    internal bool CanSee(GridMap map, GridPos watcher, GridPos square) =>
        map.HasLineOfSight(watcher, square) && LightOn(map, square) >= Light.Dim;

    internal static bool WithinEarshot(GridMap map, GridPos listener, GridPos source) =>
        map.SoundSteps(listener, source, Tuning.HearingRadius) is not null;

    /// <summary>
    /// Whether <paramref name="watcher"/>, standing on <paramref name="from"/>, sees <paramref name="target"/> on
    /// <paramref name="at"/> in <paramref name="light"/> (F3, ADAPTATION of Hide): never in the dark; in dim light a
    /// sneaking target is seen only with Passive Perception at Disadvantage (−5) reaching its Stealth total; in bright
    /// light anyone in line of sight is seen.
    /// </summary>
    internal static bool Sees(GridMap map, Actor watcher, GridPos from, Actor target, GridPos at, Light light) =>
        map.HasLineOfSight(from, at) && light switch
        {
            Light.Bright => true,
            Light.Dim => target.Sneak is not { } dc || watcher.Sheet.PassivePerception(disadvantage: true) >= dc,
            _ => false,
        };

    /// <summary>F5: a sneaking walk pays for light, so it takes a few more squares to stay in the dark.</summary>
    private int SneakCost(GridMap map, GridPos square) => LightOn(map, square) switch
    {
        Light.Bright => 3,
        Light.Dim => 1,
        _ => 0,
    };

    /// <summary>
    /// Where an actor stood at an earlier instant, if it can be told from its current action: the square on a walk
    /// in progress, or its square during an activity begun before then. Otherwise its current square (approximation).
    /// </summary>
    private GridPos? PositionAt(Actor actor, GameTime t) =>
        actor.CurrentAction is MoveAction move && move.StartedAt <= t ? move.PositionAt(t) : CurrentPosition(actor);

    /// <summary>
    /// Perception of a theft on a mapped area: anyone on the same map may notice it by sight (line of sight and light on
    /// the thief's square, best light over the stretch watched) or by hearing (within earshot). Recognising the thief
    /// takes seeing them both now and, by the sneaking rules, from where the witness stood when the theft began.
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
            // F4: recognising takes having SEEN the thief when the theft began (a sneaking thief in dim light may not be).
            var recognised = seen && witness.ArrivedAt <= take.StartedAt && startSquare is { } s
                             && Sees(map, witness, s, thief, thiefSquare, LightOnAt(map, thiefSquare, take.StartedAt));
            Witnessed(witness, thief, store, amount, fact, recognised, seen ? PerceptionMode.Seen : PerceptionMode.Heard, light);
        }
    }
}
