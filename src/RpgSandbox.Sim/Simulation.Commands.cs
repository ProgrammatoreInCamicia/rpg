using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

// Command handlers and action completions. Player and NPCs go through the same handlers:
// every precondition is validated before anything is mutated, and re-checked at completion.
internal sealed partial class Simulation
{
    public CommandResult Execute(Command command) => command switch
    {
        TravelCommand travel => StartTravel(travel),
        DepositFoodCommand deposit => StartDeposit(deposit),
        TakeFoodCommand take => StartTake(take),
        WaitCommand wait => StartWait(wait.Actor, wait.Duration, interruptible: false, description: null),
        ReportCommand report => StartReport(report),
        _ => CommandResult.Rejected(RejectionReason.UnknownCommand, $"Comando non supportato: {command.GetType().Name}."),
    };

    private CommandResult StartTravel(TravelCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (!World.Locations.TryGetValue(command.Destination, out var destination))
            return CommandResult.Rejected(RejectionReason.DestinationNotFound, $"Luogo sconosciuto: {command.Destination}.");
        if (actor.CurrentAction is not null || actor.Location is null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        var origin = actor.Location.Value;
        if (origin == destination.Id)
            return CommandResult.Rejected(RejectionReason.AlreadyThere, $"{actor.Name} è già a {destination.Name}.");
        if (!World.Routes.TryGetValue((origin, destination.Id), out var travelTime))
            return CommandResult.Rejected(RejectionReason.RouteNotFound,
                $"Nessun collegamento diretto da {World.Locations[origin].Name} a {destination.Name}.");

        var travel = new TravelAction
        {
            Id = World.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = World.Now,
            CompletesAt = World.Now.Plus(travelTime),
            Origin = origin,
            Destination = destination.Id,
            Description = $"In viaggio verso {destination.Name}",
        };
        actor.Location = null;
        Begin(actor, travel);
        World.RecordFact("TravelStarted", $"{actor.Name} parte da {World.Locations[origin].Name} verso {destination.Name}.");
        return CommandResult.Started(travel.Id, travel.CompletesAt, $"{actor.Name} si incammina verso {destination.Name}.");
    }

    private CommandResult StartDeposit(DepositFoodCommand command)
    {
        if (ValidateStoreAction(command.Actor, command.Store, command.Amount) is { } rejection)
            return rejection;
        var actor = World.Actors[command.Actor];
        var store = World.Stores[command.Store];
        if (command.Amount > actor.Food)
            return CommandResult.Rejected(RejectionReason.InsufficientFood, $"{actor.Name} ha solo {actor.Food} razioni.");
        if (store.Food > int.MaxValue - command.Amount)
            return CommandResult.Rejected(RejectionReason.CapacityExceeded, $"{store.Name} non può contenere altre {command.Amount} razioni.");

        var deposit = new DepositFoodAction
        {
            Id = World.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = World.Now,
            CompletesAt = World.Now.Plus(Tuning.DepositFoodDuration),
            Store = store.Id,
            Amount = command.Amount,
            Description = $"Consegna di {command.Amount} razioni a {store.Name}",
        };
        Begin(actor, deposit);
        return CommandResult.Started(deposit.Id, deposit.CompletesAt, $"{actor.Name} inizia a consegnare {command.Amount} razioni.");
    }

    private CommandResult StartTake(TakeFoodCommand command)
    {
        if (ValidateStoreAction(command.Actor, command.Store, command.Amount) is { } rejection)
            return rejection;
        var actor = World.Actors[command.Actor];
        var store = World.Stores[command.Store];
        if (IsTheft(actor, store) && IsGuarded(store))
            return CommandResult.Rejected(RejectionReason.StoreGuarded, $"{store.Name} è sorvegliato.");
        if (store.Food == 0)
            return CommandResult.Rejected(RejectionReason.StoreEmpty, $"{store.Name} è vuoto.");
        if (actor.Food == int.MaxValue)
            return CommandResult.Rejected(RejectionReason.CapacityExceeded, $"{actor.Name} non può portare altre razioni.");

        var take = new TakeFoodAction
        {
            Id = World.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = World.Now,
            CompletesAt = World.Now.Plus(Tuning.TakeFoodDuration),
            Store = store.Id,
            Amount = command.Amount,
            Description = IsTheft(actor, store)
                ? $"Furto di razioni da {store.Name}"
                : $"Prelievo di razioni da {store.Name}",
        };
        Begin(actor, take);
        return CommandResult.Started(take.Id, take.CompletesAt, $"{actor.Name} inizia a prendere razioni da {store.Name}.");
    }

    internal CommandResult StartWait(ActorId actorId, Duration duration, bool interruptible, string? description)
    {
        if (!World.Actors.TryGetValue(actorId, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {actorId}.");
        if (actor.CurrentAction is not null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        if (duration.Seconds <= 0 || duration.Seconds > long.MaxValue - World.Now.Seconds)
            return CommandResult.Rejected(RejectionReason.InvalidDuration, "La durata dell'attesa deve essere positiva.");

        var wait = new WaitAction
        {
            Id = World.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = World.Now,
            CompletesAt = World.Now.Plus(duration),
            Interruptible = interruptible,
            Description = description ?? "Attende",
        };
        Begin(actor, wait);
        return CommandResult.Started(wait.Id, wait.CompletesAt, $"{actor.Name} attende.");
    }

    private CommandResult? ValidateStoreAction(ActorId actorId, StoreId storeId, int amount)
    {
        if (!World.Actors.TryGetValue(actorId, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {actorId}.");
        if (!World.Stores.TryGetValue(storeId, out var store))
            return CommandResult.Rejected(RejectionReason.StoreNotFound, $"Deposito sconosciuto: {storeId}.");
        if (actor.CurrentAction is not null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        if (actor.Location != store.Location)
            return CommandResult.Rejected(RejectionReason.NotAtStore,
                $"{actor.Name} deve trovarsi a {World.Locations[store.Location].Name} per usare {store.Name}.");
        if (amount <= 0)
            return CommandResult.Rejected(RejectionReason.InvalidAmount, "La quantità deve essere almeno 1.");
        return null;
    }

    private static bool IsTheft(Actor actor, Store store) => store.Owner is not null && store.Owner != actor.Faction;

    private void Begin(Actor actor, PendingAction action)
    {
        actor.CurrentAction = action;
        World.Scheduler.Schedule(action.CompletesAt, new CompleteAction(action.Id));
    }

    // ---------------------------------------------------------------- completions

    /// <summary>
    /// Completes the action if it is still active. Returns false when it was cancelled earlier in the same
    /// instant: the batch of due entries was taken from the scheduler before the cancellation.
    /// </summary>
    private bool CompleteActionJob(ActionId actionId)
    {
        var actor = World.Actors.Values.FirstOrDefault(a => a.CurrentAction?.Id == actionId);
        if (actor is null)
            return false;

        var action = actor.CurrentAction!;
        actor.CurrentAction = null;
        switch (action)
        {
            case TravelAction travel:
                actor.Location = travel.Destination;
                actor.ArrivedAt = World.Now;
                World.RecordFact("TravelCompleted", $"{actor.Name} arriva a {World.Locations[travel.Destination].Name}.");
                break;
            case DepositFoodAction deposit:
                CompleteDeposit(actor, deposit);
                break;
            case TakeFoodAction take:
                CompleteTake(actor, take);
                break;
            case ReportAction report:
                CompleteReport(actor, report);
                break;
            case GuardAction:
            case WaitAction:
                break;
        }
        return true;
    }

    private void CompleteDeposit(Actor actor, DepositFoodAction deposit)
    {
        var store = World.Stores[deposit.Store];
        var failure =
            actor.Location != store.Location ? "non è più presso il deposito" :
            actor.Food < deposit.Amount ? "non ha più abbastanza razioni" :
            store.Food > int.MaxValue - deposit.Amount ? "il deposito non può contenerle" :
            null;
        if (failure is not null)
        {
            World.RecordFact("FoodDepositFailed",
                $"{actor.Name} non riesce a consegnare {deposit.Amount} razioni a {store.Name}: {failure}.");
            return;
        }

        actor.Food -= deposit.Amount;
        store.Food += deposit.Amount;
        World.RecordFact("FoodDeposited", $"{actor.Name} consegna {deposit.Amount} razioni a {store.Name} (ora {store.Food}).");
    }

    private void CompleteTake(Actor actor, TakeFoodAction take)
    {
        var store = World.Stores[take.Store];
        if (actor.Assignment is { } assignment && assignment.Target == store.Id)
            assignment.TakeAttempted = true;

        var theft = IsTheft(actor, store);
        if (theft && IsGuarded(store))
        {
            World.RecordFact("FoodTakeFailed", $"{actor.Name} rinuncia: {store.Name} ora è sorvegliato.");
            return;
        }

        // Never more than is there, nor more than the actor can carry without overflowing.
        var taken = actor.Location == store.Location ? Math.Min(Math.Min(take.Amount, store.Food), int.MaxValue - actor.Food) : 0;
        if (taken == 0)
        {
            World.RecordFact("FoodTakeFailed", $"{actor.Name} non trova razioni da prendere in {store.Name}.");
            return;
        }

        store.Food -= taken;
        actor.Food += taken;
        if (!theft)
        {
            World.RecordFact("FoodTaken", $"{actor.Name} prende {taken} razioni da {store.Name} (restano {store.Food}).");
            return;
        }

        var fact = World.RecordFact("FoodStolen", $"{actor.Name} ruba {taken} razioni da {store.Name} (restano {store.Food}).");
        PerceiveTheft(actor, take, store, taken, fact);
    }

    /// <summary>
    /// Perception at the moment of the deed: everyone present sees the theft; only those who were there
    /// since before it started recognise the thief. Observations copy what was perceived, nothing more.
    /// </summary>
    private void PerceiveTheft(Actor thief, TakeFoodAction take, Store store, int amount, FactId fact)
    {
        var witnesses = World.Actors.Values
            .Where(a => a.Id != thief.Id && a.Location == store.Location)
            .ToList();
        foreach (var witness in witnesses)
        {
            var recognised = witness.ArrivedAt <= take.StartedAt;
            var id = new ObservationId(World.NextObservationId++);
            witness.Knowledge.Add(new Observation
            {
                Id = id,
                Origin = id,
                Store = store.Id,
                StoreName = store.Name,
                Location = store.Location,
                Amount = amount,
                Thief = recognised ? thief.Id : null,
                ThiefName = recognised ? thief.Name : null,
                ObservedAt = World.Now,
                LearnedAt = World.Now,
                Fact = fact,
            });
            World.RecordFact("FoodTheftWitnessed", recognised
                ? $"{witness.Name} vede {thief.Name} rubare da {store.Name}."
                : $"{witness.Name} vede un furto da {store.Name}, ma non riconosce il ladro.");
            InterruptRoutine(witness);
        }
    }

    // ---------------------------------------------------------------- reports

    private CommandResult StartReport(ReportCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (!World.Actors.TryGetValue(command.Recipient, out var recipient) || recipient.Id == actor.Id)
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Destinatario sconosciuto: {command.Recipient}.");
        if (actor.CurrentAction is not null || actor.Location is null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        if (recipient.Location != actor.Location)
            return CommandResult.Rejected(RejectionReason.RecipientNotPresent, $"{recipient.Name} non è qui.");
        var observation = actor.Knowledge.FirstOrDefault(o => o.Id == command.Observation);
        if (observation is null)
            return CommandResult.Rejected(RejectionReason.UnknownObservation, $"{actor.Name} non sa nulla del genere.");

        var report = new ReportAction
        {
            Id = World.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = World.Now,
            CompletesAt = World.Now.Plus(Tuning.ReportDuration),
            Recipient = recipient.Id,
            Observation = observation.Id,
            Description = $"Racconta a {recipient.Name} del furto da {observation.StoreName}",
        };
        Begin(actor, report);
        return CommandResult.Started(report.Id, report.CompletesAt, $"{actor.Name} parla con {recipient.Name}.");
    }

    private void CompleteReport(Actor reporter, ReportAction report)
    {
        var recipient = World.Actors[report.Recipient];
        if (recipient.Location is null || recipient.Location != reporter.Location)
        {
            World.RecordFact("ReportFailed", $"{reporter.Name} non trova più {recipient.Name} per parlargli.");
            return;
        }

        // The content is the one chosen when the report started.
        var told = reporter.Knowledge.First(o => o.Id == report.Observation);
        told.ToldTo.Add(recipient.Id);

        // A rumour that comes back is not a second piece of evidence.
        if (recipient.Knowledge.Any(o => o.Origin == told.Origin))
        {
            World.RecordFact("InformationShared", $"{reporter.Name} racconta a {recipient.Name} del furto, ma lo sapeva già.");
            return;
        }

        recipient.Knowledge.Add(new Observation
        {
            Id = new ObservationId(World.NextObservationId++),
            Origin = told.Origin,
            Store = told.Store,
            StoreName = told.StoreName,
            Location = told.Location,
            Amount = told.Amount,
            Thief = told.Thief,
            ThiefName = told.ThiefName,
            ObservedAt = told.ObservedAt,
            LearnedAt = World.Now,
            Source = reporter.Id,
            Fact = told.Fact,
        });
        World.RecordFact("InformationShared",
            $"{reporter.Name} racconta a {recipient.Name} del furto da {told.StoreName}" +
            (told.ThiefName is { } thief ? $" ({thief})." : " (ladro sconosciuto)."));
        InterruptRoutine(recipient);
    }

    // ---------------------------------------------------------------- guarding

    private CommandResult StartGuard(Actor actor, Store store, GameTime until)
    {
        var end = World.Now.Plus(Tuning.GuardShift);
        if (until < end)
            end = until;
        var guard = new GuardAction
        {
            Id = World.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = World.Now,
            CompletesAt = end,
            Store = store.Id,
            Description = $"Sorveglia {store.Name}",
        };
        Begin(actor, guard);
        return CommandResult.Started(guard.Id, guard.CompletesAt, $"{actor.Name} sorveglia {store.Name}.");
    }

    /// <summary>A store is guarded when someone on guard duty for it is there.</summary>
    private bool IsGuarded(Store store) =>
        World.Actors.Values.Any(a => a.Location == store.Location && a.GuardDuty is { } duty && duty.Store == store.Id);

    /// <summary>Something new happened to this actor: cut a routine wait short so it decides now.</summary>
    private void InterruptRoutine(Actor actor)
    {
        if (actor.CurrentAction is WaitAction { Interruptible: true })
            CancelAction(actor);
    }

    /// <summary>Stops the actor's action without completing it, and removes its deadline: nothing is left behind.</summary>
    internal void CancelAction(Actor actor)
    {
        if (actor.CurrentAction is not { } action)
            return;
        World.Scheduler.Remove(action.Id);
        actor.CurrentAction = null;
    }
}
