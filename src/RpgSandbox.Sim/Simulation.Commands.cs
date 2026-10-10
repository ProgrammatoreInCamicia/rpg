using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// Command handlers and action completions. Player and NPCs go through the same handlers:
// every precondition is validated before anything is mutated, and re-checked at completion.
internal sealed partial class Simulation
{
    public CommandResult Execute(Command command)
    {
        var result = Dispatch(command);
        // Anything louder than a whisper ends sneaking (SRD Hide): talking, depositing, waiting... Taking food quietly,
        // stopping, a door or a torch do not (a lit torch gives you away by its light anyway); a normal walk ends it in StartMove.
        if (result.Success && command is not (MoveCommand or StopCommand or TakeFoodCommand or DoorCommand or TorchCommand)
            && World.Actors.TryGetValue(command.Actor, out var actor))
            actor.Sneak = null;
        return result;
    }

    private CommandResult Dispatch(Command command) => command switch
    {
        TravelCommand travel => StartTravel(travel),
        DepositFoodCommand deposit => StartDeposit(deposit),
        TakeFoodCommand take => StartTake(take),
        WaitCommand wait => StartWait(wait.Actor, wait.Duration, interruptible: false, description: null),
        ReportCommand report => StartReport(report),
        MoveCommand move => StartMove(move),
        StopCommand stop => Stop(stop),
        DoorCommand door => UseDoor(door),
        TorchCommand torch => UseTorch(torch),
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
        // T6c: a journey leaves a map only from the exit square of its place.
        if (MapOf(actor) is { } map && (!map.Exits.TryGetValue(origin, out var exit) || CurrentPosition(actor) != exit))
            return CommandResult.Rejected(RejectionReason.NotAtExit,
                $"Per partire verso {destination.Name} raggiungi l'uscita di {World.Locations[origin].Name}.");

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
        actor.Position = null; // off the map for the journey
        actor.MapArea = null;
        Begin(actor, travel);
        World.RecordFact("TravelStarted", $"{actor.Name} parte da {World.Locations[origin].Name} verso {destination.Name}.", actor.Id);
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

        // A theft is done as quietly as possible: one Stealth check when it starts, valid for the whole action
        // (ADAPTATION, see Perception). Heavy armor imposes Disadvantage.
        var theft = IsTheft(actor, store);
        D20Roll? stealth = null;
        if (theft)
        {
            stealth = D20.Roll(World.Rng, actor.Sheet.Bonus(Skill.Stealth), disadvantage: actor.Sheet.StealthDisadvantage);
            World.RecordFact("Roll", $"{actor.Name}, Furtività: {stealth.Describe()} ({Perception.Describe(Perception.LightAt(World.Locations[store.Location], World.Now))}).", actor.Id);
        }

        var take = new TakeFoodAction
        {
            Id = World.AllocateActionId(),
            Actor = actor.Id,
            StartedAt = World.Now,
            CompletesAt = World.Now.Plus(Tuning.TakeFoodDuration),
            Store = store.Id,
            Amount = command.Amount,
            StealthTotal = stealth?.Total,
            SeenAtStart = theft ? WhoSeesOnMap(actor) : new HashSet<ActorId>(),
            BestLight = theft && MapOf(actor) is { } lightMap && CurrentPosition(actor) is { } spot ? LightOn(lightMap, spot) : null,
            Description = theft ? $"Furto di razioni da {store.Name}" : $"Prelievo di razioni da {store.Name}",
        };
        Begin(actor, take);
        // The actor knows how quiet it managed to be, never whether someone noticed.
        var how = stealth is null ? "" : $" Furtività: {stealth.Describe()}.";
        return CommandResult.Started(take.Id, take.CompletesAt, $"{actor.Name} inizia a prendere razioni da {store.Name}.{how}");
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
        if (!InReach(actor, store))
            return store.Position is not null
                ? CommandResult.Rejected(RejectionReason.OutOfReach, $"{actor.Name} deve avvicinarsi a {store.Name}.")
                : CommandResult.Rejected(RejectionReason.NotAtStore,
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
                World.RecordFact("TravelCompleted", $"{actor.Name} arriva a {World.Locations[travel.Destination].Name}.", actor.Id);
                if (World.Maps.TryGetValue(World.Locations[travel.Destination].Area, out var arrivalMap))
                {
                    // T6c: onto a map, on the exit square of the place; the arrival is noticed with the contacts.
                    actor.MapArea = arrivalMap.Area;
                    Place(actor, arrivalMap.Exits[travel.Destination]);
                    break;
                }
                actor.Location = travel.Destination;
                actor.ArrivedAt = World.Now;
                NoticeArrival(actor);
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
            case ConfiscateAction confiscate:
                CompleteConfiscate(actor, confiscate);
                break;
            case MoveAction move:
                Place(actor, move.Path[^1]); // contacts follow once the instant's positions are all known
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
            !InReach(actor, store) ? "non è più presso il deposito" :
            actor.Food < deposit.Amount ? "non ha più abbastanza razioni" :
            store.Food > int.MaxValue - deposit.Amount ? "il deposito non può contenerle" :
            null;
        if (failure is not null)
        {
            World.RecordFact("FoodDepositFailed",
                $"{actor.Name} non riesce a consegnare {deposit.Amount} razioni a {store.Name}: {failure}.", actor.Id);
            return;
        }

        actor.Food -= deposit.Amount;
        store.Food += deposit.Amount;
        // Rations carried back on someone else's behalf are delivered first.
        if (actor.Cargo.FirstOrDefault(c => c.Store == store.Id) is { } cargo)
        {
            cargo.Amount -= Math.Min(cargo.Amount, deposit.Amount);
            if (cargo.Amount == 0)
                actor.Cargo.Remove(cargo);
        }
        World.RecordFact("FoodDeposited", $"{actor.Name} consegna {deposit.Amount} razioni a {store.Name} (ora {store.Food}).", actor.Id);
    }

    private void CompleteTake(Actor actor, TakeFoodAction take)
    {
        var store = World.Stores[take.Store];
        if (actor.Assignment is { } assignment && assignment.Target == store.Id)
            assignment.TakeAttempted = true;

        var theft = IsTheft(actor, store);
        if (theft && IsGuarded(store))
        {
            World.RecordFact("FoodTakeFailed", $"{actor.Name} rinuncia: {store.Name} ora è sorvegliato.", actor.Id);
            return;
        }

        // Never more than is there, nor more than the actor can carry without overflowing.
        var taken = InReach(actor, store) ? Math.Min(Math.Min(take.Amount, store.Food), int.MaxValue - actor.Food) : 0;
        if (taken == 0)
        {
            World.RecordFact("FoodTakeFailed", $"{actor.Name} non trova razioni da prendere in {store.Name}.", actor.Id);
            return;
        }

        store.Food -= taken;
        actor.Food += taken;
        if (!theft)
        {
            World.RecordFact("FoodTaken", $"{actor.Name} prende {taken} razioni da {store.Name} (restano {store.Food}).", actor.Id);
            return;
        }

        var fact = World.RecordFact("FoodStolen", $"{actor.Name} ruba {taken} razioni da {store.Name} (restano {store.Food}).", actor.Id);
        PerceiveTheft(actor, take, store, taken, fact);
    }

    /// <summary>
    /// Perception at the moment of the deed: everyone present sees the theft; only those who were there
    /// since before it started recognise the thief. Observations copy what was perceived, nothing more.
    /// </summary>
    private void PerceiveTheft(Actor thief, TakeFoodAction take, Store store, int amount, FactId fact)
    {
        // On a map, sight and hearing follow walls, light and distance (T6b).
        if (store.Position is not null && thief.MapArea is { } area && World.Maps.TryGetValue(area, out var map))
        {
            PerceiveTheftOnMap(thief, take, store, amount, fact, map);
            return;
        }

        var place = World.Locations[store.Location];
        var stealth = take.StealthTotal ?? 0;
        var witnesses = World.Actors.Values
            .Where(a => a.Id != thief.Id && a.Location == store.Location)
            .ToList();
        foreach (var witness in witnesses)
        {
            // Judged by the best light during the part of the theft this witness was there for.
            var watchedFrom = witness.ArrivedAt > take.StartedAt ? witness.ArrivedAt : take.StartedAt;
            var light = Perception.BestLightDuring(place, watchedFrom, World.Now);
            var seen = Perception.Witness(witness.Sheet, light, stealth);
            if (!seen.Noticed)
            {
                World.RecordFact("FoodTheftUnnoticed",
                    $"{witness.Name} non si accorge di nulla ({Perception.Describe(light)}: vista {seen.SightPerception}, " +
                    $"udito {seen.HearingPerception} < Furtività {stealth}).");
                continue;
            }

            // Recognising takes seeing the thief, and having been there since the theft began.
            var recognised = seen.SawActor && witness.ArrivedAt <= take.StartedAt;
            Witnessed(witness, thief, store, amount, fact, recognised, seen.Mode, light);
        }
    }

    /// <summary>A witness noticed a theft: it keeps a self-contained observation, and stops resting to react.</summary>
    private void Witnessed(Actor witness, Actor thief, Store store, int amount, FactId fact, bool recognised, PerceptionMode mode, Light light)
    {
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
            Perceived = mode,
        });
        World.RecordFact("FoodTheftWitnessed",
            recognised ? $"{witness.Name} vede {thief.Name} rubare da {store.Name}."
            : mode == PerceptionMode.Seen ? $"{witness.Name} vede un furto da {store.Name}, ma non riconosce il ladro."
            : $"{witness.Name} sente qualcuno rubare da {store.Name} nel {Perception.Describe(light)}, ma non vede chi.");
        InterruptRoutine(witness);
    }

    // ---------------------------------------------------------------- reports

    private CommandResult StartReport(ReportCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (!World.Actors.TryGetValue(command.Recipient, out var recipient) || recipient.Id == actor.Id)
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Destinatario sconosciuto: {command.Recipient}.");
        if (actor.CurrentAction is not null || (actor.Location is null && actor.Position is null))
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        if (!CanTalk(actor, recipient))
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
        if (!CanTalk(reporter, recipient))
        {
            World.RecordFact("ReportFailed", $"{reporter.Name} non trova più {recipient.Name} per parlargli.", reporter.Id, recipient.Id);
            return;
        }

        // The content is the one chosen when the report started.
        var told = reporter.Knowledge.First(o => o.Id == report.Observation);
        told.ToldTo.Add(recipient.Id);

        // A rumour that comes back is not a second piece of evidence.
        if (recipient.Knowledge.Any(o => o.Origin == told.Origin))
        {
            World.RecordFact("InformationShared", $"{reporter.Name} racconta a {recipient.Name} del furto, ma lo sapeva già.", reporter.Id, recipient.Id);
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
            Perceived = told.Perceived,
            LearnedAt = World.Now,
            Source = reporter.Id,
            Fact = told.Fact,
        });
        World.RecordFact("InformationShared",
            $"{reporter.Name} racconta a {recipient.Name} del furto da {told.StoreName}" +
            (told.ThiefName is { } thief ? $" ({thief})." : " (ladro sconosciuto)."), reporter.Id, recipient.Id);
        InterruptRoutine(recipient);
    }

    // ---------------------------------------------------------------- confiscation

    /// <summary>The authority asks a known thief, standing in front of it, to hand back what is owed.</summary>
    private CommandResult StartConfiscate(Actor authority, Actor thief, Claim claim)
    {
        var confiscate = new ConfiscateAction
        {
            Id = World.AllocateActionId(),
            Actor = authority.Id,
            StartedAt = World.Now,
            CompletesAt = World.Now.Plus(Tuning.ConfiscateDuration),
            Target = thief.Id,
            Store = claim.Store,
            Description = $"Si fa restituire da {thief.Name} le razioni rubate",
        };
        Begin(authority, confiscate);
        return CommandResult.Started(confiscate.Id, confiscate.CompletesAt, $"{authority.Name} ferma {thief.Name}.");
    }

    /// <summary>
    /// Takes back at most what is still owed and what the thief still carries: never more, never from afar.
    /// </summary>
    private void CompleteConfiscate(Actor authority, ConfiscateAction confiscate)
    {
        var thief = World.Actors[confiscate.Target];
        var claim = authority.Claims.FirstOrDefault(c => c.Thief == thief.Id && c.Store == confiscate.Store);
        if (claim is null || thief.Location is null || thief.Location != authority.Location)
        {
            World.RecordFact("ConfiscationFailed", $"{authority.Name} non riesce a fermare {thief.Name}.", authority.Id, thief.Id);
            return;
        }

        var taken = Math.Min(Math.Min(claim.Owed, thief.Food), int.MaxValue - authority.Food);
        if (taken == 0)
        {
            World.RecordFact("ConfiscationFailed", $"{thief.Name} non ha più le razioni rubate.", authority.Id, thief.Id);
            return;
        }

        thief.Food -= taken;
        authority.Food += taken;
        claim.Owed -= taken;
        if (claim.Owed == 0)
        {
            authority.Claims.Remove(claim);
            authority.SettledThefts.Add(claim.Theft);
        }
        var cargo = authority.Cargo.FirstOrDefault(c => c.Store == claim.Store);
        if (cargo is null)
            authority.Cargo.Add(new Cargo { Store = claim.Store, Amount = taken });
        else
            cargo.Amount += taken;
        World.RecordFact("FoodConfiscated",
            $"{authority.Name} si fa restituire da {thief.Name} {taken} razioni rubate" +
            (claim.Owed > 0 ? $" (ne mancano {claim.Owed})." : "."), authority.Id, thief.Id);
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

    /// <summary>
    /// Arrival is contact: an authority on watch or resting here that has an open claim on the newcomer, who carries
    /// rations, stops what it is doing and decides at once. It never learns where a thief is from afar.
    /// </summary>
    private void NoticeArrival(Actor newcomer)
    {
        if (newcomer.Food == 0)
            return;
        foreach (var authority in World.Actors.Values.Where(a => a.Id != newcomer.Id && a.Location == newcomer.Location).ToList())
        {
            if (authority.Claims.Any(c => c.Thief == newcomer.Id && c.Owed > 0)
                && authority.CurrentAction is GuardAction or WaitAction { Interruptible: true })
                CancelAction(authority);
        }
    }

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
        // A walker stops on the last square it reached.
        if (action is MoveAction move)
            Reach(actor, move.PositionAt(World.Now));
    }
}
