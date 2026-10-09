using RpgSandbox.Sim.Api;
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
}

internal sealed class Actor
{
    public required ActorId Id { get; init; }
    public required string Name { get; init; }
    public required bool IsPlayer { get; init; }
    public FactionId? Faction { get; init; }

    /// <summary>Where the actor lives; routine brings NPCs back here outside work hours.</summary>
    public LocationId? Home { get; init; }

    /// <summary>Optional daily work shift (NPCs only).</summary>
    public WorkShift? Shift { get; init; }

    /// <summary>Null while the actor is travelling.</summary>
    public LocationId? Location { get; set; }

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

    /// <summary>Debug link to the world fact; never needed to understand the observation.</summary>
    public FactId? Fact { get; init; }

    /// <summary>Who this actor has already told, so it does not repeat itself.</summary>
    public SortedSet<ActorId> ToldTo { get; } = new();
}

internal sealed class Store
{
    public required StoreId Id { get; init; }
    public required string Name { get; init; }
    public required LocationId Location { get; init; }
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
}

internal sealed class ReportAction : PendingAction
{
    public override string Kind => "Report";
    public required ActorId Recipient { get; init; }

    /// <summary>The reporter's observation, chosen when the report started.</summary>
    public required ObservationId Observation { get; init; }
}

internal sealed class GuardAction : PendingAction
{
    public override string Kind => "Guard";
    public required StoreId Store { get; init; }
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

    /// <summary>Keyed by (from, to); both directions are stored.</summary>
    public SortedDictionary<(LocationId From, LocationId To), Duration> Routes { get; } = new();

    public Scheduler Scheduler { get; set; } = new();

    /// <summary>Bounded history, oldest first. Memories will keep their own copies, so pruning is safe.</summary>
    public LinkedList<Fact> RecentFacts { get; } = new();

    public long NextActionId { get; set; } = 1;
    public long NextFactId { get; set; } = 1;
    public long NextObservationId { get; set; } = 1;

    public static WorldState FromScenario(Scenario scenario)
    {
        var world = new WorldState { Player = scenario.Player };
        foreach (var a in scenario.Areas)
            world.Areas.Add(a.Id, new Area { Id = a.Id, Name = a.Name });
        foreach (var l in scenario.Locations)
            world.Locations.Add(l.Id, new Location { Id = l.Id, Name = l.Name, Area = l.Area });
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
                Home = a.IsPlayer ? null : a.Location, Shift = a.Shift, ArrivedAt = GameTime.Start,
            });
        }
        foreach (var s in scenario.Stores)
            world.Stores.Add(s.Id, new Store { Id = s.Id, Name = s.Name, Location = s.Location, Owner = s.Owner, Food = s.Food });
        return world;
    }

    public ActionId AllocateActionId() => new(NextActionId++);

    public IEnumerable<Actor> MembersOf(FactionId faction) => Actors.Values.Where(a => a.Faction == faction);

    public FactId RecordFact(string kind, string description)
    {
        var id = new FactId(NextFactId++);
        RecentFacts.AddLast(new Fact { Id = id, At = Now, Kind = kind, Description = description });
        while (RecentFacts.Count > RecentFactCapacity)
            RecentFacts.RemoveFirst();
        return id;
    }
}
