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
        if (actor.CurrentAction is not null and not MoveAction)
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

        // A new order replaces the walk in progress: stop on the square reached, then set off from there.
        if (actor.CurrentAction is MoveAction)
            CancelAction(actor);

        // Sneaking: SRD Slow pace and one Stealth check for as long as the actor keeps sneaking (no reroll per walk).
        var speed = command.Stealthy ? Math.Max(1, actor.Sheet.Speed * 2 / 3) : actor.Sheet.Speed;
        D20Roll? stealth = null;
        if (!command.Stealthy)
            actor.Sneak = null;
        else if (actor.Sneak is null)
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

        // The only instants that matter on the way: entering or leaving a place (the arrival is the completion), and the
        // square before each door, where the walker opens it if it is closed (free, SRD). A door next to the start is
        // opened at once.
        if (map.IsClosedDoor(path[0]))
            OpenDoorOnTheWay(actor, map, path[0]);
        var zone = map.ZoneAt(from);
        for (var step = 1; step < path.Count; step++)
        {
            var next = map.ZoneAt(path[step - 1]);
            var door = map.IsDoor(path[step]);
            if (next == zone && !door)
                continue;
            zone = next;
            World.Scheduler.Schedule(move.StartedAt.Plus(Duration.FromSeconds(MoveAction.SecondsFor(step, move.Speed))), new MoveWaypoint(move.Id, step));
        }
        var how = stealth is null ? "" : $" Furtività: {stealth.Describe()}.";
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
    private void Reach(Actor actor, GridPos square)
    {
        actor.Position = square;
        var map = World.Maps[actor.MapArea!.Value];
        if (TorchLit(actor))
            NoteLightChange(map); // the light moved with its bearer
        var zone = map.ZoneAt(square);
        if (zone == actor.Location)
            return;
        actor.Location = zone;
        actor.ArrivedAt = World.Now;
        if (zone is not null)
            NoticeArrival(actor);
    }

    private void RunWaypoint(MoveWaypoint waypoint)
    {
        var actor = World.Actors.Values.FirstOrDefault(a => a.CurrentAction?.Id == waypoint.Action);
        if (actor?.CurrentAction is not MoveAction move)
            return;
        Reach(actor, move.Path[waypoint.Step - 1]);
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
        return actor.MapArea == area && CurrentPosition(actor) is { } here && here != at
            && here.IsAdjacentOrSame(at) && OpenDiagonal(World.Maps[area], here, at);
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
