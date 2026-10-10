using RpgSandbox.Sim.Rules;

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

    /// <summary>The player's character sheet.</summary>
    public required CharacterSheet Sheet { get; init; }

    public ActionView? Action { get; init; }
    public TravelView? Travel { get; init; }

    /// <summary>Light where the player is, by time of day: it decides what can be seen (see the rules notes).</summary>
    public Light Light { get; init; }

    /// <summary>People right next to the player that it cannot see (darkness): only their presence is known.</summary>
    public int UnseenNearby { get; init; }

    /// <summary>The area the player is in or, while travelling, the area it left (until arrival).</summary>
    public required AreaId Area { get; init; }

    /// <summary>The map is common knowledge.</summary>
    public required IReadOnlyList<AreaView> Areas { get; init; }
    public required IReadOnlyList<LocationView> Locations { get; init; }
    public required IReadOnlyList<RouteView> Routes { get; init; }

    /// <summary>Other actors the player can see: in its area where there is light, on the road only by daylight.</summary>
    public required IReadOnlyList<VisibleActorView> VisibleActors { get; init; }

    /// <summary>Stores in the player's area that the player can see (lit, or where the player stands).</summary>
    public required IReadOnlyList<StoreView> VisibleStores { get; init; }

    /// <summary>The player's own knowledge: what it saw and what it was told.</summary>
    public required IReadOnlyList<ObservationView> Observations { get; init; }

    /// <summary>Things the player could tell to someone standing here right now. Superseded by <see cref="PeopleHere"/>.</summary>
    public required IReadOnlyList<ReportOptionView> ReportOptions { get; init; }

    /// <summary>
    /// Everyone the player can talk to right now (same place), with what the player could tell each of them.
    /// Present even with no topics, so the client can offer "Parla con…" and say there is nothing new to tell.
    /// </summary>
    public IReadOnlyList<PersonView> PeopleHere { get; init; } = Array.Empty<PersonView>();

    /// <summary>Recent facts the player took part in (oldest first): what happened to the player, as the player knows it.</summary>
    public IReadOnlyList<FactView> RecentEvents { get; init; } = Array.Empty<FactView>();
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

/// <summary>Someone the player can talk to, as seen by the player.</summary>
public sealed record PersonView
{
    public required ActorId Id { get; init; }
    public required string Name { get; init; }
    public string? Doing { get; init; }
    public required IReadOnlyList<TopicView> Topics { get; init; }
}

/// <summary>Something the player can bring up in a conversation.</summary>
public sealed record TopicView
{
    /// <summary>"Tell" for now: tell what you know about a theft. New kinds are announced before they appear.</summary>
    public required string Kind { get; init; }
    public required ObservationId Observation { get; init; }
    public required string Summary { get; init; }
}
