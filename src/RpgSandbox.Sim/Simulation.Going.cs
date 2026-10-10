using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

// T6c-2: how an NPC gets somewhere, one step at a time, on maps and along Routes alike. Every rule that sends an NPC
// to a place goes through here, so walking, leaving a map by its exit and journeys all follow the same logic.
internal sealed partial class Simulation
{
    /// <summary>
    /// The square an NPC standing on <paramref name="from"/> heads for in a place on a map: the post of the given kind (for
    /// Home without a Home post, the Rest post), if it can be reached; otherwise the first square of the place in reading
    /// order that can be reached. Null when there is no way there (a place cut off by walls): the NPC then hesitates and
    /// decides again later, instead of failing.
    /// </summary>
    private static GridPos? TargetSquare(GridMap map, LocationId place, PostKind? kind, GridPos from)
    {
        if (PostOf(map, place, kind) is { } post)
            return Reachable(map, from, post) ? post : null;
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var square = new GridPos(x, y);
            if (map.ZoneAt(square) == place && map.IsWalkable(square) && Reachable(map, from, square))
                return square;
        }
        return null;
    }

    private static bool Reachable(GridMap map, GridPos from, GridPos to) => from == to || map.FindPath(from, to) is not null;

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
                if (IsAt(npc, place, kind) || CurrentPosition(npc) is not { } standing)
                    return null;
                return TargetSquare(map, place, kind, standing) is { } target ? new MoveCommand { Actor = npc.Id, To = target } : null;
            }

            // Leave by the exit that makes the rest of the way shortest, among those that can be walked to from here.
            var from = CurrentPosition(npc)!.Value;
            var best = map.Exits
                .Where(exit => Reachable(map, from, exit.Value))
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

    // ---------------------------------------------------------------- T6c-3: journeys to stores, guards on maps

    /// <summary>
    /// The cost of a journey from a place to another: along the Routes, to the place itself or, if it lies on a mapped
    /// area, to that map's most convenient exit (the walk on the map does not count). Zero within the same mapped area.
    /// </summary>
    private Duration? JourneyCost(LocationId from, LocationId to)
    {
        if (from == to || (World.Locations[from].Area == World.Locations[to].Area && World.Maps.ContainsKey(World.Locations[to].Area)))
            return Duration.Zero;
        return JourneyTargets(to)
            .Select(t => t == from ? Duration.Zero : PathCost(from, t))
            .Where(c => c is not null)
            .OrderBy(c => c!.Value.Seconds)
            .FirstOrDefault();
    }

    /// <summary>The squares a store on a map is used from: its declared access, or the adjacent squares with an open diagonal.</summary>
    private static IEnumerable<GridPos> UseSquares(Store store, GridMap map)
    {
        if (store.Access.Count > 0)
            return store.Access;
        var at = store.Position!.Value;
        return Enumerable.Range(-1, 3).SelectMany(dy => Enumerable.Range(-1, 3).Select(dx => new GridPos(at.X + dx, at.Y + dy)))
            .Where(p => p != at && map.IsWalkable(p) && map.DiagonalOpen(p, at));
    }

    /// <summary>
    /// The next step towards using a store: on its map, a walk to the nearest square it is used from; from elsewhere,
    /// the way to its place (onto the map by an exit). Null when in reach already, or with no way there.
    /// <paramref name="stealthy"/>: walks on maps are made sneaking.
    /// </summary>
    private Command? StepToStore(Actor npc, Store store, bool stealthy)
    {
        if (InReach(npc, store))
            return null;
        var area = World.Locations[store.Location].Area;
        if (store.Position is not null && npc.MapArea == area && CurrentPosition(npc) is { } from)
        {
            var map = World.Maps[area];
            var square = UseSquares(store, map)
                .Select(s => (Square: s, Path: map.FindPath(from, s)))
                .Where(c => c.Path is not null)
                .OrderBy(c => c.Path!.Count).ThenBy(c => c.Square.Y).ThenBy(c => c.Square.X)
                .Select(c => (GridPos?)c.Square)
                .FirstOrDefault();
            return square is { } s2 ? new MoveCommand { Actor = npc.Id, To = s2, Stealthy = stealthy } : null;
        }
        return Sneaking(StepTowards(npc, store.Location), stealthy);
    }

    /// <summary>Makes a walk sneaking when asked (journeys and other commands are left as they are).</summary>
    private static Command? Sneaking(Command? step, bool stealthy) =>
        stealthy && step is MoveCommand move ? move with { Stealthy = true } : step;

    /// <summary>
    /// T6c-3, physical guarding on maps: a guard on duty for the store, on a square next to one it is used from, with
    /// a line of sight to it. Being in the same place is not enough.
    /// </summary>
    private bool Covers(Actor guard, Store store)
    {
        if (guard.GuardDuty is not { } duty || duty.Store != store.Id || store.Position is null)
            return false;
        var area = World.Locations[store.Location].Area;
        if (guard.MapArea != area || CurrentPosition(guard) is not { } at)
            return false;
        var map = World.Maps[area];
        return UseSquares(store, map).Any(s => at.IsAdjacentOrSame(s) && map.HasLineOfSight(at, s));
    }

    /// <summary>A guard covering the store that <paramref name="watcher"/> can see right now (F3 rules, light included).</summary>
    private Actor? GuardSeenBy(Actor watcher, Store store)
    {
        if (store.Position is null || watcher.MapArea is not { } area || CurrentPosition(watcher) is not { } eye)
            return null;
        var map = World.Maps[area];
        return World.Actors.Values
            .Where(g => g.Id != watcher.Id && Covers(g, store))
            .OrderBy(g => g.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault(g => CurrentPosition(g) is { } there && Sees(map, watcher, eye, g, there, LightOn(map, there)));
    }
}
