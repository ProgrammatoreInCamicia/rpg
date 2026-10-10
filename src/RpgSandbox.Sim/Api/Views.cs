using RpgSandbox.Sim.Rules;

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

    /// <summary>Rations carried.</summary>
    public int Food { get; init; }

    /// <summary>The action in progress, of any kind. Null when the actor is free.</summary>
    public ActionView? Action { get; init; }

    /// <summary>Travel-specific details, set only while travelling.</summary>
    public TravelView? Travel { get; init; }

    public FactionId? Faction { get; init; }

    /// <summary>The faction assignment being carried out, if any (NPCs only).</summary>
    public AssignmentView? Assignment { get; init; }

    /// <summary>Why the NPC is doing what it does (debug). Null for the player.</summary>
    public DecisionView? LastDecision { get; init; }

    /// <summary>Where the NPC lives (null for the player).</summary>
    public LocationId? Home { get; init; }

    /// <summary>When the actor arrived where it is (debug: decides who recognises a thief).</summary>
    public GameTime ArrivedAt { get; init; }

    /// <summary>What the actor knows (debug for NPCs; the player sees its own through <see cref="PlayerView"/>).</summary>
    public IReadOnlyList<ObservationView> Knowledge { get; init; } = Array.Empty<ObservationView>();

    public GuardDutyView? GuardDuty { get; init; }

    /// <summary>Set while the actor keeps an eye on a robbed store (debug).</summary>
    public VigilView? Vigil { get; init; }

    /// <summary>What the rules know about the actor (debug for NPCs; the player sees its own in <see cref="PlayerView"/>).</summary>
    public CharacterSheet? Sheet { get; init; }

    /// <summary>Square on the area map (mapped areas only); mid-walk, the last square reached.</summary>
    public GridPos? Position { get; init; }

    /// <summary>The walk in progress, if any.</summary>
    public MoveView? Move { get; init; }

    /// <summary>Stealth total while sneaking (debug).</summary>
    public int? Sneak { get; init; }

    public int Torches { get; init; }
    public GameTime? TorchLitUntil { get; init; }

    /// <summary>Thefts this authority wants made good (debug).</summary>
    public IReadOnlyList<ClaimView> Claims { get; init; } = Array.Empty<ClaimView>();
}

public sealed record ClaimView
{
    public required ActorId Thief { get; init; }
    public required StoreId Store { get; init; }
    public required int Owed { get; init; }
}

public sealed record VigilView
{
    public required StoreId Store { get; init; }
    public required GameTime Until { get; init; }
}

public sealed record GuardDutyView
{
    public required StoreId Store { get; init; }
    public required GameTime Since { get; init; }
    public required GameTime Until { get; init; }
}

/// <summary>What an actor knows about a theft, as perceived or as told. Never a read of the current world.</summary>
public sealed record ObservationView
{
    public required ObservationId Id { get; init; }

    /// <summary>The first-hand observation this one descends from (itself, if first-hand).</summary>
    public required ObservationId Origin { get; init; }

    /// <summary>"Theft" for now. New kinds are announced before they appear.</summary>
    public required string Kind { get; init; }
    public required StoreId Store { get; init; }
    public required string StoreName { get; init; }
    public required LocationId Location { get; init; }
    public required int Amount { get; init; }

    /// <summary>Null when the thief was not recognised.</summary>
    public ActorId? Thief { get; init; }
    public string? ThiefName { get; init; }

    public required GameTime ObservedAt { get; init; }
    public required GameTime LearnedAt { get; init; }

    /// <summary>Null when seen first-hand; otherwise who told it.</summary>
    public ActorId? Source { get; init; }
    public string? SourceName { get; init; }

    /// <summary>"Seen" or "Heard": how the first-hand witness perceived it (kept when passed on).</summary>
    public required string Perceived { get; init; }

    public required IReadOnlyList<ActorId> ToldTo { get; init; }
}

public sealed record AssignmentView
{
    /// <summary>"Raid" for now. New kinds are announced before they appear.</summary>
    public required string Kind { get; init; }
    public required FactionId Faction { get; init; }
    public required StoreId Target { get; init; }
    public required StoreId Home { get; init; }
    public required int Amount { get; init; }
    public required GameTime AssignedAt { get; init; }
    public required bool TakeAttempted { get; init; }

