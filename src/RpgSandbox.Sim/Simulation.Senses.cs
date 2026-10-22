using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

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
        Max(map.DaylightOn(square, Perception.DaylightAt(t)), map.SourceLight(square, CarriedLights(map)));

    /// <summary>A torch burning in someone's hand (SRD: 1 hour from when it was lit).</summary>
    internal bool TorchLit(Actor actor) => actor.TorchLitUntil is { } until && World.Now < until;

    /// <summary>Torches burning on this map, each on its bearer's square (SRD Torch: Bright 20 ft, Dim 20 ft more).</summary>
    private List<LightSource> CarriedLights(GridMap map) =>
        World.Actors.Values
            .Where(a => a.MapArea == map.Area && (TorchLit(a) || HasEffect(a, "sacred-weapon")) && CurrentPosition(a) is not null) // R3c: Sacred Weapon, Bright 20 ft and Dim 20 ft too
            .Select(a => new LightSource(CurrentPosition(a)!.Value, Tuning.TorchBrightFeet, Tuning.TorchDimFeet))
            .ToList();

    /// <summary>Applies a change of light around <paramref name="actor"/>, noting it for thefts in progress before and after.</summary>
    private void LightChange(Actor actor, Action change)
    {
        var map = MapOf(actor);
        if (map is not null)
            NoteSensesChange(map);
        change();
        if (map is not null)
            NoteSensesChange(map);
    }

    /// <summary>
    /// T1, SRD Torch: a lit torch is held in the off hand, so lighting it takes that hand free (no shield, no second weapon,
    /// no two-handed weapon); <see cref="TorchCommand.StowShield"/> (always, for an NPC) puts the shield away first. In a
    /// fight lighting or putting it out is the turn's free interaction (a second takes the action), and doffing the shield
    /// takes the Utilize action.
    /// </summary>
    private CommandResult UseTorch(TorchCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (TorchLit(actor) == command.Lit)
            return CommandResult.Rejected(RejectionReason.AlreadyDone, command.Lit ? "La torcia è già accesa." : "Non hai una torcia accesa.");
        if (command.Lit && actor.Torches == 0)
            return CommandResult.Rejected(RejectionReason.NoTorch, "Non hai torce.");
        var stow = command.Lit && actor.Equipment.Shield is not null && (command.StowShield || !actor.IsPlayer);
        if (command.Lit && !stow && OffHandTaken(actor) is { } taken)
            return CommandResult.Rejected(RejectionReason.NoFreeHand, taken);
        if (command.Lit && stow && TwoHandedInHand(actor))
            return CommandResult.Rejected(RejectionReason.NoFreeHand, "Ti serve una mano libera: l'arma che impugni ne vuole due.");
        if (command.Lit && stow && actor.Equipment.OffHand is not null)
            return CommandResult.Rejected(RejectionReason.NoFreeHand, "Ti serve una mano libera: riponi la seconda arma.");

        var paid = "";
        if (EncounterOf(actor) is { } e)
        {
            if (!IsTurnOf(e, actor))
                return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
            var me = CombatantOf(e, actor.Id);
            var actions = (stow ? 1 : 0) + (me.Interacted ? 1 : 0); // the shield takes the action; a second interaction too
            if (actions > 1 || actions == 1 && me.ActionUsed)
                return CommandResult.Rejected(RejectionReason.NoActionLeft, stow
                    ? "Riporre lo scudo richiede l'azione, e accendere l'interazione: non ti restano."
                    : "Hai già usato l'interazione con un oggetto e l'azione in questo turno.");
            if (actions == 1)
            {
                me.ActionUsed = true;
                paid = " (usi l'azione)";
            }
            if (!me.Interacted)
                me.Interacted = true;
        }

        if (!command.Lit)
        {
            LightChange(actor, () => actor.TorchLitUntil = null);
            World.Refresh(actor);
            World.RecordFact("TorchOut", $"{actor.Name} spegne la torcia.", actor.Id);
            return new CommandResult { Message = $"Spegni la torcia{paid}." };
        }
        if (stow)
            actor.Equipment = actor.Equipment with { Shield = null };
        actor.Torches--;
        LightChange(actor, () => actor.TorchLitUntil = World.Now.Plus(Tuning.TorchBurns));
        World.Refresh(actor);
        World.RecordFact("TorchLit", stow ? $"{actor.Name} ripone lo scudo e accende una torcia." : $"{actor.Name} accende una torcia.", actor.Id);
        return new CommandResult
        {
            Message = $"{(stow ? "Riponi lo scudo e accendi" : "Accendi")} una torcia: brucerà per un'ora. Te ne restano {actor.Torches}.{paid}",
        };
    }

    /// <summary>T1: why the off hand is not free for a torch (null if it is).</summary>
    private string? OffHandTaken(Actor actor) =>
        actor.Equipment.Shield is not null ? "Ti serve una mano libera: riponi lo scudo (o accendi riponendolo)."
        : actor.Equipment.OffHand is not null ? "Ti serve una mano libera: riponi la seconda arma."
        : TwoHandedInHand(actor) ? "Ti serve una mano libera: l'arma che impugni ne vuole due."
        : null;

    private bool TwoHandedInHand(Actor actor) =>
        actor.Equipment.MainHand is { } main && World.Content.Item(main)?.Weapon is { } w && w.Has(WeaponProperty.TwoHanded);

    /// <summary>Light on a square from lamps and carried torches only (daylight is accounted for apart).</summary>
    private Light SourceLightOn(GridMap map, GridPos square) => map.SourceLight(square, CarriedLights(map));

    /// <summary>
    /// T6c-5: the best light a witness could have seen the thief in: daylight and lamp or torch light, taken only over
    /// the stretches during which it had a line of sight to the thief. Dark if it never had one.
    /// </summary>
    private Light BestLightInSight(TakeFoodAction take, ActorId witness)
    {
        if (!take.Sight.TryGetValue(witness, out var spans))
            return Light.Dark;
        var best = Light.Dark;
        foreach (var span in spans)
        {
            var from = span.From;
            var to = span.To ?? World.Now;
            best = Max(best, Perception.BestDaylightDuring(from, to));
            for (var i = 0; i < take.SourceLightLog.Count; i++)
            {
                // Each recorded light holds from its instant until the next change.
                var (at, light) = take.SourceLightLog[i];
                var until = i + 1 < take.SourceLightLog.Count ? take.SourceLightLog[i + 1].At : (GameTime?)null;
                if (at <= to && (until is null || until > from))
                    best = Max(best, light);
            }
        }
        return best;
    }

    /// <summary>
    /// T6c-5: senses may have changed on a map (someone moved, a torch, a door). For every theft in progress there, the
    /// light on the thief's square is logged if it changed, and every actor's stretches in sight of the thief are
    /// opened or closed. Called after every such change, so the evidence never depends on the state at the end.
    /// </summary>
    private void NoteSensesChange(GridMap map)
    {
        foreach (var thief in World.Actors.Values.Where(a => a.MapArea == map.Area)
                     .OrderBy(a => a.Id.Value, StringComparer.Ordinal).ToList())
        {
            if (thief.CurrentAction is not TakeFoodAction take || take.SourceLightLog.Count == 0
                || CurrentPosition(thief) is not { } square)
                continue;
            var light = SourceLightOn(map, square);
            if (take.SourceLightLog[^1].Light != light)
                take.SourceLightLog.Add((World.Now, light));
            UpdateSight(map, thief, take, square);
        }
    }

    /// <summary>Opens or closes each actor's stretch in sight of the thief (anyone off this map is out of sight).</summary>
    private void UpdateSight(GridMap map, Actor thief, TakeFoodAction take, GridPos square)
    {
        foreach (var other in World.Actors.Values.Where(a => a.Id != thief.Id).OrderBy(a => a.Id.Value, StringComparer.Ordinal))
        {
            var inSight = other.MapArea == map.Area && !Asleep(other) && CurrentPosition(other) is { } from
                && map.HasLineOfSight(from, square) && InFacing(other, CurrentSpot(other) ?? Spot.CenterOf(from), Spot.CenterOf(square));
            take.Sight.TryGetValue(other.Id, out var spans);
            var open = spans is { Count: > 0 } && spans[^1].To is null;
            if (inSight && !open)
            {
                if (spans is null)
                    take.Sight[other.Id] = spans = new List<SightSpan>();
                spans.Add(new SightSpan { From = World.Now });
            }
            else if (!inSight && open)
                spans![^1].To = World.Now;
        }
    }

    /// <summary>T6c-5: the evidence a theft on a map starts with: the light on the thief's square and who has it in sight.</summary>
    private void StartTheftEvidence(Actor thief, TakeFoodAction take)
    {
        if (MapOf(thief) is not { } map || CurrentPosition(thief) is not { } square)
            return;
        take.SourceLightLog.Add((World.Now, SourceLightOn(map, square)));
        UpdateSight(map, thief, take, square);
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


    private static Light Max(Light a, Light b) => a > b ? a : b;

    /// <summary>The player may turn without spending time. Turning updates sight evidence and exploration immediately.</summary>
    internal void FacePlayer(GridPos direction)
    {
        var player = World.Actors[World.Player];
        var map = MapOf(player);
        if (map is not null) NoteSensesChange(map);
        player.Facing = direction;
        if (map is not null)
        {
            NoteSensesChange(map);
            RememberDoors();
        }
    }

    /// <summary>Near the body there is peripheral awareness; farther away, a 160-degree cone without a range cap.</summary>
    internal bool InFacing(Actor watcher, Spot eye, Spot target, bool ignoreFacing = false)
    {
        if (ignoreFacing || EncounterOf(watcher) is not null || watcher.Facing is not { } direction) return true;
        var dx = target.X - (long)eye.X;
        var dy = target.Y - (long)eye.Y;
        var distanceSquared = dx * dx + dy * dy;
        if (distanceSquared <= 90L * 90) return true; // adjacent floor remains apparent, including behind the body
        var dot = direction.X * dx + direction.Y * dy;
        var facingSquared = (long)direction.X * direction.X + (long)direction.Y * direction.Y;
        return dot > 0 && (double)dot * dot >= distanceSquared * (double)facingSquared * 0.03015369; // cos(80°)^2
    }

    /// <summary>Whether <paramref name="watcher"/> can see what stands on <paramref name="square"/> right now.</summary>
    internal bool CanSee(GridMap map, Actor watcher, GridPos from, GridPos square) =>
        !Asleep(watcher) && map.HasLineOfSight(from, square) && LightOn(map, square) >= Light.Dim
        && InFacing(watcher, CurrentSpot(watcher) ?? Spot.CenterOf(from), Spot.CenterOf(square));

    internal static bool WithinEarshot(GridMap map, GridPos listener, GridPos source) =>
        map.SoundSteps(listener, source, Tuning.HearingRadius) is not null;

    /// <summary>
    /// Whether <paramref name="watcher"/>, standing on <paramref name="from"/>, sees <paramref name="target"/> on
    /// <paramref name="at"/> in <paramref name="light"/> (F3, ADAPTATION of Hide): never in the dark; in dim light a
    /// sneaking target is seen only with Passive Perception at Disadvantage (−5) reaching its Stealth total; in bright
    /// light anyone in line of sight is seen.
    /// </summary>
    internal bool Sees(GridMap map, Actor watcher, GridPos from, Actor target, GridPos at, Light light,
        bool ignoreFacing = false, Spot? fromSpot = null, Spot? atSpot = null) =>
        watcher.Conscious && !Asleep(watcher) && map.HasLineOfSight(from, at) // R1: asleep, it sees nothing
        && !Has(watcher, ConditionKind.Blinded) && !Has(target, ConditionKind.Invisible) // R2
        && InFacing(watcher,
            fromSpot ?? (CurrentSpot(watcher) is { } eye && eye.Cell == from ? eye : Spot.CenterOf(from)),
            atSpot ?? (CurrentSpot(target) is { } there && there.Cell == at ? there : Spot.CenterOf(at)),
            ignoreFacing)
        && light switch // C2: the fallen see nothing
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
    /// Perception of a theft on a mapped area: anyone on the same map may notice it by sight (the best light on the
    /// thief's square, judged only over the stretches it had a line of sight to the thief, T6c-5) or by hearing (within
    /// earshot at the end). Recognising the thief takes seeing it and having seen it when the theft began (F4).
    /// </summary>
    private void PerceiveTheftOnMap(Actor thief, TakeFoodAction take, Store store, int amount, FactId fact, GridMap map)
    {
        var stealth = take.StealthTotal ?? 0;
        var thiefSquare = CurrentPosition(thief)!.Value;
        NoteSensesChange(map); // the evidence up to this very instant
        foreach (var witness in World.Actors.Values.Where(a => a.Id != thief.Id && a.MapArea == map.Area && a.Conscious).ToList())
        {
            var here = CurrentPosition(witness)!.Value;
            var everInSight = take.Sight.ContainsKey(witness.Id);
            var light = everInSight ? BestLightInSight(take, witness.Id) : LightOn(map, thiefSquare);
            var senses = Perception.Witness(witness.Sheet, light, stealth);
            // At arm's reach, an actor visibly handling the store cannot conceal that ongoing deed with a
            // Stealth roll. This matches the visible Doing shown to the player; darkness and walls still hide it.
            var closeSeen = here.IsAdjacentOrSame(thiefSquare) && CanSee(map, witness, here, thiefSquare)
                && !Has(witness, ConditionKind.Blinded) && !Has(thief, ConditionKind.Invisible);
            var seen = closeSeen || (everInSight && senses.SawActor);
            var heard = senses.HearingPerception >= stealth && WithinEarshot(map, here, thiefSquare);
            if (!seen && !heard)
            {
                World.RecordFact("FoodTheftUnnoticed",
                    $"{witness.Name} non si accorge di nulla ({Perception.Describe(light)}, " +
                    $"{(everInSight ? "in vista" : "fuori vista")}, " +
                    $"{(WithinEarshot(map, here, thiefSquare) ? "a portata d'orecchio" : "troppo lontano per sentire")}).");
                continue;
            }

            // F4: recognising takes having SEEN the thief when the theft began, as recorded then: later changes of light
            // (a torch lit afterwards) cannot reveal who started it.
            var recognised = seen && take.SeenAtStart.Contains(witness.Id);
            Witnessed(witness, thief, store, amount, fact, recognised, seen ? PerceptionMode.Seen : PerceptionMode.Heard, light);
        }
    }

    // ---------------------------------------------------------------- T6c-5: the player's memory of doors

    /// <summary>A door is seen from next to it, or in line of sight with at least dim light on it.</summary>
    internal bool SeesDoor(Actor actor, GridMap map, GridPos door) =>
        actor.MapArea == map.Area && !Asleep(actor) && CurrentPosition(actor) is { } eye
        && (eye.IsAdjacentOrSame(door) || CanSee(map, actor, eye, door));

    /// <summary>The player remembers the doors it sees now, as they are now. Called at processed instants and after commands only.</summary>
    internal void RememberDoors()
    {
        var player = World.Actors[World.Player];
        if (player.MapArea is not { } area || !World.Maps.TryGetValue(area, out var map))
            return;
        foreach (var (at, open) in map.Doors)
            if (SeesDoor(player, map, at))
                player.DoorMemory[new DoorKey(area, at)] = new RememberedDoor(open, World.Now);
        // The explored floor is remembered here, with the doors, never when a view is read.
        if (map.UsesExploration && player.Conscious && CurrentPosition(player) is { } eye)
            map.Explored.UnionWith(SquaresSeen(map, player, eye, onlyUnexplored: true).ToList());
    }

    /// <summary>
    /// Terrain currently perceivable from <paramref name="eye"/>. Even without light, the square underfoot and
    /// immediately surrounding floor can be made out; a wall still blocks it. This affects only map exploration:
    /// recognising actors and objects continues to use <see cref="CanSee"/> and its light requirement.
    /// </summary>
    internal IEnumerable<GridPos> SquaresSeen(GridMap map, Actor watcher, GridPos eye, bool onlyUnexplored = false)
    {
        if (Asleep(watcher))
            yield break; // R1
        var daylight = Perception.DaylightAt(World.Now);
        var torches = CarriedLights(map);
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var square = new GridPos(x, y);
            if (onlyUnexplored && map.Explored.Contains(square))
                continue;
            if (!map.HasLineOfSight(eye, square))
                continue;
            if (!InFacing(watcher, CurrentSpot(watcher) ?? Spot.CenterOf(eye), Spot.CenterOf(square)))
                continue;
            if (Math.Max(Math.Abs(x - eye.X), Math.Abs(y - eye.Y)) <= 1
                || Max(map.DaylightOn(square, daylight), map.SourceLight(square, torches)) >= Light.Dim)
                yield return square;
        }
    }

    /// <summary>The player knows the state of a door it opens or closes itself, even passing through in the dark.</summary>
    private void Touched(Actor actor, GridMap map, GridPos door)
    {
        if (actor.IsPlayer)
            actor.DoorMemory[new DoorKey(map.Area, door)] = new RememberedDoor(map.Doors[door], World.Now);
    }
}
