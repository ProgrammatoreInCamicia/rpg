using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>R5a: eating and drinking (SRD 5.2.1 Malnutrition, Dehydration; Rations, Waterskin).</summary>
public class NeedsTests
{
    /// <summary>
    /// A yard of two places: the open yard (3,1) where the player stands, and a well (8,1) with water. The player carries
    /// <paramref name="food"/> rations and <paramref name="skins"/> waterskins.
    /// </summary>
    private static SimulationSession Yard(int food = 3, int skins = 1, bool atWell = false)
    {
        var items = new List<(string, int)>();
        if (food > 0)
            items.Add(("ration", food));
        if (skins > 0)
            items.Add(("waterskin", skins));
        return SimulationSession.Create(new ScenarioBuilder().WithSeed(1)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a").AddLocation("well", "Pozzo", "a").AddWater("well")
            .AddMap("a", new[] { "YYYYYYWWWW", "YYYYYYWWWW" }, new Dictionary<char, string> { ['Y'] = "yard", ['W'] = "well" })
            .AddActor("player", "Paladino", atWell ? "well" : "yard", isPlayer: true, at: atWell ? (8, 1) : (3, 1), items: items)
            .Build());
    }

    private static Actor Me(SimulationSession s) => s.Engine.World.Actors[s.Player];

    [Fact]
    public void A_ration_is_eaten_at_each_midnight()
    {
        var s = Yard(food: 3);
        s.AdvanceTo(At(2, 1));
        Assert.Equal(2, s.GetPlayerView().Food);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "Ate");
        Assert.Equal(0, s.GetPlayerView().Hunger);
    }

    [Fact]
    public void Five_days_without_food_bring_exhaustion_that_rest_does_not_take_until_a_full_meal()
    {
        var s = Yard(food: 0, atWell: true);
        s.AdvanceTo(At(5, 1)); // four midnights
        Assert.Equal(4, s.GetPlayerView().Hunger);
        Assert.Equal(0, s.GetPlayerView().Exhaustion);
        s.AdvanceTo(At(7, 1)); // the 5th and 6th
        Assert.Equal(6, s.GetPlayerView().Hunger);
        Assert.Equal(2, s.GetPlayerView().Exhaustion);

        var rest = s.Execute(new RestCommand { Actor = s.Player, Long = true });
        s.AdvanceUntilCompleted(rest.Action!.Value, Duration.FromHours(9));
        Assert.Equal(2, s.GetPlayerView().Exhaustion); // hunger's levels stay

        Me(s).Food = 1;
        s.AdvanceTo(At(8, 1)); // a ration eaten at midnight
        Assert.Equal(0, s.GetPlayerView().Hunger);
        Me(s).LastLongRestEnd = null;
        rest = s.Execute(new RestCommand { Actor = s.Player, Long = true });
        s.AdvanceUntilCompleted(rest.Action!.Value, Duration.FromHours(9));
        Assert.Equal(1, s.GetPlayerView().Exhaustion);
    }

    [Fact]
    public void At_a_place_with_water_one_drinks_and_fills_the_waterskins()
    {
        var s = Yard(atWell: true);
        s.AdvanceTo(At(1, 1));
        Assert.Equal(new WaterView { Pints = 4, Capacity = 4, DrankToday = true }, s.GetPlayerView().Water);
        s.AdvanceTo(At(3, 1));
        Assert.Equal(0, s.GetPlayerView().Exhaustion);
    }

    [Fact]
    public void Away_from_water_a_waterskin_is_half_a_day_and_less_than_half_brings_exhaustion()
    {
        var s = Yard(skins: 1);
        Me(s).WaterPints = 4;
        s.AdvanceTo(At(2, 1)); // 4 pints of 8: exactly half, enough not to tire
        Assert.Equal(0, s.GetPlayerView().Water!.Pints);
        Assert.Equal(0, s.GetPlayerView().Exhaustion);
        s.AdvanceTo(At(3, 1)); // nothing left
        Assert.Equal(1, s.GetPlayerView().Exhaustion);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "Exhaustion" && f.Description.Contains("sete"));

        var dry = Yard(skins: 0);
        dry.AdvanceTo(At(2, 1));
        Assert.Equal(1, dry.GetPlayerView().Exhaustion);
    }

    [Fact]
    public void In_the_living_village_the_paladin_has_a_waterskin_filled_at_the_inn_and_needs_survive_a_save()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        s.AdvanceTo(At(1, 1));
        Assert.Equal(new WaterView { Pints = 4, Capacity = 4, DrankToday = true }, s.GetPlayerView().Water);
        Me(s).DaysWithoutFood = 2;
        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(2, loaded.GetPlayerView().Hunger);
    }
}
