using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>
/// The walkable grid of one area. Built from text rows: '#' blocked, '.' open ground outside any zone, a letter an
/// open square inside the zone (place) that letter stands for. Stores occupy a square, which then blocks movement.
/// Doors are the only state that changes (open or closed). The whole map is part of the save, so a game never
/// depends on a map file that may change.
/// </summary>
internal sealed class GridMap
{
    private readonly char[,] _cells;
    private readonly Dictionary<(GridPos, GridPos), MapEdgeView> _edges = new();
    private readonly HashSet<GridPos> _edgeDoorCells = new();
    private readonly HashSet<LocationId> _indoorZones;
    private readonly HashSet<GridPos> _occupied;
    private readonly SortedDictionary<LocationId, GridPos> _exits = new(Comparer<LocationId>.Create((a, b) => string.CompareOrdinal(a.Value, b.Value)));
    private readonly SortedDictionary<(LocationId Location, PostKind Kind), GridPos> _posts =
        new(Comparer<(LocationId Location, PostKind Kind)>.Create((a, b) => string.CompareOrdinal(a.Location.Value, b.Location.Value) is var c && c != 0 ? c : a.Kind.CompareTo(b.Kind)));
    private readonly SortedDictionary<GridPos, bool> _doors = new(Comparer<GridPos>.Create((a, b) => (a.Y, a.X).CompareTo((b.Y, b.X))));

