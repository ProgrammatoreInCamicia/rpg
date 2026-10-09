namespace RpgSandbox.Sim.Api;

// Read-only copies of simulation state for the client.
// Views are nominal records (init properties, not positional) so new properties can be added
// without breaking callers. Collections are fresh read-only copies, sorted by id: mutating the
// world never changes a view already handed out, and views cannot be cast back to mutable state.

public sealed record AreaView
{
    public required AreaId Id { get; init; }
    public required string Name { get; init; }
}

public sealed record LocationView
{
    public required LocationId Id { get; init; }
    public required string Name { get; init; }
    public required AreaId Area { get; init; }
}

/// <summary>A direct connection between two locations. Routes are symmetric; each is listed once per direction.</summary>
public sealed record RouteView
{
    public required LocationId From { get; init; }
    public required LocationId To { get; init; }
    public required Duration TravelTime { get; init; }
}

public sealed record TravelView
{
    public required ActionId Action { get; init; }
    public required LocationId Origin { get; init; }
    public required LocationId Destination { get; init; }
    public required GameTime DepartedAt { get; init; }
    public required GameTime ArrivesAt { get; init; }
}

public sealed record ActorView
{
    public required ActorId Id { get; init; }
    public required string Name { get; init; }
    public required bool IsPlayer { get; init; }

    /// <summary>Where the actor is. Null while travelling: a traveller is at neither end of the route.</summary>
    public LocationId? Location { get; init; }

    public TravelView? Travel { get; init; }
}

/// <summary>A fact recorded in the world history, as shown on the debug timeline.</summary>
public sealed record FactView
{
    public required FactId Id { get; init; }
    public required GameTime At { get; init; }
    public required string Kind { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// The complete, omniscient state of the world. Intended for rendering in the prototype and for the
/// debug panel. A player-filtered view will be added when knowledge exists (increment 3).
/// </summary>
public sealed record WorldView
{
    public required GameTime Now { get; init; }
    public required ActorId Player { get; init; }
    public required IReadOnlyList<AreaView> Areas { get; init; }
    public required IReadOnlyList<LocationView> Locations { get; init; }
    public required IReadOnlyList<RouteView> Routes { get; init; }
    public required IReadOnlyList<ActorView> Actors { get; init; }

    /// <summary>Most recent facts, oldest first.</summary>
    public required IReadOnlyList<FactView> RecentFacts { get; init; }
}
