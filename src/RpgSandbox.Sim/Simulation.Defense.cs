using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

// Region Task 6A, game ADAPTATION (the SRD has no morale rule): a fighter of a faction that defends a place answers whoever
// it holds hostile and sees there. It sizes them up once (the Task 10 size-up: Insight reads their worth or only their
// look) and attacks if it expects to win; if unsure it holds its store and tells them to go, and attacks if they steal or
// stay; if it expects to lose badly it keeps its distance and lets them be. Nothing is known from afar: only what it sees.
internal sealed partial class Simulation
{
    /// <summary>Someone on a place its faction defends, on its map, conscious.</summary>
    private bool OnGroundOf(Faction faction, Actor npc, Actor someone) =>
        someone.Conscious && someone.MapArea == npc.MapArea && someone.Location is { } place && faction.Defends.Contains(place);

    /// <summary>Whoever it holds hostile and sees on its faction's ground, in actor ID order.</summary>
    private List<Actor> IntrudersSeenBy(Actor npc, Faction faction) =>
        World.Actors.Values
            .Where(a => a.Id != npc.Id && OnGroundOf(faction, npc, a) && HoldsHostile(npc, a) && SeesActor(npc, a))
            .OrderBy(a => a.Id.Value, StringComparer.Ordinal)
            .ToList();

    /// <summary>The faction's own store, if it stands on the ground it defends.</summary>
    private Store? DefendedStore(Faction faction) =>
        faction.HomeStore is { } id && World.Stores[id] is var store && faction.Defends.Contains(store.Location) ? store : null;

    /// <summary>A defender that is not judging intruders it now sees, or sees one of them at its store: it must decide now.</summary>
    private bool DefenderAlerted(Actor npc)
    {
        if (npc.IsPlayer || ProfileOf(npc).Flees || npc.Faction is not { } id || World.Factions[id] is not { Defends.Count: > 0 } faction)
            return false;
        var seen = IntrudersSeenBy(npc, faction);
        return seen.Any(a => npc.Defense is not { } d || !d.Intruders.Contains(a.Id) || TakingFrom(a, faction));
    }

    private bool TakingFrom(Actor someone, Faction faction) =>
        someone.CurrentAction is TakeFoodAction take && World.Stores[take.Store].Owner == faction.Id;

    /// <summary>
    /// The defence of the faction's ground: forgets intruders that have left it, sizes up new ones, then acts on its
    /// verdict. Null when there is nobody to defend against.
    /// </summary>
    private Plan? DefendStep(Actor npc, List<string> inputs)
    {
        if (ProfileOf(npc).Flees || npc.Faction is not { } id || World.Factions[id] is not { Defends.Count: > 0 } faction
            || npc.MapArea is null || CurrentPosition(npc) is not { } here)
            return null;
        if (npc.Defense is { } old)
        {
            old.Intruders.RemoveWhere(i => !OnGroundOf(faction, npc, World.Actors[i]));
            if (old.Intruders.Count == 0)
                npc.Defense = null;
        }
        var seen = IntrudersSeenBy(npc, faction);
        if (npc.Defense is null && seen.Count == 0)
            return null;

        var place = World.Locations[(seen.FirstOrDefault() ?? World.Actors[npc.Defense!.Intruders.First()]).Location!.Value].Name;
        if (npc.Defense is null || seen.Any(a => !npc.Defense.Intruders.Contains(a.Id)))
            Judge(npc, seen, place, inputs);
        var defense = npc.Defense!;
        var names = string.Join(", ", defense.Intruders.Select(i => World.Actors[i].Name));
        inputs.Add($"Intrusi in {place}: {names} ({defense.Verdict})");

        // A theft from its faction it saw, or someone it sees taking from its store: provocation.
        var stolen = npc.Knowledge.Where(o => o.Kind == DeedKind.Theft && World.Stores[o.Store!.Value].Owner == id
                                              && o.ObservedAt >= defense.Since && (defense.Answered is not { } done || o.ObservedAt > done)).ToList();
        if (stolen.Count > 0)
            defense.Answered = stolen.Max(o => o.ObservedAt);
        var provoked = stolen.Count > 0 || seen.Any(a => TakingFrom(a, faction));
        if (defense.Verdict == DefenseVerdict.Warn && seen.Count > 0
            && (provoked || World.Now >= defense.Since.Plus(Tuning.DefendPatience)))
        {
            defense.Verdict = DefenseVerdict.Attack;
            World.RecordFact("DefenseAttack", provoked
                    ? $"{npc.Name} vede {names} mettere le mani sulle provviste e attacca."
                    : $"{npc.Name} perde la pazienza: {names} non se ne {(defense.Intruders.Count > 1 ? "vanno" : "va")} da {place}.",
                defense.Intruders.Prepend(npc.Id).ToArray());
        }
        else if (defense.Verdict == DefenseVerdict.Withdraw && stolen.Count > 0)
            World.RecordFact("DefenseHelpless", $"{npc.Name} guarda {names} prendere le provviste e non interviene.",
                defense.Intruders.Prepend(npc.Id).ToArray());

        // One that gave up on its ground still goes where its band sends it.
        if (defense.Verdict == DefenseVerdict.Withdraw && npc.Assignment is not null)
            return null;
        var store = DefendedStore(faction);
        return defense.Verdict switch
        {
            DefenseVerdict.Attack => AttackIntruder(npc, seen, store),
            DefenseVerdict.Warn => HoldStore(npc, seen, store),
            _ => KeepAway(npc, faction, seen, here),
        };
    }

