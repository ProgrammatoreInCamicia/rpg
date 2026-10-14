using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>
/// P1 (free movement prototype): a short way for a body between two exact points of a map. The body is a disc of
/// <see cref="BodyRadius"/> inches (a Medium creature, 3 feet across, in its 5-foot space). Obstacles are the squares
/// one cannot stand in (walls, stores), the thin walls and windows between squares, the map's border and the creatures
/// one cannot pass; doors, open or closed, are passable (the walker opens them on its way). The way is any-angle: straight
/// lines between the start, the goal and points just off the obstacles' corners (a visibility graph, searched with A*). It is
/// the shortest way through those points, not always the shortest around every corner: a few inches longer
/// per turn, since the points sit at fixed offsets around each corner (review P1a).
/// Everything is computed from integers in a fixed order, so the same question always gets the same way, in a preview
/// as in the walk.
/// </summary>
internal sealed class SpotPlanner
{
    public const int BodyRadius = 18;

    /// <summary>How far off a corner the way passes, beyond the body: a little room, so rounding never grazes a wall.</summary>
    private const int Margin = 2;

    /// <summary>Tolerance in inches of the clearance checks of a way (points of a walk are rounded to the inch).</summary>
    private const double Slack = 0.75;

    private readonly int _width;
    private readonly int _height;
    private readonly List<(Spot A, Spot B)> _walls = new();
    private readonly List<(int X0, int Y0, int X1, int Y1)> _boxes = new();
    private readonly List<Spot> _blockers;
    private readonly List<Spot> _standing;
    private readonly List<Spot> _corners = new();

    /// <param name="blockers">Bodies one cannot pass through (hostile creatures).</param>
    /// <param name="standing">Every other body: one cannot stop on them.</param>
    /// <param name="closedDoorsBlock">In a fight a closed door is a wall (opening it is an interaction); outside, the walker opens it.</param>
    public SpotPlanner(GridMap map, IEnumerable<Spot> blockers, IEnumerable<Spot> standing, bool closedDoorsBlock = false)
    {
        _width = map.Width * Spot.PerSquare;
        _height = map.Height * Spot.PerSquare;
        _blockers = blockers.ToList();
        _standing = standing.ToList();

        bool Blocked(int x, int y) => !map.InBounds(new GridPos(x, y)) || !map.IsWalkable(new GridPos(x, y))
            || closedDoorsBlock && map.IsClosedDoor(new GridPos(x, y)) && !map.IsEdgeDoor(new GridPos(x, y));
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            // Only blocked squares that touch open ground can stop a body; the inside of a wall mass cannot.
            var touchesOpen = false;
            for (var dy = -1; dy <= 1 && !touchesOpen; dy++)
            for (var dx = -1; dx <= 1 && !touchesOpen; dx++)
                touchesOpen = !Blocked(x + dx, y + dy);
            if (Blocked(x, y) && touchesOpen)
                _boxes.Add((x * Spot.PerSquare, y * Spot.PerSquare, (x + 1) * Spot.PerSquare, (y + 1) * Spot.PerSquare));
        }
        var cornerPoints = new SortedSet<(int Y, int X)>();
        for (var cy = 0; cy <= map.Height; cy++)
        for (var cx = 0; cx <= map.Width; cx++)
        {
            // A corner sticking out into open ground: one blocked square of the four around it, or two facing diagonally.
            var nw = Blocked(cx - 1, cy - 1);
            var ne = Blocked(cx, cy - 1);
            var sw = Blocked(cx - 1, cy);
            var se = Blocked(cx, cy);
            var count = (nw ? 1 : 0) + (ne ? 1 : 0) + (sw ? 1 : 0) + (se ? 1 : 0);
            if (count == 1 || count == 2 && nw == se)
                cornerPoints.Add((cy * Spot.PerSquare, cx * Spot.PerSquare));
        }
        foreach (var edge in map.Boundaries.Where(e => e.Kind != MapEdgeKind.Door || closedDoorsBlock && map.IsClosedDoor(e.DoorAt!.Value)))
        {
            var (a, b) = Border(edge.A, edge.B);
            _walls.Add((a, b));
            cornerPoints.Add((a.Y, a.X));
            cornerPoints.Add((b.Y, b.X));
        }

