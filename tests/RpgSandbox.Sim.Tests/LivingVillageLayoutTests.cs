using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;

namespace RpgSandbox.Sim.Tests;

public class LivingVillageLayoutTests
{
    private static void Walk(SimulationSession session, GridPos to)
    {
        var command = session.Execute(new MoveCommand { Actor = session.Player, To = to });
        Assert.True(command.Success, command.Message);
        Assert.Equal(AdvanceOutcome.Completed,
            session.AdvanceUntilCompleted(command.Action!.Value, Duration.FromMinutes(5)).Outcome);
    }

    [Fact]
    public void Forest_route_house_and_guarded_store_are_reachable_on_the_living_map()
    {
        var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var map = session.Engine.World.Maps[Village];

        Assert.Equal((24, 17), (map.Width, map.Height));
        Assert.Equal(ForestRoad, map.ZoneAt(ForestExit));
        Assert.Equal(FarmerHouse, map.ZoneAt(FarmerHome));
        Assert.Equal(Granary, map.ZoneAt(FarmerWork));
        Assert.Equal(Granary, map.ZoneAt(GuardPost));
        Assert.True(map.IsClosedDoor(HouseDoor));
        Assert.Equal(ForestExit, map.Exits[ForestRoad]);
        Assert.Equal(FarmerHome, map.Posts[(FarmerHouse, PostKind.Home)]);
        Assert.Equal(FarmerWork, map.Posts[(Granary, PostKind.Work)]);
        Assert.Equal(GuardPost, map.Posts[(Granary, PostKind.Guard)]);
        Assert.Equal(new[] { GranaryAccess }, session.Engine.World.Stores[GranaryStore].Access);

        Assert.Null(map.FindPath(FarmerHome, FarmerWork)); // P4a: the farmhouse is locked...
        Assert.NotNull(map.FindPath(FarmerHome, FarmerWork, opens: door => door == HouseDoor)); // ...the farmer has the key
        Assert.NotNull(map.FindPath(ForestExit, GranaryAccess));
        Assert.True(GuardPost.IsAdjacentOrSame(GranaryAccess));
        Assert.True(map.HasLineOfSight(GuardPost, GranaryAccess));
        Assert.True(GranaryAccess.IsAdjacentOrSame(GranaryStoreSquare));
    }

    [Fact]
    public void Living_village_enforces_the_granary_entrance_and_the_forest_exit()
    {
        var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var deposit = new DepositFoodCommand { Actor = session.Player, Store = GranaryStore, Amount = 1 };

        Walk(session, new GridPos(18, 5)); // beside the store, but not at its entrance
        Assert.Equal(RejectionReason.OutOfReach, session.Execute(deposit).Rejection);
        Walk(session, GranaryAccess);
        var delivered = session.Execute(deposit);
        Assert.True(delivered.Success, delivered.Message);
        session.AdvanceUntilCompleted(delivered.Action!.Value, Duration.FromMinutes(5));

        Walk(session, new GridPos(ForestExit.X - 1, ForestExit.Y));
        Assert.Equal(RejectionReason.NotAtExit,
            session.Execute(new TravelCommand { Actor = session.Player, Destination = BanditCamp }).Rejection);
        Walk(session, ForestExit);
        var away = session.Execute(new TravelCommand { Actor = session.Player, Destination = BanditCamp });
        Assert.True(away.Success, away.Message);
        session.AdvanceUntilCompleted(away.Action!.Value, Duration.FromHours(1));
        Assert.Equal(BanditCamp, session.GetPlayerView().Location);

        var back = session.Execute(new TravelCommand { Actor = session.Player, Destination = ForestRoad });
        Assert.True(back.Success, back.Message);
        session.AdvanceUntilCompleted(back.Action!.Value, Duration.FromHours(1));
        Assert.Equal(ForestExit, session.GetPlayerView().Position);
    }

    [Fact]
    public void Living_village_uses_thin_room_boundaries_and_accepts_exact_floor_points()
    {
        var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var map = session.Engine.World.Maps[Village];

        Assert.Contains(Inn, map.IndoorZones);
        Assert.Contains(FarmerHouse, map.IndoorZones);
        Assert.Equal('.', map.Rows[2][7]); // the former one-square-thick inn wall is now floor
        Assert.True(map.HasLineOfSight(new GridPos(3, 0), new GridPos(3, 1))); // window
        Assert.False(map.DiagonalOpen(new GridPos(3, 0), new GridPos(3, 1)));
        Assert.False(map.HasLineOfSight(new GridPos(7, 1), new GridPos(6, 1))); // stone
        Assert.True(map.IsClosedDoor(HouseDoor));
        Assert.False(map.HasLineOfSight(new GridPos(8, 13), new GridPos(7, 13)));
        Assert.NotNull(session.GetPlayerView().MapVisibility);

        var exact = new Spot(165, 147);
        var walk = session.Execute(new MoveToSpotCommand { Actor = session.Player, To = exact });
        Assert.True(walk.Success, walk.Message);
        Assert.Equal(AdvanceOutcome.Completed,
            session.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(1)).Outcome);
        Assert.Equal(exact, session.GetPlayerView().Spot);
        Assert.Equal(PlayerStart, session.GetPlayerView().Position);
    }

    [Fact]
    public void Walking_in_unlit_road_explores_nearby_floor_without_a_torch_and_remembers_it_after_leaving()
    {
        var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var dark = new GridPos(12, 8);
        Assert.Equal('0', session.GetPlayerView().MapVisibility![dark.Y][dark.X]);

        Walk(session, dark);
        var here = session.GetPlayerView();
        Assert.Equal('0', here.MapLight![dark.Y][dark.X]);
        Assert.Equal('2', here.MapVisibility![dark.Y][dark.X]);
        Assert.Equal('2', here.MapVisibility[dark.Y][dark.X + 1]);

        Walk(session, new GridPos(12, 10));
        Assert.Equal('1', session.GetPlayerView().MapVisibility![dark.Y][dark.X]);
        Assert.True(session.Execute(new TorchCommand { Actor = session.Player, Lit = true, StowShield = true }).Success);
        Assert.Equal('2', session.GetPlayerView().MapVisibility![dark.Y][dark.X]);
        Assert.True(session.Execute(new TorchCommand { Actor = session.Player, Lit = false }).Success);
        Assert.Equal('1', session.GetPlayerView().MapVisibility![dark.Y][dark.X]);
    }
}
