using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim.Tests;

/// <summary>C1: pure combat rules from SRD 5.2.1 (weapons, attacks, Hit Points, death, Initiative).</summary>
public class CombatRulesTests
{
    /// <summary>A seed whose first d20 shows <paramref name="face"/>, to test a natural 20 or 1 deterministically.</summary>
    private static ulong SeedForFirstD20(int face)
    {
        for (ulong seed = 0; ; seed++)
            if (new SplitMix64(seed).Roll(20) == face)
                return seed;
    }

    private static readonly AttackProfile Longsword = CombatProfiles.Paladin().Attacks[0];

    // ---------------------------------------------------------------- weapons and profiles

    [Fact]
    public void Weapons_match_the_SRD_table()
    {
        Assert.Equal(new Dice(1, 8), Weapons.Longsword.Damage);
        Assert.Equal(new Dice(1, 10), Weapons.Longsword.TwoHandedDamage);
        Assert.Equal(WeaponMastery.Sap, Weapons.Longsword.Mastery);
        Assert.Equal((30, 120), Weapons.Javelin.Range);
        Assert.True(Weapons.Scimitar.Has(WeaponProperty.Finesse | WeaponProperty.Light));
        Assert.True(Weapons.LightCrossbow.Ranged);
        Assert.Equal(new Dice(2, 6), Weapons.Greatsword.Damage);
    }

    [Fact]
    public void The_paladin_fights_as_its_sheet_says()
    {
        var paladin = CombatProfiles.Paladin();

        Assert.Equal(11, paladin.MaxHitPoints); // 10 + Con +1
        Assert.Equal(18, paladin.ArmorClass);
        Assert.Equal(0, paladin.InitiativeBonus);
        Assert.True(paladin.MakesDeathSaves);

        var sword = paladin.Attacks[0];
        Assert.Equal((4, new Dice(1, 8), 2, WeaponMastery.Sap, true), (sword.AttackBonus, sword.Damage, sword.DamageBonus, sword.Mastery!.Value, sword.Melee));
        var javelin = paladin.Attacks[1];
        Assert.Equal((4, new Dice(1, 6), 2, WeaponMastery.Slow, false), (javelin.AttackBonus, javelin.Damage, javelin.DamageBonus, javelin.Mastery!.Value, javelin.Melee));
        Assert.Equal((30, 120), javelin.Range);
        var fist = paladin.Attacks[2];
        Assert.Equal((4, Dice.None, 3), (fist.AttackBonus, fist.Damage, fist.DamageBonus)); // Str +2, PB +2; 1 + Str
    }

    [Fact]
    public void Finesse_versatile_heavy_and_proficiency_follow_the_SRD()
    {
        var nimble = Sheets.Raider(); // Str 11, Dex 15
        Assert.Equal(4, AttackProfile.With(nimble, Weapons.Scimitar, proficient: true).AttackBonus);  // Dex +2, PB +2
        Assert.Equal(2, AttackProfile.With(nimble, Weapons.Scimitar, proficient: false).AttackBonus); // no PB
        Assert.Equal(0, AttackProfile.With(nimble, Weapons.Longsword, proficient: false).AttackBonus); // Str +0

        var paladin = Sheets.Paladin();
        Assert.Equal(new Dice(1, 10), AttackProfile.With(paladin, Weapons.Longsword, true, twoHanded: true).Damage);
        Assert.Null(AttackProfile.With(paladin, Weapons.Longsword, true).Mastery); // not unlocked unless said so

        Assert.True(AttackProfile.With(nimble, Weapons.Greatsword, true).HeavyDisadvantage);   // Str 11 < 13
        Assert.False(AttackProfile.With(paladin, Weapons.Greatsword, true).HeavyDisadvantage); // Str 15
    }

