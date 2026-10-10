namespace RpgSandbox.Sim.Rules;

// Game rules from the System Reference Document 5.2.1 (CC-BY-4.0, see CREDITS.md): abilities, modifiers,
// proficiency, skills, D20 Tests with Advantage/Disadvantage and Passive Perception.
// Only what the simulation uses is implemented. Adaptations are marked ADAPTATION.

public enum Ability { Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma }

public enum Skill
{
    Athletics, Insight, Intimidation, Medicine, Perception, Persuasion, Religion, SleightOfHand, Stealth,
}

public static class Abilities
{
    /// <summary>SRD: score 10–11 is +0, every two points above or below adds or removes 1.</summary>
    public static int Modifier(int score) => (int)Math.Floor((score - 10) / 2.0);

    /// <summary>The ability each skill uses (SRD "Skills" table).</summary>
    public static Ability Of(Skill skill) => skill switch
    {
        Skill.Athletics => Ability.Strength,
        Skill.SleightOfHand or Skill.Stealth => Ability.Dexterity,
        Skill.Insight or Skill.Medicine or Skill.Perception => Ability.Wisdom,
        Skill.Intimidation or Skill.Persuasion => Ability.Charisma,
        Skill.Religion => Ability.Intelligence,
        _ => throw new ArgumentOutOfRangeException(nameof(skill)),
    };
}

/// <summary>
/// What the rules need to know about a creature. Immutable: a sheet changes only by replacing it.
/// </summary>
public sealed record CharacterSheet
{
    /// <summary>Short description, e.g. "Paladino 1 (Accolito)". Shown to the player, never used by rules.</summary>
    public required string Title { get; init; }

    public required int Strength { get; init; }
    public required int Dexterity { get; init; }
    public required int Constitution { get; init; }
    public required int Intelligence { get; init; }
    public required int Wisdom { get; init; }
    public required int Charisma { get; init; }

    /// <summary>+2 at levels 1–4 (SRD Character Advancement table).</summary>
    public required int ProficiencyBonus { get; init; }

    /// <summary>Stored as a private read-only copy: a sheet handed out in a view can never alter the world.</summary>
    public required IReadOnlyList<Skill> SkillProficiencies
    {
        get => _skills;
        init => _skills = Array.AsReadOnly(value.Distinct().OrderBy(s => s).ToArray());
    }

    private readonly IReadOnlyList<Skill> _skills = Array.Empty<Skill>();

    public required int ArmorClass { get; init; }

    /// <summary>Speed in feet per round of 6 seconds (30 for the slice's people). On a grid: Speed/5 squares per round.</summary>
    public int Speed { get; init; } = 30;

    /// <summary>Worn armor imposes Disadvantage on Dexterity (Stealth) checks (e.g. Chain Mail).</summary>
    public bool StealthDisadvantage { get; init; }

    public int Score(Ability ability) => ability switch
    {
        Ability.Strength => Strength,
        Ability.Dexterity => Dexterity,
        Ability.Constitution => Constitution,
        Ability.Intelligence => Intelligence,
        Ability.Wisdom => Wisdom,
        Ability.Charisma => Charisma,
        _ => throw new ArgumentOutOfRangeException(nameof(ability)),
    };

    public bool IsProficient(Skill skill) => SkillProficiencies.Contains(skill);

    /// <summary>Ability modifier, plus the Proficiency Bonus if proficient.</summary>
    public int Bonus(Skill skill) =>
        Abilities.Modifier(Score(Abilities.Of(skill))) + (IsProficient(skill) ? ProficiencyBonus : 0);

    /// <summary>SRD: 10 + Wisdom (Perception) bonus; +5 with Advantage, −5 with Disadvantage (they cancel out).</summary>
    public int PassivePerception(bool advantage = false, bool disadvantage = false) =>
        10 + Bonus(Skill.Perception) + (advantage == disadvantage ? 0 : advantage ? 5 : -5);

    /// <summary>A plain sheet for people the rules do not care much about: all 10s, no proficiencies.</summary>
    public static CharacterSheet Commoner(string title = "Popolano") => new()
    {
        Title = title,
        Strength = 10, Dexterity = 10, Constitution = 10, Intelligence = 10, Wisdom = 10, Charisma = 10,
        ProficiencyBonus = 2, SkillProficiencies = Array.Empty<Skill>(), ArmorClass = 10,
    };
}

/// <summary>Outcome of one D20 Test: both dice when two were rolled, which one counted, and the total.</summary>
public sealed record D20Roll
{
    public required int First { get; init; }
    public int? Second { get; init; }
    public required int Kept { get; init; }
    public required int Bonus { get; init; }
    public required bool Advantage { get; init; }
    public required bool Disadvantage { get; init; }
    public int Total => Kept + Bonus;

    /// <summary>E.g. "d20 13 (svantaggio: 13/17) +0 = 13".</summary>
    public string Describe()
    {
        var mode = Second is { } second ? $" ({(Advantage ? "vantaggio" : "svantaggio")}: {First}/{second})" : "";
        var sign = Bonus >= 0 ? "+" : "−";
        return $"d20 {Kept}{mode} {sign}{Math.Abs(Bonus)} = {Total}";
    }
}

internal static class D20
{
    /// <summary>
    /// SRD D20 Test: roll 1d20, or two keeping the higher (Advantage) or lower (Disadvantage); both together cancel
    /// out and a single die is rolled. Success is decided by the caller: total >= target number.
    /// </summary>
    public static D20Roll Roll(SplitMix64 rng, int bonus, bool advantage = false, bool disadvantage = false)
    {
        var first = rng.Roll(20);
        if (advantage == disadvantage)
            return new D20Roll { First = first, Kept = first, Bonus = bonus, Advantage = false, Disadvantage = false };
        var second = rng.Roll(20);
        var kept = advantage ? Math.Max(first, second) : Math.Min(first, second);
        return new D20Roll { First = first, Second = second, Kept = kept, Bonus = bonus, Advantage = advantage, Disadvantage = disadvantage };
    }
}
