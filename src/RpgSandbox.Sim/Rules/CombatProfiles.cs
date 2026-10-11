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

    /// <summary>
    /// C4c, ADAPTATION (behaviour, not an SRD rule): a creature that is no fighter keeps away from fights and runs from
    /// one when badly hurt (below half its Hit Points).
    /// </summary>
    public bool Flees { get; init; }

    /// <summary>C3b: what it carries to begin with (catalog id and how many), and its coins in copper pieces.</summary>
    public IReadOnlyList<(string Item, int Count)> Kit { get; init; } = Array.Empty<(string, int)>();
    public long Coins { get; init; }

    public Vitality NewVitality() => new(MaxHitPoints, MakesDeathSaves);
}

/// <summary>Ready-made fighters: the protagonist from its sheet, the others copied from the SRD stat blocks.</summary>
/// <summary>
/// Shortcuts to the default pack's builds with their starting equipment (D1a: the numbers live in game/content/), for
/// tests and tools. The engine reads its world's pack.
/// </summary>
public static class CombatProfiles
{
    /// <summary>Every build of the default pack: creatures and pregenerated characters.</summary>
    public static IReadOnlyList<string> Ids => ContentPack.Default.Creatures.Keys.Concat(ContentPack.Default.Characters.Keys).ToArray();

    /// <summary>The profile of that build with its starting equipment, or null if there is none.</summary>
    public static CombatProfile? ById(string id) => ContentPack.Default.IsBuild(id)
        ? Builds.Profile(ContentPack.Default, id, Builds.InitialEquipment(ContentPack.Default, id))
        : null;

    /// <summary>The pregenerated level 1 Paladin: HP 11, AC 18, Longsword +4 (1d8+2, Sap), Javelin +4 (1d6+2, Slow).</summary>
    public static CombatProfile Paladin() => ById("paladin")!;

    /// <summary>SRD Bandit: AC 12, HP 11, Initiative +1; Scimitar +3 (1d6 + 1), Light Crossbow +3 (1d8 + 1, 80/320).</summary>
    public static CombatProfile Bandit() => ById("bandit")!;

    /// <summary>SRD Guard: AC 16, HP 11, Initiative +1; Spear +3 (1d6 + 1), in melee or thrown (20/60).</summary>
    public static CombatProfile Guard() => ById("guard")!;

    /// <summary>SRD Commoner: AC 10, HP 4, Initiative +0; Club +2 (1d4).</summary>
    public static CombatProfile Commoner() => ById("commoner")!;
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
