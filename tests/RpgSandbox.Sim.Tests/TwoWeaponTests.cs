using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>R4b: weapons in hand and fighting with two (SRD 5.2.1 Weapons: Light, Nick; Two-Weapon Fighting).</summary>
public class TwoWeaponTests
{
    private static readonly ActorId Foe = new("foe");
    private const string Short = "Spada corta", OffDagger = "Pugnale (mano secondaria)";

    /// <summary>A yard at noon: the Paladin at (3,3) carrying a shortsword, a dagger and a greatsword too; a guard at (4,3).</summary>
    private static SimulationSession Yard(ulong seed = 1)
    {
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYYYY", 8).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, at: (3, 3))
            .AddActor("foe", "Guardia", "yard", combat: "guard", at: (4, 3))
            .Build());
        s.AdvanceTo(At(1, 12));
        foreach (var item in new[] { "shortsword", "dagger", "greatsword" })
            Me(s).Give(item, 1);
        return s;
    }

    /// <summary>Shield away, shortsword and dagger in hand.</summary>
    private static void TwoBlades(SimulationSession s)
    {
        Assert.True(s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.Shield, Item = null }).Success);
        Assert.True(s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.MainHand, Item = "shortsword" }).Success);
        Assert.True(s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.OffHand, Item = "dagger" }).Success);
    }

    /// <summary>The Paladin strikes first with the shortsword; the first seed where it is then its fresh turn with the guard up.</summary>
    private static SimulationSession Fight(Action<SimulationSession>? before = null)
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Yard(seed);
            TwoBlades(s);
            Of(s, Foe).Profile = Of(s, Foe).Profile with { ArmorClass = 40 }; // only a natural 20 hits: the fight goes on
            before?.Invoke(s);
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Short });
            if (s.GetPlayerView().Combat is { IsYourTurn: true })
                s.Execute(new EndTurnCommand { Actor = s.Player });
            for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
                s.Advance(Duration.FromSeconds(1));
            if (s.GetPlayerView().Combat is { IsYourTurn: true, You.ActionAvailable: true } && Me(s).Conscious && Of(s, Foe).Conscious)
                return s;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    private static Actor Me(SimulationSession s) => s.Engine.World.Actors[s.Player];

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    [Fact]
    public void Two_hands_hold_a_shield_or_a_second_weapon_and_a_two_handed_weapon_takes_both()
    {
        var s = Yard();
        Assert.Equal(RejectionReason.InvalidAmount,
            s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.OffHand, Item = "dagger" }).Rejection); // the shield is there
        Assert.Equal(RejectionReason.InvalidAmount,
            s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.MainHand, Item = "greatsword" }).Rejection);
        Assert.Equal(RejectionReason.InvalidAmount,
            s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.MainHand, Item = "javelin-of-nobody" }).Rejection);
        TwoBlades(s);
        Assert.Contains(s.GetPlayerView().Equipment, q => q.Slot == EquipmentSlot.OffHand && q.Item == new ItemId("dagger"));
        var extra = Me(s).Profile.Attacks.Single(a => a.Name == OffDagger);
        Assert.True(extra.OffHand);
        Assert.Equal(0, extra.DamageBonus); // a positive modifier is left out

        Me(s).Choices = Me(s).Choices with { FightingStyle = "TwoWeaponFighting" };
        s.Engine.World.Refresh(Me(s));
        Assert.Equal(Me(s).Profile.Attacks.Single(a => a.Name == Short).DamageBonus, Me(s).Profile.Attacks.Single(a => a.Name == OffDagger).DamageBonus);
    }

    [Fact]
    public void The_extra_attack_comes_after_a_light_weapon_attack_and_takes_the_bonus_action()
    {
        var s = Fight();
        Assert.False(s.GetPlayerView().Combat!.You.OffHandAttackAvailable);
        Assert.Equal(RejectionReason.NoActionLeft, s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = OffDagger, OffHand = true }).Rejection);
        Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Short }).Success);
        if (!Of(s, Foe).Conscious || s.GetPlayerView().Combat is null)
            return; // the first blow ended it
        Assert.True(s.GetPlayerView().Combat!.You.OffHandAttackAvailable);
        Assert.Contains(s.GetPlayerView().Combat!.Targets, t => t.Attack == OffDagger && t.Valid);
        Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = OffDagger, OffHand = true }).Success);
        Assert.False(s.GetPlayerView().Combat?.You.BonusActionAvailable ?? false);
        if (s.GetPlayerView().Combat is { } after)
            Assert.False(after.You.OffHandAttackAvailable);
    }

    [Fact]
    public void With_nick_the_extra_attack_is_part_of_the_attack_action_and_leaves_the_bonus_action()
    {
        var s = Fight(x =>
        {
            Me(x).Choices = Me(x).Choices with { Masteries = new[] { "shortsword", "dagger" } };
            x.Engine.World.Refresh(Me(x));
        });
        Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Short }).Success);
        if (!Of(s, Foe).Conscious || s.GetPlayerView().Combat is null)
            return;
        Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = OffDagger, OffHand = true }).Success);
        if (s.GetPlayerView().Combat is { } after)
            Assert.True(after.You.BonusActionAvailable); // Lay On Hands still there
    }

    [Fact]
    public void In_a_fight_taking_a_weapon_is_the_free_interaction_a_second_takes_the_action_and_it_survives_a_save()
    {
        var s = Fight();
        Assert.True(s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.OffHand, Item = null }).Success);
        var second = s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.OffHand, Item = "dagger" });
        Assert.True(second.Success, second.Message);
        Assert.Contains("usi l'azione", second.Message);
        Assert.False(s.GetPlayerView().Combat!.You.ActionAvailable);
        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Contains(loaded.GetPlayerView().Equipment, q => q.Slot == EquipmentSlot.OffHand);
    }
}
