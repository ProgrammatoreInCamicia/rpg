using System.Text.Json;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.SliceScenario.Ids;

namespace RpgSandbox.Sim.Tests;

public class DepositFoodTests
{
    private const int PlayerStartFood = 10;
    private const int GranaryStartFood = 40;
    private static readonly Duration DepositTime = Duration.FromMinutes(2);

    /// <summary>A session with the player already standing at the granary.</summary>
    private static SimulationSession AtGranary()
    {
        var s = SimulationSession.Create(SliceScenario.Create());
        var travel = s.Execute(new TravelCommand { Actor = s.Player, Destination = Granary });
        s.AdvanceUntilCompleted(travel.Action!.Value, Duration.FromHours(1));
        return s;
    }

    private static CommandResult Deposit(SimulationSession s, int amount, StoreId? store = null) =>
        s.Execute(new DepositFoodCommand { Actor = s.Player, Store = store ?? GranaryStore, Amount = amount });

    private static ActorView PlayerView(SimulationSession s) => s.GetWorldView().Actors.Single(a => a.IsPlayer);
    private static int GranaryFood(SimulationSession s) => s.GetWorldView().Stores.Single(x => x.Id == GranaryStore).Food;
    private static int TotalFood(SimulationSession s)
    {
        var view = s.GetWorldView();
        return view.Actors.Sum(a => a.Food) + view.Stores.Sum(x => x.Food);
    }

    [Fact]
    public void Deposit_moves_food_when_the_action_completes()
    {
        var s = AtGranary();
        var start = s.Now;

        var result = Deposit(s, 4);

        Assert.True(result.Success);
        Assert.Equal(start.Plus(DepositTime), result.CompletesAt);
        // Nothing moves until the action completes.
        Assert.Equal(PlayerStartFood, PlayerView(s).Food);
        Assert.Equal(GranaryStartFood, GranaryFood(s));
        Assert.Equal("DepositFood", PlayerView(s).Action!.Kind);

        s.AdvanceUntilCompleted(result.Action!.Value, Duration.FromHours(1));

        Assert.Equal(start.Plus(DepositTime), s.Now);
        Assert.Equal(PlayerStartFood - 4, PlayerView(s).Food);
        Assert.Equal(GranaryStartFood + 4, GranaryFood(s));
        Assert.Null(PlayerView(s).Action);
        Assert.Equal("FoodDeposited", s.GetWorldView().RecentFacts[^1].Kind);
    }

    [Fact]
    public void Food_is_conserved()
    {
        var s = AtGranary();
        var total = TotalFood(s);

        foreach (var amount in new[] { 3, 5, 2 })
        {
            var result = Deposit(s, amount);
            s.AdvanceUntilCompleted(result.Action!.Value, Duration.FromHours(1));
            Assert.Equal(total, TotalFood(s));
        }

        Assert.Equal(0, PlayerView(s).Food);
        Assert.Equal(GranaryStartFood + PlayerStartFood, GranaryFood(s));
    }

    [Theory]
    [InlineData(0, RejectionReason.InvalidAmount)]
    [InlineData(-3, RejectionReason.InvalidAmount)]
    [InlineData(PlayerStartFood + 1, RejectionReason.InsufficientFood)]
    public void Invalid_amounts_are_rejected_without_changes(int amount, RejectionReason expected)
    {
        var s = AtGranary();
        var before = Snapshot(s);

        var result = Deposit(s, amount);

        Assert.Equal(expected, result.Rejection);
        Assert.Equal(before, Snapshot(s));
    }

    [Fact]
    public void Depositing_everything_is_allowed()
    {
        var s = AtGranary();

        var result = Deposit(s, PlayerStartFood);
        s.AdvanceUntilCompleted(result.Action!.Value, Duration.FromHours(1));

        Assert.Equal(0, PlayerView(s).Food);
    }

    [Fact]
    public void Deposit_from_another_place_is_rejected()
    {
        var s = SimulationSession.Create(SliceScenario.Create()); // player starts at the inn
        var before = Snapshot(s);

        var result = Deposit(s, 1);

        Assert.Equal(RejectionReason.NotAtStore, result.Rejection);
        Assert.Equal(before, Snapshot(s));
    }

    [Fact]
    public void Unknown_store_is_rejected()
    {
        var s = AtGranary();

        Assert.Equal(RejectionReason.StoreNotFound, Deposit(s, 1, new StoreId("nowhere")).Rejection);
    }

    [Fact]
    public void Busy_actor_cannot_deposit_or_travel()
    {
        var s = AtGranary();
        Deposit(s, 2);
        var before = Snapshot(s);

        Assert.Equal(RejectionReason.ActorBusy, Deposit(s, 1).Rejection);
        Assert.Equal(RejectionReason.ActorBusy,
            s.Execute(new TravelCommand { Actor = s.Player, Destination = Inn }).Rejection);
        Assert.Equal(before, Snapshot(s));
    }

    [Fact]
    public void Travelling_actor_cannot_deposit()
    {
        var s = SimulationSession.Create(SliceScenario.Create());
        s.Execute(new TravelCommand { Actor = s.Player, Destination = Granary });

        Assert.Equal(RejectionReason.ActorBusy, Deposit(s, 1).Rejection);
    }

    [Fact]
    public void Stores_appear_in_the_world_view()
    {
        var store = SimulationSession.Create(SliceScenario.Create()).GetWorldView().Stores.Single(x => x.Id == GranaryStore);

        Assert.Equal(GranaryStore, store.Id);
        Assert.Equal(Granary, store.Location);
        Assert.Equal(GranaryStartFood, store.Food);
    }

    private static string Snapshot(SimulationSession s) => JsonSerializer.Serialize(s.GetWorldView());
}