    public GridMap(AreaId area, IReadOnlyList<string> rows, IReadOnlyDictionary<char, LocationId> zones, IEnumerable<GridPos> occupied,
        IEnumerable<LightSource>? lights = null, IEnumerable<(GridPos At, bool Open)>? doors = null,
        IEnumerable<(LocationId Location, GridPos At)>? exits = null, IEnumerable<(LocationId Location, PostKind Kind, GridPos At)>? posts = null,
        IEnumerable<MapEdgeView>? edges = null, IEnumerable<LocationId>? indoorZones = null)
    {
        if (rows.Count == 0 || rows.Any(r => r.Length != rows[0].Length || r.Length == 0))
            throw new InvalidDataException($"la mappa dell'area '{area}' deve avere righe non vuote e della stessa lunghezza");
        Area = area;
        Height = rows.Count;
        Width = rows[0].Length;
        _cells = new char[Width, Height];
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var c = rows[y][x];
            if (c != '#' && c != '.' && !zones.ContainsKey(c))
                throw new InvalidDataException($"carattere '{c}' sconosciuto nella mappa dell'area '{area}'");
            _cells[x, y] = c;
        }
        Zones = new SortedDictionary<char, LocationId>(zones.ToDictionary(z => z.Key, z => z.Value));
        _indoorZones = (indoorZones ?? Array.Empty<LocationId>()).ToHashSet();
        if (_indoorZones.Any(z => !Zones.Values.Contains(z)))
            throw new InvalidDataException($"zona interna sconosciuta nella mappa '{area}'");
        foreach (var edge in edges ?? Array.Empty<MapEdgeView>())
        {
            if (edge is null || !InBounds(edge.A) || !InBounds(edge.B) || Math.Abs(edge.A.X - edge.B.X) + Math.Abs(edge.A.Y - edge.B.Y) != 1
                || _cells[edge.A.X, edge.A.Y] == '#' || _cells[edge.B.X, edge.B.Y] == '#'
                || !Enum.IsDefined(edge.Kind) || (edge.Kind == MapEdgeKind.Door) != (edge.DoorAt is not null)
                || edge.DoorAt is { } doorAt && doorAt != edge.A && doorAt != edge.B
                || !_edges.TryAdd(EdgeKey(edge.A, edge.B), edge))
                throw new InvalidDataException($"confine non valido nella mappa '{area}'");
            if (edge.DoorAt is { } at && !_edgeDoorCells.Add(at))
                throw new InvalidDataException($"due porte sullo stesso quadrato in '{area}'");
        }
        _occupied = occupied.ToHashSet();
        foreach (var cell in _occupied)
            if (!InBounds(cell) || _cells[cell.X, cell.Y] == '#')
                throw new InvalidDataException($"oggetto fuori mappa o dentro un muro in {cell}");
        Lights = (lights ?? Array.Empty<LightSource>()).ToArray();
        foreach (var light in Lights)
            if (!InBounds(light.At) || BlocksSight(light.At) || light.BrightFeet < 0 || light.DimFeet < 0
                || light.BrightFeet % 5 != 0 || light.DimFeet % 5 != 0)
                throw new InvalidDataException($"sorgente di luce non valida in {light.At}");
        foreach (var (at, open) in doors ?? Array.Empty<(GridPos, bool)>())
            if (!IsWalkable(at) || !_doors.TryAdd(at, open) || Lights.Any(l => l.At == at))
                throw new InvalidDataException($"porta non valida in {at}");
        if (_edgeDoorCells.Any(at => !_doors.ContainsKey(at)))
            throw new InvalidDataException($"stato di una porta su un confine mancante in '{area}'");
        foreach (var (location, at) in exits ?? Array.Empty<(LocationId, GridPos)>())
            if (!IsWalkable(at) || ZoneAt(at) != location || !OnEdge(at) || !_exits.TryAdd(location, at))
                throw new InvalidDataException($"uscita di '{location}' non valida in {at}: deve essere una casella percorribile del Luogo sul bordo della mappa, una per Luogo");
        foreach (var (location, kind, at) in posts ?? Array.Empty<(LocationId, PostKind, GridPos)>())
            if (!Enum.IsDefined(kind) || !IsWalkable(at) || ZoneAt(at) != location || !_posts.TryAdd((location, kind), at))
                throw new InvalidDataException($"posto {kind} di '{location}' non valido in {at}: deve essere una casella percorribile del Luogo, uno per tipo");
    }

    public AreaId Area { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyDictionary<char, LocationId> Zones { get; }
    public IReadOnlyCollection<MapEdgeView> Edges => _edges.Values;
    public IReadOnlyCollection<LocationId> IndoorZones => _indoorZones;
    public HashSet<GridPos> Explored { get; } = new();
    public bool UsesExploration => _indoorZones.Count > 0;

    /// <summary>The rows as given, without objects (what the save stores).</summary>
    public IReadOnlyList<string> Rows => Enumerable.Range(0, Height)
        .Select(y => new string(Enumerable.Range(0, Width).Select(x => _cells[x, y]).ToArray())).ToArray();

    public IReadOnlyCollection<GridPos> Occupied => _occupied;

    public IReadOnlyList<LightSource> Lights { get; }

    /// <summary>
    /// T6c: the exit square of each place on the map's edge that a Route leaves from (e.g. "the road to the woods").
    /// A journey from that place starts on this square; a journey to it ends here.
    /// </summary>
    public IReadOnlyDictionary<LocationId, GridPos> Exits => _exits;

    /// <summary>T6c: named squares of the places (work bench, home, guard post...).</summary>
    public IReadOnlyDictionary<(LocationId Location, PostKind Kind), GridPos> Posts => _posts;

    /// <summary>Doors by square (true = open), in reading order.</summary>
    public IReadOnlyDictionary<GridPos, bool> Doors => _doors;

    private readonly SortedDictionary<GridPos, SortedDictionary<ItemId, int>> _ground = new(Comparer<GridPos>.Create((a, b) => (a.Y, a.X).CompareTo((b.Y, b.X))));

    /// <summary>C3b: items lying on the squares, in reading order (only squares with something).</summary>
    public IReadOnlyDictionary<GridPos, SortedDictionary<ItemId, int>> Ground => _ground;

    public int OnGround(GridPos at, ItemId item) => _ground.TryGetValue(at, out var here) && here.TryGetValue(item, out var n) ? n : 0;

    /// <summary>Puts (or, negative, takes) items on a walkable square; a kind that drops to none is removed.</summary>
    public void PutOnGround(GridPos at, ItemId item, int count)
    {
        if (!IsWalkable(at))
            throw new InvalidOperationException($"nessuno spazio per oggetti in {at}");
        var total = OnGround(at, item) + count;
        if (total < 0)
            throw new InvalidOperationException($"meno di {-count} {item} in {at}");
        if (!_ground.TryGetValue(at, out var here))
            _ground[at] = here = new SortedDictionary<ItemId, int>();
        if (total == 0)
            here.Remove(item);
        else
            here[item] = total;
        if (here.Count == 0)
            _ground.Remove(at);
    }

    public bool IsDoor(GridPos p) => _doors.ContainsKey(p);

    public bool IsClosedDoor(GridPos p) => _doors.TryGetValue(p, out var open) && !open;

    private static (GridPos, GridPos) EdgeKey(GridPos a, GridPos b) =>
        (a.Y, a.X).CompareTo((b.Y, b.X)) <= 0 ? (a, b) : (b, a);

    private bool EdgeBlocks(GridPos a, GridPos b, bool sight)
    {
        if (!_edges.TryGetValue(EdgeKey(a, b), out var edge))
            return false;
        return edge.Kind switch
        {
            MapEdgeKind.Wall => true,
            MapEdgeKind.Window => !sight,
            MapEdgeKind.Door => sight && IsClosedDoor(edge.DoorAt!.Value),
            _ => false,
        };
    }

    private bool StepOpen(GridPos from, GridPos to, bool sight, bool requireDestinationWalkable = true)
    {
        if (!InBounds(to) || (!sight && requireDestinationWalkable && !IsWalkable(to)))
            return false;
        if (from.X == to.X || from.Y == to.Y)
            return !EdgeBlocks(from, to, sight);
        var viaX = new GridPos(to.X, from.Y);
        var viaY = new GridPos(from.X, to.Y);
        bool RouteOpen(GridPos via) => InBounds(via) && !BlocksSightOrWalk(via, sight)
            && !EdgeBlocks(from, via, sight) && !EdgeBlocks(via, to, sight)
            && (sight || ClosedDoorBetween(from, via) is null && ClosedDoorBetween(via, to) is null);
        // Sight slips past a corner if either side is open; a step does not cut a corner: both sides must be open.
        return sight ? RouteOpen(viaX) || RouteOpen(viaY) : RouteOpen(viaX) && RouteOpen(viaY);
    }

    private bool BlocksSightOrWalk(GridPos at, bool sight) => sight ? BlocksSight(at) : !IsWalkable(at);

    public bool IsEdgeDoor(GridPos at) => _edgeDoorCells.Contains(at);

    public GridPos? ClosedDoorBetween(GridPos from, GridPos to) =>
        _edges.TryGetValue(EdgeKey(from, to), out var edge) && edge.Kind == MapEdgeKind.Door && IsClosedDoor(edge.DoorAt!.Value)
            ? edge.DoorAt : IsClosedDoor(to) && !_edgeDoorCells.Contains(to) ? to : null;

    public void SetDoor(GridPos p, bool open)
    {
        if (!_doors.ContainsKey(p))
            throw new InvalidOperationException($"nessuna porta in {p}");
        _doors[p] = open;
    }

    /// <summary>
    /// Light cast on a square by the map's sources (SRD: Bright Light within the first radius, Dim Light for the
    /// additional one), from the fixed sources and any <paramref name="carried"/> ones (torches). Distances are counted in
    /// 5-ft squares; walls and closed doors stop light like sight, so it only leaks out through openings.
    /// </summary>
    public Light SourceLight(GridPos square, IEnumerable<LightSource>? carried = null)
    {
        var best = Light.Dark;
        foreach (var source in carried is null ? Lights : Lights.Concat(carried))
        {
            var feet = source.At.StepsTo(square) * 5;
            if (feet > source.BrightFeet + source.DimFeet || !HasLineOfSight(source.At, square))
                continue;
            var level = feet <= source.BrightFeet ? Light.Bright : Light.Dim;
            if (level > best)
                best = level;
        }
        return best;
    }

    /// <summary>Daylight enters marked interiors only through visible openings, fading after the first square.</summary>
    public Light DaylightOn(GridPos square, Light outside)
    {
        if (outside == Light.Dark || !InBounds(square))
            return Light.Dark;
        if (ZoneAt(square) is not { } zone || !_indoorZones.Contains(zone))
            return outside;
        var best = Light.Dark;
        for (var y = Math.Max(0, square.Y - 3); y <= Math.Min(Height - 1, square.Y + 3); y++)
        for (var x = Math.Max(0, square.X - 3); x <= Math.Min(Width - 1, square.X + 3); x++)
        {
            var outsideSquare = new GridPos(x, y);
            var distance = outsideSquare.StepsTo(square);
            if (distance > 3 || ZoneAt(outsideSquare) is { } outer && _indoorZones.Contains(outer)
                || !HasLineOfSight(outsideSquare, square))
                continue;
            var level = distance <= 1 ? outside : Light.Dim;
            if (level > best)
                best = level;
        }
        return best;
    }

    /// <summary>A step from <paramref name="from"/> to the adjacent <paramref name="to"/> does not squeeze past a blocked corner.</summary>
    public bool DiagonalOpen(GridPos from, GridPos to) => StepOpen(from, to, sight: false, requireDestinationWalkable: false);

    /// <summary>On the outer edge of the map (where an exit leads off it).</summary>
    public bool OnEdge(GridPos p) => InBounds(p) && (p.X == 0 || p.Y == 0 || p.X == Width - 1 || p.Y == Height - 1);

    public bool InBounds(GridPos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    public bool IsWalkable(GridPos p) => InBounds(p) && _cells[p.X, p.Y] != '#' && !_occupied.Contains(p);

    /// <summary>The place a square belongs to, or null on open ground (the road).</summary>
    public LocationId? ZoneAt(GridPos p) =>
        InBounds(p) && Zones.TryGetValue(_cells[p.X, p.Y], out var zone) ? zone : null;

    /// <summary>Walls and closed doors block sight; stores and open squares do not.</summary>
    public bool BlocksSight(GridPos p) => !InBounds(p) || _cells[p.X, p.Y] == '#' || IsClosedDoor(p) && !_edgeDoorCells.Contains(p);

    private bool IsWall(GridPos p) => !InBounds(p) || _cells[p.X, p.Y] == '#';

    /// <summary>
    /// ADAPTATION: line of sight along a Bresenham line between square centres, blocked by walls and by diagonal
    /// steps squeezing between two walls. Symmetric: clear if the line is clear in either direction.
    /// </summary>
    public bool HasLineOfSight(GridPos a, GridPos b) => ClearLine(a, b) || ClearLine(b, a);

    private bool ClearLine(GridPos from, GridPos to)
    {
        int x = from.X, y = from.Y;
        int dx = Math.Abs(to.X - x), dy = -Math.Abs(to.Y - y);
        int sx = x < to.X ? 1 : -1, sy = y < to.Y ? 1 : -1;
        var err = dx + dy;
        while (x != to.X || y != to.Y)
        {
            int px = x, py = y;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
            var here = new GridPos(x, y);
            if (!StepOpen(new GridPos(px, py), here, sight: true))
                return false;
            if (here != to && BlocksSight(here))
                return false;
            if (x != px && y != py && BlocksSight(new GridPos(x, py)) && BlocksSight(new GridPos(px, y)))
                return false; // squeezing diagonally between two walls
        }
        return true;
    }

    /// <summary>
    /// Steps a sound travels from <paramref name="from"/> to <paramref name="to"/> around walls (8 directions, no cut
    /// corners), or null if farther than <paramref name="max"/> squares. Stores do not stop sound; a closed door muffles
    /// it, counting as <see cref="Tuning.ClosedDoorSoundSteps"/> squares more.
    /// </summary>
    public int? SoundSteps(GridPos from, GridPos to, int max)
    {
        if (from == to)
            return 0;
        var best = new Dictionary<GridPos, int> { [from] = 0 };
        var open = new SortedSet<(int Steps, int Y, int X)> { (0, from.Y, from.X) };
        while (open.Count > 0)
        {
            var (steps, y, x) = open.Min;
            open.Remove(open.Min);
            var here = new GridPos(x, y);
            if (here == to)
                return steps;
            foreach (var (dx, dy) in Directions)
            {
                var p = new GridPos(here.X + dx, here.Y + dy);
                if (IsWall(p) || !StepOpen(here, p, sight: false, requireDestinationWalkable: false)
                    && !_edges.ContainsKey(EdgeKey(here, p)))
                    continue;
                if (_edges.TryGetValue(EdgeKey(here, p), out var soundEdge) && soundEdge.Kind == MapEdgeKind.Wall)
                    continue;
                if (dx != 0 && dy != 0 && (BlocksSight(new GridPos(here.X + dx, here.Y)) || BlocksSight(new GridPos(here.X, here.Y + dy))))
                    continue;
                var doorPenalty = soundEdge?.Kind == MapEdgeKind.Door && IsClosedDoor(soundEdge.DoorAt!.Value)
                    || !_edgeDoorCells.Contains(p) && IsClosedDoor(p) ? Tuning.ClosedDoorSoundSteps : 0;
                var cost = steps + 1 + doorPenalty
                    + (soundEdge?.Kind == MapEdgeKind.Window ? Tuning.ClosedDoorSoundSteps : 0);
                if (cost > max || best.TryGetValue(p, out var known) && known <= cost)
                    continue;
                if (best.TryGetValue(p, out var old))
                    open.Remove((old, p.Y, p.X));
                best[p] = cost;
                open.Add((cost, p.Y, p.X));
            }
        }
        return null;
    }

    private static readonly (int Dx, int Dy)[] Directions =
        { (0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1) };

    /// <summary>
    /// Shortest path by the SRD grid rules: 8 directions, 1 square each, no diagonal across the corner of a blocked
    /// square; closed doors are walked through (opening one is free, SRD). <paramref name="extraCost"/> (never
    /// negative) makes some squares dearer: a sneaking walk detours around light. Time is always counted in squares
    /// walked. Deterministic A*: among paths of the same cost the one closest to the straight line wins, then
    /// (estimate, y, x). Returns the squares after <paramref name="from"/>, ending at <paramref name="to"/>, or null
    /// when unreachable.
    /// </summary>
    public IReadOnlyList<GridPos>? FindPath(GridPos from, GridPos to, Func<GridPos, int>? extraCost = null)
    {
        if (!IsWalkable(to) || !InBounds(from))
            return null;
        if (from == to)
            return Array.Empty<GridPos>();

        // Cost = (squares, drift): first the fewest squares (the rules), then, among paths that take the same time, the
        // one staying closest to the straight line, so the walk looks natural. Drift is in exact integers.
        long Drift(GridPos p) => Math.Abs((long)(to.X - from.X) * (p.Y - from.Y) - (long)(to.Y - from.Y) * (p.X - from.X));
        var cost = new Dictionary<GridPos, (int Steps, long Drift)> { [from] = (0, 0) };
        var cameFrom = new Dictionary<GridPos, GridPos>();
        var open = new SortedSet<(int F, long Drift, int H, int Y, int X)> { (from.StepsTo(to), 0, from.StepsTo(to), from.Y, from.X) };
        while (open.Count > 0)
        {
            var current = open.Min;
            open.Remove(current);
            var here = new GridPos(current.X, current.Y);
            if (here == to)
                break;
            foreach (var (dx, dy) in Directions)
            {
                var next = new GridPos(here.X + dx, here.Y + dy);
                if (!IsWalkable(next) || !StepOpen(here, next, sight: false))
                    continue;
                if (dx != 0 && dy != 0 && (!IsWalkable(new GridPos(here.X + dx, here.Y)) || !IsWalkable(new GridPos(here.X, here.Y + dy))))
                    continue; // no cutting corners
                var nextCost = (Steps: cost[here].Steps + 1 + (extraCost?.Invoke(next) ?? 0), Drift: cost[here].Drift + Drift(next));
                if (cost.TryGetValue(next, out var known) && known.CompareTo(nextCost) <= 0)
                    continue;
                if (cost.TryGetValue(next, out var old))
                    open.Remove((old.Steps + next.StepsTo(to), old.Drift, next.StepsTo(to), next.Y, next.X));
                cost[next] = nextCost;
                cameFrom[next] = here;
                open.Add((nextCost.Steps + next.StepsTo(to), nextCost.Drift, next.StepsTo(to), next.Y, next.X));
            }
        }

        if (!cameFrom.ContainsKey(to))
            return null;
        var path = new List<GridPos>();
        for (var p = to; p != from; p = cameFrom[p])
            path.Add(p);
        path.Reverse();
        return path;
    }
}

/// <summary>A lamp, torch or other light on a map square (radii in feet, as in the SRD equipment list).</summary>
internal sealed record LightSource(GridPos At, int BrightFeet, int DimFeet);
