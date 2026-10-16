using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// C4c: the world reacts to fights. Who sees an ally or an innocent attacked by someone it holds hostile joins in; an
// authority that only hears a fight goes to look; who is no fighter keeps away, and runs from a fight when badly hurt.
internal sealed partial class Simulation
{
    /// <summary>Squares within which a fight, or someone wanted, is too close for comfort to a non-fighter.</summary>
    private const int TooClose = 6;

    // ---------------------------------------------------------------- joining

    /// <summary>
    /// The side <paramref name="npc"/> would take in a fight: the one opposing those it holds hostile, if that side has
    /// someone still standing. With two sides that both qualify (it holds someone hostile on each), it stays out.
    /// Null when no side, or both, would do.
    /// </summary>
    private int? SideToJoin(Encounter e, Actor npc)
    {
        int? choice = null;
        foreach (var side in e.Order.Select(c => c.Side).Distinct().Order())
        {
            var members = e.Order.Where(c => c.Side == side).Select(c => World.Actors[c.Actor]).ToList();
            var others = e.Order.Where(c => c.Side != side).Select(c => World.Actors[c.Actor]).ToList();
            if (!members.Any(m => m.Conscious)
                || !others.Any(o => o.Conscious && HoldsHostile(npc, o)))
                continue;
            if (choice is not null)
                return null;
            choice = side;
        }
        return choice;
    }

    /// <summary>A fighter that sees a fight where it would take a side joins it.</summary>
    private Plan? JoinStep(Actor npc, List<string> inputs)
    {
        if (ProfileOf(npc).Flees || npc.MapArea is not { } area)
            return null;
        foreach (var e in World.Encounters.Values.Where(x => x.Area == area))
        {
            if (!e.Order.Any(c => SeesActor(npc, World.Actors[c.Actor])) || SideToJoin(e, npc) is not { } side)
                continue;
            var defended = e.Order.Where(c => c.Side == side).Select(c => World.Actors[c.Actor].Name);
            inputs.Add($"Vede uno scontro: sta con {string.Join(" e ", defended)}");
            return new Plan("Entra nello scontro", $"Difende {string.Join(" e ", defended)}.", () =>
            {
                JoinFight(e, npc, side);
                return new CommandResult { Message = $"{npc.Name} entra nello scontro." };
            });
        }
        return null;
    }

    /// <summary>
    /// ADAPTATION (the SRD has no rule for it): the newcomer rolls initiative now and takes its place in the order, without
    /// moving the turn in progress; it acts from the next round.
    /// </summary>
    private void JoinFight(Encounter e, Actor npc, int side)
    {
        CancelAction(npc, "inizia uno scontro");
        npc.LastHarmAt = World.Now;
        var roll = InitiativeRoll(npc, surprised: false);
        var joiner = new Combatant
        {
            Actor = npc.Id, Side = side, Initiative = roll.Roll.Total, InitiativeBonus = roll.Bonus, JoinedInRound = e.Round,
        };
        bool After(Combatant o) => o.Initiative < joiner.Initiative
                                   || (o.Initiative == joiner.Initiative && (o.InitiativeBonus < joiner.InitiativeBonus
                                       || (o.InitiativeBonus == joiner.InitiativeBonus && string.CompareOrdinal(o.Actor.Value, joiner.Actor.Value) > 0)));
        var index = e.Order.FindIndex(After);
        if (index < 0)
            index = e.Order.Count;
        e.Order.Insert(index, joiner);
        if (!e.RoundClosed && index <= e.Turn)
            e.Turn++; // the turn in progress stays the same combatant's
        Record(e, CombatLogKind.Joined, npc, null, (a, _) => $"{a} entra nello scontro (iniziativa {roll.Roll.Describe()}).", roll.Roll,
            playerText: (a, _) => $"{a} entra nello scontro."); // C5a
        World.RecordFact("JoinedFight", $"{npc.Name} entra nello scontro.", npc.Id);
    }

    // ---------------------------------------------------------------- going to look

