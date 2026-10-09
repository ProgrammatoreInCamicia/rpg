using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

public enum Light { Bright, Dim, Dark }

/// <summary>
/// How deeds are noticed. Built on SRD 5.2.1 (Hide needs Heavy Obscurement or cover; Dim Light gives Disadvantage
/// on sight-based Perception, i.e. −5 to Passive Perception; Darkness Blinds sight) with game ADAPTATIONS:
/// light follows the time of day, and an action lasting minutes uses one Stealth check made when it starts.
/// Noticing an event, seeing who did it and recognising them are separate outcomes.
/// </summary>
internal static class Perception
{
    /// <summary>ADAPTATION: outdoor light by time of day, the same everywhere for now.</summary>
    public static Light LightAt(GameTime t)
    {
        var hour = t.Seconds % 86_400 / 3600;
        return hour switch
        {
            >= 7 and < 19 => Light.Bright,
            6 or 19 => Light.Dim,
            _ => Light.Dark,
        };
    }

    public static string Describe(Light light) => light switch
    {
        Light.Bright => "luce piena",
        Light.Dim => "luce fioca",
        _ => "buio",
    };

    /// <summary>What a witness makes of a deed done in front of them.</summary>
    internal readonly record struct Outcome(bool Noticed, bool SawActor, int? PassivePerception);

    /// <summary>
    /// ADAPTATION of Hide/Passive Perception to a deed lasting minutes:
    /// in bright light nobody hides from someone watching, the deed is seen;
    /// in dim light the witness notices it if Passive Perception with Disadvantage (−5) reaches the Stealth total;
    /// in the dark the witness can only hear it: noticed on Passive Perception, but the doer is never seen.
    /// </summary>
    public static Outcome Witness(CharacterSheet witness, Light light, int stealthTotal) => light switch
    {
        Light.Bright => new Outcome(true, true, null),
        Light.Dim => witness.PassivePerception(disadvantage: true) is var dim
            ? new Outcome(dim >= stealthTotal, dim >= stealthTotal, dim)
            : default,
        _ => witness.PassivePerception() is var heard
            ? new Outcome(heard >= stealthTotal, false, heard)
            : default,
    };
}
