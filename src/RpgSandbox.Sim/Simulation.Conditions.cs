using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// R2: saving throws, conditions and Exhaustion (SRD 5.2.1 "Saving Throws", Rules Glossary conditions, "Exhaustion").
internal sealed partial class Simulation
{
    /// <summary>The conditions one sees on a creature from outside; Poisoned, Frightened, Charmed and the rest do not show.</summary>
    internal static readonly IReadOnlySet<ConditionKind> VisibleConditions = new HashSet<ConditionKind>
    {
        ConditionKind.Prone, ConditionKind.Grappled, ConditionKind.Restrained, ConditionKind.Paralyzed, ConditionKind.Stunned,
    };

    /// <summary>Whether the creature has the condition now (one with a time limit has not run out).</summary>
    internal bool Has(Actor actor, ConditionKind kind) =>
        actor.Conditions.Any(c => c.Kind == kind && (c.Until is not { } until || until > World.Now));

    /// <summary>SRD Incapacitated, also through Paralyzed and Stunned: no actions, no Reactions.</summary>
    internal bool Incapacitated(Actor actor) =>
        Has(actor, ConditionKind.Incapacitated) || Has(actor, ConditionKind.Paralyzed) || Has(actor, ConditionKind.Stunned);

    /// <summary>
    /// The conditions in force now, as <paramref name="viewer"/> knows them: its own and those it sees on others; who caused
    /// one only if that is the viewer itself or someone it sees (review R2: the source is "if known").
    /// </summary>
    internal IEnumerable<ConditionView> ConditionsOf(Actor actor, bool visibleOnly, Actor viewer) =>
        actor.Conditions
            .Where(c => (c.Until is not { } until || until > World.Now) && (!visibleOnly || VisibleConditions.Contains(c.Kind)))
            .Select(c => new ConditionView
            {
                Kind = c.Kind, Until = c.Until,
                Source = c.Source is { } source && (source == viewer.Id || World.Actors.TryGetValue(source, out var who) && SeesActor(viewer, who)) ? source : null,
            });

    /// <summary>A condition begins (a second one of the same kind replaces the first): a log line in a fight, and a fact.</summary>
    internal void AddCondition(Encounter? e, Actor actor, ConditionKind kind, Actor? source = null, GameTime? until = null,
        ActorId? endsAtTurnOf = null)
    {
        if (kind is ConditionKind.Charmed or ConditionKind.Frightened && ProtectedFrom(actor, source))
            return; // R3c, Protection from Evil and Good
        actor.Conditions.RemoveAll(c => c.Kind == kind);
        actor.Conditions.Add(new Condition { Kind = kind, Source = source?.Id, Until = until, EndsAtTurnOf = endsAtTurnOf });
        var words = $"{actor.Name} è {ConditionName(kind)}";
        if (e is not null)
            Record(e, CombatLogKind.Condition, actor, source, (a, _) => $"{a} è {ConditionName(kind)}.");
        World.RecordFact("ConditionStarted", $"{words}.", actor.Id);
        if (kind is ConditionKind.Incapacitated or ConditionKind.Paralyzed or ConditionKind.Stunned)
            EndConcentration(actor, "è incapacitato"); // R3b
        if (kind is ConditionKind.Incapacitated or ConditionKind.Paralyzed or ConditionKind.Stunned)
            CheckGrapples(); // R4a: an incapacitated grappler lets go
    }

    /// <summary>A condition ends, if the creature has it.</summary>
    internal void RemoveCondition(Encounter? e, Actor actor, ConditionKind kind, string? how = null)
    {
        if (actor.Conditions.RemoveAll(c => c.Kind == kind) == 0)
            return;
        var words = how ?? $"non è più {ConditionName(kind)}";
        if (e is not null)
            Record(e, CombatLogKind.Condition, actor, null, (a, _) => $"{a} {words}.");
        World.RecordFact("ConditionEnded", $"{actor.Name} {words}.", actor.Id);
    }

    internal static string ConditionName(ConditionKind kind) => kind switch
    {
        ConditionKind.Blinded => "accecato",
        ConditionKind.Charmed => "affascinato",
        ConditionKind.Deafened => "assordato",
        ConditionKind.Frightened => "spaventato",
        ConditionKind.Grappled => "afferrato",
        ConditionKind.Incapacitated => "incapacitato",
        ConditionKind.Invisible => "invisibile",
        ConditionKind.Paralyzed => "paralizzato",
        ConditionKind.Poisoned => "avvelenato",
        ConditionKind.Prone => "prono",
        ConditionKind.Restrained => "trattenuto",
        _ => "stordito",
    };

    // ---------------------------------------------------------------- d20 Tests

    /// <summary>SRD Exhaustion: each level takes 2 from every d20 Test (attack rolls, ability checks, saving throws).</summary>
    internal static int ExhaustionPenalty(Actor actor) => -2 * actor.Exhaustion;

