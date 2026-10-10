using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

// Walking on area maps (T6a). Movement follows the SRD grid rules: Speed/5 squares per 6-second round, so a
// Speed of 30 is one square per second. Time passes only while walking: stopping stops the clock.
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
        if (map.FindPath(from, command.To) is not { } path)
            return CommandResult.Rejected(RejectionReason.Unreachable, "Non si può raggiungere quel punto.");

        // A new order replaces the walk in progress: stop on the square reached, then set off from there.
        if (actor.CurrentAction is MoveAction)
            CancelAction(actor);

        var move = new MoveAction
        {
            Id = World.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = World.Now,
            CompletesAt = World.Now.Plus(Duration.FromSeconds(MoveAction.SecondsFor(path.Count, actor.Sheet.Speed))),
            From = from,
            Path = path,
            Speed = actor.Sheet.Speed,
            Description = "Cammina",
        };
        Begin(actor, move);

        // The only instants that matter on the way: entering or leaving a place (the arrival is the completion).
        var zone = map.ZoneAt(from);
        for (var step = 1; step < path.Count; step++)
        {
            var next = map.ZoneAt(path[step - 1]);
            if (next == zone)
                continue;
            zone = next;
            World.Scheduler.Schedule(move.StartedAt.Plus(Duration.FromSeconds(MoveAction.SecondsFor(step, move.Speed))), new MoveWaypoint(move.Id, step));
        }
        return CommandResult.Started(move.Id, move.CompletesAt, $"{actor.Name} si incammina.");
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
        if (actor?.CurrentAction is MoveAction move)
            Reach(actor, move.Path[waypoint.Step - 1]);
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
