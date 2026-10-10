using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Sim;

// Mutable world state. Internal to Sim: the client only sees views and sends commands.
// Collections are sorted by id so every iteration order is stable (determinism).

internal sealed class Area
{
    public required AreaId Id { get; init; }
    public required string Name { get; init; }
}

internal sealed class Location
{
    public required LocationId Id { get; init; }
    public required string Name { get; init; }
    public required AreaId Area { get; init; }

    /// <summary>Indoors with lamps: never darker than dim light at night.</summary>
    public bool Lit { get; init; }
}

internal sealed class Actor
{
    public required ActorId Id { get; init; }
    public required string Name { get; init; }
    public required bool IsPlayer { get; init; }
    public FactionId? Faction { get; init; }

    /// <summary>What the rules know about the actor (abilities, skills, armor).</summary>
    public CharacterSheet Sheet { get; init; } = CharacterSheet.Commoner();

    /// <summary>Where the actor lives; routine brings NPCs back here outside work hours.</summary>
    public LocationId? Home { get; init; }

    /// <summary>Optional daily work shift (NPCs only).</summary>
    public WorkShift? Shift { get; init; }

    /// <summary>Null while the actor is travelling.</summary>
    public LocationId? Location { get; set; }

    /// <summary>Square on the area map (mapped areas only). While moving: the last square reached at a recorded instant.</summary>
    public GridPos? Position { get; set; }

    /// <summary>The mapped area the actor is on (also on open ground, outside any place). Null off the maps.</summary>
    public AreaId? MapArea { get; set; }

    /// <summary>
    /// Set while the actor moves stealthily (ADAPTATION of the SRD Hide action): its Stealth total, rolled once when it
    /// starts sneaking, is the DC to notice it in dim light. Cleared by walking normally or by anything noisy.
    /// </summary>
    public int? Sneak { get; set; }

    /// <summary>Unlit torches carried.</summary>
    public int Torches { get; set; }

    /// <summary>
    /// While a torch burns in the actor's hand: when it goes out (SRD: a Torch burns for 1 hour). Lit while
    /// <c>Now &lt; TorchLitUntil</c>; no job is needed to put it out.
    /// </summary>
    public GameTime? TorchLitUntil { get; set; }

    /// <summary>When the actor last arrived at <see cref="Location"/>: decides whether a witness saw a deed from its start.</summary>
    public GameTime ArrivedAt { get; set; }

    /// <summary>Rations carried. Never negative.</summary>
    public int Food { get; set; }

    /// <summary>At most one action at a time.</summary>
    public PendingAction? CurrentAction { get; set; }

    /// <summary>At most one assignment at a time.</summary>
    public RaidAssignment? Assignment { get; set; }

    /// <summary>Set while the actor is guarding a store.</summary>
    public GuardDuty? GuardDuty { get; set; }

    /// <summary>Set while the actor keeps an eye on a robbed store of its faction (not a guard: it deters nothing).</summary>
    public Vigil? Vigil { get; set; }

    /// <summary>Thefts this authority wants to make good: known thief, robbed store, rations still owed.</summary>
    public List<Claim> Claims { get; } = new();

    /// <summary>Thefts already made good: a late testimony must not reopen them.</summary>
    public SortedSet<FactId> SettledThefts { get; } = new();

    /// <summary>Rations carried on someone else's behalf (confiscated), with the store they go back to.</summary>
    public List<Cargo> Cargo { get; } = new();

    /// <summary>What the actor knows: its own observations and what others told it. One entry per origin.</summary>
    public List<Observation> Knowledge { get; } = new();

    /// <summary>Origins of observations this actor already reacted to (e.g. by organising a guard).</summary>
    public SortedSet<ObservationId> ActedOn { get; } = new();

    /// <summary>Only the latest decision is kept (NPCs only).</summary>
    public DecisionTrace? LastDecision { get; set; }
}

/// <summary>A daily shift at a workplace, between two times of day.</summary>
internal sealed record WorkShift(LocationId Location, Duration Start, Duration End)
{
    public bool Covers(GameTime t)
    {
        var tod = t.Seconds % 86_400;
        return tod >= Start.Seconds && tod < End.Seconds;
    }
}

