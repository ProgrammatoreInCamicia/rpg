using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>R3b: the Paladin's spells (SRD 5.2.1 p. 53–54 and the spells' own pages).</summary>
public class SpellTests
{
    private static readonly ActorId Foe = new("foe");
    private const string Sword = "Spada lunga";

    /// <summary>A yard at noon: the Paladin at (3,2) with <paramref name="experience"/>, a guard at (4,2) or (8,2).</summary>
    private static SimulationSession Yard(long experience = 0, bool foeNear = false, ulong seed = 1)
    {
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, at: (3, 2))
            .AddActor("foe", "Guardia", "yard", combat: "guard", at: foeNear ? (4, 2) : (8, 2))
            .Build());
        s.AdvanceTo(At(1, 12));
        var me = Me(s);
        me.Experience = experience;
        var before = me.Profile.MaxHitPoints;
        s.Engine.World.Refresh(me);
        me.Vitality.RaiseMaximum(me.Profile.MaxHitPoints - before);
        return s;
    }

    /// <summary>The Paladin strikes the guard next to it first; the first seed where it is then its fresh turn, the guard up.</summary>
    private static SimulationSession Fight(long experience = 0, params string[] prepared)
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Yard(experience, foeNear: true, seed: seed);
            Me(s).PreparedSpells.AddRange(prepared.Except(Me(s).PreparedSpells));
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

    private static Actor Me(SimulationSession s) => s.Engine.World.Actors[s.Player];

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static Encounter E(SimulationSession s) => s.Engine.World.Encounters.Values.Single();

    /// <summary>The dice from here on start with a d20 that is not a 1, so the next plain attack roll hits a target of AC 1.</summary>
    private static void NoNaturalOne(SimulationSession s)
    {
        for (ulong k = 1; ; k++)
            if (new SplitMix64(k).Roll(20) > 1)
            {
                s.Engine.World.Rng = new SplitMix64(k);
                return;
            }
    }

    [Fact]
    public void The_level_one_paladin_has_two_slots_two_spells_prepared_and_a_save_dc_of_eight_plus_charisma_and_proficiency()
    {
        var s = Yard();
        var book = s.GetPlayerView().Spells!;
        Assert.Equal(new[] { "bless", "cure-wounds" }, book.Prepared.Select(p => p.Id));
        Assert.Equal(new SlotView { Level = 1, Remaining = 2, Max = 2 }, Assert.Single(book.Slots));
        Assert.Equal(8 + 3 + 2, book.SaveDc); // Charisma 16
        Assert.False(book.FreeSmite); // Paladin's Smite comes at level 2
        Assert.DoesNotContain(s.GetPlayerView().PendingChoices, c => c.Kind == "PreparedSpells");

        var five = Yard(experience: 6500).GetPlayerView();
        Assert.Equal(new[] { 4, 2 }, five.Spells!.Slots.Select(x => x.Max));
        Assert.Contains(five.Spells.Prepared, p => p.Id == "divine-smite" && p.AlwaysPrepared);
        Assert.Equal(4, five.PendingChoices.Single(c => c.Kind == "PreparedSpells").Picks); // 6 at level 5, 2 known
    }


    [Fact]
    public void The_spellbook_lists_every_paladin_spell_with_its_description_which_are_prepared_and_which_within_reach()
    {
        var book = Yard().GetPlayerView().Spells!;
        Assert.Equal(new[] { "bless", "cure-wounds", "divine-favor", "divine-smite", "protection-from-evil-and-good", "shield-of-faith",
            "aid", "lesser-restoration" }, book.ClassSpells.Select(c => c.Id));
        Assert.All(book.ClassSpells, c => Assert.False(string.IsNullOrWhiteSpace(c.Description)));
        Assert.All(book.Prepared, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
        Assert.Equal(new[] { "bless", "cure-wounds" }, book.ClassSpells.Where(c => c.Prepared).Select(c => c.Id));
        Assert.Equal(new[] { "aid", "lesser-restoration" }, book.ClassSpells.Where(c => !c.WithinReach).Select(c => c.Id));
        Assert.Contains("1d4", book.ClassSpells.Single(c => c.Id == "bless").Description);

        var five = Yard(experience: 6500).GetPlayerView().Spells!;
        Assert.All(five.ClassSpells, c => Assert.True(c.WithinReach));
        Assert.True(five.ClassSpells.Single(c => c.Id == "divine-smite").Prepared);
    }
    [Fact]
    public void Cure_wounds_heals_two_d8_plus_charisma_by_touch_and_spends_a_slot()
    {
        var s = Yard();
        Me(s).Vitality.TakeDamage(Me(s).Vitality.Maximum - 1);
        var cast = s.Execute(new CastCommand { Actor = s.Player, Spell = "cure-wounds", SlotLevel = 1, Targets = new[] { s.Player } });
        Assert.True(cast.Success, cast.Message);
        Assert.InRange(Me(s).Vitality.Current - 1, 2 + 3, 16 + 3);
        Assert.Equal(1, s.GetPlayerView().Spells!.Slots[0].Remaining);
        Assert.Equal(RejectionReason.OutOfReach,
            s.Execute(new CastCommand { Actor = s.Player, Spell = "cure-wounds", SlotLevel = 1, Targets = new[] { Foe } }).Rejection);
        Assert.True(s.Execute(new CastCommand { Actor = s.Player, Spell = "bless", SlotLevel = 1 }).Success);
        Assert.Equal(RejectionReason.NoSlot,
            s.Execute(new CastCommand { Actor = s.Player, Spell = "cure-wounds", SlotLevel = 1, Targets = new[] { s.Player } }).Rejection);
        Assert.Equal(RejectionReason.UnknownCommand,
            s.Execute(new CastCommand { Actor = s.Player, Spell = "aid", SlotLevel = 1 }).Rejection);
    }

    [Fact]
    public void Bless_takes_the_action_adds_a_d4_to_attacks_and_one_slot_a_turn_is_all()
    {
        var s = Fight(prepared: "shield-of-faith");
        Assert.True(s.Execute(new CastCommand { Actor = s.Player, Spell = "bless", SlotLevel = 1 }).Success);
        Assert.False(s.GetPlayerView().Combat!.You.ActionAvailable);
        Assert.True(s.GetPlayerView().Combat!.You.SpellSlotSpent);
        Assert.Equal("bless", s.GetPlayerView().Spells!.Concentrating);
        Assert.Contains(s.GetPlayerView().Effects, f => f.Spell == "bless");
        Assert.Equal(RejectionReason.NoSlot,
            s.Execute(new CastCommand { Actor = s.Player, Spell = "shield-of-faith", SlotLevel = 1 }).Rejection);

        // Next turn: the blessed attack shows its d4.
        s.Execute(new EndTurnCommand { Actor = s.Player });
        for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
            s.Advance(Duration.FromSeconds(1));
        if (s.GetPlayerView().Combat is not { IsYourTurn: true } || !s.Engine.HasEffect(Me(s), "bless"))
            return; // the fight or the blessing ended meanwhile
        var from = E(s).Log.Count;
        s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword });
        Assert.Contains(E(s).Log.Skip(from), l => l.Kind == CombatLogKind.Attack && l.Text.Contains("Benedizione +"));
    }

    [Fact]
    public void Shield_of_faith_raises_the_armor_class_by_two_and_a_new_concentration_ends_the_old()
    {
        var s = Yard();
        Me(s).PreparedSpells.Add("shield-of-faith");
        var ac = s.Engine.World.Actors[s.Player].Profile.ArmorClass;
        Assert.True(s.Execute(new CastCommand { Actor = s.Player, Spell = "bless", SlotLevel = 1 }).Success);
        Assert.True(s.Execute(new CastCommand { Actor = s.Player, Spell = "shield-of-faith", SlotLevel = 1 }).Success);
        Assert.False(s.Engine.HasEffect(Me(s), "bless"));
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "ConcentrationLost");

        // The guard strikes the shielded Paladin: the AC it rolls against is two higher.
        Of(s, Foe).PlaceAt(Spot.CenterOf(new GridPos(4, 2)));
        s.Execute(new AttackCommand { Actor = Foe, Target = s.Player, Attack = "Lancia" });
        var blow = E(s).Log.First(l => l.Kind == CombatLogKind.Attack && l.Actor == Foe);
        Assert.Equal(ac + 2, blow.ArmorClass);
    }

    [Fact]
    public void Concentration_ends_when_incapacitated_and_damage_calls_for_a_constitution_save()
    {
        var s = Fight();
        Assert.True(s.Execute(new CastCommand { Actor = s.Player, Spell = "bless", SlotLevel = 1 }).Success);
        s.Execute(new EndTurnCommand { Actor = s.Player });
        for (var i = 0; i < 30 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
            s.Advance(Duration.FromSeconds(1));
        var hit = E(s).Log.Any(l => l.Kind == CombatLogKind.Attack && l.Actor == Foe && l.Hit == true && l.Damage > 0);
        if (hit)
            Assert.Contains(E(s).Log, l => l.Kind == CombatLogKind.Save && l.Text.Contains("la concentrazione"));
        if (Me(s).Concentrating is null)
            return; // lost to the blow
        s.Engine.AddCondition(E(s), Me(s), ConditionKind.Stunned);
        Assert.Null(Me(s).Concentrating);
        Assert.False(s.Engine.HasEffect(Me(s), "bless"));
    }

    [Fact]
    public void Divine_smite_adds_two_d8_radiant_to_a_melee_hit_and_spends_the_slot_and_bonus_action_only_on_a_hit()
    {
        var s = Fight(experience: 300);
        Assert.True(s.GetPlayerView().Spells!.FreeSmite);
        Of(s, Foe).Profile = Of(s, Foe).Profile with { ArmorClass = 1 };
        NoNaturalOne(s);
        var from = E(s).Log.Count;
        var smote = s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword, SmiteSlot = 1 });
        Assert.True(smote.Success, smote.Message);
        Assert.Contains(E(s).Log.Skip(from), l => l.Kind == CombatLogKind.Attack && l.Text.Contains("Punizione divina ["));
        Assert.Equal(1, s.GetPlayerView().Spells!.Slots[0].Remaining);
        Assert.False(s.GetPlayerView().Combat!.You.BonusActionAvailable);
        Assert.True(s.GetPlayerView().Spells!.FreeSmite); // the slot was used, not the free one

        var outside = Yard(experience: 300, foeNear: true);
        Assert.Equal(RejectionReason.NoSlot,
            outside.Execute(new AttackCommand { Actor = outside.Player, Target = Foe, Attack = Sword, SmiteFree = true }).Rejection);
    }

    [Fact]
    public void Divine_favor_takes_the_bonus_action_and_adds_a_d4_to_weapon_hits()
    {
        var s = Fight(prepared: "divine-favor");
        Assert.True(s.Execute(new CastCommand { Actor = s.Player, Spell = "divine-favor", SlotLevel = 1 }).Success);
        Assert.True(s.GetPlayerView().Combat!.You.ActionAvailable);
        Assert.False(s.GetPlayerView().Combat!.You.BonusActionAvailable);
        Of(s, Foe).Profile = Of(s, Foe).Profile with { ArmorClass = 1 };
        NoNaturalOne(s);
        var from = E(s).Log.Count;
        s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword });
        Assert.Contains(E(s).Log.Skip(from), l => l.Kind == CombatLogKind.Attack && l.Text.Contains("Favore divino ["));
    }

    [Fact]
    public void Aid_raises_the_hit_points_for_eight_hours_and_lesser_restoration_ends_poison()
    {
        var s = Yard(experience: 6500);
        Me(s).PreparedSpells.AddRange(new[] { "aid", "lesser-restoration" });
        var max = Me(s).Vitality.Maximum;
        Assert.True(s.Execute(new CastCommand { Actor = s.Player, Spell = "aid", SlotLevel = 2 }).Success);
        Assert.Equal(max + 5, Me(s).Vitality.Maximum);
        Assert.Equal(max + 5, s.GetPlayerView().MaxHitPoints);
        s.Advance(Duration.FromHours(8));
        Assert.Equal(max, Me(s).Vitality.Maximum);
        Assert.DoesNotContain(s.GetPlayerView().Effects, f => f.Spell == "aid");

        s.Engine.AddCondition(null, Me(s), ConditionKind.Poisoned);
        Assert.True(s.Execute(new CastCommand { Actor = s.Player, Spell = "lesser-restoration", SlotLevel = 2 , Targets = new[] { s.Player } }).Success);
        Assert.False(s.Engine.Has(Me(s), ConditionKind.Poisoned));
    }

    [Fact]
    public void A_long_rest_gives_back_the_slots_and_the_free_smite_and_lets_one_spell_be_swapped()
    {
        var s = Yard(experience: 300);
        var prep = s.GetPlayerView().PendingChoices.Single(c => c.Kind == "PreparedSpells");
        Assert.True(s.Execute(new ChooseCommand { Actor = s.Player, Choice = prep.Id, Options = new[] { "shield-of-faith" } }).Success);
        Assert.True(s.Execute(new CastCommand { Actor = s.Player, Spell = "bless", SlotLevel = 1 }).Success);
        Me(s).FreeSmiteUsed = true;
        var rest = s.Execute(new RestCommand { Actor = s.Player, Long = true });
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "ConcentrationLost"); // asleep, it lets the blessing go
        s.AdvanceUntilCompleted(rest.Action!.Value, Duration.FromHours(9));
        Assert.Equal(2, s.GetPlayerView().Spells!.Slots[0].Remaining);
        Assert.True(s.GetPlayerView().Spells!.FreeSmite);

        var swap = s.GetPlayerView().PendingChoices.Single(c => c.Kind == "SwapSpell");
        Assert.True(swap.Optional);
        Assert.True(s.Execute(new ChooseCommand { Actor = s.Player, Choice = swap.Id, Options = new[] { "bless", "divine-favor" } }).Success);
        Assert.Contains(s.GetPlayerView().Spells!.Prepared, p => p.Id == "divine-favor");
        Assert.DoesNotContain(s.GetPlayerView().Spells!.Prepared, p => p.Id == "bless");
    }

    [Fact]
    public void Spells_in_effect_and_concentration_survive_save_and_load()
    {
        var s = Yard(experience: 6500);
        Me(s).PreparedSpells.Add("aid");
        Assert.True(s.Execute(new CastCommand { Actor = s.Player, Spell = "bless", SlotLevel = 1 }).Success);
        Assert.True(s.Execute(new CastCommand { Actor = s.Player, Spell = "aid", SlotLevel = 2 }).Success);
        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal("bless", loaded.GetPlayerView().Spells!.Concentrating);
        loaded.Advance(Duration.FromHours(9));
        Assert.Empty(loaded.GetPlayerView().Effects);
        Assert.Null(loaded.GetPlayerView().Spells!.Concentrating);
    }
}
