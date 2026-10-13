using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>D1a: contents as data; one definition per actor, its sheet and profile derived from it and its equipment.</summary>
public class ContentTests
{
    private static Dictionary<string, string> DefaultFiles() => ContentPack.Default.Files.ToDictionary(f => f.Name, f => f.Text);

    /// <summary>The default files with one of them edited (as JSON).</summary>
    private static Dictionary<string, string> Edited(string file, Action<JsonNode> edit)
    {
        var files = DefaultFiles();
        var json = JsonNode.Parse(files[file])!;
        edit(json);
        files[file] = json.ToJsonString();
        return files;
    }

    private static JsonNode Paladin(JsonNode characters) => characters["characters"]!.AsArray().Single(c => (string)c!["id"]! == "paladin")!;

    [Fact]
    public void The_default_pack_loads_and_says_what_it_is()
    {
        var pack = ContentPack.Default;

        Assert.Equal(("rpg-sandbox-core", "1"), (pack.Id, pack.Version));
        Assert.Equal(64, pack.Hash.Length);
        Assert.Equal("manifest.json", pack.Files[0].Name);
        Assert.Contains("chain-mail", pack.Items.Keys);
        Assert.Equal(new[] { "bandit", "commoner", "farmer", "guard", "raider", "village-guard" }, pack.Creatures.Keys.Order());
        Assert.Equal(pack.Hash, ContentPack.Load(DefaultFiles()).Hash); // the same files, the same identity
    }

