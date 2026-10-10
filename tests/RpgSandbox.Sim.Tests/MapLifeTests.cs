using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>T6c: the world's life on the map (waypoints on every square, exits, posts, store access).</summary>
public class MapLifeTests
{
    private static readonly ActorId Bearer = new("bearer");

    /// <summary>
    /// Night in a dark yard 21 squares wide. The player hides at (10,0); a villager with a lit torch walks along the
    /// bottom row from (0,2) to (20,2): far from the player at both ends, two squares away halfway.
    /// </summary>
    private static SimulationSession TorchPassesBy()
    {
        var scenario = new ScenarioBuilder()
            .AddArea("a", "A")
            .AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat(new string('Y', 21), 3).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Protagonista", "yard", isPlayer: true, sheet: Sheets.Raider(), at: (9, 0))
            .AddActor(Bearer.Value, "Contadino", "yard", sheet: Sheets.Farmer(), at: (0, 2), torches: 1)
            .Build();
        var s = SimulationSession.Create(scenario);
        s.AdvanceTo(At(1, 2));
        var hide = s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(10, 0), Stealthy = true });
        s.AdvanceUntilCompleted(hide.Action!.Value, Duration.FromMinutes(1));
        Assert.NotNull(s.GetPlayerView().Sneaking);
        Assert.True(s.Execute(new TorchCommand { Actor = Bearer, Lit = true }).Success);
        return s;
    }

    [Fact]
    public void A_torch_bearer_walking_past_finds_the_hidden_player_halfway()
    {
        var s = TorchPassesBy();
        Assert.NotNull(s.GetPlayerView().Sneaking); // the torch is still far: 50 ft of darkness between them

        var walk = s.Execute(new MoveCommand { Actor = Bearer, To = new GridPos(20, 2) });
        Assert.True(walk.Success, walk.Message);
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(5));

        Assert.Null(s.GetPlayerView().Sneaking); // found on the way, though the player never moved
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "SneakDiscovered" && f.Description.Contains("Contadino"));
    }

    [Fact]
    public void One_long_advance_equals_many_short_ones()
    {
        var once = TorchPassesBy();
        var steps = TorchPassesBy();
        once.Execute(new MoveCommand { Actor = Bearer, To = new GridPos(20, 2) });
        steps.Execute(new MoveCommand { Actor = Bearer, To = new GridPos(20, 2) });

        once.Advance(Duration.FromSeconds(30));
        for (var i = 0; i < 30; i++)
            steps.Advance(Duration.FromSeconds(1));

        Assert.Equal(once.SaveToString(), steps.SaveToString());
    }

    // ---------------------------------------------------------------- exits, posts, store access

    // A village yard (Y) with a road (E, "Strada per il bosco") on its east edge; the woods are not mapped.
    private static readonly string[] VillageRows =
    {
        "#######",
        "#YYYYEE",
        "#YYYYEE",
        "#######",
    };

    private static ScenarioBuilder Village(bool withExit = true)
    {
        var b = new ScenarioBuilder()
            .AddArea("village", "Villaggio")
            .AddArea("woods", "Bosco")
            .AddLocation("yard", "Cortile", "village")
            .AddLocation("road", "Strada per il bosco", "village")
            .AddLocation("camp", "Campo", "woods")
            .AddRoute("road", "camp", Duration.FromMinutes(10))
            .AddMap("village", VillageRows, new Dictionary<char, string> { ['Y'] = "yard", ['E'] = "road" })
            .AddFaction("village", "Villaggio", homeStoreId: "store", authorityId: "guard")
            .AddStore("store", "Scorte", "yard", 10, "village", at: (2, 1), access: new[] { (2, 2) })
            .AddActor("player", "Protagonista", "yard", isPlayer: true, food: 3, sheet: Sheets.Paladin(), at: (1, 2))
            .AddActor("guard", "Guardia", "yard", factionId: "village", sheet: Sheets.VillageGuard(), at: (4, 1));
        if (withExit)
            b.AddExit("village", "road", (6, 1)).AddPost("village", "yard", PostKind.Guard, (3, 2));
        return b;
    }

    private static void Walk(SimulationSession s, GridPos to)
    {
        var walk = s.Execute(new MoveCommand { Actor = s.Player, To = to });
        Assert.True(walk.Success, walk.Message);
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(5));
    }

    [Fact]
    public void A_journey_leaves_the_map_from_the_exit_and_comes_back_onto_it()
    {
        var s = SimulationSession.Create(Village().Build());
        var exits = s.GetPlayerView().Map!.Exits;
        var exit = Assert.Single(exits);
        Assert.Equal((new LocationId("road"), new GridPos(6, 1), new LocationId("camp")), (exit.Location, exit.At, exit.Routes.Single().To));

        Walk(s, new GridPos(5, 2)); // on the road, but not on its exit square
        Assert.Equal(RejectionReason.NotAtExit, s.Execute(new TravelCommand { Actor = s.Player, Destination = new LocationId("camp") }).Rejection);

        Walk(s, new GridPos(6, 1));
        var away = s.Execute(new TravelCommand { Actor = s.Player, Destination = new LocationId("camp") });
        Assert.True(away.Success, away.Message);
        Assert.Null(s.GetWorldView().Actor(s.Player).Position); // off the map on the way
        s.AdvanceUntilCompleted(away.Action!.Value, Duration.FromHours(1));
        Assert.Equal(new LocationId("camp"), s.GetPlayerView().Location);

        var back = s.Execute(new TravelCommand { Actor = s.Player, Destination = new LocationId("road") });
        s.AdvanceUntilCompleted(back.Action!.Value, Duration.FromHours(1));
        Assert.Equal((new LocationId("road"), new GridPos(6, 1)), (s.GetPlayerView().Location!.Value, s.GetPlayerView().Position!.Value));
        Assert.NotNull(s.GetPlayerView().Map); // back on the village map
    }

    [Fact]
    public void A_journey_in_progress_survives_save_and_load()
    {
        var s = SimulationSession.Create(Village().Build());
        Walk(s, new GridPos(6, 1));
        var away = s.Execute(new TravelCommand { Actor = s.Player, Destination = new LocationId("camp") });
        s.AdvanceUntilCompleted(away.Action!.Value, Duration.FromHours(1));
        var back = s.Execute(new TravelCommand { Actor = s.Player, Destination = new LocationId("road") });
        s.Advance(Duration.FromMinutes(4));

        var loaded = s.SaveAndReload();
        s.AdvanceUntilCompleted(back.Action!.Value, Duration.FromHours(1));
        loaded.AdvanceUntilCompleted(back.Action!.Value, Duration.FromHours(1));

        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(new GridPos(6, 1), loaded.GetPlayerView().Position);
    }

    [Fact]
    public void A_store_with_declared_access_is_used_only_from_there()
    {
        var s = SimulationSession.Create(Village().Build());
        Walk(s, new GridPos(1, 1)); // next to the store, but on its blind side
        var deposit = new DepositFoodCommand { Actor = s.Player, Store = new StoreId("store"), Amount = 1 };
        Assert.Equal(RejectionReason.OutOfReach, s.Execute(deposit).Rejection);

        Walk(s, new GridPos(2, 2)); // the access square
        Assert.True(s.Execute(deposit).Success);
        Assert.Equal(new[] { new GridPos(2, 2) }, s.GetWorldView().Store(new StoreId("store")).Access);
    }

    [Fact]
    public void Exits_posts_and_access_are_validated()
    {
        Assert.Contains("no exit", Assert.Throws<InvalidOperationException>(() => Village(withExit: false).Build()).Message);
        Assert.Throws<InvalidOperationException>(() => Village(withExit: false).AddExit("village", "road", (2, 2)).Build()); // not on the road
        Assert.Throws<InvalidOperationException>(() => Village().AddPost("village", "road", PostKind.Work, (1, 1)).Build()); // not on the road
        Assert.Throws<InvalidOperationException>(() => new ScenarioBuilder()
            .AddArea("a", "A").AddLocation("y", "Y", "a")
            .AddMap("a", new[] { "YYYY" }, new Dictionary<char, string> { ['Y'] = "y" })
            .AddStore("s", "S", "y", 1, at: (0, 0), access: new[] { (3, 0) }) // not next to the store
            .AddActor("p", "P", "y", isPlayer: true, at: (1, 0))
            .Build());
    }

    [Fact]
    public void Exits_posts_and_access_survive_save_and_load()
    {
        var s = SimulationSession.Create(Village().Build());
        var map = s.SaveAndReload().Engine.World.Maps[new AreaId("village")];

        Assert.Equal(new GridPos(6, 1), map.Exits[new LocationId("road")]);
        Assert.Equal(new GridPos(3, 2), map.Posts[(new LocationId("yard"), PostKind.Guard)]);
    }
}