internal sealed class Cargo
{
    public required StoreId Store { get; init; }
    public required int Amount { get; set; }
}

/// <summary>What a known thief still owes to a robbed store, as far as the authority knows.</summary>
internal sealed class Claim
{
    public required ActorId Thief { get; init; }
    public required StoreId Store { get; init; }
    /// <summary>The theft itself: several testimonies of the same theft make one claim.</summary>
    public required FactId Theft { get; init; }
    public required int Owed { get; set; }
}

internal sealed class Vigil
{
    public required StoreId Store { get; init; }
    public required GameTime Until { get; init; }
}

internal sealed class GuardDuty
{
    public required StoreId Store { get; init; }
    public required GameTime Since { get; init; }
    public required GameTime Until { get; init; }
}

/// <summary>
/// Something an actor knows about a theft. Self-contained: it describes what was perceived at the time
/// (the thief may be unknown) and never reads the current world. Copies passed on keep the origin id.
/// </summary>
internal sealed class Observation
{
    public required ObservationId Id { get; init; }
    public required ObservationId Origin { get; init; }
    public required StoreId Store { get; init; }
    public required string StoreName { get; init; }
    public required LocationId Location { get; init; }
    public required int Amount { get; init; }

    /// <summary>Null when the witness did not recognise the thief.</summary>
    public ActorId? Thief { get; init; }
    public string? ThiefName { get; init; }

    public required GameTime ObservedAt { get; init; }
    public required GameTime LearnedAt { get; init; }

    /// <summary>Null for a first-hand observation; otherwise who told it.</summary>
    public ActorId? Source { get; init; }

    /// <summary>The fact this is about (internal: also identifies the theft for claims; never shown to the player).</summary>
    public FactId? Fact { get; init; }

    /// <summary>How the first-hand witness perceived it; copies passed on keep it.</summary>
    public PerceptionMode Perceived { get; init; } = PerceptionMode.Seen;

    /// <summary>Who this actor has already told, so it does not repeat itself.</summary>
    public SortedSet<ActorId> ToldTo { get; } = new();
}

internal sealed class Store
{
    public required StoreId Id { get; init; }
    public required string Name { get; init; }
    public required LocationId Location { get; init; }

    /// <summary>Square the store occupies on a mapped area; it is used from an adjacent square.</summary>
    public GridPos? Position { get; init; }

    /// <summary>T6c: the squares the store is used from, if declared; empty = any adjacent square (with an open diagonal).</summary>
    public IReadOnlyList<GridPos> Access { get; init; } = Array.Empty<GridPos>();
    public FactionId? Owner { get; init; }

    /// <summary>Rations stored. Never negative.</summary>
    public int Food { get; set; }
}

/// <summary>"Raid when food is low": the only faction policy so far.</summary>
internal sealed record RaidPolicy(int FoodThreshold, int RaidAmount, Duration EvaluationInterval);

internal sealed class Faction
{
    public required FactionId Id { get; init; }
    public required string Name { get; init; }
    public StoreId? HomeStore { get; init; }

    /// <summary>Rations consumed every day at <see cref="UpkeepTimeOfDay"/> from the home store.</summary>
    public int DailyUpkeep { get; init; }
    public Duration UpkeepTimeOfDay { get; init; }

    /// <summary>Who receives reports for this faction (e.g. the guard).</summary>
    public ActorId? Authority { get; init; }

    public RaidPolicy? Policy { get; init; }
    public GameTime? NextEvaluation { get; set; }
    public DecisionTrace? LastDecision { get; set; }

    /// <summary>Raid targets to avoid until the given time, learned from members who came back.</summary>
    public SortedDictionary<StoreId, GameTime> AvoidUntil { get; } = new();
}

internal sealed class RaidAssignment
{
    public required FactionId Faction { get; init; }
    public required StoreId Target { get; init; }
    public required StoreId Home { get; init; }
    public required int Amount { get; init; }
    public required GameTime AssignedAt { get; init; }

    /// <summary>Set once the take action has completed, whatever it yielded.</summary>
    public bool TakeAttempted { get; set; }

    /// <summary>Set when the member gave up (e.g. the target was guarded); it still has to go home and tell.</summary>
    public bool Aborted { get; set; }
}