    [Theory]
    [InlineData("manifest", "manca manifest.json")]
    [InlineData("missing", "manca il file")]
    [InlineData("stray", "non è nel manifest")]
    [InlineData("unknown item", "oggetto sconosciuto 'sword-of-nothing'")]
    [InlineData("duplicate", "'bandit' ripetuto")]
    [InlineData("dice", "dadi non validi")]
    [InlineData("no xp", "'xp'")]
    [InlineData("negative xp", "punti esperienza non validi")]
    public void A_broken_pack_is_refused_with_a_readable_reason(string breakage, string reason)
    {
        var files = breakage switch
        {
            "manifest" => DefaultFiles().Where(f => f.Key != "manifest.json").ToDictionary(f => f.Key, f => f.Value),
            "missing" => DefaultFiles().Where(f => f.Key != "srd/classes.json").ToDictionary(f => f.Key, f => f.Value),
            "stray" => DefaultFiles().Append(new("extra/notes.json", "{}")).ToDictionary(f => f.Key, f => f.Value),
            "unknown item" => Edited("srd/creatures.json", j => j["creatures"]![0]!["gear"]!.AsArray().Add("sword-of-nothing")),
            "duplicate" => Edited("original/creatures.json", j => j["creatures"]![0]!["id"] = "bandit"),
            "no xp" => Edited("srd/creatures.json", j => j["creatures"]![0]!.AsObject().Remove("xp")),
            "negative xp" => Edited("original/creatures.json", j => j["creatures"]![0]!["xp"] = -1),
            _ => Edited("srd/items.json", j => j["items"]![0]!["weapon"]!["damage"] = "1x4"),
        };

        var error = Assert.Throws<InvalidDataException>(() => ContentPack.Load(files));
        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public void The_paladin_is_computed_from_class_level_abilities_and_equipment_exactly_as_before()
    {
        var pack = ContentPack.Default;
        var worn = Builds.InitialEquipment(pack, "paladin");
        var profile = Builds.Profile(pack, "paladin", worn);
        var sheet = Builds.Sheet(pack, "paladin", worn);

        Assert.Equal(new Equipment("chain-mail", "shield", "longsword"), worn);
        Assert.Equal((11, 18, 0, 30, true), (profile.MaxHitPoints, profile.ArmorClass, profile.InitiativeBonus, profile.Speed, profile.MakesDeathSaves));
        Assert.Equal((18, true, 2), (sheet.ArmorClass, sheet.StealthDisadvantage, sheet.ProficiencyBonus));
        var sword = profile.Attacks[0];
        Assert.Equal(("Spada lunga", 4, new Dice(1, 8), 2, WeaponMastery.Sap, "longsword"),
            (sword.Name, sword.AttackBonus, sword.Damage, sword.DamageBonus, sword.Mastery, sword.Requires));
        var javelin = profile.Attacks[1];
        Assert.Equal(("Giavellotto (lanciato)", 4, (30, 120), "javelin", ItemFate.Thrown),
            (javelin.Name, javelin.AttackBonus, javelin.Range!.Value, javelin.Item, javelin.Fate));
        Assert.Equal("Colpo senz'armi", profile.Attacks[2].Name);
    }

    [Fact]
    public void Equipment_changes_the_numbers_by_the_srd_rules()
    {
        var pack = ContentPack.Default;

        var noShield = Builds.Profile(pack, "paladin", new Equipment("chain-mail", null, "longsword"));
        Assert.Equal((16, new Dice(1, 10)), (noShield.ArmorClass, noShield.Attacks[0].Damage)); // Versatile: two hands

        var noArmor = Builds.Sheet(pack, "paladin", new Equipment(null, "shield", "longsword"));
        Assert.Equal((12, false), (noArmor.ArmorClass, noArmor.StealthDisadvantage)); // 10 + Dex 0 + Shield 2

        var unarmed = Builds.Profile(pack, "paladin", Equipment.None);
        Assert.DoesNotContain(unarmed.Attacks, a => a.Name == "Spada lunga");
    }

    [Fact]
    public void Heavy_armor_without_the_strength_it_asks_for_slows_one_down()
    {
        var weak = ContentPack.Load(Edited("original/characters.json", j => Paladin(j)["abilities"]!["str"] = 12));

        Assert.Equal(20, Builds.Sheet(weak, "paladin", Builds.InitialEquipment(weak, "paladin")).Speed);
    }

    [Fact]
    public void Every_creature_has_one_description_its_sheet_and_profile_agree()
    {
        var pack = ContentPack.Default;
        foreach (var id in pack.Creatures.Keys)
        {
            var worn = Builds.InitialEquipment(pack, id);
            Assert.Equal(Builds.Sheet(pack, id, worn).ArmorClass, Builds.Profile(pack, id, worn).ArmorClass);
            Assert.Equal(Builds.Sheet(pack, id, worn).Speed, Builds.Profile(pack, id, worn).Speed);
        }
        // The raider of the test bench: Dexterity 15, Stealth, AC 13, and it fights with that Dexterity.
        var raider = Builds.Profile(pack, "raider", Builds.InitialEquipment(pack, "raider"));
        Assert.Equal((13, 4), (raider.ArmorClass, raider.Attacks[0].AttackBonus));
    }

    [Fact]
    public void What_one_wears_or_wields_is_carried_shown_and_cannot_be_dropped()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var me = s.GetPlayerView();

        Assert.Equal(new[] { (EquipmentSlot.Armor, "chain-mail"), (EquipmentSlot.Shield, "shield"), (EquipmentSlot.MainHand, "longsword") },
            me.Equipment.Select(e => (e.Slot, e.Item.Value)));
        Assert.Equal(1, me.Inventory.Single(i => i.Item == new ItemId("longsword")).Equipped);
        Assert.Equal(RejectionReason.Equipped, s.Execute(new DropCommand { Actor = s.Player, Item = new ItemId("longsword"), Count = 1 }).Rejection);
        Assert.Equal(RejectionReason.Equipped, s.Execute(new DropCommand { Actor = s.Player, Item = new ItemId("shield"), Count = 1 }).Rejection);
        Assert.True(s.Execute(new DropCommand { Actor = s.Player, Item = new ItemId("javelin"), Count = 1 }).Success);
    }

    [Fact]
    public void Without_its_spear_a_guard_has_no_attack_with_it()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var guard = s.Engine.World.Actors[Guard];
        Assert.Equal(new[] { "spear", "spear" }, guard.Profile.Attacks.Select(a => a.Requires ?? a.Item));

        guard.Give("spear", -1); // lost somehow: both its attacks need it

