namespace RpgSandbox.Sim.Api;

/// <summary>A request from the player or the UI. Commands start actions; they never advance time.</summary>
public abstract record Command
{
    public required ActorId Actor { get; init; }
}

/// <summary>Travel from the actor's current location to a directly connected location.</summary>
public sealed record TravelCommand : Command
{
    public required LocationId Destination { get; init; }
}

/// <summary>Hand over some of the actor's own rations to a store in the actor's current location.</summary>
public sealed record DepositFoodCommand : Command
{
    public required StoreId Store { get; init; }
    public required int Amount { get; init; }
}

/// <summary>
/// Take up to <see cref="Amount"/> rations from a store in the actor's current location. Taking from a
/// store owned by another faction is theft. The amount actually taken is decided at completion.
/// </summary>
public sealed record TakeFoodCommand : Command
{
    public required StoreId Store { get; init; }
    public required int Amount { get; init; }
}

/// <summary>Let time pass. Waiting is an action like any other.</summary>
public sealed record WaitCommand : Command
{
    public required Duration Duration { get; init; }
}

/// <summary>
/// Tell <see cref="Recipient"/>, who must be in the same place, something the actor knows. The content is
/// fixed when the report starts; the recipient learns it when the report completes.
/// </summary>
public sealed record ReportCommand : Command
{
    public required ActorId Recipient { get; init; }
    public required ObservationId Observation { get; init; }
}

/// <summary>Walk to a square of the area map. Replaces a walk in progress; any other action makes the actor busy.</summary>
public sealed record MoveCommand : Command
{
    public required GridPos To { get; init; }

    /// <summary>
    /// Walk up to <see cref="To"/> and stop on the square before it (to talk to someone or reach something there).
    /// Rejected as already there if no step is needed.
    /// </summary>
    public bool StopNextTo { get; init; }

    /// <summary>
    /// Sneak: SRD Slow pace (two thirds of the speed), keeping to the dark where it can, with one Stealth check when the
    /// sneaking starts. Walking normally or doing anything noisy ends it.
    /// </summary>
    public bool Stealthy { get; init; }
}

/// <summary>Stop walking, on the last square reached. The world goes on: how time flows is the client's choice.</summary>
public sealed record StopCommand : Command;

/// <summary>
/// Open or close a door from a square next to it. Instant (SRD: interacting with one object is free); quiet enough not
/// to end sneaking. Walking through a closed door opens it anyway.
/// </summary>
public sealed record DoorCommand : Command
{
    public required GridPos At { get; init; }
    public required bool Open { get; init; }
}

/// <summary>
/// P2b: open or shut the window between the squares <see cref="A"/> and <see cref="B"/>, standing in one of them with
/// the body within 5 feet of it. Not in a fight (like a door).
/// </summary>
public sealed record WindowCommand : Command
{
    public required GridPos A { get; init; }
    public required GridPos B { get; init; }
    public required bool Open { get; init; }
}

/// <summary>
/// P4a: a door (<see cref="Door"/>, its square) or a window (<see cref="A"/> and <see cref="B"/>, its two squares): give one
/// or the other. The actor must be where it could open it (as for <see cref="DoorCommand"/> or <see cref="WindowCommand"/>).
/// </summary>
public abstract record BarrierCommand : Command
{
    public GridPos? Door { get; init; }
    public GridPos? A { get; init; }
    public GridPos? B { get; init; }
}

/// <summary>P4a: lock or unlock a closed door or window with its key (an item carried).</summary>
public sealed record LockCommand : BarrierCommand
{
    public required bool Locked { get; init; }
}

/// <summary>P4a: pick a lock with thieves' tools: a minute, then a Dexterity check (SRD: DC 15). Quiet.</summary>
public sealed record PickLockCommand : BarrierCommand;

/// <summary>P4a: force a locked or stuck door or window: a minute, then a Strength (Athletics) check. Loud.</summary>
public sealed record ForceCommand : BarrierCommand;

/// <summary>P4b: break a window's glass (fragile: no check, SRD). Six seconds, loud; it lets shots and blows through and never shuts again.</summary>
public sealed record BreakGlassCommand : BarrierCommand;

/// <summary>P4b: clear the shards from a broken window: a minute, quiet; then nobody is cut climbing through.</summary>
public sealed record ClearGlassCommand : BarrierCommand;

/// <summary>P4b: climb through an open or broken window without bars, to the square beyond (10 feet of walking).</summary>
public sealed record ClimbCommand : BarrierCommand;

/// <summary>Light a torch (it burns for 1 hour, SRD) or put out the one burning; a torch put out is spent.</summary>
public sealed record TorchCommand : Command
{
    public required bool Lit { get; init; }
}

public enum RejectionReason
{
    None = 0,
    ActorNotFound,
    DestinationNotFound,
    ActorBusy,
    AlreadyThere,
    RouteNotFound,
    UnknownCommand,
    StoreNotFound,
    NotAtStore,
    InvalidAmount,
    InsufficientFood,
    StoreEmpty,
    InvalidDuration,
    RecipientNotPresent,
    UnknownObservation,
    StoreGuarded,
    CapacityExceeded,
    NoMap,
    Unreachable,
    NotMoving,
    OutOfReach,
    NotADoor,
    NotAtExit,
    DoorBlocked,
    NoTorch,
    AlreadyDone,

    // C2: the fight
    NotInCombat,
    NotYourTurn,
    InCombat,
    NoActionLeft,
    NoMovementLeft,
    NotVisible,
    TooFar,
    UnknownAttack,
    NotHostile,

