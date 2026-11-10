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
        IEnumerable<MapEdgeView>? edges = null, IEnumerable<LocationId>? indoorZones = null,
        IEnumerable<(BarrierKey Key, Barrier Barrier)>? barriers = null, IEnumerable<(BarrierKey Key, WindowGlass Glass)>? glass = null)
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
                || edge.Open is not null && edge.Kind != MapEdgeKind.Window
                || !_edges.TryAdd(EdgeKey(edge.A, edge.B), edge))
                throw new InvalidDataException($"confine non valido nella mappa '{area}'");
            if (edge.DoorAt is { } at && !_edgeDoorCells.Add(at))
                throw new InvalidDataException($"due porte sullo stesso quadrato in '{area}'");
            if (edge.Kind == MapEdgeKind.Window && edge.Open == true)
                _openWindows.Add(EdgeKey(edge.A, edge.B));
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
        foreach (var (key, barrier) in barriers ?? Array.Empty<(BarrierKey, Barrier)>()) // P4a
        {
            if (!(key.IsDoor ? _doors.ContainsKey(key.A) : IsWindow(key.A, key.B) && key == BarrierKey.Window(key.A, key.B)) || barrier.ForceDc is < 1 or > 30
                || barrier.Broken && barrier.Locked || !_barriers.TryAdd(key, barrier))
                throw new InvalidDataException($"serratura non valida nella mappa '{area}': {key}");
            if (barrier.Holds && (key.IsDoor ? _doors[key.A] : _openWindows.Contains(EdgeKey(key.A, key.B))))
                throw new InvalidDataException($"una {key} aperta non può essere chiusa a chiave o bloccata in '{area}'");
            if (barrier.Barred && key.IsDoor || barrier.BarsDc is < 1 or > 30)
                throw new InvalidDataException($"sbarre non valide nella mappa '{area}': {key}");
        }
        foreach (var (key, state) in glass ?? Array.Empty<(BarrierKey, WindowGlass)>()) // P4b
            if (key.IsDoor || !IsWindow(key.A, key.B) || key != BarrierKey.Window(key.A, key.B) || !Enum.IsDefined(state)
                || state != WindowGlass.Intact && !_glass.TryAdd(EdgeKey(key.A, key.B), state))
                throw new InvalidDataException($"vetro non valido nella mappa '{area}': {key}");
    }

    public AreaId Area { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyDictionary<char, LocationId> Zones { get; }
    /// <summary>The thin boundaries, windows with their state now (P2b).</summary>
    public IReadOnlyCollection<MapEdgeView> Edges =>
        _edges.Select(e => e.Value.Kind == MapEdgeKind.Window
            ? e.Value with
            {
                Open = _openWindows.Contains(e.Key), Glass = _glass.GetValueOrDefault(e.Key, WindowGlass.Intact), // P4b
                Barred = _barriers.TryGetValue(BarrierKey.Window(e.Key.Item1, e.Key.Item2), out var b) && b.Barred,
            }
            : e.Value).ToList();

    /// <summary>P4b: windows whose glass is not whole (a whole one is not listed).</summary>
    private readonly Dictionary<(GridPos, GridPos), WindowGlass> _glass = new();

    public WindowGlass GlassOf(GridPos a, GridPos b) => _glass.GetValueOrDefault(EdgeKey(a, b), WindowGlass.Intact);

    public void SetGlass(GridPos a, GridPos b, WindowGlass glass)
    {
        if (!IsWindow(a, b))
            throw new InvalidOperationException($"nessuna finestra fra {a} e {b}");
        if (glass == WindowGlass.Intact)
            _glass.Remove(EdgeKey(a, b));
        else
            _glass[EdgeKey(a, b)] = glass;
    }

    /// <summary>P4b: a shot or a blow passes this window: open, or its glass broken.</summary>
    public bool LetsThrough(GridPos a, GridPos b) => IsOpenWindow(a, b) || GlassOf(a, b) != WindowGlass.Intact;

    /// <summary>P4b: the windows whose glass is not whole, for the save.</summary>
    public IEnumerable<(BarrierKey Key, WindowGlass Glass)> BrokenGlass =>
        _glass.Select(g => (BarrierKey.Window(g.Key.Item1, g.Key.Item2), g.Value));

    /// <summary>P4a: doors and windows that lock or stick, and how they hold now.</summary>
    public IReadOnlyDictionary<BarrierKey, Barrier> Barriers => _barriers;

    private readonly SortedDictionary<BarrierKey, Barrier> _barriers = new(BarrierKey.Order);

    public Barrier? BarrierOf(BarrierKey key) => _barriers.GetValueOrDefault(key);

    /// <summary>P4a: the closed door here does not open by hand (locked or stuck).</summary>
    public bool DoorHolds(GridPos door) => IsClosedDoor(door) && _barriers.TryGetValue(BarrierKey.Door(door), out var b) && b.Holds;

    /// <summary>The thin boundaries as built, for geometry (a window's state: <see cref="IsOpenWindow"/>).</summary>
    public IReadOnlyCollection<MapEdgeView> Boundaries => _edges.Values;

    /// <summary>P2b: the windows open now (a window is closed unless listed).</summary>
    private readonly HashSet<(GridPos, GridPos)> _openWindows = new();

    /// <summary>P2b: the window between two adjacent squares, if there is one.</summary>
    public bool IsWindow(GridPos a, GridPos b) => _edges.TryGetValue(EdgeKey(a, b), out var edge) && edge.Kind == MapEdgeKind.Window;

    public bool IsOpenWindow(GridPos a, GridPos b) => _openWindows.Contains(EdgeKey(a, b));

    /// <summary>P2b: opens or shuts a window. Sight and light do not change (they always pass a window).</summary>
    public void SetWindow(GridPos a, GridPos b, bool open)
    {
        if (!IsWindow(a, b))
            throw new InvalidOperationException($"nessuna finestra fra {a} e {b}");
        if (open)
            _openWindows.Add(EdgeKey(a, b));
        else
            _openWindows.Remove(EdgeKey(a, b));
    }
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

    /// <summary>R5c: the traps on this map, by square.</summary>
    public SortedDictionary<GridPos, Trap> Traps { get; } = new();

    /// <summary>R5c: a pit one falls into here: not made safe (a sprung one is an open hole).</summary>
    public bool PitAt(GridPos square) => Traps.TryGetValue(square, out var t) && t.Kind == TrapKind.Pit && !t.Disarmed;

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

    internal bool StepOpen(GridPos from, GridPos to, bool sight, bool requireDestinationWalkable = true)
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
        _daylight.Clear(); // a door opened or shut changes what can be seen, and what daylight reaches
        _sight.Clear();
        _opaque = null;
        _fieldEye = null;
    }

    /// <summary>
    /// Light cast on a square by the map's sources (SRD: Bright Light within the first radius, Dim Light for the
    /// additional one), from the fixed sources and any <paramref name="carried"/> ones (torches). Radii are measured
    /// between square centres in feet, so their footprint is round rather than a square of diagonal grid steps.
    /// Walls and closed doors stop light like sight, so it only leaks out through openings.
    /// </summary>
    public Light SourceLight(GridPos square, IEnumerable<LightSource>? carried = null)
    {
        var best = Light.Dark;
        for (var i = 0; i < Lights.Count; i++)
            best = Brighter(best, Lights[i], square);
        if (carried is IReadOnlyList<LightSource> list)
            for (var i = 0; i < list.Count; i++)
                best = Brighter(best, list[i], square);
        else if (carried is not null)
            foreach (var source in carried)
                best = Brighter(best, source, square);
        return best;
    }

    /// <summary>The better of <paramref name="best"/> and the light <paramref name="source"/> casts on the square.</summary>
    private Light Brighter(Light best, LightSource source, GridPos square)
    {
        var dx = (source.At.X - square.X) * 5L;
        var dy = (source.At.Y - square.Y) * 5L;
        var feetSquared = dx * dx + dy * dy;
        var reach = (long)source.BrightFeet + source.DimFeet;
        if (feetSquared > reach * reach || !HasLineOfSight(source.At, square))
            return best;
        var level = feetSquared <= (long)source.BrightFeet * source.BrightFeet ? Light.Bright : Light.Dim;
        return level > best ? level : best;
    }

    /// <summary>Daylight enters marked interiors only through visible openings, fading after the first square.</summary>
    public Light DaylightOn(GridPos square, Light outside)
    {
        if (outside == Light.Dark || !InBounds(square))
            return Light.Dark;
        if (!Indoors(square))
            return outside;
        if (_daylight.TryGetValue((square, outside), out var known))
            return known;
        var best = Light.Dark;
        for (var y = Math.Max(0, square.Y - 3); y <= Math.Min(Height - 1, square.Y + 3); y++)
        for (var x = Math.Max(0, square.X - 3); x <= Math.Min(Width - 1, square.X + 3); x++)
        {
            var outsideSquare = new GridPos(x, y);
            var distance = outsideSquare.StepsTo(square);
            if (distance > 3 || Indoors(outsideSquare)
                || !HasLineOfSight(outsideSquare, square))
                continue;
            var level = distance <= 1 ? outside : Light.Dim;
            if (level > best)
                best = level;
        }
        return _daylight[(square, outside)] = best;
    }

    /// <summary>Whether a square (in bounds) belongs to a marked interior. Squares and interiors never change, so it is read once.</summary>
    private bool Indoors(GridPos square)
    {
        if (_indoors is null)
        {
            _indoors = new bool[Width, Height];
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                _indoors[x, y] = Zones.TryGetValue(_cells[x, y], out var zone) && _indoorZones.Contains(zone);
        }
        return _indoors[square.X, square.Y];
    }

    private bool[,]? _indoors;

    /// <summary>Daylight on indoor squares, by the light outside; it depends only on the walls and the doors.</summary>
    private readonly Dictionary<(GridPos, Light), Light> _daylight = new();

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
    public bool HasLineOfSight(GridPos a, GridPos b)
    {
        var key = EdgeKey(a, b);
        if (!_sight.TryGetValue(key, out var clear))
        {
            if (_sight.Count >= SightCacheLimit)
                _sight.Clear(); // only a memory of lines already traced: forgetting them changes no answer
            _sight[key] = clear = ClearLine(a, b) || ClearLine(b, a);
        }
        return clear;
    }

    /// <summary>
    /// <see cref="HasLineOfSight"/> from one square to many, as a look over the whole map asks: the answers are kept in a
    /// grid for the last square looked from instead of among the lines between any two squares, which a single look on a
    /// large map would otherwise fill with thousands of entries.
    /// </summary>
    internal bool HasLineOfSightFrom(GridPos eye, GridPos square)
    {
        if (!InBounds(eye) || !InBounds(square))
            return HasLineOfSight(eye, square);
        if (_fieldEye != eye)
        {
            if (_field is null)
                _field = new sbyte[Width, Height];
            else
                Array.Clear(_field);
            _fieldEye = eye;
        }
        ref var known = ref _field![square.X, square.Y];
        if (known == 0)
            known = (_sight.TryGetValue(EdgeKey(eye, square), out var traced) ? traced : ClearLine(eye, square) || ClearLine(square, eye))
                ? (sbyte)1 : (sbyte)2;
        return known == 1;
    }

    /// <summary>For <see cref="HasLineOfSightFrom"/>: 0 not traced yet, 1 clear, 2 blocked; forgotten when a door changes.</summary>
    private sbyte[,]? _field;

    private GridPos? _fieldEye;

    /// <summary>Lines of sight already traced (symmetric); they change only when a door does.</summary>
    private readonly Dictionary<(GridPos, GridPos), bool> _sight = new();

    /// <summary>
    /// Region P3: how many traced lines are kept at most. Every square the player stands on adds a line to every square
    /// it looks at, so on a large map walked for hours the memory would otherwise grow without end (a 160×120 field
    /// crossed once by day: about 3 million lines, 190 MB). This many is roughly 20 MB: the sight from some
    /// dozens of standing places on a large map.
    /// </summary>
    internal int SightCacheLimit { get; set; } = 500_000;

    /// <summary>For tests: how many lines of sight are remembered now.</summary>
    internal int SightCacheCount => _sight.Count;

    /// <summary>For tests: the line of sight traced step by step with every edge looked up, without the grids or the caches.</summary>
    internal bool HasLineOfSightChecked(GridPos a, GridPos b) => ClearLineChecked(a, b) || ClearLineChecked(b, a);

    /// <summary>
    /// <see cref="ClearLine(GridPos, GridPos)"/> read from two grids: what blocks sight on each square, and which squares
    /// touch an edge (a wall, door or window between two squares). A step between squares that touch no edge needs no edge
    /// lookup; any other step is checked by <see cref="StepOpen"/> as before, so the answer is the same.
    /// </summary>
    private bool ClearLine(GridPos from, GridPos to)
    {
        if (!InBounds(from) || !InBounds(to))
            return ClearLineChecked(from, to);
        var opaque = _opaque ??= OpaqueSquares();
        var touched = _edgeTouched ??= EdgeTouchedSquares();
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
            var straight = x == px || y == py;
            // With no edge around, StepOpen asks only that sight slips past a corner on one side: the squeeze test below.
            var edgeFree = !touched[px, py] && !touched[x, y] && (straight || !touched[x, py] && !touched[px, y]);
            if (!edgeFree && !StepOpen(new GridPos(px, py), new GridPos(x, y), sight: true))
                return false;
            if ((x != to.X || y != to.Y) && opaque[x, y])
                return false;
            if (!straight && opaque[x, py] && opaque[px, y])
                return false; // squeezing diagonally between two walls
        }
        return true;
    }

    private bool[,] OpaqueSquares()
    {
        var opaque = new bool[Width, Height];
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            opaque[x, y] = BlocksSight(new GridPos(x, y));
        return opaque;
    }

    private bool[,] EdgeTouchedSquares()
    {
        var touched = new bool[Width, Height];
        foreach (var (a, b) in _edges.Keys)
        {
            touched[a.X, a.Y] = true;
            touched[b.X, b.Y] = true;
        }
        return touched;
    }

    /// <summary><see cref="BlocksSight"/> on each square, rebuilt after a door opens or shuts.</summary>
    private bool[,]? _opaque;

    /// <summary>Squares on either side of an edge; edges never change.</summary>
    private bool[,]? _edgeTouched;

    private bool ClearLineChecked(GridPos from, GridPos to)
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

    /// <summary>P5a: getting through a window, in squares of a way: opening or breaking it, then climbing over (10 feet).</summary>
    public const int WindowCrossingSteps = 4;

    /// <summary>
    /// Shortest path by the SRD grid rules: 8 directions, 1 square each, no diagonal across the corner of a blocked
    /// square; closed doors are walked through (opening one is free, SRD). <paramref name="extraCost"/> (never
    /// negative) makes some squares dearer: a sneaking walk detours around light. Time is always counted in squares
    /// walked. Deterministic A*: among paths of the same cost the one closest to the straight line wins, then
    /// (estimate, y, x). Returns the squares after <paramref name="from"/>, ending at <paramref name="to"/>, or null
    /// when unreachable.
    /// </summary>
    /// <param name="opens">P4a: the doors the walker can open though they hold (locked, with its key); null: none. A closed door
    /// that holds and is not among them is a wall to this walker.</param>
    /// <param name="climbs">P5a: the windows (by their two squares) the walker would get through, straight across; each
    /// costs <see cref="WindowCrossingSteps"/> squares. Null: windows are walls, as for any walk.</param>
    public IReadOnlyList<GridPos>? FindPath(GridPos from, GridPos to, Func<GridPos, int>? extraCost = null, Func<GridPos, bool>? opens = null,
        Func<GridPos, GridPos, bool>? climbs = null)
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
                var climb = climbs is not null && (dx == 0 || dy == 0) && IsWindow(here, next) && climbs(here, next);
                if (!IsWalkable(next) || !climb && !StepOpen(here, next, sight: false))
                    continue;
                if (dx != 0 && dy != 0 && (!IsWalkable(new GridPos(here.X + dx, here.Y)) || !IsWalkable(new GridPos(here.X, here.Y + dy))))
                    continue; // no cutting corners
                if (ClosedDoorBetween(here, next) is { } door && DoorHolds(door) && opens?.Invoke(door) != true)
                    continue; // P4a: locked without its key, or stuck
                var nextCost = (Steps: cost[here].Steps + (climb ? WindowCrossingSteps : 1) + (extraCost?.Invoke(next) ?? 0), Drift: cost[here].Drift + Drift(next));
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
