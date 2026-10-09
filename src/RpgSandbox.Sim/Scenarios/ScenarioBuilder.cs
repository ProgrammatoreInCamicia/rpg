using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>
/// Immutable initial definition of a world. A scenario is never mutated by the simulation:
/// each <see cref="SimulationSession"/> builds its own mutable state from it.
/// </summary>
public sealed class Scenario
{
    internal Scenario(
        IReadOnlyList<AreaDefinition> areas,
        IReadOnlyList<LocationDefinition> locations,
        IReadOnlyList<RouteDefinition> routes,
        IReadOnlyList<ActorDefinition> actors,
        IReadOnlyList<StoreDefinition> stores,
        ActorId player)
    {
        Areas = areas;
        Locations = locations;
        Routes = routes;
        Actors = actors;
        Stores = stores;
        Player = player;
    }

    internal IReadOnlyList<AreaDefinition> Areas { get; }
    internal IReadOnlyList<LocationDefinition> Locations { get; }
    internal IReadOnlyList<RouteDefinition> Routes { get; }
    internal IReadOnlyList<ActorDefinition> Actors { get; }
    internal IReadOnlyList<StoreDefinition> Stores { get; }
    internal ActorId Player { get; }
}

internal sealed record AreaDefinition(AreaId Id, string Name);
internal sealed record LocationDefinition(LocationId Id, string Name, AreaId Area);
internal sealed record RouteDefinition(LocationId A, LocationId B, Duration TravelTime);
internal sealed record ActorDefinition(ActorId Id, string Name, LocationId Location, bool IsPlayer, int Food);
internal sealed record StoreDefinition(StoreId Id, string Name, LocationId Location, int Food);

/// <summary>Builds a <see cref="Scenario"/> in C#. Validates references when <see cref="Build"/> is called.</summary>
public sealed class ScenarioBuilder
{
    private readonly List<AreaDefinition> _areas = new();
    private readonly List<LocationDefinition> _locations = new();
    private readonly List<RouteDefinition> _routes = new();
    private readonly List<ActorDefinition> _actors = new();
    private readonly List<StoreDefinition> _stores = new();

    public ScenarioBuilder AddArea(string id, string name)
    {
        _areas.Add(new AreaDefinition(new AreaId(id), name));
        return this;
    }

    public ScenarioBuilder AddLocation(string id, string name, string areaId)
    {
        _locations.Add(new LocationDefinition(new LocationId(id), name, new AreaId(areaId)));
        return this;
    }

    /// <summary>Connects two locations in both directions.</summary>
    public ScenarioBuilder AddRoute(string fromId, string toId, Duration travelTime)
    {
        _routes.Add(new RouteDefinition(new LocationId(fromId), new LocationId(toId), travelTime));
        return this;
    }

    public ScenarioBuilder AddActor(string id, string name, string locationId, bool isPlayer = false, int food = 0)
    {
        _actors.Add(new ActorDefinition(new ActorId(id), name, new LocationId(locationId), isPlayer, food));
        return this;
    }

    public ScenarioBuilder AddStore(string id, string name, string locationId, int food)
    {
        _stores.Add(new StoreDefinition(new StoreId(id), name, new LocationId(locationId), food));
        return this;
    }

    public Scenario Build()
    {
        RequireUnique(_areas.Select(a => a.Id.Value), "area");
        RequireUnique(_locations.Select(l => l.Id.Value), "location");
        RequireUnique(_actors.Select(a => a.Id.Value), "actor");

        var areaIds = _areas.Select(a => a.Id).ToHashSet();
        var locationIds = _locations.Select(l => l.Id).ToHashSet();

        foreach (var location in _locations)
            if (!areaIds.Contains(location.Area))
                throw new InvalidOperationException($"Location '{location.Id}' references unknown area '{location.Area}'.");

        var routePairs = new HashSet<(LocationId, LocationId)>();
        foreach (var route in _routes)
        {
            if (!locationIds.Contains(route.A) || !locationIds.Contains(route.B))
                throw new InvalidOperationException($"Route '{route.A}'-'{route.B}' references an unknown location.");
            if (route.A == route.B)
                throw new InvalidOperationException($"Route from '{route.A}' to itself.");
            if (route.TravelTime.Seconds <= 0)
                throw new InvalidOperationException($"Route '{route.A}'-'{route.B}' must take positive time.");
            var key = route.A.CompareTo(route.B) < 0 ? (route.A, route.B) : (route.B, route.A);
            if (!routePairs.Add(key))
                throw new InvalidOperationException($"Duplicate route '{route.A}'-'{route.B}'.");
        }

        foreach (var actor in _actors)
        {
            if (!locationIds.Contains(actor.Location))
                throw new InvalidOperationException($"Actor '{actor.Id}' starts at unknown location '{actor.Location}'.");
            if (actor.Food < 0)
                throw new InvalidOperationException($"Actor '{actor.Id}' starts with negative food.");
        }

        RequireUnique(_stores.Select(s => s.Id.Value), "store");
        foreach (var store in _stores)
        {
            if (!locationIds.Contains(store.Location))
                throw new InvalidOperationException($"Store '{store.Id}' is at unknown location '{store.Location}'.");
            if (store.Food < 0)
                throw new InvalidOperationException($"Store '{store.Id}' starts with negative food.");
        }

        var players = _actors.Where(a => a.IsPlayer).ToList();
        if (players.Count != 1)
            throw new InvalidOperationException($"A scenario needs exactly one player actor, found {players.Count}.");

        return new Scenario(_areas.ToArray(), _locations.ToArray(), _routes.ToArray(), _actors.ToArray(), _stores.ToArray(), players[0].Id);
    }

    private static void RequireUnique(IEnumerable<string> ids, string kind)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException($"Empty {kind} id.");
            if (!seen.Add(id))
                throw new InvalidOperationException($"Duplicate {kind} id '{id}'.");
        }
    }
}
