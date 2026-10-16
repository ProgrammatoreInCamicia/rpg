using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// R3c: Channel Divinity and the Oath of Devotion (SRD 5.2.1 p. 54–56): Divine Sense, Sacred Weapon, the oath's spells.
internal sealed partial class Simulation
{
    /// <summary>Protection from Evil and Good guards against these kinds of creature (SRD).</summary>
    private static readonly IReadOnlySet<CreatureType> OtherworldlyKinds = new HashSet<CreatureType>
    {
        CreatureType.Aberration, CreatureType.Celestial, CreatureType.Elemental, CreatureType.Fey, CreatureType.Fiend, CreatureType.Undead,
    };

    private static readonly Duration TenMinutes = Duration.FromMinutes(10);

    /// <summary>The Channel Divinity uses: left and in all; null without the feature.</summary>
    internal (int Remaining, int Max)? ChannelDivinityPool(Actor actor)
    {
        if (!HasFeature(actor, ClassFeatures.ChannelDivinity) || ClassOf(actor) is not { } cls || World.LevelOf(actor) is not { } level)
            return null;
        var table = World.Content.Classes[cls].ChannelDivinity;
        var max = table.Count == 0 ? 0 : table[Math.Min(level, table.Count) - 1];
        return max == 0 ? null : (Math.Max(0, max - actor.ChannelDivinitySpent), max);
    }

    /// <summary>The subclass the character has, if any.</summary>
    private SubclassDefinition? SubclassOf(Actor actor) =>
        actor.Choices.Subclass is { } id && ClassOf(actor) is { } cls
            ? World.Content.Classes[cls].Subclasses.FirstOrDefault(s => s.Id == id)
            : null;

    /// <summary>The subclass's spells, always prepared from their level on (Oath of Devotion Spells).</summary>
    private IEnumerable<string> OathSpells(Actor actor) =>
        SubclassOf(actor) is { } sub && World.LevelOf(actor) is { } level
            ? sub.Spells.Where(l => level >= l.Level).SelectMany(l => l.Spells)
            : Enumerable.Empty<string>();

    /// <summary>
    /// SRD Divine Sense: a Bonus Action and a Channel Divinity use; for 10 minutes it knows where Celestials, Fiends and
    /// Undead within 60 feet are. ADAPTATION: it is told who they are, those within 60 feet now, as a fact.
    /// </summary>
    private CommandResult UseChannelDivinity(ChannelDivinityCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (command.Effect != "DivineSense")
            return CommandResult.Rejected(RejectionReason.UnknownCommand, "Effetto sconosciuto.");
        if (ChannelDivinityPool(actor) is not { Remaining: > 0 })
            return CommandResult.Rejected(RejectionReason.NoSlot, "Non hai usi di Incanalare divinità.");
        var e = EncounterOf(actor);
        if (e is not null)
        {
            var me = CombatantOf(e, actor.Id);
            if (!IsTurnOf(e, actor))
                return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
            if (me.BonusActionUsed || Incapacitated(actor))
                return CommandResult.Rejected(RejectionReason.NoActionLeft, "Hai già usato l'azione bonus in questo turno.");
            me.BonusActionUsed = true;
        }
        else if (actor.CurrentAction is not null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");

        actor.ChannelDivinitySpent++;
        actor.Effects.RemoveAll(f => f.Spell == "divine-sense");
        actor.Effects.Add(new SpellEffect { Spell = "divine-sense", Caster = actor.Id, Until = World.Now.Plus(TenMinutes) });
        World.Scheduler.Schedule(World.Now.Plus(TenMinutes), new ExpireEffects());
        var near = CurrentSpot(actor) is { } here
            ? World.Actors.Values.Where(o => o.Id != actor.Id && o.MapArea == actor.MapArea && o.Vitality.State != LifeState.Dead
                    && CurrentSpot(o) is { } there && here.DistanceTo(there) <= 60 * Spot.PerFoot
                    && World.Content.KindOf(o.Build).Type is CreatureType.Celestial or CreatureType.Fiend or CreatureType.Undead)
                .OrderBy(o => o.Id.Value, StringComparer.Ordinal).ToList()
            : new List<Actor>();
        var text = near.Count == 0
            ? $"{actor.Name} apre i sensi al divino: nessun celestiale, immondo o non morto nei dintorni."
            : $"{actor.Name} apre i sensi al divino: {string.Join(", ", near.Select(o => $"{o.Name} ({World.Content.KindOf(o.Build).Name})"))}.";
        if (e is not null)
            Record(e, CombatLogKind.Spell, actor, null, (_, _) => text);
        World.RecordFact("DivineSense", text, actor.Id);
        return new CommandResult { Message = text };
    }

    /// <summary>
    /// R3c, Sacred Weapon: why it cannot be used with this attack (null if it can): the Oath of Devotion, a use left, a melee
    /// attack with a weapon held, in one's turn.
    /// </summary>
    private string? SacredWeaponRefused(Encounter? e, Actor attacker, AttackProfile attack)
    {
        if (SubclassOf(attacker)?.Id != "devotion")
            return "Arma sacra è del Giuramento di devozione.";
        if (ChannelDivinityPool(attacker) is not { Remaining: > 0 })
            return "Non hai usi di Incanalare divinità.";
        if (!attack.Melee || attack.Requires is null)
            return "Arma sacra vale per un'arma da mischia impugnata.";
        return e is null ? "Arma sacra con l'azione Attacco, durante uno scontro." : null;
    }

    /// <summary>SRD Sacred Weapon: for 10 minutes, the Charisma modifier (at least +1) on attack rolls with that weapon; it sheds light.</summary>
    private void TakeUpSacredWeapon(Encounter e, Actor attacker, AttackProfile attack)
    {
        attacker.ChannelDivinitySpent++;
        LightChange(attacker, () =>
        {
            attacker.Effects.RemoveAll(f => f.Spell == "sacred-weapon");
            attacker.Effects.Add(new SpellEffect { Spell = "sacred-weapon", Caster = attacker.Id, Until = World.Now.Plus(TenMinutes), Item = attack.Requires });
        });
        World.Scheduler.Schedule(World.Now.Plus(TenMinutes), new ExpireEffects());
        Record(e, CombatLogKind.Spell, attacker, null, (a, _) => $"{a} consacra la sua arma: risplende di luce sacra.");
        World.RecordFact("SacredWeapon", $"{attacker.Name} consacra la sua arma: risplende di luce sacra.", attacker.Id);
    }

    /// <summary>Sacred Weapon's bonus to an attack with its weapon.</summary>
    private int SacredWeaponBonus(Actor attacker, AttackProfile attack) =>
        attack.Requires is { } weapon && attacker.Effects.Any(f => f.Spell == "sacred-weapon" && f.Item == weapon
            && (f.Until is not { } until || until > World.Now))
            ? Math.Max(1, Abilities.Modifier(attacker.Sheet.Charisma))
            : 0;

    /// <summary>Protection from Evil and Good: an attacker of those kinds against the protected one has Disadvantage.</summary>
    private bool ProtectedFrom(Actor target, Actor? source) =>
        source is not null && HasEffect(target, "protection-from-evil-and-good") && OtherworldlyKinds.Contains(World.Content.KindOf(source.Build).Type);
}
