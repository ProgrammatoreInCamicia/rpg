namespace RpgSandbox.Sim.Rules;

/// <summary>Ready-made character sheets used by the scenarios.</summary>
public static class Sheets
{
    /// <summary>
    /// Level 1 Paladin built with the SRD 5.2.1 rules: Standard Array as suggested for the Paladin
    /// (Str 15, Dex 10, Con 13, Int 8, Wis 12, Cha 14), Acolyte background (+2 Cha, +1 Wis; Insight, Religion),
    /// class skills Athletics and Persuasion, starting equipment A (Chain Mail: AC 16 and Stealth Disadvantage;
    /// Shield: +2 AC). Origin feat, spells and class features are not modelled yet.
    /// </summary>
    public static CharacterSheet Paladin() => new()
    {
        Title = "Paladino 1 (Accolito)",
        Strength = 15, Dexterity = 10, Constitution = 13, Intelligence = 8, Wisdom = 13, Charisma = 16,
        ProficiencyBonus = 2,
        SkillProficiencies = new[] { Skill.Athletics, Skill.Insight, Skill.Persuasion, Skill.Religion },
        ArmorClass = 18,
        StealthDisadvantage = true,
    };

    // Original sheets for the slice's people (not SRD creatures): only the numbers the rules use.

    /// <summary>Light-footed bandit: Dexterity (Stealth) +4.</summary>
    public static CharacterSheet Raider() => new()
    {
        Title = "Razziatore",
        Strength = 11, Dexterity = 15, Constitution = 12, Intelligence = 10, Wisdom = 10, Charisma = 9,
        ProficiencyBonus = 2, SkillProficiencies = new[] { Skill.Stealth, Skill.SleightOfHand }, ArmorClass = 13,
    };

    /// <summary>Watchful village guard: Passive Perception 13.</summary>
    public static CharacterSheet VillageGuard() => new()
    {
        Title = "Guardia del villaggio",
        Strength = 13, Dexterity = 12, Constitution = 12, Intelligence = 10, Wisdom = 13, Charisma = 10,
        ProficiencyBonus = 2, SkillProficiencies = new[] { Skill.Perception, Skill.Intimidation }, ArmorClass = 15,
    };

    /// <summary>Farmer: no training, decent eyes. Passive Perception 11.</summary>
    public static CharacterSheet Farmer() => new()
    {
        Title = "Contadino",
        Strength = 13, Dexterity = 10, Constitution = 13, Intelligence = 9, Wisdom = 12, Charisma = 10,
        ProficiencyBonus = 2, SkillProficiencies = Array.Empty<Skill>(), ArmorClass = 10,
    };
}
