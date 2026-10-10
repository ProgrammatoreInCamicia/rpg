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
}

public sealed record AdvanceResult
{
    public required AdvanceOutcome Outcome { get; init; }
    public required GameTime Now { get; init; }
}
