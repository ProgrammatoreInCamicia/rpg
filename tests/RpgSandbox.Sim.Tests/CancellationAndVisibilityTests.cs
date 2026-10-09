using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.SliceScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>S1/S2 (cancellation semantics, F6, F10) and S4 (outward visibility, F5) agreed with Codex.</summary>
public class CancellationAndVisibilityTests
{
    private static SimulationSession NewSession() => SimulationSession.Create(SliceScenario.Create());

    private static void PlayerGoesTo(SimulationSession s, LocationId destination)
    {
        var travel = s.Execute(new TravelCommand { Actor = s.Player, Destination = destination });
        s.AdvanceUntilCompleted(travel.Action!.Value, Duration.FromHours(2));
    }

    /// <summary>The player saw the 09:33 theft and is back at the inn at 09:39, where the guard rests until 10:00.</summary>
    private static SimulationSession WitnessBackAtInn()
    {
        var s = NewSession();
        PlayerGoesTo(s, Granary);
        s.AdvanceTo(At(1, 9, 34));
        PlayerGoesTo(s, Inn);
        return s;
    }

    // ---------------------------------------------------------------- S1/S2 — F6

    [Fact]
    public void Awaiting_a_wait_that_gets_interrupted_reports_cancelled_not_completed()
    {
        var s = WitnessBackAtInn();
        var guardWait = s.GetWorldView().Actor(Guard).Action!;
        Assert.Equal("Wait", guardWait.Kind);
        var observation = s.GetPlayerView().Observations.Single().Id;
        s.Execute(new ReportCommand { Actor = s.Player, Recipient = Guard, Observation = observation }); // ends 09:44

        var result = s.AdvanceUntilCompleted(guardWait.Id, Duration.FromHours(1));

        Assert.Equal(AdvanceOutcome.Cancelled, result.Outcome);
        Assert.Equal(At(1, 9, 44), result.Now); // when the report interrupted it, not at its old 10:00 deadline
        Assert.NotEqual(guardWait.Id, s.GetWorldView().Actor(Guard).Action!.Id);
    }

    [Fact]
    public void An_action_cancelled_earlier_in_the_same_instant_is_not_completed()
    {
        // Staged with engine internals: the report and the listener's wait end at the same instant, and the
        // report (scheduled first) interrupts the wait whose deadline was already taken in the same batch.
        var scenario = new ScenarioBuilder()
            .AddArea("a", "A").AddLocation("x", "X", "a")
            .AddStore("s", "Scorte", "x", 5)
            .AddActor("p", "P", "x", isPlayer: true)
            .AddActor("n", "N", "x")
            .Build();
        var s = SimulationSession.Create(scenario);
        var engine = s.Engine;
        var player = engine.World.Actors[s.Player];
        var listener = engine.World.Actors[new ActorId("n")];
        var seen = new Observation
        {
            Id = new ObservationId(engine.World.NextObservationId++), Origin = new ObservationId(1),
            Store = new StoreId("s"), StoreName = "Scorte", Location = new LocationId("x"), Amount = 1,
            ObservedAt = GameTime.Start, LearnedAt = GameTime.Start,
        };
        player.Knowledge.Add(seen);

        var report = s.Execute(new ReportCommand { Actor = s.Player, Recipient = listener.Id, Observation = seen.Id });
        engine.CancelAction(listener);
        var wait = engine.StartWait(listener.Id, Duration.FromMinutes(5), interruptible: true, description: "Riposa");
        Assert.Equal(report.CompletesAt, wait.CompletesAt);

        var result = s.AdvanceUntilCompleted(wait.Action!.Value, Duration.FromHours(1));

        Assert.Equal(AdvanceOutcome.Cancelled, result.Outcome);
        Assert.Equal(report.CompletesAt, result.Now);
        Assert.Single(listener.Knowledge);
    }

    [Fact]
    public void Cancelling_leaves_no_deadline_behind()
    {
        var s = WitnessBackAtInn();
        var observation = s.GetPlayerView().Observations.Single().Id;
        var report = s.Execute(new ReportCommand { Actor = s.Player, Recipient = Guard, Observation = observation });
        s.AdvanceUntilCompleted(report.Action!.Value, Duration.FromHours(1));

        var save = JsonNode.Parse(s.SaveToString())!;
        var activeIds = save["Actors"]!.AsArray()
            .Select(a => a!["Action"]?["Id"]?.GetValue<long>())
            .Where(id => id is not null).ToHashSet();
        var scheduled = save["Schedule"]!.AsArray()
            .Where(e => (string?)e!["Job"] == "CompleteAction")
            .Select(e => e!["Action"]!.GetValue<long>()).ToList();

        Assert.Equal(activeIds.Count, scheduled.Count);
        Assert.All(scheduled, id => Assert.Contains(id, activeIds));
    }

