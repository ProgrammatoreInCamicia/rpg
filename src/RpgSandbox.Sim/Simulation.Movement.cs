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
        if (CurrentPosition(actor) is not { } from || MapOf(actor) is not { } map)
            return CommandResult.Rejected(RejectionReason.NoMap, $"Qui non c'è una mappa su cui camminare.");
        if (from == command.To)
            return CommandResult.Rejected(RejectionReason.AlreadyThere, $"{actor.Name} è già lì.");
        if (map.FindPath(from, command.To, command.Stealthy ? square => SneakCost(map, square) : null) is not { } path)
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

        // Sneaking: SRD Slow pace and one Stealth check for as long as the actor keeps sneaking (no reroll per walk).
        // Hiding anew takes being out of everyone's sight (SRD Hide): watched, the actor only goes slowly.
        var speed = command.Stealthy ? Math.Max(1, actor.Sheet.Speed * 2 / 3) : actor.Sheet.Speed;
        D20Roll? stealth = null;
        var watchedBy = new List<string>();
        if (!command.Stealthy)
            actor.Sneak = null;
        else if (actor.Sneak is null && (watchedBy = WhoSeesOnMap(actor).Select(id => World.Actors[id].Name).Order(StringComparer.Ordinal).ToList()).Count == 0)
        {
            stealth = D20.Roll(World.Rng, actor.Sheet.Bonus(Skill.Stealth), disadvantage: actor.Sheet.StealthDisadvantage);
            actor.Sneak = stealth.Total;
            World.RecordFact("Roll", $"{actor.Name} si muove di soppiatto, Furtività: {stealth.Describe()}.", actor.Id);
        }

        var move = new MoveAction
        {
            Id = World.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = World.Now,
            CompletesAt = World.Now.Plus(Duration.FromSeconds(MoveAction.SecondsFor(path.Count, speed))),
            From = from,
            Path = path,
            Speed = speed,
            Stealthy = command.Stealthy,
            Description = command.Stealthy ? "Si muove di soppiatto" : "Cammina",
        };
        Begin(actor, move);

        // T6c-1: a waypoint on every square of the way (the arrival is the completion), where contacts are evaluated and
        // the door ahead, if closed, is opened (free, SRD). A door next to the start is opened at once.
        if (map.IsClosedDoor(path[0]))
            OpenDoorOnTheWay(actor, map, path[0]);
        for (var step = 1; step < path.Count; step++)
            World.Scheduler.Schedule(move.StartedAt.Plus(Duration.FromSeconds(MoveAction.SecondsFor(step, move.Speed))), new MoveWaypoint(move.Id, step));
        var how = stealth is not null ? $" Furtività: {stealth.Describe()}."
            : watchedBy.Count > 0 ? $" Ti vede {string.Join(", ", watchedBy)}: vai piano, ma non puoi nasconderti." : "";
        return CommandResult.Started(move.Id, move.CompletesAt,
            command.Stealthy ? $"{actor.Name} avanza di soppiatto.{how}" : $"{actor.Name} si incammina.");
    }

    private CommandResult Stop(StopCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
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
    private void Place(Actor actor, GridPos square)
    {
        _moved.TryAdd(actor.Id, (actor, actor.Location)); // the zone at the start of the instant, kept on later moves
        actor.Position = square;
        actor.Location = World.Maps[actor.MapArea!.Value].ZoneAt(square);
    }

    /// <summary>Places an actor and evaluates the contacts at once (moves outside the per-instant phases, e.g. a stop).</summary>
    private void Reach(Actor actor, GridPos square)
    {
        Place(actor, square);
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
        foreach (var (actor, zoneBefore) in moved)
        {
            if (actor.MapArea is { } area && TorchLit(actor))
                NoteLightChange(World.Maps[area]); // the light moved with its bearer
            if (actor.Location == zoneBefore)
                continue;
            actor.ArrivedAt = World.Now;
            if (actor.Location is not null)
                NoticeArrival(actor);
        }
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
        Place(actor, move.Path[waypoint.Step - 1]);
        var map = World.Maps[actor.MapArea!.Value];
        if (map.IsClosedDoor(move.Path[waypoint.Step]))
            OpenDoorOnTheWay(actor, map, move.Path[waypoint.Step]);
    }

    private void OpenDoorOnTheWay(Actor actor, GridMap map, GridPos door)
    {
        NoteLightChange(map);
        map.SetDoor(door, open: true);
        NoteLightChange(map);
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
        if (here == command.At || !here.IsAdjacentOrSame(command.At) || !OpenDiagonal(map, here, command.At))
            return CommandResult.Rejected(RejectionReason.OutOfReach, "La porta è troppo lontana: avvicinati.");
        if (map.Doors[command.At] == command.Open)
            return CommandResult.Rejected(RejectionReason.AlreadyDone, command.Open ? "La porta è già aperta." : "La porta è già chiusa.");
        // Nobody can be standing in a closing door, nor be about to step into it.
        if (!command.Open && World.Actors.Values.Any(a => a.MapArea == map.Area
                && (CurrentPosition(a) == command.At || a.CurrentAction is MoveAction m && NextSquare(m) == command.At)))
            return CommandResult.Rejected(RejectionReason.DoorBlocked, "Qualcuno è sulla porta.");

        NoteLightChange(map);
        map.SetDoor(command.At, command.Open);
        NoteLightChange(map);
        World.RecordFact(command.Open ? "DoorOpened" : "DoorClosed",
            $"{actor.Name} {(command.Open ? "apre" : "chiude")} la porta in {command.At}.", actor.Id);
        return new CommandResult { Message = command.Open ? "Apri la porta." : "Chiudi la porta." };
    }

    /// <summary>The square a walker steps into next, or null once it has arrived.</summary>
    private GridPos? NextSquare(MoveAction move)
    {
        var elapsed = World.Now.Seconds - move.StartedAt.Seconds;
        var reached = (int)Math.Min(move.Path.Count, elapsed * move.Speed / 30);
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
    private static bool OpenDiagonal(GridMap map, GridPos from, GridPos to) =>
        from.X == to.X || from.Y == to.Y ||
        (map.IsWalkable(new GridPos(to.X, from.Y)) && map.IsWalkable(new GridPos(from.X, to.Y)));
}
