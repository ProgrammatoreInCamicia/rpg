using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>Tunable rule constants. Kept together so playtesting can adjust them in one place.</summary>
internal static class Rules
{
    public static readonly Duration DepositFoodDuration = Duration.FromMinutes(2);
    public static readonly Duration TakeFoodDuration = Duration.FromMinutes(3);

    /// <summary>How long an NPC with nothing to do rests before deciding again.</summary>
    public static readonly Duration IdleRestDuration = Duration.FromHours(1);

    /// <summary>Back-off when an NPC's chosen command is unexpectedly rejected, to avoid deciding in a loop.</summary>
    public static readonly Duration DecisionRetryDelay = Duration.FromMinutes(10);
}
