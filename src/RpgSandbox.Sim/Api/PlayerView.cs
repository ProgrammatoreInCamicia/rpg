namespace RpgSandbox.Sim.Api;

/// <summary>
/// What the player can know: the map, what is visible in the player's current area, and the
/// player's own observations. Built by the core, so the client never filters secrets itself.
/// <see cref="SimulationSession.GetWorldView"/> stays the omniscient debug view.
/// </summary>
public sealed record PlayerView
{
    public required GameTime Now { get; init; }

    /// <summary>The player, as the player knows itself.</summary>
    public required ActorId Id { get; init; }
    public LocationId? Location { get; init; }
    public required int Food { get; init; }
    public ActionView? Action { get; init; }
    public TravelView? Travel { get; init; }

    /// <summary>The area the player is in or, while travelling, the area it left (until arrival).</summary>
    public required AreaId Area { get; init; }

    /// <summary>The map is common knowledge.</summary>
    public required IReadOnlyList<AreaView> Areas { get; init; }
    public required IReadOnlyList<LocationView> Locations { get; init; }
    public required IReadOnlyList<RouteView> Routes { get; init; }

    /// <summary>Other actors in the player's area, or travelling to or from it.</summary>
    public required IReadOnlyList<VisibleActorView> VisibleActors { get; init; }

    /// <summary>Stores in the player's area, with their current contents.</summary>
    public required IReadOnlyList<StoreView> VisibleStores { get; init; }

    /// <summary>The player's own knowledge: what it saw and what it was told.</summary>
    public required IReadOnlyList<ObservationView> Observations { get; init; }

    /// <summary>Things the player could tell to someone standing here right now.</summary>
    public required IReadOnlyList<ReportOptionView> ReportOptions { get; init; }
}

/// <summary>Another actor as seen from outside: where it is and what it is visibly doing. No intentions, no knowledge.</summary>
public sealed record VisibleActorView
{
    public required ActorId Id { get; init; }
    public required string Name { get; init; }
    public FactionId? Faction { get; init; }
    public LocationId? Location { get; init; }
    public TravelView? Travel { get; init; }
    /// <summary>
    /// What the player sees this actor doing: only for actors in the player's own place, and only the outward
    /// gesture ("armeggia con le scorte", "parla con X"), never the intention or the topic. Null otherwise.
    /// </summary>
    public string? Doing { get; init; }
}

public sealed record ReportOptionView
{
    public required ActorId Recipient { get; init; }
    public required string RecipientName { get; init; }
    public required ObservationId Observation { get; init; }
    public required string Summary { get; init; }
}
