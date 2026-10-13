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

    /// <summary>The player's square and walk in progress on a mapped area.</summary>
    public GridPos? Position { get; init; }
    public MoveView? Move { get; init; }

    /// <summary>The map of the player's area, if it has one (common knowledge, like the place names).</summary>
    public MapView? Map { get; init; }

    /// <summary>
    /// Light on each square of <see cref="Map"/> right now, one character per square: '0' dark, '1' dim, '2' bright.
    /// These are the levels the rules use; any softening between them is presentation.
    /// </summary>
    public IReadOnlyList<string>? MapLight { get; init; }

    /// <summary>Interior prototype: '2' visible now, '1' remembered, '0' never seen.</summary>
    public IReadOnlyList<string>? MapVisibility { get; init; }

    /// <summary>Light where the player stands (daylight, lamps): it decides what can be seen (see the rules notes).</summary>
    public Light Light { get; init; }

    /// <summary>Daylight outdoors right now, whatever lamps are near: day, dawn or dusk, night.</summary>
    public Light Daylight { get; init; }

    /// <summary>While sneaking: the player's own Stealth total (the DC to notice it in dim light). Null otherwise.</summary>
    public int? Sneaking { get; init; }

    /// <summary>Unlit torches carried, and when the one burning goes out (null if none burns).</summary>
    public int Torches { get; init; }
    public GameTime? TorchLitUntil { get; init; }

    /// <summary>
    /// Doors of the player's map and whether each is open. ADAPTATION: the state of every door is shown, even one out
    /// of sight.
    /// </summary>
    public IReadOnlyList<DoorView> Doors { get; init; } = Array.Empty<DoorView>();

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

    /// <summary>C5a: the kinds of creatures the player has learned something about.</summary>
    public IReadOnlyList<KindCardView> KnownKinds { get; init; } = Array.Empty<KindCardView>();

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

    /// <summary>C2: the fight the player is in; null outside a fight. While it is the player's turn the world is still.</summary>
    public CombatView? Combat { get; init; }

    /// <summary>C2: the player is dead. The world may go on; the client offers to load a game.</summary>
    public bool Dead { get; init; }

    /// <summary>
    /// The player lies unconscious (dying, stable or knocked out). Outside a fight the client lets the hours pass with
    /// <see cref="SimulationSession.AdvanceWhileUnconscious"/> instead of waiting them at the exploration pace.
    /// </summary>
    public bool Unconscious { get; init; }

    /// <summary>C5b: the player's level (SRD Character Advancement); null if it is a creature, not a character.</summary>
    public int? Level { get; init; }

    /// <summary>C5b: its own Experience Points, and those it needs for the next level (null at level 20 or without a level).</summary>
    public long Experience { get; init; }
    public int? NextLevelAt { get; init; }

    /// <summary>C2: the attacks of the player's combat profile, in the profile's order.</summary>
    public IReadOnlyList<AttackOptionView> Attacks { get; init; } = Array.Empty<AttackOptionView>();

    /// <summary>C3b: what the player carries (torches included), and its coins in copper pieces.</summary>
    public IReadOnlyList<ItemStackView> Inventory { get; init; } = Array.Empty<ItemStackView>();
    public long Coins { get; init; }

    /// <summary>D1a: what it wears and wields, slot by slot.</summary>
    public IReadOnlyList<EquippedView> Equipment { get; init; } = Array.Empty<EquippedView>();

    /// <summary>C3b: items lying on the squares of the player's map that it sees (in sight with light, or next to it).</summary>
    public IReadOnlyList<GroundItemView> GroundItems { get; init; } = Array.Empty<GroundItemView>();
}

/// <summary>Another actor as seen from outside: where it is and what it is visibly doing. No intentions, no knowledge.</summary>
public sealed record VisibleActorView
{
    public required ActorId Id { get; init; }
    public required string Name { get; init; }
    public FactionId? Faction { get; init; }
    public LocationId? Location { get; init; }
    public GridPos? Position { get; init; }
    public MoveView? Move { get; init; }
    public TravelView? Travel { get; init; }
    /// <summary>
    /// What the player sees this actor doing: only for actors in the player's own place, and only the outward
    /// gesture ("armeggia con le scorte", "parla con X"), never the intention or the topic. Null otherwise.
    /// </summary>
    public string? Doing { get; init; }

    /// <summary>
    /// On maps: whether this actor sees the player right now (SRD: if you can see a creature, you can discern whether
    /// it can see you). Null off the maps.
    /// </summary>
    public bool? SeesYou { get; init; }

    /// <summary>Carrying a burning torch (visible from afar).</summary>
    public bool Torch { get; init; }

    /// <summary>C2: how hurt it looks, the C1 descriptor ("illeso", "ferito", "malconcio", ...); never the exact Hit Points.</summary>
    public string? Condition { get; init; }

    /// <summary>
    /// C4: this actor holds the player hostile right now (it would fight it). ADAPTATION of the interface, BG style: an
    /// attitude one can read at a glance, never the reason nor what it knows.
    /// </summary>
    public bool Hostile { get; init; }

    /// <summary>C5a: what the player knows of it (right click → information): only what it saw and made out.</summary>
    public CreatureCardView? Card { get; init; }
}

/// <summary>T6c-5: how much the player knows of a door.</summary>
public enum DoorKnowledge
{
    /// <summary>Never seen: whether it is open is unknown.</summary>
    Unknown,

    /// <summary>Seen before: its state is the one seen then.</summary>
    Remembered,

    /// <summary>In sight (or next to the player) right now: its state is the current one.</summary>
    Seen,
}

public sealed record DoorView
{
    public required GridPos At { get; init; }
    public required DoorKnowledge Known { get; init; }

    /// <summary>Open or closed as the player knows it (now if Seen, then if Remembered); null if Unknown.</summary>
    public bool? Open { get; init; }

    /// <summary>When the player last saw it (now if Seen); null if Unknown.</summary>
    public GameTime? SeenAt { get; init; }
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
