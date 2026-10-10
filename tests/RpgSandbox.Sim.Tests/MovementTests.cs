using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>T6a: walking on the village map. 1 square per second at Speed 30; stopping stops the clock.</summary>
public class MovementTests
{
    private static SimulationSession NewVillage() => SimulationSession.Create(MappedVillageScenario.Create());

    private static CommandResult Move(SimulationSession s, int x, int y) =>
        s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(x, y) });

    private static void WalkTo(SimulationSession s, int x, int y)
    {
        var move = Move(s, x, y);
        Assert.True(move.Success, move.Message);
        Assert.Equal(AdvanceOutcome.Completed, s.AdvanceUntilCompleted(move.Action!.Value, Duration.FromHours(1)).Outcome);
    }

    [Fact]
    public void The_village_map_is_given_to_the_player()
    {
        var view = NewVillage().GetPlayerView();

        Assert.NotNull(view.Map);
        Assert.Equal(24, view.Map!.Width);
        Assert.Equal('S', view.Map.Rows[GranaryStoreSquare.Y][GranaryStoreSquare.X]);
        Assert.Equal(Inn, view.Map.Zones['I']);
        Assert.Equal(PlayerStart, view.Position);
        Assert.Equal(Inn, view.Location);
    }

    [Fact]
    public void Walking_takes_one_second_per_square_including_diagonals()
    {
        var s = NewVillage();

        var move = Move(s, InnDoor.X, InnDoor.Y); // (2,2) -> (7,3): 5 squares, diagonals cost 1
        s.AdvanceUntilCompleted(move.Action!.Value, Duration.FromHours(1));

        Assert.Equal(GameTime.Start.Plus(Duration.FromSeconds(5)), s.Now);
        Assert.Equal(InnDoor, s.GetPlayerView().Position);
        Assert.Null(s.GetPlayerView().Location); // the door square is the road
    }

    [Fact]
    public void Leaving_a_place_happens_at_the_square_where_it_happens()
    {
        var s = NewVillage();
        Move(s, 12, 3); // through the door at second 5

        s.Advance(Duration.FromSeconds(4));
        Assert.Equal(Inn, s.GetWorldView().Actor(Player).Location);
        s.Advance(Duration.FromSeconds(1));
        Assert.Null(s.GetWorldView().Actor(Player).Location);
        Assert.Equal(InnDoor, s.GetWorldView().Actor(Player).Position);
    }

    [Fact]
    public void Stopping_keeps_the_last_square_reached_and_nothing_moves_afterwards()
    {
        var s = NewVillage();
        Move(s, 20, 8);
        s.Advance(Duration.FromSeconds(3));
        var reached = s.GetPlayerView().Position;

        var stop = s.Execute(new StopCommand { Actor = s.Player });
        s.Advance(Duration.FromSeconds(30));

        Assert.True(stop.Success);
        Assert.Equal(reached, s.GetPlayerView().Position);
        Assert.Null(s.GetPlayerView().Move);
        Assert.Equal(RejectionReason.NotMoving, s.Execute(new StopCommand { Actor = s.Player }).Rejection);
    }

    [Fact]
    public void A_new_order_replaces_the_walk_from_where_the_walker_is()
    {
        var s = NewVillage();
        var first = Move(s, 20, 8);
        s.Advance(Duration.FromSeconds(2));
        var here = s.GetPlayerView().Position!.Value;

        var second = Move(s, 2, 1);

        Assert.True(second.Success);
        Assert.Equal(AdvanceOutcome.NotPending, s.AdvanceUntilCompleted(first.Action!.Value, Duration.FromSeconds(1)).Outcome);
        Assert.Equal(here, s.GetPlayerView().Move!.From);
    }

    [Fact]
    public void Walls_and_stores_cannot_be_walked_into_and_corners_cannot_be_cut()
    {
        var s = NewVillage();

        Assert.Equal(RejectionReason.Unreachable, Move(s, 0, 0).Rejection);
        Assert.Equal(RejectionReason.Unreachable, Move(s, GranaryStoreSquare.X, GranaryStoreSquare.Y).Rejection);
        Assert.Equal(RejectionReason.AlreadyThere, Move(s, PlayerStart.X, PlayerStart.Y).Rejection);

        // From (6,2) to the door (7,3): the wall at (7,2) forbids the diagonal, so it takes two steps.
        WalkTo(s, 6, 2);
        var move = Move(s, InnDoor.X, InnDoor.Y);
        Assert.Equal(new[] { new GridPos(6, 3), InnDoor }, s.GetPlayerView().Move!.Path.ToArray());
        s.AdvanceUntilCompleted(move.Action!.Value, Duration.FromHours(1));
    }

    [Fact]
    public void Paths_are_deterministic()
    {
        var a = NewVillage();
        var b = NewVillage();
        Move(a, 20, 9);
        Move(b, 20, 9);

        Assert.Equal(a.GetPlayerView().Move!.Path, b.GetPlayerView().Move!.Path);
    }

    [Fact]
    public void Speed_comes_from_the_sheet()
    {
        var scenario = new ScenarioBuilder()
            .AddArea("v", "V").AddLocation("p", "P", "v")
            .AddMap("v", new[] { "PPPPPPPP" }, new Dictionary<char, string> { ['P'] = "p" })
            .AddActor("me", "Me", "p", isPlayer: true, sheet: Sheets.Paladin() with { Speed = 25 }, at: (0, 0))
            .Build();
        var s = SimulationSession.Create(scenario);

        var move = Move(s, 5, 0); // 5 squares at 25 ft/round: 5 squares per 6 s
        s.AdvanceUntilCompleted(move.Action!.Value, Duration.FromHours(1));

        Assert.Equal(GameTime.Start.Plus(Duration.FromSeconds(6)), s.Now);
    }

    // ---------------------------------------------------------------- reach and talk

    [Fact]
    public void A_store_is_used_from_an_adjacent_square()
    {
        var s = NewVillage();
        var far = s.Execute(new DepositFoodCommand { Actor = s.Player, Store = GranaryStore, Amount = 1 });
        Assert.Equal(RejectionReason.OutOfReach, far.Rejection);

        WalkTo(s, GranaryStoreSquare.X - 1, GranaryStoreSquare.Y);
        var near = s.Execute(new DepositFoodCommand { Actor = s.Player, Store = GranaryStore, Amount = 4 });
        s.AdvanceUntilCompleted(near.Action!.Value, Duration.FromHours(1));

        Assert.Equal(44, s.GetWorldView().Store(GranaryStore).Food);
    }

    [Fact]
    public void Talking_takes_being_next_to_someone()
    {
        var s = NewVillage();
        Assert.DoesNotContain(s.GetPlayerView().PeopleHere, p => p.Id == Guard); // same room, three squares away

        WalkTo(s, 4, 2);

        Assert.Contains(s.GetPlayerView().PeopleHere, p => p.Id == Guard);
    }

    [Fact]
    public void Equal_coordinates_in_different_maps_do_not_put_a_store_in_reach()
    {
        var scenario = new ScenarioBuilder()
            .AddArea("a", "A").AddArea("b", "B")
            .AddLocation("a_place", "AP", "a").AddLocation("b_place", "BP", "b")
            .AddMap("a", new[] { "AA" }, new Dictionary<char, string> { ['A'] = "a_place" })
            .AddMap("b", new[] { "BB" }, new Dictionary<char, string> { ['B'] = "b_place" })
            .AddActor("player", "P", "a_place", isPlayer: true, food: 1, at: (0, 0))
            .AddStore("remote", "Remote", "b_place", 0, at: (1, 0))
            .Build();
        var session = SimulationSession.Create(scenario);

        var result = session.Execute(new DepositFoodCommand { Actor = session.Player, Store = new StoreId("remote"), Amount = 1 });

        Assert.Equal(RejectionReason.OutOfReach, result.Rejection);
        Assert.Equal(1, session.GetPlayerView().Food);
        Assert.Equal(0, session.GetWorldView().Stores.Single().Food);
    }

    [Fact]
    public void A_blocked_corner_cannot_be_used_to_reach_a_store()
    {
        var scenario = new ScenarioBuilder()
            .AddArea("a", "A")
            .AddLocation("start", "Start", "a").AddLocation("depot", "Depot", "a")
            .AddMap("a", new[] { "A#", "#G" }, new Dictionary<char, string> { ['A'] = "start", ['G'] = "depot" })
            .AddActor("player", "P", "start", isPlayer: true, food: 1, at: (0, 0))
            .AddStore("store", "Store", "depot", 0, at: (1, 1))
            .Build();
        var session = SimulationSession.Create(scenario);

        var result = session.Execute(new DepositFoodCommand { Actor = session.Player, Store = new StoreId("store"), Amount = 1 });

        Assert.Equal(RejectionReason.OutOfReach, result.Rejection);
    }

    // ---------------------------------------------------------------- time and saves

    [Fact]
    public void One_long_advance_equals_many_short_ones_while_walking()
    {
        var single = NewVillage();
        var stepped = NewVillage();
        Move(single, 20, 9);
        Move(stepped, 20, 9);

        single.Advance(Duration.FromSeconds(13));
        for (var i = 0; i < 13; i++)
            stepped.Advance(Duration.FromSeconds(1));

        Assert.Equal(single.SaveToString(), stepped.SaveToString());
    }

    [Theory]
    [InlineData(3)]  // inside the inn
    [InlineData(5)]  // on the door square, the instant the place changes
    [InlineData(11)] // on the road
    public void A_reload_mid_walk_ends_like_continuous_play(int seconds)
    {
        var continuous = NewVillage();
        var reloaded = NewVillage();
        Move(continuous, 16, 5);
        Move(reloaded, 16, 5);
        reloaded.Advance(Duration.FromSeconds(seconds));
        reloaded = reloaded.SaveAndReload();

        continuous.Advance(Duration.FromMinutes(2));
        reloaded.AdvanceTo(continuous.Now);

        Assert.Equal(continuous.SaveToString(), reloaded.SaveToString());
        Assert.Equal(new GridPos(16, 5), reloaded.GetPlayerView().Position);
        Assert.Equal(Granary, reloaded.GetPlayerView().Location);
    }

    // ---------------------------------------------------------------- scenario validation

    [Fact]
    public void Mapped_scenarios_reject_misplaced_things()
    {
        ScenarioBuilder Base() => new ScenarioBuilder()
            .AddArea("v", "V").AddLocation("p", "P", "v")
            .AddMap("v", new[] { "#PP", "..P" }, new Dictionary<char, string> { ['P'] = "p" });

        Assert.Throws<InvalidOperationException>(() => Base().AddActor("me", "Me", "p", isPlayer: true, at: (0, 0)).Build()); // wall
        Assert.Throws<InvalidOperationException>(() => Base().AddActor("me", "Me", "p", isPlayer: true, at: (0, 1)).Build()); // road, not "p"
        Assert.Throws<InvalidOperationException>(() => Base().AddActor("me", "Me", "p", isPlayer: true).Build());             // no square
        Assert.Throws<InvalidOperationException>(() => new ScenarioBuilder()
            .AddArea("v", "V").AddLocation("p", "P", "v")
            .AddMap("v", new[] { "P?" }, new Dictionary<char, string> { ['P'] = "p" })
            .AddActor("me", "Me", "p", isPlayer: true, at: (0, 0)).Build()); // unknown character
    }
}
