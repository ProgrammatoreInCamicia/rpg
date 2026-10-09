using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>Tunable rule constants. Kept together so playtesting can adjust them in one place.</summary>
internal static class Rules
{
    public static readonly Duration DepositFoodDuration = Duration.FromMinutes(2);
}
