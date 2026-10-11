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
}
