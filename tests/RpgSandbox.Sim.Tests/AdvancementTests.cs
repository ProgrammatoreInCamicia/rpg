using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>C5b: Experience Points for defeated creatures and the levels they bring (SRD 5.2.1).</summary>
public class AdvancementTests
{
    private static readonly ActorId Foe = new("foe");
    private const string Sword = "Spada lunga";

    /// <summary>A yard at noon: the player, a level 1 Paladin (11 HP, Con +1), next to a foe of the given build.</summary>
    private static SimulationSession Yard(ulong seed = 1, string foe = "guard", Action<ScenarioBuilder>? more = null)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "Villaggio")
            .AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, at: (2, 2))
            .AddActor("foe", "Avversario", "yard", combat: foe, at: (3, 2));
        more?.Invoke(b);
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, 12));
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static CommandResult Attack(SimulationSession s, bool nonLethal = false) =>
        s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword, NonLethal = nonLethal });

    /// <summary>The first seed whose opening blow drops a 1 HP foe (killed, or knocked out if <paramref name="nonLethal"/>).</summary>
    private static SimulationSession FoeDropped(bool nonLethal = false, Action<SimulationSession>? before = null)
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Yard(seed);
            Of(s, Foe).Vitality = new Vitality(11, makesDeathSaves: false, current: 1);
            before?.Invoke(s);
            Attack(s, nonLethal);
            if (!Of(s, Foe).Conscious)
                return s;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    [Fact]
    public void The_advancement_table_is_the_srds()
    {
        Assert.Equal(new[] { 0, 300, 900, 2_700, 6_500, 14_000, 23_000, 34_000, 48_000, 64_000, 85_000, 100_000, 120_000,
            140_000, 165_000, 195_000, 225_000, 265_000, 305_000, 355_000 }, Enumerable.Range(1, 20).Select(Builds.ExperienceFor));
        Assert.Equal(1, Builds.LevelFor(299));
        Assert.Equal(2, Builds.LevelFor(300));
        Assert.Equal(20, Builds.LevelFor(1_000_000));
        Assert.Equal(new[] { 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6 }, Enumerable.Range(1, 20).Select(Builds.ProficiencyBonus));
        // Every creature's XP is that of its stat block (SRD: Commoner CR 0, 10 XP; Bandit and Guard CR 1/8, 25 XP).
        Assert.Equal(10, ContentPack.Default.Creatures["commoner"].Experience);
        Assert.Equal(25, ContentPack.Default.Creatures["bandit"].Experience);
        Assert.Equal(25, ContentPack.Default.Creatures["guard"].Experience);
    }

    [Fact]
    public void A_new_level_1_character_has_no_experience_and_needs_300_points()
    {
        var view = Yard().GetPlayerView();

        Assert.Equal((1, 0L, 300), (view.Level, view.Experience, view.NextLevelAt));
    }

    [Fact]
    public void Defeating_a_creature_gives_its_xp_once_even_if_it_is_finished_off_later()
    {
        var s = FoeDropped(nonLethal: true);
        Assert.True(Of(s, Foe).Vitality.KnockedOut);
        Assert.Equal(25, s.GetPlayerView().Experience);

        // Struck again where it lies: dead now, but it was already defeated.
        while (Of(s, Foe).Vitality.State != LifeState.Dead)
        {
            if (s.GetPlayerView().Combat is null)
                Attack(s);
            else if (s.GetPlayerView().Combat is { IsYourTurn: true, You.ActionAvailable: true })
                Attack(s);
            else
                s.Advance(Duration.FromSeconds(6));
        }
        Assert.Equal(25, s.GetPlayerView().Experience);
        Assert.Equal(new[] { Foe }, Of(s, s.Player).Defeated);
    }


    [Fact]
    public void A_foe_the_player_fought_once_and_someone_else_kills_far_from_it_gives_the_player_nothing()
    {
        // Review C5b: the player and the foe fought before and parted; later a guard fights the foe without the player.
        var watchman = new ActorId("watchman");
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Yard(seed, more: b => b.AddActor("watchman", "Guardia", "yard", combat: "guard", at: (4, 2)));
            Of(s, s.Player).FoughtWith.Add(Foe);
            Of(s, Foe).FoughtWith.Add(s.Player);
            Of(s, Foe).Vitality = new Vitality(11, makesDeathSaves: false, current: 1);
            s.Execute(new AttackCommand { Actor = watchman, Target = Foe, Attack = "Lancia" });
            if (Of(s, Foe).Conscious)
                continue;
            Assert.DoesNotContain(s.Engine.World.Encounters.Values, e => e.Order.Any(c => c.Actor == s.Player));
            Assert.Equal(0, s.GetPlayerView().Experience);
            Assert.Empty(Of(s, s.Player).Defeated);
            return;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }
    [Fact]
    public void Reaching_300_points_makes_the_paladin_level_2_with_its_fixed_hit_points()
    {
        var s = FoeDropped(before: x => Of(x, x.Player).Experience = 290);
        var view = s.GetPlayerView();

        Assert.Equal((2, 315L, 900), (view.Level, view.Experience, view.NextLevelAt));
        // SRD Fixed Hit Points by Class: Paladin 6 + Con modifier (+1) = 7 more, current too.
        Assert.Equal((18, 18), (Of(s, s.Player).Vitality.Maximum, Of(s, s.Player).Vitality.Current));
        Assert.Equal(18, Of(s, s.Player).Profile.MaxHitPoints);
        Assert.Equal(2, view.Sheet.ProficiencyBonus);
        Assert.Contains(view.RecentEvents, f => f.Kind == "LevelUp" && f.Description.Contains("livello 2"));
    }

    [Fact]
    public void Several_levels_at_once_bring_the_hit_points_and_proficiency_bonus_of_each()
    {
        var s = FoeDropped(before: x => Of(x, x.Player).Experience = 6_490);

        Assert.Equal(5, s.GetPlayerView().Level);
        Assert.Equal(11 + 4 * 7, Of(s, s.Player).Vitality.Maximum);
        Assert.Equal(3, s.GetPlayerView().Sheet.ProficiencyBonus);
        // The attack bonus follows the Proficiency Bonus: Str +2 and PB +3 with the Longsword (+4 at level 1).
        Assert.Equal(5, Of(s, s.Player).Profile.Attacks.First(a => a.Name == Sword).AttackBonus);
    }

    [Fact]
    public void A_wounded_character_gains_the_new_hit_points_on_top_of_what_it_has_left()
    {
        var s = FoeDropped(before: x =>
        {
            Of(x, x.Player).Experience = 290;
            Of(x, x.Player).Vitality = new Vitality(11, makesDeathSaves: true, current: 4);
        });

        Assert.Equal((18, 11), (Of(s, s.Player).Vitality.Maximum, Of(s, s.Player).Vitality.Current));
    }

    [Fact]
    public void Experience_and_defeated_creatures_survive_a_save_and_a_level_2_paladin_reloads_consistent()
    {
        var s = FoeDropped(before: x => Of(x, x.Player).Experience = 290).SaveAndReload();

        Assert.Equal((2, 315L), (s.GetPlayerView().Level, s.GetPlayerView().Experience));
        Assert.Equal(18, Of(s, s.Player).Vitality.Maximum);
        Assert.Equal(new[] { Foe }, Of(s, s.Player).Defeated);
    }

    [Fact]
    public void A_schema_21_save_starts_its_characters_at_their_level_and_a_schema_22_one_must_say_it()
    {
        var data = JsonNode.Parse(FoeDropped().SaveToString())!;
        foreach (var actor in data["Actors"]!.AsArray())
        {
            actor!.AsObject().Remove("Experience");
            actor.AsObject().Remove("Defeated");
        }
        var missing = LoadFromString(data.ToJsonString());
        Assert.False(missing.Success);
        Assert.Contains("esperienza", missing.Error);

        AsSchema(data, 21);
        var old = LoadFromString(data.ToJsonString());
        Assert.True(old.Success, old.Error);
        Assert.Equal((1, 0L), (old.Session!.GetPlayerView().Level, old.Session.GetPlayerView().Experience));
    }

    [Fact]
    public void A_save_cannot_give_a_creature_experience_or_claim_a_foe_never_fought()
    {
        var data = JsonNode.Parse(FoeDropped().SaveToString())!;
        var foe = data["Actors"]!.AsArray().Single(a => (string)a!["Id"]! == "foe")!;
        foe["Experience"] = 50;
        Assert.False(LoadFromString(data.ToJsonString()).Success);

        data = JsonNode.Parse(Yard().SaveToString())!;
        var player = data["Actors"]!.AsArray().Single(a => (bool)a!["IsPlayer"]!)!;
        player["Defeated"] = new JsonArray("foe");
        var result = LoadFromString(data.ToJsonString());
        Assert.False(result.Success);
        Assert.Contains("mai affrontate", result.Error);
    }
}