    [Fact]
    public void Advancing_after_a_cancellation_matches_save_and_reload()
    {
        var continuous = WitnessBackAtInn();
        var reloaded = WitnessBackAtInn();
        foreach (var s in new[] { continuous, reloaded })
        {
            var observation = s.GetPlayerView().Observations.Single().Id;
            s.Execute(new ReportCommand { Actor = s.Player, Recipient = Guard, Observation = observation });
        }
        reloaded.AdvanceTo(At(1, 9, 50));
        reloaded = reloaded.SaveAndReload();

        continuous.AdvanceTo(At(2, 12));
        reloaded.AdvanceTo(At(2, 12));
        Assert.Equal(continuous.SaveToString(), reloaded.SaveToString());
    }

    // ---------------------------------------------------------------- S1 — F10

    private static JsonNode SaveDuringRaid()
    {
        var s = NewSession();
        s.AdvanceTo(At(1, 9, 15));
        return JsonNode.Parse(s.SaveToString())!;
    }

    [Fact]
    public void F10_two_active_actions_with_the_same_id_are_rejected()
    {
        var save = SaveDuringRaid();
        var actors = save["Actors"]!.AsArray();
        var raiderAction = actors.Single(a => (string?)a!["Id"] == Raider.Value)!["Action"]!;
        var farmerAction = actors.Single(a => (string?)a!["Id"] == Farmer.Value)!["Action"]!;
        farmerAction["Id"] = raiderAction["Id"]!.GetValue<long>();

        var result = LoadFromString(save.ToJsonString());

        Assert.False(result.Success);
        Assert.Contains("stesso ID", result.Error);
    }

    [Fact]
    public void F10_a_deadline_for_no_active_action_is_rejected()
    {
        var save = SaveDuringRaid();
        var orphan = save["Schedule"]!.AsArray().First(e => (string?)e!["Job"] == "CompleteAction")!.DeepClone();
        orphan["Action"] = 999_999;
        orphan["Sequence"] = save["NextSequence"]!.GetValue<long>();
        save["NextSequence"] = orphan["Sequence"]!.GetValue<long>() + 1;
        save["Schedule"]!.AsArray().Add(orphan);

        var result = LoadFromString(save.ToJsonString());

        Assert.False(result.Success);
        Assert.Contains("non è in corso", result.Error);
    }

    // ---------------------------------------------------------------- S4 — F5

    [Fact]
    public void Others_are_seen_doing_gestures_not_intentions()
    {
        var s = NewSession();
        PlayerGoesTo(s, Granary);

        s.AdvanceTo(At(1, 9, 31)); // the raider is taking food
        var raider = s.GetPlayerView().VisibleActors.Single(a => a.Id == Raider);

        Assert.Equal("armeggia con le scorte", raider.Doing);
        Assert.DoesNotContain(s.GetPlayerView().VisibleActors, a => a.Doing?.Contains("Furto") == true);
    }

    [Fact]
    public void Nothing_is_seen_of_what_happens_elsewhere()
    {
        var s = NewSession(); // the player stays at the inn

        s.AdvanceTo(At(1, 9, 31));
        var raider = s.GetPlayerView().VisibleActors.Single(a => a.Id == Raider); // same area, other place

        Assert.Null(raider.Doing);
    }

    [Fact]
    public void Conversations_show_who_talks_not_the_topic()
    {
        // The farmer (morning shift) sees the theft and walks to the inn to tell the guard, 09:39–09:44.
        var s = SimulationSession.Create(SliceScenario.Build(Duration.FromHours(8), Duration.FromHours(17)));

        s.AdvanceTo(At(1, 9, 41)); // the player never left the inn
        var farmer = s.GetPlayerView().VisibleActors.Single(a => a.Id == Farmer);

        Assert.Equal("parla con Guardia", farmer.Doing);
        Assert.Empty(s.GetPlayerView().Observations); // watching them talk teaches the player nothing
    }

    [Fact]
    public void Travellers_are_never_in_the_same_place()
    {
        var s = NewSession();
        s.AdvanceTo(At(1, 9, 1)); // the raider walks from the camp to the granary
        var travel = s.Execute(new TravelCommand { Actor = s.Player, Destination = Granary });
        s.Advance(Duration.FromMinutes(1)); // both on the road, both with no location

        Assert.All(s.GetPlayerView().VisibleActors, a => Assert.Null(a.Doing));
        s.AdvanceUntilCompleted(travel.Action!.Value, Duration.FromHours(1));
    }

    [Fact]
    public void While_travelling_the_player_still_sees_the_area_it_left()
    {
        var s = NewSession();
        PlayerGoesTo(s, Granary);
        s.Execute(new TravelCommand { Actor = s.Player, Destination = BanditCamp });
        s.Advance(Duration.FromMinutes(10)); // on the road to the camp

        var view = s.GetPlayerView();
        Assert.Equal(Village, view.Area);
        Assert.DoesNotContain(view.VisibleStores, x => x.Id == CampStore);

        s.Advance(Duration.FromMinutes(25)); // arrived
        Assert.Equal(Forest, s.GetPlayerView().Area);
        Assert.Contains(s.GetPlayerView().VisibleStores, x => x.Id == CampStore);
    }
}
