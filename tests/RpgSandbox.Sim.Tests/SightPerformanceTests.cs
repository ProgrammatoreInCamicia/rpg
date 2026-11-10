using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Sim.Tests;

/// <summary>
/// Region P3: the player's view on large maps is read faster, with the same answers. Lines of sight come from grids
/// and a field kept for the square looked from; the traced lines kept in memory are bounded; the map's layout is built once.
/// </summary>
public class SightPerformanceTests
{
    /// <summary>A random map: walls, edges (walls, windows, doors on edges) and doors on squares, some open.</summary>
    private static GridMap RandomMap(int seed, int width = 12, int height = 9)
    {
        var random = new Random(seed);
        var cells = new char[height][];
        for (var y = 0; y < height; y++)
        {
            cells[y] = new char[width];
            for (var x = 0; x < width; x++)
                cells[y][x] = random.NextDouble() < 0.15 ? '#' : '.';
        }
        bool Free(GridPos p) => cells[p.Y][p.X] != '#';
        var edges = new List<MapEdgeView>();
        var used = new HashSet<(GridPos, GridPos)>();
        var doorCells = new HashSet<GridPos>();
        for (var i = 0; i < width * height / 4; i++)
        {
            var a = new GridPos(random.Next(width), random.Next(height));
            var b = random.Next(2) == 0 ? new GridPos(a.X + 1, a.Y) : new GridPos(a.X, a.Y + 1);
            if (b.X >= width || b.Y >= height || !Free(a) || !Free(b) || !used.Add((a, b)))
                continue;
            var kind = random.Next(3) switch { 0 => MapEdgeKind.Wall, 1 => MapEdgeKind.Window, _ => MapEdgeKind.Door };
            GridPos? doorAt = null;
            if (kind == MapEdgeKind.Door)
            {
                var at = random.Next(2) == 0 ? a : b;
                if (!doorCells.Add(at))
                    kind = MapEdgeKind.Wall;
                else
                    doorAt = at;
            }
            edges.Add(new MapEdgeView { A = a, B = b, Kind = kind, DoorAt = doorAt });
        }
        var squareDoors = new List<(GridPos, bool)>();
        for (var i = 0; i < 6; i++)
        {
            var at = new GridPos(random.Next(width), random.Next(height));
            if (Free(at) && doorCells.Add(at))
                squareDoors.Add((at, random.Next(2) == 0));
        }
        var edgeDoors = edges.Where(e => e.DoorAt is not null).Select(e => (e.DoorAt!.Value, random.Next(2) == 0));
        return new GridMap(new AreaId("a"), cells.Select(r => new string(r)).ToArray(), new Dictionary<char, LocationId>(),
            Array.Empty<GridPos>(), doors: squareDoors.Concat(edgeDoors).ToList(), edges: edges);
    }

    private static IEnumerable<GridPos> Squares(GridMap map) =>
        Enumerable.Range(0, map.Height).SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new GridPos(x, y)));

    private static void AssertSameSight(GridMap map, int seed)
    {
        foreach (var a in Squares(map))
        foreach (var b in Squares(map))
        {
            var traced = map.HasLineOfSightChecked(a, b);
            Assert.True(traced == map.HasLineOfSight(a, b), $"seed {seed}: {a}→{b} should be {traced}");
            Assert.True(traced == map.HasLineOfSightFrom(a, b), $"seed {seed}: from {a} to {b} should be {traced}");
        }
    }

    [Fact]
    public void Lines_of_sight_read_from_the_grids_are_the_ones_traced_edge_by_edge_also_after_doors_move()
    {
        for (var seed = 1; seed <= 25; seed++)
        {
            var map = RandomMap(seed);
            AssertSameSight(map, seed);
            foreach (var (at, open) in map.Doors.ToList())
                map.SetDoor(at, !open);
            AssertSameSight(map, seed);
        }
    }

    [Fact]
    public void The_lines_of_sight_kept_in_memory_stay_bounded()
    {
        var map = new GridMap(new AreaId("a"), Enumerable.Repeat(new string('.', 30), 30).ToArray(),
            new Dictionary<char, LocationId>(), Array.Empty<GridPos>()) { SightCacheLimit = 1_000 };
        var max = 0;
        foreach (var a in Squares(map).Take(40))
        foreach (var b in Squares(map))
        {
            Assert.True(map.HasLineOfSight(a, b));
            max = Math.Max(max, map.SightCacheCount);
        }
        Assert.InRange(max, 1, 1_000);
    }

    [Fact]
    public void A_look_over_the_whole_map_does_not_fill_the_memory_of_lines()
    {
        // A 60×40 field by day with a hut, only the player on it: every square is lit, so the player looks over all of it.
        var rows = Enumerable.Range(0, 40).Select(y => new string(Enumerable.Range(0, 60).Select(x => x < 3 && y < 3 ? 'H' : 'F').ToArray())).ToArray();
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(1)
            .AddArea("a", "Campo").AddLocation("field", "Campo", "a").AddLocation("hut", "Capanno", "a")
            .AddMap("a", rows, new Dictionary<char, string> { ['H'] = "hut", ['F'] = "field" }).AddInterior("a", "hut")
            .AddActor("p", "Paladino", "field", isPlayer: true, at: (30, 20), build: "paladin").Build());
        s.Advance(Duration.FromHours(12));
        var map = s.Engine.World.Maps[new AreaId("a")];
        var before = map.SightCacheCount;
        for (var i = 0; i < 4; i++)
        {
            s.FacePlayer(i % 2 == 0 ? 1 : -1, i < 2 ? 0 : 1);
            var view = s.GetPlayerView();
            Assert.True(view.MapVisibility!.Sum(r => r.Count(c => c == '2')) > 1_000);
        }
        // The look is kept in the field of the square looked from, not as a line per square (2,400 of them). What is
        // remembered is how daylight gets into the hut: some lines between its squares and those just outside.
        Assert.True(map.SightCacheCount - before < 300, $"{before} → {map.SightCacheCount}");
    }

    [Fact]
    public void The_map_layout_is_built_once_while_doors_still_show_as_they_are()
    {
        var b = new ScenarioBuilder().WithSeed(1)
            .AddArea("a", "Casa").AddLocation("room", "Stanza", "a")
            .AddMap("a", new[] { "RRRR", "RRRR" }, new Dictionary<char, string> { ['R'] = "room" })
            .AddEdge("a", (1, 0), (2, 0), MapEdgeKind.Door, doorAt: (2, 0))
            .AddEdge("a", (1, 1), (2, 1), MapEdgeKind.Wall)
            .AddActor("p", "Paladino", "room", isPlayer: true, at: (1, 0), build: "paladin");
        var s = SimulationSession.Create(b.Build());
        s.Advance(Duration.FromHours(12));
        var first = s.GetPlayerView();
        Assert.False(Assert.Single(first.Doors).Open);

        var opened = s.Execute(new DoorCommand { Actor = s.Player, At = new GridPos(2, 0), Open = true });
        Assert.True(opened.Success, opened.Message);
        s.Advance(Duration.FromMinutes(1));
        var second = s.GetPlayerView();

        Assert.Same(first.Map, second.Map);
        Assert.True(Assert.Single(second.Doors).Open);
        var reloaded = s.SaveAndReload().GetPlayerView();
        Assert.NotSame(second.Map, reloaded.Map); // a loaded world builds its own
        Assert.Equal(second.Map!.Rows, reloaded.Map!.Rows);
    }
}
