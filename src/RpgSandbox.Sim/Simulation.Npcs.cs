using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

// NPC decisions: explicit rules in priority order. The first rule that applies wins, and the
// decision is recorded with its reason and the data it read. Rules issue ordinary commands.
internal sealed partial class Simulation
{
    private void DecideForFreeNpcs()
    {
        // Snapshot the list: deciding never adds actors, but keep iteration independent of mutation.
        foreach (var npc in World.Actors.Values.Where(a => !a.IsPlayer && a.CurrentAction is null).ToList())
            Decide(npc);
    }

    private void Decide(Actor npc)
    {
        var inputs = new List<string>
        {
            $"Luogo: {(npc.Location is { } here ? World.Locations[here].Name : "in viaggio")}",
            $"Razioni con sé: {npc.Food}",
        };

        var (rule, reason, command) = ChooseRaidStep(npc, inputs) ?? Routine(npc);
        var result = command is null ? null : Execute(command);
        if (result is { Success: false })
            reason += $" Comando rifiutato: {result.Message}";
        if (npc.CurrentAction is null)
        {
            // Rejected or impossible step (e.g. unreachable target): back off instead of idling forever,
            // since a free NPC with no deadline would never be asked to decide again.
            if (result is null)
                reason += " Nessuna azione possibile.";
            StartWait(npc.Id, Rules.DecisionRetryDelay, interruptible: true, description: "Esita");
        }
        npc.LastDecision = new DecisionTrace(World.Now, rule, reason, inputs.ToArray());
    }

    private (string Rule, string Reason, Command? Command)? ChooseRaidStep(Actor npc, List<string> inputs)
    {
        if (npc.Assignment is not { } raid || npc.Location is not { } here)
            return null;

        var home = World.Stores[raid.Home];
        var target = World.Stores[raid.Target];
        inputs.Add($"Incarico: razzia di {target.Name} per {World.Factions[raid.Faction].Name}");

        if (raid.TakeAttempted && npc.Food > 0)
        {
            if (here == home.Location)
                return ("Riporta il bottino", $"Consegna {npc.Food} razioni a {home.Name}.",
                    new DepositFoodCommand { Actor = npc.Id, Store = home.Id, Amount = npc.Food });
            return ("Torna al campo", $"Porta {npc.Food} razioni a {home.Name}.",
                TravelTowards(npc, home.Location));
        }

        if (raid.TakeAttempted)
        {
            npc.Assignment = null;
            World.RecordFact("RaidCompleted", $"{npc.Name} conclude la razzia a {target.Name}.");
            var (rule, reason, command) = Routine(npc);
            return ("Razzia conclusa", $"Incarico terminato. Poi: {rule.ToLowerInvariant()} — {reason}", command);
        }

        if (here != target.Location)
            return ("Raggiungi il bersaglio", $"Va verso {World.Locations[target.Location].Name} per la razzia.",
                TravelTowards(npc, target.Location));

        inputs.Add($"Razioni in {target.Name}: {target.Food}");
        return ("Ruba", $"Prende fino a {raid.Amount} razioni da {target.Name}.",
            new TakeFoodCommand { Actor = npc.Id, Store = target.Id, Amount = raid.Amount });
    }

    private (string Rule, string Reason, Command? Command) Routine(Actor npc)
    {
        // Routine is an interruptible wait so a faction assignment can pre-empt it.
        StartWait(npc.Id, Rules.IdleRestDuration, interruptible: true, description: "Riposa");
        return ("Routine: riposa", "Nessun incarico: riposa per un'ora.", null);
    }

    private TravelCommand? TravelTowards(Actor npc, LocationId destination)
    {
        var next = NextHop(npc.Location!.Value, destination);
        return next is null ? null : new TravelCommand { Actor = npc.Id, Destination = next.Value };
    }

    // ---------------------------------------------------------------- pathfinding (Dijkstra, deterministic ties)

    private Duration? PathCost(LocationId from, LocationId to) => ShortestPath(from, to)?.Cost;

    private LocationId? NextHop(LocationId from, LocationId to) => ShortestPath(from, to)?.FirstHop;

    private (Duration Cost, LocationId? FirstHop)? ShortestPath(LocationId from, LocationId to)
    {
        if (from == to)
            return (Duration.Zero, null);

        var best = new Dictionary<LocationId, (long Cost, LocationId FirstHop)>();
        var frontier = new SortedSet<(long Cost, LocationId Location, LocationId FirstHop)>();
        foreach (var ((a, b), time) in World.Routes)
        {
            if (a != from) continue;
            frontier.Add((time.Seconds, b, b));
        }

        while (frontier.Count > 0)
        {
            var (cost, location, firstHop) = frontier.Min;
            frontier.Remove(frontier.Min);
            if (best.ContainsKey(location) || location == from)
                continue;
            best[location] = (cost, firstHop);
            if (location == to)
                return (new Duration(cost), firstHop);
            foreach (var ((a, b), time) in World.Routes)
            {
                if (a == location && !best.ContainsKey(b))
                    frontier.Add((cost + time.Seconds, b, firstHop));
            }
        }
        return null;
    }
}