    [Fact]
    public void Stat_block_creatures_are_copied_from_the_SRD()
    {
        var bandit = CombatProfiles.Bandit();
        Assert.Equal((12, 11, 1, false), (bandit.ArmorClass, bandit.MaxHitPoints, bandit.InitiativeBonus, bandit.MakesDeathSaves));
        Assert.Equal((3, new Dice(1, 6), 1), (bandit.Attacks[0].AttackBonus, bandit.Attacks[0].Damage, bandit.Attacks[0].DamageBonus));
        Assert.Equal((80, 320), bandit.Attacks[1].Range);
        Assert.Equal((16, 11), (CombatProfiles.Guard().ArmorClass, CombatProfiles.Guard().MaxHitPoints));
        Assert.Equal((10, 4), (CombatProfiles.Commoner().ArmorClass, CombatProfiles.Commoner().MaxHitPoints));
    }

    // ---------------------------------------------------------------- attack rolls

    [Fact]
    public void A_natural_20_always_hits_and_doubles_the_damage_dice()
    {
        var hit = Attacks.Roll(new SplitMix64(SeedForFirstD20(20)), Longsword, targetArmorClass: 99);

        Assert.True(hit.Hit);
        Assert.True(hit.Critical);
        Assert.Equal(2, hit.DamageDice.Count); // 2d8 instead of 1d8, the bonus once
        Assert.Equal(hit.DamageDice.Sum() + 2, hit.Damage);
        Assert.Contains("COLPO CRITICO", hit.Describe());
    }

    [Fact]
    public void A_natural_1_always_misses()
    {
        var miss = Attacks.Roll(new SplitMix64(SeedForFirstD20(1)), Longsword, targetArmorClass: 0);

        Assert.False(miss.Hit);
        Assert.Equal(0, miss.Damage);
        Assert.Empty(miss.DamageDice);
    }