    /// <summary>
    /// The conditions that tip an ability check: Poisoned, Frightened (while its source is in sight) give Disadvantage.
    /// With the Exhaustion penalty, for the d20 roll.
    /// </summary>
    internal (int Modifier, bool Advantage, bool Disadvantage) CheckEdges(Actor actor) =>
        (ExhaustionPenalty(actor), false, Has(actor, ConditionKind.Poisoned) || FrightenedInSight(actor));

    /// <summary>Frightened, with its source in sight (SRD: only then the Disadvantage).</summary>
    private bool FrightenedInSight(Actor actor) =>
        actor.Conditions.Any(c => c.Kind == ConditionKind.Frightened && (c.Until is not { } until || until > World.Now)
            && (c.Source is not { } source || World.Actors.TryGetValue(source, out var who) && SeesActor(actor, who)));

    /// <summary>
    /// SRD Saving Throws: d20 + the ability modifier + the Proficiency Bonus if proficient, against a DC. Paralyzed,
    /// Stunned and Unconscious fail Strength and Dexterity saves; Restrained has Disadvantage on Dexterity ones;
    /// Exhaustion takes 2 a level. A line in the fight's log and a fact. Returns whether it succeeded.
    /// </summary>
    internal bool SavingThrow(Encounter? e, Actor actor, Ability ability, int dc, string against)
    {
        var name = AbilityName(ability);
        if (ability is Ability.Strength or Ability.Dexterity
            && (!actor.Conscious || Has(actor, ConditionKind.Paralyzed) || Has(actor, ConditionKind.Stunned)))
        {
            if (e is not null)
                Record(e, CombatLogKind.Save, actor, null, (a, _) => $"{a}, tiro salvezza su {name} contro {against}: fallito senza tiro.");
            World.RecordFact("SavingThrow", $"{actor.Name}, tiro salvezza su {name} contro {against}: fallito senza tiro.", actor.Id);
            return false;
        }
        var disadvantage = ability == Ability.Dexterity && Has(actor, ConditionKind.Restrained);
        var roll = D20.Roll(World.Rng, actor.Sheet.SaveBonus(ability) + ExhaustionPenalty(actor) + BlessRoll(actor), disadvantage: disadvantage); // R3b: Bless
        var success = roll.Total >= dc;
        var text = $"tiro salvezza su {name} contro {against}: {roll.Describe()}, {(success ? "riuscito" : "fallito")}";
        if (e is not null)
            Record(e, CombatLogKind.Save, actor, null, (a, _) => $"{a}, {text}.", roll);
        World.RecordFact("SavingThrow", $"{actor.Name}, {text}.", actor.Id);
        return success;
    }

    private static string AbilityName(Ability ability) => ability switch
    {
        Ability.Strength => "Forza",
        Ability.Dexterity => "Destrezza",
        Ability.Constitution => "Costituzione",
        Ability.Intelligence => "Intelligenza",
        Ability.Wisdom => "Saggezza",
        _ => "Carisma",
    };

    /// <summary>
    /// The conditions and masteries that tip an attack roll, with their reasons (SRD Rules Glossary):
    /// <list type="bullet">
    /// <item>the attacker Blinded, Poisoned, Prone, Restrained, Frightened by a source in sight, Grappled against
    /// someone else than its grappler, or Sapped: Disadvantage; Invisible: Advantage;</item>
    /// <item>the target Blinded, Paralyzed, Restrained or Stunned: Advantage; Prone: Advantage within 5 feet,
    /// Disadvantage beyond; Invisible: Disadvantage; Vexed by the attacker: Advantage.</item>
    /// </list>
    /// </summary>
    internal (bool Advantage, bool Disadvantage, List<string> Reasons) ConditionEdges(Actor attacker, Actor target, bool withinFiveFeet)
    {
        var reasons = new List<string>();
        bool advantage = false, disadvantage = false;
        void Dis(bool when, string why)
        {
            if (!when) return;
            disadvantage = true;
            reasons.Add(why);
        }
        void Adv(bool when, string why)
        {
            if (!when) return;
            advantage = true;
            reasons.Add(why);
        }
        Dis(Has(attacker, ConditionKind.Blinded), "sei accecato");
        Dis(Has(attacker, ConditionKind.Poisoned), "sei avvelenato");
        Dis(Has(attacker, ConditionKind.Prone), "sei prono");
        Dis(Has(attacker, ConditionKind.Restrained), "sei trattenuto");
        Dis(FrightenedInSight(attacker), "hai paura");
        Dis(attacker.Conditions.Any(c => c.Kind == ConditionKind.Grappled && c.Source is { } by && by != target.Id), "sei afferrato da un altro");
        Dis(attacker.SappedBy is not null, "è ancora intontito dal colpo");
        Adv(Has(attacker, ConditionKind.Invisible), "non ti vede");
        Adv(Has(target, ConditionKind.Blinded), "è accecato");
        Adv(Has(target, ConditionKind.Paralyzed), "è paralizzato");
        Adv(Has(target, ConditionKind.Restrained), "è trattenuto");
        Adv(Has(target, ConditionKind.Stunned), "è stordito");
        Adv(Has(target, ConditionKind.Prone) && withinFiveFeet, "è a terra, vicino");
        Dis(Has(target, ConditionKind.Prone) && !withinFiveFeet, "è a terra, lontano");
        Dis(Has(target, ConditionKind.Invisible), "è invisibile");
        Adv(attacker.Vexing == target.Id, "lo hai messo in difficoltà");
        Dis(ProtectedFrom(target, attacker), "è protetto dal male"); // R3c
        return (advantage, disadvantage, reasons);
    }

