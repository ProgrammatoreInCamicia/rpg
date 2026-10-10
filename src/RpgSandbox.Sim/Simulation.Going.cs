using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

// T6c-2: how an NPC gets somewhere, one step at a time, on maps and along Routes alike. Every rule that sends an NPC
// to a place goes through here, so walking, leaving a map by its exit and journeys all follow the same logic.
internal sealed partial class Simulation
{
    /// <summary>
    /// The square an NPC heads for in a place on a map: the post of the given kind; for Home without a Home post, the
    /// place's Rest post; otherwise the first walkable square of the place in reading order.
    /// </summary>
    private static GridPos TargetSquare(GridMap map, LocationId place, PostKind? kind)
    {
        if (PostOf(map, place, kind) is { } post)
            return post;
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var square = new GridPos(x, y);
            if (map.ZoneAt(square) == place && map.IsWalkable(square))
                return square;
        }
        throw new InvalidOperationException($"Il luogo '{place}' non ha caselle percorribili sulla mappa.");
    }

    /// <summary>
    /// Where the NPC was going: on the place's post when it has one of that kind (or Rest for Home); otherwise anywhere
    /// in the place, on a map as off it (an NPC already somewhere in a place without posts stays where it is).
    /// </summary>
    private bool IsAt(Actor npc, LocationId place, PostKind? kind = null)
    {
        var area = World.Locations[place].Area;
        if (!World.Maps.TryGetValue(area, out var map) || PostOf(map, place, kind) is not { } post)
            return npc.Location == place;
        return npc.MapArea == area && CurrentPosition(npc) == post;
    }

    private static GridPos? PostOf(GridMap map, LocationId place, PostKind? kind) =>
        kind is { } k && map.Posts.TryGetValue((place, k), out var post) ? post
        : kind == PostKind.Home && map.Posts.TryGetValue((place, PostKind.Rest), out var rest) ? rest
        : null;

    /// <summary>
    /// The next step towards a place, or null when already there or there is no way:
    /// on a map, towards a place of the same map, walk to its target square;
    /// on a map, towards another area, walk to the best exit, then set off along its Route;
    /// off the maps, journey along the Routes, aiming at the destination or, if it lies on a mapped area, at the most
    /// convenient exit of that map.
    /// </summary>
    private Command? StepTowards(Actor npc, LocationId place, PostKind? kind = null)
    {
        var destinationArea = World.Locations[place].Area;
        if (npc.MapArea is { } area && World.Maps.TryGetValue(area, out var map))
        {
            if (area == destinationArea)
            {
                if (IsAt(npc, place, kind))
                    return null;
                return new MoveCommand { Actor = npc.Id, To = TargetSquare(map, place, kind) };
            }

            // Leave by the exit that makes the rest of the way shortest.
            var best = map.Exits
                .SelectMany(exit => JourneyTargets(place).Select(t => (Exit: exit, Target: t, Cost: PathCost(exit.Key, t))))
                .Where(c => c.Cost is not null)
                .OrderBy(c => c.Cost!.Value.Seconds).ThenBy(c => c.Exit.Key.Value, StringComparer.Ordinal)
                .ThenBy(c => c.Target.Value, StringComparer.Ordinal)
                .Select(c => ((KeyValuePair<LocationId, GridPos> Exit, LocationId Target)?)(c.Exit, c.Target))
                .FirstOrDefault();
            if (best is not { } way)
                return null;
            if (CurrentPosition(npc) != way.Exit.Value)
                return new MoveCommand { Actor = npc.Id, To = way.Exit.Value };
            return NextHop(way.Exit.Key, way.Target) is { } hop ? new TravelCommand { Actor = npc.Id, Destination = hop } : null;
        }

        if (npc.Location is not { } here)
            return null; // travelling: nothing to decide
        if (here == place)
            return null;
        var aim = JourneyTargets(place)
            .Select(t => (Target: t, Cost: PathCost(here, t)))
            .Where(c => c.Cost is not null)
            .OrderBy(c => c.Cost!.Value.Seconds).ThenBy(c => c.Target.Value, StringComparer.Ordinal)
            .Select(c => (LocationId?)c.Target)
            .FirstOrDefault();
        return aim is { } goal && NextHop(here, goal) is { } next ? new TravelCommand { Actor = npc.Id, Destination = next } : null;
    }

    /// <summary>
    /// Where a journey towards <paramref name="place"/> should end: the place itself, or, if it lies on a mapped area,
    /// that map's exits (the rest of the way is on foot).
    /// </summary>
    private IEnumerable<LocationId> JourneyTargets(LocationId place) =>
        World.Maps.TryGetValue(World.Locations[place].Area, out var map) ? map.Exits.Keys : new[] { place };
}
