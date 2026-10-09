using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

// NPC decisions: explicit rules in priority order. The first rule that applies wins, and the
// decision is recorded with its reason and the data it read. Rules act through the same handlers
// as the player's commands, and decide from what the NPC knows or can see where it stands.
internal sealed partial class Simulation
{
    /// <summary>A chosen rule: its name, why, and how to start the corresponding action (if any).</summary>
    private sealed record Plan(string Rule, string Reason, Func<CommandResult?> Act);

    private void DecideForFreeNpcs()
    {
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

        var plan = RaidStep(npc, inputs)
                   ?? OrganiseGuard(npc, inputs)
                   ?? GuardStep(npc, inputs)
                   ?? ReportStep(npc, inputs)
                   ?? Routine(npc);

        var reason = plan.Reason;
        var result = plan.Act();
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
        npc.LastDecision = new DecisionTrace(World.Now, plan.Rule, reason, inputs.ToArray());
    }

    // ---------------------------------------------------------------- raids (bandits)

    private Plan? RaidStep(Actor npc, List<string> inputs)
    {
        if (npc.Assignment is not { } raid || npc.Location is not { } here)
            return null;

        var home = World.Stores[raid.Home];
        var target = World.Stores[raid.Target];
        inputs.Add($"Incarico: razzia di {target.Name} per {World.Factions[raid.Faction].Name}");

        if (raid.Aborted)
        {
            if (here != home.Location)
                return Go("Torna al campo", $"Rientra a mani vuote: {target.Name} è sorvegliato.", npc, home.Location);

            // Only now, back home, does the faction learn that the target is guarded.
            var faction = World.Factions[raid.Faction];
            faction.AvoidUntil[target.Id] = World.Now.Plus(Rules.AvoidGuardedTarget);
            npc.Assignment = null;
            World.RecordFact("RaidAborted", $"{npc.Name} torna e riferisce ai {faction.Name}: {target.Name} è sorvegliato.");
            var next = Routine(npc);
            return next with { Rule = "Riferisce: bersaglio sorvegliato", Reason = $"Razzia annullata. Poi: {next.Reason}" };
        }

        if (raid.TakeAttempted && npc.Food > 0)
        {
            if (here == home.Location)
                return Do("Riporta il bottino", $"Consegna {npc.Food} razioni a {home.Name}.",
                    new DepositFoodCommand { Actor = npc.Id, Store = home.Id, Amount = npc.Food });
            return Go("Torna al campo", $"Porta {npc.Food} razioni a {home.Name}.", npc, home.Location);
        }

        if (raid.TakeAttempted)
        {
            npc.Assignment = null;
            World.RecordFact("RaidCompleted", $"{npc.Name} conclude la razzia a {target.Name}.");
            var next = Routine(npc);
            return next with { Rule = "Razzia conclusa", Reason = $"Incarico terminato. Poi: {next.Reason}" };
        }

        if (here != target.Location)
            return Go("Raggiungi il bersaglio", $"Va verso {World.Locations[target.Location].Name} per la razzia.", npc, target.Location);

        // On site the raider sees who is there: a guard on duty makes the theft impracticable.
        if (IsGuarded(target))
        {
            inputs.Add($"{target.Name} è sorvegliato");
            raid.Aborted = true;
            World.RecordFact("RaidDeterred", $"{npc.Name} vede la guardia a {target.Name} e desiste.");
            return Go("Desisti", $"{target.Name} è sorvegliato: torna al campo.", npc, home.Location);
        }

        inputs.Add($"Razioni in {target.Name}: {target.Food}");

        // On site the raider sees the store is empty: the attempt is over, go home empty-handed.
        if (target.Food == 0)
        {
            raid.TakeAttempted = true;
            npc.Assignment = null;
            World.RecordFact("RaidCompleted", $"{npc.Name} trova {target.Name} vuoto e rinuncia alla razzia.");
            var next = Routine(npc);
            return next with { Rule = "Bersaglio vuoto", Reason = $"{target.Name} è vuoto: razzia chiusa. Poi: {next.Reason}" };
        }

        var take = new TakeFoodCommand { Actor = npc.Id, Store = target.Id, Amount = raid.Amount };
        return new Plan("Ruba", $"Prende fino a {raid.Amount} razioni da {target.Name}.", () =>
        {
            // Any refusal still counts as the attempt: the raid must end rather than retry forever.
            var result = Execute(take);
            if (!result.Success)
                raid.TakeAttempted = true;
            return result;
        });
    }

    // ---------------------------------------------------------------- guarding (the authority)

