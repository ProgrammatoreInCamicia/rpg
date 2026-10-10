using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.SliceScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>Theft -> observation -> report -> guard, with deterministic perception.</summary>
public class KnowledgeTests
{
    private static SimulationSession NewSession() => SimulationSession.Create(SliceScenario.Create());

    private static SimulationSession WithFarmerShift(int startHour, int startMinute, int endHour) =>
        SimulationSession.Create(SliceScenario.Build(
            Duration.FromMinutes(startHour * 60 + startMinute), Duration.FromHours(endHour)));

    private static void PlayerGoesTo(SimulationSession s, LocationId destination)
    {
        var travel = s.Execute(new TravelCommand { Actor = s.Player, Destination = destination });
        Assert.True(travel.Success, travel.Message);
        s.AdvanceUntilCompleted(travel.Action!.Value, Duration.FromHours(2));
    }

    private static void PlayerReportsToGuard(SimulationSession s)
    {
        var option = s.GetPlayerView().ReportOptions.Single(o => o.Recipient == Guard);
        var report = s.Execute(new ReportCommand { Actor = s.Player, Recipient = Guard, Observation = option.Observation });
        Assert.True(report.Success, report.Message);
        s.AdvanceUntilCompleted(report.Action!.Value, Duration.FromHours(1));
    }

    /// <summary>The player sees the 09:30–09:33 raid from the granary, then tells the guard at the inn.</summary>
    private static SimulationSession PlayerWitnessesAndReports()
    {
        var s = NewSession();
        PlayerGoesTo(s, Granary);
        s.AdvanceTo(At(1, 9, 34));
        PlayerGoesTo(s, Inn);
        PlayerReportsToGuard(s);
        return s;
    }

    // ---------------------------------------------------------------- perception

    [Fact]
    public void Nobody_sees_the_morning_raid_in_the_base_game()
    {
        var s = NewSession();

        s.AdvanceTo(At(1, 12));

        var view = s.GetWorldView();
        Assert.DoesNotContain(view.RecentFacts, f => f.Kind == "FoodTheftWitnessed");
        Assert.All(view.Actors, a => Assert.Empty(a.Knowledge));
    }

    [Fact]
    public void A_witness_present_from_the_start_recognises_the_thief()
    {
        var s = NewSession();
        PlayerGoesTo(s, Granary);

        s.AdvanceTo(At(1, 9, 34));

        var seen = Assert.Single(s.GetPlayerView().Observations);
        Assert.Equal(Raider, seen.Thief);
        Assert.Equal("Razziatore", seen.ThiefName);
        Assert.Equal(8, seen.Amount);
        Assert.Equal(At(1, 9, 33), seen.ObservedAt);
        Assert.Null(seen.Source);
    }

