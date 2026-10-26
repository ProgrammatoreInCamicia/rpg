using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// Task 10, game ADAPTATION (the SRD has no morale rule): a raider who finds someone hostile holding the store it came for
// sizes them up once, and fights only when it expects to win. A Wisdom (Insight) check decides what it reads: on a success
// what they are really worth in a fight, on a failure only how they look (armor, weapon, wounds). The estimate is how many
// rounds each side needs to drop the other, never a fight played out.
internal sealed partial class Simulation
{
    /// <summary>What a raider makes of a combatant: Armor Class, Hit Points left, and its best melee attack.</summary>
    private sealed record Reading(int ArmorClass, int HitPoints, int AttackBonus, double Damage);

    /// <summary>
    /// Whoever conscious it holds hostile and sees holding the store: standing on a square it is used from. (Someone a step
    /// away only watches: the raider still steals under its eyes, and is seen doing it.) One it cannot see counts too once
    /// it has walked into them (<see cref="RaidAssignment.Startled"/>): it knows a body is there, next to it.
    /// </summary>
    private List<Actor> RivalsAtStore(Actor npc, Store store)
    {
        if (store.Position is null || npc.MapArea is not { } area || World.Locations[store.Location].Area != area)
            return new List<Actor>();
        var map = World.Maps[area];
        var squares = UseSquares(store, map).ToList();
        var felt = npc.Assignment is { Startled: true };
        return World.Actors.Values
            .Where(a => a.Id != npc.Id && a.Conscious && a.MapArea == area && HoldsHostile(npc, a)
                        && CurrentPosition(a) is { } at && squares.Contains(at)
                        && (SeesActor(npc, a) || felt && CurrentPosition(npc) is { } me && me.IsAdjacentOrSame(at)))
            .OrderBy(a => a.Id.Value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Sizes up the rivals once and records it: true when it expects to drop them all before they drop it. Ties go to
    /// caution. Its own worth it always knows.
    /// </summary>
    private bool SizeUp(Actor npc, IReadOnlyList<Actor> rivals, List<string> inputs)
    {
        var edges = CheckEdges(npc);
        var insight = D20.Roll(World.Rng, npc.Sheet.Bonus(Skill.Insight) + edges.Modifier, edges.Advantage, edges.Disadvantage);
        var reads = insight.Total >= Tuning.SizeUpDc;
        var names = string.Join(", ", rivals.Select(r => r.Name));
        World.RecordFact("Roll", $"{npc.Name}, Intuizione su {names}: {insight.Describe()} (CD {Tuning.SizeUpDc}).", npc.Id);

        var self = Truly(npc);
        var them = rivals.Select(r => reads ? Truly(r) : ByTheLook(r)).ToList();
        var toDropThem = them.Sum(r => r.HitPoints / PerRound(self, r));
        var toDropMe = self.HitPoints / them.Sum(r => PerRound(r, self));
        var fights = toDropThem < toDropMe;
        inputs.Add($"Prende le misure a {names} ({(reads ? "li legge" : "solo dall'aspetto")}): "
                   + $"li abbatte in {toDropThem:0.0} round, lo abbattono in {toDropMe:0.0}");
        World.RecordFact("SizedUp", $"{npc.Name} squadra {names}.", rivals.Select(r => r.Id).Prepend(npc.Id).ToArray());
        return fights;
    }

    /// <summary>What a combatant is really worth: its Armor Class, its Hit Points left, its best melee attack.</summary>
    private Reading Truly(Actor actor)
    {
        var profile = ProfileOf(actor);
        var best = profile.Attacks.Where(a => a.Melee).MaxBy(Average);
        return new Reading(profile.ArmorClass + SpellArmorBonus(actor), actor.Vitality.Current,
            best?.AttackBonus ?? 0, best is null ? 0 : Average(best));
    }

    /// <summary>
    /// What it looks like: the armor and shield worn (without what skill adds), the Hit Points of an unhurt fighter cut by
    /// how hurt it looks, and the weapon in its hand at a fighter's bonuses.
    /// </summary>
    private Reading ByTheLook(Actor actor)
    {
        var armor = actor.Equipment.Armor is { } worn ? World.Content.Item(worn)?.Armor?.Base ?? 10 : 10;
        var shield = actor.Equipment.Shield is not null ? 2 : 0;
        var looks = actor.Vitality.Describe() switch
        {
            "ferito" => 0.75,
            "malconcio" => 0.35,
            _ => 1.0,
        };
        var hitPoints = Math.Max(1, (int)Math.Ceiling(Tuning.SizeUpLooksHitPoints * looks));
        var weapon = actor.Equipment.MainHand is { } held ? World.Content.Item(held)?.Weapon : null;
        var die = weapon is null || weapon.Ranged ? 1.0 : Average(weapon.Damage);
        return new Reading(armor + shield, hitPoints, Tuning.SizeUpLooksAttackBonus, die + Tuning.SizeUpLooksDamageBonus);
    }

    /// <summary>Expected damage of one attack a round: the chance to hit (a 1 always misses, a 20 always hits) times the damage.</summary>
    private static double PerRound(Reading attacker, Reading target) =>
        Math.Clamp((21 + attacker.AttackBonus - target.ArmorClass) / 20.0, 0.05, 0.95) * Math.Max(attacker.Damage, 0.1);

    private static double Average(AttackProfile attack) => Average(attack.Damage) + attack.DamageBonus;

    private static double Average(Dice dice) => dice.Count * (dice.Sides + 1) / 2.0;
}
