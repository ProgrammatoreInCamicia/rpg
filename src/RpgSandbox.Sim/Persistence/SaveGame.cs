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
    public const int SchemaVersion = 34;

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
        if (dto.Version is not (>= 20 and <= SchemaVersion))
            throw new SaveGameException($"Versione del salvataggio non supportata: {dto.Version} (attese da 20 a {SchemaVersion}).");

        // Validation is explicit: every rule below raises InvalidDataException with a readable reason.
        // Any other exception is a programming error and is deliberately not disguised as a bad save.
        try
        {
            ValidateShape(dto);
            _scale = dto.Version >= 25 ? 1 : GameTime.TicksPerSecond; // P1c: before schema 25, times were in seconds
            return FromDto(dto);
        }
        catch (InvalidDataException e)
        {
            throw new SaveGameException($"Il salvataggio contiene dati incoerenti: {e.Message}", e);
        }
    }

    /// <summary>P1c: ticks per unit of the times in the save being read (schema 25: 1; before: a second).</summary>
    [ThreadStatic] private static long _scale;

    private static GameTime Time(long v) => GameTime.FromTicks(Scaled(v));
    private static Duration Span(long v) => Duration.FromTicks(Scaled(v));

    /// <summary>Review P1c: a time in an old save too large to count in ticks is a bad save, not a crash.</summary>
    private static long Scaled(long v) => v > long.MaxValue / _scale || v < long.MinValue / _scale
        ? throw new InvalidDataException($"istante fuori intervallo: {v}") : v * _scale;

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
        Content = new ContentDto
        {
            Id = w.Content.Id, Version = w.Content.Version, Hash = w.Content.Hash,
            Files = w.Content.Files.Select(f => new ContentFileDto { Name = f.Name, Text = f.Text }).ToList(),
        },
        Now = w.Now.Ticks,
        Player = w.Player.Value,
        NextActionId = w.NextActionId,
        NextFactId = w.NextFactId,
        NextSequence = w.Scheduler.NextSequence,
        NextObservationId = w.NextObservationId,
        RngAlgorithm = SplitMix64.Algorithm,
        RngState = w.Rng.State,
        Areas = w.Areas.Values.Select(a => new AreaDto { Id = a.Id.Value, Name = a.Name }).ToList(),
        Locations = w.Locations.Values.Select(l => new LocationDto { Id = l.Id.Value, Name = l.Name, Area = l.Area.Value, Lit = l.Lit }).ToList(),
        Routes = w.Routes.Select(r => new RouteDto { From = r.Key.From.Value, To = r.Key.To.Value, Seconds = r.Value.Ticks }).ToList(),
        Factions = w.Factions.Values.Select(f => new FactionDto
        {
            Id = f.Id.Value,
            Name = f.Name,
            HomeStore = f.HomeStore?.Value,
            DailyUpkeep = f.DailyUpkeep,
            UpkeepTimeOfDay = f.UpkeepTimeOfDay.Ticks,
            Policy = f.Policy is { } p
                ? new RaidPolicyDto { FoodThreshold = p.FoodThreshold, RaidAmount = p.RaidAmount, EvaluationInterval = p.EvaluationInterval.Ticks }
                : null,
            NextEvaluation = f.NextEvaluation?.Ticks,
            LastDecision = ToDto(f.LastDecision),
            Authority = f.Authority?.Value,
            AvoidUntil = f.AvoidUntil.Select(x => new AvoidDto { Store = x.Key.Value, Until = x.Value.Ticks }).ToList(),
            HostileToStrangers = f.HostileToStrangers,
            Enemies = f.Enemies.Select(x => x.Value).ToList(),
            Wanted = f.Wanted.Select(x => new WantedDto { Actor = x.Key.Value, Fact = x.Value.Value }).ToList(),
        }).ToList(),
        Actors = w.Actors.Values.Select(a => new ActorDto
        {
            Id = a.Id.Value,
            Name = a.Name,
            IsPlayer = a.IsPlayer,
            Faction = a.Faction?.Value,
            Location = a.Location?.Value,
            Action = ToDto(a.CurrentAction),
            Assignment = a.Assignment is { } r
                ? new RaidAssignmentDto
                {
                    Faction = r.Faction.Value, Target = r.Target.Value, Home = r.Home.Value, Amount = r.Amount,
                    AssignedAt = r.AssignedAt.Ticks, TakeAttempted = r.TakeAttempted, Aborted = r.Aborted,
                    BreakInA = r.BreakIn is { } k ? new PosDto { X = k.A.X, Y = k.A.Y } : null,
                    BreakInB = r.BreakIn is { } kb ? new PosDto { X = kb.B.X, Y = kb.B.Y } : null,
                    BreakInTries = r.BreakInTries, Balked = r.Balked,
                }
                : null,
            LastDecision = ToDto(a.LastDecision),
            Home = a.Home?.Value,
            Shift = a.Shift is { } sh ? new WorkShiftDto { Location = sh.Location.Value, Start = sh.Start.Ticks, End = sh.End.Ticks } : null,
            ArrivedAt = a.ArrivedAt.Ticks,
            GuardDuty = a.GuardDuty is { } g ? new GuardDutyDto { Store = g.Store.Value, Since = g.Since.Ticks, Until = g.Until.Ticks } : null,
            Vigil = a.Vigil is { } v ? new VigilDto { Store = v.Store.Value, Until = v.Until.Ticks } : null,
            Sheet = ToDto(a.Sheet),
            Claims = a.Claims.Select(c => new ClaimDto { Thief = c.Thief.Value, Store = c.Store.Value, Theft = c.Theft.Value, Owed = c.Owed }).ToList(),
            SettledThefts = a.SettledThefts.Select(t => t.Value).ToList(),
            Cargo = a.Cargo.Select(c => new CargoDto { Store = c.Store.Value, Amount = c.Amount }).ToList(),
            Position = a.Position is { } p ? new PosDto { X = p.X, Y = p.Y } : null,
            Spot = a.ExactSpot is { } exact ? new PosDto { X = exact.X, Y = exact.Y } : null,
            Facing = a.Facing is { } facing ? new PosDto { X = facing.X, Y = facing.Y } : null,
            MapArea = a.MapArea?.Value,
            Sneak = a.Sneak,
            Inventory = a.Inventory.Select(i => new ItemCountDto { Item = i.Key.Value, Count = i.Value }).ToList(),
            Coins = a.Coins,
            TorchLitUntil = a.TorchLitUntil?.Ticks,
            Doors = a.DoorMemory.Select(d => new RememberedDoorDto
            {
                Area = d.Key.Area.Value, X = d.Key.At.X, Y = d.Key.At.Y, Open = d.Value.Open, SeenAt = d.Value.SeenAt.Ticks,
            }).ToList(),
            KnownBarriers = a.KnownBarriers.Select(k => new KnownBarrierDto
            {
                Area = k.Key.Area.Value, AX = k.Key.Key.A.X, AY = k.Key.Key.A.Y, BX = k.Key.Key.B.X, BY = k.Key.Key.B.Y,
                Locked = k.Value.Locked, Stuck = k.Value.Stuck, Broken = k.Value.Broken, Clue = k.Value.Clue, LearnedAt = k.Value.LearnedAt.Ticks, HasLock = k.Value.HasLock,
            }).ToList(),
            PendingRelock = a.PendingRelock is { } relockBehind ? new PendingRelockDto { X = relockBehind.Door.X, Y = relockBehind.Door.Y, Walk = relockBehind.Walk.Value, AfterStep = relockBehind.AfterStep } : null,
            Knowledge = a.Knowledge.Select(ToDto).ToList(),
            ActedOn = a.ActedOn.Select(o => o.Value).ToList(),
            Build = a.Build, SheetOverride = a.SheetOverride,
            Equipment = new EquipmentDto { Armor = a.Equipment.Armor, Shield = a.Equipment.Shield, MainHand = a.Equipment.MainHand },
            Vitality = ToDto(a.Vitality.Snapshot()),
            FoughtWith = a.FoughtWith.Select(f => f.Value).ToList(),
            Experience = a.Experience,
            HitDiceSpent = a.HitDiceSpent, LastLongRestEnd = a.LastLongRestEnd?.Ticks, LastHarmAt = a.LastHarmAt?.Ticks,
            Conditions = a.Conditions.Select(c => new ConditionDto
            {
                Kind = c.Kind.ToString(), Source = c.Source?.Value, Until = c.Until?.Ticks, EndsAtTurnOf = c.EndsAtTurnOf?.Value,
            }).ToList(),
            Exhaustion = a.Exhaustion, SappedBy = a.SappedBy?.Value, SlowedBy = a.SlowedBy?.Value, Vexing = a.Vexing?.Value, VexFresh = a.VexFresh,
            Defeated = a.Defeated.Select(x => x.Value).ToList(),
            Lore = a.Lore.IsEmpty ? null : new LoreDto
            {
                Creatures = a.Lore.Creatures.Select(c => new CreatureLoreDto
                {
                    Creature = c.Key.Value, ArmorClassAbove = c.Value.ArmorClassAbove, ArmorClassAtMost = c.Value.ArmorClassAtMost,
                    Attacks = c.Value.Attacks.ToList(), Tells = c.Value.Tells.ToList(), Observed = c.Value.Observed,
                }).ToList(),
                Kinds = a.Lore.Kinds.Select(k => new KindLoreDto
                {
                    Kind = k.Key, Attacks = k.Value.Attacks.ToList(), Tells = k.Value.Tells.ToList(), Studied = k.Value.Studied,
                }).ToList(),
            },
        }).ToList(),
        NextEncounterId = w.NextEncounterId,
        LastFight = w.LastFight is { } last ? new LastFightDto { Area = last.Area.Value, Log = last.Log.Select(RecordDto).ToList() } : null,
        Encounters = w.Encounters.Values.Select(ToDto).ToList(),
        Stores = w.Stores.Values.Select(s => new StoreDto
        {
            Id = s.Id.Value, Name = s.Name, Location = s.Location.Value, Owner = s.Owner?.Value,
            Contents = s.Contents.Select(i => new ItemCountDto { Item = i.Key.Value, Count = i.Value }).ToList(),
            Position = s.Position is { } sp ? new PosDto { X = sp.X, Y = sp.Y } : null,
            Access = s.Access.Select(a => new PosDto { X = a.X, Y = a.Y }).ToList(),
        }).ToList(),
        Schedule = w.Scheduler.Entries.Select(e => new ScheduledDto
        {
            Due = e.Due.Ticks,
            Sequence = e.Sequence,
            Job = e.Job switch
            {
                CompleteAction c => "CompleteAction",
                MoveWaypoint => "MoveWaypoint",
                FactionUpkeep => "FactionUpkeep",
                EvaluateFaction => "EvaluateFaction",
                CombatRound => "CombatRound",
                _ => throw new InvalidOperationException($"Unknown job {e.Job}"),
            },
            Action = e.Job switch { CompleteAction c => c.Action.Value, MoveWaypoint w => w.Action.Value, _ => (long?)null },
            Step = (e.Job as MoveWaypoint)?.Step,
            Faction = e.Job switch { FactionUpkeep u => u.Faction.Value, EvaluateFaction v => v.Faction.Value, _ => null },
            Encounter = (e.Job as CombatRound)?.Encounter,
        }).ToList(),
        Facts = w.RecentFacts.Select(f => new FactDto { Id = f.Id.Value, At = f.At.Ticks, Kind = f.Kind, Description = f.Description, PlayerDescription = f.PlayerDescription, Participants = f.Participants.Select(p => p.Value).ToList() }).ToList(),
        Maps = w.Maps.Values.Select(m => new MapDto
        {
            Area = m.Area.Value, Rows = m.Rows.ToList(), Zones = m.Zones.ToDictionary(z => z.Key.ToString(), z => z.Value.Value),
            Lights = m.Lights.Select(l => new LightDto { X = l.At.X, Y = l.At.Y, Bright = l.BrightFeet, Dim = l.DimFeet }).ToList(),
            Doors = m.Doors.Select(d => new DoorDto { X = d.Key.X, Y = d.Key.Y, Open = d.Value }).ToList(),
            Edges = m.Edges.Select(e => new MapEdgeDto { AX = e.A.X, AY = e.A.Y, BX = e.B.X, BY = e.B.Y,
                Kind = e.Kind.ToString(), DoorX = e.DoorAt?.X, DoorY = e.DoorAt?.Y, Open = e.Open }).ToList(),
            IndoorZones = m.IndoorZones.Select(z => z.Value).Order(StringComparer.Ordinal).ToList(),
            Explored = m.Explored.OrderBy(p => p.Y).ThenBy(p => p.X).Select(p => new PosDto { X = p.X, Y = p.Y }).ToList(),
            Glass = m.BrokenGlass.Select(g => new GlassDto { AX = g.Key.A.X, AY = g.Key.A.Y, BX = g.Key.B.X, BY = g.Key.B.Y, State = g.Glass.ToString() }).ToList(),
            Barriers = m.Barriers.Select(b => new BarrierDto
            {
                AX = b.Key.A.X, AY = b.Key.A.Y, BX = b.Key.B.X, BY = b.Key.B.Y, Key = b.Value.Key, Locked = b.Value.Locked,
                Stuck = b.Value.Stuck, Broken = b.Value.Broken, ForceDc = b.Value.ForceDc, Clue = b.Value.Clue,
                Barred = b.Value.Barred, BarsDc = b.Value.BarsDc, BarsClue = b.Value.BarsClue,
            }).ToList(),
            Exits = m.Exits.Select(e => new ExitDto { Location = e.Key.Value, X = e.Value.X, Y = e.Value.Y }).ToList(),
            Posts = m.Posts.Select(p => new PostDto { Location = p.Key.Location.Value, Kind = p.Key.Kind.ToString(), X = p.Value.X, Y = p.Value.Y }).ToList(),
            Ground = m.Ground.SelectMany(g => g.Value.Select(i => new GroundItemDto { X = g.Key.X, Y = g.Key.Y, Item = i.Key.Value, Count = i.Value })).ToList(),
        }).ToList(),
    };


    /// <summary>T6c-5: the light log and the stretches in sight of a theft in progress, validated.</summary>
    private static TakeFoodAction RestoreEvidence(TakeFoodActionDto dto, TakeFoodAction take)
    {
        GameTime? previous = null;
        foreach (var change in dto.SourceLightLog ?? throw new InvalidDataException("luce del furto mancante"))
        {
            if (change is null || !Enum.IsDefined(typeof(Light), change.Light)
                || (previous is { } p && Time(change.At) <= p) || Time(change.At) < take.StartedAt)
                throw new InvalidDataException("luce del furto non valida");
            take.SourceLightLog.Add((Time(change.At), (Light)change.Light));
            previous = Time(change.At);
        }
        foreach (var sight in dto.Sight ?? throw new InvalidDataException("vista del furto mancante"))
        {
            if (sight?.Spans is null || sight.Spans.Count == 0)
                throw new InvalidDataException("vista del furto non valida");
            var spans = new List<SightSpan>();
            GameTime? end = take.StartedAt;
            for (var i = 0; i < sight.Spans.Count; i++)
            {
                var span = sight.Spans[i] ?? throw new InvalidDataException("vista del furto non valida");
                var from = Time(span.From);
                GameTime? to = span.To is { } t ? Time(t) : null;
                // Spans follow one another without overlapping; only the last can still be open.
                if (end is null || from < end || (to is { } closed && closed < from) || (to is null && i != sight.Spans.Count - 1))
                    throw new InvalidDataException("vista del furto non valida");
                spans.Add(new SightSpan { From = from, To = to });
                end = to;
            }
            if (!take.Sight.TryAdd(new ActorId(Required(sight.Actor, "testimone della vista")), spans))
                throw new InvalidDataException("vista del furto ripetuta");
        }
        return take;
    }


    private static SheetDto ToDto(CharacterSheet s) => new()
    {
        Title = s.Title, Strength = s.Strength, Dexterity = s.Dexterity, Constitution = s.Constitution,
        Intelligence = s.Intelligence, Wisdom = s.Wisdom, Charisma = s.Charisma, ProficiencyBonus = s.ProficiencyBonus,
        SkillProficiencies = s.SkillProficiencies.Select(k => k.ToString()).ToList(), ArmorClass = s.ArmorClass,
        ToolProficiencies = s.ToolProficiencies.ToList(),
        SaveProficiencies = s.SaveProficiencies.Select(a => a.ToString()).ToList(),
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
        ToolProficiencies = (s.ToolProficiencies ?? new()).Select(t => Required(t, "attrezzo")).ToArray(),
        SaveProficiencies = (s.SaveProficiencies ?? new())
            .Select(k => Enum.TryParse<Ability>(k, out var ability) && Enum.IsDefined(ability) ? ability : throw new InvalidDataException($"caratteristica sconosciuta '{k}'"))
            .ToArray(),
        ArmorClass = s.ArmorClass,
        StealthDisadvantage = s.StealthDisadvantage,
        Speed = s.Speed,
    };

    private static ObservationDto ToDto(Observation o) => new()
    {
        Id = o.Id.Value, Origin = o.Origin.Value, Kind = o.Kind.ToString(), Store = o.Store?.Value, StoreName = o.StoreName,
        Location = o.Location?.Value, Square = ToDto(o.Square), Amount = o.Amount,
        Culprit = o.Culprit?.Value, CulpritName = o.CulpritName, Victim = o.Victim?.Value, VictimName = o.VictimName,
        Involved = o.Involved.Select(i => new InvolvedDto { Actor = i.Id.Value, Name = i.Name }).ToList(), Encounter = o.Encounter,
        ObservedAt = o.ObservedAt.Ticks, LearnedAt = o.LearnedAt.Ticks, Source = o.Source?.Value, Fact = o.Fact?.Value,
        Perceived = o.Perceived.ToString(), ToldTo = o.ToldTo.Select(t => t.Value).ToList(),
    };

    private static DecisionDto? ToDto(DecisionTrace? t) => t is null
        ? null
        : new DecisionDto { At = t.At.Ticks, Rule = t.Rule, Reason = t.Reason, Inputs = t.Inputs.ToList() };

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
                SourceLightLog = k.SourceLightLog.Select(c => new LightChangeDto { At = c.At.Ticks, Light = (int)c.Light }).ToList(),
                Sight = k.Sight.Select(s => new SightDto
                {
                    Actor = s.Key.Value,
                    Spans = s.Value.Select(p => new SpanDto { From = p.From.Ticks, To = p.To?.Ticks }).ToList(),
                }).ToList(),
            },
            WaitAction wa => new WaitActionDto { Interruptible = wa.Interruptible },
            RestAction ra => new RestActionDto { Long = ra.Long, HitDice = ra.HitDice },
            ReportAction rep => new ReportActionDto { Recipient = rep.Recipient.Value, Observation = rep.Observation.Value },
            GuardAction ga => new GuardActionDto { Store = ga.Store.Value },
            ConfiscateAction ca => new ConfiscateActionDto { Target = ca.Target.Value, Store = ca.Store.Value },
            DownedAction => new DownedActionDto(),
            BarrierAction b => new BarrierActionDto { Work = b.Work.ToString(), AX = b.Target.A.X, AY = b.Target.A.Y, BX = b.Target.B.X, BY = b.Target.B.Y },
            MoveAction mv => new MoveActionDto
            {
                From = new PosDto { X = mv.From.X, Y = mv.From.Y }, Path = mv.Path.Select(p => new PosDto { X = p.X, Y = p.Y }).ToList(), Speed = mv.Speed, Stealthy = mv.Stealthy,
                FromSpot = mv.FromSpot is { } fs ? new PosDto { X = fs.X, Y = fs.Y } : null,
                Spots = mv.Spots?.Select(s => new PosDto { X = s.X, Y = s.Y }).ToList(), Ticks = mv.Ticks?.ToList(),
                Vertices = mv.Vertices?.Select(s => new PosDto { X = s.X, Y = s.Y }).ToList(),
            },
            _ => throw new InvalidOperationException($"Unknown action {action.GetType().Name}"),
        };
        if (dto is null)
            return null;
        dto.Id = action!.Id.Value;
        dto.StartedAt = action.StartedAt.Ticks;
        dto.CompletesAt = action.CompletesAt.Ticks;
        dto.Description = action.Description;
        return dto;
    }

    // ---------------------------------------------------------------- dto -> world (validated)

    private static WorldState FromDto(SaveDto d)
    {
        var content = RestoreContent(d.Content);
        var w = new WorldState { Player = new ActorId(Required(d.Player, "player")), Now = Time(d.Now), Content = content };
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
            AddUnique(w.Routes, (new LocationId(r.From), new LocationId(r.To)), Span(r.Seconds), "percorso");
        }
        foreach (var s in d.Stores)
        {
            Check(w.Locations.ContainsKey(new LocationId(s.Location)), $"deposito '{s.Id}' in luogo sconosciuto");
            var store = new Store
            {
                Id = new StoreId(s.Id), Name = s.Name, Location = new LocationId(s.Location),
                Owner = s.Owner is null ? null : new FactionId(s.Owner), Position = s.Position is { } sp ? new GridPos(sp.X, sp.Y) : null,
                Access = (s.Access ?? throw new InvalidDataException($"accesso del deposito '{s.Id}' mancante"))
                    .Select(a => a is null ? throw new InvalidDataException($"accesso del deposito '{s.Id}' non valido") : new GridPos(a.X, a.Y)).ToArray(),
            };
            foreach (var item in s.Contents ?? throw new InvalidDataException($"contenuto del deposito '{s.Id}' mancante"))
            {
                Check(item is not null && content.Item(item.Item ?? "") is not null && item.Count > 0 && store.Holds(item.Item!) == 0,
                    $"contenuto del deposito '{s.Id}' non valido");
                store.Put(item!.Item!, item.Count);
            }
            AddUnique(w.Stores, store.Id, store, "deposito");
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
            Check((d.Version == 20 || m.Edges is not null && m.IndoorZones is not null && m.Explored is not null)
                  && (m.Edges ?? new()).All(e => e is not null),
                $"confini o interni della mappa '{area}' non validi");
            var edges = (m.Edges ?? new()).Select(e => new MapEdgeView
            {
                A = new GridPos(e.AX, e.AY), B = new GridPos(e.BX, e.BY),
                Kind = Enum.TryParse<MapEdgeKind>(e.Kind, out var kind) && Enum.IsDefined(kind)
                    ? kind : throw new InvalidDataException($"tipo di confine sconosciuto '{e.Kind}'"),
                DoorAt = e.DoorX is { } x && e.DoorY is { } y ? new GridPos(x, y) : null,
                // P2b: before schema 27 a window had no state: closed.
                Open = kind == MapEdgeKind.Window ? e.Open ?? (d.Version >= 27 ? throw new InvalidDataException("finestra senza stato") : false)
                    : e.Open is null ? null : throw new InvalidDataException("stato aperto/chiuso su un confine che non è una finestra"),
            });
            var indoorZones = (m.IndoorZones ?? new()).Select(z => new LocationId(Required(z, "zona interna")));
            Check(m.Exits is not null && m.Exits.All(e => e is not null) && m.Posts is not null && m.Posts.All(p => p is not null),
                $"uscite o posti della mappa '{area}' non validi");
            var exits = m.Exits!.Select(e => (new LocationId(Required(e.Location, "luogo di un'uscita")), new GridPos(e.X, e.Y)));
            var posts = m.Posts!.Select(p => (new LocationId(Required(p.Location, "luogo di un posto")),
                Enum.TryParse<PostKind>(p.Kind, out var kind) && Enum.IsDefined(kind) ? kind : throw new InvalidDataException($"tipo di posto sconosciuto '{p.Kind}'"),
                new GridPos(p.X, p.Y)));
            // P4a: before schema 29 nothing locked or stuck.
            Check(d.Version < 29 || m.Barriers is not null, $"serrature della mappa '{area}' mancanti");
            var barriers = (m.Barriers ?? new()).Select(b => b is null || b.Key is { } k && w.Content.Item(k) is null
                ? throw new InvalidDataException($"serratura non valida nella mappa '{area}'")
                : (new BarrierKey(new GridPos(b.AX, b.AY), new GridPos(b.BX, b.BY)),
                    new Barrier
                    {
                        Key = b.Key, Locked = b.Locked, Stuck = b.Stuck, Broken = b.Broken, ForceDc = b.ForceDc, Clue = b.Clue,
                        Barred = b.Barred, BarsDc = b.BarsDc, BarsClue = b.BarsClue,
                    }));
            Check(d.Version < 30 || m.Glass is not null, $"vetri della mappa '{area}' mancanti"); // P4b: before, all whole
            var glass = (m.Glass ?? new()).Select(g => g is not null && Enum.TryParse<WindowGlass>(g.State, out var state) && Enum.IsDefined(state)
                ? (new BarrierKey(new GridPos(g.AX, g.AY), new GridPos(g.BX, g.BY)), state)
                : throw new InvalidDataException($"vetro non valido nella mappa '{area}'"));
            AddUnique(w.Maps, area, new GridMap(area, m.Rows!, zones, occupied, lights, doors, exits, posts, edges, indoorZones, barriers, glass), "mappa");
            Check((m.Explored ?? new()).All(p => p is not null), $"memoria della mappa '{area}' non valida");
            foreach (var p in m.Explored ?? new())
                Check(w.Maps[area].InBounds(new GridPos(p.X, p.Y)) && w.Maps[area].Explored.Add(new GridPos(p.X, p.Y)),
                    $"casella esplorata non valida nella mappa '{area}'");
            foreach (var g in m.Ground ?? throw new InvalidDataException($"oggetti a terra della mappa '{area}' mancanti"))
            {
                var square = new GridPos(g?.X ?? -1, g?.Y ?? -1);
                Check(g is not null && w.Content.Item(g.Item ?? "") is not null && g.Count > 0 && w.Maps[area].IsWalkable(square)
                      && w.Maps[area].OnGround(square, new ItemId(g.Item!)) == 0, $"oggetti a terra della mappa '{area}' non validi");
                w.Maps[area].PutOnGround(square, new ItemId(g!.Item!), g.Count);
            }
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
                UpkeepTimeOfDay = Span(f.UpkeepTimeOfDay),
                Policy = f.Policy is { } p ? new RaidPolicy(p.FoodThreshold, p.RaidAmount, Span(p.EvaluationInterval)) : null,
                NextEvaluation = f.NextEvaluation is { } next ? Time(next) : null,
                LastDecision = FromDto(f.LastDecision),
                Authority = f.Authority is null ? null : new ActorId(f.Authority),
                HostileToStrangers = f.HostileToStrangers,
            }, "fazione");
            var restored = w.Factions[new FactionId(f.Id)];
            Check(Invariants.Faction(f.Id, restored.DailyUpkeep, restored.HomeStore is not null, restored.UpkeepTimeOfDay, restored.Policy));
            Check((restored.Policy is null) == (restored.NextEvaluation is null),
                $"la fazione '{f.Id}' ha una prossima valutazione solo se ha una politica");
            foreach (var avoid in f.AvoidUntil)
            {
                Check(w.Stores.ContainsKey(new StoreId(avoid.Store)), $"fazione '{f.Id}' evita un deposito sconosciuto");
                AddUnique(w.Factions[new FactionId(f.Id)].AvoidUntil, new StoreId(avoid.Store), Time(avoid.Until), "bersaglio evitato");
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
                CurrentAction = FromDto(a.Action, new ActorId(a.Id)),
                LastDecision = FromDto(a.LastDecision),
                Assignment = a.Assignment is { } r
                    ? new RaidAssignment
                    {
                        Faction = new FactionId(r.Faction), Target = new StoreId(r.Target), Home = new StoreId(r.Home),
                        Amount = r.Amount, AssignedAt = Time(r.AssignedAt), TakeAttempted = r.TakeAttempted, Aborted = r.Aborted,
                        BreakIn = r.BreakInA is { } ba && r.BreakInB is { } bb ? new BarrierKey(new GridPos(ba.X, ba.Y), new GridPos(bb.X, bb.Y)) : null,
                        BreakInTries = r.BreakInTries, Balked = r.Balked,
                    }
                    : null,
                Home = a.Home is null ? null : new LocationId(a.Home),
                Shift = a.Shift is { } sh ? new WorkShift(new LocationId(sh.Location), Span(sh.Start), Span(sh.End)) : null,
                ArrivedAt = Time(a.ArrivedAt),
                GuardDuty = a.GuardDuty is { } g
                    ? new GuardDuty { Store = new StoreId(g.Store), Since = Time(g.Since), Until = Time(g.Until) }
                    : null,
                Vigil = a.Vigil is { } v ? new Vigil { Store = new StoreId(v.Store), Until = Time(v.Until) } : null,
                Position = a.Position is { } ap ? new GridPos(ap.X, ap.Y) : null,
                Facing = a.Facing is { } direction ? new GridPos(direction.X, direction.Y) : null,
                MapArea = a.MapArea is null ? null : new AreaId(a.MapArea),
                Sneak = a.Sneak,
                Coins = a.Coins,
                TorchLitUntil = a.TorchLitUntil is { } lit ? Time(lit) : null,
                Sheet = FromDto(a.Sheet!), // replaced by the derived one unless it is the actor's own (see below)
                Build = content.IsBuild(a.Build ?? "") ? a.Build! : throw new InvalidDataException($"attore '{a.Id}' fatto di un contenuto sconosciuto '{a.Build}'"),
                SheetOverride = a.SheetOverride,
                Vitality = FromDto(a.Vitality ?? throw new InvalidDataException($"punti ferita di '{a.Id}' mancanti"), a.Id),
            };
            foreach (var foe in a.FoughtWith ?? throw new InvalidDataException($"avversari di '{a.Id}' mancanti"))
                Check(actor.FoughtWith.Add(new ActorId(Required(foe, "avversario"))), $"avversario ripetuto per '{a.Id}'");
            // P1: an exact point, inside the actor's own square (schema 23).
            if (a.Spot is { } exact)
            {
                Check(d.Version >= 23 && actor.Position is { } square && new Spot(exact.X, exact.Y).Cell == square, $"punto esatto di '{a.Id}' fuori dalla sua casella");
                actor.PlaceAt(new Spot(exact.X, exact.Y));
            }
            Check(actor.Facing is not { } facing || a.IsPlayer && (facing.X != 0 || facing.Y != 0),
                $"direzione dello sguardo di '{a.Id}' non valida");
            // C5b: before schema 22 a character had the points of its definition's level, and nobody had defeated anyone.
            Check(d.Version < 22 || a.Experience is not null && a.Defeated is not null, $"esperienza di '{a.Id}' mancante");
            actor.Experience = a.Experience
                ?? (content.Characters.TryGetValue(actor.Build, out var character) ? Builds.ExperienceFor(character.Level) : 0);
            Check(actor.Experience >= 0 && (content.Characters.ContainsKey(actor.Build) || actor.Experience == 0)
                  && (a.IsPlayer || a.Defeated is not { Count: > 0 }), $"esperienza di '{a.Id}' non valida");
            actor.HitDiceSpent = a.HitDiceSpent;
            foreach (var c in a.Conditions ?? new())
                actor.Conditions.Add(new Condition
                {
                    Kind = Enum.TryParse<ConditionKind>(c.Kind, out var kind) && Enum.IsDefined(kind) ? kind : throw new InvalidDataException($"condizione sconosciuta '{c.Kind}'"),
                    Source = c.Source is null ? null : new ActorId(c.Source), Until = c.Until is { } until ? Time(until) : null,
                    EndsAtTurnOf = c.EndsAtTurnOf is null ? null : new ActorId(c.EndsAtTurnOf),
                });
            Check(a.Exhaustion is >= 0 and <= 6, $"sfinimento di '{a.Id}' non valido");
            actor.Exhaustion = a.Exhaustion;
            actor.SappedBy = a.SappedBy is null ? null : new ActorId(a.SappedBy);
            actor.SlowedBy = a.SlowedBy is null ? null : new ActorId(a.SlowedBy);
            actor.Vexing = a.Vexing is null ? null : new ActorId(a.Vexing);
            actor.VexFresh = a.VexFresh;
            actor.LastLongRestEnd = a.LastLongRestEnd is { } rested ? Time(rested) : null;
            actor.LastHarmAt = a.LastHarmAt is { } harmed ? Time(harmed) : null;
            Check(a.HitDiceSpent >= 0 && (w.LevelOf(actor) is { } lvl ? a.HitDiceSpent <= lvl : a.HitDiceSpent == 0), $"Dadi Vita di '{a.Id}' non validi");
            foreach (var foe in a.Defeated ?? new())
                Check(actor.Defeated.Add(new ActorId(Required(foe, "creatura sconfitta"))), $"creatura sconfitta ripetuta per '{a.Id}'");
            if (a.Lore is { } lore) // C5a: the player's only
            {
                Check(a.IsPlayer && lore.Creatures is not null && lore.Kinds is not null, $"conoscenze di '{a.Id}' non valide");
                foreach (var c in lore.Creatures!)
                {
                    Check(c is not null && c.Attacks is not null && c.Tells is not null
                          && !(c.ArmorClassAbove is { } above && c.ArmorClassAtMost is { } atMost && above >= atMost)
                          && !actor.Lore.Creatures.ContainsKey(new ActorId(Required(c.Creature, "creatura conosciuta"))),
                        $"conoscenza di '{c?.Creature}' non valida");
                    var known = actor.Lore.Of(new ActorId(c!.Creature));
                    (known.ArmorClassAbove, known.ArmorClassAtMost, known.Observed) = (c.ArmorClassAbove, c.ArmorClassAtMost, c.Observed);
                    known.Attacks.UnionWith(c.Attacks!);
                    known.Tells.AddRange(c.Tells!);
                }
                foreach (var k in lore.Kinds!)
                {
                    Check(k is not null && k.Attacks is not null && k.Tells is not null && content.Kinds.ContainsKey(k.Kind ?? "")
                          && !actor.Lore.Kinds.ContainsKey(k.Kind!), $"conoscenza del tipo '{k?.Kind}' non valida");
                    var known = actor.Lore.OfKind(k!.Kind!);
                    known.Studied = k.Studied;
                    known.Attacks.UnionWith(k.Attacks!);
                    known.Tells.AddRange(k.Tells!);
                }
            }
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
            foreach (var door in a.Doors ?? throw new InvalidDataException($"porte ricordate da '{a.Id}' mancanti"))
            {
                Check(door is not null && w.Maps.TryGetValue(new AreaId(door.Area), out var doorMap) && doorMap.IsDoor(new GridPos(door.X, door.Y))
                      && door.SeenAt <= d.Now && a.IsPlayer, $"porta ricordata da '{a.Id}' non valida");
                Check(actor.DoorMemory.TryAdd(new DoorKey(new AreaId(door!.Area), new GridPos(door.X, door.Y)), new RememberedDoor(door.Open, Time(door.SeenAt))),
                    $"porta ricordata due volte da '{a.Id}'");
            }
            Check(d.Version < 29 || a.KnownBarriers is not null, $"serrature conosciute da '{a.Id}' mancanti"); // P4a
            foreach (var k in a.KnownBarriers ?? new())
            {
                var key = new BarrierKey(new GridPos(k?.AX ?? -1, k?.AY ?? -1), new GridPos(k?.BX ?? -1, k?.BY ?? -1));
                Check(k is not null && a.IsPlayer && w.Maps.TryGetValue(new AreaId(k.Area), out var known) && known.BarrierOf(key) is not null
                      && k.Clue is not null && Time(k.LearnedAt) <= Time(d.Now), $"serratura conosciuta da '{a.Id}' non valida");
                Check(actor.KnownBarriers.TryAdd((new AreaId(k!.Area), key), new KnownBarrier(k.Locked, k.Stuck, k.Broken, k.Clue!, Time(k.LearnedAt), k.HasLock)),
                    $"serratura conosciuta due volte da '{a.Id}'");
            }
            if (a.PendingRelock is { } relock)
                actor.PendingRelock = new PendingRelock(new GridPos(relock.X, relock.Y), new ActionId(relock.Walk), relock.AfterStep);
            foreach (var item in a.Inventory ?? throw new InvalidDataException($"inventario di '{a.Id}' mancante"))
            {
                Check(item is not null && content.Item(item.Item ?? "") is not null && item.Count > 0 && actor.Carried(item.Item!) == 0,
                    $"inventario di '{a.Id}' non valido");
                actor.Give(item!.Item!, item.Count);
            }
            // D1a: what it wears and wields is in its inventory and of the right kind; then its profile is derived again.
            var eq = a.Equipment ?? throw new InvalidDataException($"equipaggiamento di '{a.Id}' mancante");
            actor.Equipment = new Equipment(eq.Armor, eq.Shield, eq.MainHand);
            Check(actor.Equipment.Items.All(i => actor.Carried(i) >= actor.Equipment.Items.Count(x => x == i))
                  && (eq.Armor is null || content.Item(eq.Armor)?.Armor is { Type: not ArmorType.Shield })
                  && (eq.Shield is null || content.Item(eq.Shield)?.Armor is { Type: ArmorType.Shield })
                  && (eq.MainHand is null || content.Item(eq.MainHand)?.Weapon is not null), $"equipaggiamento di '{a.Id}' non valido");
            w.Refresh(actor);
            if (actor.SheetOverride)
                actor.Sheet = FromDto(a.Sheet!);
            Check(actor.Vitality.MakesDeathSaves == actor.Profile.MakesDeathSaves && actor.Vitality.Maximum == actor.Profile.MaxHitPoints,
                $"punti ferita di '{a.Id}' incoerenti con il suo profilo");
            ValidateActor(w, actor);
            AddUnique(w.Actors, actor.Id, actor, "attore");
        }
        // Witnesses of thefts in progress are checked once every actor is loaded (they may come later in the file).
        foreach (var thief in w.Actors.Values)
            Check(thief.CurrentAction is not TakeFoodAction theft
                  || (theft.SeenAtStart.All(w.Actors.ContainsKey) && theft.Sight.Keys.All(w.Actors.ContainsKey)),
                $"furto di '{thief.Id}' con testimoni sconosciuti");
        Check(w.Actors.TryGetValue(w.Player, out var player) && player.IsPlayer, "giocatore mancante");
        Check(w.Actors.Values.Count(a => a.IsPlayer) == 1, "più di un giocatore");
        foreach (var actor in w.Actors.Values)
            ValidateKnowledge(w, actor);
        foreach (var faction in w.Factions.Values)
            Check(faction.Authority is null || w.Actors.ContainsKey(faction.Authority.Value), $"autorità di '{faction.Id}' sconosciuta");
        RestoreEncounters(w, d);
        if (d.LastFight is { } last) // P2b
        {
            var area = new AreaId(Required(last.Area, "area dell'ultimo scontro"));
            Check(w.Maps.ContainsKey(area) && last.Log is not null, "ultimo scontro non valido");
            w.LastFight = new LastFight(area, last.Log!.Select(r => RecordOf(r, w, d.Version, "registro dell'ultimo scontro")).ToList());
        }
        // C4b: wars are kept both ways; the wanted are real people, judged on a recorded deed.
        foreach (var f in d.Factions)
        {
            var faction = w.Factions[new FactionId(f.Id)];
            foreach (var enemy in f.Enemies ?? throw new InvalidDataException($"nemici di '{f.Id}' mancanti"))
                Check(!string.IsNullOrEmpty(enemy) && enemy != f.Id && w.Factions.ContainsKey(new FactionId(enemy)) && faction.Enemies.Add(new FactionId(enemy)),
                    $"nemici di '{f.Id}' non validi");
            foreach (var wanted in f.Wanted ?? throw new InvalidDataException($"ricercati di '{f.Id}' mancanti"))
                Check(wanted is not null && w.Actors.ContainsKey(new ActorId(Required(wanted.Actor, "ricercato"))) && wanted.Fact > 0 && wanted.Fact < w.NextFactId
                      && faction.Wanted.TryAdd(new ActorId(wanted.Actor), new FactId(wanted.Fact)), $"ricercati di '{f.Id}' non validi");
        }
        foreach (var faction in w.Factions.Values)
            Check(faction.Enemies.All(e => w.Factions[e].Enemies.Contains(faction.Id)), $"guerra di '{faction.Id}' non reciproca");

        var entries = d.Schedule.Select(e => new Scheduler.Entry(Time(e.Due), e.Sequence, e.Job switch
        {
            "CompleteAction" => (ScheduledJob)new CompleteAction(new ActionId(e.Action ?? throw new InvalidDataException("azione mancante"))),
            "MoveWaypoint" => new MoveWaypoint(new ActionId(e.Action ?? throw new InvalidDataException("azione mancante")), e.Step ?? throw new InvalidDataException("passo mancante")),
            "FactionUpkeep" => new FactionUpkeep(ExistingFaction(w, e.Faction)),
            "EvaluateFaction" => new EvaluateFaction(ExistingFaction(w, e.Faction)),
            "CombatRound" => new CombatRound(e.Encounter ?? throw new InvalidDataException("scontro mancante")),
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

        // C2: a fight between rounds has exactly its next round scheduled, 6 s after the last began; one waiting for the
        // player's turn has none.
        foreach (var e in w.Encounters.Values)
        {
            var rounds = entries.Where(x => x.Job is CombatRound r && r.Encounter == e.Id).ToList();
            Check(e.RoundClosed ? rounds.Count == 1 && rounds[0].Due == e.RoundStart.Plus(Duration.FromSeconds(6)) : rounds.Count == 0,
                $"round dello scontro {e.Id} non programmato correttamente");
        }
        Check(entries.All(x => x.Job is not CombatRound r || w.Encounters.ContainsKey(r.Encounter)), "round di uno scontro inesistente");

        // Waypoints belong to a walk in progress, at the instant its path reaches that step.
        foreach (var entry in entries.Where(e => e.Job is MoveWaypoint))
        {
            var waypoint = (MoveWaypoint)entry.Job;
            var walk = w.Actors.Values.Select(x => x.CurrentAction).OfType<MoveAction>().FirstOrDefault(m => m.Id == waypoint.Action);
            Check(walk is not null && waypoint.Step >= 1 && waypoint.Step < walk.Path.Count
                  && entry.Due == walk.StartedAt.Plus(Duration.FromTicks(walk.TicksTo(waypoint.Step))),
                "tappa di un movimento non valida");
        }
        // T6c-1: and every walk has exactly its future waypoints, one per square still ahead (those due after now).
        foreach (var walk in w.Actors.Values.Select(x => x.CurrentAction).OfType<MoveAction>())
        {
            var expected = Enumerable.Range(1, Math.Max(0, walk.Path.Count - 1))
                .Where(step => walk.StartedAt.Plus(Duration.FromTicks(walk.TicksTo(step))) > w.Now);
            var scheduled = entries.Select(e => e.Job).OfType<MoveWaypoint>().Where(m => m.Action == walk.Id).Select(m => m.Step).OrderBy(s => s);
            Check(scheduled.SequenceEqual(expected), $"tappe del cammino di '{walk.Actor}' mancanti o ripetute");
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
            w.RecentFacts.AddLast(new Fact { Id = new FactId(f.Id), At = Time(f.At), Kind = f.Kind, Description = f.Description, PlayerDescription = f.PlayerDescription, Participants = f.Participants.Select(p => new ActorId(p)).ToArray() });
        Check(w.RecentFacts.All(f => f.Id.Value < w.NextFactId), "contatore dei fatti incoerente");

        return w;
    }

    /// <summary>
    /// D1a: the content pack the game was played with, rebuilt from the save itself (never from the installed files),
    /// with its identity checked: a save whose texts do not match its own hash is damaged.
    /// </summary>
    private static ContentPack RestoreContent(ContentDto? dto)
    {
        Check(dto is not null && dto.Files is not null && dto.Files.All(f => f is not null && !string.IsNullOrEmpty(f.Name) && f.Text is not null),
            "contenuti del salvataggio mancanti o incompleti");
        Check(dto!.Files!.Select(f => f.Name).Distinct().Count() == dto.Files!.Count, "file dei contenuti ripetuto");
        var pack = ContentPack.Load(dto.Files.ToDictionary(f => f.Name, f => f.Text));
        Check(pack.Id == dto.Id && pack.Version == dto.Version && pack.Hash == dto.Hash,
            "i contenuti del salvataggio non corrispondono alla loro impronta");
        return pack;
    }

    /// <summary>Checks references that may point to any actor; runs once all actors are loaded.</summary>
    private static void ValidateKnowledge(WorldState w, Actor a)
    {
        foreach (var o in a.Knowledge)
        {
            Check((o.Store is null || w.Stores.ContainsKey(o.Store.Value)) && (o.Location is null || w.Locations.ContainsKey(o.Location.Value)),
                $"conoscenza di '{a.Id}' su luoghi sconosciuti");
            Check(new[] { o.Culprit, o.Victim }.All(x => x is null || w.Actors.ContainsKey(x.Value)) && o.Involved.All(i => w.Actors.ContainsKey(i.Id)),
                $"conoscenza di '{a.Id}' su persone sconosciute");
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
        Check(a.Coins >= 0 && a.Inventory.All(i => w.Content.Item(i.Key.Value) is not null && i.Value > 0), $"inventario di '{a.Id}' non valido");
        Check(a.Faction is null || w.Factions.ContainsKey(a.Faction.Value), $"attore '{a.Id}' di fazione sconosciuta");
        Check(a.Location is null || w.Locations.ContainsKey(a.Location.Value), $"attore '{a.Id}' in luogo sconosciuto");
        if (a.Position is { } pos)
        {
            // On a map: the square is real and walkable, and its zone is the actor's place (null on open ground).
            Check(a.MapArea is { } area && w.Maps.TryGetValue(area, out var map) && map.IsWalkable(pos) && map.ZoneAt(pos) == a.Location
                  && (!map.IsClosedDoor(pos) || map.IsEdgeDoor(pos)),
                $"attore '{a.Id}' in una casella non valida {pos}");
            Check(a.CurrentAction is not TravelAction, $"attore '{a.Id}' in viaggio e su una mappa");
            if (a.CurrentAction is MoveAction mv)
            {
                var grid = w.Maps[a.MapArea!.Value];
                var free = mv.Spots is not null;
                // P1: a free walk has its start, its way, a point and a tick for every step, in order, each point in its square; a
                // walk from square to square starts in its first square (P1c: from an exact point; an old save may lack it).
                Check(free == (mv.Vertices is not null) && (!free || mv.Ticks is not null && mv.FromSpot is not null)
                      && (mv.FromSpot is not { } start || start.Cell == mv.From)
                      && (mv.Ticks is null || mv.Ticks.Count == mv.Path.Count && mv.Ticks.Zip(mv.Ticks.Skip(1)).All(x => x.First <= x.Second) && mv.Ticks[0] >= 0)
                      && (!free || mv.Spots!.Count == mv.Path.Count && mv.Vertices!.Count >= 2 && mv.Vertices[0] == mv.FromSpot
                          && mv.Vertices[^1] == mv.Spots[^1] && mv.Spots.Zip(mv.Path).All(x => x.First.Cell == x.Second)),
                    $"cammino libero di '{a.Id}' incoerente");
                Check(mv.Speed > 0 && mv.Path.Count > 0 && mv.CompletesAt == mv.StartedAt.Plus(Duration.FromTicks(mv.TicksTo(mv.Path.Count))),
                    $"movimento di '{a.Id}' incoerente");
                var previous = mv.From;
                foreach (var step in mv.Path)
                {
                    Check((free ? previous.StepsTo(step) <= 1 : previous.StepsTo(step) == 1) && grid.IsWalkable(step), $"percorso di '{a.Id}' non continuo o bloccato");
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
            case BarrierAction work: // P4a
                Check(a.MapArea is { } area && w.Maps.TryGetValue(area, out var workMap) && workMap.BarrierOf(work.Target) is not null,
                    $"lavoro di '{a.Id}' su una porta o finestra sconosciuta");
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
        var started = Time(dto.StartedAt);
        var completes = Time(dto.CompletesAt);
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
            TakeFoodActionDto take => RestoreEvidence(take, new TakeFoodAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                Store = new StoreId(take.Store), Amount = take.Amount, StealthTotal = take.StealthTotal,
                SeenAtStart = (take.SeenAtStart ?? throw new InvalidDataException("testimoni del furto mancanti"))
                    .Select(a => new ActorId(Required(a, "testimone del furto"))).ToHashSet(),
            }),
            ReportActionDto rep => new ReportAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                Recipient = new ActorId(rep.Recipient), Observation = new ObservationId(rep.Observation),
            },
            MoveActionDto mv when mv.From is null || mv.Path is null || mv.Path.Any(p => p is null) => throw new InvalidDataException("percorso non valido"),
            MoveActionDto mv => ReadMove(mv, id, actor, started, completes, description),
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
            RestActionDto r => new RestAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                Long = r.Long, HitDice = r.HitDice,
            },
            DownedActionDto => new DownedAction { Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description },
            BarrierActionDto b => new BarrierAction
            {
                Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
                Work = Enum.TryParse<BarrierWork>(b.Work, out var work) && Enum.IsDefined(work) ? work : throw new InvalidDataException($"lavoro sconosciuto '{b.Work}'"),
                Target = new BarrierKey(new GridPos(b.AX, b.AY), new GridPos(b.BX, b.BY)),
            },
            _ => throw new InvalidDataException("tipo di azione sconosciuto"),
        };
    }

    // ---------------------------------------------------------------- C2: fights


    /// <summary>
    /// A walk as saved. P1c: before schema 25 its steps were reached at whole seconds: a walk from square to square keeps
    /// those instants (as ticks), and a free walk's way is rebuilt through its start and checkpoints.
    /// </summary>
    private static MoveAction ReadMove(MoveActionDto mv, ActionId id, ActorId actor, GameTime started, GameTime completes, string description)
    {
        Spot SpotOf(PosDto? p) => p is null ? throw new InvalidDataException("percorso non valido") : new Spot(p.X, p.Y);
        var from = new GridPos(mv.From.X, mv.From.Y);
        var path = mv.Path.Select(p => new GridPos(p.X, p.Y)).ToArray();
        var fromSpot = mv.FromSpot is { } fs ? new Spot(fs.X, fs.Y) : (Spot?)null;
        var spots = mv.Spots?.Select(SpotOf).ToArray();
        IReadOnlyList<long>? ticks = _scale == 1 ? mv.Ticks?.ToArray()
            : spots is not null ? mv.Seconds?.Select(Scaled).ToArray()
            : Enumerable.Range(1, path.Length).Select(i => checked((i * 30L + mv.Speed - 1) / Math.Max(1, mv.Speed) * _scale)).ToArray();
        var vertices = _scale == 1 ? mv.Vertices?.Select(SpotOf).ToArray()
            : spots is not null && fromSpot is { } start ? new[] { start }.Concat(spots).ToArray() : null;
        return new MoveAction
        {
            Id = id, Actor = actor, StartedAt = started, CompletesAt = completes, Description = description,
            From = from, Path = path, Speed = mv.Speed, Stealthy = mv.Stealthy,
            FromSpot = fromSpot, Spots = spots, Ticks = ticks, Vertices = vertices,
        };
    }
    private static VitalityDto ToDto(VitalitySnapshot v) => new()
    {
        Maximum = v.Maximum, MakesDeathSaves = v.MakesDeathSaves, Current = v.Current, State = v.State.ToString(),
        KnockedOut = v.KnockedOut, DeathSaveSuccesses = v.DeathSaveSuccesses, DeathSaveFailures = v.DeathSaveFailures,
    };

    private static Vitality FromDto(VitalityDto v, string owner)
    {
        var state = Enum.TryParse<LifeState>(v.State, out var s) && Enum.IsDefined(s) ? s
            : throw new InvalidDataException($"stato vitale di '{owner}' sconosciuto");
        try
        {
            return Vitality.Restore(new VitalitySnapshot(v.Maximum, v.MakesDeathSaves, v.Current, state, v.KnockedOut,
                v.DeathSaveSuccesses, v.DeathSaveFailures));
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"punti ferita di '{owner}': {e.Message}");
        }
    }

    private static PosDto? ToDto(GridPos? p) => p is { } q ? new PosDto { X = q.X, Y = q.Y } : null;

    private static EncounterDto ToDto(Encounter e) => new()
    {
        Id = e.Id, Area = e.Area.Value, Round = e.Round, Turn = e.Turn, RoundStart = e.RoundStart.Ticks,
        RoundClosed = e.RoundClosed, AttackedThisRound = e.AttackedThisRound,
        Opening = e.Opening is { } o
            ? new OpeningDto { Attacker = o.Attacker.Value, Target = o.Target.Value, Attack = o.Attack, NonLethal = o.NonLethal }
            : null,
        Combatants = e.Order.Select(c => new CombatantDto
        {
            Actor = c.Actor.Value, Side = c.Side, Initiative = c.Initiative, InitiativeBonus = c.InitiativeBonus,
            MovedFeet = c.MovedInches / Spot.PerFoot, MovedInches = c.MovedInches, ActionUsed = c.ActionUsed, ReactionUsed = c.ReactionUsed, OpeningSpent = c.OpeningSpent,
            LastSeenAt = c.LastSeenAt is { } at ? new PosDto { X = at.X, Y = at.Y } : null, JoinedInRound = c.JoinedInRound, Fleeing = c.Fleeing,
            Dashes = c.Dashes, Disengaged = c.Disengaged, Dodging = c.Dodging, Interacted = c.Interacted, Cleaved = c.Cleaved,
        }).ToList(),
        Log = e.Log.Select(RecordDto).ToList(),
    };

    private static CombatRecordDto RecordDto(CombatRecord r) => new()
    {
        Round = r.Round, Kind = r.Kind.ToString(), Actor = r.Actor?.Value, Target = r.Target?.Value, Attack = r.Attack,
        D20 = r.D20.ToList(), Kept = r.Kept, Bonus = r.Bonus, Total = r.Total, ArmorClass = r.ArmorClass, Hit = r.Hit,
        Critical = r.Critical, Damage = r.Damage, Path = r.Path.Select(p => new PosDto { X = p.X, Y = p.Y }).ToList(),
        Spots = r.Spots.Select(p => new PosDto { X = p.X, Y = p.Y }).ToList(),
        ShotFrom = r.ShotFrom is { } f ? new PosDto { X = f.X, Y = f.Y } : null, ShotTo = r.ShotTo is { } s ? new PosDto { X = s.X, Y = s.Y } : null,
        Cover = r.Cover.ToString(),
        Text = r.Text, PlayerText = r.PlayerText, PlayerSawActor = r.PlayerSawActor, PlayerSawTarget = r.PlayerSawTarget,
    };

    /// <summary>One line of a fight's log, checked (<paramref name="where"/> names the log in the error).</summary>
    private static CombatRecord RecordOf(CombatRecordDto? r, WorldState w, int version, string where)
    {
        Check(r is not null && Enum.TryParse<CombatLogKind>(r.Kind, out var kind) && Enum.IsDefined(kind) && r.Text is not null
              && r.D20 is not null && r.Path is not null && r.Path.All(p => p is not null) && (r.Spots ?? new()).All(p => p is not null)
              && (version < 26 || r.Spots is not null), $"{where} non valido");
        Check((r!.Actor is null || w.Actors.ContainsKey(new ActorId(r.Actor))) && (r.Target is null || w.Actors.ContainsKey(new ActorId(r.Target))),
            $"{where} con attori sconosciuti");
        return new CombatRecord
        {
            Round = r.Round, Kind = Enum.Parse<CombatLogKind>(r.Kind), Actor = r.Actor is null ? null : new ActorId(r.Actor),
            Target = r.Target is null ? null : new ActorId(r.Target), Attack = r.Attack, D20 = r.D20!.ToArray(), Kept = r.Kept,
            Bonus = r.Bonus, Total = r.Total, ArmorClass = r.ArmorClass, Hit = r.Hit, Critical = r.Critical, Damage = r.Damage,
            Path = r.Path!.Select(p => new GridPos(p!.X, p.Y)).ToArray(),
            Spots = (r.Spots ?? new()).Select(p => new Spot(p!.X, p.Y)).ToArray(),
            ShotFrom = r.ShotFrom is { } f ? new Spot(f.X, f.Y) : null, ShotTo = r.ShotTo is { } s ? new Spot(s.X, s.Y) : null, Text = r.Text!, PlayerText = r.PlayerText,
            Cover = r.Cover is null ? (version >= 28 ? throw new InvalidDataException($"{where}: copertura mancante") : CoverDegree.None) // P3: none before 28
                : Enum.TryParse<CoverDegree>(r.Cover, out var cover) && Enum.IsDefined(cover) ? cover : throw new InvalidDataException($"{where}: copertura sconosciuta"),
            PlayerSawActor = r.PlayerSawActor, PlayerSawTarget = r.PlayerSawTarget,
        };
    }

    /// <summary>
    /// C2: fights in progress, and the rules that bind them to the actors: each combatant in one fight only, on the
    /// fight's map, doing nothing else; both sides present; a round either waiting for the player's turn or closed; the
    /// fallen outside a fight lying down (a Downed action), the conscious never.
    /// </summary>
    private static void RestoreEncounters(WorldState w, SaveDto d)
    {
        w.NextEncounterId = d.NextEncounterId;
        foreach (var dto in d.Encounters ?? throw new InvalidDataException("elenco degli scontri mancante"))
        {
            Check(dto is not null && dto.Combatants is not null && dto.Log is not null, "scontro non valido");
            var area = new AreaId(Required(dto!.Area, "area di uno scontro"));
            Check(w.Maps.TryGetValue(area, out var map), $"scontro {dto.Id} senza mappa");
            Check(dto.Id > 0 && dto.Id < w.NextEncounterId, "contatore degli scontri incoerente");
            var e = new Encounter
            {
                Id = dto.Id, Area = area, Round = dto.Round, Turn = dto.Turn, RoundStart = Time(dto.RoundStart),
                RoundClosed = dto.RoundClosed, AttackedThisRound = dto.AttackedThisRound,
            };
            foreach (var c in dto.Combatants!)
            {
                Check(c is not null, $"combattente non valido nello scontro {dto.Id}");
                var id = new ActorId(Required(c!.Actor, "combattente"));
                Check(w.Actors.TryGetValue(id, out var actor), $"combattente sconosciuto '{id}'");
                Check(actor!.MapArea == area && actor.Position is { } pos && map!.IsWalkable(pos) && actor.CurrentAction is null,
                    $"combattente '{id}' fuori dalla mappa dello scontro o impegnato in altro");
                Check(d.Version < 24 || c.MovedInches is not null, $"movimento del combattente '{id}' mancante");
                var moved = c.MovedInches ?? c.MovedFeet * Spot.PerFoot;
                Check(c.Side is 0 or 1 && c.Dashes is 0 or 1 && moved >= 0 && moved <= Simulation.ProfileOf(actor).Speed * Spot.PerFoot * (1 + c.Dashes),
                    $"combattente '{id}' non valido");
                Check(e.Order.All(o => o.Actor != id) && w.Encounters.Values.All(o => o.Order.All(x => x.Actor != id)),
                    $"'{id}' è in più di uno scontro");
                e.Order.Add(new Combatant
                {
                    Actor = id, Side = c.Side, Initiative = c.Initiative, InitiativeBonus = c.InitiativeBonus, MovedInches = moved,
                    ActionUsed = c.ActionUsed, ReactionUsed = c.ReactionUsed, OpeningSpent = c.OpeningSpent,
                    LastSeenAt = d.Version >= 26 ? (c.LastSeenAt is { } at ? new Spot(at.X, at.Y) : null)
                        : c.LastSeenEnemy is { } seen ? Spot.CenterOf(new GridPos(seen.X, seen.Y)) : null,
                    JoinedInRound = c.JoinedInRound is { } joined && joined >= 1 && joined <= dto.Round ? joined
                        : c.JoinedInRound is null ? null : throw new InvalidDataException($"ingresso nello scontro di '{id}' non valido"),
                    Fleeing = c.Fleeing, Dashes = c.Dashes, Disengaged = c.Disengaged, Dodging = c.Dodging, Interacted = c.Interacted, Cleaved = c.Cleaved,
                });
            }
            Check(e.Order.Any(c => c.Side == 0) && e.Order.Any(c => c.Side == 1), $"scontro {dto.Id} senza due lati");
            Check(e.Round >= 1 && e.Turn >= 0 && e.Turn < e.Order.Count && e.RoundStart <= w.Now, $"turno dello scontro {dto.Id} non valido");
            // Only the player's turn makes the world wait: a round still open is waiting for it, now.
            Check(e.RoundClosed || (e.Order[e.Turn].Actor == w.Player && w.Actors[w.Player].Conscious && e.RoundStart == w.Now),
                $"scontro {dto.Id} fermo sul turno di qualcun altro");
            if (dto.Opening is { } o)
            {
                var attacker = new ActorId(Required(o.Attacker, "attaccante"));
                var target = new ActorId(Required(o.Target, "bersaglio"));
                Check(e.Order.Any(c => c.Actor == attacker) && e.Order.Any(c => c.Actor == target)
                      && Simulation.ProfileOf(w.Actors[attacker]).Attacks.Any(a => a.Name == o.Attack),
                    $"attacco d'apertura dello scontro {dto.Id} non valido");
                e.Opening = new OpeningAttack(attacker, target, o.Attack, o.NonLethal);
            }
            foreach (var r in dto.Log!)
                e.Log.Add(RecordOf(r, w, d.Version, $"registro dello scontro {dto.Id}"));
            AddUnique(w.Encounters, e.Id, e, "scontro");
        }
        foreach (var actor in w.Actors.Values)
        {
            Check(actor.FoughtWith.All(w.Actors.ContainsKey) && !actor.FoughtWith.Contains(actor.Id), $"avversari di '{actor.Id}' non validi");
            Check(actor.Defeated.All(actor.FoughtWith.Contains), $"creature sconfitte da '{actor.Id}' mai affrontate");
            Check(actor.Lore.Creatures.Keys.All(w.Actors.ContainsKey) && !actor.Lore.Creatures.ContainsKey(actor.Id), $"conoscenze di '{actor.Id}' su creature sconosciute");
            var fighting = w.Encounters.Values.Any(e => e.Order.Any(c => c.Actor == actor.Id));
            var lying = actor.CurrentAction is DownedAction;
            var fallen = !actor.Conscious && actor.Vitality.State != LifeState.Dead;
            Check(lying ? fallen && !fighting : !fallen || fighting, $"'{actor.Id}': stato a terra incoerente");
            Check(actor.Vitality.State != LifeState.Dead || actor.CurrentAction is null, $"'{actor.Id}' è morto ma agisce");
        }
    }

    private static Observation FromDto(ObservationDto o)
    {
        var kind = Enum.TryParse<DeedKind>(o.Kind, out var k) && Enum.IsDefined(k) ? k : throw new InvalidDataException($"tipo di fatto sconosciuto '{o.Kind}'");
        // A theft names its store; a fight or a killing never does, and always belongs to a fight.
        Check(kind == DeedKind.Theft ? !string.IsNullOrEmpty(o.Store) && o.Location is not null && o.Encounter is null
                : kind == DeedKind.BreakIn ? o.Store is null && o.StoreName is null && o.Amount == 0 && o.Encounter is null // P4a: no fight
                : o.Store is null && o.StoreName is null && o.Amount == 0 && o.Encounter is not null,
            $"osservazione di tipo {kind} incoerente");
        Check(o.Involved is not null && o.Involved.All(i => i is not null && !string.IsNullOrEmpty(i.Actor) && i.Name is not null),
            "partecipanti di un'osservazione non validi");
        var observation = new Observation
        {
            Id = new ObservationId(o.Id),
            Origin = new ObservationId(o.Origin),
            Kind = kind,
            Store = o.Store is null ? null : new StoreId(o.Store),
            StoreName = o.StoreName,
            Location = o.Location is null ? null : new LocationId(o.Location),
            Square = o.Square is { } sq ? new GridPos(sq.X, sq.Y) : null,
            Amount = o.Amount,
            Culprit = o.Culprit is null ? null : new ActorId(o.Culprit),
            CulpritName = o.CulpritName,
            Victim = o.Victim is null ? null : new ActorId(o.Victim),
            VictimName = o.VictimName,
            Encounter = o.Encounter,
            ObservedAt = Time(o.ObservedAt),
            LearnedAt = Time(o.LearnedAt),
            Source = o.Source is null ? null : new ActorId(o.Source),
            Fact = o.Fact is { } fact ? new FactId(fact) : null,
            Perceived = Enum.TryParse<PerceptionMode>(o.Perceived, out var mode) && Enum.IsDefined(mode) ? mode : throw new InvalidDataException($"modo di percezione sconosciuto '{o.Perceived}'"),
        };
        foreach (var i in o.Involved!)
            observation.Involved.Add((new ActorId(i.Actor), i.Name));
        foreach (var told in o.ToldTo)
            observation.ToldTo.Add(new ActorId(told));
        Check(observation.LearnedAt >= observation.ObservedAt, "osservazione appresa prima di essere avvenuta");
        return observation;
    }

    private static DecisionTrace? FromDto(DecisionDto? d) =>
        d is null ? null : new DecisionTrace(Time(d.At), d.Rule, d.Reason, d.Inputs.ToArray());

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
