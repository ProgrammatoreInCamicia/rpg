using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>T1: a lit torch is held in the off hand (SRD 5.2.1 Torch); a shield takes the Utilize action to don or doff.</summary>
public class TorchHandTests
{
    private static readonly ActorId Foe = new("foe"), Guard = new("guard");
    private const string Sword = "Spada lunga";

    /// <summary>A yard at night: the Paladin (longsword, shield, 3 torches) at (3,3), a guard of the watch at (4,3).</summary>
    private static SimulationSession Yard(ulong seed = 1)
    {
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYYYY", 8).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, at: (3, 3), torches: 3)
            .AddActor("foe", "Guardia", "yard", combat: "guard", at: (4, 3), torches: 1)
            .Build());
        s.AdvanceTo(At(1, 22));
        Me(s).Give("dagger", 1);
        Me(s).Give("greatsword", 1);
        return s;
    }

    private static Actor Me(SimulationSession s) => s.Engine.World.Actors[s.Player];

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    [Fact]
    public void A_torch_needs_the_off_hand_free_and_can_put_the_shield_away_to_light()
    {
        var s = Yard();
        var refused = s.Execute(new TorchCommand { Actor = s.Player, Lit = true });
        Assert.Equal(RejectionReason.NoFreeHand, refused.Rejection);
        Assert.Contains("scudo", refused.Message);
        Assert.Equal(3, s.GetPlayerView().Torches);

        var lit = s.Execute(new TorchCommand { Actor = s.Player, Lit = true, StowShield = true });
        Assert.True(lit.Success, lit.Message);
        Assert.Contains(s.GetPlayerView().Equipment, q => q.Slot == EquipmentSlot.OffHand && q.Item == new ItemId("torch"));
        Assert.DoesNotContain(s.GetPlayerView().Equipment, q => q.Slot == EquipmentSlot.Shield);
        Assert.Contains(s.GetPlayerView().Inventory, i => i.Item == new ItemId("shield")); // put away, still carried
        Assert.Equal(new Dice(1, 8), Me(s).Profile.Attacks.Single(a => a.Name == Sword).Damage); // the longsword is in one hand

        Assert.Equal(RejectionReason.NoFreeHand, s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.Shield, Item = "shield" }).Rejection);
        Assert.Equal(RejectionReason.NoFreeHand, s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.OffHand, Item = "dagger" }).Rejection);
        Assert.Equal(RejectionReason.NoFreeHand, s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.MainHand, Item = "greatsword" }).Rejection);

        Assert.True(s.Execute(new TorchCommand { Actor = s.Player, Lit = false }).Success);
        Assert.DoesNotContain(s.GetPlayerView().Equipment, q => q.Slot is EquipmentSlot.OffHand or EquipmentSlot.Shield); // the shield stays away
        Assert.DoesNotContain(Me(s).Profile.Attacks, a => a.Torch);
        Assert.True(s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.Shield, Item = "shield" }).Success);
    }

    [Fact]
    public void The_lit_torch_is_a_simple_melee_weapon_doing_one_fire_damage()
    {
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var s = Yard(seed);
            Assert.True(s.Execute(new TorchCommand { Actor = s.Player, Lit = true, StowShield = true }).Success);
            var torch = Me(s).Profile.Attacks.Single(a => a.Torch);
            Assert.Equal("Torcia", torch.Name);
            Assert.Equal(DamageType.Fire, torch.Type);
            Assert.Equal(Abilities.Modifier(Me(s).Sheet.Strength) + Me(s).Sheet.ProficiencyBonus, torch.AttackBonus);
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Torcia" });
            var blow = s.Engine.World.Encounters.Values.SingleOrDefault()?.Log.FirstOrDefault(l => l.Kind == CombatLogKind.Attack && l.Actor == s.Player)
                       ?? s.Engine.World.LastFight?.Log.FirstOrDefault(l => l.Kind == CombatLogKind.Attack && l.Actor == s.Player);
            if (blow is not { Hit: true, Critical: false })
                continue;
            Assert.Equal(1, blow.Damage);
            Assert.Contains("da fuoco", blow.Text);
            return;
        }
        throw new InvalidOperationException("No seed gives a plain hit.");
    }

    [Fact]
    public void In_a_fight_lighting_is_the_free_interaction_and_the_shield_takes_the_action()
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Yard(seed);
            Assert.True(s.Execute(new TorchCommand { Actor = s.Player, Lit = true, StowShield = true }).Success); // to see the guard
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword });
            if (s.GetPlayerView().Combat is { IsYourTurn: true })
                s.Execute(new EndTurnCommand { Actor = s.Player });
            for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
                s.Advance(Duration.FromSeconds(1));
            if (s.GetPlayerView().Combat is not { IsYourTurn: true, You: { ActionAvailable: true, InteractionAvailable: true } } || !Me(s).Conscious)
                continue;

            Assert.True(s.Execute(new TorchCommand { Actor = s.Player, Lit = false }).Success); // the free interaction
            Assert.False(s.GetPlayerView().Combat!.You.InteractionAvailable);
            Assert.True(s.GetPlayerView().Combat!.You.ActionAvailable);
            var shield = s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.Shield, Item = "shield" });
            Assert.True(shield.Success, shield.Message);
            Assert.Contains("usi l'azione", shield.Message); // SRD: Utilize to don it
            Assert.False(s.GetPlayerView().Combat!.You.ActionAvailable);
            Assert.Equal(RejectionReason.NoActionLeft, s.Execute(new TorchCommand { Actor = s.Player, Lit = true, StowShield = true }).Rejection);
            return;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    [Fact]
    public void An_npc_puts_its_shield_away_to_light_a_torch_and_an_old_save_with_both_keeps_the_torch()
    {
        var s = Yard();
        Assert.NotNull(Of(s, Foe).Equipment.Shield);
        Assert.True(s.Execute(new TorchCommand { Actor = Foe, Lit = true }).Success);
        Assert.Null(Of(s, Foe).Equipment.Shield);

        var old = Yard();
        Me(old).TorchLitUntil = old.Now.Plus(Duration.FromMinutes(30)); // as a save from before T1: torch and shield together
        var loaded = old.SaveAndReload();
        var me = loaded.Engine.World.Actors[loaded.Player];
        Assert.Null(me.Equipment.Shield);
        Assert.True(loaded.Engine.TorchLit(me));
        Assert.Contains(me.Profile.Attacks, a => a.Torch);
    }

    [Fact]
    public void Donning_a_shield_in_a_fight_takes_the_action_and_leaves_the_free_interaction()
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Yard(seed);
            s.AdvanceTo(At(2, 12)); // daylight
            Assert.True(s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.Shield, Item = null }).Success);
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword });
            if (s.GetPlayerView().Combat is { IsYourTurn: true })
                s.Execute(new EndTurnCommand { Actor = s.Player });
            for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
                s.Advance(Duration.FromSeconds(1));
            if (s.GetPlayerView().Combat is not { IsYourTurn: true, You: { ActionAvailable: true, InteractionAvailable: true } } || !Me(s).Conscious)
                continue;
            Assert.True(s.Execute(new EquipCommand { Actor = s.Player, Slot = EquipmentSlot.Shield, Item = "shield" }).Success);
            Assert.False(s.GetPlayerView().Combat!.You.ActionAvailable);
            Assert.True(s.GetPlayerView().Combat!.You.InteractionAvailable);
            return;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }
}
