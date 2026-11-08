using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>Regional boundaries exercised through commands and views, without editing a running world.</summary>
public class RegionalTravelTests
{
    private static readonly ActorId Courier = new("courier");
    private static readonly LocationId WestGate = new("west-gate"), EastGate = new("east-gate");
    private static readonly GridPos Exit = new(4, 2);

    private static ScenarioBuilder Places(bool westMapped = true, bool eastMapped = true)
    {
        var b = new ScenarioBuilder();
        foreach (var (area, mapped) in new[] { ("west", westMapped), ("east", eastMapped) })
        {
            b.AddArea(area, area)
                .AddLocation(area + "-room", area + " room", area, lit: true)
                .AddLocation(area + "-gate", area + " gate", area, lit: true);
            if (mapped)
                b.AddMap(area, new[] { "#####", "#HHHE", "#HHHE", "#HHH#", "#####" },
                    new Dictionary<char, string> { ['H'] = area + "-room", ['E'] = area + "-gate" })
                    .AddExit(area, area + "-gate", (4, 2))
                    .AddLight(area, (2, 1), 30, 40);
        }
        return b.AddRoute(westMapped ? "west-gate" : "west-room",
            eastMapped ? "east-gate" : "east-room", Duration.FromMinutes(2));
    }

    private static SimulationSession Journey(bool occupant = false) => SimulationSession.Create(Places()
        .AddActor("player", "Player", "west-gate", isPlayer: true, at: (4, 1))
        .AddActor("resident", "Resident", occupant ? "east-gate" : "east-room", at: occupant ? (4, 2) : (1, 1))
        .AddActor("neighbour", "Neighbour", "west-room", at: (1, 1))
        .AddStore("west-stock", "West stock", "west-room", 4, at: (2, 2), access: new[] { (2, 3) })
        .AddStore("east-stock", "East stock", "east-room", 4, at: (2, 2), access: new[] { (2, 3) })
        .Build());

