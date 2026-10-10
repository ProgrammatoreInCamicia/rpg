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
            $"Luogo: {(npc.Location is { } here ? World.Locations[here].Name : npc.MapArea is not null ? "per strada" : "in viaggio")}",
            $"Razioni con sé: {npc.Food}",
        };

        UpdateVigil(npc, inputs);
        var plan = RaidStep(npc, inputs)
                   ?? ConfiscateStep(npc, inputs)
                   ?? ReturnStep(npc, inputs)
                   ?? OrganiseGuard(npc, inputs)
                   ?? GuardStep(npc, inputs)
                   ?? ReportStep(npc, inputs)
                   ?? VigilStep(npc, inputs)
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
            StartWait(npc.Id, Tuning.DecisionRetryDelay, interruptible: true, description: "Esita");
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
            faction.AvoidUntil[target.Id] = World.Now.Plus(Tuning.AvoidGuardedTarget);
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
        // A recognised thief owes the stolen rations: the authority will ask for them when it meets him.
        // One claim per theft, however many people saw it, and never again once it was made good.
        if (news.Thief is { } culprit && culprit != npc.Id && news.Fact is { } theft
            && !npc.SettledThefts.Contains(theft) && npc.Claims.All(c => c.Theft != theft))
            npc.Claims.Add(new Claim { Thief = culprit, Store = news.Store, Theft = theft, Owed = news.Amount });
        var until = World.Now.Plus(Tuning.GuardDutyLength);
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

    /// <summary>A known thief standing in front of the authority, still carrying rations: stop him and take them back.</summary>
    private Plan? ConfiscateStep(Actor npc, List<string> inputs)
    {
        if (npc.Location is not { } here)
            return null;
        foreach (var claim in npc.Claims)
        {
            var thief = World.Actors[claim.Thief];
            if (thief.Location != here || thief.Food == 0)
                continue;
            inputs.Add($"{thief.Name} è qui, ha {thief.Food} razioni e ne deve {claim.Owed} a {World.Stores[claim.Store].Name}");
            return new Plan("Ferma il ladro", $"Si fa restituire da {thief.Name} le razioni rubate.",
                () => StartConfiscate(npc, thief, claim));
        }
        return null;
    }

    /// <summary>Confiscated rations go back to the store they were stolen from.</summary>
    private Plan? ReturnStep(Actor npc, List<string> inputs)
    {
        // Only confiscated rations, to the store they were taken from; personal food is never touched.
        if (npc.Cargo.FirstOrDefault() is not { } cargo)
            return null;
        var store = World.Stores[cargo.Store];
        var amount = Math.Min(cargo.Amount, npc.Food);
        if (amount == 0)
        {
            npc.Cargo.Remove(cargo); // nothing left to carry back
            return null;
        }
        inputs.Add($"Ha con sé {cargo.Amount} razioni recuperate da restituire a {store.Name}");
        if (npc.Location != store.Location)
            return Go("Riporta le razioni", $"Riporta {amount} razioni a {store.Name}.", npc, store.Location);
        return Do("Riporta le razioni", $"Restituisce {amount} razioni a {store.Name}.",
            new DepositFoodCommand { Actor = npc.Id, Store = store.Id, Amount = amount });
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
        if (!IsAt(npc, store.Location, PostKind.Guard))
            return Go("Raggiungi il presidio", $"Va a sorvegliare {store.Name}.", npc, store.Location, PostKind.Guard);
        return new Plan("Presidia il deposito", $"Sorveglia {store.Name}.", () => StartGuard(npc, store, duty.Until));
    }

    // ---------------------------------------------------------------- reporting

    /// <summary>Members tell their faction's authority about thefts they know of and have not told yet.</summary>
    private Plan? ReportStep(Actor npc, List<string> inputs)
    {
        if (npc.Faction is not { } factionId || World.Factions[factionId].Authority is not { } authorityId || authorityId == npc.Id)
            return null;

        // Only first-hand testimony goes to the authority: nobody bothers the guard with hearsay.
        var untold = npc.Knowledge
            .Where(o => o.Source is null && !o.ToldTo.Contains(authorityId))
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

    // ---------------------------------------------------------------- vigilance (villagers who know of a theft)

    /// <summary>
    /// A member of the robbed faction (not its authority) who learns of a theft, first-hand or by hearsay,
    /// keeps an eye on the store for a while. Updates state only; <see cref="VigilStep"/> acts on it.
    /// </summary>
    private void UpdateVigil(Actor npc, List<string> inputs)
    {
        if (npc.Faction is not { } factionId || World.Factions[factionId].Authority == npc.Id)
            return;

        foreach (var news in npc.Knowledge
                     .Where(o => World.Stores[o.Store].Owner == factionId && !npc.ActedOn.Contains(o.Origin))
                     .OrderBy(o => o.LearnedAt).ThenBy(o => o.Id)
                     .ToList())
        {
            npc.ActedOn.Add(news.Origin);
            var until = World.Now.Plus(Tuning.VigilLength);
            if (npc.Vigil is null)
                World.RecordFact("VigilStarted", $"{npc.Name} decide di tenere d'occhio {news.StoreName}.");
            npc.Vigil = new Vigil { Store = news.Store, Until = until };
            inputs.Add($"Sa del furto da {news.StoreName}" +
                       (news.Source is { } source ? $" (gliel'ha detto {World.Actors[source].Name})" : " (l'ha visto)") +
                       $": vigila fino al giorno {until.Day + 1} {Clock(until)}");
        }
    }

    private Plan? VigilStep(Actor npc, List<string> inputs)
    {
        if (npc.Vigil is not { } vigil)
            return null;
        if (World.Now >= vigil.Until)
        {
            npc.Vigil = null;
            World.RecordFact("VigilEnded", $"{npc.Name} smette di tenere d'occhio {World.Stores[vigil.Store].Name}.");
            return null;
        }

        var tod = World.Now.Seconds % SecondsPerDay;
        if (tod < Tuning.VigilStart.Seconds || tod >= Tuning.VigilEnd.Seconds)
            return null;

        var store = World.Stores[vigil.Store];
        inputs.Add($"Vigila su {store.Name} fino al giorno {vigil.Until.Day + 1} {Clock(vigil.Until)}");
        if (npc.Location != store.Location)
            return Go("Vigila", $"Va a tenere d'occhio {store.Name}.", npc, store.Location);
        var end = TodayAt(Tuning.VigilEnd);
        if (vigil.Until < end)
            end = vigil.Until;
        return Rest("Vigila", $"Tiene d'occhio {store.Name}.", npc, "Vigila", end);
    }

    // ---------------------------------------------------------------- routine

    private Plan Routine(Actor npc)
    {
        // On a map "being there" means being on the place's post (T6c-2); off the maps, being in the place.
        if (npc.Shift is { } shift && shift.Covers(World.Now))
        {
            if (!IsAt(npc, shift.Location, PostKind.Work))
                return Go("Routine: va al lavoro", $"Turno a {World.Locations[shift.Location].Name}.", npc, shift.Location, PostKind.Work);
            var end = TodayAt(shift.End);
            return Rest("Routine: lavora", "Lavora fino alla fine del turno o per un'ora.", npc, "Lavora", end);
        }

        if (npc.Home is { } home && !IsAt(npc, home, PostKind.Home))
            return Go("Routine: torna a casa", $"Rientra a {World.Locations[home].Name}.", npc, home, PostKind.Home);

        // Wake up exactly when the next commitment starts: the work shift or, while vigilant, the watch.
        var nextShift = npc.Shift is { } s ? NextTimeOfDay(s.Start) : (GameTime?)null;
        var nextWatch = npc.Vigil is not null ? NextTimeOfDay(Tuning.VigilStart) : (GameTime?)null;
        var wake = nextShift is null ? nextWatch
            : nextWatch is null ? nextShift
            : nextShift < nextWatch ? nextShift : nextWatch;
        return Rest("Routine: riposa", "Nessun incarico: riposa.", npc, "Riposa", wake);
    }

    /// <summary>An interruptible wait of at most an hour, cut at <paramref name="boundary"/> so schedules stay exact.</summary>
    private Plan Rest(string rule, string reason, Actor npc, string description, GameTime? boundary)
    {
        var length = Tuning.IdleRestDuration;
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

    /// <summary>One step towards a place (and, on a map, a post of it): see <see cref="StepTowards"/>.</summary>
    private Plan Go(string rule, string reason, Actor npc, LocationId destination, PostKind? post = null) =>
        new(rule, reason, () => StepTowards(npc, destination, post) is { } step ? Execute(step) : null);

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
