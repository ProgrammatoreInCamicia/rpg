using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Sim.Api;

/// <summary>
/// The only entry point the client uses. It reads the world through views and changes it only
/// through commands. Time passes only through <see cref="Advance"/> and <see cref="AdvanceUntilCompleted"/>.
/// </summary>
public sealed class SimulationSession
{
    private readonly WorldState _world;

    private SimulationSession(WorldState world) => _world = world;

    public static SimulationSession Create(Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return new SimulationSession(WorldState.FromScenario(scenario));
    }

    public GameTime Now => _world.Now;
    public ActorId Player => _world.Player;

    /// <summary>Validates and starts the action requested by <paramref name="command"/>. Never advances time.</summary>
    public CommandResult Execute(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command switch
        {
            TravelCommand travel => StartTravel(travel),
            DepositFoodCommand deposit => StartDeposit(deposit),
            _ => CommandResult.Rejected(RejectionReason.UnknownCommand, $"Comando non supportato: {command.GetType().Name}."),
        };
    }

    /// <summary>
    /// Advances time by <paramref name="duration"/>, processing every deadline in between in order.
    /// Advance(a + b) is equivalent to Advance(a) followed by Advance(b).
    /// </summary>
    public void Advance(Duration duration)
    {
        var target = TargetAfter(duration);
        ProcessUntil(target, stopAfter: null);
        _world.Now = target;
    }

    /// <summary>
    /// Advances time until <paramref name="action"/> completes, processing every deadline in between
    /// (other actors keep acting). Stops at the end of the instant in which the action completes, or
    /// after <paramref name="maxWait"/>, whichever comes first.
    /// </summary>
    public AdvanceResult AdvanceUntilCompleted(ActionId action, Duration maxWait)
    {
        var limit = TargetAfter(maxWait);
        if (!_world.Scheduler.Contains(action))
            return new AdvanceResult { Outcome = AdvanceOutcome.NotPending, Now = _world.Now };

        if (ProcessUntil(limit, stopAfter: action))
            return new AdvanceResult { Outcome = AdvanceOutcome.Completed, Now = _world.Now };

        _world.Now = limit;
        return new AdvanceResult { Outcome = AdvanceOutcome.TimeLimitReached, Now = _world.Now };
    }

    public WorldView GetWorldView()
    {
        var w = _world;
        return new WorldView
        {
            Now = w.Now,
            Player = w.Player,
            Areas = Freeze(w.Areas.Values.Select(a => new AreaView { Id = a.Id, Name = a.Name })),
            Locations = Freeze(w.Locations.Values.Select(l => new LocationView { Id = l.Id, Name = l.Name, Area = l.Area })),
            Routes = Freeze(w.Routes.Select(r => new RouteView { From = r.Key.From, To = r.Key.To, TravelTime = r.Value })),
            Actors = Freeze(w.Actors.Values.Select(ToView)),
            Stores = Freeze(w.Stores.Values.Select(s => new StoreView { Id = s.Id, Name = s.Name, Location = s.Location, Food = s.Food })),
            RecentFacts = Freeze(w.RecentFacts.Select(f => new FactView { Id = f.Id, At = f.At, Kind = f.Kind, Description = f.Description })),
        };
    }