        const int off = BodyRadius + Margin;
        var diagonal = (int)Math.Ceiling(off / Math.Sqrt(2));
        var around = new SortedSet<(int Y, int X)>();
        foreach (var (y, x) in cornerPoints)
            foreach (var (dx, dy) in new[] { (off, 0), (-off, 0), (0, off), (0, -off), (diagonal, diagonal), (diagonal, -diagonal), (-diagonal, diagonal), (-diagonal, -diagonal) })
                around.Add((y + dy, x + dx));
        const int body = 2 * BodyRadius + Margin + 1;
        var bodyDiagonal = (int)Math.Ceiling(body / Math.Sqrt(2));
        foreach (var c in _blockers)
            foreach (var (dx, dy) in new[] { (body, 0), (-body, 0), (0, body), (0, -body), (bodyDiagonal, bodyDiagonal), (bodyDiagonal, -bodyDiagonal), (-bodyDiagonal, bodyDiagonal), (-bodyDiagonal, -bodyDiagonal) })
                around.Add((c.Y + dy, c.X + dx));
        foreach (var (y, x) in around)
            if (Clearance(new Spot(x, y)) >= BodyRadius + 1)
                _corners.Add(new Spot(x, y));
    }

    /// <summary>The border two adjacent squares share, as a segment from its top or left end.</summary>
    internal static (Spot A, Spot B) Border(GridPos a, GridPos b)
    {
        var lo = a.CompareTo(b) <= 0 ? a : b;
        return a.X != b.X
            ? (new Spot(Math.Max(a.X, b.X) * Spot.PerSquare, lo.Y * Spot.PerSquare), new Spot(Math.Max(a.X, b.X) * Spot.PerSquare, (lo.Y + 1) * Spot.PerSquare))
            : (new Spot(lo.X * Spot.PerSquare, Math.Max(a.Y, b.Y) * Spot.PerSquare), new Spot((lo.X + 1) * Spot.PerSquare, Math.Max(a.Y, b.Y) * Spot.PerSquare));
    }

    /// <summary>The body fits at <paramref name="p"/>: off every wall and the border, and not on anyone.</summary>
    public bool Fits(Spot p) =>
        Clearance(p) >= BodyRadius && _standing.All(o => p.DistanceTo(o) >= 2 * BodyRadius);

    /// <summary>Distance from a point to the nearest static obstacle or the map's border (0 inside one).</summary>
    private double Clearance(Spot p)
    {
        double best = Math.Min(Math.Min(p.X, p.Y), Math.Min(_width - p.X, _height - p.Y));
        foreach (var (a, b) in _walls)
            best = Math.Min(best, PointSegment(p, a, b));
        foreach (var box in _boxes)
            best = Math.Min(best, PointBox(p, box));
        return Math.Max(0, best);
    }

    /// <summary>
    /// The shortest way through the corner points from <paramref name="from"/> to <paramref name="to"/> (which must fit), or
    /// null if there is none.
    /// The start need not fit: one standing where it is can always set off.
    /// </summary>
    public SpotPath? Find(Spot from, Spot to)
    {
        if (from == to)
            return new SpotPath(new[] { from }, 0);
        if (Clear(from, to))
            return new SpotPath(new[] { from, to }, from.DistanceTo(to));

        var nodes = new List<Spot> { from, to };
        nodes.AddRange(_corners.Where(c => c != from && c != to));
        var g = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
        var previous = Enumerable.Repeat(-1, nodes.Count).ToArray();
        var closed = new bool[nodes.Count];
        // Ties broken by node order, which is fixed: the same question always gets the same way.
        var open = new PriorityQueue<int, (double F, int Node)>();
        g[0] = 0;
        open.Enqueue(0, (from.DistanceTo(to), 0));
        while (open.TryDequeue(out var u, out _))
        {
            if (closed[u])
                continue;
            closed[u] = true;
            if (u == 1)
                break;
            for (var v = 1; v < nodes.Count; v++)
            {
                if (closed[v])
                    continue;
                var cost = g[u] + nodes[u].DistanceTo(nodes[v]);
                if (cost >= g[v] || !Clear(nodes[u], nodes[v]))
                    continue;
                g[v] = cost;
                previous[v] = u;
                open.Enqueue(v, (cost + nodes[v].DistanceTo(to), v));
            }
        }
        if (previous[1] < 0)
            return null;
        var way = new List<Spot>();
        for (var at = 1; at >= 0; at = previous[at])
            way.Add(nodes[at]);
        way.Reverse();
        return new SpotPath(way, g[1]);
    }

    /// <summary>
    /// P2a: the goal of <paramref name="goals"/> (all must fit) with the shortest way from <paramref name="from"/>, and
    /// that way; ties go to the goal listed first. Null if none can be reached. One search for all of them.
    /// </summary>
    public (int Goal, SpotPath Way)? FindNearest(Spot from, IReadOnlyList<Spot> goals)
    {
        if (goals.Count == 0)
            return null;
        var nodes = new List<Spot> { from };
        nodes.AddRange(goals);
        nodes.AddRange(_corners.Where(c => c != from && !goals.Contains(c)));
        var g = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
        var previous = Enumerable.Repeat(-1, nodes.Count).ToArray();
        var closed = new bool[nodes.Count];
        var open = new PriorityQueue<int, (double Cost, int Node)>();
        g[0] = 0;
        open.Enqueue(0, (0, 0));
        while (open.TryDequeue(out var u, out _))
        {
            if (closed[u])
                continue;
            closed[u] = true;
            if (u >= 1 && u <= goals.Count)
            {
                var way = new List<Spot>();
                for (var at = u; at >= 0; at = previous[at])
                    way.Add(nodes[at]);
                way.Reverse();
                return (u - 1, new SpotPath(way, g[u]));
            }
            for (var v = 1; v < nodes.Count; v++)
            {
                if (closed[v])
                    continue;
                var cost = g[u] + nodes[u].DistanceTo(nodes[v]);
                if (cost >= g[v] || !Clear(nodes[u], nodes[v]))
                    continue;
                g[v] = cost;
                previous[v] = u;
                open.Enqueue(v, (cost, v));
            }
        }
        return null;
    }

    /// <summary>
    /// P1b: nothing solid stands on the straight line between two points: no square one cannot stand in, no thin wall, no
    /// closed door or window (P2b: a blow or a shot passes an open window, a body never). Touching a corner counts as
    /// blocked, as two squares meeting at a corner do not let one reach across. The line of a blow and of a shot.
    /// </summary>
    public static bool LineClear(GridMap map, Spot a, Spot b)
    {
        foreach (var edge in map.Boundaries.Where(e => e.Kind switch
                 {
                     MapEdgeKind.Door => map.IsClosedDoor(e.DoorAt!.Value),
                     MapEdgeKind.Window => !map.IsOpenWindow(e.A, e.B), // P2b: a blow or a shot passes an open window
                     _ => true,
                 }))
        {
            var (p, q) = Border(edge.A, edge.B);
            if (Intersect(a, b, p, q))
                return false;
        }
        int x0 = Math.Min(a.Cell.X, b.Cell.X) - 1, x1 = Math.Max(a.Cell.X, b.Cell.X) + 1;
        int y0 = Math.Min(a.Cell.Y, b.Cell.Y) - 1, y1 = Math.Max(a.Cell.Y, b.Cell.Y) + 1;
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var cell = new GridPos(x, y);
            var solid = !map.InBounds(cell) || !map.IsWalkable(cell) || map.IsClosedDoor(cell) && !map.IsEdgeDoor(cell);
            if (solid && SegmentBox(a, b, (x * Spot.PerSquare, y * Spot.PerSquare, (x + 1) * Spot.PerSquare, (y + 1) * Spot.PerSquare)) == 0)
                return false;
        }
        return true;
    }

    /// <summary>A body can go straight from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private bool Clear(Spot a, Spot b)
    {
        double need = BodyRadius - Slack;
        int minX = Math.Min(a.X, b.X) - BodyRadius * 2, maxX = Math.Max(a.X, b.X) + BodyRadius * 2;
        int minY = Math.Min(a.Y, b.Y) - BodyRadius * 2, maxY = Math.Max(a.Y, b.Y) + BodyRadius * 2;
        if (Math.Min(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)) < need || Math.Max(a.X, b.X) > _width - need || Math.Max(a.Y, b.Y) > _height - need)
            return false;
        foreach (var (p, q) in _walls)
            if (Math.Max(p.X, q.X) >= minX && Math.Min(p.X, q.X) <= maxX && Math.Max(p.Y, q.Y) >= minY && Math.Min(p.Y, q.Y) <= maxY
                && SegmentSegment(a, b, p, q) < need)
                return false;
        foreach (var box in _boxes)
            if (box.X1 >= minX && box.X0 <= maxX && box.Y1 >= minY && box.Y0 <= maxY && SegmentBox(a, b, box) < need)
                return false;
        foreach (var c in _blockers)
            if (PointSegment(c, a, b) < 2 * BodyRadius - Slack)
                return false;
        return true;
    }

    // ---------------------------------------------------------------- geometry on integer points

    private static long Cross(Spot o, Spot a, Spot b) => (long)(a.X - o.X) * (b.Y - o.Y) - (long)(a.Y - o.Y) * (b.X - o.X);

    private static bool OnSegment(Spot p, Spot a, Spot b) =>
        Math.Min(a.X, b.X) <= p.X && p.X <= Math.Max(a.X, b.X) && Math.Min(a.Y, b.Y) <= p.Y && p.Y <= Math.Max(a.Y, b.Y);

    private static bool Intersect(Spot a, Spot b, Spot c, Spot d)
    {
        long d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;
        return d1 == 0 && OnSegment(a, c, d) || d2 == 0 && OnSegment(b, c, d) || d3 == 0 && OnSegment(c, a, b) || d4 == 0 && OnSegment(d, a, b);
    }

    internal static double PointSegment(Spot p, Spot a, Spot b)
    {
        long dx = b.X - a.X, dy = b.Y - a.Y;
        var length = dx * dx + dy * dy;
        if (length == 0)
            return p.DistanceTo(a);
        var t = Math.Clamp(((double)(p.X - a.X) * dx + (double)(p.Y - a.Y) * dy) / length, 0, 1);
        double x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static double SegmentSegment(Spot a, Spot b, Spot c, Spot d) =>
        Intersect(a, b, c, d) ? 0
            : Math.Min(Math.Min(PointSegment(a, c, d), PointSegment(b, c, d)), Math.Min(PointSegment(c, a, b), PointSegment(d, a, b)));

    private static double PointBox(Spot p, (int X0, int Y0, int X1, int Y1) box)
    {
        double dx = Math.Max(Math.Max(box.X0 - p.X, 0), p.X - box.X1);
        double dy = Math.Max(Math.Max(box.Y0 - p.Y, 0), p.Y - box.Y1);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double SegmentBox(Spot a, Spot b, (int X0, int Y0, int X1, int Y1) box)
    {
        if (PointBox(a, box) == 0 || PointBox(b, box) == 0)
            return 0;
        Spot c0 = new(box.X0, box.Y0), c1 = new(box.X1, box.Y0), c2 = new(box.X1, box.Y1), c3 = new(box.X0, box.Y1);
        return Math.Min(Math.Min(SegmentSegment(a, b, c0, c1), SegmentSegment(a, b, c1, c2)),
            Math.Min(SegmentSegment(a, b, c2, c3), SegmentSegment(a, b, c3, c0)));
    }
}

/// <summary>P1: a way as its turning points (start and goal included) and its length in inches.</summary>
internal sealed record SpotPath(IReadOnlyList<Spot> Vertices, double Length)
{
    /// <summary>
    /// The points where a walk at <paramref name="speed"/> (feet per 6-second round) is checked: every square entered,
    /// every turning point, and every 5 feet in between; each with the square it is in and the tick it is reached
    /// (rounded up). The exact point of a checkpoint is rounded to the inch and kept inside its square.
    /// </summary>
    public IReadOnlyList<(Spot At, GridPos Cell, long Ticks)> Checkpoints(int speed)
    {
        var inchesPerTick = speed / 30.0; // Speed feet per 6 seconds, 12 inches a foot, 60 ticks a second (P1c)
        var marks = new List<(double Along, double X, double Y, double CellX, double CellY)>();
        var walked = 0.0;
        for (var i = 1; i < Vertices.Count; i++)
        {
            Spot a = Vertices[i - 1], b = Vertices[i];
            var length = a.DistanceTo(b);
            if (length == 0)
                continue;
            double dx = (b.X - a.X) / length, dy = (b.Y - a.Y) / length;
            var along = new SortedSet<double>();
            // Squares entered: every grid line crossed (the cell is judged half an inch past it).
            foreach (var (from, to, d) in new[] { (a.X, b.X, dx), (a.Y, b.Y, dy) })
                if (d != 0)
                    for (var line = Math.Min(from, to) / Spot.PerSquare; line <= Math.Max(from, to) / Spot.PerSquare + 1; line++)
                    {
                        var t = (line * Spot.PerSquare - from) / d;
                        if (t > 0 && t < length)
                            along.Add(t);
                    }
            for (var s = Math.Ceiling(walked / Spot.PerSquare) * Spot.PerSquare; s < walked + length; s += Spot.PerSquare)
                if (s > walked)
                    along.Add(s - walked);
            along.Add(length);
            foreach (var t in along)
            {
                var ahead = Math.Min(length, t + 0.5);
                marks.Add((walked + t, a.X + dx * t, a.Y + dy * t, a.X + dx * ahead, a.Y + dy * ahead));
            }
            walked += length;
        }
        var points = new List<(Spot At, GridPos Cell, long Ticks)>();
        foreach (var (along, x, y, cx, cy) in marks)
        {
            var cell = new GridPos((int)Math.Floor(cx / Spot.PerSquare), (int)Math.Floor(cy / Spot.PerSquare));
            var at = new Spot(
                Math.Clamp((int)Math.Round(x, MidpointRounding.AwayFromZero), cell.X * Spot.PerSquare, cell.X * Spot.PerSquare + Spot.PerSquare - 1),
                Math.Clamp((int)Math.Round(y, MidpointRounding.AwayFromZero), cell.Y * Spot.PerSquare, cell.Y * Spot.PerSquare + Spot.PerSquare - 1));
            var ticks = (long)Math.Ceiling(along / inchesPerTick - 1e-9);
            if (points.Count == 0 || points[^1].At != at || points[^1].Cell != cell)
                points.Add((at, cell, ticks));
        }
        // The walk ends exactly on the goal, in its own square.
        var goal = Vertices[^1];
        if (points.Count > 0)
            points[^1] = (goal, goal.Cell, points[^1].Ticks);
        return points;
    }
}
