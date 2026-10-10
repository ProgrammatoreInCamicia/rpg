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

        Assert.NotNull(map.FindPath(FarmerHome, FarmerWork));
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
}
