using System.Text.Json;
using System.Text.Json.Serialization;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim.Persistence;

/// <summary>A save that cannot be loaded. The message is meant for the player.</summary>
internal sealed class SaveGameException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// JSON snapshot of the whole world. Self-contained (no reference to the scenario), versioned, and
/// validated on load before anything is handed to the caller. Incompatible versions are rejected.
/// </summary>
internal static class SaveGame
{
    public const int SchemaVersion = 12;

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
        // NotSupportedException: a polymorphic object without (or with an unusable) type discriminator.
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            throw new SaveGameException("Il salvataggio è danneggiato o non è un salvataggio valido.", e);
        }
        if (dto is null)
            throw new SaveGameException("Il salvataggio è vuoto.");
        if (dto.Version != SchemaVersion)
            throw new SaveGameException($"Versione del salvataggio non supportata: {dto.Version} (attesa {SchemaVersion}).");

        // Validation is explicit: every rule below raises InvalidDataException with a readable reason.
        // Any other exception is a programming error and is deliberately not disguised as a bad save.
        try
        {
            ValidateShape(dto);
            return FromDto(dto);
        }
        catch (InvalidDataException e)
        {
            throw new SaveGameException($"Il salvataggio contiene dati incoerenti: {e.Message}", e);
        }
    }

    /// <summary>Structural checks: no missing collections, no null entries, no empty identifiers.</summary>
    private static void ValidateShape(SaveDto d)
    {
        Required(d.Player, "giocatore");
        foreach (var (list, what) in new (System.Collections.IList?, string)[]
                 {
                     (d.Areas, "aree"), (d.Locations, "luoghi"), (d.Routes, "percorsi"), (d.Factions, "fazioni"),
                     (d.Actors, "attori"), (d.Stores, "depositi"), (d.Schedule, "scadenze"), (d.Facts, "cronaca"),
                 })
        {
            Check(list is not null, $"elenco delle {what} mancante");
            foreach (var item in list!)
                Check(item is not null, $"voce vuota nell'elenco delle {what}");
        }

        foreach (var a in d.Areas) { Required(a.Id, "id di un'area"); Required(a.Name, "nome di un'area"); }
        foreach (var l in d.Locations) { Required(l.Id, "id di un luogo"); Required(l.Name, "nome di un luogo"); Required(l.Area, "area di un luogo"); }
        foreach (var r in d.Routes) { Required(r.From, "origine di un percorso"); Required(r.To, "destinazione di un percorso"); }
        foreach (var s in d.Stores) { Required(s.Id, "id di un deposito"); Required(s.Name, "nome di un deposito"); Required(s.Location, "luogo di un deposito"); }
        foreach (var f in d.Factions)
        {
            Required(f.Id, "id di una fazione");
            Required(f.Name, "nome di una fazione");
            Check(f.AvoidUntil is not null && f.AvoidUntil.All(x => x is not null && !string.IsNullOrEmpty(x.Store)),
                $"bersagli evitati di '{f.Id}' non validi");
            CheckDecision(f.LastDecision, f.Id);
        }
        foreach (var a in d.Actors)
        {
            Required(a.Id, "id di un attore");
            Required(a.Name, "nome di un attore");
            Check(a.Knowledge is not null && a.Knowledge.All(o => o is not null && o.ToldTo is not null && o.ToldTo.All(t => !string.IsNullOrEmpty(t))),
                $"conoscenze di '{a.Id}' non valide");
            Check(a.ActedOn is not null, $"elenco ActedOn di '{a.Id}' mancante");
            if (a.Assignment is { } r)
                Check(new[] { r.Faction, r.Target, r.Home }.All(x => !string.IsNullOrEmpty(x)), $"incarico di '{a.Id}' incompleto");
            if (a.Shift is { } sh)
                Required(sh.Location, $"luogo di lavoro di '{a.Id}'");
            if (a.GuardDuty is { } g)
                Required(g.Store, $"deposito sorvegliato da '{a.Id}'");
            if (a.Vigil is { } v)
                Required(v.Store, $"deposito tenuto d'occhio da '{a.Id}'");
            Check(a.Sheet is not null && a.Sheet.SkillProficiencies is not null && !string.IsNullOrEmpty(a.Sheet.Title),
                $"scheda di '{a.Id}' mancante o incompleta");
            CheckDecision(a.LastDecision, a.Id);
        }
        foreach (var f in d.Facts) { Required(f.Kind, "tipo di un fatto"); Check(f.Description is not null, "descrizione di un fatto mancante"); Check(f.Participants is not null && f.Participants.All(p => !string.IsNullOrEmpty(p)), "partecipanti di un fatto non validi"); }
        foreach (var e in d.Schedule) Required(e.Job, "tipo di una scadenza");
    }

    private static void CheckDecision(DecisionDto? decision, string owner) =>
        Check(decision is null || (decision.Rule is not null && decision.Reason is not null && decision.Inputs is not null && decision.Inputs.All(i => i is not null)),
            $"decisione di '{owner}' non valida");

    // ---------------------------------------------------------------- world -> dto

    private static SaveDto ToDto(WorldState w) => new()
    {
        Version = SchemaVersion,
        Now = w.Now.Seconds,
        Player = w.Player.Value,
        NextActionId = w.NextActionId,
        NextFactId = w.NextFactId,
        NextSequence = w.Scheduler.NextSequence,
        NextObservationId = w.NextObservationId,
        RngAlgorithm = SplitMix64.Algorithm,
        RngState = w.Rng.State,
        Areas = w.Areas.Values.Select(a => new AreaDto { Id = a.Id.Value, Name = a.Name }).ToList(),
        Locations = w.Locations.Values.Select(l => new LocationDto { Id = l.Id.Value, Name = l.Name, Area = l.Area.Value, Lit = l.Lit }).ToList(),
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
            Authority = f.Authority?.Value,
            AvoidUntil = f.AvoidUntil.Select(x => new AvoidDto { Store = x.Key.Value, Until = x.Value.Seconds }).ToList(),
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
                    AssignedAt = r.AssignedAt.Seconds, TakeAttempted = r.TakeAttempted, Aborted = r.Aborted,
                }
                : null,
            LastDecision = ToDto(a.LastDecision),
            Home = a.Home?.Value,
            Shift = a.Shift is { } sh ? new WorkShiftDto { Location = sh.Location.Value, Start = sh.Start.Seconds, End = sh.End.Seconds } : null,
            ArrivedAt = a.ArrivedAt.Seconds,
            GuardDuty = a.GuardDuty is { } g ? new GuardDutyDto { Store = g.Store.Value, Since = g.Since.Seconds, Until = g.Until.Seconds } : null,
            Vigil = a.Vigil is { } v ? new VigilDto { Store = v.Store.Value, Until = v.Until.Seconds } : null,
            Sheet = ToDto(a.Sheet),
            Claims = a.Claims.Select(c => new ClaimDto { Thief = c.Thief.Value, Store = c.Store.Value, Theft = c.Theft.Value, Owed = c.Owed }).ToList(),
            SettledThefts = a.SettledThefts.Select(t => t.Value).ToList(),
            Cargo = a.Cargo.Select(c => new CargoDto { Store = c.Store.Value, Amount = c.Amount }).ToList(),
            Position = a.Position is { } p ? new PosDto { X = p.X, Y = p.Y } : null,
            MapArea = a.MapArea?.Value,
            Sneak = a.Sneak,
            Torches = a.Torches,
            TorchLitUntil = a.TorchLitUntil?.Seconds,
            Knowledge = a.Knowledge.Select(ToDto).ToList(),
            ActedOn = a.ActedOn.Select(o => o.Value).ToList(),
        }).ToList(),
        Stores = w.Stores.Values.Select(s => new StoreDto
        {
            Id = s.Id.Value, Name = s.Name, Location = s.Location.Value, Owner = s.Owner?.Value, Food = s.Food,
            Position = s.Position is { } sp ? new PosDto { X = sp.X, Y = sp.Y } : null,
            Access = s.Access.Select(a => new PosDto { X = a.X, Y = a.Y }).ToList(),
        }).ToList(),
        Schedule = w.Scheduler.Entries.Select(e => new ScheduledDto
        {
            Due = e.Due.Seconds,
            Sequence = e.Sequence,
            Job = e.Job switch
            {
                CompleteAction c => "CompleteAction",
                MoveWaypoint => "MoveWaypoint",
                FactionUpkeep => "FactionUpkeep",
                EvaluateFaction => "EvaluateFaction",
                _ => throw new InvalidOperationException($"Unknown job {e.Job}"),
            },
            Action = e.Job switch { CompleteAction c => c.Action.Value, MoveWaypoint w => w.Action.Value, _ => (long?)null },
            Step = (e.Job as MoveWaypoint)?.Step,
            Faction = e.Job switch { FactionUpkeep u => u.Faction.Value, EvaluateFaction v => v.Faction.Value, _ => null },
        }).ToList(),
        Facts = w.RecentFacts.Select(f => new FactDto { Id = f.Id.Value, At = f.At.Seconds, Kind = f.Kind, Description = f.Description, Participants = f.Participants.Select(p => p.Value).ToList() }).ToList(),
        Maps = w.Maps.Values.Select(m => new MapDto
        {
            Area = m.Area.Value, Rows = m.Rows.ToList(), Zones = m.Zones.ToDictionary(z => z.Key.ToString(), z => z.Value.Value),
            Lights = m.Lights.Select(l => new LightDto { X = l.At.X, Y = l.At.Y, Bright = l.BrightFeet, Dim = l.DimFeet }).ToList(),
            Doors = m.Doors.Select(d => new DoorDto { X = d.Key.X, Y = d.Key.Y, Open = d.Value }).ToList(),
            Exits = m.Exits.Select(e => new ExitDto { Location = e.Key.Value, X = e.Value.X, Y = e.Value.Y }).ToList(),
            Posts = m.Posts.Select(p => new PostDto { Location = p.Key.Location.Value, Kind = p.Key.Kind.ToString(), X = p.Value.X, Y = p.Value.Y }).ToList(),
        }).ToList(),
    };

    private static SheetDto ToDto(CharacterSheet s) => new()
    {
        Title = s.Title, Strength = s.Strength, Dexterity = s.Dexterity, Constitution = s.Constitution,
        Intelligence = s.Intelligence, Wisdom = s.Wisdom, Charisma = s.Charisma, ProficiencyBonus = s.ProficiencyBonus,
        SkillProficiencies = s.SkillProficiencies.Select(k => k.ToString()).ToList(), ArmorClass = s.ArmorClass,
        StealthDisadvantage = s.StealthDisadvantage,
        Speed = s.Speed,
    };

    private static CharacterSheet FromDto(SheetDto s) => new()
    {
        Title = s.Title, Strength = s.Strength, Dexterity = s.Dexterity, Constitution = s.Constitution,
        Intelligence = s.Intelligence, Wisdom = s.Wisdom, Charisma = s.Charisma, ProficiencyBonus = s.ProficiencyBonus,
        SkillProficiencies = s.SkillProficiencies
            .Select(k => Enum.TryParse<Skill>(k, out var skill) && Enum.IsDefined(skill) ? skill : throw new InvalidDataException($"abilità sconosciuta '{k}'"))
            .ToArray(),
        ArmorClass = s.ArmorClass,
        StealthDisadvantage = s.StealthDisadvantage,
        Speed = s.Speed,
    };

    private static ObservationDto ToDto(Observation o) => new()
    {
        Id = o.Id.Value, Origin = o.Origin.Value, Store = o.Store.Value, StoreName = o.StoreName, Location = o.Location.Value,
        Amount = o.Amount, Thief = o.Thief?.Value, ThiefName = o.ThiefName, ObservedAt = o.ObservedAt.Seconds,
        LearnedAt = o.LearnedAt.Seconds, Source = o.Source?.Value, Fact = o.Fact?.Value, Perceived = o.Perceived.ToString(),
        ToldTo = o.ToldTo.Select(t => t.Value).ToList(),
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
            TakeFoodAction k => new TakeFoodActionDto
            {
                Store = k.Store.Value, Amount = k.Amount, StealthTotal = k.StealthTotal,
                SeenAtStart = k.SeenAtStart.Select(a => a.Value).OrderBy(a => a, StringComparer.Ordinal).ToList(),
                BestLight = (int?)k.BestLight,
            },
            WaitAction wa => new WaitActionDto { Interruptible = wa.Interruptible },
            ReportAction rep => new ReportActionDto { Recipient = rep.Recipient.Value, Observation = rep.Observation.Value },
            GuardAction ga => new GuardActionDto { Store = ga.Store.Value },
            ConfiscateAction ca => new ConfiscateActionDto { Target = ca.Target.Value, Store = ca.Store.Value },
            MoveAction mv => new MoveActionDto { From = new PosDto { X = mv.From.X, Y = mv.From.Y }, Path = mv.Path.Select(p => new PosDto { X = p.X, Y = p.Y }).ToList(), Speed = mv.Speed, Stealthy = mv.Stealthy },
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
        w.NextObservationId = d.NextObservationId;
        Check(d.RngAlgorithm == SplitMix64.Algorithm, $"generatore casuale sconosciuto '{d.RngAlgorithm}'");
        w.Rng = new SplitMix64(d.RngState);

        foreach (var a in d.Areas)
            AddUnique(w.Areas, new AreaId(a.Id), new Area { Id = new AreaId(a.Id), Name = a.Name }, "area");
        foreach (var l in d.Locations)
        {
            Check(w.Areas.ContainsKey(new AreaId(l.Area)), $"luogo '{l.Id}' in area sconosciuta");
            AddUnique(w.Locations, new LocationId(l.Id), new Location { Id = new LocationId(l.Id), Name = l.Name, Area = new AreaId(l.Area), Lit = l.Lit }, "luogo");
        }
        foreach (var r in d.Routes)
        {
            Check(w.Locations.ContainsKey(new LocationId(r.From)) && w.Locations.ContainsKey(new LocationId(r.To)),
                $"percorso '{r.From}'-'{r.To}' con luogo sconosciuto");
            Check(r.Seconds > 0, "percorso di durata non positiva");
            AddUnique(w.Routes, (new LocationId(r.From), new LocationId(r.To)), new Duration(r.Seconds), "percorso");
        }
        foreach (var s in d.Stores)
        {
            Check(w.Locations.ContainsKey(new LocationId(s.Location)), $"deposito '{s.Id}' in luogo sconosciuto");
            Check(s.Food >= 0, $"deposito '{s.Id}' con razioni negative");
            AddUnique(w.Stores, new StoreId(s.Id), new Store
            {
                Id = new StoreId(s.Id), Name = s.Name, Location = new LocationId(s.Location),
                Owner = s.Owner is null ? null : new FactionId(s.Owner), Food = s.Food, Position = s.Position is { } sp ? new GridPos(sp.X, sp.Y) : null,
                Access = (s.Access ?? throw new InvalidDataException($"accesso del deposito '{s.Id}' mancante"))
                    .Select(a => a is null ? throw new InvalidDataException($"accesso del deposito '{s.Id}' non valido") : new GridPos(a.X, a.Y)).ToArray(),
            }, "deposito");
        }
        foreach (var m in d.Maps ?? throw new InvalidDataException("elenco delle mappe mancante"))
        {
            Check(m is not null && m.Rows is not null && m.Zones is not null && m.Zones.Keys.All(k => k.Length == 1), "mappa non valida");
            var area = new AreaId(Required(m!.Area, "area di una mappa"));
            Check(w.Areas.ContainsKey(area), $"mappa di un'area sconosciuta '{area}'");
            var zones = m.Zones!.ToDictionary(z => z.Key[0], z => new LocationId(z.Value));
            Check(zones.Values.All(z => w.Locations.TryGetValue(z, out var l) && l.Area == area), $"zone della mappa '{area}' non valide");
            var occupied = w.Stores.Values.Where(s => s.Position is not null && w.Locations[s.Location].Area == area).Select(s => s.Position!.Value);
            Check(m.Lights is not null && m.Lights.All(l => l is not null), $"luci della mappa '{area}' non valide");
            var lights = m.Lights!.Select(l => new LightSource(new GridPos(l.X, l.Y), l.Bright, l.Dim));
            Check(m.Doors is not null && m.Doors.All(d => d is not null), $"porte della mappa '{area}' non valide");
            var doors = m.Doors!.Select(d => (new GridPos(d.X, d.Y), d.Open));
            Check(m.Exits is not null && m.Exits.All(e => e is not null) && m.Posts is not null && m.Posts.All(p => p is not null),
                $"uscite o posti della mappa '{area}' non validi");
            var exits = m.Exits!.Select(e => (new LocationId(Required(e.Location, "luogo di un'uscita")), new GridPos(e.X, e.Y)));
            var posts = m.Posts!.Select(p => (new LocationId(Required(p.Location, "luogo di un posto")),
                Enum.TryParse<PostKind>(p.Kind, out var kind) && Enum.IsDefined(kind) ? kind : throw new InvalidDataException($"tipo di posto sconosciuto '{p.Kind}'"),
                new GridPos(p.X, p.Y)));
            AddUnique(w.Maps, area, new GridMap(area, m.Rows!, zones, occupied, lights, doors, exits, posts), "mappa");
        }
        foreach (var store in w.Stores.Values.Where(s => s.Position is not null))
            Check(w.Maps.TryGetValue(w.Locations[store.Location].Area, out var sm) && sm.ZoneAt(store.Position!.Value) == store.Location,
                $"deposito '{store.Id}' fuori dalla sua zona");
        foreach (var store in w.Stores.Values.Where(s => s.Access.Count > 0))
            Check(store.Position is { } at && Invariants.StoreAccess(store.Id.Value, w.Maps[w.Locations[store.Location].Area], at, store.Access) is null,
                $"accesso del deposito '{store.Id}' non valido");
        foreach (var (from, to) in w.Routes.Keys)
        foreach (var end in new[] { from, to })
            Check(!w.Maps.TryGetValue(w.Locations[end].Area, out var endMap) || endMap.Exits.ContainsKey(end),
                $"percorso '{from}'-'{to}' da un luogo mappato senza uscita");
        foreach (var f in d.Factions)
        {
            AddUnique(w.Factions, new FactionId(f.Id), new Faction
            {
                Id = new FactionId(f.Id),
                Name = f.Name,
                HomeStore = f.HomeStore is null ? null : new StoreId(f.HomeStore),
                DailyUpkeep = f.DailyUpkeep,
                UpkeepTimeOfDay = new Duration(f.UpkeepTimeOfDay),
                Policy = f.Policy is { } p ? new RaidPolicy(p.FoodThreshold, p.RaidAmount, new Duration(p.EvaluationInterval)) : null,
                NextEvaluation = f.NextEvaluation is { } next ? new GameTime(next) : null,
                LastDecision = FromDto(f.LastDecision),
                Authority = f.Authority is null ? null : new ActorId(f.Authority),
            }, "fazione");
            var restored = w.Factions[new FactionId(f.Id)];
            Check(Invariants.Faction(f.Id, restored.DailyUpkeep, restored.HomeStore is not null, restored.UpkeepTimeOfDay, restored.Policy));
            Check((restored.Policy is null) == (restored.NextEvaluation is null),
                $"la fazione '{f.Id}' ha una prossima valutazione solo se ha una politica");
            foreach (var avoid in f.AvoidUntil)
            {
                Check(w.Stores.ContainsKey(new StoreId(avoid.Store)), $"fazione '{f.Id}' evita un deposito sconosciuto");
                AddUnique(w.Factions[new FactionId(f.Id)].AvoidUntil, new StoreId(avoid.Store), new GameTime(avoid.Until), "bersaglio evitato");
            }
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
                        Amount = r.Amount, AssignedAt = new GameTime(r.AssignedAt), TakeAttempted = r.TakeAttempted, Aborted = r.Aborted,
                    }
                    : null,
                Home = a.Home is null ? null : new LocationId(a.Home),
                Shift = a.Shift is { } sh ? new WorkShift(new LocationId(sh.Location), new Duration(sh.Start), new Duration(sh.End)) : null,
                ArrivedAt = new GameTime(a.ArrivedAt),
                GuardDuty = a.GuardDuty is { } g
                    ? new GuardDuty { Store = new StoreId(g.Store), Since = new GameTime(g.Since), Until = new GameTime(g.Until) }
                    : null,
                Vigil = a.Vigil is { } v ? new Vigil { Store = new StoreId(v.Store), Until = new GameTime(v.Until) } : null,
                Position = a.Position is { } ap ? new GridPos(ap.X, ap.Y) : null,
                MapArea = a.MapArea is null ? null : new AreaId(a.MapArea),
                Sneak = a.Sneak,
                Torches = a.Torches,
                TorchLitUntil = a.TorchLitUntil is { } lit ? new GameTime(lit) : null,
                Sheet = FromDto(a.Sheet!),
            };
            foreach (var o in a.Knowledge)
                actor.Knowledge.Add(FromDto(o));
            foreach (var c in a.Claims ?? throw new InvalidDataException($"debiti di '{a.Id}' mancanti"))
                actor.Claims.Add(new Claim { Thief = new ActorId(Required(c?.Thief, "ladro di un debito")), Store = new StoreId(Required(c!.Store, "deposito di un debito")), Theft = new FactId(c.Theft), Owed = c.Owed });
            foreach (var theft in a.SettledThefts ?? throw new InvalidDataException($"furti saldati di '{a.Id}' mancanti"))
                actor.SettledThefts.Add(new FactId(theft));
            foreach (var c in a.Cargo ?? throw new InvalidDataException($"carico di '{a.Id}' mancante"))
            {
                Check(c is not null && !string.IsNullOrEmpty(c.Store) && c.Amount > 0, $"carico non valido per '{a.Id}'");
                actor.Cargo.Add(new Cargo { Store = new StoreId(c!.Store), Amount = c.Amount });
            }
            foreach (var origin in a.ActedOn)
                actor.ActedOn.Add(new ObservationId(origin));
            ValidateActor(w, actor);
            AddUnique(w.Actors, actor.Id, actor, "attore");
        }
        Check(w.Actors.TryGetValue(w.Player, out var player) && player.IsPlayer, "giocatore mancante");
        Check(w.Actors.Values.Count(a => a.IsPlayer) == 1, "più di un giocatore");
        foreach (var actor in w.Actors.Values)
            ValidateKnowledge(w, actor);
        foreach (var faction in w.Factions.Values)
            Check(faction.Authority is null || w.Actors.ContainsKey(faction.Authority.Value), $"autorità di '{faction.Id}' sconosciuta");

        var entries = d.Schedule.Select(e => new Scheduler.Entry(new GameTime(e.Due), e.Sequence, e.Job switch
        {
            "CompleteAction" => (ScheduledJob)new CompleteAction(new ActionId(e.Action ?? throw new InvalidDataException("azione mancante"))),
            "MoveWaypoint" => new MoveWaypoint(new ActionId(e.Action ?? throw new InvalidDataException("azione mancante")), e.Step ?? throw new InvalidDataException("passo mancante")),
            "FactionUpkeep" => new FactionUpkeep(ExistingFaction(w, e.Faction)),
            "EvaluateFaction" => new EvaluateFaction(ExistingFaction(w, e.Faction)),
            _ => throw new InvalidDataException($"lavoro sconosciuto '{e.Job}'"),
        })).ToList();
        // Everything due at or before Now was processed before saving.
        foreach (var entry in entries)
            Check(entry.Due > w.Now, "scadenza non futura");
        w.Scheduler = Scheduler.Restore(entries, d.NextSequence);

        // Periodic faction jobs are not optional: without them consumption and raids would silently stop.
        foreach (var faction in w.Factions.Values)
        {
            var upkeeps = entries.Where(e => e.Job is FactionUpkeep u && u.Faction == faction.Id).ToList();
            if (faction.DailyUpkeep > 0)
                Check(upkeeps.Count == 1 && upkeeps[0].Due == Simulation.NextUpkeepAfter(w.Now, faction),
                    $"consumo giornaliero di '{faction.Id}' non programmato correttamente");
            else
                Check(upkeeps.Count == 0, $"consumo programmato per '{faction.Id}', che non consuma");

            var evaluations = entries.Where(e => e.Job is EvaluateFaction v && v.Faction == faction.Id).ToList();
            if (faction.Policy is not null)
                Check(evaluations.Count == 1 && evaluations[0].Due == faction.NextEvaluation,
                    $"valutazione di '{faction.Id}' non programmata correttamente");
            else
                Check(evaluations.Count == 0, $"valutazione programmata per '{faction.Id}', che non ha una politica");
        }

        // Waypoints belong to a walk in progress, at the instant its path reaches that step.
        foreach (var entry in entries.Where(e => e.Job is MoveWaypoint))
        {
            var waypoint = (MoveWaypoint)entry.Job;
            var walk = w.Actors.Values.Select(x => x.CurrentAction).OfType<MoveAction>().FirstOrDefault(m => m.Id == waypoint.Action);
            Check(walk is not null && waypoint.Step >= 1 && waypoint.Step < walk.Path.Count
                  && entry.Due == walk.StartedAt.Plus(Duration.FromSeconds(MoveAction.SecondsFor(waypoint.Step, walk.Speed))),
                "tappa di un movimento non valida");
        }

        // Actions and completions match one to one: cancelling an action removes its deadline (schema v3),
        // so every completion belongs to exactly one active action, and every active action is due.
        var active = w.Actors.Values.Where(a => a.CurrentAction is not null).Select(a => a.CurrentAction!).ToList();
        Check(active.Select(a => a.Id).Distinct().Count() == active.Count, "due azioni in corso con lo stesso ID");
        var completions = entries.Where(e => e.Job is CompleteAction).ToList();
        Check(completions.Select(e => ((CompleteAction)e.Job).Action).Distinct().Count() == completions.Count,
            "due scadenze per la stessa azione");
        foreach (var action in active)
        {
            Check(completions.Any(e => ((CompleteAction)e.Job).Action == action.Id && e.Due == action.CompletesAt),
                $"azione di '{action.Actor}' senza scadenza");
            Check(action.Id.Value < w.NextActionId, "contatore delle azioni incoerente");
        }
        Check(completions.All(e => active.Any(a => a.Id == ((CompleteAction)e.Job).Action)),
            "scadenza di un'azione che non è in corso");

        foreach (var f in d.Facts)
            w.RecentFacts.AddLast(new Fact { Id = new FactId(f.Id), At = new GameTime(f.At), Kind = f.Kind, Description = f.Description, Participants = f.Participants.Select(p => new ActorId(p)).ToArray() });
        Check(w.RecentFacts.All(f => f.Id.Value < w.NextFactId), "contatore dei fatti incoerente");

        return w;
    }

    /// <summary>Checks references that may point to any actor; runs once all actors are loaded.</summary>
    private static void ValidateKnowledge(WorldState w, Actor a)
    {
        foreach (var o in a.Knowledge)
        {
            Check(w.Stores.ContainsKey(o.Store) && w.Locations.ContainsKey(o.Location), $"conoscenza di '{a.Id}' su luoghi sconosciuti");
            Check(o.Thief is null || w.Actors.ContainsKey(o.Thief.Value), $"conoscenza di '{a.Id}' su un ladro sconosciuto");
            Check(o.Source is null || w.Actors.ContainsKey(o.Source.Value), $"conoscenza di '{a.Id}' da una fonte sconosciuta");
            Check(o.ToldTo.All(w.Actors.ContainsKey), $"conoscenza di '{a.Id}' raccontata a sconosciuti");
            Check(o.Id.Value < w.NextObservationId && o.Origin.Value < w.NextObservationId, "contatore delle osservazioni incoerente");
        }
        Check(a.Knowledge.Select(o => o.Origin).Distinct().Count() == a.Knowledge.Count, $"conoscenze duplicate per '{a.Id}'");
        foreach (var c in a.Claims)
            Check(w.Actors.ContainsKey(c.Thief), $"debito di '{a.Id}' verso un ladro sconosciuto");
        if (a.CurrentAction is ConfiscateAction confiscate)
            Check(w.Actors.ContainsKey(confiscate.Target), $"confisca di '{a.Id}' verso uno sconosciuto");
        if (a.CurrentAction is ReportAction report)
        {
            Check(w.Actors.ContainsKey(report.Recipient), $"rapporto di '{a.Id}' a destinatario sconosciuto");
            Check(a.Knowledge.Any(o => o.Id == report.Observation), $"rapporto di '{a.Id}' su qualcosa che non sa");
        }
    }

    private static void ValidateActor(WorldState w, Actor a)
    {
        Check(Invariants.Sheet(a.Id.Value, a.Sheet));
        if (a.Shift is { } shift)
            Check(Invariants.Shift(a.Id.Value, a.IsPlayer, shift.Start, shift.End));
        Check(a.Home is null || w.Locations.ContainsKey(a.Home.Value), $"attore '{a.Id}' con casa sconosciuta");
        Check(a.Shift is null || w.Locations.ContainsKey(a.Shift.Location), $"attore '{a.Id}' con lavoro sconosciuto");
        Check(a.GuardDuty is null || w.Stores.ContainsKey(a.GuardDuty.Store), $"attore '{a.Id}' sorveglia un deposito sconosciuto");
        Check(a.Vigil is null || w.Stores.ContainsKey(a.Vigil.Store), $"attore '{a.Id}' vigila su un deposito sconosciuto");
        if (a.CurrentAction is ConfiscateAction ca)
            Check(w.Stores.ContainsKey(ca.Store), $"confisca di '{a.Id}' per un deposito sconosciuto");
        foreach (var c in a.Claims)
            Check(w.Stores.ContainsKey(c.Store) && c.Owed > 0, $"debito non valido per '{a.Id}'");
        foreach (var c in a.Cargo)
            Check(w.Stores.ContainsKey(c.Store), $"carico di '{a.Id}' per un deposito sconosciuto");
        Check(a.Cargo.Sum(c => (long)c.Amount) <= a.Food, $"'{a.Id}' trasporta più razioni di quante ne abbia");
        if (a.CurrentAction is GuardAction guard)
            Check(w.Stores.ContainsKey(guard.Store), $"sorveglianza di '{a.Id}' su deposito sconosciuto");
        Check(a.Food >= 0, $"attore '{a.Id}' con razioni negative");
        Check(a.Torches >= 0, $"attore '{a.Id}' con torce negative");
        Check(a.CurrentAction is not TakeFoodAction theft || theft.SeenAtStart.All(w.Actors.ContainsKey),
            $"furto di '{a.Id}' con testimoni sconosciuti");
        Check(a.Faction is null || w.Factions.ContainsKey(a.Faction.Value), $"attore '{a.Id}' di fazione sconosciuta");
        Check(a.Location is null || w.Locations.ContainsKey(a.Location.Value), $"attore '{a.Id}' in luogo sconosciuto");
        if (a.Position is { } pos)
        {
            // On a map: the square is real and walkable, and its zone is the actor's place (null on open ground).
            Check(a.MapArea is { } area && w.Maps.TryGetValue(area, out var map) && map.IsWalkable(pos) && map.ZoneAt(pos) == a.Location
                  && !map.IsClosedDoor(pos),
                $"attore '{a.Id}' in una casella non valida {pos}");
            Check(a.CurrentAction is not TravelAction, $"attore '{a.Id}' in viaggio e su una mappa");
            if (a.CurrentAction is MoveAction mv)
            {
                var grid = w.Maps[a.MapArea!.Value];
                Check(mv.Speed > 0 && mv.Path.Count > 0 && mv.CompletesAt == mv.StartedAt.Plus(Duration.FromSeconds(MoveAction.SecondsFor(mv.Path.Count, mv.Speed))),
                    $"movimento di '{a.Id}' incoerente");
                var previous = mv.From;
                foreach (var step in mv.Path)
                {
                    Check(previous.StepsTo(step) == 1 && grid.IsWalkable(step), $"percorso di '{a.Id}' non continuo o bloccato");
                    previous = step;
                }
                Check(pos == mv.From || mv.Path.Contains(pos), $"'{a.Id}' non è sul proprio percorso");
            }
        }
        else
        {
            Check(a.MapArea is null && a.CurrentAction is not MoveAction, $"attore '{a.Id}' senza casella su una mappa");
            Check((a.Location is null) == (a.CurrentAction is TravelAction), $"attore '{a.Id}': posizione e viaggio incoerenti");
        }
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
                Store = new StoreId(take.Store), Amount = take.Amount, StealthTotal = take.StealthTotal,
                SeenAtStart = (take.SeenAtStart ?? throw new InvalidDataException("testimoni del furto mancanti"))
                    .Select(a => new ActorId(Required(a, "testimone del furto"))).ToHashSet(),
                BestLight = take.BestLight is { } light && Enum.IsDefined(typeof(Light), light) ? (Light)light
                    : take.BestLight is null ? null : throw new InvalidDataException("luce del furto non valida"),
            },
            ReportActionDto rep => new ReportAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                Recipient = new ActorId(rep.Recipient), Observation = new ObservationId(rep.Observation),
            },
            MoveActionDto mv when mv.From is null || mv.Path is null || mv.Path.Any(p => p is null) => throw new InvalidDataException("percorso non valido"),
            MoveActionDto mv => new MoveAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                From = new GridPos(mv.From.X, mv.From.Y), Path = mv.Path.Select(p => new GridPos(p.X, p.Y)).ToArray(), Speed = mv.Speed, Stealthy = mv.Stealthy,
            },
            ConfiscateActionDto ca => new ConfiscateAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                Target = new ActorId(Required(ca.Target, "bersaglio della confisca")), Store = new StoreId(Required(ca.Store, "deposito della confisca")),
            },
            GuardActionDto guard => new GuardAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                Store = new StoreId(guard.Store),
            },
            WaitActionDto wait => new WaitAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                Interruptible = wait.Interruptible,
            },
            _ => throw new InvalidDataException("tipo di azione sconosciuto"),
        };
    }

    private static Observation FromDto(ObservationDto o)
    {
        var observation = new Observation
        {
            Id = new ObservationId(o.Id),
            Origin = new ObservationId(o.Origin),
            Store = new StoreId(Required(o.Store, "deposito dell'osservazione")),
            StoreName = o.StoreName,
            Location = new LocationId(Required(o.Location, "luogo dell'osservazione")),
            Amount = o.Amount,
            Thief = o.Thief is null ? null : new ActorId(o.Thief),
            ThiefName = o.ThiefName,
            ObservedAt = new GameTime(o.ObservedAt),
            LearnedAt = new GameTime(o.LearnedAt),
            Source = o.Source is null ? null : new ActorId(o.Source),
            Fact = o.Fact is { } fact ? new FactId(fact) : null,
            Perceived = Enum.TryParse<PerceptionMode>(o.Perceived, out var mode) && Enum.IsDefined(mode) ? mode : throw new InvalidDataException($"modo di percezione sconosciuto '{o.Perceived}'"),
        };
        foreach (var told in o.ToldTo)
            observation.ToldTo.Add(new ActorId(told));
        Check(observation.LearnedAt >= observation.ObservedAt, "osservazione appresa prima di essere avvenuta");
        return observation;
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

    /// <summary>Fails with a shared invariant's message, if any.</summary>
    private static void Check(string? problem)
    {
        if (problem is not null)
            throw new InvalidDataException(problem);
    }

    private static void AddUnique<TKey, TValue>(SortedDictionary<TKey, TValue> map, TKey key, TValue value, string what)
        where TKey : notnull
    {
        Check(!map.ContainsKey(key), $"{what} duplicato: {key}");
        map.Add(key, value);
    }
}