        // At midnight both are in the lit inn: the guard sees the player, but has nothing to strike with.
        Assert.True(s.Engine.SeesActor(guard, s.Engine.World.Actors[s.Player]));
        Assert.Equal(RejectionReason.NoWeapon, s.Execute(new AttackCommand { Actor = Guard, Target = s.Player, Attack = "Lancia" }).Rejection);
        Assert.Equal(RejectionReason.NoAmmo, s.Execute(new AttackCommand { Actor = Guard, Target = s.Player, Attack = "Lancia (lanciata)" }).Rejection);
    }

    [Fact]
    public void Throwing_away_the_weapon_in_hand_leaves_the_hand_empty_and_its_attack_gone()
    {
        // A Paladin wielding a spear, with a second one to throw, against a foe 8 squares off (within 20/60).
        var spearman = ContentPack.Load(Edited("original/characters.json", j =>
        {
            Paladin(j)["equipment"]!["mainHand"] = "spear";
            Paladin(j)["items"] = new JsonArray(new JsonArray("spear", 1));
        }));
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = SimulationSession.Create(new ScenarioBuilder(spearman).WithSeed(seed)
                .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
                .AddMap("a", Enumerable.Repeat("YYYYYYYYYYYY", 3).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
                .AddActor("player", "P", "yard", isPlayer: true, at: (1, 1))
                .AddActor("foe", "Avversario", "yard", build: "guard", at: (9, 1))
                .Build());
            s.AdvanceTo(At(1, 12));
            var me = s.Engine.World.Actors[s.Player];
            Assert.Equal(2, me.Carried("spear"));
            s.Execute(new AttackCommand { Actor = s.Player, Target = new ActorId("foe"), Attack = "Lancia (lanciato)" });
            if (s.GetPlayerView().Combat is not { IsYourTurn: true })
                continue;
            s.Execute(new EndTurnCommand { Actor = s.Player });
            s.Advance(Duration.FromSeconds(6));
            if (s.GetPlayerView().Combat is not { IsYourTurn: true }
                || !s.Execute(new AttackCommand { Actor = s.Player, Target = new ActorId("foe"), Attack = "Lancia (lanciato)" }).Success)
                continue;

            Assert.Equal(0, me.Carried("spear"));
            Assert.Null(me.Equipment.MainHand);
            Assert.DoesNotContain(me.Profile.Attacks, a => a.Requires == "spear");
            Assert.DoesNotContain(s.GetPlayerView().Equipment, e => e.Slot == EquipmentSlot.MainHand);
            return;
        }
        Assert.Fail("No seed gives two throws.");
    }

    [Fact]
    public void A_save_carries_its_contents_and_is_played_with_them_whatever_is_installed()
    {
        // A pack where the Paladin is tougher (Constitution 16: 13 HP) plays a game; its save brings that pack along.
        var tough = ContentPack.Load(Edited("original/characters.json", j => Paladin(j)["abilities"]!["con"] = 16));
        var s = SimulationSession.Create(new ScenarioBuilder(tough)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", new[] { "YYYY" }, new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "P", "yard", isPlayer: true, at: (0, 0))
            .Build());
        Assert.Equal(13, s.Engine.World.Actors[s.Player].Profile.MaxHitPoints);

        var loaded = s.SaveAndReload();

        Assert.Equal(13, loaded.Engine.World.Actors[loaded.Player].Profile.MaxHitPoints); // not the installed 11
        Assert.Equal(tough.Hash, loaded.Engine.World.Content.Hash);
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
    }

    [Fact]
    public void A_save_whose_contents_do_not_match_their_hash_is_refused()
    {
        var json = JsonNode.Parse(SimulationSession.Create(MappedVillageScenario.CreateLivingVillage()).SaveToString())!;
        var file = json["Content"]!["Files"]!.AsArray().First(f => (string)f!["Name"]! == "original/characters.json")!;
        file["Text"] = ((string)file["Text"]!).Replace("\"con\": 13", "\"con\": 20");

        Assert.Contains("impronta", LoadFromString(json.ToJsonString()).Error);
    }
}
