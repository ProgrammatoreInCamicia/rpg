namespace RpgSandbox.Sim.Rules;

/// <summary>
/// Shortcuts to the default pack's character sheets (D1a: derived from the builds in game/content/), for tests, tools
/// and scenarios that want a sheet of their own.
/// </summary>
public static class Sheets
{
    /// <summary>The pregenerated level 1 Paladin (Acolyte) in Chain Mail and Shield: AC 18, Stealth Disadvantage.</summary>
    public static CharacterSheet Paladin() => Of("paladin");

    /// <summary>The test bench's raider: light-footed, Dexterity (Stealth) +4.</summary>
    public static CharacterSheet Raider() => Of("raider");

    /// <summary>The test bench's village guard: watchful, Passive Perception 13.</summary>
    public static CharacterSheet VillageGuard() => Of("village-guard");

    /// <summary>The test bench's farmer: no training, decent eyes. Passive Perception 11.</summary>
    public static CharacterSheet Farmer() => Of("farmer");

    private static CharacterSheet Of(string build) =>
        Builds.Sheet(ContentPack.Default, build, Builds.InitialEquipment(ContentPack.Default, build));
}
