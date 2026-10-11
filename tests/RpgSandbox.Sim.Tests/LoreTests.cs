using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>C5a: what the player knows of the creatures it meets, and nothing more.</summary>
public class LoreTests
{
    private static readonly ActorId Foe = new("foe");

    /// <summary>Noon, a 12×4 yard: the Paladin at (1,1), a foe next to it.</summary>
    private static SimulationSession Yard(ulong seed, string foe = "guard") =>
        SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYYYY", 4).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, build: "paladin", at: (1, 1))
            .AddActor("foe", "Avversario", "yard", build: foe, at: (2, 1))
            .Build());

    /// <summary>A fight where both sides have struck at least once: the player opens, then rounds pass.</summary>
    private static SimulationSession Exchanged(ulong seed = 1)
    {
        for (; seed < 200; seed++)
        {
            var s = Yard(seed);
            s.AdvanceTo(At(1, 12));
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Spada lunga" });
            for (var i = 0; i < 6 && s.GetPlayerView().Combat is { } c && !c.Log.Any(l => l.Kind == CombatLogKind.Attack && l.Actor == Foe); i++)
            {
                if (c.IsYourTurn)
                    s.Execute(new EndTurnCommand { Actor = s.Player });
                s.Advance(Duration.FromSeconds(6));
            }
            var log = s.GetPlayerView().Combat?.Log;
            if (log is not null && log.Any(l => l.Kind == CombatLogKind.Attack && l.Actor == Foe)
                && log.Any(l => l.Kind == CombatLogKind.Attack && l.Actor == s.Player))
                return s;
        }
        throw new InvalidOperationException("No seed gives an exchange of blows.");
    }

    [Fact]
    public void The_player_sees_its_own_rolls_but_never_the_foes_armor_class_nor_the_foes_dice()
    {
        var s = Exchanged();
        var log = s.GetPlayerView().Combat!.Log;

        var mine = log.First(l => l.Kind == CombatLogKind.Attack && l.Actor == s.Player);
        Assert.NotNull(mine.Total);
        Assert.Null(mine.ArmorClass);
        Assert.Contains("d20", mine.Text);
        Assert.DoesNotContain("CA", mine.Text);

        var theirs = log.First(l => l.Kind == CombatLogKind.Attack && l.Actor == Foe);
        Assert.Empty(theirs.D20);
        Assert.Null(theirs.Total);
        Assert.Null(theirs.Bonus);
        Assert.Null(theirs.ArmorClass);
        Assert.DoesNotContain("d20", theirs.Text);
        Assert.DoesNotContain("CA", theirs.Text);
        Assert.Equal(s.Player, theirs.Target);
        Assert.NotNull(theirs.Damage); // what it suffers, it knows

        var foeInitiative = log.Single(l => l.Kind == CombatLogKind.Initiative && l.Actor == Foe);
        Assert.Empty(foeInitiative.D20);
        Assert.DoesNotContain("d20", foeInitiative.Text);
        Assert.Null(s.GetPlayerView().Combat!.Order.Single(o => o.Actor == Foe).Total);
        Assert.NotNull(s.GetPlayerView().Combat!.Order.Single(o => o.Actor == s.Player).Total);
    }

    [Fact]
    public void Recent_events_carry_no_armor_class_while_the_true_record_keeps_it()
    {
        var s = Exchanged();

        Assert.All(s.GetPlayerView().RecentEvents.Where(f => f.Kind == "Attack"), f => Assert.DoesNotContain("CA", f.Description));
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "Attack" && f.Description.Contains("contro CA"));

        var reloaded = s.SaveAndReload(); // the player's text is saved with the fact
        Assert.Equal(s.GetPlayerView().RecentEvents, reloaded.GetPlayerView().RecentEvents);
    }

    [Fact]
    public void Armor_class_is_learned_from_the_players_own_blows_as_at_the_table()
    {
        var lore = new CreatureLore();
        lore.LearnArmorClass(8, hit: false);
        lore.LearnArmorClass(18, hit: true);
        Assert.Equal((8, 18, (int?)null), (lore.ArmorClassAbove!.Value, lore.ArmorClassAtMost!.Value, lore.ArmorClass));
        lore.LearnArmorClass(15, hit: false);
        lore.LearnArmorClass(10, hit: false); // says nothing new
        lore.LearnArmorClass(16, hit: true);
        Assert.Equal(16, lore.ArmorClass); // missed at 15, hit at 16

        lore.LearnArmorClass(14, hit: true); // it took off its armor: what was known no longer holds
        Assert.Equal((null, 14), (lore.ArmorClassAbove, lore.ArmorClassAtMost));
    }

    [Fact]
    public void A_fight_teaches_the_foes_attacks_and_narrows_its_armor_class_but_never_says_it()
    {
        var s = Exchanged();
        var truth = s.Engine.World.Actors[Foe].Profile.ArmorClass;
        var card = s.GetPlayerView().VisibleActors.Single(a => a.Id == Foe).Card!;

        Assert.Equal("Umano", card.Kind);
        Assert.NotEmpty(card.Attacks); // the guard's spear, seen striking
        Assert.All(card.Attacks, a => Assert.StartsWith("Lancia", a));
        Assert.True(card.ArmorClassAbove is not null || card.ArmorClassAtMost is not null);
        Assert.True((card.ArmorClassAbove ?? 0) < truth && truth <= (card.ArmorClassAtMost ?? 99));
        Assert.Empty(card.KindLore!.Attacks); // a spear is a weapon, not something every human has

        var reloaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), reloaded.SaveToString());
        Assert.Equal(card.Attacks, reloaded.GetPlayerView().VisibleActors.Single(a => a.Id == Foe).Card!.Attacks);
    }

    [Fact]
    public void Observing_reveals_what_the_insight_total_reaches_once_and_only_to_the_player()
    {
        var s = Yard(1, foe: "farmer");
        s.AdvanceTo(At(1, 12));

        var result = s.Execute(new ObserveCommand { Actor = s.Player, Target = Foe });
        Assert.True(result.Success, result.Message);
        var card = s.GetPlayerView().VisibleActors.Single(a => a.Id == Foe).Card!;
        Assert.True(card.Observed);
        var total = int.Parse(System.Text.RegularExpressions.Regex.Match(result.Message!, @"Intuizione: d20 \d+ \+\d+ = (\d+)").Groups[1].Value);
        var tells = s.Engine.World.Content.Creatures["farmer"].Tells;
        Assert.Equal(tells.Where(t => t.Dc <= total).Select(t => t.Text), card.Tells);
        Assert.DoesNotContain("Storia", result.Message); // the Paladin knows no History: a GM would not ask for it

        Assert.Equal(RejectionReason.AlreadyDone, s.Execute(new ObserveCommand { Actor = s.Player, Target = Foe }).Rejection);
        var fact = s.GetWorldView().RecentFacts.Single(f => f.Kind == "Observe");
        Assert.Equal(new[] { s.Player }, s.Engine.World.RecentFacts.Single(f => f.Kind == "Observe").Participants);
        Assert.Contains(s.GetPlayerView().RecentEvents, e => e.Id == fact.Id);

        var reloaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), reloaded.SaveToString());
        Assert.Equal(card.Tells, reloaded.GetPlayerView().VisibleActors.Single(a => a.Id == Foe).Card!.Tells);
        Assert.Equal(RejectionReason.AlreadyDone, reloaded.Execute(new ObserveCommand { Actor = reloaded.Player, Target = Foe }).Rejection);
    }

    [Fact]
    public void Who_knows_the_lore_of_a_kind_recalls_it_once_for_every_creature_of_that_kind()
    {
        var files = ContentPack.Default.Files.ToDictionary(f => f.Name, f => f.Text);
        var characters = JsonNode.Parse(files["original/characters.json"])!;
        characters["characters"]![0]!["skills"]!.AsArray().Add("History");
        files["original/characters.json"] = characters.ToJsonString();
        var kinds = JsonNode.Parse(files["original/kinds.json"])!;
        kinds["kinds"]![0]!["tells"] = new JsonArray(new JsonObject { ["dc"] = 1, ["text"] = "Gli umani si arrendono, se la lotta è persa." });
        files["original/kinds.json"] = kinds.ToJsonString();
        var pack = ContentPack.Load(files);
        var s = SimulationSession.Create(new ScenarioBuilder(pack).WithSeed(1)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYYYY", 4).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, build: "paladin", at: (1, 1))
            .AddActor("foe", "Contadino", "yard", build: "farmer", at: (2, 1))
            .AddActor("other", "Guardia", "yard", build: "village-guard", at: (4, 1))
            .Build());
        s.AdvanceTo(At(1, 12));

        Assert.Contains("Storia", s.Execute(new ObserveCommand { Actor = s.Player, Target = Foe }).Message);
        var kind = s.GetPlayerView().KnownKinds.Single();
        Assert.Equal(("Umano", true), (kind.Name, kind.Studied));
        Assert.Equal(new[] { "Gli umani si arrendono, se la lotta è persa." }, kind.Tells);

        var second = s.Execute(new ObserveCommand { Actor = s.Player, Target = new ActorId("other") }).Message!;
        Assert.Contains("Intuizione", second);
        Assert.DoesNotContain("Storia", second); // the kind was already recalled
        Assert.Equal(kind.Tells, s.GetPlayerView().VisibleActors.Single(a => a.Id == new ActorId("other")).Card!.KindLore!.Tells);
    }

    [Fact]
    public void In_a_fight_observing_takes_the_action_and_waits_for_ones_turn()
    {
        var s = Exchanged();
        if (!s.GetPlayerView().Combat!.IsYourTurn)
        {
            Assert.Equal(RejectionReason.NotYourTurn, s.Execute(new ObserveCommand { Actor = s.Player, Target = Foe }).Rejection);
            s.Advance(Duration.FromSeconds(6));
        }
        while (s.GetPlayerView().Combat is { IsYourTurn: true, You.ActionAvailable: false })
        {
            s.Execute(new EndTurnCommand { Actor = s.Player });
            s.Advance(Duration.FromSeconds(6));
        }
        Assume(s.GetPlayerView().Combat is { IsYourTurn: true, You.ActionAvailable: true });

        Assert.True(s.Execute(new ObserveCommand { Actor = s.Player, Target = Foe }).Success);
        Assert.False(s.GetPlayerView().Combat!.You.ActionAvailable);
    }

    [Fact]
    public void One_cannot_observe_what_one_does_not_see()
    {
        var s = Yard(1, foe: "farmer");
        s.AdvanceTo(At(1, 0)); // midnight, an unlit yard
        Assume(!s.Engine.SeesActor(s.Engine.World.Actors[s.Player], s.Engine.World.Actors[Foe]));

        Assert.Equal(RejectionReason.NotVisible, s.Execute(new ObserveCommand { Actor = s.Player, Target = Foe }).Rejection);
    }

    [Theory]
    [InlineData("Kind", "dragon")]
    [InlineData("Creature", "nobody")]
    public void A_save_with_lore_about_what_does_not_exist_is_refused(string field, string value)
    {
        var s = Yard(1, foe: "farmer");
        s.AdvanceTo(At(1, 12));
        s.Execute(new ObserveCommand { Actor = s.Player, Target = Foe });
        s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Spada lunga" });
        var json = JsonNode.Parse(s.SaveToString())!;
        var lore = json["Actors"]!.AsArray().First(a => (string)a!["Id"]! == "player")!["Lore"]!;
        var list = field == "Kind" ? lore["Kinds"]!.AsArray() : lore["Creatures"]!.AsArray();
        if (field == "Kind")
            list.Add(new JsonObject { ["Kind"] = value, ["Attacks"] = new JsonArray(), ["Tells"] = new JsonArray(), ["Studied"] = true });
        else
            list[0]!["Creature"] = value;

        Assert.NotNull(LoadFromString(json.ToJsonString()).Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Only_blows_the_player_sees_teach_it_anything(bool watching)
    {
        // A yard and a guardhouse split by a wall; a brawler strikes a miller in the yard for a few rounds.
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(1)
            .AddArea("a", "Villaggio")
            .AddLocation("yard", "Cortile", "a").AddLocation("house", "Corpo di guardia", "a")
            .AddMap("a", new[] { "YYYYY#GGGG", "YYYYY#GGGG", "YYYYY#GGGG", "YYYYY#GGGG" },
                new Dictionary<char, string> { ['Y'] = "yard", ['G'] = "house" })
            .AddActor("player", "Paladino", watching ? "yard" : "house", isPlayer: true, build: "paladin", at: watching ? (1, 0) : (8, 0))
            .AddActor("miller", "Mugnaio", "yard", build: "guard", at: (3, 2))
            .AddActor("brawler", "Attaccabrighe", "yard", build: "guard", at: (2, 2))
            .Build());
        s.AdvanceTo(At(1, 12));
        var brawler = new ActorId("brawler");
        Assert.True(s.Execute(new AttackCommand { Actor = brawler, Target = new ActorId("miller"), Attack = "Lancia" }).Success);
        s.Advance(Duration.FromSeconds(30));
        Assume(s.Engine.World.RecentFacts.Count(f => f.Kind == "Attack") >= 2);

        var lore = s.Engine.World.Actors[s.Player].Lore;
        if (watching)
            Assert.Contains("Lancia", lore.Of(brawler).Attacks);
        else
            Assert.True(lore.IsEmpty);
    }

    private static void Assume(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("The scene did not set up as the test expects.");
    }
}