    private static void Complete(SimulationSession s, Command command)
    {
        var result = s.Execute(command);
        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Action);
        Assert.Equal(AdvanceOutcome.Completed,
            s.AdvanceUntilCompleted(result.Action!.Value, Duration.FromMinutes(10)).Outcome);
    }

    private static void Walk(SimulationSession s, GridPos to) => Complete(s, new MoveCommand { Actor = s.Player, To = to });
    private static void Leave(SimulationSession s)
    {
        Walk(s, Exit);
        Assert.True(s.Execute(new TravelCommand { Actor = s.Player, Destination = EastGate }).Success);
        s.Advance(Duration.FromMinutes(1));
        Assert.NotNull(s.GetPlayerView().Travel);
    }

    [Fact]
    public void Player_must_reach_the_exit_and_can_visit_an_internal_store_then_return()
    {
        var s = Journey();
        Assert.Equal(RejectionReason.NotAtExit,
            s.Execute(new TravelCommand { Actor = s.Player, Destination = EastGate }).Rejection);
        Walk(s, Exit);
        Complete(s, new TravelCommand { Actor = s.Player, Destination = EastGate });
        Assert.Equal(new AreaId("east"), s.GetPlayerView().Area);
        Assert.Equal(Exit, s.GetPlayerView().Position);
        Walk(s, new GridPos(2, 3));
        Complete(s, new TakeFoodCommand { Actor = s.Player, Store = new StoreId("east-stock"), Amount = 2 });
        Assert.Equal(2, s.GetPlayerView().Food);
        Walk(s, Exit);
        Complete(s, new TravelCommand { Actor = s.Player, Destination = WestGate });
        Assert.Equal(new AreaId("west"), s.GetPlayerView().Area);
        Assert.Equal(Exit, s.GetPlayerView().Position);
        Assert.Equal(2, s.GetWorldView().Store(new StoreId("east-stock")).Food);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Faction_sends_its_member_from_an_internal_store_to_steal_and_bring_food_home(bool westMapped, bool eastMapped)
    {
        var s = SimulationSession.Create(Places(westMapped, eastMapped)
            .AddFaction("settlers", "Settlers")
            .AddRaidingFaction("hungry", "Hungry", "home", 0, Duration.Zero, 1, 4, Duration.FromHours(1))
            .AddStore("target", "Target", "west-room", 4, "settlers",
                at: westMapped ? (2, 2) : null, access: westMapped ? new[] { (2, 3) } : null)
            .AddStore("home", "Home", "east-room", 0, "hungry",
                at: eastMapped ? (2, 2) : null, access: eastMapped ? new[] { (2, 3) } : null)
            .AddActor("player", "Player", westMapped ? "west-gate" : "west-room",
                isPlayer: true, at: westMapped ? (4, 1) : null)
            .AddActor("courier", "Courier", "east-room", factionId: "hungry", at: eastMapped ? (1, 1) : null)
            .Build());

        Assert.NotNull(s.GetWorldView().Actor(Courier).Assignment);
        s.Advance(Duration.FromMinutes(1));
        Assert.NotNull(s.GetWorldView().Actor(Courier).Travel);
        var loaded = s.SaveAndReload();
        s.Advance(Duration.FromMinutes(29));
        loaded.Advance(Duration.FromMinutes(29));
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        var world = s.GetWorldView();
        Assert.Equal(0, world.Store(new StoreId("target")).Food);
        Assert.Equal(4, world.Store(new StoreId("home")).Food);
        Assert.Equal(0, world.Actor(Courier).Food);
        Assert.Equal(new LocationId("east-room"), world.Actor(Courier).Location);
        Assert.Null(world.Actor(Courier).Assignment);
        Assert.Contains(world.RecentFacts, f => f.Kind == "FoodStolen");
        Assert.Contains(world.RecentFacts, f => f.Kind == "FoodDeposited");
        Assert.Equal(4, world.TotalFood());
    }

    [Fact]
    public void During_mapped_travel_the_player_cannot_watch_remote_people()
    {
        var s = Journey();
        Assert.Contains(s.GetPlayerView().VisibleActors, a => a.Id.Value == "neighbour");
        Leave(s);
        Assert.Empty(s.GetPlayerView().VisibleActors);
        Assert.Empty(s.GetPlayerView().ReportOptions);
    }

    [Fact]
    public void During_mapped_travel_the_player_cannot_inspect_remote_stocks()
    {
        var s = Journey();
        Assert.Contains(s.GetPlayerView().VisibleStores, a => a.Id.Value == "west-stock");
        Leave(s);
        Assert.Empty(s.GetPlayerView().VisibleStores);
        Assert.Empty(s.GetPlayerView().GroundItems);
    }

    [Fact]
    public void Equal_coordinates_in_different_maps_do_not_allow_sight_or_store_use()
    {
        var s = Journey();
        Walk(s, new GridPos(2, 3));
        Assert.DoesNotContain(s.GetPlayerView().VisibleActors, a => a.Id.Value == "resident");
        Assert.DoesNotContain(s.GetPlayerView().VisibleStores, a => a.Id.Value == "east-stock");
        Assert.Equal(RejectionReason.OutOfReach, s.Execute(new TakeFoodCommand
            { Actor = s.Player, Store = new StoreId("east-stock"), Amount = 1 }).Rejection);
        Assert.Empty(s.GetPlayerView().Observations);
    }

    [Fact]
    public void A_theft_at_the_players_coordinates_in_another_map_is_not_witnessed()
    {
        var s = SimulationSession.Create(Places()
            .AddFaction("settlers", "Settlers")
            .AddRaidingFaction("hungry", "Hungry", "home", 0, Duration.Zero, 1, 4, Duration.FromHours(1))
            .AddStore("target", "Target", "east-room", 4, "settlers", at: (2, 2), access: new[] { (2, 3) })
            .AddStore("home", "Home", "east-gate", 0, "hungry", at: (4, 1), access: new[] { (4, 2) })
            .AddActor("player", "Player", "west-room", isPlayer: true, at: (2, 3))
            .AddActor("courier", "Courier", "east-room", factionId: "hungry", at: (2, 3))
            .Build());
        s.Advance(Duration.FromMinutes(10));
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "FoodStolen");
        Assert.Equal(4, s.GetWorldView().Store(new StoreId("home")).Food);
        Assert.Empty(s.GetPlayerView().Observations);
        Assert.Empty(s.GetPlayerView().ReportOptions);
        Assert.DoesNotContain(s.GetPlayerView().RecentEvents, f => f.Kind == "FoodStolen");
    }

    [Fact]
    public void Arrival_does_not_overlap_a_resident_and_the_player_can_move_on()
    {
        var s = Journey(occupant: true);
        Leave(s);
        s.Advance(Duration.FromMinutes(1));
        var view = s.GetPlayerView();
        Assert.Null(view.Travel);
        Assert.Equal(new AreaId("east"), view.Area);
        Assert.True(view.Position!.Value.IsAdjacentOrSame(Exit));
        Assert.NotEqual(s.GetWorldView().Actor(new ActorId("resident")).Position, view.Position);
        Walk(s, new GridPos(1, 3));
        Assert.Equal(new GridPos(1, 3), s.GetPlayerView().Position);
    }

    [Fact]
    public void Loading_mid_journey_and_advancing_in_small_steps_preserves_the_arrival()
    {
        var s = Journey(occupant: true);
        Leave(s);
        var loaded = s.SaveAndReload();
        s.Advance(Duration.FromMinutes(1));
        for (var i = 0; i < 60; i++) loaded.Advance(Duration.FromSeconds(1));
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(new AreaId("east"), loaded.GetPlayerView().Area);
    }

    [Fact]
    public void Simultaneous_arrivals_occupy_distinct_squares()
    {
        var s = SimulationSession.Create(Places(westMapped: false)
            .AddActor("player", "Player", "west-room", isPlayer: true)
            .AddActor("courier", "Courier", "west-room", workLocationId: "east-room",
                shiftStart: Duration.Zero, shiftEnd: Duration.FromHours(23))
            .Build());
        // The routine already started the NPC's journey at bootstrap. The player leaves at that same instant.
        Assert.NotNull(s.GetWorldView().Actor(Courier).Travel);
        Complete(s, new TravelCommand { Actor = s.Player, Destination = EastGate });
        Assert.Null(s.GetWorldView().Actor(Courier).Travel);
        Assert.NotEqual(s.GetWorldView().Actor(Courier).Position, s.GetPlayerView().Position);
    }

    [Fact]
    public void A_completely_blocked_entrance_waits_survives_load_and_arrives_once_cleared()
    {
        var s = SimulationSession.Create(new ScenarioBuilder()
            .AddArea("west", "West").AddArea("east", "East")
            .AddLocation("west-gate", "West gate", "west").AddLocation("east-gate", "East gate", "east")
            .AddRoute("west-gate", "east-gate", Duration.FromMinutes(2))
            .AddMap("west", new[] { "EEE" }, new Dictionary<char, string> { ['E'] = "west-gate" })
            .AddMap("east", new[] { "#E#", "###" }, new Dictionary<char, string> { ['E'] = "east-gate" })
            .AddExit("west", "west-gate", (0, 0)).AddExit("east", "east-gate", (1, 0))
            .AddActor("player", "Player", "east-gate", isPlayer: true, at: (1, 0))
            .AddActor("courier", "Courier", "west-gate", at: (0, 0), workLocationId: "east-gate",
                shiftStart: Duration.Zero, shiftEnd: Duration.FromHours(23))
            .Build());
        var action = s.GetWorldView().Actor(Courier).Travel!.Action;
        Assert.Equal(AdvanceOutcome.TimeLimitReached,
            s.AdvanceUntilCompleted(action, Duration.FromMinutes(3)).Outcome);
        Assert.Null(s.GetWorldView().Actor(Courier).Position);
        Assert.DoesNotContain(s.GetWorldView().RecentFacts, f => f.Kind == "TravelCompleted");
        var loaded = s.SaveAndReload();
        foreach (var session in new[] { s, loaded })
        {
            Assert.True(session.Execute(new TravelCommand { Actor = session.Player, Destination = WestGate }).Success);
            Assert.Equal(AdvanceOutcome.Completed,
                session.AdvanceUntilCompleted(action, Duration.FromSeconds(10)).Outcome);
            Assert.Equal(new GridPos(1, 0), session.GetWorldView().Actor(Courier).Position);
            Assert.Null(session.GetWorldView().Actor(Courier).Travel);
            Assert.Single(session.GetWorldView().RecentFacts, f => f.Kind == "TravelCompleted");
        }
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
    }

    [Fact]
    public void An_occupied_exit_does_not_place_the_arrival_through_a_wall_or_closed_door()
    {
        var s = SimulationSession.Create(Places()
            .AddEdge("east", (4, 2), (3, 2), MapEdgeKind.Wall)
            .AddEdge("east", (4, 2), (4, 1), MapEdgeKind.Door, doorAt: (4, 1), open: false)
            .AddActor("player", "Player", "west-gate", isPlayer: true, at: (4, 2))
            .AddActor("resident", "Resident", "east-gate", at: (4, 2))
            .Build());
        var travel = s.Execute(new TravelCommand { Actor = s.Player, Destination = EastGate });
        Assert.True(travel.Success, travel.Message);
        Assert.Equal(AdvanceOutcome.TimeLimitReached,
            s.AdvanceUntilCompleted(travel.Action!.Value, Duration.FromMinutes(3)).Outcome);
        Assert.Null(s.GetPlayerView().Position);
        Assert.NotNull(s.GetPlayerView().Travel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Waiting_for_entry_is_not_counted_as_marching_even_across_midnight(bool crossMidnight)
    {
        var s = SimulationSession.Create(new ScenarioBuilder()
            .AddArea("west", "West").AddArea("east", "East")
            .AddLocation("west-gate", "West gate", "west").AddLocation("east-gate", "East gate", "east")
            .AddRoute("west-gate", "east-gate", Duration.FromMinutes(2))
            .AddMap("west", new[] { "EEE" }, new Dictionary<char, string> { ['E'] = "west-gate" })
            .AddMap("east", new[] { "#E#", "###" }, new Dictionary<char, string> { ['E'] = "east-gate" })
            .AddExit("west", "west-gate", (0, 0)).AddExit("east", "east-gate", (1, 0))
            .AddActor("player", "Player", "west-gate", isPlayer: true, at: (0, 0), food: 5)
            .AddActor("courier", "Courier", "east-gate", at: (1, 0), workLocationId: "west-gate",
                shiftStart: Duration.FromHours(1), shiftEnd: Duration.FromHours(23))
            .Build());
        if (crossMidnight) s.AdvanceTo(At(1, 23, 55));
        var travel = s.Execute(new TravelCommand { Actor = s.Player, Destination = EastGate });
        Assert.True(travel.Success, travel.Message);
        Assert.False(s.GetPlayerView().Travel!.WaitingForEntry);
        s.Advance(Duration.FromMinutes(1));
        Assert.False(s.GetPlayerView().Travel!.WaitingForEntry);
        s.Advance(Duration.FromMinutes(3));
        Assert.True(s.GetPlayerView().Travel!.WaitingForEntry); // scheduled arrival passed; the entrance is still occupied
        var loaded = s.SaveAndReload();
        Assert.True(loaded.GetPlayerView().Travel!.WaitingForEntry);
        foreach (var session in new[] { s, loaded })
        {
            Assert.Equal(AdvanceOutcome.Completed,
                session.AdvanceUntilCompleted(travel.Action!.Value, Duration.FromHours(2)).Outcome);
            Assert.Equal(crossMidnight ? 0 : 2.0 / 60, session.GetPlayerView().TravelHoursToday, 6);
            Assert.Equal(new GridPos(1, 0), session.GetPlayerView().Position);
            Assert.Null(session.GetPlayerView().Travel);
        }
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
    }
}
