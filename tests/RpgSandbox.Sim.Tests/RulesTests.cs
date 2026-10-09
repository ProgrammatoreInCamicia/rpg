using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.SliceScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>T4a: random generator, SRD 5.2.1 D20 rules and character sheets.</summary>
public class RulesTests
{
    // ---------------------------------------------------------------- SplitMix64

    [Fact]
    public void SplitMix64_matches_the_reference_sequence()
    {
        // Reference outputs of Vigna's splitmix64.c for seed 1234567.
        var rng = new SplitMix64(1234567);
        var expected = new[]
        {
            6457827717110365317UL, 3203168211198807973UL, 9817491932198370423UL,
            4593380528125082431UL, 16408922859458223821UL,
        };

        Assert.Equal(expected, expected.Select(_ => rng.NextUInt64()).ToArray());
    }

    [Fact]
    public void SplitMix64_resumes_from_its_saved_state()
    {
        var original = new SplitMix64(42);
        for (var i = 0; i < 10; i++)
            original.NextUInt64();

        var resumed = new SplitMix64(original.State);

        Assert.Equal(Enumerable.Range(0, 20).Select(_ => original.NextUInt64()), Enumerable.Range(0, 20).Select(_ => resumed.NextUInt64()));
    }

    [Fact]
    public void Dice_stay_in_range_and_cover_every_face()
    {
        // Deterministic for a fixed seed: no statistical gate that could fail by chance.
        var rng = new SplitMix64(7);
        var faces = Enumerable.Range(0, 2000).Select(_ => rng.Roll(20)).ToList();

        Assert.All(faces, f => Assert.InRange(f, 1, 20));
        Assert.Equal(Enumerable.Range(1, 20), faces.Distinct().OrderBy(f => f));
    }

    // ---------------------------------------------------------------- abilities and D20 Tests

    [Theory]
    [InlineData(1, -5)] [InlineData(3, -4)] [InlineData(8, -1)] [InlineData(9, -1)] [InlineData(10, 0)]
    [InlineData(11, 0)] [InlineData(12, 1)] [InlineData(15, 2)] [InlineData(16, 3)] [InlineData(20, 5)] [InlineData(30, 10)]
    public void Ability_modifiers_follow_the_srd_table(int score, int modifier) =>
        Assert.Equal(modifier, Abilities.Modifier(score));

    [Fact]
    public void Advantage_keeps_the_higher_die_and_disadvantage_the_lower()
    {
        for (ulong seed = 0; seed < 50; seed++)
        {
            var adv = D20.Roll(new SplitMix64(seed), bonus: 3, advantage: true);
            var dis = D20.Roll(new SplitMix64(seed), bonus: 3, disadvantage: true);

            Assert.Equal(Math.Max(adv.First, adv.Second!.Value), adv.Kept);
            Assert.Equal(Math.Min(dis.First, dis.Second!.Value), dis.Kept);
            Assert.Equal(adv.Kept + 3, adv.Total);
        }
    }

    [Fact]
    public void Advantage_and_disadvantage_together_roll_a_single_die()
    {
        var roll = D20.Roll(new SplitMix64(1), bonus: 0, advantage: true, disadvantage: true);

        Assert.Null(roll.Second);
        Assert.False(roll.Advantage);
        Assert.False(roll.Disadvantage);
    }

    // ---------------------------------------------------------------- sheets

    [Fact]
    public void The_paladin_is_built_by_the_srd_rules()
    {
        var p = Sheets.Paladin();

        Assert.Equal(new[] { 15, 10, 13, 8, 13, 16 }, Enum.GetValues<Ability>().Select(p.Score).ToArray());
        Assert.Equal(2, p.ProficiencyBonus);
        Assert.Equal(4, p.Bonus(Skill.Athletics));   // Str +2, proficient
        Assert.Equal(5, p.Bonus(Skill.Persuasion));  // Cha +3, proficient
        Assert.Equal(0, p.Bonus(Skill.Stealth));     // Dex +0, not proficient
        Assert.True(p.StealthDisadvantage);          // Chain Mail
        Assert.Equal(11, p.PassivePerception());     // 10 + Wis +1
        Assert.Equal(18, p.ArmorClass);              // Chain Mail 16 + Shield 2
    }

    [Fact]
    public void Passive_perception_moves_by_five_with_advantage_or_disadvantage()
    {
        var guard = Sheets.VillageGuard();

        Assert.Equal(13, guard.PassivePerception());
        Assert.Equal(18, guard.PassivePerception(advantage: true));
        Assert.Equal(8, guard.PassivePerception(disadvantage: true));
        Assert.Equal(13, guard.PassivePerception(advantage: true, disadvantage: true));
    }

    [Fact]
    public void Sheets_handed_out_cannot_change_the_world()
    {
        var s = SimulationSession.Create(SliceScenario.Create());
        var skills = s.GetPlayerView().Sheet.SkillProficiencies;

        Assert.Throws<NotSupportedException>(() => ((IList<Skill>)skills)[0] = Skill.Stealth);
    }

    [Fact]
    public void Every_slice_character_has_a_sheet()
    {
        var view = SimulationSession.Create(SliceScenario.Create()).GetWorldView();

        Assert.Equal("Paladino 1 (Accolito)", view.Actor(Player).Sheet!.Title);
        Assert.Equal(4, view.Actor(Raider).Sheet!.Bonus(Skill.Stealth));
        Assert.Equal(13, view.Actor(Guard).Sheet!.PassivePerception());
        Assert.Equal(11, view.Actor(Farmer).Sheet!.PassivePerception());
    }

    [Fact]
    public void Invalid_sheets_are_rejected_by_scenarios()
    {
        var bad = Sheets.Paladin() with { Strength = 0 };

        Assert.Throws<InvalidOperationException>(() =>
            new ScenarioBuilder().AddArea("a", "A").AddLocation("x", "X", "a")
                .AddActor("p", "P", "x", isPlayer: true, sheet: bad).Build());
    }

    // ---------------------------------------------------------------- persistence

    [Fact]
    public void Generator_state_and_sheets_survive_save_and_reload()
    {
        var s = SimulationSession.Create(SliceScenario.Create());
        s.Engine.World.Rng.NextUInt64(); // move the generator off its seed

        var loaded = s.SaveAndReload();

        Assert.Equal(s.Engine.World.Rng.State, loaded.Engine.World.Rng.State);
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
    }

    [Fact]
    public void An_unknown_generator_is_rejected()
    {
        var json = JsonNode.Parse(SimulationSession.Create(SliceScenario.Create()).SaveToString())!;
        json["RngAlgorithm"] = "mersenne";

        var result = LoadFromString(json.ToJsonString());

        Assert.False(result.Success);
        Assert.Contains("generatore casuale", result.Error);
    }

    [Fact]
    public void An_unknown_skill_in_a_save_is_rejected()
    {
        var json = JsonNode.Parse(SimulationSession.Create(SliceScenario.Create()).SaveToString())!;
        json["Actors"]![0]!["Sheet"]!["SkillProficiencies"]!.AsArray().Add("Teleportation");

        var result = LoadFromString(json.ToJsonString());

        Assert.False(result.Success);
        Assert.Contains("abilità sconosciuta", result.Error);
    }
}
