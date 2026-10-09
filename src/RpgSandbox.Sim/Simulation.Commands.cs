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

        var deposit = new DepositFoodAction
        {
            Id = World.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = World.Now,
            CompletesAt = World.Now.Plus(Rules.DepositFoodDuration),
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
        if (store.Food == 0)
            return CommandResult.Rejected(RejectionReason.StoreEmpty, $"{store.Name} è vuoto.");

        var take = new TakeFoodAction
        {
            Id = World.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = World.Now,
            CompletesAt = World.Now.Plus(Rules.TakeFoodDuration),
            Store = store.Id,
            Amount = command.Amount,
            Description = IsTheft(actor, store)
                ? $"Furto di razioni da {store.Name}"
                : $"Prelievo di razioni da {store.Name}",
        };
        Begin(actor, take);
        return CommandResult.Started(take.Id, take.CompletesAt, $"{actor.Name} inizia a prendere razioni da {store.Name}.");
    }

    private CommandResult StartWait(ActorId actorId, Duration duration, bool interruptible, string? description)
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

    private void CompleteActionJob(ActionId actionId)
    {
        var actor = World.Actors.Values.FirstOrDefault(a => a.CurrentAction?.Id == actionId);
        if (actor is null)
            return; // Cancelled action: nothing to complete.

        var action = actor.CurrentAction!;
        actor.CurrentAction = null;
        switch (action)
        {
            case TravelAction travel:
                actor.Location = travel.Destination;
                World.RecordFact("TravelCompleted", $"{actor.Name} arriva a {World.Locations[travel.Destination].Name}.");
                break;
            case DepositFoodAction deposit:
                CompleteDeposit(actor, deposit);
                break;
            case TakeFoodAction take:
                CompleteTake(actor, take);
                break;
            case WaitAction:
                break;
        }
    }

    private void CompleteDeposit(Actor actor, DepositFoodAction deposit)
    {
        var store = World.Stores[deposit.Store];
        var failure =
            actor.Location != store.Location ? "non è più presso il deposito" :
            actor.Food < deposit.Amount ? "non ha più abbastanza razioni" :
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

        var taken = actor.Location == store.Location ? Math.Min(take.Amount, store.Food) : 0;
        if (taken == 0)
        {
            World.RecordFact("FoodTakeFailed", $"{actor.Name} non trova razioni da prendere in {store.Name}.");
            return;
        }

        store.Food -= taken;
        actor.Food += taken;
        if (IsTheft(actor, store))
            World.RecordFact("FoodStolen", $"{actor.Name} ruba {taken} razioni da {store.Name} (restano {store.Food}).");
        else
            World.RecordFact("FoodTaken", $"{actor.Name} prende {taken} razioni da {store.Name} (restano {store.Food}).");
    }
}
