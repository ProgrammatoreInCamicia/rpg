using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

// Faction upkeep and policies. Factions act through their members: a policy creates assignments,
// it never moves or creates food itself.
internal sealed partial class Simulation
{
    private const long SecondsPerDay = 86_400;

    internal static GameTime NextUpkeepAfter(GameTime now, Faction faction)
    {
        var dayStart = now.Seconds / SecondsPerDay * SecondsPerDay;
        var candidate = dayStart + faction.UpkeepTimeOfDay.Seconds;
        return new GameTime(candidate > now.Seconds ? candidate : candidate + SecondsPerDay);
    }

    private void RunUpkeep(Faction faction)
    {
        var store = World.Stores[faction.HomeStore!.Value];
        var eaten = Math.Min(faction.DailyUpkeep, store.Food);
        store.Food -= eaten;
        var shortfall = faction.DailyUpkeep - eaten;
        World.RecordFact("FoodConsumed",
            $"{faction.Name} consuma {eaten} razioni da {store.Name} (restano {store.Food})" +
            (shortfall > 0 ? $"; ne mancavano {shortfall}." : "."));
        World.Scheduler.Schedule(NextUpkeepAfter(World.Now, faction), new FactionUpkeep(faction.Id));
    }

    private void EvaluateFactionPolicy(Faction faction)
    {
        var policy = faction.Policy!;
        faction.LastDecision = DecideRaid(faction, policy);
        faction.NextEvaluation = World.Now.Plus(policy.EvaluationInterval);
        World.Scheduler.Schedule(faction.NextEvaluation.Value, new EvaluateFaction(faction.Id));
    }

    private DecisionTrace DecideRaid(Faction faction, RaidPolicy policy)
    {
        var home = World.Stores[faction.HomeStore!.Value];
        var inputs = new List<string> { $"Scorte di casa ({home.Name}): {home.Food}", $"Soglia: {policy.FoodThreshold}" };
        DecisionTrace Trace(string rule, string reason) => new(World.Now, rule, reason, inputs.ToArray());

        var raider = World.MembersOf(faction.Id).FirstOrDefault(m => m.Assignment is not null);
        if (raider is not null)
            return Trace("Razzia in corso", $"{raider.Name} sta già eseguendo una razzia.");

        if (home.Food >= policy.FoodThreshold)
            return Trace("Scorte sufficienti", $"{home.Food} ≥ {policy.FoodThreshold}: nessuna razzia.");

        // Targets reported as guarded by a returning member are avoided for a while.
        var avoided = faction.AvoidUntil.Where(a => a.Value > World.Now).Select(a => a.Key).ToHashSet();
        foreach (var store in avoided)
            inputs.Add($"Da evitare fino alle {faction.AvoidUntil[store].Hour:00}:{faction.AvoidUntil[store].Minute:00}: {World.Stores[store].Name} (sorvegliato)");

        var target = World.Stores.Values
            .Where(s => s.Owner != faction.Id && s.Food > 0 && !avoided.Contains(s.Id))
            .Select(s => (Store: s, Cost: PathCost(home.Location, s.Location)))
            .Where(x => x.Cost is not null)
            .OrderBy(x => x.Cost!.Value.Seconds).ThenBy(x => x.Store.Id)
            .Select(x => x.Store)
            .FirstOrDefault();
        if (target is null && avoided.Count > 0)
            return Trace("Bersaglio sorvegliato", "Scorte basse, ma l'unico bersaglio è sorvegliato: aspettano.");
        if (target is null)
            return Trace("Nessun bersaglio", "Scorte basse, ma non c'è nessun deposito altrui raggiungibile e non vuoto.");
        inputs.Add($"Bersaglio più vicino: {target.Name}");

        var member = World.MembersOf(faction.Id)
            .FirstOrDefault(m => !m.IsPlayer && m.Location is not null &&
                                 (m.CurrentAction is null || m.CurrentAction is WaitAction { Interruptible: true }));
        if (member is null)
            return Trace("Nessun membro disponibile", "Scorte basse, ma tutti i membri sono impegnati.");

        // An assignment pre-empts routine only: cancel an interruptible wait, never anything else.
        member.CurrentAction = null;
        member.Assignment = new RaidAssignment
        {
            Faction = faction.Id,
            Target = target.Id,
            Home = home.Id,
            Amount = policy.RaidAmount,
            AssignedAt = World.Now,
        };
        World.RecordFact("RaidOrdered", $"{faction.Name} manda {member.Name} a razziare {target.Name}.");
        return Trace("Ordina una razzia",
            $"{home.Food} < {policy.FoodThreshold}: {member.Name} va a prendere {policy.RaidAmount} razioni da {target.Name}.");
    }
}
