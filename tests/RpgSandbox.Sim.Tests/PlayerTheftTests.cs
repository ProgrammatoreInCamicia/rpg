using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.SliceScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>T4c: the player can steal; being recognised has consequences, with the constraints agreed with Codex (C4).</summary>
public class PlayerTheftTests
{
    private static void PlayerGoesTo(SimulationSession s, LocationId destination)
    {
        var travel = s.Execute(new TravelCommand { Actor = s.Player, Destination = destination });
        Assert.True(travel.Success, travel.Message);
        s.AdvanceUntilCompleted(travel.Action!.Value, Duration.FromHours(2));
    }

    private static void PlayerSteals(SimulationSession s, int amount)
    {
        var take = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = GranaryStore, Amount = amount });
        Assert.True(take.Success, take.Message);
        s.AdvanceUntilCompleted(take.Action!.Value, Duration.FromHours(1));
    }

    /// <summary>At 13:10 the farmer (at work since 13:05) watches the player take 3 rations, in broad daylight.</summary>
    private static SimulationSession StealUnderTheFarmersEyes()
    {
        var s = SimulationSession.Create(SliceScenario.Create());
        PlayerGoesTo(s, Granary);
        s.AdvanceTo(At(1, 13, 10));
        PlayerSteals(s, 3); // granary 32 (after the morning raid) -> 29
        return s;
    }

    [Fact]
    public void A_recognised_thief_is_reported_and_the_guard_takes_the_rations_back()
    {
        var s = StealUnderTheFarmersEyes();
        Assert.Equal(13, s.GetPlayerView().Food);
        Assert.Equal(Player, s.GetWorldView().Actor(Farmer).Knowledge.Single().Thief);

        s.AdvanceTo(At(1, 13, 35)); // farmer tells the guard 13:23; guard reaches the granary 13:28, stops the player 13:30

        var view = s.GetWorldView();
        Assert.Equal(10, s.GetPlayerView().Food);
        Assert.Equal(32, view.Store(GranaryStore).Food);
        Assert.Empty(view.Actor(Guard).Claims);
        Assert.Contains(view.RecentFacts, f => f.Kind == "FoodConfiscated");
    }

    [Fact]
    public void The_player_learns_what_happened_to_him_but_not_who_saw_him()
    {
        var s = StealUnderTheFarmersEyes();
        s.AdvanceTo(At(1, 13, 35));

        var events = s.GetPlayerView().RecentEvents;

        Assert.Contains(events, e => e.Kind == "FoodConfiscated");
        Assert.Contains(events, e => e.Kind == "FoodStolen");
        Assert.DoesNotContain(events, e => e.Kind == "FoodTheftWitnessed" || e.Kind == "InformationShared");
    }

    [Fact]
    public void A_thief_who_walked_away_is_stopped_when_the_guard_next_sees_him()
    {
        var s = StealUnderTheFarmersEyes();
        PlayerGoesTo(s, Inn); // leaves at 13:13, before the guard comes; they cross paths on the road

        s.AdvanceTo(At(1, 15));
        Assert.Equal(13, s.GetPlayerView().Food);
        Assert.Equal(3, s.GetWorldView().Actor(Guard).Claims.Single().Owed);

        PlayerGoesTo(s, Granary); // back at 15:05, where the guard stands watch
        s.AdvanceTo(At(1, 16, 10)); // the guard notices him at the end of his current watch

        Assert.Equal(10, s.GetPlayerView().Food);
        Assert.Empty(s.GetWorldView().Actor(Guard).Claims);
    }

    [Fact]
    public void Nothing_is_taken_from_someone_who_no_longer_has_the_rations()
    {
        var s = StealUnderTheFarmersEyes();
        var deposit = s.Execute(new DepositFoodCommand { Actor = s.Player, Store = GranaryStore, Amount = 13 }); // gives everything back
        s.AdvanceUntilCompleted(deposit.Action!.Value, Duration.FromHours(1));

        s.AdvanceTo(At(1, 14));

        var view = s.GetWorldView();
        Assert.Equal(0, s.GetPlayerView().Food);
        Assert.Equal(3, view.Actor(Guard).Claims.Single().Owed); // still owed: the guard does not know he returned it
        Assert.DoesNotContain(view.RecentFacts, f => f.Kind == "FoodConfiscated");
        Assert.Equal(0, view.Actor(Guard).Food);
    }

    [Fact]
    public void A_theft_nobody_sees_has_no_consequence()
    {
        var s = SimulationSession.Create(SliceScenario.Create());
        PlayerGoesTo(s, Granary);
        s.AdvanceTo(At(1, 2)); // nobody at the granary at night

        PlayerSteals(s, 5);
        s.AdvanceTo(At(2, 12));

        var view = s.GetWorldView();
        Assert.Equal(15, s.GetPlayerView().Food);
        Assert.All(view.Actors, a => Assert.DoesNotContain(a.Knowledge, o => o.Thief == Player));
        Assert.Empty(view.Actor(Guard).Claims);
    }

    [Theory]
    [InlineData(1, 13, 20)] // the farmer telling the guard
    [InlineData(1, 13, 29)] // the guard stopping the player
    [InlineData(1, 13, 31)] // the guard carrying the rations back
    public void Confiscation_survives_save_and_reload(int day, int hour, int minute)
    {
        var continuous = StealUnderTheFarmersEyes();
        var reloaded = StealUnderTheFarmersEyes();
        reloaded.AdvanceTo(At(day, hour, minute));
        reloaded = reloaded.SaveAndReload();

        continuous.AdvanceTo(At(2, 12));
        reloaded.AdvanceTo(At(2, 12));
        Assert.Equal(continuous.SaveToString(), reloaded.SaveToString());
    }
}
