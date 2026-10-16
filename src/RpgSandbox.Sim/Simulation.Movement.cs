using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// Walking on area maps (T6a). Movement follows the SRD grid rules: Speed/5 squares per 6-second round, so a
// Speed of 30 is one square per second. The Sim only moves when advanced; the client decides how time flows.
internal sealed partial class Simulation
{
    /// <summary>Where an actor stands right now: mid-walk, the last square fully reached.</summary>
    internal GridPos? CurrentPosition(Actor actor) =>
        actor.CurrentAction is MoveAction move ? move.PositionAt(World.Now) : actor.Position;

    private GridMap? MapOf(Actor actor) => actor.MapArea is { } area ? World.Maps[area] : null;

    private CommandResult StartMove(MoveCommand command)
    {
        // Validate everything before touching anything, including a walk already in progress.
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        // A walk replaces a walk in progress or an interruptible routine wait (an NPC idling); anything else is busy.
        if (actor.CurrentAction is not null and not MoveAction and not WaitAction { Interruptible: true })
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        // Review P1c: a walker half-way is already on the square of its exact point at this tick; the new way sets off
        // from there, not from the last centre it reached.
        if (CurrentSpot(actor) is not { } here || MapOf(actor) is not { } map)
            return CommandResult.Rejected(RejectionReason.NoMap, $"Qui non c'è una mappa su cui camminare.");
        var from = here.Cell;
        if (from == command.To)
        {
            // Off the centre of its own square, an actor goes back to the centre, as a free walk there.
            if (here == Spot.CenterOf(from) || command.StopNextTo)
                return CommandResult.Rejected(RejectionReason.AlreadyThere, $"{actor.Name} è già lì.");
            return StartMoveToSpot(new MoveToSpotCommand { Actor = actor.Id, To = Spot.CenterOf(from), Stealthy = command.Stealthy });
        }
        if (map.FindPath(from, command.To, command.Stealthy ? square => SneakCost(map, square) : null, Opens(actor, map)) is not { } path)
            return CommandResult.Rejected(RejectionReason.Unreachable, "Non si può raggiungere quel punto.");
        if (command.StopNextTo)
        {
            // The last step of a path is always a legal move, so the square before it is next to the goal.
            if (path.Count == 1)
                return CommandResult.Rejected(RejectionReason.AlreadyThere, $"{actor.Name} è già lì accanto.");
            path = path.Take(path.Count - 1).ToList();
        }

        // A new order replaces the walk in progress (stop on the square reached, then set off from there) or the idling.
        if (actor.CurrentAction is MoveAction or WaitAction { Interruptible: true })
            CancelAction(actor);

        return SetOff(actor, map, from, null, path, null, command.Stealthy);
    }

    /// <summary>
    /// P1 (free movement prototype): walks to an exact point, outside a fight, along the shortest way for its body
    /// (<see cref="SpotPlanner"/>). Hostile creatures block the way; nobody can be stood on at the end.
    /// </summary>
    private CommandResult StartMoveToSpot(MoveToSpotCommand command)
    {
        var (rejected, way) = PlanSpotWalk(command.Actor, command.To);
        if (rejected is not null)
            return rejected;
        var actor = World.Actors[command.Actor];
        var from = CurrentSpot(actor)!.Value;
        if (actor.CurrentAction is MoveAction or WaitAction { Interruptible: true })
            CancelAction(actor);
        return SetOff(actor, MapOf(actor)!, CurrentPosition(actor)!.Value, from, null, way, command.Stealthy);
    }

    /// <summary>P1: what a <see cref="MoveToSpotCommand"/> would do, by the same code, without touching the world.</summary>
    internal SpotMovePreview PreviewMoveToSpot(ActorId actor, Spot to, bool stealthy)
    {
        if (World.Actors.TryGetValue(actor, out var mover) && mover.Conscious && EncounterOf(mover) is { } fight)
            return stealthy
                ? new SpotMovePreview { Valid = false, Reason = RejectionReason.InCombat, Message = "Durante uno scontro non puoi muoverti di soppiatto." }
                : PreviewFreeCombatMove(fight, mover, to);
        return PreviewOutsideFights(actor, to, stealthy);
    }

