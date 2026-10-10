using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

/// <summary>Brightness levels, ordered: a higher value lets one see more.</summary>
public enum Light { Dark = 0, Dim = 1, Bright = 2 }

/// <summary>How a witness became aware of something.</summary>
public enum PerceptionMode { Seen, Heard }

/// <summary>
/// How deeds are noticed. Built on SRD 5.2.1 (Hide needs Heavy Obscurement or cover; Dim Light gives Disadvantage
/// on sight-based Perception, i.e. −5 to Passive Perception; Darkness Blinds sight but not hearing) with game
/// ADAPTATIONS: light follows the time of day and the place; an action lasting minutes uses one Stealth check made
/// when it starts, and a witness judges it by the best light during the part they were present for.
/// Noticing an event, seeing who did it and recognising them are separate outcomes.
/// </summary>
internal static class Perception
{
    /// <summary>ADAPTATION: outdoor light by time of day.</summary>
    public static Light DaylightAt(GameTime t)
    {
        var hour = t.Seconds % 86_400 / 3600;
        return hour switch
        {
            >= 7 and < 19 => Light.Bright,
            6 or 19 => Light.Dim,
            _ => Light.Dark,
        };
    }

    /// <summary>ADAPTATION: indoor places with lamps (e.g. the inn) are never darker than dim light.</summary>
    public static Light LightAt(Location place, GameTime t)
    {
        var daylight = DaylightAt(t);
        return place.Lit && daylight < Light.Dim ? Light.Dim : daylight;
    }

    /// <summary>The best light at <paramref name="place"/> at any moment between <paramref name="from"/> and <paramref name="to"/>.</summary>
    public static Light BestLightDuring(Location place, GameTime from, GameTime to)
    {
        var best = LightAt(place, from);
        // Light only changes on the hour: check every hour boundary crossed, then the end.
        for (var t = (from.Seconds / 3600 + 1) * 3600; t <= to.Seconds && best < Light.Bright; t += 3600)
            best = (Light)Math.Max((int)best, (int)LightAt(place, new GameTime(t)));
        return (Light)Math.Max((int)best, (int)LightAt(place, to));
    }

    /// <summary>The best daylight between two instants (it only changes on the hour).</summary>
    public static Light BestDaylightDuring(GameTime from, GameTime to)
    {
        var best = DaylightAt(from);
        for (var t = (from.Seconds / 3600 + 1) * 3600; t <= to.Seconds && best < Light.Bright; t += 3600)
            best = (Light)Math.Max((int)best, (int)DaylightAt(new GameTime(t)));
        return (Light)Math.Max((int)best, (int)DaylightAt(to));
    }

    public static string Describe(Light light) => light switch
    {
        Light.Bright => "luce piena",
        Light.Dim => "luce fioca",
        _ => "buio",
    };

    /// <summary>What a witness makes of a deed done near them.</summary>
    internal readonly record struct Outcome(bool Noticed, bool SawActor, int SightPerception, int HearingPerception)
    {
        public PerceptionMode Mode => SawActor ? PerceptionMode.Seen : PerceptionMode.Heard;
    }

    /// <summary>
    /// Sight and hearing are judged separately (ADAPTATION of Hide/Passive Perception):
    /// sight — in bright light someone watching sees the deed; in dim light, Passive Perception with Disadvantage (−5)
    /// must reach the Stealth total; in the dark, nothing is seen.
    /// Hearing — Passive Perception against the Stealth total, whatever the light.
    /// Noticing takes either sense; seeing who did it takes sight.
    /// </summary>
    public static Outcome Witness(CharacterSheet witness, Light light, int stealthTotal)
    {
        var sightScore = witness.PassivePerception(disadvantage: light == Light.Dim);
        var hearingScore = witness.PassivePerception();
        var seen = light switch
        {
            Light.Bright => true,
            Light.Dim => sightScore >= stealthTotal,
            _ => false,
        };
        var heard = hearingScore >= stealthTotal;
        return new Outcome(seen || heard, seen, sightScore, hearingScore);
    }
}
