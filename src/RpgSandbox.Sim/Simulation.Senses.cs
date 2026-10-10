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

    /// <summary>Applies a change of light around <paramref name="actor"/>, noting it for thefts in progress before and after.</summary>
    private void LightChange(Actor actor, Action change)
    {
        var map = MapOf(actor);
        if (map is not null)
            NoteLightChange(map);
        change();
        if (map is not null)
            NoteLightChange(map);
    }

    private CommandResult UseTorch(TorchCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (TorchLit(actor) == command.Lit)
            return CommandResult.Rejected(RejectionReason.AlreadyDone, command.Lit ? "La torcia è già accesa." : "Non hai una torcia accesa.");
        if (!command.Lit)
        {
            LightChange(actor, () => actor.TorchLitUntil = null);
            World.RecordFact("TorchOut", $"{actor.Name} spegne la torcia.", actor.Id);
            return new CommandResult { Message = "Spegni la torcia." };
        }
        if (actor.Torches == 0)
            return CommandResult.Rejected(RejectionReason.NoTorch, "Non hai torce.");
        actor.Torches--;
        LightChange(actor, () => actor.TorchLitUntil = World.Now.Plus(Tuning.TorchBurns));
        World.RecordFact("TorchLit", $"{actor.Name} accende una torcia.", actor.Id);
        return new CommandResult { Message = $"Accendi una torcia: brucerà per un'ora. Te ne restano {actor.Torches}." };
    }

    /// <summary>
    /// The best light on a thief's square over the part of a theft a witness watched: what was recorded at the start
    /// and at every change of light since (<see cref="TakeFoodAction.BestLight"/>), daylight over the stretch, and the
    /// light now. A witness who arrived later only gets daylight over its own stretch and the light now.
    /// </summary>
    private Light BestLightDuringTheft(GridMap map, GridPos square, TakeFoodAction take, GameTime watchedFrom)
    {
        var best = Max(Perception.BestDaylightDuring(watchedFrom, World.Now), LightOn(map, square));
        return watchedFrom == take.StartedAt && take.BestLight is { } recorded ? Max(best, recorded) : best;
    }

    /// <summary>Who sees <paramref name="target"/> right now on its map, by the rules of F3.</summary>
    internal HashSet<ActorId> WhoSeesOnMap(Actor target)
    {
        if (MapOf(target) is not { } map || CurrentPosition(target) is not { } at)
            return new HashSet<ActorId>();
        var light = LightOn(map, at);
        return World.Actors.Values
            .Where(a => a.Id != target.Id && a.MapArea == map.Area && CurrentPosition(a) is { } from && Sees(map, a, from, target, at, light))
            .Select(a => a.Id)
            .ToHashSet();
    }

    /// <summary>
    /// A hidden actor seen by someone stops being hidden (SRD Hide: "an enemy finds you"). Checked on every square of a
    /// sneaking walk, so crossing bright light in someone's view gives one away.
    /// </summary>
    private void CheckDiscovered(Actor actor)
    {
        var finders = WhoSeesOnMap(actor);
        if (finders.Count == 0)
            return;
        actor.Sneak = null;
        var names = string.Join(", ", finders.Select(id => World.Actors[id].Name).Order(StringComparer.Ordinal));
        World.RecordFact("SneakDiscovered", $"{actor.Name} è scoperto: lo vede {names}.", actor.Id);
    }

    /// <summary>
    /// Light may have changed on a map (a torch, a door, a torch bearer on the move): thefts in progress keep the best
    /// light seen so far on the thief's square. Called before and after each change.
    /// </summary>
    private void NoteLightChange(GridMap map)
    {
        foreach (var thief in World.Actors.Values.Where(a => a.MapArea == map.Area))
            if (thief.CurrentAction is TakeFoodAction { BestLight: { } best } take && CurrentPosition(thief) is { } square)
                take.BestLight = Max(best, LightOn(map, square));
    }

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
    /// Perception of a theft on a mapped area: anyone on the same map may notice it by sight (line of sight and light on
    /// the thief's square, best light over the stretch watched) or by hearing (within earshot). Recognising the thief
    /// takes seeing them now and having seen them when the theft began (evidence fixed then, F4).
    /// </summary>
    private void PerceiveTheftOnMap(Actor thief, TakeFoodAction take, Store store, int amount, FactId fact, GridMap map)
    {
        var stealth = take.StealthTotal ?? 0;
        var thiefSquare = CurrentPosition(thief)!.Value;
        foreach (var witness in World.Actors.Values.Where(a => a.Id != thief.Id && a.MapArea == map.Area).ToList())
        {
            var here = CurrentPosition(witness)!.Value;
            var watchedFrom = witness.ArrivedAt > take.StartedAt ? witness.ArrivedAt : take.StartedAt;
            var light = BestLightDuringTheft(map, thiefSquare, take, watchedFrom);
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

            // F4: recognising takes having SEEN the thief when the theft began, as recorded then: later changes of light
            // (a torch lit afterwards) cannot reveal who started it.
            var recognised = seen && take.SeenAtStart.Contains(witness.Id);
            Witnessed(witness, thief, store, amount, fact, recognised, seen ? PerceptionMode.Seen : PerceptionMode.Heard, light);
        }
    }
}