    private SpotMovePreview PreviewOutsideFights(ActorId actor, Spot to, bool stealthy)
    {
        var (rejected, way) = PlanSpotWalk(actor, to);
        if (rejected is not null)
            return new SpotMovePreview { Valid = false, Reason = rejected.Rejection, Message = rejected.Message };
        var speed = stealthy ? Math.Max(1, World.Actors[actor].Sheet.Speed * 2 / 3) : World.Actors[actor].Sheet.Speed;
        return new SpotMovePreview
        {
            Valid = true, Way = way!.Vertices, Feet = Math.Ceiling(way.Length / Spot.PerFoot * 10 - 1e-9) / 10,
            Ticks = way.Checkpoints(speed)[^1].Ticks,
        };
    }

    /// <summary>P1: the way to <paramref name="to"/>, or why there is none.</summary>
    private (CommandResult? Rejected, SpotPath? Way) PlanSpotWalk(ActorId actorId, Spot to)
    {
        if (!World.Actors.TryGetValue(actorId, out var actor))
            return (CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {actorId}."), null);
        if (!actor.Conscious)
            return (CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} non può muoversi."), null);
        if (EncounterOf(actor) is not null)
            return (CommandResult.Rejected(RejectionReason.InCombat, $"{actor.Name} sta combattendo."), null);
        if (actor.CurrentAction is not null and not MoveAction and not WaitAction { Interruptible: true })
            return (CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato."), null);
        if (CurrentSpot(actor) is not { } from || MapOf(actor) is not { } map)
            return (CommandResult.Rejected(RejectionReason.NoMap, "Qui non c'è una mappa su cui camminare."), null);
        if (from == to)
            return (CommandResult.Rejected(RejectionReason.AlreadyThere, $"{actor.Name} è già lì."), null);
        var planner = PlannerFor(actor, map);
        if (!planner.Fits(to))
            return (CommandResult.Rejected(RejectionReason.NoRoom, "Lì non c'è spazio per stare in piedi."), null);
        return planner.Find(from, to) is { } way
            ? (null, way)
            : (CommandResult.Rejected(RejectionReason.Unreachable, "Non si può raggiungere quel punto."), null);
    }

    /// <summary>
    /// P1: the way an actor's body may take on its map. Conscious creatures hostile to it, or that it holds hostile, block
    /// it; every other creature standing (or about to stop) on the map is somewhere it cannot stop.
    /// </summary>
    private SpotPlanner PlannerFor(Actor actor, GridMap map)
    {
        var others = World.Actors.Values
            .Where(o => o.Id != actor.Id && o.MapArea == map.Area && o.Vitality.State != LifeState.Dead && CurrentSpot(o) is not null)
            .ToList();
        var blockers = others.Where(o => o.Conscious && (HoldsHostile(o, actor) || HoldsHostile(actor, o))).Select(o => CurrentSpot(o)!.Value);
        var standing = others.Select(o => CurrentSpot(o)!.Value)
            .Concat(others.Select(o => o.CurrentAction as MoveAction).OfType<MoveAction>().Select(FinalSpot));
        return new SpotPlanner(map, blockers, standing, barredDoors: BarredDoors(actor, map)); // P4a
    }

    /// <summary>P1: where an actor stands right now, exactly: mid-walk, its point on its way at this tick (P1c).</summary>
    internal Spot? CurrentSpot(Actor actor) =>
        actor.CurrentAction is MoveAction move ? move.SpotAt(World.Now) : actor.Spot;

    private static Spot FinalSpot(MoveAction move) => move.Spots?[^1] ?? Spot.CenterOf(move.Path[^1]);

    /// <summary>
    /// Sets a walk going, from square to square (<paramref name="path"/>) or, P1, along a free way (<paramref name="way"/>,
    /// checked at the points <see cref="SpotPath.Checkpoints"/> gives).
    /// </summary>
    private CommandResult SetOff(Actor actor, GridMap map, GridPos from, Spot? fromSpot, IReadOnlyList<GridPos>? path, SpotPath? way, bool stealthy)
    {
        // Sneaking: SRD Slow pace and one Stealth check for as long as the actor keeps sneaking (no reroll per walk).
        // Hiding anew takes being out of everyone's sight (SRD Hide): watched, the actor only goes slowly.
        var speed = stealthy ? Math.Max(1, actor.Sheet.Speed * 2 / 3) : actor.Sheet.Speed;
        D20Roll? stealth = null;
        var watchedBy = new List<string>();
        if (!stealthy)
            actor.Sneak = null;
        else if (actor.Sneak is null && (watchedBy = WhoSeesOnMap(actor).Select(id => World.Actors[id].Name).Order(StringComparer.Ordinal).ToList()).Count == 0)
        {
            stealth = D20.Roll(World.Rng, actor.Sheet.Bonus(Skill.Stealth), disadvantage: actor.Sheet.StealthDisadvantage);
            actor.Sneak = stealth.Total;
            World.RecordFact("Roll", $"{actor.Name} si muove di soppiatto, Furtività: {stealth.Describe()}.", actor.Id);
        }

        var checkpoints = way?.Checkpoints(speed);
        var steps = path ?? checkpoints!.Select(c => c.Cell).ToList();
        var move = new MoveAction
        {
            Id = World.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = World.Now,
            CompletesAt = World.Now.Plus(Duration.FromTicks(checkpoints is null ? MoveAction.TicksFor(steps.Count, speed) : checkpoints[^1].Ticks)),
            From = from,
            Path = steps,
            Speed = speed,
            Stealthy = stealthy,
            FromSpot = fromSpot ?? CurrentSpot(actor),
            Vertices = way?.Vertices,
            Spots = checkpoints?.Select(c => c.At).ToList(),
            Ticks = checkpoints?.Select(c => c.Ticks).ToList(),
            Description = stealthy ? "Si muove di soppiatto" : "Cammina",
        };
        Begin(actor, move);

        if (actor.IsPlayer && actor.Facing is not null)
        {
            var start = move.FromSpot ?? Spot.CenterOf(from);
            Spot? ahead = move.Vertices?.Where(p => p != start).Select(p => (Spot?)p).FirstOrDefault()
                ?? move.Spots?.Where(p => p != start).Select(p => (Spot?)p).FirstOrDefault()
                ?? steps.Select(Spot.CenterOf).Where(p => p != start).Select(p => (Spot?)p).FirstOrDefault();
            if (ahead is { } next)
                FacePlayer(new GridPos(next.X - start.X, next.Y - start.Y));
        }

        // T6c-1: a waypoint on every square of the way (the arrival is the completion), where contacts are evaluated and
        // the door ahead, if closed, is opened (free, SRD). A door next to the start is opened at once.
        if (map.ClosedDoorBetween(from, steps[0]) is { } firstDoor)
            OpenDoorOnTheWay(actor, map, firstDoor, move, crossingTo: 0);
        for (var step = 1; step < steps.Count; step++)
            World.Scheduler.Schedule(move.StartedAt.Plus(Duration.FromTicks(move.TicksTo(step))), new MoveWaypoint(move.Id, step));
        var how = stealth is not null ? $" Furtività: {stealth.Describe()}."
            : watchedBy.Count > 0 ? $" Ti vede {string.Join(", ", watchedBy)}: vai piano, ma non puoi nasconderti." : "";
        return CommandResult.Started(move.Id, move.CompletesAt,
            stealthy ? $"{actor.Name} avanza di soppiatto.{how}" : $"{actor.Name} si incammina.");
    }

    private CommandResult Stop(StopCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (actor.CurrentAction is RestAction)
        {
            CancelAction(actor, "si alza"); // R1
            return new CommandResult { Message = $"{actor.Name} smette di riposare." };
        }
        if (actor.CurrentAction is not MoveAction)
            return CommandResult.Rejected(RejectionReason.NotMoving, $"{actor.Name} non sta camminando.");
        CancelAction(actor);
        return new CommandResult { Message = $"{actor.Name} si ferma." };
    }

    /// <summary>The walker reaches a square where its place changes (or its destination).</summary>
    /// <summary>Actors placed on a new square during this instant, with the zone each was in before its first move.</summary>
    private readonly Dictionary<ActorId, (Actor Actor, LocationId? ZoneBefore)> _moved = new();

    /// <summary>
    /// Moves an actor's recorded square (and zone), nothing else. Contacts are evaluated by <see cref="EvaluateContacts"/>
    /// once every position of the instant is known, so they never depend on the order walkers were processed in.
    /// </summary>
    private void Place(Actor actor, GridPos square, Spot? spot = null)
    {
        _moved.TryAdd(actor.Id, (actor, actor.Location)); // the zone at the start of the instant, kept on later moves
        var previous = actor.Spot;
        var arrival = spot ?? Spot.CenterOf(square);
        if (actor.Facing is not null && previous is { } from && from != arrival)
            actor.Facing = new GridPos(arrival.X - from.X, arrival.Y - from.Y);
        if (spot is { } exact)
            actor.PlaceAt(exact); // P1: a free walk's checkpoint, in that square
        else
            actor.Position = square;
        actor.Location = World.Maps[actor.MapArea!.Value].ZoneAt(square);
    }

    /// <summary>Places an actor and evaluates the contacts at once (moves outside the per-instant phases, e.g. a stop).</summary>
    private void Reach(Actor actor, GridPos square, Spot? spot = null)
    {
        Place(actor, square, spot);
        EvaluateContacts();
    }

    /// <summary>
    /// T6c-1: what follows from the positions of this instant, evaluated once, in actor ID order: light carried by
    /// torch bearers (thefts in progress), arrivals in a place, then, on every map where someone moved, hidden actors
    /// found by anyone who now sees them (the walker, or the one it walked past).
    /// </summary>
    private void EvaluateContacts()
    {
        if (_moved.Count == 0)
            return;
        // Only the zone at the end of the instant counts against the one at its start: passing through a place and back
        // within the same second (a fast walker) is no arrival.
        var moved = _moved.Values.OrderBy(m => m.Actor.Id.Value, StringComparer.Ordinal).ToList();
        _moved.Clear();
        foreach (var area in moved.Select(m => m.Actor.MapArea).OfType<AreaId>().Distinct().OrderBy(a => a.Value, StringComparer.Ordinal))
            NoteSensesChange(World.Maps[area]); // sight and carried light changed with the walkers (T6c-5)
        foreach (var (actor, zoneBefore) in moved)
        {
            if (actor.Location == zoneBefore)
                continue;
            actor.ArrivedAt = World.Now; // on maps the meeting that matters is contact, not entering a zone (T6c-4)
        }
        foreach (var area in moved.Select(m => m.Actor.MapArea).OfType<AreaId>().Distinct().OrderBy(a => a.Value, StringComparer.Ordinal))
            NoticeContacts(area);
        foreach (var area in moved.Select(m => m.Actor.MapArea).OfType<AreaId>().Distinct().OrderBy(a => a.Value, StringComparer.Ordinal))
        foreach (var hidden in World.Actors.Values.Where(a => a.MapArea == area && a.Sneak is not null)
                     .OrderBy(a => a.Id.Value, StringComparer.Ordinal).ToList())
            CheckDiscovered(hidden);
    }

    private void RunWaypoint(MoveWaypoint waypoint)
    {
        var actor = World.Actors.Values.FirstOrDefault(a => a.CurrentAction?.Id == waypoint.Action);
        if (actor?.CurrentAction is not MoveAction move)
            return;
        Place(actor, move.Path[waypoint.Step - 1], move.Spots?[waypoint.Step - 1]);
        var map = World.Maps[actor.MapArea!.Value];
        RelockBehind(actor, map, move, reached: waypoint.Step - 1);
        if (map.ClosedDoorBetween(move.Path[waypoint.Step - 1], move.Path[waypoint.Step]) is { } nextDoor)
            OpenDoorOnTheWay(actor, map, nextDoor, move, crossingTo: waypoint.Step);
    }

    /// <summary>
    /// Opens the closed door a walker comes to (free, SRD). P4a: a locked one opens with the walker's key and is locked
    /// again once it is past (<see cref="RelockBehind"/>); one it cannot open (locked meanwhile, or stuck) stops it here.
    /// </summary>
    private void OpenDoorOnTheWay(Actor actor, GridMap map, GridPos door, MoveAction move, int crossingTo)
    {
        if (map.BarrierOf(BarrierKey.Door(door)) is { Holds: true } barrier)
        {
            if (!Opens(actor, map)(door))
            {
                Learn(actor, map, BarrierKey.Door(door), barrier);
                World.RecordFact(barrier.Locked ? "FoundLocked" : "FoundStuck",
                    $"{actor.Name} trova la porta {(barrier.Locked ? "chiusa a chiave" : "bloccata")} e si ferma.", actor.Id);
                CancelAction(actor);
                return;
            }
            barrier.Locked = false;
            World.RecordFact("DoorUnlocked", $"{actor.Name} apre con la chiave la porta in {door}.", actor.Id);
            // Past an edge door once on the square beyond it; past a door square once off it, one step further.
            // An NPC leaves it as it found it; the player decides for itself (LockCommand).
            if (!actor.IsPlayer)
                actor.PendingRelock = new PendingRelock(door, move.Id, map.IsEdgeDoor(door) ? crossingTo : crossingTo + 1);
        }
        NoteSensesChange(map);
        map.SetDoor(door, open: true);
        Touched(actor, map, door);
        NoteSensesChange(map);
        World.RecordFact("DoorOpened", $"{actor.Name} apre la porta in {door}.", actor.Id);
    }

    private CommandResult UseDoor(DoorCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (actor.CurrentAction is not null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        if (MapOf(actor) is not { } map || CurrentPosition(actor) is not { } here)
            return CommandResult.Rejected(RejectionReason.NoMap, "Qui non ci sono porte.");
        if (!map.IsDoor(command.At))
            return CommandResult.Rejected(RejectionReason.NotADoor, "Lì non c'è una porta.");
        if ((!map.IsEdgeDoor(command.At) && here == command.At) || !here.IsAdjacentOrSame(command.At)
            || here != command.At && !OpenDiagonal(map, here, command.At))
            return CommandResult.Rejected(RejectionReason.OutOfReach, "La porta è troppo lontana: avvicinati.");
        if (map.Doors[command.At] == command.Open)
            return CommandResult.Rejected(RejectionReason.AlreadyDone, command.Open ? "La porta è già aperta." : "La porta è già chiusa.");
        // Nobody can be standing in a closing door, nor be about to step into it.
        if (!command.Open && !map.IsEdgeDoor(command.At) && World.Actors.Values.Any(a => a.MapArea == map.Area
                && (CurrentPosition(a) == command.At || a.CurrentAction is MoveAction m && NextSquare(m) == command.At)))
            return CommandResult.Rejected(RejectionReason.DoorBlocked, "Qualcuno è sulla porta.");

        if (command.Open && OpensByHand(actor, map, BarrierKey.Door(command.At)) is { } held) // P4a: locked or stuck
            return held;
        NoteSensesChange(map);
        map.SetDoor(command.At, command.Open);
        Touched(actor, map, command.At);
        NoteSensesChange(map);
        World.RecordFact(command.Open ? "DoorOpened" : "DoorClosed",
            $"{actor.Name} {(command.Open ? "apre" : "chiude")} la porta in {command.At}.", actor.Id);
        return new CommandResult { Message = command.Open ? "Apri la porta." : "Chiudi la porta." };
    }

    /// <summary>
    /// P2b: opens or shuts a window, from one of its two squares with the body within 5 feet of it (reach, edge of the
    /// body to the window). Outside fights, like a door; sight and light do not change, only shots and blows.
    /// </summary>
    private CommandResult UseWindow(WindowCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (actor.CurrentAction is not null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        if (MapOf(actor) is not { } map || CurrentSpot(actor) is not { } here)
            return CommandResult.Rejected(RejectionReason.NoMap, "Qui non ci sono finestre.");
        if (!map.IsWindow(command.A, command.B))
            return CommandResult.Rejected(RejectionReason.NotAWindow, "Lì non c'è una finestra.");
        var (p, q) = SpotPlanner.Border(command.A, command.B);
        if (here.Cell != command.A && here.Cell != command.B
            || SpotPlanner.PointSegment(here, p, q) - SpotPlanner.BodyRadius > 5 * Spot.PerFoot)
            return CommandResult.Rejected(RejectionReason.OutOfReach, "La finestra è troppo lontana: avvicinati.");
        if (map.GlassOf(command.A, command.B) != WindowGlass.Intact) // P4b
            return CommandResult.Rejected(RejectionReason.Broken, "Il vetro è rotto: la finestra resta aperta.");
        if (map.IsOpenWindow(command.A, command.B) == command.Open)
            return CommandResult.Rejected(RejectionReason.AlreadyDone, command.Open ? "La finestra è già aperta." : "La finestra è già chiusa.");
        if (command.Open && OpensByHand(actor, map, BarrierKey.Window(command.A, command.B)) is { } held) // P4a: locked or stuck
            return held;

        map.SetWindow(command.A, command.B, command.Open);
        World.RecordFact(command.Open ? "WindowOpened" : "WindowClosed",
            $"{actor.Name} {(command.Open ? "apre" : "chiude")} la finestra fra {command.A} e {command.B}.", actor.Id);
        return new CommandResult { Message = command.Open ? "Apri la finestra." : "Chiudi la finestra." };
    }

    /// <summary>The square a walker steps into next, or null once it has arrived.</summary>
    private GridPos? NextSquare(MoveAction move)
    {
        var reached = move.StepsAt(World.Now);
        return reached < move.Path.Count ? move.Path[reached] : null;
    }

    /// <summary>
    /// Within reach for using a store: on a mapped area, the square next to the store's; elsewhere, the same place.
    /// </summary>
    private bool InReach(Actor actor, Store store)
    {
        if (store.Position is not { } at)
            return actor.Location is not null && actor.Location == store.Location;
        var area = World.Locations[store.Location].Area;
        if (actor.MapArea != area || CurrentPosition(actor) is not { } here)
            return false;
        // T6c: a store with declared access squares is used only from them (not from behind it).
        return store.Access.Count > 0
            ? store.Access.Contains(here)
            : here != at && here.IsAdjacentOrSame(at) && OpenDiagonal(World.Maps[area], here, at);
    }

    /// <summary>Close enough to talk: adjacent squares on a mapped area, otherwise the same place.</summary>
    internal bool CanTalk(Actor a, Actor b)
    {
        if (CurrentPosition(a) is { } pa && CurrentPosition(b) is { } pb)
            return a.MapArea is { } area && b.MapArea == area && pa.IsAdjacentOrSame(pb)
                && OpenDiagonal(World.Maps[area], pa, pb);
        return a.Location is not null && a.Location == b.Location;
    }

    /// <summary>A diagonal interaction cannot pass through the corner of two blocked squares.</summary>
    private static bool OpenDiagonal(GridMap map, GridPos from, GridPos to) => map.DiagonalOpen(from, to);
}