    [Fact]
    public void A_witness_arriving_during_the_theft_does_not_recognise_the_thief()
    {
        var s = NewSession();
        s.AdvanceTo(At(1, 9, 26));
        PlayerGoesTo(s, Granary); // arrives 09:31, the raider started taking at 09:30

        s.AdvanceTo(At(1, 9, 34));

        var seen = Assert.Single(s.GetPlayerView().Observations);
        Assert.Null(seen.Thief);
        Assert.Null(seen.ThiefName);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "FoodTheftWitnessed" && f.Description.Contains("non riconosce"));
    }

    [Fact]
    public void Observations_survive_pruning_of_the_timeline()
    {
        var s = NewSession();
        PlayerGoesTo(s, Granary);
        s.AdvanceTo(At(1, 9, 34));

        // Plenty of travelling fills the bounded timeline and pushes the theft out of it.
        for (var i = 0; i < 120; i++)
            PlayerGoesTo(s, i % 2 == 0 ? Inn : Granary);

        Assert.DoesNotContain(s.GetWorldView().RecentFacts, f => f.Kind == "FoodStolen" && f.At == At(1, 9, 33));
        Assert.Equal(Raider, Assert.Single(s.GetPlayerView().Observations, o => o.ObservedAt == At(1, 9, 33)).Thief);
    }

    // ---------------------------------------------------------------- reports and the guard

    [Fact]
    public void The_player_can_tell_the_guard_and_the_guard_reacts()
    {
        var s = PlayerWitnessesAndReports();

        var view = s.GetWorldView();
        var guard = view.Actor(Guard);
        var told = Assert.Single(guard.Knowledge);
        Assert.Equal(Player, told.Source);
        Assert.Equal(Raider, told.Thief);
        Assert.Equal("Organizza il presidio", guard.LastDecision!.Rule);
        Assert.Equal(GranaryStore, guard.GuardDuty!.Store);
        Assert.Equal(Granary, guard.Travel!.Destination);
    }

    [Fact]
    public void A_guarded_granary_deters_the_next_raid_and_the_bandits_learn_it_back_home()
    {
        var s = PlayerWitnessesAndReports();

        s.AdvanceTo(At(3, 9, 31)); // day-3 raid: the raider reaches the granary at 09:30
        var view = s.GetWorldView();
        Assert.Contains(view.RecentFacts, f => f.Kind == "RaidDeterred");
        Assert.Equal(32, view.Store(GranaryStore).Food); // only the day-1 raid succeeded
        Assert.Empty(view.Faction(Bandits).AvoidedTargets); // the raider has not told anyone yet

        s.AdvanceTo(At(3, 10, 1)); // back at the camp
        view = s.GetWorldView();
        Assert.Contains(view.RecentFacts, f => f.Kind == "RaidAborted");
        Assert.Equal(GranaryStore, Assert.Single(view.Faction(Bandits).AvoidedTargets).Store);

        s.AdvanceTo(At(3, 12));
        Assert.Equal("Bersaglio sorvegliato", s.GetWorldView().Faction(Bandits).LastDecision!.Rule);
    }

    [Fact]
    public void Waiting_delivering_and_reporting_lead_to_different_worlds()
    {
        var wait = NewSession();

        var deliver = NewSession();
        PlayerGoesTo(deliver, Granary);
        var deposit = deliver.Execute(new DepositFoodCommand { Actor = deliver.Player, Store = GranaryStore, Amount = 10 });
        deliver.AdvanceUntilCompleted(deposit.Action!.Value, Duration.FromHours(1));

        var report = PlayerWitnessesAndReports();

        foreach (var s in new[] { wait, deliver, report })
            s.AdvanceTo(At(4, 7));

        Assert.Equal(24, wait.GetWorldView().Store(GranaryStore).Food);     // raids on day 1 and day 3
        Assert.Equal(34, deliver.GetWorldView().Store(GranaryStore).Food);  // same raids, more food to start with
        Assert.Equal(32, report.GetWorldView().Store(GranaryStore).Food);   // day-3 raid deterred by the guard
        Assert.Equal(12, wait.GetWorldView().Store(CampStore).Food);
        Assert.Equal(4, report.GetWorldView().Store(CampStore).Food);       // the bandits go hungry instead
    }

    [Fact]
    public void A_farmer_who_sees_the_theft_goes_to_tell_the_guard()
    {
        var s = WithFarmerShift(8, 0, 17);

        s.AdvanceTo(At(1, 9, 34));
        var farmer = s.GetWorldView().Actor(Farmer);
        Assert.Equal(Raider, Assert.Single(farmer.Knowledge).Thief);
        Assert.Equal("Cerca la guardia", farmer.LastDecision!.Rule);

        s.AdvanceTo(At(1, 9, 45)); // 5 min to the inn, 5 min to tell
        var guard = s.GetWorldView().Actor(Guard);
        Assert.Equal(Farmer, Assert.Single(guard.Knowledge).Source);
        Assert.NotNull(guard.GuardDuty);
    }

    [Fact]
    public void An_unrecognised_thief_is_still_reported_and_guarded_against()
    {
        var s = WithFarmerShift(9, 26, 17); // the farmer walks in at 09:31, mid-theft

        s.AdvanceTo(At(1, 10));

        var view = s.GetWorldView();
        Assert.Null(Assert.Single(view.Actor(Farmer).Knowledge).Thief);
        Assert.Null(Assert.Single(view.Actor(Guard).Knowledge).Thief);
        Assert.NotNull(view.Actor(Guard).GuardDuty);
    }

    [Fact]
    public void Telling_the_same_thing_twice_is_not_new_evidence()
    {
        var s = PlayerWitnessesAndReports();
        Assert.DoesNotContain(s.GetPlayerView().ReportOptions, o => o.Recipient == Guard); // already told (and gone)

        var observation = s.GetPlayerView().Observations.Single().Id;
        PlayerGoesTo(s, Granary); // where the guard now stands
        Assert.DoesNotContain(s.GetPlayerView().ReportOptions, o => o.Recipient == Guard);
        var again = s.Execute(new ReportCommand { Actor = s.Player, Recipient = Guard, Observation = observation });
        s.AdvanceUntilCompleted(again.Action!.Value, Duration.FromHours(1));

        Assert.Single(s.GetWorldView().Actor(Guard).Knowledge);
        Assert.Contains("lo sapeva già", s.GetWorldView().RecentFacts[^1].Description);
    }

    [Fact]
    public void A_report_fails_if_the_listener_walks_away()
    {
        var s = NewSession();
        PlayerGoesTo(s, Granary);
        s.AdvanceTo(At(1, 9, 34));
        PlayerGoesTo(s, Inn);
        s.AdvanceTo(At(1, 12, 58)); // the farmer leaves for work at 13:00

        var observation = s.GetPlayerView().Observations.Single().Id;
        var report = s.Execute(new ReportCommand { Actor = s.Player, Recipient = Farmer, Observation = observation });
        Assert.True(report.Success);
        s.AdvanceUntilCompleted(report.Action!.Value, Duration.FromHours(1));

        Assert.Equal("ReportFailed", s.GetWorldView().RecentFacts.Last(f => f.Kind.StartsWith("Report") || f.Kind == "InformationShared").Kind);
        Assert.Empty(s.GetWorldView().Actor(Farmer).Knowledge);
    }

    [Fact]
    public void Report_preconditions_are_checked()
    {
        var s = NewSession();

        var unknown = s.Execute(new ReportCommand { Actor = s.Player, Recipient = Guard, Observation = new ObservationId(99) });
        var absent = s.Execute(new ReportCommand { Actor = s.Player, Recipient = Raider, Observation = new ObservationId(99) });

        Assert.Equal(RejectionReason.UnknownObservation, unknown.Rejection);
        Assert.Equal(RejectionReason.RecipientNotPresent, absent.Rejection);
    }

    [Fact]
    public void Nobody_can_steal_from_a_guarded_store()
    {
        var s = PlayerWitnessesAndReports();
        PlayerGoesTo(s, Granary);
        s.AdvanceTo(At(1, 10, 30)); // the guard is on duty here

        var take = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = GranaryStore, Amount = 1 });

        Assert.Equal(RejectionReason.StoreGuarded, take.Rejection);
    }

    // ---------------------------------------------------------------- the player's filtered view

    [Fact]
    public void The_player_view_hides_what_the_player_cannot_see()
    {
        var s = NewSession();

        var atInn = s.GetPlayerView();
        Assert.Equal(Village, atInn.Area);
        Assert.DoesNotContain(atInn.VisibleActors, a => a.Id == Raider);
        Assert.Contains(atInn.VisibleActors, a => a.Id == Guard);
        Assert.DoesNotContain(atInn.VisibleStores, x => x.Id == CampStore);
        Assert.Empty(atInn.Observations);

        s.AdvanceTo(At(1, 9, 15)); // the raider is walking from the forest to the village
        Assert.Contains(s.GetPlayerView().VisibleActors, a => a.Id == Raider && a.Travel is not null);
    }

    [Fact]
    public void Report_options_list_people_here_and_untold_observations()
    {
        var s = NewSession();
        PlayerGoesTo(s, Granary);
        s.AdvanceTo(At(1, 9, 34));
        PlayerGoesTo(s, Inn);

        var options = s.GetPlayerView().ReportOptions;

        Assert.Equal(new[] { Farmer, Guard }, options.Select(o => o.Recipient).ToArray());
        Assert.All(options, o => Assert.Contains("Razziatore", o.Summary));
        Assert.All(options, o => Assert.Contains("giorno 1 alle 09:33", o.Summary)); // thefts on different days must not look identical
    }

    // ---------------------------------------------------------------- persistence

    [Theory]
    [InlineData(1, 9, 41)]  // during the player's report
    [InlineData(1, 9, 47)]  // the guard walking to the granary
    [InlineData(2, 15, 0)]  // guard duty in progress
    [InlineData(3, 9, 45)]  // raider walking home with the bad news
    public void Knowledge_and_guard_duty_survive_save_and_reload(int day, int hour, int minute)
    {
        var continuous = PlayerWitnessesAndReports();
        var reloaded = PlayerWitnessesAndReports();
        if (At(day, hour, minute) > reloaded.Now)
            reloaded.AdvanceTo(At(day, hour, minute));
        reloaded = reloaded.SaveAndReload();

        continuous.AdvanceTo(At(5, 12));
        reloaded.AdvanceTo(At(5, 12));

        Assert.Equal(continuous.SaveToString(), reloaded.SaveToString());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)] // v3 forbids leftover deadlines of cancelled actions, which v2 allowed
    [InlineData(3)] // v4 adds vigilance
    [InlineData(4)] // v5 adds the random generator state and character sheets
    [InlineData(5)] // v6 adds lit places, perception mode, theft-keyed claims and cargo
    [InlineData(6)] // v7 adds maps, positions and walks
    [InlineData(7)] // v8 adds light sources
    [InlineData(8)] // v9 adds sneaking
    [InlineData(9)] // v10 adds doors and torches
    [InlineData(10)] // v11 adds the evidence fixed when a theft starts
    public void Older_save_versions_are_rejected(int version)
    {
        var json = JsonNode.Parse(NewSession().SaveToString())!;
        json["Version"] = version;

        var result = LoadFromString(json.ToJsonString());

        Assert.False(result.Success);
        Assert.Contains($"Versione del salvataggio non supportata: {version}", result.Error);
    }
}
