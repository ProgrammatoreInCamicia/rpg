namespace RpgSandbox.Sim.Rules;

/// <summary>
/// R3a: what a character chose as it grew: Ability Score Improvements (each entry is +1; the same ability twice is +2),
/// the weapons whose mastery it uses (null: those of its definition), its Fighting Style.
/// </summary>
public sealed record CharacterChoices
{
    public IReadOnlyList<Ability> AbilityIncreases { get; init; } = Array.Empty<Ability>();
    public IReadOnlyList<string>? Masteries { get; init; }
    public string? FightingStyle { get; init; }

    public static readonly CharacterChoices None = new();
}

/// <summary>R3a: the class features by level (SRD 5.2.1 Paladin Features table, p. 53), for the classes in the game.</summary>
public static class ClassFeatures
{
    public const string LayOnHands = "LayOnHands", FightingStyle = "FightingStyle", Smite = "PaladinsSmite",
        ChannelDivinity = "ChannelDivinity", Subclass = "Subclass", ExtraAttack = "ExtraAttack";

    /// <summary>The Fighting Style feats of the SRD that work in the game (Two-Weapon Fighting comes with two weapons).</summary>
    public static readonly IReadOnlyList<string> FightingStyles = new[] { "Archery", "Defense", "GreatWeaponFighting" };

    private static readonly IReadOnlyDictionary<string, int> PaladinLevels = new Dictionary<string, int>
    {
        [LayOnHands] = 1, [FightingStyle] = 2, [Smite] = 2, [ChannelDivinity] = 3, [Subclass] = 3, [ExtraAttack] = 5,
    };

    public static bool Has(string? cls, int? level, string feature) =>
        cls == "paladin" && level is { } l && PaladinLevels.TryGetValue(feature, out var at) && l >= at;

    /// <summary>SRD: the Ability Score Improvement feature at levels 4, 8, 12 and 16 (19 is an Epic Boon).</summary>
    public static int AbilityScoreImprovements(string? cls, int? level) =>
        cls == "paladin" && level is { } l ? new[] { 4, 8, 12, 16 }.Count(at => l >= at) : 0;

    /// <summary>SRD Extra Attack: two attacks with the Attack action.</summary>
    public static int AttacksPerAction(string? cls, int? level) => Has(cls, level, ExtraAttack) ? 2 : 1;
}