    // C3b: items
    NoAmmo,
    NothingThere,
    NotCarried,

    // D1a: equipment
    NoWeapon,
    Equipped,

    // P1: free movement
    /// <summary>No room there for the walker's body: a wall, a closed space or someone standing too close.</summary>
    NoRoom,

    // P2b: shots on real positions, windows
    /// <summary>Something solid stands on the straight line from the attacker to the target (a wall, a closed door or window).</summary>
    NoLineOfFire,
    NotAWindow,

    // P4a: locks and stuck doors and windows
    Locked,
    Stuck,
    NoKey,
    /// <summary>No lock to lock or unlock: no key fits it.</summary>
    NoLock,
    NotClosed,
    Broken,
    NoTools,
    /// <summary>Nothing to pick or force: it opens by hand.</summary>
    NotHeld,

    // P4b: windows
    /// <summary>A closed window with its glass whole: nobody climbs through.</summary>
    Closed,
    Barred,

    // R1: rests
    /// <summary>A Long Rest less than 16 hours after the end of the last one (SRD).</summary>
    TooSoon,

    // R3b: spells
    /// <summary>No spell slot of that level left, or a slot already spent this turn.</summary>
    NoSlot,
}

public sealed record CommandResult
{
    public bool Success => Rejection == RejectionReason.None;
    public RejectionReason Rejection { get; init; }

    /// <summary>The action started by the command, when it succeeded.</summary>
    public ActionId? Action { get; init; }

    /// <summary>When the started action is due to complete, when it succeeded.</summary>
    public GameTime? CompletesAt { get; init; }

    /// <summary>Human readable text for the UI. Client logic must rely on <see cref="Rejection"/>, not on this text.</summary>
    public string Message { get; init; } = "";

    internal static CommandResult Started(ActionId action, GameTime completesAt, string message) =>
        new() { Action = action, CompletesAt = completesAt, Message = message };

    internal static CommandResult Rejected(RejectionReason reason, string message) =>
        new() { Rejection = reason, Message = message };
}

public enum AdvanceOutcome
{
    /// <summary>The awaited action completed (successfully or not).</summary>
    Completed,
    /// <summary>The time limit was reached before the action completed.</summary>
    TimeLimitReached,
    /// <summary>No pending action with that id exists (unknown or already completed before the call).</summary>
    NotPending,

    /// <summary>The awaited action was cancelled (e.g. an NPC's routine wait interrupted). It did not succeed.</summary>
    Cancelled,

    /// <summary>C2: a fight reached the player's turn: the world waits for the player's commands.</summary>
    YourTurn,
}

public sealed record AdvanceResult
{
    public required AdvanceOutcome Outcome { get; init; }
    public required GameTime Now { get; init; }
}

/// <summary>
/// P1 (free movement prototype): walk to an exact point of the map, not a square, along a short way around walls,
/// closed spaces and hostile creatures (doors on the way are opened, as with <see cref="MoveCommand"/>). Outside a fight
/// only, for now. Rejected as <see cref="RejectionReason.NoRoom"/> if the walker's body does not fit there, as
/// <see cref="RejectionReason.Unreachable"/> if there is no way.
/// </summary>
public sealed record MoveToSpotCommand : Command
{
    public required Spot To { get; init; }

    /// <summary>Sneak, as with <see cref="MoveCommand.Stealthy"/> (Slow pace; this prototype does not seek the dark).</summary>
    public bool Stealthy { get; init; }
}

/// <summary>
/// R1, SRD Short Rest and Long Rest: rest on the spot, outside a fight. Short: 1 hour, then up to <see cref="HitDice"/>
/// Hit Point Dice are spent (each: the die + Constitution modifier). Long: 8 hours of sleep; all Hit Points and Hit
/// Point Dice back. Interrupted by a fight, by damage, or by a noise heard while asleep.
/// </summary>
public sealed record RestCommand : Command
{
    public required bool Long { get; init; }
    public int HitDice { get; init; }
}

/// <summary>
/// R3a, SRD Lay On Hands: a Bonus Action (free outside a fight), touching oneself or a creature within 5 feet: restores
/// <see cref="Amount"/> Hit Points from the pool, or with <see cref="CurePoison"/> spends 5 to end the Poisoned condition.
/// </summary>
public sealed record LayOnHandsCommand : Command
{
    public required ActorId Target { get; init; }
    public int Amount { get; init; }
    public bool CurePoison { get; init; }
}

/// <summary>R3a: makes the choice <see cref="Choice"/> (a <see cref="ChoiceView.Id"/>) with the options picked, in order.</summary>
public sealed record ChooseCommand : Command
{
    public required string Choice { get; init; }
    public required IReadOnlyList<string> Options { get; init; }
}

/// <summary>
/// R3b: casts a prepared spell with a slot of <see cref="SlotLevel"/> (at least the spell's level), on the creatures
/// <see cref="Targets"/> (none for a spell on oneself). <see cref="Condition"/>: what Lesser Restoration ends.
/// </summary>
public sealed record CastCommand : Command
{
    public required string Spell { get; init; }
    public required int SlotLevel { get; init; }
    public IReadOnlyList<ActorId> Targets { get; init; } = Array.Empty<ActorId>();
    public ConditionKind? Condition { get; init; }
}

/// <summary>R3c: uses Channel Divinity for an effect that is not part of an attack: "DivineSense" (a Bonus Action).</summary>
public sealed record ChannelDivinityCommand : Command
{
    public required string Effect { get; init; }
}