    /// <summary>The member gave up (target guarded) and is going home to tell.</summary>
    public required bool Aborted { get; init; }
}

/// <summary>A recorded decision: which rule fired, why, and the data it read.</summary>
public sealed record DecisionView
{
    public required GameTime At { get; init; }
    public required string Rule { get; init; }
    public required string Reason { get; init; }
    public required IReadOnlyList<string> Inputs { get; init; }
}

public sealed record FactionView
{
    public required FactionId Id { get; init; }
    public required string Name { get; init; }
    public StoreId? HomeStore { get; init; }
    public required int DailyUpkeep { get; init; }
    public required IReadOnlyList<ActorId> Members { get; init; }

    /// <summary>Null for factions without a policy.</summary>
    public GameTime? NextEvaluation { get; init; }
    public DecisionView? LastDecision { get; init; }

    /// <summary>Who receives reports for this faction.</summary>
    public ActorId? Authority { get; init; }

    /// <summary>Raid targets the faction avoids, and until when.</summary>
    public IReadOnlyList<AvoidedTargetView> AvoidedTargets { get; init; } = Array.Empty<AvoidedTargetView>();
}

public sealed record AvoidedTargetView
{
    public required StoreId Store { get; init; }
    public required GameTime Until { get; init; }
}

public sealed record ActionView
{
    public required ActionId Id { get; init; }

    /// <summary>"Travel", "DepositFood", "TakeFood", "Wait", "Report" or "Guard". New kinds are announced before they appear.</summary>
    public required string Kind { get; init; }

    public required GameTime StartedAt { get; init; }
    public required GameTime CompletesAt { get; init; }
    public required string Description { get; init; }
}

public sealed record StoreView
{
    public required StoreId Id { get; init; }
    public required string Name { get; init; }
    public required LocationId Location { get; init; }
    public required int Food { get; init; }
    public FactionId? Owner { get; init; }

    /// <summary>Square the store occupies on a mapped area (used from an adjacent square).</summary>
    public GridPos? Position { get; init; }

    /// <summary>T6c: squares the store is used from, if declared (otherwise any adjacent square).</summary>
    public IReadOnlyList<GridPos> Access { get; init; } = Array.Empty<GridPos>();
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
    public required IReadOnlyList<StoreView> Stores { get; init; }
    public required IReadOnlyList<FactionView> Factions { get; init; }

    /// <summary>Most recent facts, oldest first.</summary>
    public required IReadOnlyList<FactView> RecentFacts { get; init; }
}

/// <summary>A walk in progress: the client animates exactly this (square i is reached at DepartedAt + ceil(i·30/Speed) s).</summary>
public sealed record MoveView
{
    public required GridPos From { get; init; }
    public required IReadOnlyList<GridPos> Path { get; init; }
    public required GameTime DepartedAt { get; init; }

    /// <summary>Feet per 6-second round; Speed/5 squares per round.</summary>
    public required int Speed { get; init; }

    /// <summary>Moving at the Slow pace, sneaking (an outward gesture: anyone who sees the walker sees it creep).</summary>
    public bool Stealthy { get; init; }
}

/// <summary>An area's walkable map, as given to the scenario: one character per 5-ft square.</summary>
public sealed record MapView
{
    public required AreaId Area { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>'#' blocked, '.' open ground, a letter a square of the place in <see cref="Zones"/>, 'S' a store.</summary>
    public required IReadOnlyList<string> Rows { get; init; }
    public required IReadOnlyDictionary<char, LocationId> Zones { get; init; }

    /// <summary>T6c: exits of the map (common knowledge, like the roads): where journeys start and where they lead.</summary>
    public IReadOnlyList<ExitView> Exits { get; init; } = Array.Empty<ExitView>();
}

public sealed record ExitView
{
    public required LocationId Location { get; init; }
    public required string Name { get; init; }
    public required GridPos At { get; init; }

    /// <summary>Places a journey from this exit can reach, with the time it takes.</summary>
    public required IReadOnlyList<RouteView> Routes { get; init; }
}