    /// <summary>Sizes up everyone on the ground it knows of and records its verdict, as a fact the intruders perceive.</summary>
    private void Judge(Actor npc, List<Actor> seen, string place, List<string> inputs)
    {
        var group = seen.Select(a => a.Id).Concat(npc.Defense?.Intruders ?? Enumerable.Empty<ActorId>())
            .Distinct().OrderBy(i => i.Value, StringComparer.Ordinal).Select(i => World.Actors[i]).ToList();
        var (toDropThem, toDropMe) = Estimate(npc, group, inputs);
        var verdict = toDropThem < toDropMe ? DefenseVerdict.Attack
            : toDropMe < toDropThem * Tuning.DefendHopelessRatio ? DefenseVerdict.Withdraw
            : DefenseVerdict.Warn;
        npc.Defense = new Defense { Verdict = verdict, Since = World.Now };
        foreach (var a in group)
            npc.Defense.Intruders.Add(a.Id);
        var names = string.Join(", ", group.Select(a => a.Name));
        var participants = group.Select(a => a.Id).Prepend(npc.Id).ToArray();
        switch (verdict)
        {
            case DefenseVerdict.Attack:
                World.RecordFact("DefenseAttack", $"{npc.Name} si scaglia contro {names} per difendere {place}.", participants);
                break;
            case DefenseVerdict.Warn:
                World.RecordFact("DefenseWarning", $"{npc.Name} intima a {names} di andarsene da {place}.", participants);
                break;
            default:
                World.RecordFact("DefenseWithdrawal", $"{npc.Name} arretra davanti a {names}.", participants);
                break;
        }
    }

    /// <summary>Attacks the nearest intruder it sees, walking up to it first; with none in sight, goes back to its store.</summary>
    private Plan AttackIntruder(Actor npc, List<Actor> seen, Store? store)
    {
        var here = CurrentPosition(npc)!.Value;
        var foe = seen.OrderBy(a => here.StepsTo(CurrentPosition(a)!.Value)).ThenBy(a => a.Id.Value, StringComparer.Ordinal).FirstOrDefault();
        if (foe is not null && MeleeAttack(npc) is { } blade)
        {
            if (CanTalk(npc, foe))
                return Do("Difendi il territorio", $"Attacca {foe.Name}.",
                    new AttackCommand { Actor = npc.Id, Target = foe.Id, Attack = blade.Name });
            if (StepNextTo(npc, foe) is { } approach)
                return new Plan("Difendi il territorio", $"Va verso {foe.Name} per attaccarlo.", () => Execute(approach));
        }
        return HoldStore(npc, seen, store);
    }

    /// <summary>
    /// Stands up to the intruders without striking: next to the one closest to its store, or on its store, and watches.
    /// </summary>
    private Plan HoldStore(Actor npc, List<Actor> seen, Store? store)
    {
        if (store is not null && seen.Count > 0)
        {
            var map = World.Maps[npc.MapArea!.Value];
            var squares = UseSquares(store, map).ToList();
            var closest = seen.OrderBy(a => squares.Min(s => s.StepsTo(CurrentPosition(a)!.Value)))
                .ThenBy(a => a.Id.Value, StringComparer.Ordinal).First();
            if (squares.Any(s => s.IsAdjacentOrSame(CurrentPosition(closest)!.Value)) && StepNextTo(npc, closest) is { } face)
                return new Plan("Difendi il territorio", $"Si para davanti a {closest.Name}, vicino a {store.Name}.", () => Execute(face));
        }
        if (store is not null && StepToStore(npc, store, stealthy: false) is { } guard)
            return new Plan("Difendi il territorio", $"Si mette a guardia di {store.Name}.", () => Execute(guard));
        return Rest("Difendi il territorio", "Tiene d'occhio gli intrusi.", npc, "Tiene d'occhio gli intrusi",
            World.Now.Plus(Duration.FromMinutes(1)));
    }

    /// <summary>Backs away from intruders that come too close, staying on its ground where it can; otherwise watches them.</summary>
    private Plan KeepAway(Actor npc, Faction faction, List<Actor> seen, GridPos here)
    {
        int Distance(GridPos square) => seen.Count == 0 ? int.MaxValue : seen.Min(a => CurrentPosition(a)!.Value.StepsTo(square));
        if (Distance(here) <= Tuning.DefendWithdrawSteps)
        {
            var map = World.Maps[npc.MapArea!.Value];
            var opens = Opens(npc, map);
            var away = Enumerable.Range(0, map.Height).SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new GridPos(x, y)))
                .Where(s => map.IsWalkable(s) && Distance(s) > Distance(here))
                .OrderByDescending(s => map.ZoneAt(s) is { } zone && faction.Defends.Contains(zone))
                .ThenByDescending(Distance).ThenBy(s => s.Y).ThenBy(s => s.X)
                .Take(16)
                .Where(s => Reachable(map, here, s, opens))
                .Select(s => (GridPos?)s)
                .FirstOrDefault();
            if (away is { } to)
                return new Plan("Tieni le distanze", "Arretra dagli intrusi.", () => Execute(new MoveCommand { Actor = npc.Id, To = to }));
        }
        return Rest("Tieni le distanze", "Tiene d'occhio gli intrusi da lontano.", npc, "Tiene d'occhio gli intrusi",
            World.Now.Plus(Duration.FromMinutes(1)));
    }
}
