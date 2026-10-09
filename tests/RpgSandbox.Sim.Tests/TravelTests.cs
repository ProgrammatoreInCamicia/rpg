using System.Text.Json;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.SliceScenario.Ids;

namespace RpgSandbox.Sim.Tests;

public class TravelTests
{
    private static readonly Duration InnToGranary = Duration.FromMinutes(5);

    private static SimulationSession NewSession() => SimulationSession.Create(SliceScenario.Create());

    private static ActorView PlayerView(SimulationSession s) =>
        s.GetWorldView().Actors.Single(a => a.Id == s.Player);

    private static CommandResult Travel(SimulationSession s, LocationId to, ActorId? actor = null) =>
        s.Execute(new TravelCommand { Actor = actor ?? s.Player, Destination = to });

    [Fact]
    public void Execute_starts_travel_without_advancing_time()
    {
        var s = NewSession();

        var result = Travel(s, Granary);

        Assert.True(result.Success);
        Assert.Equal(GameTime.Start, s.Now);
        Assert.Equal(GameTime.Start.Plus(InnToGranary), result.CompletesAt);
        var player = PlayerView(s);
        Assert.Null(player.Location);
        Assert.NotNull(player.Travel);
        Assert.Equal(Inn, player.Travel!.Origin);
        Assert.Equal(Granary, player.Travel.Destination);
    }

    [Fact]
    public void Traveller_is_nowhere_halfway_and_arrives_at_the_deadline()
    {
        var s = NewSession();
        Travel(s, Granary);

        s.Advance(Duration.FromSeconds(InnToGranary.Seconds - 1));
        Assert.Null(PlayerView(s).Location);

        s.Advance(Duration.FromSeconds(1));
        var player = PlayerView(s);
        Assert.Equal(Granary, player.Location);
        Assert.Null(player.Travel);
    }

    [Fact]
    public void Advancing_past_the_deadline_still_completes_the_travel()
    {
        var s = NewSession();
        Travel(s, Granary);

        s.Advance(Duration.FromHours(3));

        Assert.Equal(Granary, PlayerView(s).Location);
        Assert.Equal(GameTime.Start.Plus(Duration.FromHours(3)), s.Now);
    }

    [Fact]
    public void Single_and_fractional_advances_produce_the_same_world()
    {
        var single = NewSession();
        var fractional = NewSession();
        Travel(single, Granary);
        Travel(fractional, Granary);

        single.Advance(Duration.FromMinutes(17));
        for (var i = 0; i < 17 * 60; i++)
            fractional.Advance(Duration.FromSeconds(1));

        Assert.Equal(Snapshot(single), Snapshot(fractional));
    }

    [Fact]
    public void AdvanceUntilCompleted_stops_at_arrival()
    {
        var s = NewSession();
        var action = Travel(s, Granary).Action!.Value;

        var result = s.AdvanceUntilCompleted(action, Duration.FromHours(1));

        Assert.Equal(AdvanceOutcome.Completed, result.Outcome);
        Assert.Equal(GameTime.Start.Plus(InnToGranary), s.Now);
        Assert.Equal(Granary, PlayerView(s).Location);
    }

    [Fact]
    public void AdvanceUntilCompleted_respects_the_time_limit()
    {
        var s = NewSession();
        var action = Travel(s, Granary).Action!.Value;

        var result = s.AdvanceUntilCompleted(action, Duration.FromMinutes(1));

        Assert.Equal(AdvanceOutcome.TimeLimitReached, result.Outcome);
        Assert.Equal(GameTime.Start.Plus(Duration.FromMinutes(1)), s.Now);
        Assert.Null(PlayerView(s).Location);
    }

    [Fact]
    public void AdvanceUntilCompleted_on_a_finished_action_does_not_move_time()
    {
        var s = NewSession();
        var action = Travel(s, Granary).Action!.Value;
        s.Advance(Duration.FromHours(1));

        var result = s.AdvanceUntilCompleted(action, Duration.FromHours(1));

        Assert.Equal(AdvanceOutcome.NotPending, result.Outcome);
        Assert.Equal(GameTime.Start.Plus(Duration.FromHours(1)), s.Now);
    }

    [Fact]
    public void Travel_while_travelling_is_rejected_without_changes()
    {
        var s = NewSession();
        Travel(s, Granary);
        var before = Snapshot(s);

        var result = Travel(s, Inn);

        Assert.Equal(RejectionReason.ActorBusy, result.Rejection);
        Assert.Null(result.Action);
        Assert.Equal(before, Snapshot(s));
    }

    [Theory]
    [InlineData("inn", RejectionReason.AlreadyThere)]
    [InlineData("nowhere", RejectionReason.DestinationNotFound)]
    public void Invalid_destinations_are_rejected_without_changes(string destination, RejectionReason expected)
    {
        var s = NewSession();
        var before = Snapshot(s);

        var result = Travel(s, new LocationId(destination));

        Assert.Equal(expected, result.Rejection);
        Assert.Equal(before, Snapshot(s));
    }

    [Fact]
    public void Unknown_actor_is_rejected()
    {
        var s = NewSession();

        var result = Travel(s, Granary, new ActorId("ghost"));

        Assert.Equal(RejectionReason.ActorNotFound, result.Rejection);
    }

    [Fact]
    public void Unconnected_destination_is_rejected()
    {
        var scenario = new ScenarioBuilder()
            .AddArea("a", "A")
            .AddLocation("x", "X", "a")
            .AddLocation("y", "Y", "a")
            .AddLocation("z", "Z", "a")
            .AddRoute("x", "y", Duration.FromMinutes(1))
            .AddActor("p", "P", "x", isPlayer: true)
            .Build();
        var s = SimulationSession.Create(scenario);

        var result = Travel(s, new LocationId("z"));

        Assert.Equal(RejectionReason.RouteNotFound, result.Rejection);
    }

    [Fact]
    public void Zero_advance_changes_nothing_and_negative_advance_throws()
    {
        var s = NewSession();
        Travel(s, Granary);
        var before = Snapshot(s);

        s.Advance(Duration.Zero);
        Assert.Equal(before, Snapshot(s));

        Assert.Throws<ArgumentOutOfRangeException>(() => s.Advance(Duration.FromSeconds(-1)));
        Assert.Equal(before, Snapshot(s));
    }

    [Fact]
    public void Views_are_detached_copies()
    {
        var s = NewSession();
        var view = s.GetWorldView();

        Travel(s, Granary);
        s.Advance(Duration.FromHours(1));

        Assert.Equal(Inn, view.Actors.Single(a => a.IsPlayer).Location);
        Assert.False(view.Actors is List<ActorView>);
        Assert.Throws<NotSupportedException>(() => ((IList<ActorView>)view.Actors).Add(view.Actors[0]));
    }

    [Fact]
    public void Travel_is_recorded_on_the_timeline()
    {
        var s = NewSession();
        Travel(s, Granary);
        s.Advance(Duration.FromHours(1));

        var kinds = s.GetWorldView().RecentFacts.Select(f => f.Kind).ToArray();
        Assert.Equal(new[] { "TravelStarted", "TravelCompleted" }, kinds);
    }

    private static string Snapshot(SimulationSession s) => JsonSerializer.Serialize(s.GetWorldView());
}
