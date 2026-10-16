using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// R4a: grappling and shoving with the Unarmed Strike (SRD 5.2.1 Rules Glossary: Unarmed Strike, Grappling, Grappled).
internal sealed partial class Simulation
{
    /// <summary>SRD Shove: 5 feet straight away.</summary>
    private const int ShoveInches = 5 * Spot.PerFoot;

    /// <summary>
    /// A hand free to grab: nothing in the main hand, or no shield and no off-hand weapon beside a one-handed weapon.
    /// ADAPTATION: a Versatile weapon without a shield is held in two hands (as the attack profile counts it).
    /// </summary>
    private bool HasFreeHand(Actor actor)
    {
        var eq = actor.Equipment;
        if (eq.MainHand is null)
            return true;
        if (eq.Shield is not null || eq.OffHand is not null || TorchLit(actor)) // T1: a lit torch fills a hand
            return false;
        return World.Content.Items.TryGetValue(eq.MainHand, out var item) && item.Weapon is { } w
               && !w.Has(WeaponProperty.TwoHanded) && !w.Has(WeaponProperty.Versatile);
    }

    /// <summary>SRD: the DC of a grapple or a shove, and of escaping a grapple: 8 + Strength modifier + Proficiency Bonus.</summary>
    private static int ManeuverDc(Actor actor) => 8 + Abilities.Modifier(actor.Sheet.Strength) + actor.Sheet.ProficiencyBonus;

    /// <summary>Why this Unarmed Strike cannot be a grapple or a shove now (null if it can).</summary>
    private string? ManeuverRefused(Encounter? e, Actor attacker, Actor target, AttackProfile attack, string maneuver)
    {
        if (maneuver is not ("Grapple" or "Shove"))
            return "Manovra sconosciuta: Grapple o Shove.";
        if (e is null)
            return "Lottare e spingere solo durante uno scontro (non per aprirlo).";
        if (!attack.Melee || attack.Requires is not null || attack.Item is not null || attack.Damage.Count > 0)
            return "Si lotta e si spinge con il colpo senz'armi.";
        if (maneuver == "Grapple" && !HasFreeHand(attacker))
            return "Ti serve una mano libera per afferrare.";
        if (maneuver == "Grapple" && target.Conditions.Any(c => c.Kind == ConditionKind.Grappled && c.Source == attacker.Id))
            return $"Tieni già {target.Name}.";
        return null;
    }

    /// <summary>
    /// SRD Unarmed Strike, Grapple or Shove: the target makes a Strength or Dexterity saving throw (the better for it)
    /// against the attacker's DC; on a failure it is Grappled, or pushed 5 feet away, or knocked Prone.
    /// </summary>
    private string ResolveManeuver(Encounter e, Actor attacker, Actor target, string maneuver, bool prone)
    {
        attacker.Sneak = null;
        attacker.FoughtWith.Add(target.Id);
        target.FoughtWith.Add(attacker.Id);
        e.AttackedThisRound = true;
        var grapple = maneuver == "Grapple";
        var tries = grapple ? "tenta di afferrare" : prone ? "tenta di buttare a terra" : "tenta di spingere via";
        Record(e, CombatLogKind.Condition, attacker, target, (a, t) => $"{a} {tries} {t}.");
        var ability = target.Sheet.SaveBonus(Ability.Strength) >= target.Sheet.SaveBonus(Ability.Dexterity) ? Ability.Strength : Ability.Dexterity;
        if (SavingThrow(e, target, ability, ManeuverDc(attacker), grapple ? "la presa" : "la spinta"))
            return $"{attacker.Name} {tries} {target.Name}, che resiste.";
        if (grapple)
            AddCondition(e, target, ConditionKind.Grappled, attacker);
        else if (prone)
            AddCondition(e, target, ConditionKind.Prone, attacker);
        else
            Push(e, attacker, target, ShoveInches);
        return $"{attacker.Name} {tries} {target.Name}: riesce.";
    }

