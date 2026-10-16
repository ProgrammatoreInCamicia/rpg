using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// R2: the weapon mastery properties (SRD 5.2.1 "Mastery Properties"), for whoever has unlocked the weapon's mastery.
// Graze is in the attack roll itself; Nick comes with fighting with two weapons (R4).
internal sealed partial class Simulation
{
    /// <summary>SRD Push: up to 10 feet straight away from the attacker.</summary>
    private const int PushInches = 10 * Spot.PerFoot;

    /// <summary>
    /// After an attack with a mastery: Sap, Slow, Vex, Topple, Push or Cleave, as the SRD says (Slow and Vex need damage
    /// dealt; the others a hit). ADAPTATION: Cleave's second target and Push are chosen by the game (Cleave: the nearest
    /// enemy within 5 feet of the first that is also in reach; Push always pushes the full 10 feet, or as far as the way
    /// is free).
    /// </summary>
    private void ApplyMastery(Encounter e, Actor attacker, Actor target, AttackProfile attack, AttackResult result)
    {
        if (attack.Mastery is not { } mastery || !result.Hit)
            return;
        var dealt = result.Damage > 0;
        switch (mastery)
        {
            case WeaponMastery.Sap when target.Conscious:
                target.SappedBy = attacker.Id;
                Record(e, CombatLogKind.Mastery, attacker, target, (a, t) => $"{t} è intontito: svantaggio al suo prossimo attacco.");
                break;
            case WeaponMastery.Slow when dealt && target.Conscious:
                target.SlowedBy = attacker.Id;
                Record(e, CombatLogKind.Mastery, attacker, target, (a, t) => $"{t} è rallentato: 10 piedi in meno.");
                break;
            case WeaponMastery.Vex when dealt && target.Conscious:
                attacker.Vexing = target.Id;
                attacker.VexFresh = true;
                Record(e, CombatLogKind.Mastery, attacker, target, (a, t) => $"{a} ha messo {t} in difficoltà: vantaggio al prossimo attacco.");
                break;
            case WeaponMastery.Topple when target.Conscious && !Has(target, ConditionKind.Prone):
                var dc = 8 + attack.AbilityModifier + attacker.Sheet.ProficiencyBonus;
                if (!SavingThrow(e, target, Ability.Constitution, dc, "Atterrare"))
                    AddCondition(e, target, ConditionKind.Prone, attacker);
                break;
            case WeaponMastery.Push when target.Vitality.State != LifeState.Dead:
                Push(e, attacker, target);
                break;
            case WeaponMastery.Cleave when attack.Melee && !CombatantOf(e, attacker.Id).Cleaved:
                Cleave(e, attacker, target, attack);
                break;
        }
    }

    /// <summary>SRD Push: the target goes straight away from the attacker, up to 10 feet, while there is room (no Opportunity Attacks).</summary>
    private void Push(Encounter e, Actor attacker, Actor target, int inches = PushInches)
    {
        var map = World.Maps[e.Area];
        var from = CurrentSpot(attacker)!.Value;
        var start = CurrentSpot(target)!.Value;
        var length = from.DistanceTo(start);
        if (length < 1)
            return;
        var others = World.Actors.Values
            .Where(o => o.Id != target.Id && o.MapArea == map.Area && o.Vitality.State != LifeState.Dead && CurrentSpot(o) is not null)
            .Select(o => CurrentSpot(o)!.Value).ToList();
        var room = new SpotPlanner(map, others, others, closedDoorsBlock: true);
        Spot? landed = null;
        for (var k = 6; k <= inches; k += 6)
        {
            var p = new Spot((int)Math.Round(start.X + (start.X - from.X) * k / length, MidpointRounding.AwayFromZero),
                (int)Math.Round(start.Y + (start.Y - from.Y) * k / length, MidpointRounding.AwayFromZero));
            if (!map.InBounds(p.Cell) || !room.Fits(p) || !SpotPlanner.LineClear(map, start, p))
                break;
            landed = p;
        }
        if (landed is not { } to)
            return;
        Place(target, to.Cell, to);
        EvaluateContacts();
        CheckGrapples(); // R4a: pushed out of a grip
        Record(e, CombatLogKind.Mastery, attacker, target, (a, t) => $"{a} spinge indietro {t} di {FeetText(start.DistanceTo(to))} piedi.",
            spots: new[] { start, to });
    }

    /// <summary>
    /// SRD Cleave: once a turn, after a melee hit, a melee attack against a second creature within 5 feet of the first and
    /// within reach; its damage leaves out the ability modifier unless it is negative.
    /// </summary>
    private void Cleave(Encounter e, Actor attacker, Actor first, AttackProfile attack)
    {
        var map = World.Maps[e.Area];
        var near = CurrentSpot(first)!.Value;
        var second = EnemiesOf(e, attacker)
            .Where(o => o.Id != first.Id && o.Conscious && CurrentSpot(o) is { } p && WithinFiveFeet(map, near, p)
                        && InReach(map, StandingOf(attacker), attack, StandingOf(o)) && SeesActor(attacker, o))
            .OrderBy(o => CurrentSpot(attacker)!.Value.DistanceTo(CurrentSpot(o)!.Value)).ThenBy(o => o.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (second is null)
            return;
        CombatantOf(e, attacker.Id).Cleaved = true;
        Record(e, CombatLogKind.Mastery, attacker, second, (a, t) => $"{a} prosegue il fendente su {t}.");
        ResolveAttack(e, attacker, second, attack with
        {
            DamageBonus = attack.DamageBonus - Math.Max(0, attack.AbilityModifier), Mastery = null,
        }, nonLethal: false, CombatLogKind.Attack);
    }
}
