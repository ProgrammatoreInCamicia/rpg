using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>Tunable constants (durations, lengths, windows). Kept together so playtesting can adjust them in one place.</summary>
internal static class Tuning
{
    public static readonly Duration DepositFoodDuration = Duration.FromMinutes(2);
    public static readonly Duration TakeFoodDuration = Duration.FromMinutes(3);
    public static readonly Duration ReportDuration = Duration.FromMinutes(5);
    public static readonly Duration ConfiscateDuration = Duration.FromMinutes(2);

    /// <summary>How long an NPC with nothing to do rests before deciding again (shortened to the next shift boundary).</summary>
    public static readonly Duration IdleRestDuration = Duration.FromHours(1);

    /// <summary>Back-off when an NPC's chosen command is unexpectedly rejected, to avoid deciding in a loop.</summary>
    public static readonly Duration DecisionRetryDelay = Duration.FromMinutes(10);

    /// <summary>How long the authority guards a store after learning it was robbed.</summary>
    public static readonly Duration GuardDutyLength = Duration.FromDays(3);

    /// <summary>Length of one guard shift; the guard re-decides between shifts.</summary>
    public static readonly Duration GuardShift = Duration.FromHours(1);

    /// <summary>How long a villager keeps an eye on a robbed store after learning of the theft.</summary>
    public static readonly Duration VigilLength = Duration.FromDays(3);

    /// <summary>A vigilant villager watches the store between these times of day.</summary>
    public static readonly Duration VigilStart = Duration.FromHours(7);
    public static readonly Duration VigilEnd = Duration.FromHours(18);

    /// <summary>ADAPTATION: on maps, sounds are heard within this many squares, walking around walls (30 ft).</summary>
    public const int HearingRadius = 6;

    /// <summary>ADAPTATION: a closed door muffles sound as much as this many extra squares.</summary>
    public const int ClosedDoorSoundSteps = 3;

    /// <summary>ADAPTATION (T6c-3): a raid on a mapped village sets off once it is dark, at this time of day.</summary>
    public static readonly Duration RaidAfterDark = Duration.FromHours(20);

    /// <summary>SRD Torch: burns for 1 hour, Bright Light 20 ft and Dim Light 20 ft more.</summary>
    public static readonly Duration TorchBurns = Duration.FromHours(1);
    public const int TorchBrightFeet = 20;
    public const int TorchDimFeet = 20;

    /// <summary>How long a raiding faction avoids a target its member found guarded.</summary>
    public static readonly Duration AvoidGuardedTarget = Duration.FromHours(24);
}
