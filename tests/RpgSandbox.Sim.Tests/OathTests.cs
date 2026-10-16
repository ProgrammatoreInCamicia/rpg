using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>R3c: Channel Divinity and the Oath of Devotion (SRD 5.2.1 p. 54–56).</summary>
public class OathTests
{
    private static readonly ActorId Foe = new("foe");
    private const string Sword = "Spada lunga";

    /// <summary>A yard: a level 3 Paladin of the Oath of Devotion at (3,2), a guard next to it at (4,2).</summary>
    private static SimulationSession Yard(int hour = 12, ulong seed = 1, bool sworn = true)
    {
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYYYY", 8).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, at: (3, 2))
            .AddActor("foe", "Guardia", "yard", combat: "guard", at: (4, 2))
            .Build());
        s.AdvanceTo(At(1, hour));
        var me = Me(s);
        me.Experience = 900;
        var before = me.Profile.MaxHitPoints;
        s.Engine.World.Refresh(me);
        me.Vitality.RaiseMaximum(me.Profile.MaxHitPoints - before);
        if (sworn)
            me.Choices = me.Choices with { Subclass = "devotion" };
        return s;
    }

    private static Actor Me(SimulationSession s) => s.Engine.World.Actors[s.Player];

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    /// <summary>The Paladin strikes first; the first seed where it is then its fresh turn with the guard up.</summary>
    private static SimulationSession Fight(int hour = 12)
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Yard(hour, seed);
            Of(s, Foe).Vitality.RaiseMaximum(100);
            Of(s, Foe).Vitality.Heal(100);
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword });
            if (s.GetPlayerView().Combat is { IsYourTurn: true })
                s.Execute(new EndTurnCommand { Actor = s.Player });
            for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
                s.Advance(Duration.FromSeconds(1));
            if (s.GetPlayerView().Combat is { IsYourTurn: true, You.ActionAvailable: true } && Me(s).Conscious && Of(s, Foe).Conscious)
                return s;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    [Fact]
    public void At_level_three_the_oath_is_chosen_and_brings_its_spells_and_two_uses_of_channel_divinity()
    {
        var s = Yard(sworn: false);
        var oath = s.GetPlayerView().PendingChoices.Single(c => c.Kind == "Subclass");
        Assert.Equal(new[] { "devotion" }, oath.Options);
        Assert.True(s.Execute(new ChooseCommand { Actor = s.Player, Choice = oath.Id, Options = new[] { "devotion" } }).Success);
        Assert.Equal("devotion", s.GetPlayerView().Subclass);
        var prepared = s.GetPlayerView().Spells!.Prepared;
        Assert.Contains(prepared, p => p.Id == "protection-from-evil-and-good" && p.AlwaysPrepared);
        Assert.Contains(prepared, p => p.Id == "shield-of-faith" && p.AlwaysPrepared);
        Assert.DoesNotContain(prepared, p => p.Id == "aid"); // from level 5
        Assert.Equal(new PoolView { Remaining = 2, Max = 2 }, s.GetPlayerView().ChannelDivinity);
    }

    [Fact]
    public void Divine_sense_spends_a_use_and_tells_what_is_near_a_short_rest_gives_one_back_a_long_rest_all()
    {
        var s = Yard();
        var sensed = s.Execute(new ChannelDivinityCommand { Actor = s.Player, Effect = "DivineSense" });
        Assert.True(sensed.Success, sensed.Message);
        Assert.Contains("nessun celestiale", sensed.Message);
        Assert.True(s.Execute(new ChannelDivinityCommand { Actor = s.Player, Effect = "DivineSense" }).Success);
        Assert.Equal(RejectionReason.NoSlot, s.Execute(new ChannelDivinityCommand { Actor = s.Player, Effect = "DivineSense" }).Rejection);

        var rest = s.Execute(new RestCommand { Actor = s.Player, Long = false });
        s.AdvanceUntilCompleted(rest.Action!.Value, Duration.FromHours(2));
        Assert.Equal(1, s.GetPlayerView().ChannelDivinity!.Remaining);
        rest = s.Execute(new RestCommand { Actor = s.Player, Long = true });
        s.AdvanceUntilCompleted(rest.Action!.Value, Duration.FromHours(9));
        Assert.Equal(2, s.GetPlayerView().ChannelDivinity!.Remaining);
    }

    [Fact]
    public void Sacred_weapon_adds_charisma_to_the_attack_rolls_with_that_weapon_and_lights_the_night()
    {
        var s = Fight();
        var bonus = Me(s).Profile.Attacks.Single(a => a.Name == Sword).AttackBonus;
        var log = s.Engine.World.Encounters.Values.Single().Log;
        var from = log.Count;
        Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword, SacredWeapon = true }).Success);
        Assert.Equal(1, s.GetPlayerView().ChannelDivinity!.Remaining);
        var blow = log.Skip(from).Single(l => l.Kind == CombatLogKind.Attack && l.Actor == s.Player);
        Assert.Equal(bonus + 3, blow.Bonus); // Charisma 16
        Assert.Contains(log.Skip(from), l => l.Kind == CombatLogKind.Spell && l.Text.Contains("luce sacra"));
        // At night the consecrated weapon lights the ground around it.
        var night = Yard(hour: 23);
        var map = night.Engine.World.Maps[new AreaId("a")];
        var far = new GridPos(3, 6); // 20 feet away
        var dark = night.Engine.LightOn(map, far);
        Me(night).Effects.Add(new SpellEffect { Spell = "sacred-weapon", Caster = night.Player, Until = night.Now.Plus(Duration.FromMinutes(10)), Item = "longsword" });
        Assert.True(night.Engine.LightOn(map, far) > dark);

        var plain = Yard(sworn: false);
        Assert.Equal(RejectionReason.NoSlot,
            plain.Execute(new AttackCommand { Actor = plain.Player, Target = Foe, Attack = Sword, SacredWeapon = true }).Rejection);
    }

    [Fact]
    public void Protection_from_evil_and_good_is_cast_by_touch_with_concentration_and_the_oath_survives_a_save()
    {
        var s = Yard();
        Assert.True(s.Execute(new CastCommand { Actor = s.Player, Spell = "protection-from-evil-and-good", SlotLevel = 1, Targets = new[] { s.Player } }).Success);
        Assert.Equal("protection-from-evil-and-good", s.GetPlayerView().Spells!.Concentrating);
        Assert.DoesNotContain("è protetto dal male", s.Engine.ConditionEdges(Of(s, Foe), Me(s), withinFiveFeet: true).Reasons); // a guard is human
        s.Execute(new ChannelDivinityCommand { Actor = s.Player, Effect = "DivineSense" });

        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal("devotion", loaded.GetPlayerView().Subclass);
        Assert.Equal(1, loaded.GetPlayerView().ChannelDivinity!.Remaining);
    }
}
