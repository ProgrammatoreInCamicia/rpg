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
    private readonly HashSet<GridPos> _occupied;
    private readonly SortedDictionary<GridPos, bool> _doors = new(Comparer<GridPos>.Create((a, b) => (a.Y, a.X).CompareTo((b.Y, b.X))));

    public GridMap(AreaId area, IReadOnlyList<string> rows, IReadOnlyDictionary<char, LocationId> zones, IEnumerable<GridPos> occupied,
        IEnumerable<LightSource>? lights = null, IEnumerable<(GridPos At, bool Open)>? doors = null)
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
    }

    public AreaId Area { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyDictionary<char, LocationId> Zones { get; }

    /// <summary>The rows as given, without objects (what the save stores).</summary>
    public IReadOnlyList<string> Rows => Enumerable.Range(0, Height)
        .Select(y => new string(Enumerable.Range(0, Width).Select(x => _cells[x, y]).ToArray())).ToArray();

    public IReadOnlyCollection<GridPos> Occupied => _occupied;

    public IReadOnlyList<LightSource> Lights { get; }

    /// <summary>Doors by square (true = open), in reading order.</summary>
    public IReadOnlyDictionary<GridPos, bool> Doors => _doors;

    public bool IsDoor(GridPos p) => _doors.ContainsKey(p);

    public bool IsClosedDoor(GridPos p) => _doors.TryGetValue(p, out var open) && !open;

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

    public bool InBounds(GridPos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    public bool IsWalkable(GridPos p) => InBounds(p) && _cells[p.X, p.Y] != '#' && !_occupied.Contains(p);

    /// <summary>The place a square belongs to, or null on open ground (the road).</summary>
    public LocationId? ZoneAt(GridPos p) =>
        InBounds(p) && Zones.TryGetValue(_cells[p.X, p.Y], out var zone) ? zone : null;

    /// <summary>Walls and closed doors block sight; stores and open squares do not.</summary>
    public bool BlocksSight(GridPos p) => !InBounds(p) || _cells[p.X, p.Y] == '#' || IsClosedDoor(p);

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
                if (IsWall(p))
                    continue;
                if (dx != 0 && dy != 0 && (BlocksSight(new GridPos(here.X + dx, here.Y)) || BlocksSight(new GridPos(here.X, here.Y + dy))))
                    continue;
                var cost = steps + 1 + (IsClosedDoor(p) ? Tuning.ClosedDoorSoundSteps : 0);
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
                if (!IsWalkable(next))
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