    /// <summary>The faction's authority reacts to thefts it knows about by guarding the robbed store.</summary>
    private Plan? OrganiseGuard(Actor npc, List<string> inputs)
    {
        if (npc.Faction is not { } factionId || World.Factions[factionId].Authority != npc.Id)
            return null;

        var news = npc.Knowledge
            .Where(o => World.Stores[o.Store].Owner == factionId && !npc.ActedOn.Contains(o.Origin))
            .OrderBy(o => o.LearnedAt).ThenBy(o => o.Id)
            .FirstOrDefault();
        if (news is null)
            return null;

        npc.ActedOn.Add(news.Origin);
        var until = World.Now.Plus(Rules.GuardDutyLength);
        if (npc.GuardDuty is { } current && current.Store == news.Store)
        {
            npc.GuardDuty = new GuardDuty { Store = current.Store, Since = current.Since, Until = until };
        }
        else
        {
            npc.GuardDuty = new GuardDuty { Store = news.Store, Since = World.Now, Until = until };
            World.RecordFact("GuardDutyStarted", $"{npc.Name} decide di sorvegliare {news.StoreName}.");
        }

        inputs.Add($"Sa del furto da {news.StoreName} del {Clock(news.ObservedAt)}" +
                   (news.ThiefName is { } thief ? $" (ladro: {thief})" : " (ladro sconosciuto)") +
                   (news.Source is { } source ? $", gliel'ha detto {World.Actors[source].Name}" : ", l'ha visto di persona"));
        var step = GuardStep(npc, inputs)!;
        return step with
        {
            Rule = "Organizza il presidio",
            Reason = $"Furto ai danni del villaggio: sorveglia {news.StoreName} fino a {Clock(until)} del giorno {until.Day + 1}. {step.Reason}",
        };
    }

    private Plan? GuardStep(Actor npc, List<string> inputs)
    {
        if (npc.GuardDuty is not { } duty)
            return null;
        if (World.Now >= duty.Until)
        {
            npc.GuardDuty = null;
            World.RecordFact("GuardDutyEnded", $"{npc.Name} termina la sorveglianza di {World.Stores[duty.Store].Name}.");
            return null;
        }

        var store = World.Stores[duty.Store];
        inputs.Add($"Presidio di {store.Name} fino a {Clock(duty.Until)} del giorno {duty.Until.Day + 1}");
        if (npc.Location != store.Location)
            return Go("Raggiungi il presidio", $"Va a sorvegliare {store.Name}.", npc, store.Location);
        return new Plan("Presidia il deposito", $"Sorveglia {store.Name}.", () => StartGuard(npc, store, duty.Until));
    }

    // ---------------------------------------------------------------- reporting

    /// <summary>Members tell their faction's authority about thefts they know of and have not told yet.</summary>
    private Plan? ReportStep(Actor npc, List<string> inputs)
    {
        if (npc.Faction is not { } factionId || World.Factions[factionId].Authority is not { } authorityId || authorityId == npc.Id)
            return null;

        var untold = npc.Knowledge
            .Where(o => !o.ToldTo.Contains(authorityId))
            .OrderBy(o => o.LearnedAt).ThenBy(o => o.Id)
            .FirstOrDefault();
        if (untold is null)
            return null;

        var authority = World.Actors[authorityId];
        inputs.Add($"Sa del furto da {untold.StoreName} e non l'ha ancora detto a {authority.Name}");

        // The authority's post is common knowledge; where it is right now is only known by looking around.
        if (authority.Location == npc.Location)
            return Do("Riferisci il furto", $"Racconta a {authority.Name} del furto da {untold.StoreName}.",
                new ReportCommand { Actor = npc.Id, Recipient = authority.Id, Observation = untold.Id });
        if (authority.Home is { } post && post != npc.Location)
            return Go("Cerca la guardia", $"Va a {World.Locations[post].Name}, dove di solito sta {authority.Name}.", npc, post);
        inputs.Add($"{authority.Name} non è al suo posto");
        return null;
    }

    // ---------------------------------------------------------------- routine

    private Plan Routine(Actor npc)
    {
        var here = npc.Location!.Value;
        if (npc.Shift is { } shift && shift.Covers(World.Now))
        {
            if (here != shift.Location)
                return Go("Routine: va al lavoro", $"Turno a {World.Locations[shift.Location].Name}.", npc, shift.Location);
            var end = TodayAt(shift.End);
            return Rest("Routine: lavora", "Lavora fino alla fine del turno o per un'ora.", npc, "Lavora", end);
        }

        if (npc.Home is { } home && here != home)
            return Go("Routine: torna a casa", $"Rientra a {World.Locations[home].Name}.", npc, home);

        var nextShift = npc.Shift is { } s ? NextTimeOfDay(s.Start) : (GameTime?)null;
        return Rest("Routine: riposa", "Nessun incarico: riposa.", npc, "Riposa", nextShift);
    }

    /// <summary>An interruptible wait of at most an hour, cut at <paramref name="boundary"/> so schedules stay exact.</summary>
    private Plan Rest(string rule, string reason, Actor npc, string description, GameTime? boundary)
    {
        var length = Rules.IdleRestDuration;
        if (boundary is { } b && b > World.Now && b.Since(World.Now).Seconds < length.Seconds)
            length = b.Since(World.Now);
        return new Plan(rule, reason, () => StartWait(npc.Id, length, interruptible: true, description: description));
    }

    private GameTime TodayAt(Duration timeOfDay) => new(World.Now.Seconds / SecondsPerDay * SecondsPerDay + timeOfDay.Seconds);

    private GameTime NextTimeOfDay(Duration timeOfDay)
    {
        var today = TodayAt(timeOfDay);
        return today > World.Now ? today : today.Plus(Duration.FromDays(1));
    }

    private static string Clock(GameTime t) => $"{t.Hour:00}:{t.Minute:00}";

    private Plan Do(string rule, string reason, Command command) => new(rule, reason, () => Execute(command));

    private Plan Go(string rule, string reason, Actor npc, LocationId destination)
    {
        var next = NextHop(npc.Location!.Value, destination);
        return new Plan(rule, reason, () => next is null
            ? null
            : Execute(new TravelCommand { Actor = npc.Id, Destination = next.Value }));
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