    /// <summary>
    /// SRD Grappling, Ending a Grapple: the action, a Strength (Athletics) or Dexterity (Acrobatics) check (the better)
    /// against the grapple's escape DC.
    /// </summary>
    private CommandResult Escape(Encounter e, Actor actor)
    {
        var hold = actor.Conditions.FirstOrDefault(c => c.Kind == ConditionKind.Grappled);
        if (hold?.Source is not { } id || !World.Actors.TryGetValue(id, out var grappler))
            return CommandResult.Rejected(RejectionReason.NotHeld, "Non sei afferrato.");
        CombatantOf(e, actor.Id).ActionUsed = true;
        var skill = actor.Sheet.Bonus(Skill.Athletics) >= actor.Sheet.Bonus(Skill.Acrobatics) ? Skill.Athletics : Skill.Acrobatics;
        var (modifier, _, disadvantage) = CheckEdges(actor);
        var roll = D20.Roll(World.Rng, actor.Sheet.Bonus(skill) + modifier, disadvantage: disadvantage);
        var freed = roll.Total >= ManeuverDc(grappler);
        var text = $"{actor.Name} cerca di liberarsi da {grappler.Name}: {(skill == Skill.Athletics ? "Forza (Atletica)" : "Destrezza (Acrobazia)")} {roll.Describe()}, "
                   + (freed ? "si libera" : "non ci riesce");
        Record(e, CombatLogKind.Condition, actor, grappler, (_, _) => $"{text}.", roll);
        if (freed)
            RemoveCondition(e, actor, ConditionKind.Grappled, "si libera dalla presa");
        return new CommandResult { Message = $"{text}." };
    }

    /// <summary>SRD: the grappler can release the target at any time (no action required).</summary>
    private CommandResult Release(ReleaseCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        var held = Grappled(actor).ToList();
        if (held.Count == 0)
            return CommandResult.Rejected(RejectionReason.NotHeld, "Non tieni nessuno.");
        foreach (var other in held)
            RemoveCondition(EncounterOf(other), other, ConditionKind.Grappled, $"viene lasciato andare da {actor.Name}");
        return new CommandResult { Message = $"{actor.Name} lascia la presa." };
    }

    /// <summary>The creatures this one grapples.</summary>
    private IEnumerable<Actor> Grappled(Actor grappler) =>
        World.Actors.Values.Where(a => a.Conditions.Any(c => c.Kind == ConditionKind.Grappled && c.Source == grappler.Id))
            .OrderBy(a => a.Id.Value, StringComparer.Ordinal);

    /// <summary>
    /// SRD Grappling: a grapple ends when the grappler is Incapacitated (or down), or the two are no longer within 5 feet.
    /// </summary>
    private void CheckGrapples()
    {
        foreach (var held in World.Actors.Values.OrderBy(a => a.Id.Value, StringComparer.Ordinal).ToList())
            foreach (var hold in held.Conditions.Where(c => c.Kind == ConditionKind.Grappled).ToList())
            {
                var ok = hold.Source is { } id && World.Actors.TryGetValue(id, out var grappler) && grappler.Conscious && !Incapacitated(grappler)
                         && grappler.MapArea == held.MapArea && held.MapArea is { } area && CurrentSpot(grappler) is { } a && CurrentSpot(held) is { } b
                         && WithinFiveFeet(World.Maps[area], a, b);
                if (!ok)
                    RemoveCondition(EncounterOf(held), held, ConditionKind.Grappled, "non è più afferrato");
            }
    }

    /// <summary>Whether it drags someone it grapples when it moves (SRD: every foot costs 1 extra foot).</summary>
    private bool Dragging(Actor actor) => Grappled(actor).Any();

    /// <summary>
    /// SRD Grappled, Movable: the grappler drags the creature it holds. ADAPTATION: the held one keeps where it was in
    /// relation to the grappler; if there is no room there, the grapple is lost.
    /// </summary>
    private void DragAlong(Encounter e, Actor grappler, Spot from, Spot to)
    {
        if (from == to)
            return;
        var map = World.Maps[e.Area];
        foreach (var held in Grappled(grappler).ToList())
        {
            var was = CurrentSpot(held)!.Value;
            var now = new Spot(was.X + to.X - from.X, was.Y + to.Y - from.Y);
            var others = World.Actors.Values
                .Where(o => o.Id != held.Id && o.Id != grappler.Id && o.MapArea == map.Area && o.Vitality.State != LifeState.Dead && CurrentSpot(o) is not null)
                .Select(o => CurrentSpot(o)!.Value).Append(to).ToList();
            if (map.InBounds(now.Cell) && new SpotPlanner(map, others, others, closedDoorsBlock: true).Fits(now) && SpotPlanner.LineClear(map, was, now))
            {
                Place(held, now.Cell, now);
                Record(e, CombatLogKind.Mastery, grappler, held, (a, t) => $"{a} trascina {t}.", spots: new[] { was, now });
            }
            else
                RemoveCondition(e, held, ConditionKind.Grappled, "sfugge alla presa");
        }
        CheckGrapples();
    }
}
