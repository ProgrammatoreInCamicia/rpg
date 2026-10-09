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

    /// <summary>Null while the actor is travelling.</summary>
    public LocationId? Location { get; set; }

    /// <summary>At most one action at a time.</summary>
    public PendingAction? CurrentAction { get; set; }
}

internal abstract class PendingAction
{
    public required ActionId Id { get; init; }
    public required ActorId Actor { get; init; }
    public required GameTime StartedAt { get; init; }
    public required GameTime CompletesAt { get; init; }
}

internal sealed class TravelAction : PendingAction
{
    public required LocationId Origin { get; init; }
    public required LocationId Destination { get; init; }
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

    /// <summary>Keyed by (from, to); both directions are stored.</summary>
    public SortedDictionary<(LocationId From, LocationId To), Duration> Routes { get; } = new();

    public Scheduler Scheduler { get; } = new();

    /// <summary>Bounded history, oldest first. Memories will keep their own copies, so pruning is safe.</summary>
    public LinkedList<Fact> RecentFacts { get; } = new();

    public long NextActionId { get; set; } = 1;
    public long NextFactId { get; set; } = 1;

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
        foreach (var a in scenario.Actors)
            world.Actors.Add(a.Id, new Actor { Id = a.Id, Name = a.Name, IsPlayer = a.IsPlayer, Location = a.Location });
        return world;
    }

    public ActionId AllocateActionId() => new(NextActionId++);

    public void RecordFact(string kind, string description)
    {
        RecentFacts.AddLast(new Fact { Id = new FactId(NextFactId++), At = Now, Kind = kind, Description = description });
        while (RecentFacts.Count > RecentFactCapacity)
            RecentFacts.RemoveFirst();
    }
}
