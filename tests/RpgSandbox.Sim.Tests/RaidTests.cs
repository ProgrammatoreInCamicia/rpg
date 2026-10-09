using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.SliceScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>The bandits' raid happens on its own, without any player input.</summary>
public class RaidTests
{
    private static SimulationSession NewSession() => SimulationSession.Create(SliceScenario.Create());

    [Fact]
    public void Nothing_happens_while_the_bandits_have_enough_food()
    {
        var s = NewSession();

        s.AdvanceTo(At(1, 8, 59));

        var view = s.GetWorldView();
        Assert.Equal(8, view.Store(CampStore).Food); // 14 - 6 eaten at 08:00
        Assert.Equal(40, view.Store(GranaryStore).Food);
        Assert.Equal(BanditCamp, view.Actor(Raider).Location);
        Assert.Null(view.Actor(Raider).Assignment);
        Assert.Equal("Scorte sufficienti", view.Faction(Bandits).LastDecision!.Rule); // evaluated at 06:00
    }

    [Fact]
    public void Hungry_bandits_raid_the_granary_without_the_player()
    {
        var s = NewSession();

        s.AdvanceTo(At(1, 9));
        var view = s.GetWorldView();
        Assert.Equal("Ordina una razzia", view.Faction(Bandits).LastDecision!.Rule);
        Assert.NotNull(view.Actor(Raider).Assignment);
        Assert.Equal(Granary, view.Actor(Raider).Travel!.Destination);
        Assert.Equal("Raggiungi il bersaglio", view.Actor(Raider).LastDecision!.Rule);

        s.AdvanceTo(At(1, 9, 33)); // 30 min walk + 3 min to take
        view = s.GetWorldView();
        Assert.Equal(32, view.Store(GranaryStore).Food);
        Assert.Equal(8, view.Actor(Raider).Food);
        Assert.Contains(view.RecentFacts, f => f.Kind == "FoodStolen");

        s.AdvanceTo(At(1, 10, 5)); // 30 min back + 2 min to deposit
        view = s.GetWorldView();
        Assert.Equal(16, view.Store(CampStore).Food);
        Assert.Equal(0, view.Actor(Raider).Food);
        Assert.Null(view.Actor(Raider).Assignment);
        Assert.Equal("Razzia conclusa", view.Actor(Raider).LastDecision!.Rule);
        Assert.Contains(view.RecentFacts, f => f.Kind == "RaidCompleted");
    }

    [Fact]
    public void Food_only_leaves_the_world_through_upkeep()
    {
        var s = NewSession();
        var initial = s.GetWorldView().TotalFood();

        s.AdvanceTo(At(3, 23, 59));

        // Three upkeeps of 6, never short: day 1 14->8 (+8 raid), day 2 16->10, day 3 10->4 (+8 raid).
        var view = s.GetWorldView();
        Assert.Equal(initial - 3 * 6, view.TotalFood());
        Assert.Equal(40 - 16, view.Store(GranaryStore).Food);
        Assert.Equal(12, view.Store(CampStore).Food);
    }

    [Fact]
    public void Raid_takes_only_what_is_there()
    {
        var s = SimulationSession.Create(SmallWorld(granaryFood: 5));

        s.AdvanceTo(At(1, 9, 33));

        var view = s.GetWorldView();
        Assert.Equal(0, view.Store(GranaryStore).Food);
        Assert.Equal(5, view.Actor(Raider).Food);
    }

    [Fact]
    public void Nothing_to_raid_is_explained()
    {
        var s = SimulationSession.Create(SmallWorld(granaryFood: 0));

        s.AdvanceTo(At(1, 9));

        var view = s.GetWorldView();
        Assert.Equal("Nessun bersaglio", view.Faction(Bandits).LastDecision!.Rule);
        Assert.Null(view.Actor(Raider).Assignment);
        Assert.Equal("Routine: riposa", view.Actor(Raider).LastDecision!.Rule);
    }

    [Fact]
    public void Decisions_explain_their_inputs()
    {
        var s = NewSession();
        s.AdvanceTo(At(1, 9));

        var decision = s.GetWorldView().Faction(Bandits).LastDecision!;

        Assert.Equal(At(1, 9), decision.At);
        Assert.Contains("Scorte di casa (Scorte del campo): 8", decision.Inputs);
        Assert.Contains("Soglia: 10", decision.Inputs);
        Assert.Contains("Bersaglio più vicino: Scorte del granaio", decision.Inputs);
    }

    [Fact]
    public void Player_delivering_food_is_seen_by_the_raid_target_choice()
    {
        // The player can change the outcome: the raid still happens, but the granary had more to lose.
        var s = NewSession();
        var travel = s.Execute(new TravelCommand { Actor = s.Player, Destination = Granary });
        s.AdvanceUntilCompleted(travel.Action!.Value, Duration.FromHours(1));
        var deposit = s.Execute(new DepositFoodCommand { Actor = s.Player, Store = GranaryStore, Amount = 10 });
        s.AdvanceUntilCompleted(deposit.Action!.Value, Duration.FromHours(1));

        s.AdvanceTo(At(1, 10, 5));

        Assert.Equal(50 - 8, s.GetWorldView().Store(GranaryStore).Food);
    }

    [Fact]
    public void Single_and_fractional_advances_agree_with_npcs_acting()
    {
        var single = NewSession();
        var hourly = NewSession();
        var byMinute = NewSession();

        single.Advance(Duration.FromHours(26));
        for (var i = 0; i < 26; i++)
            hourly.Advance(Duration.FromHours(1));
        for (var i = 0; i < 26 * 60; i++)
            byMinute.Advance(Duration.FromMinutes(1));

        Assert.Equal(single.SaveToString(), hourly.SaveToString());
        Assert.Equal(single.SaveToString(), byMinute.SaveToString());
    }

    /// <summary>The slice map with a configurable granary, for edge cases.</summary>
    private static Scenario SmallWorld(int granaryFood) =>
        new ScenarioBuilder()
            .AddArea(Village.Value, "Villaggio")
            .AddArea(Forest.Value, "Bosco")
            .AddLocation(Granary.Value, "Granaio", Village.Value)
            .AddLocation(BanditCamp.Value, "Campo dei banditi", Forest.Value)
            .AddRoute(Granary.Value, BanditCamp.Value, Duration.FromMinutes(30))
            .AddFaction(VillageFaction.Value, "Villaggio", homeStoreId: GranaryStore.Value)
            .AddRaidingFaction(Bandits.Value, "Banditi", CampStore.Value, 6, Duration.FromHours(8), 10, 8, Duration.FromHours(3))
            .AddStore(GranaryStore.Value, "Scorte del granaio", Granary.Value, granaryFood, VillageFaction.Value)
            .AddStore(CampStore.Value, "Scorte del campo", BanditCamp.Value, 14, Bandits.Value)
            .AddActor(Player.Value, "Protagonista", Granary.Value, isPlayer: true)
            .AddActor(Raider.Value, "Razziatore", BanditCamp.Value, factionId: Bandits.Value)
            .Build();
}
