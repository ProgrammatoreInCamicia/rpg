using System.Text.Json;
using System.Text.Json.Serialization;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim.Persistence;

/// <summary>A save that cannot be loaded. The message is meant for the player.</summary>
internal sealed class SaveGameException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// JSON snapshot of the whole world. Self-contained (no reference to the scenario), versioned, and
/// validated on load before anything is handed to the caller. Incompatible versions are rejected.
/// </summary>
internal static class SaveGame
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Write(WorldState world, Stream stream) =>
        JsonSerializer.Serialize(stream, ToDto(world), Options);

    public static WorldState Read(Stream stream)
    {
        SaveDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SaveDto>(stream, Options);
        }
        catch (JsonException e)
        {
            throw new SaveGameException("Il salvataggio è danneggiato o non è un salvataggio valido.", e);
        }
        if (dto is null)
            throw new SaveGameException("Il salvataggio è vuoto.");
        if (dto.Version != SchemaVersion)
            throw new SaveGameException($"Versione del salvataggio non supportata: {dto.Version} (attesa {SchemaVersion}).");

        try
        {
            return FromDto(dto);
        }
        catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or ArgumentException or NullReferenceException)
        {
            throw new SaveGameException($"Il salvataggio contiene dati incoerenti: {e.Message}", e);
        }
    }

    // ---------------------------------------------------------------- world -> dto

    private static SaveDto ToDto(WorldState w) => new()
    {
        Version = SchemaVersion,
        Now = w.Now.Seconds,
        Player = w.Player.Value,
        NextActionId = w.NextActionId,
        NextFactId = w.NextFactId,
        NextSequence = w.Scheduler.NextSequence,
        Areas = w.Areas.Values.Select(a => new AreaDto { Id = a.Id.Value, Name = a.Name }).ToList(),
        Locations = w.Locations.Values.Select(l => new LocationDto { Id = l.Id.Value, Name = l.Name, Area = l.Area.Value }).ToList(),
        Routes = w.Routes.Select(r => new RouteDto { From = r.Key.From.Value, To = r.Key.To.Value, Seconds = r.Value.Seconds }).ToList(),
        Factions = w.Factions.Values.Select(f => new FactionDto
        {
            Id = f.Id.Value,
            Name = f.Name,
            HomeStore = f.HomeStore?.Value,
            DailyUpkeep = f.DailyUpkeep,
            UpkeepTimeOfDay = f.UpkeepTimeOfDay.Seconds,
            Policy = f.Policy is { } p
                ? new RaidPolicyDto { FoodThreshold = p.FoodThreshold, RaidAmount = p.RaidAmount, EvaluationInterval = p.EvaluationInterval.Seconds }
                : null,
            NextEvaluation = f.NextEvaluation?.Seconds,
            LastDecision = ToDto(f.LastDecision),
        }).ToList(),
        Actors = w.Actors.Values.Select(a => new ActorDto
        {
            Id = a.Id.Value,
            Name = a.Name,
            IsPlayer = a.IsPlayer,
            Faction = a.Faction?.Value,
            Location = a.Location?.Value,
            Food = a.Food,
            Action = ToDto(a.CurrentAction),
            Assignment = a.Assignment is { } r
                ? new RaidAssignmentDto
                {
                    Faction = r.Faction.Value, Target = r.Target.Value, Home = r.Home.Value, Amount = r.Amount,
                    AssignedAt = r.AssignedAt.Seconds, TakeAttempted = r.TakeAttempted,
                }
                : null,
            LastDecision = ToDto(a.LastDecision),
        }).ToList(),
        Stores = w.Stores.Values.Select(s => new StoreDto
        {
            Id = s.Id.Value, Name = s.Name, Location = s.Location.Value, Owner = s.Owner?.Value, Food = s.Food,
        }).ToList(),
        Schedule = w.Scheduler.Entries.Select(e => new ScheduledDto
        {
            Due = e.Due.Seconds,
            Sequence = e.Sequence,
            Job = e.Job switch
            {
                CompleteAction c => "CompleteAction",
                FactionUpkeep => "FactionUpkeep",
                EvaluateFaction => "EvaluateFaction",
                _ => throw new InvalidOperationException($"Unknown job {e.Job}"),
            },
            Action = (e.Job as CompleteAction)?.Action.Value,
            Faction = e.Job switch { FactionUpkeep u => u.Faction.Value, EvaluateFaction v => v.Faction.Value, _ => null },
        }).ToList(),
        Facts = w.RecentFacts.Select(f => new FactDto { Id = f.Id.Value, At = f.At.Seconds, Kind = f.Kind, Description = f.Description }).ToList(),
    };

    private static DecisionDto? ToDto(DecisionTrace? t) => t is null
        ? null
        : new DecisionDto { At = t.At.Seconds, Rule = t.Rule, Reason = t.Reason, Inputs = t.Inputs.ToList() };

    private static ActionDto? ToDto(PendingAction? action)
    {
        ActionDto? dto = action switch
        {
            null => null,
            TravelAction t => new TravelActionDto { Origin = t.Origin.Value, Destination = t.Destination.Value },
            DepositFoodAction d => new DepositFoodActionDto { Store = d.Store.Value, Amount = d.Amount },
            TakeFoodAction k => new TakeFoodActionDto { Store = k.Store.Value, Amount = k.Amount },
            WaitAction wa => new WaitActionDto { Interruptible = wa.Interruptible },
            _ => throw new InvalidOperationException($"Unknown action {action.GetType().Name}"),
        };
        if (dto is null)
            return null;
        dto.Id = action!.Id.Value;
        dto.StartedAt = action.StartedAt.Seconds;
        dto.CompletesAt = action.CompletesAt.Seconds;
        dto.Description = action.Description;
        return dto;
    }

    // ---------------------------------------------------------------- dto -> world (validated)

    private static WorldState FromDto(SaveDto d)
    {
        var w = new WorldState { Player = new ActorId(Required(d.Player, "player")), Now = new GameTime(d.Now) };
        w.NextActionId = d.NextActionId;
        w.NextFactId = d.NextFactId;

        foreach (var a in d.Areas)
            w.Areas.Add(new AreaId(a.Id), new Area { Id = new AreaId(a.Id), Name = a.Name });
        foreach (var l in d.Locations)
        {
            Check(w.Areas.ContainsKey(new AreaId(l.Area)), $"luogo '{l.Id}' in area sconosciuta");
            w.Locations.Add(new LocationId(l.Id), new Location { Id = new LocationId(l.Id), Name = l.Name, Area = new AreaId(l.Area) });
        }
        foreach (var r in d.Routes)
        {
            Check(w.Locations.ContainsKey(new LocationId(r.From)) && w.Locations.ContainsKey(new LocationId(r.To)),
                $"percorso '{r.From}'-'{r.To}' con luogo sconosciuto");
            Check(r.Seconds > 0, "percorso di durata non positiva");
            w.Routes.Add((new LocationId(r.From), new LocationId(r.To)), new Duration(r.Seconds));
        }
        foreach (var s in d.Stores)
        {
            Check(w.Locations.ContainsKey(new LocationId(s.Location)), $"deposito '{s.Id}' in luogo sconosciuto");
            Check(s.Food >= 0, $"deposito '{s.Id}' con razioni negative");
            w.Stores.Add(new StoreId(s.Id), new Store
            {
                Id = new StoreId(s.Id), Name = s.Name, Location = new LocationId(s.Location),
                Owner = s.Owner is null ? null : new FactionId(s.Owner), Food = s.Food,
            });
        }
        foreach (var f in d.Factions)
        {
            w.Factions.Add(new FactionId(f.Id), new Faction
            {
                Id = new FactionId(f.Id),
                Name = f.Name,
                HomeStore = f.HomeStore is null ? null : new StoreId(f.HomeStore),
                DailyUpkeep = f.DailyUpkeep,
                UpkeepTimeOfDay = new Duration(f.UpkeepTimeOfDay),
                Policy = f.Policy is { } p ? new RaidPolicy(p.FoodThreshold, p.RaidAmount, new Duration(p.EvaluationInterval)) : null,
                NextEvaluation = f.NextEvaluation is { } next ? new GameTime(next) : null,
                LastDecision = FromDto(f.LastDecision),
            });
        }
        foreach (var store in w.Stores.Values)
            Check(store.Owner is null || w.Factions.ContainsKey(store.Owner.Value), $"deposito '{store.Id}' di fazione sconosciuta");
        foreach (var faction in w.Factions.Values)
            Check(faction.HomeStore is null || w.Stores.ContainsKey(faction.HomeStore.Value), $"fazione '{faction.Id}' con deposito sconosciuto");

        foreach (var a in d.Actors)
        {
            var actor = new Actor
            {
                Id = new ActorId(a.Id),
                Name = a.Name,
                IsPlayer = a.IsPlayer,
                Faction = a.Faction is null ? null : new FactionId(a.Faction),
                Location = a.Location is null ? null : new LocationId(a.Location),
                Food = a.Food,
                CurrentAction = FromDto(a.Action, new ActorId(a.Id)),
                LastDecision = FromDto(a.LastDecision),
                Assignment = a.Assignment is { } r
                    ? new RaidAssignment
                    {
                        Faction = new FactionId(r.Faction), Target = new StoreId(r.Target), Home = new StoreId(r.Home),
                        Amount = r.Amount, AssignedAt = new GameTime(r.AssignedAt), TakeAttempted = r.TakeAttempted,
                    }
                    : null,
            };
            ValidateActor(w, actor);
            w.Actors.Add(actor.Id, actor);
        }
        Check(w.Actors.TryGetValue(w.Player, out var player) && player.IsPlayer, "giocatore mancante");
        Check(w.Actors.Values.Count(a => a.IsPlayer) == 1, "più di un giocatore");

        var entries = d.Schedule.Select(e => new Scheduler.Entry(new GameTime(e.Due), e.Sequence, e.Job switch
        {
            "CompleteAction" => (ScheduledJob)new CompleteAction(new ActionId(e.Action ?? throw new InvalidDataException("azione mancante"))),
            "FactionUpkeep" => new FactionUpkeep(ExistingFaction(w, e.Faction)),
            "EvaluateFaction" => new EvaluateFaction(ExistingFaction(w, e.Faction)),
            _ => throw new InvalidDataException($"lavoro sconosciuto '{e.Job}'"),
        })).ToList();
        foreach (var entry in entries)
            Check(entry.Due >= w.Now, "scadenza nel passato");
        w.Scheduler = Scheduler.Restore(entries, d.NextSequence);

        // Every action in progress must still be due, or the actor would be stuck forever.
        foreach (var actor in w.Actors.Values)
        {
            if (actor.CurrentAction is { } action)
            {
                Check(entries.Any(e => e.Job is CompleteAction c && c.Action == action.Id && e.Due == action.CompletesAt),
                    $"azione di '{actor.Id}' senza scadenza");
                Check(action.Id.Value < w.NextActionId, "contatore delle azioni incoerente");
            }
        }

        foreach (var f in d.Facts)
            w.RecentFacts.AddLast(new Fact { Id = new FactId(f.Id), At = new GameTime(f.At), Kind = f.Kind, Description = f.Description });
        Check(w.RecentFacts.All(f => f.Id.Value < w.NextFactId), "contatore dei fatti incoerente");

        return w;
    }

    private static void ValidateActor(WorldState w, Actor a)
    {
        Check(a.Food >= 0, $"attore '{a.Id}' con razioni negative");
        Check(a.Faction is null || w.Factions.ContainsKey(a.Faction.Value), $"attore '{a.Id}' di fazione sconosciuta");
        Check(a.Location is null || w.Locations.ContainsKey(a.Location.Value), $"attore '{a.Id}' in luogo sconosciuto");
        Check((a.Location is null) == (a.CurrentAction is TravelAction), $"attore '{a.Id}': posizione e viaggio incoerenti");
        switch (a.CurrentAction)
        {
            case TravelAction t:
                Check(w.Routes.ContainsKey((t.Origin, t.Destination)), $"viaggio di '{a.Id}' su percorso inesistente");
                break;
            case DepositFoodAction dep:
                Check(w.Stores.ContainsKey(dep.Store), $"consegna di '{a.Id}' a deposito sconosciuto");
                break;
            case TakeFoodAction take:
                Check(w.Stores.ContainsKey(take.Store), $"prelievo di '{a.Id}' da deposito sconosciuto");
                break;
        }
        if (a.Assignment is { } r)
        {
            Check(w.Factions.ContainsKey(r.Faction) && w.Stores.ContainsKey(r.Target) && w.Stores.ContainsKey(r.Home),
                $"incarico di '{a.Id}' con riferimenti sconosciuti");
        }
    }

    private static PendingAction? FromDto(ActionDto? dto, ActorId actor)
    {
        if (dto is null)
            return null;
        var id = new ActionId(dto.Id);
        var started = new GameTime(dto.StartedAt);
        var completes = new GameTime(dto.CompletesAt);
        Check(completes > started, "azione di durata non positiva");
        var description = Required(dto.Description, "descrizione dell'azione");
        return dto switch
        {
            TravelActionDto t => new TravelAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                Origin = new LocationId(t.Origin), Destination = new LocationId(t.Destination),
            },
            DepositFoodActionDto dep => new DepositFoodAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                Store = new StoreId(dep.Store), Amount = dep.Amount,
            },
            TakeFoodActionDto take => new TakeFoodAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                Store = new StoreId(take.Store), Amount = take.Amount,
            },
            WaitActionDto wait => new WaitAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                Interruptible = wait.Interruptible,
            },
            _ => throw new InvalidDataException("tipo di azione sconosciuto"),
        };
    }

    private static DecisionTrace? FromDto(DecisionDto? d) =>
        d is null ? null : new DecisionTrace(new GameTime(d.At), d.Rule, d.Reason, d.Inputs.ToArray());

    private static FactionId ExistingFaction(WorldState w, string? id)
    {
        var faction = new FactionId(Required(id, "fazione"));
        Check(w.Factions.ContainsKey(faction), $"fazione sconosciuta '{id}'");
        return faction;
    }

    private static string Required(string? value, string what) =>
        string.IsNullOrEmpty(value) ? throw new InvalidDataException($"{what} mancante") : value;

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }
}