/// <summary>Why an NPC or a faction did what it did: rule, reason and the data it read.</summary>
internal sealed record DecisionTrace(GameTime At, string Rule, string Reason, IReadOnlyList<string> Inputs);

internal abstract class PendingAction
{
    public required ActionId Id { get; init; }
    public required ActorId Actor { get; init; }
    public required GameTime StartedAt { get; init; }
    public required GameTime CompletesAt { get; init; }
    public required string Description { get; init; }
    public abstract string Kind { get; }
}

internal sealed class TravelAction : PendingAction
{
    public override string Kind => "Travel";
    public required LocationId Origin { get; init; }
    public required LocationId Destination { get; init; }
}

internal sealed class DepositFoodAction : PendingAction
{
    public override string Kind => "DepositFood";
    public required StoreId Store { get; init; }
    public required int Amount { get; init; }
}

internal sealed class TakeFoodAction : PendingAction
{
    public override string Kind => "TakeFood";
    public required StoreId Store { get; init; }
    public required int Amount { get; init; }

    /// <summary>Total of the single Stealth check made when a theft starts (null when not a theft).</summary>
    public int? StealthTotal { get; init; }

    /// <summary>
    /// On maps, evidence fixed when the theft starts: who could see the thief then. Only they can recognise it
    /// (F4), whatever happens to the light afterwards.
    /// </summary>
    public IReadOnlySet<ActorId> SeenAtStart { get; init; } = new HashSet<ActorId>();

    /// <summary>
    /// On maps, the best light on the thief's square so far: at the start, then at every change of light (a torch
    /// lit or put out, a door, a torch bearer reaching a waypoint). Null off the maps.
    /// </summary>
    public Light? BestLight { get; set; }
}

internal sealed class ReportAction : PendingAction
{
    public override string Kind => "Report";
    public required ActorId Recipient { get; init; }

    /// <summary>The reporter's observation, chosen when the report started.</summary>
    public required ObservationId Observation { get; init; }
}

internal sealed class ConfiscateAction : PendingAction
{
    public override string Kind => "Confiscate";
    public required ActorId Target { get; init; }
    public required StoreId Store { get; init; }
}

internal sealed class GuardAction : PendingAction
{
    public override string Kind => "Guard";
    public required StoreId Store { get; init; }
}

/// <summary>
/// Walking a path on an area map at <see cref="Speed"/> feet per 6-second round, i.e. Speed/5 squares per round
/// (SRD grid rules). Square i is reached at second ceil(i·30/Speed): exact for any multiple of 5 feet.
/// </summary>
internal sealed class MoveAction : PendingAction
{
    public override string Kind => "Move";
    public required GridPos From { get; init; }

    /// <summary>Squares after <see cref="From"/>, ending at the destination.</summary>
    public required IReadOnlyList<GridPos> Path { get; init; }

    /// <summary>Feet per round of 6 seconds.</summary>
    public required int Speed { get; init; }

    /// <summary>Walking at the SRD Slow pace, trying not to be seen.</summary>
    public bool Stealthy { get; init; }

    /// <summary>Seconds needed to walk <paramref name="steps"/> squares at <paramref name="speed"/> (rounded up).</summary>
    public static long SecondsFor(int steps, int speed) => (steps * 30L + speed - 1) / speed;

    /// <summary>Where the walker is at <paramref name="t"/>: the last square fully reached.</summary>
    public GridPos PositionAt(GameTime t)
    {
        var elapsed = Math.Max(0, t.Seconds - StartedAt.Seconds);
        var steps = (int)Math.Min(Path.Count, elapsed * Speed / 30);
        return steps == 0 ? From : Path[steps - 1];
    }
}

internal sealed class WaitAction : PendingAction
{
    public override string Kind => "Wait";

    /// <summary>Routine waits can be cancelled by a faction assignment; a player's wait cannot.</summary>
    public required bool Interruptible { get; init; }
}

internal sealed class Fact
{
    public required FactId Id { get; init; }
    public required GameTime At { get; init; }
    public required string Kind { get; init; }
    public required string Description { get; init; }

    /// <summary>Who took part: they know it happened (used to show the player what concerns them).</summary>
    public IReadOnlyList<ActorId> Participants { get; init; } = Array.Empty<ActorId>();
}

