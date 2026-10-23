using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>D1b: a scenario as a data file, the same world as its C# twin.</summary>
public class ScenarioFileTests
{
    [Fact]
    public void The_living_village_from_its_file_is_the_same_world_as_the_one_built_in_code()
    {
        var fromFile = SimulationSession.Create(ScenarioFile.Load("living-village"));
        var fromCode = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());

        Assert.Equal(fromCode.SaveToString(), fromFile.SaveToString());

        // And they live the same two days, raid included.
        fromFile.AdvanceTo(At(3, 0));
        fromCode.AdvanceTo(At(3, 0));
        Assert.Equal(fromCode.SaveToString(), fromFile.SaveToString());
    }

    [Fact]
    public void The_default_pack_offers_its_scenarios()
    {
        Assert.Contains("living-village", ScenarioFile.Ids());
        Assert.Throws<InvalidDataException>(() => ScenarioFile.Load("nowhere"));
    }

    [Fact]
    public void A_scenario_file_breaking_the_rules_is_refused_like_one_built_in_code()
    {
        var files = ContentPack.Default.Files.ToDictionary(f => f.Name, f => f.Text);
        var json = JsonNode.Parse(files["scenarios/living-village.json"])!;
        json["scenario"]!["actors"]![1]!["build"] = "dragon"; // the guard made of nothing
        files["scenarios/living-village.json"] = json.ToJsonString();

        var error = Assert.ThrowsAny<Exception>(() => ScenarioFile.Load("living-village", ContentPack.Load(files)));
        Assert.Contains("dragon", error.Message);
    }

    [Fact]
    public void A_time_of_day_must_be_hours_and_minutes()
    {
        var files = ContentPack.Default.Files.ToDictionary(f => f.Name, f => f.Text);
        var json = JsonNode.Parse(files["scenarios/living-village.json"])!;
        json["scenario"]!["actors"]![2]!["work"]!["from"] = "one o'clock";
        files["scenarios/living-village.json"] = json.ToJsonString();

        var error = Assert.Throws<InvalidDataException>(() => ScenarioFile.Load("living-village", ContentPack.Load(files)));
        Assert.Contains("HH:MM", error.Message);
    }

    [Theory]
    [InlineData("doors", "[{ \"at\": [7, 3], \"open\": \"yes\" }]", "open")]
    [InlineData("lights", "[{ \"at\": [3], \"bright\": 15, \"dim\": 30 }]", "[x, y]")]
    [InlineData("posts", "{ \"inn\": \"Rest\" }", "posts")]
    [InlineData("rows", "[\"####\", 4]", "riga")]
    [InlineData("obstacles", "[[10]]", "[x, y]")]
    public void A_scenario_file_of_the_wrong_shape_says_what_is_wrong(string field, string value, string expected)
    {
        var files = ContentPack.Default.Files.ToDictionary(f => f.Name, f => f.Text);
        var json = JsonNode.Parse(files["scenarios/living-village.json"])!;
        json["scenario"]!["maps"]![0]![field] = JsonNode.Parse(value);
        files["scenarios/living-village.json"] = json.ToJsonString();

        var error = Assert.Throws<InvalidDataException>(() => ScenarioFile.Load("living-village", ContentPack.Load(files)));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void Obstacles_in_a_scenario_file_reserve_floor_squares()
    {
        var map = SimulationSession.Create(ScenarioFile.Load(BlackmarshWatchhouse.ScenarioId)).Engine.World.Maps[BlackmarshWatchhouse.Ids.Island];
        Assert.Contains(BlackmarshWatchhouse.Ids.Table, map.Occupied);
        Assert.False(map.IsWalkable(BlackmarshWatchhouse.Ids.Table));
        Assert.False(map.BlocksSight(BlackmarshWatchhouse.Ids.Table)); // furniture, not a wall
    }

    [Theory]
    [InlineData("[[10, 6], [10, 6]]", "Duplicate")]
    [InlineData("[[11, 4]]", "store")]
    [InlineData("[[0, 0]]", "muro")]
    [InlineData("[[6, 6]]", "porta")]
    [InlineData("[[11, 5]]", "access")]
    [InlineData("[[1, 6]]", "blocked")]
    public void An_obstacle_where_it_does_not_fit_is_refused(string obstacles, string expected)
    {
        var files = ContentPack.Default.Files.ToDictionary(f => f.Name, f => f.Text);
        var json = JsonNode.Parse(files["scenarios/blackmarsh-watchhouse.json"])!;
        json["scenario"]!["maps"]![0]!["obstacles"] = JsonNode.Parse(obstacles);
        files["scenarios/blackmarsh-watchhouse.json"] = json.ToJsonString();

        var error = Assert.ThrowsAny<Exception>(() => ScenarioFile.Load(BlackmarshWatchhouse.ScenarioId, ContentPack.Load(files)));
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_names_for_the_same_file_are_refused()
    {
        var files = ContentPack.Default.Files.ToDictionary(f => f.Name, f => f.Text);
        files[@"srd\items.json"] = files["srd/items.json"];

        var error = Assert.Throws<InvalidDataException>(() => ContentPack.Load(files));
        Assert.Contains("ripetuto", error.Message);
    }
}
