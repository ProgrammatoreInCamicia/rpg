using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>P1a: walking to an exact point (free movement prototype, outside fights), in the prototype house.</summary>
public class FreeMovementTests
{
    private static readonly ActorId Farmer = MappedVillageScenario.Ids.Farmer;

    /// <summary>The house at 13:00. The player stands at the centre of (9,4), in the yard east of the closed door (7,4)|(8,4).</summary>
    private static SimulationSession House()
    {
        var s = SimulationSession.Create(InteriorPrototypeScenario.Create());
        s.AdvanceTo(At(1, 13));
        return s;
    }

    private static Spot In(int x, int y, int dx = 30, int dy = 30) => new(x * Spot.PerSquare + dx, y * Spot.PerSquare + dy);

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static CommandResult Walk(SimulationSession s, Spot to, bool finish = true)
    {
        var walk = s.Execute(new MoveToSpotCommand { Actor = s.Player, To = to });
        if (walk.Success && finish)
            Assert.Equal(AdvanceOutcome.Completed, s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(5)).Outcome);
        return walk;
    }

    [Fact]
    public void A_free_walk_ends_exactly_on_the_point_and_takes_its_straight_length()
    {
        var s = House();
        var to = new Spot(650, 470); // inside (10,7), off its centre
        var start = s.Now;

        var walk = Walk(s, to);

        Assert.True(walk.Success, walk.Message);
        Assert.Equal(to, s.GetPlayerView().Spot);
        Assert.Equal(new GridPos(10, 7), s.GetPlayerView().Position);
        var length = new Spot(570, 270).DistanceTo(to); // 215.4 inches, at an inch a tick (Speed 30, P1c)
        Assert.Equal((long)Math.Ceiling(length), s.Now.Since(start).Ticks);
    }

    [Fact]
    public void Into_the_house_the_way_goes_through_the_door_and_opens_it_and_round_the_inner_corner()
    {
        var s = House();
        var map = s.Engine.World.Maps[InteriorPrototypeScenario.Area];
        var to = In(4, 3, 25, 25); // the west room's far corner: no straight line through both doorways

        var preview = s.PreviewMoveToSpot(s.Player, to);
        Assert.True(preview.Valid, $"{preview.Reason} {preview.Message}");
        Walk(s, to);

        Assert.Equal(to, s.GetPlayerView().Spot);
        Assert.False(map.IsClosedDoor(InteriorPrototypeScenario.Door));
        // It went in by the door, not through a wall, and turned at least once.
        Assert.True(preview.Way.Count >= 3);
        Assert.Contains(s.GetPlayerView().RecentEvents, f => f.Kind == "DoorOpened");
        var straight = new Spot(570, 270).DistanceTo(to) / Spot.PerFoot;
        Assert.True(preview.Feet > straight);
        // The body (18 inches across from its centre) never grazes the jambs of the two doorways.
        var jambs = new[] { new Spot(480, 240), new Spot(480, 300), new Spot(360, 240), new Spot(360, 300) };
        Assert.All(preview.Way.Zip(preview.Way.Skip(1)), leg => Assert.All(jambs,
            j => Assert.True(SpotPlanner.PointSegment(j, leg.First, leg.Second) >= SpotPlanner.BodyRadius - 1)));
    }

    [Fact]
    public void A_window_lets_sight_through_but_not_the_body_so_the_way_goes_round_by_the_door()
    {
        var s = House();
        Of(s, s.Player).Position = InteriorPrototypeScenario.WindowOutside;

        // Just past the window, inside: 5 feet away as the crow flies, but the way in is by the door.
        var preview = s.PreviewMoveToSpot(s.Player, Spot.CenterOf(InteriorPrototypeScenario.WindowInside));

        Assert.True(preview.Valid, preview.Message);
        Assert.True(preview.Feet > 15, $"{preview.Feet} feet");
    }

    [Theory]
    [InlineData(630, 570)] // inside the wall square (10,9)
    [InlineData(485, 200)] // 5 inches from the thin wall (7,3)|(8,3), on the yard side
    [InlineData(-30, 100)] // off the map
    [InlineData(70, 80)]   // 10 inches from the wall square (0,1)
    public void A_point_where_the_body_does_not_fit_is_refused(int x, int y)
    {
        var s = House();

        var result = Walk(s, new Spot(x, y), finish: false);

        Assert.Equal(RejectionReason.NoRoom, result.Rejection);
        Assert.Null(s.GetPlayerView().Move);
    }

    [Fact]
    public void Nobody_stops_on_someone_else_but_one_may_walk_past_a_bystander()
    {
        var s = House();
        var farmer = Of(s, Farmer).Spot!.Value;
        Assert.Equal(RejectionReason.NoRoom, Walk(s, new Spot(farmer.X + 20, farmer.Y), finish: false).Rejection);

        // The farmer stands in the doorway: a bystander can be walked past (SRD: a creature that is not hostile).
        Of(s, Farmer).Position = new GridPos(7, 4);
        var walk = Walk(s, In(6, 4));
        Assert.True(walk.Success, walk.Message);
        Assert.Equal(In(6, 4), s.GetPlayerView().Spot);
    }

    [Fact]
    public void A_hostile_creature_in_the_doorway_bars_the_way_in()
    {
        var s = House();
        Of(s, Farmer).Position = new GridPos(7, 4);
        Of(s, Farmer).FoughtWith.Add(s.Player);
        Of(s, s.Player).FoughtWith.Add(Farmer);

        Assert.Equal(RejectionReason.Unreachable, Walk(s, In(6, 4), finish: false).Rejection);
    }

    [Fact]
    public void The_preview_is_the_walk_and_the_same_question_always_gets_the_same_way()
    {
        var a = House();
        var b = House();
        var to = In(5, 4, 40, 20);

        var preview = a.PreviewMoveToSpot(a.Player, to);
        Assert.Equal(preview.Way, b.PreviewMoveToSpot(b.Player, to).Way);
        var walk = Walk(a, to, finish: false);
        var move = a.GetPlayerView().Move!;

        Assert.Equal(preview.Way[0], move.FromSpot);
        Assert.Equal(to, move.Spots![^1]);
        Assert.Equal(preview.Ticks, walk.CompletesAt!.Value.Since(a.Now).Ticks);
        Assert.Equal(preview.Ticks, move.Ticks![^1]);
        // Every step enters the next square or stays in the same one: the senses follow it square by square.
        var cells = new[] { move.From }.Concat(move.Path).ToList();
        Assert.All(cells.Zip(cells.Skip(1)), x => Assert.True(x.First.StepsTo(x.Second) <= 1));
        Assert.All(move.Spots.Zip(move.Path), x => Assert.Equal(x.Second, x.First.Cell));
    }

    [Fact]
    public void A_diagonal_costs_its_length_never_more_than_squares_with_diagonals_weighted()
    {
        var s = House();
        // One square diagonally: about 7.1 feet, not the 5 feet of the grid rule.
        Assert.Equal(7.1, s.PreviewMoveToSpot(s.Player, In(10, 5)).Feet);
        // Across the open yard, centre to centre: the straight line, never longer than the weighted squares (√2 a diagonal).
        foreach (var (x, y) in new[] { (11, 7), (12, 8), (9, 8), (12, 1), (8, 7) })
        {
            var feet = s.PreviewMoveToSpot(s.Player, In(x, y)).Feet;
            int dx = Math.Abs(x - 9), dy = Math.Abs(y - 4);
            var weighted = 5 * (Math.Max(dx, dy) - Math.Min(dx, dy) + Math.Sqrt(2) * Math.Min(dx, dy));
            Assert.True(feet <= Math.Ceiling(weighted * 10) / 10, $"{feet} > {weighted} to ({x},{y})");
            Assert.Equal(Math.Ceiling(5 * Math.Sqrt(dx * dx + dy * dy) * 10 - 1e-9) / 10, feet);
        }
    }

    [Fact]
    public void Stopping_mid_walk_keeps_the_exact_point_and_the_next_walk_sets_off_from_there()
    {
        var s = House();
        Walk(s, new Spot(700, 500), finish: false);
        s.Advance(Duration.FromSeconds(2));
        var reached = s.GetPlayerView().Spot!.Value;
        Assert.NotEqual(new Spot(570, 270), reached);

        Assert.True(s.Execute(new StopCommand { Actor = s.Player }).Success);
        Assert.Equal(reached, s.GetPlayerView().Spot);
        Assert.Equal(reached, s.PreviewMoveToSpot(s.Player, In(11, 2)).Way[0]);

        // A walk from square to square puts it back on a centre.
        var grid = s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(11, 2) });
        s.AdvanceUntilCompleted(grid.Action!.Value, Duration.FromMinutes(1));
        Assert.Equal(Spot.CenterOf(new GridPos(11, 2)), s.GetPlayerView().Spot);
    }

    [Fact]
    public void A_free_walk_survives_a_save_mid_way_and_ends_where_it_would_have()
    {
        var a = House();
        var to = In(4, 4, 25, 35);
        Walk(a, to, finish: false);
        a.Advance(Duration.FromSeconds(3));
        var b = a.SaveAndReload();

        a.Advance(Duration.FromMinutes(1));
        b.Advance(Duration.FromMinutes(1));

        Assert.Equal(to, b.GetPlayerView().Spot);
        Assert.Equal(a.SaveToString(), b.SaveToString());
        Assert.Equal(to, b.SaveAndReload().GetPlayerView().Spot);
    }

    [Fact]
    public void A_save_cannot_put_an_exact_point_outside_its_square_or_break_a_free_walk()
    {
        var s = House();
        Walk(s, new Spot(650, 470));
        var data = JsonNode.Parse(s.SaveToString())!;
        var player = data["Actors"]!.AsArray().Single(a => (bool)a!["IsPlayer"]!)!;
        Assert.Equal(650, (int)player["Spot"]!["X"]!);
        player["Spot"]!["X"] = 700; // (11,7), not its square (10,7)
        Assert.Contains("fuori dalla sua casella", LoadFromString(data.ToJsonString()).Error);

        s = House();
        Walk(s, new Spot(700, 500), finish: false);
        data = JsonNode.Parse(s.SaveToString())!;
        var action = data["Actors"]!.AsArray().Single(a => (bool)a!["IsPlayer"]!)!["Action"]!;
        action["Ticks"]!.AsArray().RemoveAt(0);
        Assert.Contains("cammino libero", LoadFromString(data.ToJsonString()).Error);
    }

    [Fact]
    public void In_a_fight_a_free_move_is_no_walk_in_time_and_nobody_sneaks()
    {
        var s = House();
        Of(s, Farmer).Position = new GridPos(8, 4);
        Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Farmer, Attack = "Spada lunga" }).Success);
        Assert.NotNull(s.GetPlayerView().Combat);

        // P1b: a move within the turn (FreeCombatTests), never a timed walk; sneaking is not for fights.
        var sneak = s.Execute(new MoveToSpotCommand { Actor = s.Player, To = new Spot(650, 470), Stealthy = true });
        Assert.Equal(RejectionReason.InCombat, sneak.Rejection);
        Assert.False(s.PreviewMoveToSpot(s.Player, new Spot(650, 470), stealthy: true).Valid);
        Walk(s, new Spot(650, 470), finish: false);
        Assert.Null(s.GetPlayerView().Move);
    }
}