internal sealed class WorldState
{
    public const int RecentFactCapacity = 200;

    public GameTime Now { get; set; } = GameTime.Start;
    public required ActorId Player { get; init; }

    public SortedDictionary<AreaId, Area> Areas { get; } = new();
    public SortedDictionary<LocationId, Location> Locations { get; } = new();
    public SortedDictionary<ActorId, Actor> Actors { get; } = new();
    public SortedDictionary<StoreId, Store> Stores { get; } = new();
    public SortedDictionary<FactionId, Faction> Factions { get; } = new();
    public SortedDictionary<AreaId, GridMap> Maps { get; } = new();

    /// <summary>Keyed by (from, to); both directions are stored.</summary>
    public SortedDictionary<(LocationId From, LocationId To), Duration> Routes { get; } = new();

    public Scheduler Scheduler { get; set; } = new();

    /// <summary>The only randomness of the simulation; its state is part of the save.</summary>
    public SplitMix64 Rng { get; set; } = new(0);

    /// <summary>Bounded history, oldest first. Memories will keep their own copies, so pruning is safe.</summary>
    public LinkedList<Fact> RecentFacts { get; } = new();

    public long NextActionId { get; set; } = 1;
    public long NextFactId { get; set; } = 1;
    public long NextObservationId { get; set; } = 1;

    public static WorldState FromScenario(Scenario scenario)
    {
        var world = new WorldState { Player = scenario.Player, Rng = new SplitMix64(scenario.Seed) };
        foreach (var a in scenario.Areas)
            world.Areas.Add(a.Id, new Area { Id = a.Id, Name = a.Name });
        foreach (var l in scenario.Locations)
            world.Locations.Add(l.Id, new Location { Id = l.Id, Name = l.Name, Area = l.Area, Lit = l.Lit });
        foreach (var r in scenario.Routes)
        {
            world.Routes.Add((r.A, r.B), r.TravelTime);
            world.Routes.Add((r.B, r.A), r.TravelTime);
        }
        foreach (var f in scenario.Factions)
        {
            world.Factions.Add(f.Id, new Faction
            {
                Id = f.Id, Name = f.Name, HomeStore = f.HomeStore, Authority = f.Authority,
                DailyUpkeep = f.DailyUpkeep, UpkeepTimeOfDay = f.UpkeepTimeOfDay, Policy = f.Policy,
            });
        }
        foreach (var a in scenario.Actors)
        {
            world.Actors.Add(a.Id, new Actor
            {
                Id = a.Id, Name = a.Name, IsPlayer = a.IsPlayer, Faction = a.Faction, Location = a.Location, Food = a.Food,
                Home = a.IsPlayer ? null : a.Location, Shift = a.Shift, ArrivedAt = GameTime.Start, Sheet = a.Sheet, Position = a.At, Torches = a.Torches,
                MapArea = a.At is null ? null : world.Locations[a.Location].Area,
            });
        }
        foreach (var s in scenario.Stores)
            world.Stores.Add(s.Id, new Store { Id = s.Id, Name = s.Name, Location = s.Location, Owner = s.Owner, Food = s.Food, Position = s.At, Access = s.Access?.ToArray() ?? Array.Empty<GridPos>() });
        foreach (var m in scenario.Maps)
            world.Maps.Add(m.Area, new GridMap(m.Area, m.Rows, m.Zones,
                scenario.Stores.Where(s => s.At is not null && world.Locations[s.Location].Area == m.Area).Select(s => s.At!.Value), m.Lights, m.Doors, m.Exits, m.Posts));
        return world;
    }

    public ActionId AllocateActionId() => new(NextActionId++);

    public IEnumerable<Actor> MembersOf(FactionId faction) => Actors.Values.Where(a => a.Faction == faction);

    public FactId RecordFact(string kind, string description, params ActorId[] participants)
    {
        var id = new FactId(NextFactId++);
        RecentFacts.AddLast(new Fact { Id = id, At = Now, Kind = kind, Description = description, Participants = participants.Distinct().ToArray() });
        while (RecentFacts.Count > RecentFactCapacity)
            RecentFacts.RemoveFirst();
        return id;
    }
}
