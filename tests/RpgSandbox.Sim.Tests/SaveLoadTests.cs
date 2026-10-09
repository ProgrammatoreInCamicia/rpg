using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.SliceScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

public class SaveLoadTests
{
    private static SimulationSession NewSession() => SimulationSession.Create(SliceScenario.Create());

    [Fact]
    public void Save_load_save_is_identical()
    {
        var s = NewSession();
        s.AdvanceTo(At(1, 9, 15));

        var first = s.SaveToString();
        var second = s.SaveAndReload().SaveToString();

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The decisive test: playing straight through and saving/loading along the way end in the same world,
    /// including reloads in the middle of a raid, a theft and a delivery.
    /// </summary>
    [Theory]
    [InlineData(1, 9, 15)]  // raider walking to the granary
    [InlineData(1, 9, 31)]  // raider stealing
    [InlineData(1, 10, 4)]  // raider depositing the loot
    [InlineData(2, 7, 59)]  // just before upkeep
    public void Continuous_play_equals_save_and_reload(int day, int hour, int minute)
    {
        var continuous = NewSession();
        var reloaded = NewSession();
        StartPlayerTrip(continuous);
        StartPlayerTrip(reloaded);

        reloaded.AdvanceTo(At(day, hour, minute));
        reloaded = reloaded.SaveAndReload();

        continuous.AdvanceTo(At(4, 12));
        reloaded.AdvanceTo(At(4, 12));
        Assert.Equal(continuous.SaveToString(), reloaded.SaveToString());
    }

    [Fact]
    public void Player_travel_in_progress_survives_a_reload()
    {
        var s = NewSession();
        var travel = s.Execute(new TravelCommand { Actor = s.Player, Destination = Granary });
        s.Advance(Duration.FromMinutes(2));

        var loaded = s.SaveAndReload();
        var result = loaded.AdvanceUntilCompleted(travel.Action!.Value, Duration.FromHours(1));

        Assert.Equal(AdvanceOutcome.Completed, result.Outcome);
        Assert.Equal(At(1, 0, 5), loaded.Now);
        Assert.Equal(Granary, loaded.GetWorldView().Actor(Player).Location);
    }

    [Fact]
    public void Garbage_is_rejected_with_a_readable_error()
    {
        var result = LoadFromString("this is not a save");

        Assert.False(result.Success);
        Assert.Null(result.Session);
        Assert.Contains("danneggiato", result.Error);
    }

    [Fact]
    public void Other_versions_are_rejected()
    {
        var json = JsonNode.Parse(NewSession().SaveToString())!;
        json["Version"] = 99;

        var result = LoadFromString(json.ToJsonString());

        Assert.False(result.Success);
        Assert.Contains("Versione", result.Error);
    }

    [Fact]
    public void Inconsistent_references_are_rejected()
    {
        var json = JsonNode.Parse(NewSession().SaveToString())!;
        json["Actors"]![0]!["Location"] = "atlantis";

        var result = LoadFromString(json.ToJsonString());

        Assert.False(result.Success);
        Assert.Contains("incoerenti", result.Error);
    }

    [Fact]
    public void Action_without_a_deadline_is_rejected()
    {
        var s = NewSession();
        s.Execute(new TravelCommand { Actor = s.Player, Destination = Granary });
        var json = JsonNode.Parse(s.SaveToString())!;
        var schedule = json["Schedule"]!.AsArray();
        foreach (var entry in schedule.Where(e => (string?)e!["Job"] == "CompleteAction").ToList())
            schedule.Remove(entry);

        var result = LoadFromString(json.ToJsonString());

        Assert.False(result.Success);
        Assert.Contains("senza scadenza", result.Error);
    }

    [Fact]
    public void A_failed_load_leaves_the_current_game_untouched()
    {
        var s = NewSession();
        s.AdvanceTo(At(1, 9, 15));
        var before = s.SaveToString();

        var result = LoadFromString("{ \"Version\": 1, \"Player\": \"nobody\" }");

        Assert.False(result.Success);
        Assert.Equal(before, s.SaveToString());
    }

    private static void StartPlayerTrip(SimulationSession s) =>
        s.Execute(new TravelCommand { Actor = s.Player, Destination = Granary });
}