    // ---------------------------------------------------------------- movement

    /// <summary>
    /// R2: the Speed a creature has now: none if Grappled, Restrained, Paralyzed or Stunned (ADAPTATION for Stunned:
    /// it cannot move); 5 feet less a level of Exhaustion; 10 less while Slowed. Never below 0.
    /// </summary>
    internal int SpeedNow(Actor actor, int speed)
    {
        if (Has(actor, ConditionKind.Grappled) || Has(actor, ConditionKind.Restrained)
            || Has(actor, ConditionKind.Paralyzed) || Has(actor, ConditionKind.Stunned))
            return 0;
        return Math.Max(0, speed - 5 * actor.Exhaustion - (actor.SlowedBy is not null ? 10 : 0));
    }

    /// <summary>R2: Initiative is a Dexterity check: Exhaustion, Poisoned and Frightened count; so does being surprised.</summary>
    private InitiativeEntry InitiativeRoll(Actor actor, bool surprised)
    {
        var (modifier, _, disadvantage) = CheckEdges(actor);
        return Initiative.Roll(World.Rng, actor.Id.Value, ProfileOf(actor).InitiativeBonus + modifier,
            surprised: surprised || disadvantage || Incapacitated(actor));
    }

    /// <summary>
    /// R2, SRD Frightened: it cannot willingly move closer to the source of its fear (while it sees it). ADAPTATION: a
    /// move is judged by where it ends. The source it would come closer to, or null.
    /// </summary>
    internal Actor? FearAhead(Actor actor, Spot to)
    {
        if (CurrentSpot(actor) is not { } here)
            return null;
        foreach (var c in actor.Conditions.Where(c => c.Kind == ConditionKind.Frightened && (c.Until is not { } u || u > World.Now)))
            if (c.Source is { } id && World.Actors.TryGetValue(id, out var source) && SeesActor(actor, source)
                && CurrentSpot(source) is { } there && to.DistanceTo(there) < here.DistanceTo(there))
                return source;
        return null;
    }

    /// <summary>R2: the Speed of a walk outside a fight.</summary>
    internal int WalkingSpeed(Actor actor) => SpeedNow(actor, actor.Sheet.Speed);

    // ---------------------------------------------------------------- Exhaustion

    /// <summary>SRD Exhaustion: levels add up to 6, where the creature dies; a Long Rest takes one away.</summary>
    internal void AddExhaustion(Actor actor, int levels, string why)
    {
        var before = actor.Exhaustion;
        actor.Exhaustion = Math.Clamp(before + levels, 0, 6);
        if (actor.Exhaustion == before)
            return;
        World.RecordFact("Exhaustion", actor.Exhaustion > before
            ? $"{actor.Name} è sfinito ({why}): livello {actor.Exhaustion}."
            : $"{actor.Name} è meno sfinito ({why}): livello {actor.Exhaustion}.", actor.Id);
        if (actor.Exhaustion == 6 && actor.Vitality.State != LifeState.Dead)
        {
            actor.Vitality.Perish();
            Fell(EncounterOf(actor), actor, null, DamageOutcome.Killed);
        }
    }

    // ---------------------------------------------------------------- turns

    /// <summary>
    /// R2, at the start of <paramref name="actor"/>'s turn: what lasts "until the start of its next turn" ends (Sap, Slow,
    /// conditions ending then).
    /// </summary>
    private void ConditionsAtTurnStart(Encounter e, Actor actor)
    {
        CheckGrapples(); // R4a
        foreach (var other in World.Actors.Values.OrderBy(a => a.Id.Value, StringComparer.Ordinal))
        {
            if (other.SappedBy == actor.Id)
                other.SappedBy = null;
            if (other.SlowedBy == actor.Id)
                other.SlowedBy = null;
            foreach (var ending in other.Conditions.Where(c => c.EndsAtTurnOf == actor.Id).Select(c => c.Kind).ToList())
                RemoveCondition(e, other, ending);
        }
    }

    /// <summary>R2, at the end of <paramref name="actor"/>'s turn: a Vex earned this turn lasts one more; an older one ends.</summary>
    private void ConditionsAtTurnEnd(Actor actor)
    {
        if (actor.VexFresh)
            actor.VexFresh = false;
        else
            actor.Vexing = null;
    }
}
