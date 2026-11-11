using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>
/// Playtest of 11 November: a single open area of a chosen size to feel how big a place should be (Baldur's Gate's outdoor
/// areas are roughly 80×60 to 100×100 of our squares). Not a scenario of the game: open country with rocks and reeds, a
/// few cottages with a door, a hearth and someone at home, and a brazier by each door, with the same density of things
/// per square at every size, so two sizes differ only in extent. Generated from a fixed seed: the same size is the same
/// map every time.
/// </summary>
public static class ScaleTestScenario
{
    public static readonly AreaId Area = new("scale_test");
    public static readonly LocationId Country = new("scale_country");
    public static readonly ActorId Player = new("scale_player");

    /// <summary>The sizes offered to the playtest; any size from 40×30 up to 192×128 builds.</summary>
    public static readonly (int Width, int Height)[] Sizes = { (96, 64), (128, 96) };

    /// <summary>Squares of country per cottage, and the spacing of the rock and reed clusters.</summary>
    private const int SquaresPerCottage = 1_200;
    private const int ClusterSpacing = 10;

    public static GridPos Start(int height) => new(4, height / 2);

    public static SimulationSession CreateSession(int width, int height)
    {
        if (width is < 40 or > 192 || height is < 30 or > 128)
            throw new ArgumentOutOfRangeException(nameof(width), "La mappa di prova va da 40×30 a 192×128.");
        var random = new Random(width * 1_000 + height);
        var cells = new char[height, width];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            cells[y, x] = x == 0 || y == 0 || x == width - 1 || y == height - 1 ? '#' : 'G';

        var start = Start(height);
        var cottages = new List<(int Left, int Top, int Right, int Bottom)>();
        var wanted = Math.Max(2, width * height / SquaresPerCottage);
        for (var attempt = 0; cottages.Count < wanted && attempt < 2_000; attempt++)
        {
            var w = random.Next(6, 10);
            var h = random.Next(5, 8);
            var left = random.Next(4, width - w - 4);
            var top = random.Next(4, height - h - 5);
            var right = left + w - 1;
            var bottom = top + h - 1;
            if (left < start.X + 8 || cottages.Any(c => left <= c.Right + 6 && right >= c.Left - 6 && top <= c.Bottom + 6 && bottom >= c.Top - 6))
                continue;
            cottages.Add((left, top, right, bottom));
        }
        bool NearCottage(int x, int y, int margin) =>
            cottages.Any(c => x >= c.Left - margin && x <= c.Right + margin && y >= c.Top - margin && y <= c.Bottom + margin);

        // Rocks and reeds: one cluster about every ClusterSpacing squares, nudged at random, never on a cottage or its yard.
        for (var gy = ClusterSpacing / 2; gy < height - 3; gy += ClusterSpacing)
        for (var gx = ClusterSpacing / 2; gx < width - 3; gx += ClusterSpacing)
        {
            var cx = gx + random.Next(-3, 4);
            var cy = gy + random.Next(-3, 4);
            var cw = random.Next(2, 5);
            var ch = random.Next(1, 4);
            if (cx < 2 || cy < 2 || cx + cw > width - 3 || cy + ch > height - 3
                || Math.Abs(cx - start.X) < 6 && Math.Abs(cy - start.Y) < 6)
                continue;
            if (Enumerable.Range(cx, cw).Any(x => Enumerable.Range(cy, ch).Any(y => NearCottage(x, y, 2))))
                continue;
            for (var y = cy; y < cy + ch; y++)
            for (var x = cx; x < cx + cw; x++)
                cells[y, x] = '#';
        }

        var zones = new Dictionary<char, string> { ['G'] = Country.Value };
        for (var i = 0; i < cottages.Count; i++)
        {
            var (left, top, right, bottom) = cottages[i];
            var zone = (char)('a' + i);
            zones[zone] = $"scale_cottage_{i + 1}";
            for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                cells[y, x] = zone;
        }

        var rows = Enumerable.Range(0, height).Select(y => new string(Enumerable.Range(0, width).Select(x => cells[y, x]).ToArray())).ToArray();
        var b = new ScenarioBuilder().WithSeed((ulong)(width * 1_000 + height))
            .AddArea(Area.Value, $"Prova {width}×{height}")
            .AddLocation(Country.Value, "Campagna", Area.Value);
        for (var i = 0; i < cottages.Count; i++)
            b.AddLocation($"scale_cottage_{i + 1}", $"Casolare {i + 1}", Area.Value);
        b.AddMap(Area.Value, rows, zones).AddExploration(Area.Value);
        for (var i = 0; i < cottages.Count; i++)
        {
            var (left, top, right, bottom) = cottages[i];
            var location = $"scale_cottage_{i + 1}";
            var doorX = (left + right) / 2;
            for (var x = left; x <= right; x++)
            {
                b.AddEdge(Area.Value, (x, top - 1), (x, top), MapEdgeKind.Wall);
                b.AddEdge(Area.Value, (x, bottom), (x, bottom + 1), x == doorX ? MapEdgeKind.Door : MapEdgeKind.Wall,
                    doorAt: x == doorX ? (doorX, bottom) : null);
            }
            for (var y = top; y <= bottom; y++)
            {
                b.AddEdge(Area.Value, (left - 1, y), (left, y), MapEdgeKind.Wall);
                b.AddEdge(Area.Value, (right, y), (right + 1, y), MapEdgeKind.Wall);
            }
            b.AddInterior(Area.Value, location)
                .AddLight(Area.Value, (left, top), 10, 20).AddObstacle(Area.Value, (left, top)) // the hearth
                .AddLight(Area.Value, (doorX + 2, bottom + 2), 15, 30).AddObstacle(Area.Value, (doorX + 2, bottom + 2)) // a brazier
                .AddPost(Area.Value, location, PostKind.Home, (right - 1, top + 1))
                .AddActor($"scale_dweller_{i + 1}", $"Abitante {i + 1}", location, at: (right - 1, top + 1), build: "farmer");
        }
        b.AddActor(Player.Value, "Protagonista", Country.Value, isPlayer: true, at: (start.X, start.Y), build: "paladin", torches: 3,
            items: new[] { ("chain-mail", 1), ("shield", 1), ("longsword", 1) });

        var session = SimulationSession.Create(b.Build());
        session.Advance(Duration.FromHours(10)); // mid-morning: the whole country in daylight
        session.FacePlayer(1, 0);
        return session;
    }
}
