using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// R4c: the Help, Hide, Search and Ready actions in a fight (SRD 5.2.1 Rules Glossary).
internal sealed partial class Simulation
{
    /// <summary>SRD Hide: the Dexterity (Stealth) DC.</summary>
    private const int HideDc = 15;

    /// <summary>
    /// Hidden (SRD Hide): the Invisible condition it gave itself, while its Stealth total stands (<see cref="Actor.Sneak"/>,
    /// which an attack, a spell or a walk in the open clear).
    /// </summary>
    internal static bool Hidden(Actor actor) =>
        actor.Sneak is not null && actor.Conditions.Any(c => c.Kind == ConditionKind.Invisible && c.Source == actor.Id);

    /// <summary>The new actions of R4c: what they do and whether they could be done now, past the common checks.</summary>
    private CommandResult TacticalAction(Encounter e, Actor actor, CombatActionCommand command)
    {
        var me = CombatantOf(e, actor.Id);
        switch (command.Action)
        {
            case CombatAction.Help:
            {
                if (command.Target is not { } id || !World.Actors.TryGetValue(id, out var enemy) || Allies(e, actor, enemy) || EncounterOf(enemy) != e)
                    return CommandResult.Rejected(RejectionReason.ActorNotFound, "Indica il nemico da distrarre.");
                if (!enemy.Conscious || !WithinFiveFeet(World.Maps[e.Area], CurrentSpot(actor)!.Value, CurrentSpot(enemy)!.Value))
                    return CommandResult.Rejected(RejectionReason.OutOfReach, $"{enemy.Name} dev'essere entro 5 piedi.");
                me.ActionUsed = true;
                enemy.DistractedBy = actor.Id;
                var text = $"{actor.Name} distrae {enemy.Name}: il prossimo attacco di un alleato contro di lui avrà vantaggio";
                Record(e, CombatLogKind.Dodge, actor, enemy, (a, t) => $"{a} distrae {t}.");
                return new CommandResult { Message = $"{text}." };
            }
            case CombatAction.Hide:
            {
                // SRD Hide: Heavily Obscured or behind Three-Quarters Cover, and out of every enemy's line of sight.
                var watched = EnemiesOf(e, actor).Where(o => o.Conscious && SeesActor(o, actor)).Select(o => o.Name).ToList();
                if (watched.Count > 0)
                    return CommandResult.Rejected(RejectionReason.NotHeld, $"Ti vedono: {string.Join(", ", watched)}.");
                me.ActionUsed = true;
                var (modifier, _, disadvantage) = CheckEdges(actor);
                var roll = D20.Roll(World.Rng, actor.Sheet.Bonus(Skill.Stealth) + modifier, disadvantage: disadvantage || actor.Sheet.StealthDisadvantage);
                var hidden = roll.Total >= HideDc;
                if (hidden)
                {
                    actor.Sneak = roll.Total;
                    AddCondition(e, actor, ConditionKind.Invisible, actor);
                }
                var text = $"{actor.Name} cerca di nascondersi: Furtività {roll.Describe()}, {(hidden ? "è nascosto" : "non ci riesce")}";
                Record(e, CombatLogKind.Dodge, actor, null, (a, _) => hidden ? $"{a} sparisce alla vista." : $"{a} cerca di nascondersi, ma non ci riesce.",
                    playerText: (a, _) => actor.IsPlayer ? $"{text}." : $"{a} cerca di nascondersi.");
                return new CommandResult { Message = $"{text}." };
            }
            case CombatAction.Search:
            {
                me.ActionUsed = true;
                var (modifier, _, disadvantage) = CheckEdges(actor);
                var roll = D20.Roll(World.Rng, actor.Sheet.Bonus(Skill.Perception) + modifier, disadvantage: disadvantage);
                var found = EnemiesOf(e, actor).Where(o => Hidden(o) && roll.Total >= o.Sneak && CouldSee(actor, o)).ToList();
                foreach (var o in found)
                    Reveal(e, o, $"{actor.Name} lo scova");
                var text = $"{actor.Name} cerca: Saggezza (Percezione) {roll.Describe()}"
                           + (found.Count == 0 ? ", non trova nessuno" : $", trova {string.Join(", ", found.Select(o => o.Name))}");
                Record(e, CombatLogKind.Dodge, actor, null, (_, _) => $"{text}.");
                return new CommandResult { Message = $"{text}." };
            }
            default: // Ready
                if (MeleeAttack(actor) is null)
                    return CommandResult.Rejected(RejectionReason.UnknownAttack, "Non hai un attacco in mischia da preparare.");
                me.ActionUsed = true;
                me.Readied = true;
                Record(e, CombatLogKind.Dodge, actor, null, (a, _) => $"{a} si prepara a colpire il primo nemico che gli arriva a portata.");
                return new CommandResult { Message = $"{actor.Name} si prepara a colpire il primo nemico che gli arriva a portata." };
        }
    }

    /// <summary>Whether it would see the other one, hiding aside (line of sight and light).</summary>
    private bool CouldSee(Actor watcher, Actor target)
    {
        if (watcher.MapArea is not { } area || target.MapArea != area || CurrentPosition(watcher) is not { } eye || CurrentPosition(target) is not { } there)
            return false;
        var map = World.Maps[area];
        return watcher.Conscious && map.HasLineOfSight(eye, there) && LightOn(map, there) >= Light.Dim;
    }

    /// <summary>One who was hidden is found, or gives itself away.</summary>
    private void Reveal(Encounter? e, Actor hidden, string how)
    {
        hidden.Sneak = null;
        RemoveCondition(e, hidden, ConditionKind.Invisible, $"non è più nascosto: {how}");
    }

    /// <summary>
    /// R4c, Ready (ADAPTATION: only "a melee attack against the first enemy that comes within reach"): the enemies that
    /// readied it and that this step brings the mover into the reach of, with their Reaction to spend.
    /// </summary>
    private IEnumerable<Actor> ReadiedAgainst(Encounter e, Actor mover, (GridPos Cell, Spot At) from, (GridPos Cell, Spot At) to)
    {
        var map = World.Maps[e.Area];
        foreach (var c in e.Order.Where(c => c.Readied && !c.ReactionUsed))
        {
            var foe = World.Actors[c.Actor];
            if (foe.Id == mover.Id || Allies(e, foe, mover) || !foe.Conscious || Incapacitated(foe) || MeleeAttack(foe) is not { } attack
                || CurrentPosition(foe) is null)
                continue;
            var at = StandingOf(foe);
            if (!InReach(map, at, attack, from) && InReach(map, at, attack, to) && Sees(map, foe, at.Cell, mover, to.Cell, LightOn(map, to.Cell), atSpot: to.At))
                yield return foe;
        }
    }

    /// <summary>The readied blows land as the mover steps into reach (it is placed there first). Returns whether it is still up.</summary>
    private bool ReadiedStrikes(Encounter e, Actor mover, (GridPos Cell, Spot At) from, (GridPos Cell, Spot At) to)
    {
        var strikers = ReadiedAgainst(e, mover, from, to).ToList();
        if (strikers.Count == 0)
            return true;
        Place(mover, to.Cell, to.At);
        foreach (var foe in strikers)
        {
            var c = CombatantOf(e, foe.Id);
            c.ReactionUsed = true;
            c.Readied = false;
            ResolveAttack(e, foe, mover, MeleeAttack(foe)!, nonLethal: false, CombatLogKind.Attack);
            if (!mover.Conscious)
                return false;
        }
        return true;
    }
}
