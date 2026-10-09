namespace RpgSandbox.Sim.Rules;

/// <summary>
/// The simulation's only source of randomness: SplitMix64 (Steele, Lea, Flood 2014; reference
/// implementation by S. Vigna). Its whole state is one 64-bit number, saved with the game, so a
/// loaded game continues the exact same sequence. Never use System.Random in the simulation.
/// </summary>
internal sealed class SplitMix64
{
    /// <summary>Saved next to the state: a different algorithm must never read an old state.</summary>
    public const string Algorithm = "splitmix64-v1";

    public SplitMix64(ulong state) => State = state;

    public ulong State { get; private set; }

    public ulong NextUInt64()
    {
        var z = State += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform integer in [0, <paramref name="exclusiveMax"/>), without modulo bias (rejection sampling).</summary>
    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        var range = (ulong)exclusiveMax;
        var limit = ulong.MaxValue - ulong.MaxValue % range; // largest multiple of range
        ulong value;
        do value = NextUInt64(); while (value >= limit);
        return (int)(value % range);
    }

    /// <summary>One roll of a die with <paramref name="sides"/> faces: 1..sides.</summary>
    public int Roll(int sides) => NextInt(sides) + 1;
}
