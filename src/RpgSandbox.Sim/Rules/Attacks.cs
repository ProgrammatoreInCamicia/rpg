namespace RpgSandbox.Sim.Rules;

// Attack rolls and damage (SRD 5.2.1: "Rolling 20 or 1", "Critical Hits", weapon properties, Unarmed Strike).

/// <summary>
/// Everything needed to make one kind of attack, already worked out: what a stat block prints ("+3, 1d6 + 1"), or what
/// a character's sheet and weapon give.
/// </summary>
public sealed record AttackProfile
{
    public required string Name { get; init; }
    public required int AttackBonus { get; init; }
    public required Dice Damage { get; init; }
    public required int DamageBonus { get; init; }
    public required DamageType Type { get; init; }

    /// <summary>A melee attack (reach) rather than a ranged one (range).</summary>
    public required bool Melee { get; init; }

    /// <summary>Melee reach in feet (5, or 10 with Reach).</summary>
    public int ReachFeet { get; init; } = 5;

    /// <summary>Normal and long range in feet, for ranged or thrown attacks.</summary>
    public (int Normal, int Long)? Range { get; init; }

    /// <summary>The weapon's mastery, if the attacker has unlocked it (null otherwise).</summary>
    public WeaponMastery? Mastery { get; init; }

    /// <summary>The ability modifier used for the attack (Graze deals it on a miss).</summary>
    public int AbilityModifier { get; init; }

    /// <summary>SRD Heavy: Disadvantage when the wielder lacks Strength 13 (melee) or Dexterity 13 (ranged).</summary>
    public bool HeavyDisadvantage { get; init; }

    /// <summary>
    /// A character's attack with a weapon (SRD): Finesse uses the better of Strength and Dexterity, a Ranged weapon
    /// Dexterity, anything else (thrown melee weapons too) Strength; the Proficiency Bonus is added if proficient; with
    /// two hands a Versatile weapon uses its larger die. The mastery counts only if unlocked. Throwing a weapon
    /// without the Thrown property makes it an improvised weapon (see <see cref="Improvised"/>).
    /// </summary>
    public static AttackProfile With(CharacterSheet sheet, Weapon weapon, bool proficient, bool twoHanded = false,
        bool thrown = false, bool masteryUnlocked = false)
    {
        if (thrown && !weapon.Has(WeaponProperty.Thrown))
            return Improvised(sheet, weapon, thrown: true); // a sword thrown is an improvised weapon (SRD)
        var strength = Abilities.Modifier(sheet.Strength);
        var dexterity = Abilities.Modifier(sheet.Dexterity);
        var modifier = weapon.Has(WeaponProperty.Finesse) ? Math.Max(strength, dexterity)
            : weapon.Ranged ? dexterity
            : strength;
        var melee = !weapon.Ranged && !thrown;
        var heavy = weapon.Has(WeaponProperty.Heavy)
                    && (weapon.Ranged ? sheet.Dexterity < 13 : sheet.Strength < 13);
        return new AttackProfile
        {
            Name = thrown ? $"{weapon.Name} (lanciato)" : weapon.Name,
            AttackBonus = modifier + (proficient ? sheet.ProficiencyBonus : 0),
            Damage = twoHanded && weapon.TwoHandedDamage is { } big ? big : weapon.Damage,
            DamageBonus = modifier,
            Type = weapon.Type,
            Melee = melee,
            ReachFeet = weapon.Has(WeaponProperty.Reach) ? 10 : 5,
            Range = melee ? null : weapon.Range,
            Mastery = masteryUnlocked ? weapon.Mastery : null,
            AbilityModifier = modifier,
            HeavyDisadvantage = heavy,
        };
    }

    /// <summary>
    /// SRD Improvised Weapons: a weapon used contrary to its design (a Melee weapon without Thrown thrown, a Ranged
    /// weapon swung in melee) counts as improvised: no Proficiency Bonus, 1d4 damage, thrown range 20/60, no
    /// mastery. The ability modifier still applies: Strength, as for any melee or thrown melee weapon.
    /// </summary>
    public static AttackProfile Improvised(CharacterSheet sheet, Weapon weapon, bool thrown)
    {
        var strength = Abilities.Modifier(sheet.Strength);
        return new AttackProfile
        {
            Name = $"{weapon.Name} (improvvisata{(thrown ? ", lanciata" : "")})",
            AttackBonus = strength,
            Damage = new Dice(1, 4),
            DamageBonus = strength,
            Type = weapon.Ranged ? DamageType.Bludgeoning : weapon.Type, // a crossbow swung hits like a club
            Melee = !thrown,
            Range = thrown ? (20, 60) : null,
            AbilityModifier = strength,
        };
    }

