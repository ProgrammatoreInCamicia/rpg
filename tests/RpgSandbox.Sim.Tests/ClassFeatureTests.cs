using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>R3a: the Paladin's features without magic up to level 5 (SRD 5.2.1 p. 53–56, 87–88).</summary>
public class ClassFeatureTests
{
    private static readonly ActorId Foe = new("foe");
    private const string Sword = "Spada lunga";

    /// <summary>A yard at noon: the Paladin (chain mail, shield, longsword) at (3,2), a guard at (6,2).</summary>
    private static SimulationSession Yard(long experience = 0, ulong seed = 1)
    {
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, at: (3, 2))
            .AddActor("foe", "Guardia", "yard", combat: "guard", at: (6, 2))
            .Build());
        s.AdvanceTo(At(1, 12));
        var me = s.Engine.World.Actors[s.Player];
        me.Experience = experience;
        var before = me.Profile.MaxHitPoints;
        s.Engine.World.Refresh(me);
        me.Vitality.RaiseMaximum(me.Profile.MaxHitPoints - before);
        return s;
    }

    private static Actor Me(SimulationSession s) => s.Engine.World.Actors[s.Player];

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    [Fact]
    public void At_level_two_a_fighting_style_is_chosen_once_defense_adds_one_to_armor_class()
    {
        var s = Yard();
        Assert.Empty(s.GetPlayerView().PendingChoices);
        s = Yard(experience: 300);
        var choice = s.GetPlayerView().PendingChoices.Single(c => c.Kind == "FightingStyle"); // R3b: a third spell to prepare too
        Assert.Equal(new[] { "Archery", "Defense", "GreatWeaponFighting" }, choice.Options);
        Assert.Equal(RejectionReason.InvalidAmount, s.Execute(new ChooseCommand { Actor = s.Player, Choice = choice.Id, Options = new[] { "Dueling" } }).Rejection);

        var ac = Me(s).Profile.ArmorClass;
        Assert.True(s.Execute(new ChooseCommand { Actor = s.Player, Choice = choice.Id, Options = new[] { "Defense" } }).Success);
        Assert.Equal(ac + 1, Me(s).Profile.ArmorClass);
        Assert.Equal("Defense", s.GetPlayerView().FightingStyle);
        Assert.DoesNotContain(s.GetPlayerView().PendingChoices, c => c.Kind == "FightingStyle");
    }

    [Fact]
    public void Archery_helps_only_ranged_weapons_and_great_weapon_fighting_raises_low_damage_dice()
    {
        var pack = ContentPack.Default;
        var bow = Builds.Profile(pack, "paladin", new Equipment("chain-mail", null, "shortbow"), 1, new CharacterChoices { FightingStyle = "Archery" });
        var plain = Builds.Profile(pack, "paladin", new Equipment("chain-mail", null, "shortbow"), 1);
        Assert.Equal(plain.Attacks.First().AttackBonus + 2, bow.Attacks.First().AttackBonus);
        var thrown = bow.Attacks.Single(a => a.Name == "Giavellotto (lanciato)");
        Assert.Equal(plain.Attacks.Single(a => a.Name == "Giavellotto (lanciato)").AttackBonus, thrown.AttackBonus); // a thrown javelin is a Melee weapon

        var great = Builds.Profile(pack, "paladin", new Equipment("chain-mail", null, "greatsword"), 1, new CharacterChoices { FightingStyle = "GreatWeaponFighting" });
        var greatsword = great.Attacks.First();
        Assert.True(greatsword.GreatWeaponFighting);
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var hit = Attacks.Roll(new SplitMix64(seed), greatsword with { AttackBonus = 50 }, 1);
            Assert.All(hit.DamageDice, d => Assert.InRange(d, 3, 6));
        }
    }

    [Fact]
    public void At_level_four_two_points_go_to_the_abilities_never_above_twenty()
    {
        var s = Yard(experience: 2700);
        var choice = s.GetPlayerView().PendingChoices.Single(c => c.Kind == "AbilityScores");
        Assert.Equal(2, choice.Picks);
        var bonus = Me(s).Profile.Attacks.Single(a => a.Name == Sword).AttackBonus;
        Assert.True(s.Execute(new ChooseCommand { Actor = s.Player, Choice = choice.Id, Options = new[] { "Strength", "Strength" } }).Success);
        Assert.Equal(17, Me(s).Sheet.Strength);
        Assert.Equal(bonus + 1, Me(s).Profile.Attacks.Single(a => a.Name == Sword).AttackBonus);
        Assert.DoesNotContain(s.GetPlayerView().PendingChoices, c => c.Kind == "AbilityScores");

        var high = Yard(experience: 2700);
        Me(high).Choices = new CharacterChoices { FightingStyle = "Defense", AbilityIncreases = new[] { Ability.Charisma, Ability.Charisma, Ability.Charisma } };
        high.Engine.World.Refresh(Me(high)); // 19 Charisma already
        Me(high).Choices = Me(high).Choices with { AbilityIncreases = Array.Empty<Ability>() };
        Me(high).Sheet = Me(high).Sheet with { Charisma = 19 };
        var asi = high.GetPlayerView().PendingChoices.Single(c => c.Kind == "AbilityScores");
        Assert.Equal(RejectionReason.InvalidAmount,
            high.Execute(new ChooseCommand { Actor = high.Player, Choice = asi.Id, Options = new[] { "Charisma", "Charisma" } }).Rejection);
    }

    [Fact]
    public void Lay_on_hands_heals_from_a_pool_of_five_a_level_cures_poison_and_fills_again_with_a_long_rest()
    {
        var s = Yard(experience: 300); // level 2: 10 points
        Assert.Equal(new PoolView { Remaining = 10, Max = 10 }, s.GetPlayerView().LayOnHands);
        Me(s).Vitality.TakeDamage(3);
        var healed = s.Execute(new LayOnHandsCommand { Actor = s.Player, Target = s.Player, Amount = 8 });
        Assert.True(healed.Success, healed.Message);
        Assert.Equal(Me(s).Vitality.Maximum, Me(s).Vitality.Current);
        Assert.Equal(7, s.GetPlayerView().LayOnHands!.Remaining); // only the 3 it could restore were spent
        Assert.Equal(RejectionReason.OutOfReach, s.Execute(new LayOnHandsCommand { Actor = s.Player, Target = Foe, Amount = 1 }).Rejection);

        s.Engine.AddCondition(null, Me(s), ConditionKind.Poisoned);
        Assert.True(s.Execute(new LayOnHandsCommand { Actor = s.Player, Target = s.Player, CurePoison = true }).Success);
        Assert.False(s.Engine.Has(Me(s), ConditionKind.Poisoned));
        Assert.Equal(RejectionReason.InvalidAmount, s.Execute(new LayOnHandsCommand { Actor = s.Player, Target = s.Player, CurePoison = true }).Rejection);

        var rest = s.Execute(new RestCommand { Actor = s.Player, Long = true });
        s.AdvanceUntilCompleted(rest.Action!.Value, Duration.FromHours(9));
        Assert.Equal(10, s.GetPlayerView().LayOnHands!.Remaining);
    }

    [Fact]
    public void In_a_fight_lay_on_hands_takes_the_bonus_action_and_at_level_five_the_attack_action_is_two_attacks()
    {
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var s = Yard(experience: 6500, seed: seed);
            Of(s, Foe).PlaceAt(Spot.CenterOf(new GridPos(4, 2)));
            Of(s, Foe).Vitality.RaiseMaximum(100);
            Of(s, Foe).Vitality.Heal(100);
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword });
            if (s.GetPlayerView().Combat is { IsYourTurn: true })
                s.Execute(new EndTurnCommand { Actor = s.Player });
            for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
                s.Advance(Duration.FromSeconds(1));
            if (s.GetPlayerView().Combat is not { IsYourTurn: true, You.ActionAvailable: true } || !Of(s, Foe).Conscious
                || Me(s).Vitality.Current == Me(s).Vitality.Maximum || !Me(s).Conscious)
                continue; // wanted: the Paladin hurt, its turn, the guard up

            Assert.True(s.GetPlayerView().Combat!.You.BonusActionAvailable);
            Assert.True(s.Execute(new LayOnHandsCommand { Actor = s.Player, Target = s.Player, Amount = 1 }).Success);
            Assert.False(s.GetPlayerView().Combat!.You.BonusActionAvailable);
            Assert.Equal(RejectionReason.NoActionLeft, s.Execute(new LayOnHandsCommand { Actor = s.Player, Target = s.Player, Amount = 1 }).Rejection);

            Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword }).Success);
            Assert.Equal(1, s.GetPlayerView().Combat!.You.AttacksLeft);
            Assert.Contains(s.GetPlayerView().Combat!.Targets, t => t.Target == Foe && t.Attack == Sword && t.Valid);
            Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword }).Success);
            Assert.Equal(RejectionReason.NoActionLeft, s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword }).Rejection);
            return;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    [Fact]
    public void After_a_long_rest_the_weapon_masteries_may_be_changed_and_survive_a_save()
    {
        var s = Yard();
        Assert.Equal(new[] { new ItemId("longsword"), new ItemId("javelin") }, s.GetPlayerView().MasteryWeapons);
        var rest = s.Execute(new RestCommand { Actor = s.Player, Long = true });
        s.AdvanceUntilCompleted(rest.Action!.Value, Duration.FromHours(9));
        var choice = s.GetPlayerView().PendingChoices.Single(c => c.Kind == "WeaponMastery");
        Assert.True(choice.Optional);
        Assert.True(s.Execute(new ChooseCommand { Actor = s.Player, Choice = choice.Id, Options = new[] { "greatsword", "javelin" } }).Success);
        Assert.Null(Me(s).Profile.Attacks.Single(a => a.Name == Sword).Mastery); // no more Sap with the longsword
        Assert.DoesNotContain(s.GetPlayerView().PendingChoices, c => c.Kind == "WeaponMastery");

        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(new[] { new ItemId("greatsword"), new ItemId("javelin") }, loaded.GetPlayerView().MasteryWeapons);
    }
}
