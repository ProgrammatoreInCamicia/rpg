using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// C5b: the player grows by defeating creatures (SRD 5.2.1: Experience Points by Challenge Rating, Level Advancement).
internal sealed partial class Simulation
{
    /// <summary>
    /// A creature fell (dying, knocked out or dead) or ran out of a fight. SRD: XP is awarded for defeating a monster in
    /// combat or otherwise neutralizing it, so the player gains its XP, once, if it had fought it. ADAPTATION: there is
    /// no party yet, so the player gets it all, whoever struck the last blow; a creature that escaped counts as defeated
    /// (it does not count again if it comes back); a pregenerated character is worth nothing yet.
    /// </summary>
    private void Defeat(Actor creature)
    {
        var player = World.Actors[World.Player];
        if (creature.IsPlayer || !player.FoughtWith.Contains(creature.Id) || !player.Defeated.Add(creature.Id))
            return;
        if (World.Content.Creatures.TryGetValue(creature.Build, out var stats) && stats.Experience > 0)
            GainExperience(player, stats.Experience);
    }

    /// <summary>
    /// SRD Level Advancement: a character whose XP total reaches a threshold reaches that level at once, gaining the
    /// fixed Hit Points of its class for each level (Gaining a Level) and the Proficiency Bonus of the new level. Class
    /// features of higher levels are not in the game yet (ADAPTATION). The player is told it grew, not by how many points.
    /// </summary>
    private void GainExperience(Actor character, int amount)
    {
        var before = World.LevelOf(character);
        character.Experience += amount;
        if (before is not { } from || World.LevelOf(character) is not { } to || to == from)
            return;
        var oldMaximum = character.Profile.MaxHitPoints;
        World.Refresh(character);
        character.Vitality.RaiseMaximum(character.Profile.MaxHitPoints - oldMaximum);
        World.RecordFact("LevelUp", $"{character.Name} raggiunge il livello {to}: si sente più forte e più sicuro di sé.", character.Id);
    }
}