    private static ActorView ToView(Actor actor) => new()
    {
        Id = actor.Id,
        Name = actor.Name,
        IsPlayer = actor.IsPlayer,
        Location = actor.Location,
        Food = actor.Food,
        Action = actor.CurrentAction is { } a
            ? new ActionView { Id = a.Id, Kind = a.Kind, StartedAt = a.StartedAt, CompletesAt = a.CompletesAt, Description = a.Description }
            : null,
        Travel = actor.CurrentAction is TravelAction t
            ? new TravelView { Action = t.Id, Origin = t.Origin, Destination = t.Destination, DepartedAt = t.StartedAt, ArrivesAt = t.CompletesAt }
            : null,
    };

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());

    private GameTime TargetAfter(Duration duration)
    {
        if (duration.Seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Time cannot go backwards.");
        if (duration.Seconds > long.MaxValue - _world.Now.Seconds)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Time overflow.");
        return _world.Now.Plus(duration);
    }

    /// <summary>
    /// Processes deadlines up to and including <paramref name="target"/>, one instant at a time.
    /// Returns true if it stopped because <paramref name="stopAfter"/> completed.
    /// </summary>
    private bool ProcessUntil(GameTime target, ActionId? stopAfter)
    {
        while (_world.Scheduler.NextDue is { } due && due <= target)
        {
            _world.Now = due;
            var completedAwaited = false;

            // Phase 1: completions, in (due, sequence) order.
            foreach (var entry in _world.Scheduler.TakeDueAt(due))
            {
                Complete(entry.Action);
                completedAwaited |= entry.Action == stopAfter;
            }

            // Phases 2-4 (perception, faction policies, NPC decisions) arrive with increments 2-3.

            if (completedAwaited)
                return true;
        }
        return false;
    }

    private void Complete(ActionId actionId)
    {
        var actor = _world.Actors.Values.FirstOrDefault(a => a.CurrentAction?.Id == actionId);
        if (actor is null)
            return; // Cancelled action: nothing to complete.

        switch (actor.CurrentAction)
        {
            case TravelAction travel:
                actor.CurrentAction = null;
                actor.Location = travel.Destination;
                _world.RecordFact("TravelCompleted",
                    $"{actor.Name} arriva a {_world.Locations[travel.Destination].Name}.");
                break;

            case DepositFoodAction deposit:
                actor.CurrentAction = null;
                CompleteDeposit(actor, deposit);
                break;
        }
    }

    private void CompleteDeposit(Actor actor, DepositFoodAction deposit)
    {
        // Preconditions are checked again: the world may have changed since the action started.
        var store = _world.Stores[deposit.Store];
        var failure =
            actor.Location != store.Location ? "non è più presso il deposito" :
            actor.Food < deposit.Amount ? "non ha più abbastanza razioni" :
            null;
        if (failure is not null)
        {
            _world.RecordFact("FoodDepositFailed",
                $"{actor.Name} non riesce a consegnare {deposit.Amount} razioni a {store.Name}: {failure}.");
            return;
        }

        actor.Food -= deposit.Amount;
        store.Food += deposit.Amount;
        _world.RecordFact("FoodDeposited",
            $"{actor.Name} consegna {deposit.Amount} razioni a {store.Name} (ora {store.Food}).");
    }

    private CommandResult StartDeposit(DepositFoodCommand command)
    {
        // Validate everything before mutating anything.
        if (!_world.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (!_world.Stores.TryGetValue(command.Store, out var store))
            return CommandResult.Rejected(RejectionReason.StoreNotFound, $"Deposito sconosciuto: {command.Store}.");
        if (actor.CurrentAction is not null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        if (actor.Location != store.Location)
            return CommandResult.Rejected(RejectionReason.NotAtStore,
                $"{actor.Name} deve trovarsi a {_world.Locations[store.Location].Name} per consegnare a {store.Name}.");
        if (command.Amount <= 0)
            return CommandResult.Rejected(RejectionReason.InvalidAmount, "La quantità deve essere almeno 1.");
        if (command.Amount > actor.Food)
            return CommandResult.Rejected(RejectionReason.InsufficientFood,
                $"{actor.Name} ha solo {actor.Food} razioni.");

        var deposit = new DepositFoodAction
        {
            Id = _world.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = _world.Now,
            CompletesAt = _world.Now.Plus(Rules.DepositFoodDuration),
            Store = store.Id,
            Amount = command.Amount,
            Description = $"Consegna di {command.Amount} razioni a {store.Name}",
        };
        actor.CurrentAction = deposit;
        _world.Scheduler.Schedule(deposit.CompletesAt, deposit.Id);

        return CommandResult.Started(deposit.Id, deposit.CompletesAt,
            $"{actor.Name} inizia a consegnare {command.Amount} razioni.");
    }

    private CommandResult StartTravel(TravelCommand command)
    {
        // Validate everything before mutating anything.
        if (!_world.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (!_world.Locations.TryGetValue(command.Destination, out var destination))
            return CommandResult.Rejected(RejectionReason.DestinationNotFound, $"Luogo sconosciuto: {command.Destination}.");
        if (actor.CurrentAction is not null || actor.Location is null)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        var origin = actor.Location.Value;
        if (origin == destination.Id)
            return CommandResult.Rejected(RejectionReason.AlreadyThere, $"{actor.Name} è già a {destination.Name}.");
        if (!_world.Routes.TryGetValue((origin, destination.Id), out var travelTime))
            return CommandResult.Rejected(RejectionReason.RouteNotFound,
                $"Nessun collegamento diretto da {_world.Locations[origin].Name} a {destination.Name}.");

        var travel = new TravelAction
        {
            Id = _world.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = _world.Now,
            CompletesAt = _world.Now.Plus(travelTime),
            Origin = origin,
            Destination = destination.Id,
            Description = $"In viaggio verso {destination.Name}",
        };
        actor.Location = null;
        actor.CurrentAction = travel;
        _world.Scheduler.Schedule(travel.CompletesAt, travel.Id);
        _world.RecordFact("TravelStarted",
            $"{actor.Name} parte da {_world.Locations[origin].Name} verso {destination.Name}.");

        return CommandResult.Started(travel.Id, travel.CompletesAt,
            $"{actor.Name} si incammina verso {destination.Name}.");
    }
}