    [Fact]
    public void Otherwise_reaching_the_armor_class_hits()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            var result = Attacks.Roll(new SplitMix64(seed), Longsword, targetArmorClass: 12);
            var face = result.Roll.Kept;
            Assert.Equal(face == 20 || (face != 1 && face + 4 >= 12), result.Hit);
            Assert.Equal(face == 20, result.Critical);
            if (result.Hit && !result.Critical)
                Assert.InRange(result.Damage, 3, 10); // 1d8 + 2
        }
    }

    [Fact]
    public void Any_hit_on_an_unconscious_target_nearby_is_a_critical_hit()
    {
        var seed = Enumerable.Range(0, 500).Select(s => (ulong)s)
            .First(s => new SplitMix64(s).Roll(20) is >= 8 and < 20); // a plain hit against AC 12
        var result = Attacks.Roll(new SplitMix64(seed), Longsword, 12, critOnHit: true);

        Assert.True(result.Critical);
        Assert.Equal(2, result.DamageDice.Count);
    }

    [Fact]
    public void Graze_deals_the_ability_modifier_on_a_miss()
    {
        var greatsword = AttackProfile.With(Sheets.Paladin(), Weapons.Greatsword, true, masteryUnlocked: true);
        var miss = Attacks.Roll(new SplitMix64(SeedForFirstD20(1)), greatsword, 12);

        Assert.False(miss.Hit);
        Assert.True(miss.Grazed);
        Assert.Equal(2, miss.Damage); // Strength +2
        Assert.Contains("sfiora", miss.Describe());
    }

    [Fact]
    public void An_unarmed_strike_deals_flat_damage()
    {
        var fist = AttackProfile.Unarmed(Sheets.Paladin());
        var hit = Attacks.Roll(new SplitMix64(SeedForFirstD20(15)), fist, 10);

        Assert.True(hit.Hit);
        Assert.Empty(hit.DamageDice);
        Assert.Equal(3, hit.Damage);
    }

    [Fact]
    public void The_same_seed_gives_the_same_fight()
    {
        static string Fight(ulong seed)
        {
            var rng = new SplitMix64(seed);
            return string.Join("|", Enumerable.Range(0, 10).Select(_ => Attacks.Roll(rng, Longsword, 12).Describe()));
        }

        Assert.Equal(Fight(42), Fight(42));
        Assert.NotEqual(Fight(42), Fight(43));
    }

    // ---------------------------------------------------------------- hit points and death

    [Fact]
    public void A_monster_dies_at_0_hit_points()
    {
        var bandit = CombatProfiles.Bandit().NewVitality();

        Assert.Equal(DamageOutcome.Hurt, bandit.TakeDamage(10));
        Assert.Equal(DamageOutcome.Killed, bandit.TakeDamage(1));
        Assert.Equal(LifeState.Dead, bandit.State);
        Assert.Equal(DamageOutcome.NoEffect, bandit.TakeDamage(5));
        Assert.Equal(0, bandit.Heal(5)); // the dead are not healed
    }

    [Fact]
    public void A_character_falls_dying_unless_the_damage_left_reaches_its_maximum()
    {
        var down = new Vitality(12, makesDeathSaves: true, current: 6);
        Assert.Equal(DamageOutcome.Down, down.TakeDamage(17)); // 11 left over < 12
        Assert.Equal(LifeState.Dying, down.State);
        Assert.True(down.Unconscious);

        var dead = new Vitality(12, makesDeathSaves: true, current: 6);
        Assert.Equal(DamageOutcome.Killed, dead.TakeDamage(18)); // SRD example: 12 left over = maximum
    }

    [Fact]
    public void Damage_at_0_hit_points_counts_as_failed_death_saves()
    {
        var hero = new Vitality(11, makesDeathSaves: true, current: 0);
        Assert.Equal(LifeState.Dying, hero.State);

        Assert.Equal(DamageOutcome.DeathSaveFailed, hero.TakeDamage(2));
        Assert.Equal(1, hero.DeathSaveFailures);
        Assert.Equal(DamageOutcome.Killed, hero.TakeDamage(2, critical: true)); // two more: three
        Assert.Equal(LifeState.Dead, hero.State);

        var another = new Vitality(11, makesDeathSaves: true, current: 0);
        Assert.Equal(DamageOutcome.Killed, another.TakeDamage(11)); // damage equal to the maximum
    }

    [Fact]
    public void A_stable_character_hit_again_starts_dying()
    {
        var hero = new Vitality(11, makesDeathSaves: true, current: 0);
        Assert.True(hero.Stabilize());
        Assert.False(hero.Stabilize());

        Assert.Equal(DamageOutcome.DeathSaveFailed, hero.TakeDamage(1));
        Assert.Equal(LifeState.Dying, hero.State);
    }

    [Fact]
    public void Death_saves_follow_the_SRD()
    {
        var twenty = new Vitality(11, makesDeathSaves: true, current: 0);
        Assert.Equal(DeathSaveOutcome.RegainedConsciousness, twenty.RollDeathSave(new SplitMix64(SeedForFirstD20(20)), out _));
        Assert.Equal((LifeState.Alive, 1), (twenty.State, twenty.Current));

        var one = new Vitality(11, makesDeathSaves: true, current: 0);
        Assert.Equal(DeathSaveOutcome.Failure, one.RollDeathSave(new SplitMix64(SeedForFirstD20(1)), out _));
        Assert.Equal(2, one.DeathSaveFailures);

        var steady = new Vitality(11, makesDeathSaves: true, current: 0);
        var success = new SplitMix64(SeedForFirstD20(10));
        Assert.Equal(DeathSaveOutcome.Success, steady.RollDeathSave(success, out var ten));
        Assert.Equal(10, ten);
        Assert.Equal(DeathSaveOutcome.Success, steady.RollDeathSave(new SplitMix64(SeedForFirstD20(15)), out _));
        Assert.Equal(DeathSaveOutcome.Stabilized, steady.RollDeathSave(new SplitMix64(SeedForFirstD20(19)), out _));
        Assert.Equal((LifeState.Stable, 0, 0), (steady.State, steady.DeathSaveSuccesses, steady.DeathSaveFailures));

        var doomed = new Vitality(11, makesDeathSaves: true, current: 0);
        doomed.RollDeathSave(new SplitMix64(SeedForFirstD20(9)), out _);
        Assert.Equal(DeathSaveOutcome.Died, doomed.RollDeathSave(new SplitMix64(SeedForFirstD20(1)), out _));
        Assert.Equal(LifeState.Dead, doomed.State);
    }

    [Fact]
    public void Knocking_out_spares_at_1_hit_point_until_healed()
    {
        var bandit = CombatProfiles.Bandit().NewVitality();

        Assert.Equal(DamageOutcome.KnockedOut, bandit.TakeDamage(30, knockOut: true));
        Assert.Equal((LifeState.Alive, 1, true, true), (bandit.State, bandit.Current, bandit.KnockedOut, bandit.Unconscious));
        Assert.Equal("tramortito", bandit.Describe());

        Assert.Equal(1, bandit.Heal(1));
        Assert.False(bandit.Unconscious);
    }

    [Fact]
    public void Healing_is_capped_and_wakes_the_dying()
    {
        var hero = new Vitality(11, makesDeathSaves: true, current: 0);
        hero.TakeDamage(1); // one failure

        Assert.Equal(11, hero.Heal(50));
        Assert.Equal((LifeState.Alive, 11, 0), (hero.State, hero.Current, hero.DeathSaveFailures));
        Assert.Equal("illeso", hero.Describe());
        hero.TakeDamage(3);
        Assert.Equal("ferito", hero.Describe());
        hero.TakeDamage(4);
        Assert.Equal("malconcio", hero.Describe());
    }

    // ---------------------------------------------------------------- initiative

    [Fact]
    public void Initiative_orders_by_total_then_bonus_then_id()
    {
        D20Roll Fixed(int face, int bonus) => new() { First = face, Kept = face, Bonus = bonus, Advantage = false, Disadvantage = false };
        var order = Initiative.Order(new[]
        {
            new InitiativeEntry("b-low", Fixed(10, 0), 0),
            new InitiativeEntry("guard", Fixed(9, 1), 1),
            new InitiativeEntry("a-low", Fixed(10, 0), 0),
            new InitiativeEntry("fast", Fixed(15, 3), 3),
        });

        Assert.Equal(new[] { "fast", "guard", "a-low", "b-low" }, order.Select(e => e.Id));
    }

    [Fact]
    public void A_surprised_combatant_rolls_initiative_with_disadvantage()
    {
        var surprised = Initiative.Roll(new SplitMix64(7), "bandit", 1, surprised: true);

        Assert.True(surprised.Roll.Disadvantage);
        Assert.Equal(Math.Min(surprised.Roll.First, surprised.Roll.Second!.Value), surprised.Roll.Kept);
    }

    // ---------------------------------------------------------------- edge cases from the C1 review

    [Fact]
    public void A_hit_for_0_damage_changes_nothing_even_at_0_hit_points()
    {
        var dying = new Vitality(11, makesDeathSaves: true, current: 0);
        Assert.Equal(DamageOutcome.NoEffect, dying.TakeDamage(0));
        Assert.Equal((LifeState.Dying, 0), (dying.State, dying.DeathSaveFailures));

        var stable = new Vitality(11, makesDeathSaves: true, current: 0);
        stable.Stabilize();
        Assert.Equal(DamageOutcome.NoEffect, stable.TakeDamage(0));
        Assert.Equal(LifeState.Stable, stable.State);
    }

    [Fact]
    public void Huge_healing_is_capped_without_overflow()
    {
        var hero = new Vitality(11, makesDeathSaves: true, current: 1);

        Assert.Equal(10, hero.Heal(int.MaxValue));
        Assert.Equal(11, hero.Current);
    }

    [Fact]
    public void A_knock_out_ends_only_when_hit_points_are_actually_regained()
    {
        var tiny = new Vitality(1, makesDeathSaves: false);
        Assert.Equal(DamageOutcome.KnockedOut, tiny.TakeDamage(1, knockOut: true));

        Assert.Equal(0, tiny.Heal(1)); // already at its maximum: nothing regained
        Assert.True(tiny.KnockedOut);
    }

    [Fact]
    public void Wound_descriptions_hold_for_huge_hit_point_maximums()
    {
        var giant = new Vitality(int.MaxValue, makesDeathSaves: false);
        giant.TakeDamage(1);

        Assert.Equal("ferito", giant.Describe());
    }
}
