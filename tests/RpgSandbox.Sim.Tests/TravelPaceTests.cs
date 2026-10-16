using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>R5b: travel pace and extended travel (SRD 5.2.1 Travel Pace, Extended Travel).</summary>
public class TravelPaceTests
{
    private static readonly LocationId Town = new("town"), Ford = new("ford"), Far = new("far");

    /// <summary>Three places off the maps: town–ford 4 hours, ford–far 6 hours, at the normal pace. The player in town at 06:00.</summary>
    private static SimulationSession Road()
    {
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(5)
            .AddArea("a", "A")
            .AddLocation("town", "Città", "a").AddLocation("ford", "Guado", "a").AddLocation("far", "Lontano", "a")
            .AddWater("town").AddWater("ford").AddWater("far")
            .AddRoute("town", "ford", Duration.FromHours(4)).AddRoute("ford", "far", Duration.FromHours(6))
            .AddActor("player", "Paladino", "town", isPlayer: true, food: 5)
            .Build());
        s.AdvanceTo(At(1, 6));
        return s;
    }

    private static void Go(SimulationSession s, LocationId to, TravelPace pace = TravelPace.Normal)
    {
        var trip = s.Execute(new TravelCommand { Actor = s.Player, Destination = to, Pace = pace });
        Assert.True(trip.Success, trip.Message);
        s.AdvanceUntilCompleted(trip.Action!.Value, Duration.FromHours(12));
    }

    [Fact]
    public void A_fast_pace_takes_three_quarters_of_the_time_and_a_slow_one_half_again()
    {
        var fast = Road();
        var trip = fast.Execute(new TravelCommand { Actor = fast.Player, Destination = Ford, Pace = TravelPace.Fast });
        Assert.Equal(fast.Now.Plus(Duration.FromHours(3)), trip.CompletesAt);
        Assert.Equal(TravelPace.Fast, fast.GetPlayerView().Travel!.Pace);

        var slow = Road();
        Assert.Equal(slow.Now.Plus(Duration.FromHours(6)),
            slow.Execute(new TravelCommand { Actor = slow.Player, Destination = Ford, Pace = TravelPace.Slow }).CompletesAt);
    }

    [Fact]
    public void Past_eight_hours_of_travel_each_hour_calls_for_a_constitution_save()
    {
        var s = Road();
        s.Engine.World.Actors[s.Player].Sheet = s.Engine.World.Actors[s.Player].Sheet with { Constitution = 1 }; // −5
        Go(s, Ford); // 4 hours
        Assert.Equal(4, s.GetPlayerView().TravelHoursToday, 3);
        Assert.Empty(s.GetWorldView().RecentFacts.Where(f => f.Kind == "SavingThrow"));
        Go(s, Far); // 6 more: hours 9 and 10 call for saves, DC 11 and 12
        Assert.Equal(10, s.GetPlayerView().TravelHoursToday, 3);
        var saves = s.GetWorldView().RecentFacts.Where(f => f.Kind == "SavingThrow" && f.Description.Contains("marcia forzata")).ToList();
        Assert.Equal(2, saves.Count);
        Assert.Equal(saves.Count(f => f.Description.Contains("fallito")), s.GetPlayerView().Exhaustion);

        s.AdvanceTo(At(2, 1));
        Assert.Equal(0, s.GetPlayerView().TravelHoursToday);
    }

    [Fact]
    public void A_journey_saved_on_the_way_keeps_its_pace()
    {
        var s = Road();
        Assert.True(s.Execute(new TravelCommand { Actor = s.Player, Destination = Ford, Pace = TravelPace.Slow }).Success);
        s.Advance(Duration.FromHours(1));
        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(TravelPace.Slow, loaded.GetPlayerView().Travel!.Pace);
    }
}