    /// <summary>SRD Unarmed Strike (Damage option): Strength + Proficiency Bonus to hit, 1 + Strength Bludgeoning.</summary>
    public static AttackProfile Unarmed(CharacterSheet sheet)
    {
        var strength = Abilities.Modifier(sheet.Strength);
        return new AttackProfile
        {
            Name = "Colpo senz'armi", AttackBonus = strength + sheet.ProficiencyBonus, Damage = Dice.None,
            DamageBonus = 1 + strength, Type = DamageType.Bludgeoning, Melee = true, AbilityModifier = strength,
        };
    }
}

/// <summary>The result of one attack, ready for the dice log.</summary>
public sealed record AttackResult
{
    public required string Attack { get; init; }
    public required D20Roll Roll { get; init; }
    public required int TargetArmorClass { get; init; }
    public required bool Hit { get; init; }
    public required bool Critical { get; init; }

    /// <summary>Each damage die rolled (twice as many on a Critical Hit); empty on a miss or for flat damage.</summary>
    public required IReadOnlyList<int> DamageDice { get; init; }

    /// <summary>Damage dealt: dice plus bonus on a hit, never below 0; on a miss, Graze damage if any, else 0.</summary>
    public required int Damage { get; init; }

    /// <summary>The flat bonus added to the dice (the ability modifier, usually).</summary>
    public int DamageBonus { get; init; }

    public required DamageType Type { get; init; }

    /// <summary>A miss that still dealt damage through the Graze mastery.</summary>
    public bool Grazed { get; init; }

    /// <summary>E.g. "Spada lunga: d20 14 +4 = 18 contro CA 12, colpito: 1d8 [5] +2 = 7 taglienti".</summary>
    public string Describe()
    {
        var roll = $"{Attack}: {Roll.Describe()} contro CA {TargetArmorClass}";
        if (!Hit)
            return Grazed ? $"{roll}, mancato, ma lo sfiora: {Damage} {TypeName(Type)}" : $"{roll}, mancato";
        var dice = DamageDice.Count == 0 ? "" : $"[{string.Join(", ", DamageDice)}] ";
        var sign = DamageBonus >= 0 ? "+" : "−";
        return $"{roll}, {(Critical ? "COLPO CRITICO" : "colpito")}: {dice}{sign}{Math.Abs(DamageBonus)} = {Damage} {TypeName(Type)}";
    }

    public static string TypeName(DamageType type) => type switch
    {
        DamageType.Bludgeoning => "contundenti",
        DamageType.Piercing => "perforanti",
        _ => "taglienti",
    };
}

internal static class Attacks
{
    /// <summary>
    /// One attack roll and its damage (SRD): a natural 20 always hits and is a Critical Hit, a natural 1 always misses;
    /// otherwise the total must reach the target's AC. A Critical Hit rolls the damage dice twice (the bonus once).
    /// <paramref name="critOnHit"/>: any hit is a Critical Hit (an Unconscious target within 5 feet). Heavy Disadvantage
    /// is applied from the profile. Graze: on a miss, damage equal to the ability modifier used (if positive).
    /// </summary>
    public static AttackResult Roll(SplitMix64 rng, AttackProfile attack, int targetArmorClass,
        bool advantage = false, bool disadvantage = false, bool critOnHit = false)
    {
        var roll = D20.Roll(rng, attack.AttackBonus, advantage, disadvantage || attack.HeavyDisadvantage);
        var hit = roll.Kept == 20 || (roll.Kept != 1 && roll.Total >= targetArmorClass);
        var critical = hit && (roll.Kept == 20 || critOnHit);
        if (!hit)
        {
            var graze = attack.Mastery == WeaponMastery.Graze ? Math.Max(0, attack.AbilityModifier) : 0;
            return new AttackResult
            {
                Attack = attack.Name, Roll = roll, TargetArmorClass = targetArmorClass, Hit = false, Critical = false,
                DamageDice = Array.Empty<int>(), Damage = graze, Type = attack.Type, Grazed = graze > 0,
            };
        }

        var count = attack.Damage.Count * (critical ? 2 : 1);
        var dice = Enumerable.Range(0, count).Select(_ => rng.Roll(attack.Damage.Sides)).ToArray();
        return new AttackResult
        {
            Attack = attack.Name, Roll = roll, TargetArmorClass = targetArmorClass, Hit = true, Critical = critical,
            DamageDice = dice, DamageBonus = attack.DamageBonus, Damage = Math.Max(0, dice.Sum() + attack.DamageBonus), Type = attack.Type,
        };
    }
}
