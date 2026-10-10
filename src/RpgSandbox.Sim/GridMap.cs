using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>
/// The walkable grid of one area. Built from text rows: '#' blocked, '.' open ground outside any zone, a letter an
/// open square inside the zone (place) that letter stands for. Stores occupy a square, which then blocks movement.
/// Immutable once built; part of the save, so a game never depends on a map file that may change.
/// </summary>
internal sealed class GridMap
{
    private readonly char[,] _cells;
    private readonly HashSet<GridPos> _occupied;

    public GridMap(AreaId area, IReadOnlyList<string> rows, IReadOnlyDictionary<char, LocationId> zones, IEnumerable<GridPos> occupied)
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
    }

    public AreaId Area { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyDictionary<char, LocationId> Zones { get; }

    /// <summary>The rows as given, without objects (what the save stores).</summary>
    public IReadOnlyList<string> Rows => Enumerable.Range(0, Height)
        .Select(y => new string(Enumerable.Range(0, Width).Select(x => _cells[x, y]).ToArray())).ToArray();

    public IReadOnlyCollection<GridPos> Occupied => _occupied;

    public bool InBounds(GridPos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    public bool IsWalkable(GridPos p) => InBounds(p) && _cells[p.X, p.Y] != '#' && !_occupied.Contains(p);

    /// <summary>The place a square belongs to, or null on open ground (the road).</summary>
    public LocationId? ZoneAt(GridPos p) =>
        InBounds(p) && Zones.TryGetValue(_cells[p.X, p.Y], out var zone) ? zone : null;

    private static readonly (int Dx, int Dy)[] Directions =
        { (0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1) };

    /// <summary>
    /// Shortest path by the SRD grid rules: 8 directions, 1 square each, no diagonal across the corner of a blocked
    /// square. Deterministic A*: ties broken by (estimate, y, x). Returns the squares after <paramref name="from"/>,
    /// ending at <paramref name="to"/>, or null when unreachable.
    /// </summary>
    public IReadOnlyList<GridPos>? FindPath(GridPos from, GridPos to)
    {
        if (!IsWalkable(to) || !InBounds(from))
            return null;
        if (from == to)
            return Array.Empty<GridPos>();

        var cost = new Dictionary<GridPos, int> { [from] = 0 };
        var cameFrom = new Dictionary<GridPos, GridPos>();
        var open = new SortedSet<(int F, int H, int Y, int X)> { (from.StepsTo(to), from.StepsTo(to), from.Y, from.X) };
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
                var nextCost = cost[here] + 1;
                if (cost.TryGetValue(next, out var known) && known <= nextCost)
                    continue;
                if (cost.TryGetValue(next, out var old))
                    open.Remove((old + next.StepsTo(to), next.StepsTo(to), next.Y, next.X));
                cost[next] = nextCost;
                cameFrom[next] = here;
                open.Add((nextCost + next.StepsTo(to), next.StepsTo(to), next.Y, next.X));
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