    /// <summary>An authority that hears a fight it does not see goes towards the noise (roughly where it came from).</summary>
    private Plan? InvestigateStep(Actor npc, List<string> inputs)
    {
        if (npc.Faction is not { } id || World.Factions[id].Authority != npc.Id || npc.MapArea is not { } area
            || CurrentPosition(npc) is not { } here)
            return null;
        var noise = npc.Knowledge
            .Where(o => o.Source is null && o.Perceived == PerceptionMode.Heard && o.Square is not null
                        && (o.Kind == DeedKind.Assault && o.Encounter is { } fight && World.Encounters.TryGetValue(fight, out var e) && e.Area == area
                            && !e.Order.Any(c => SeesActor(npc, World.Actors[c.Actor]))
                            // P4a: blows on a door, heard lately (on its own map: it heard them there)
                            || o.Kind == DeedKind.BreakIn && World.Now.Since(o.LearnedAt).CompareTo(Tuning.InvestigateBreakInFor) <= 0))
            .OrderByDescending(o => o.LearnedAt).ThenBy(o => o.Id)
            .FirstOrDefault();
        var fightNoise = noise?.Kind == DeedKind.Assault;
        // A fight: up to the noise; blows on a door: up to the door (a door square is not always walkable).
        if (noise is not { Square: { } towards } || towards == here || !fightNoise && here.StepsTo(towards) <= 1)
            return null;
        var map = World.Maps[area];
        inputs.Add(fightNoise ? "Sente rumori di lotta e non vede chi" : "Sente colpi contro una porta");
        return new Plan("Va a vedere", fightNoise ? "Va verso i rumori di lotta." : "Va verso i colpi.", () =>
            Execute(new MoveCommand { Actor = npc.Id, To = towards, StopNextTo = !map.IsWalkable(towards) }));
    }

    // ---------------------------------------------------------------- keeping away

    /// <summary>
    /// A non-fighter keeps away from trouble: a fight it sees within <see cref="TooClose"/> squares, or someone its faction
    /// wants, close by. It goes home, and stays there while a fight it knows of goes on (it does not walk back into it).
    /// </summary>
    private Plan? FleeStep(Actor npc, List<string> inputs)
    {
        if (!ProfileOf(npc).Flees || CurrentPosition(npc) is not { } here || npc.Home is not { } home)
            return null;
        var threat = ThreatNear(npc, here);
        var fightGoesOn = npc.Knowledge.Any(o => o.Kind == DeedKind.Assault && o.Source is null
                                                 && o.Encounter is { } fight && World.Encounters.ContainsKey(fight));
        if (threat is null && !fightGoesOn)
            return null;
        inputs.Add(threat is not null ? $"Vede {threat} a pochi passi" : "Sa di uno scontro ancora in corso");
        if (StepTowards(npc, home, PostKind.Home) is { } step)
            return new Plan("Si allontana", $"Si tiene lontano da {threat ?? "lo scontro"}.", () => Execute(step));
        return Rest("Resta al sicuro", "A casa, aspetta che lo scontro finisca.", npc, "Resta al sicuro", World.Now.Plus(Duration.FromMinutes(1)));
    }

    /// <summary>What a non-fighter sees too close for comfort: a fight, or someone its faction wants. Null if nothing.</summary>
    private string? ThreatNear(Actor npc, GridPos here)
    {
        bool Close(Actor a) => CurrentPosition(a) is { } p && p.StepsTo(here) <= TooClose && SeesActor(npc, a);
        if (World.Encounters.Values.Where(e => e.Area == npc.MapArea).SelectMany(e => e.Order)
                .Select(c => World.Actors[c.Actor]).Any(a => a.Id != npc.Id && Close(a)))
            return "uno scontro";
        return npc.Faction is { } id && World.Factions[id].Wanted.Keys.Select(w => World.Actors[w])
                   .FirstOrDefault(w => w.Conscious && Close(w)) is { } wanted
            ? wanted.Name
            : null;
    }

