using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>R5c: a trap on a map square (a pit's lid, or the lock of the door on that square).</summary>
internal sealed class Trap
{
    public required TrapKind Kind { get; init; }

    /// <summary>The player knows of it (it searched and found it, or it sprang).</summary>
    public bool Found { get; set; }

    /// <summary>It went off: a pit's lid stays open, a needle is spent (ADAPTATION: one sting).</summary>
    public bool Sprung { get; set; }

    /// <summary>Made safe: a spike under the lid, the needle taken out.</summary>
    public bool Disarmed { get; set; }

    public bool Live => !Sprung && !Disarmed;
}
