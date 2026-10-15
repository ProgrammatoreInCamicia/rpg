namespace RpgSandbox.Sim.Api;

/// <summary>A thin boundary between two orthogonally adjacent map squares.</summary>
public enum MapEdgeKind { Wall, Window, Door }

public sealed record MapEdgeView
{
    public required GridPos A { get; init; }
    public required GridPos B { get; init; }
    public required MapEdgeKind Kind { get; init; }

    /// <summary>The square used by DoorCommand; present only for a door boundary.</summary>
    public GridPos? DoorAt { get; init; }

    /// <summary>
    /// P2b: for a window, whether it is open (a shot or a blow passes it; sight and light always do, a body never);
    /// null for any other boundary.
    /// </summary>
    public bool? Open { get; init; }

    /// <summary>P4b: a window's glass (visible); null for any other boundary.</summary>
    public WindowGlass? Glass { get; init; }

    /// <summary>P4b: a window with bars (visible): nobody climbs through it; null for any other boundary.</summary>
    public bool? Barred { get; init; }
}

/// <summary>P4b: a window's glass: whole; broken (shards left: they cut who climbs through); cleared of its shards.</summary>
public enum WindowGlass { Intact, Broken, Cleared }
