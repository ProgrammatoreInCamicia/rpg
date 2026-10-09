using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.SliceScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>Regressions for the defects found in Codex's review of checkpoints 0–3 (docs/REVIEW_CHECKPOINTS_0_3.md).</summary>
public class ReviewRegressionTests
{
    private static SimulationSession NewSession() => SimulationSession.Create(SliceScenario.Create());

    // ---------------------------------------------------------------- F2

    [Fact]
    public void F2_a_raid_on_a_store_emptied_meanwhile_ends_and_the_faction_moves_on()
    {
        var s = NewSession();
        var travel = s.Execute(new TravelCommand { Actor = s.Player, Destination = Granary });
        s.AdvanceUntilCompleted(travel.Action!.Value, Duration.FromHours(1));
        s.AdvanceTo(At(1, 9, 26)); // the raid was ordered at 09:00; the raider arrives at 09:30

        var take = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = GranaryStore, Amount = 40 });
        s.AdvanceUntilCompleted(take.Action!.Value, Duration.FromHours(1));
        Assert.Equal(0, s.GetWorldView().Store(GranaryStore).Food);

        s.AdvanceTo(At(1, 9, 31));
        var raider = s.GetWorldView().Actor(Raider);
        Assert.Null(raider.Assignment);
        Assert.Equal("Bersaglio vuoto", raider.LastDecision!.Rule);

        s.AdvanceTo(At(1, 12, 1));
        var view = s.GetWorldView();
        Assert.Equal(BanditCamp, view.Actor(Raider).Location);
        Assert.Equal("Nessun bersaglio", view.Faction(Bandits).LastDecision!.Rule); // not stuck in "Razzia in corso"
    }

    // ---------------------------------------------------------------- F11

    [Fact]
    public void F11_deposits_never_overflow_a_store()
    {
        var s = SimulationSession.Create(new ScenarioBuilder()
            .AddArea("a", "A").AddLocation("x", "X", "a")
            .AddStore("s", "S", "x", int.MaxValue)
            .AddActor("p", "P", "x", isPlayer: true, food: 1)
            .Build());

        var result = s.Execute(new DepositFoodCommand { Actor = s.Player, Store = new StoreId("s"), Amount = 1 });

        Assert.Equal(RejectionReason.CapacityExceeded, result.Rejection);
        Assert.Equal(int.MaxValue, s.GetWorldView().Stores.Single().Food);
    }

    [Fact]
    public void F11_an_actor_cannot_carry_more_than_the_maximum()
    {
        var s = SimulationSession.Create(new ScenarioBuilder()
            .AddArea("a", "A").AddLocation("x", "X", "a")
            .AddStore("s", "S", "x", 10)
            .AddActor("p", "P", "x", isPlayer: true, food: int.MaxValue)
            .Build());

        var result = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = new StoreId("s"), Amount = 1 });

        Assert.Equal(RejectionReason.CapacityExceeded, result.Rejection);
    }

    // ---------------------------------------------------------------- F7–F9, R6: corrupted saves give errors, never exceptions

    /// <summary>A save taken while the raider walks to the granary, so it contains actions and periodic jobs.</summary>
    private static JsonNode SaveDuringRaid()
    {
        var s = NewSession();
        s.AdvanceTo(At(1, 9, 15));
        return JsonNode.Parse(s.SaveToString())!;
    }

    private static JsonNode RaiderAction(JsonNode save) =>
        save["Actors"]!.AsArray().Single(a => (string?)a!["Id"] == Raider.Value)!["Action"]!;

    private static JsonNode BanditsJson(JsonNode save) =>
        save["Factions"]!.AsArray().Single(f => (string?)f!["Id"] == SliceScenario.Ids.Bandits.Value)!;

    private static void AssertRejected(JsonNode save, string expectedFragment)
    {
        var result = LoadFromString(save.ToJsonString());
        Assert.False(result.Success);
        Assert.Contains(expectedFragment, result.Error);
    }

    [Fact]
    public void F7_an_action_without_its_kind_is_a_damaged_save()
    {
        var save = SaveDuringRaid();
        RaiderAction(save).AsObject().Remove("kind");

        AssertRejected(save, "danneggiato");
    }

    [Fact]
    public void F7_an_action_of_unknown_kind_is_a_damaged_save()
    {
        var save = SaveDuringRaid();
        RaiderAction(save)["kind"] = "Teleport";

        AssertRejected(save, "danneggiato");
    }

    [Fact]
    public void F7_every_action_kind_round_trips()
    {
        // Travel, Wait, Report, Guard, TakeFood and DepositFood all occur in this session.
        var s = SimulationSession.Create(SliceScenario.Build(Duration.FromHours(8), Duration.FromHours(17)));
        var kinds = new HashSet<string>();
        for (var minute = 0; minute < 36 * 60; minute += 1)
        {
            s.Advance(Duration.FromMinutes(1));
            foreach (var actor in s.GetWorldView().Actors)
                if (actor.Action is { } action)
                    kinds.Add(action.Kind);
            if (minute % 97 == 0)
                s = s.SaveAndReload();
        }

        Assert.Superset(new HashSet<string> { "Travel", "Wait", "Report", "Guard", "TakeFood", "DepositFood" }, kinds);
    }

    [Fact]
    public void F8_a_zero_evaluation_interval_is_rejected()
    {
        var save = SaveDuringRaid();
        BanditsJson(save)["Policy"]!["EvaluationInterval"] = 0;

        AssertRejected(save, "intervallo di valutazione");
    }

    [Fact]
    public void F8_an_evaluation_job_without_a_policy_is_rejected()
    {
        var save = SaveDuringRaid();
        BanditsJson(save).AsObject().Remove("Policy");

        AssertRejected(save, "politica");
    }

    [Fact]
    public void F9_missing_periodic_jobs_are_rejected()
    {
        var save = SaveDuringRaid();
        var schedule = save["Schedule"]!.AsArray();
        foreach (var entry in schedule.Where(e => (string?)e!["Job"] == "FactionUpkeep").ToList())
            schedule.Remove(entry);

        AssertRejected(save, "consumo giornaliero");
    }

    [Fact]
    public void R6_missing_collections_are_errors_not_crashes()
    {
        var save = SaveDuringRaid();
        save["Actors"] = null;

        AssertRejected(save, "attori");
    }

    [Fact]
    public void R6_null_entries_are_errors_not_crashes()
    {
        var save = SaveDuringRaid();
        save["Stores"]!.AsArray().Add(null);

        AssertRejected(save, "depositi");
    }

    [Fact]
    public void R6_duplicate_ids_are_errors_not_crashes()
    {
        var save = SaveDuringRaid();
        var stores = save["Stores"]!.AsArray();
        stores.Add(stores[0]!.DeepClone());

        AssertRejected(save, "duplicato");
    }
}