    /// <summary>
    /// In a fight, a non-fighter below half its Hit Points runs (C4c): it moves where no enemy sees it, or else as far as it
    /// can from the nearest one. Returns false when it does not flee.
    /// </summary>
    private bool FleeTurn(Encounter e, Combatant c, Actor npc)
    {
        if (!ProfileOf(npc).Flees || (!c.Fleeing && (long)npc.Vitality.Current * 2 >= npc.Vitality.Maximum))
            return false;
        if (!c.Fleeing)
        {
            c.Fleeing = true;
            Record(e, CombatLogKind.Fled, npc, null, (a, _) => $"{a} cerca di scappare.");
            World.RecordFact("Fled", $"{npc.Name} cerca di scappare dallo scontro.", npc.Id);
        }
        npc.LastDecision = new DecisionTrace(World.Now, "Scontro: scappa", "È malridotto: scappa.", Array.Empty<string>());

        var map = World.Maps[e.Area];
        var from = CurrentPosition(npc)!.Value;
        var enemies = EnemiesOf(e, npc).Where(x => x.Conscious && CurrentPosition(x) is not null).ToList();
        var knows = Perceived(e, npc);
        var occupied = OccupantsOf(map, npc, knows);
        bool Unseen(GridPos sq) => !enemies.Any(x => Sees(map, x, CurrentPosition(x)!.Value, npc, sq, LightOn(map, sq)));
        int Distance(GridPos sq) => enemies.Count == 0 ? int.MaxValue : enemies.Min(x => CurrentPosition(x)!.Value.StepsTo(sq));
        // C3a: with an enemy next to it, Disengage (no opportunity attacks); otherwise Dash (twice as far).
        if (!c.ActionUsed)
            Act(e, npc, enemies.Any(x => MeleeAttack(x) is { } m && InReach(map, StandingOf(x), m, StandingOf(npc))) // P2a: by the bodies
                ? CombatAction.Disengage : CombatAction.Dash);
        var best = CombatReach(e, npc, map, from, from, knows, MovementLeft(e, npc))
            .Where(r => !occupied.Contains(r.Key))
            .Select(r => (Square: r.Key, Feet: r.Value.Feet))
            .Append((Square: from, Feet: 0))
            .OrderByDescending(x => Unseen(x.Square)).ThenByDescending(x => Distance(x.Square)).ThenBy(x => x.Feet)
            .ThenBy(x => x.Square.Y).ThenBy(x => x.Square.X)
            .First();
        // P2a: walked freely to the centre of that square, as far as the movement goes.
        if (best.Square != from && PlanFreeCombatMove(e, npc, Spot.CenterOf(best.Square)) is { Way: { } way }
            && NpcStep(e, npc, way, int.MaxValue) is { } step)
        {
            WalkFreeInCombat(e, npc, step);
            CheckEnd(e);
        }
        return true;
    }

    /// <summary>
    /// At the close of a round, who is fleeing and out of every enemy's sight is out of the fight. Returns true if that
    /// ended it (a side has nobody left in it).
    /// </summary>
    private bool Escapes(Encounter e)
    {
        foreach (var c in e.Order.Where(c => c.Fleeing).ToList())
        {
            var runner = World.Actors[c.Actor];
            if (!runner.Conscious || EnemiesOf(e, runner).Any(x => x.Conscious && SeesActor(x, runner)))
                continue;
            Defeat(e, runner, null); // C5b: driven off (judged while it is still in the fight)
            e.Order.Remove(c);
            Record(e, CombatLogKind.Fled, runner, null, (a, _) => $"{a} è scappato: è fuori dallo scontro.");
            if (runner.IsPlayer) // P2b: out of the fight, it keeps its log
                World.LastFight = new LastFight(e.Area, e.Log.ToList());
            World.RecordFact("Escaped", $"{runner.Name} è scappato dallo scontro.", runner.Id);
        }
        if (e.Order.Select(c => c.Side).Distinct().Count() < 2)
        {
            EndEncounter(e, "Chi restava da una parte è scappato: lo scontro finisce.");
            return true;
        }
        e.Turn = Math.Min(e.Turn, e.Order.Count);
        return CheckEnd(e);
    }

    /// <summary>
    /// A fight seen or heard alarms an NPC: guarding or idling, it stops to decide what to do; walking, it stops the first
    /// time it learns of this fight.
    /// </summary>
    private void Alarm(Actor witness, bool firstNews)
    {
        if (witness.IsPlayer)
            return;
        if (witness.CurrentAction is GuardAction or WaitAction { Interruptible: true } || (firstNews && witness.CurrentAction is MoveAction))
            CancelAction(witness);
    }
}
