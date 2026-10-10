namespace RpgSandbox.Sim.Rules;

// What a creature brings to a fight (SRD 5.2.1: Level 1 Hit Points by Class, Paladin features, the Bandit, Guard and
// Commoner stat blocks) and the Initiative order.

public sealed record CombatProfile
{
    public required string Name { get; init; }
    public required int MaxHitPoints { get; init; }
    public required int ArmorClass { get; init; }
    public required int InitiativeBonus { get; init; }

    /// <summary>Feet per round.</summary>
    public int Speed { get; init; } = 30;

    public required IReadOnlyList<AttackProfile> Attacks { get; init; }

    /// <summary>Characters make Death Saving Throws; other creatures die at 0 Hit Points (SRD "Monster Death").</summary>
    public required bool MakesDeathSaves { get; init; }

    public Vitality NewVitality() => new(MaxHitPoints, MakesDeathSaves);
}

/// <summary>Ready-made fighters: the protagonist from its sheet, the others copied from the SRD stat blocks.</summary>
public static class CombatProfiles
{
    /// <summary>
    /// The level 1 Paladin of <see cref="Sheets.Paladin"/>: Hit Points 10 + Constitution modifier = 11 (SRD Level 1
    /// Hit Points by Class), AC 18 (Chain Mail and Shield), Initiative = Dexterity modifier. Starting equipment A:
    /// Longsword one-handed (a Shield in the other hand) and Javelins. Weapon Mastery (two kinds): Longsword (Sap) and
    /// Javelin (Slow).
    /// </summary>
    public static CombatProfile Paladin()
    {
        var sheet = Sheets.Paladin();
        return new CombatProfile
        {
            Name = "Paladino",
            MaxHitPoints = 10 + Abilities.Modifier(sheet.Constitution),
            ArmorClass = sheet.ArmorClass,
            InitiativeBonus = Abilities.Modifier(sheet.Dexterity),
            Speed = sheet.Speed,
            Attacks = new[]
            {
                AttackProfile.With(sheet, Weapons.Longsword, proficient: true, masteryUnlocked: true),
                AttackProfile.With(sheet, Weapons.Javelin, proficient: true, thrown: true, masteryUnlocked: true),
                AttackProfile.Unarmed(sheet),
            },
            MakesDeathSaves = true,
        };
    }

    /// <summary>SRD Bandit: AC 12, HP 11, Initiative +1; Scimitar +3 (1d6 + 1), Light Crossbow +3 (1d8 + 1, 80/320).</summary>
    public static CombatProfile Bandit() => new()
    {
        Name = "Bandito", MaxHitPoints = 11, ArmorClass = 12, InitiativeBonus = 1, MakesDeathSaves = false,
        Attacks = new[]
        {
            StatBlock("Scimitarra", 3, new Dice(1, 6), 1, DamageType.Slashing),
            StatBlock("Balestra leggera", 3, new Dice(1, 8), 1, DamageType.Piercing, range: (80, 320)),
        },
    };

    /// <summary>SRD Guard: AC 16, HP 11, Initiative +1; Spear +3 (1d6 + 1), in melee or thrown (20/60).</summary>
    public static CombatProfile Guard() => new()
    {
        Name = "Guardia", MaxHitPoints = 11, ArmorClass = 16, InitiativeBonus = 1, MakesDeathSaves = false,
        Attacks = new[]
        {
            StatBlock("Lancia", 3, new Dice(1, 6), 1, DamageType.Piercing),
            StatBlock("Lancia (lanciata)", 3, new Dice(1, 6), 1, DamageType.Piercing, range: (20, 60)),
        },
    };

    /// <summary>SRD Commoner: AC 10, HP 4, Initiative +0; Club +2 (1d4).</summary>
    public static CombatProfile Commoner() => new()
    {
        Name = "Popolano", MaxHitPoints = 4, ArmorClass = 10, InitiativeBonus = 0, MakesDeathSaves = false,
        Attacks = new[] { StatBlock("Bastone", 2, new Dice(1, 4), 0, DamageType.Bludgeoning) },
    };

    /// <summary>An attack as a stat block prints it. Monsters use no weapon masteries.</summary>
    private static AttackProfile StatBlock(string name, int toHit, Dice damage, int bonus, DamageType type,
        (int Normal, int Long)? range = null) => new()
    {
        Name = name, AttackBonus = toHit, Damage = damage, DamageBonus = bonus, Type = type,
        Melee = range is null, Range = range, AbilityModifier = bonus,
    };
}

/// <summary>One combatant's place in the Initiative order.</summary>
public sealed record InitiativeEntry(string Id, D20Roll Roll, int Bonus);

internal static class Initiative
{
    /// <summary>SRD Initiative: a Dexterity check, with Disadvantage for a combatant surprised by the fight starting.</summary>
    public static InitiativeEntry Roll(SplitMix64 rng, string id, int bonus, bool surprised = false) =>
        new(id, D20.Roll(rng, bonus, disadvantage: surprised), bonus);

    /// <summary>
    /// Highest total first. ADAPTATION for ties (the SRD leaves them to the GM and the players): higher bonus first,
    /// then the id in ordinal order, so the order never depends on how the list was built.
    /// </summary>
    public static IReadOnlyList<InitiativeEntry> Order(IEnumerable<InitiativeEntry> entries) =>
        entries.OrderByDescending(e => e.Roll.Total)
            .ThenByDescending(e => e.Bonus)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
}
