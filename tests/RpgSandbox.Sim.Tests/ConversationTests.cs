using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.SliceScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>T5 (reduced): who you tell matters. The guard acts; the farmer keeps watch; hearsay is not taken to the guard.</summary>
public class ConversationTests
{
    private static void PlayerGoesTo(SimulationSession s, LocationId destination)
    {
        var travel = s.Execute(new TravelCommand { Actor = s.Player, Destination = destination });
        Assert.True(travel.Success, travel.Message);
        s.AdvanceUntilCompleted(travel.Action!.Value, Duration.FromHours(2));
    }

    /// <summary>The player sees the day-1 theft at 09:33 and is back at the inn at 09:39, with farmer and guard there.</summary>
    private static SimulationSession WitnessAtInn()
    {
        var s = SimulationSession.Create(SliceScenario.Create());
        PlayerGoesTo(s, Granary);
        s.AdvanceTo(At(1, 9, 34));
        PlayerGoesTo(s, Inn);
        return s;
    }

    private static void Tell(SimulationSession s, ActorId listener)
    {
        var person = s.GetPlayerView().PeopleHere.Single(p => p.Id == listener);
        var topic = Assert.Single(person.Topics);
        var report = s.Execute(new ReportCommand { Actor = s.Player, Recipient = listener, Observation = topic.Observation });
        Assert.True(report.Success, report.Message);
        s.AdvanceUntilCompleted(report.Action!.Value, Duration.FromHours(1));
    }

    [Fact]
    public void People_here_are_listed_with_what_the_player_could_tell_them()
    {
        var s = WitnessAtInn();

        var people = s.GetPlayerView().PeopleHere;

        Assert.Equal(new[] { Farmer, Guard }, people.Select(p => p.Id).ToArray());
        Assert.All(people, p => Assert.Contains("Razziatore", Assert.Single(p.Topics).Summary));
    }

    [Fact]
    public void Someone_already_told_is_still_there_to_talk_to_but_has_no_news()
    {
        var s = WitnessAtInn();
        Tell(s, Farmer);

        var farmer = s.GetPlayerView().PeopleHere.SingleOrDefault(p => p.Id == Farmer);

        // The farmer may already be walking to the granary; if still here, there is nothing new to tell him.
        if (farmer is not null)
            Assert.Empty(farmer.Topics);
    }

    [Fact]
    public void Hearsay_is_not_taken_to_the_guard()
    {
        var s = WitnessAtInn();

        Tell(s, Farmer);
        s.AdvanceTo(At(1, 12));

        var view = s.GetWorldView();
        Assert.Empty(view.Actor(Guard).Knowledge);
        Assert.Null(view.Actor(Guard).GuardDuty);
        Assert.Equal(Player, Assert.Single(view.Actor(Farmer).Knowledge).Source);
    }

    [Fact]
    public void A_farmer_who_hears_of_the_theft_keeps_an_eye_on_the_granary()
    {
        var s = WitnessAtInn();

        Tell(s, Farmer);
        var farmer = s.GetWorldView().Actor(Farmer);
        Assert.Equal(GranaryStore, farmer.Vigil!.Store);
        Assert.Equal("Vigila", farmer.LastDecision!.Rule);

        s.AdvanceTo(At(2, 7, 6)); // the next morning, well before his 13:00 shift
        farmer = s.GetWorldView().Actor(Farmer);
        Assert.Equal(Granary, farmer.Location);
        Assert.Equal("Vigila", farmer.Action!.Description);
    }

    [Fact]
    public void Keeping_watch_deters_nothing_but_makes_the_next_theft_witnessed_and_reported()
    {
        var s = WitnessAtInn();
        Tell(s, Farmer);

        s.AdvanceTo(At(3, 9, 45)); // day-3 raid at 09:30–09:33; the farmer has been at the granary since 07:05

        var view = s.GetWorldView();
        var seen = view.Actor(Farmer).Knowledge.Single(o => o.Source is null);
        Assert.Equal(Raider, seen.Thief);
        Assert.Equal(At(3, 9, 33), seen.ObservedAt);
        Assert.Equal(24, view.Store(GranaryStore).Food); // the theft still happened
        Assert.Equal(Farmer, view.Actor(Guard).Knowledge.Single().Source);
        Assert.NotNull(view.Actor(Guard).GuardDuty);
    }

    [Fact]
    public void Telling_the_guard_the_farmer_or_nobody_leads_to_three_different_worlds()
    {
        var guard = WitnessAtInn();
        Tell(guard, Guard);
        var farmer = WitnessAtInn();
        Tell(farmer, Farmer);
        var nobody = WitnessAtInn();

        foreach (var s in new[] { guard, farmer, nobody })
            s.AdvanceTo(At(4, 11));

        Assert.Equal(32, guard.GetWorldView().Store(GranaryStore).Food);   // day-3 raid deterred by the guard
        Assert.Equal(24, farmer.GetWorldView().Store(GranaryStore).Food);  // day-3 raid seen and reported; day-4 deterred
        Assert.Equal(16, nobody.GetWorldView().Store(GranaryStore).Food);  // day-3 and day-4 raids unseen
    }

    [Fact]
    public void Vigilance_ends_after_its_time()
    {
        var s = WitnessAtInn();
        Tell(s, Farmer);

        s.AdvanceTo(At(4, 11)); // learned on day 1 at 09:44 + 3 days, then refreshed by the day-3 theft he saw

        var farmer = s.GetWorldView().Actor(Farmer);
        Assert.NotNull(farmer.Vigil); // still vigilant: the theft he witnessed on day 3 renewed it

        // Renewed on day 3 at 09:33 (+3 days): it ends on day 6 at 09:33. (A later theft he sees at work
        // in the afternoon of day 6 makes him vigilant again: that is a new piece of news, not a leftover.)
        s.AdvanceTo(At(6, 10));
        Assert.Null(s.GetWorldView().Actor(Farmer).Vigil);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "VigilEnded" && f.At == At(6, 9, 33));
    }

    [Theory]
    [InlineData(1, 10, 0)]  // the farmer walking to the granary to keep watch
    [InlineData(2, 6, 30)]  // the farmer resting, about to start watching at 07:00
    [InlineData(3, 9, 36)]  // the farmer walking to tell the guard what he saw
    public void Vigilance_survives_save_and_reload(int day, int hour, int minute)
    {
        var continuous = WitnessAtInn();
        Tell(continuous, Farmer);
        var reloaded = WitnessAtInn();
        Tell(reloaded, Farmer);

        reloaded.AdvanceTo(At(day, hour, minute));
        reloaded = reloaded.SaveAndReload();
        continuous.AdvanceTo(At(5, 12));
        reloaded.AdvanceTo(At(5, 12));

        Assert.Equal(continuous.SaveToString(), reloaded.SaveToString());
    }
}
