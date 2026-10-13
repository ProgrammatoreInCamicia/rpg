namespace RpgSandbox.Sim.Api;

/// <summary>
/// P1 (free movement prototype): an exact point on an area map, in inches from the map's top-left corner. A square is
/// <see cref="PerSquare"/> inches (5 feet), so the square a spot lies in is its coordinates divided by 60. Integers only,
/// so positions are deterministic and saved exactly.
/// </summary>
public readonly record struct Spot(int X, int Y)
{
    public const int PerFoot = 12;
    public const int PerSquare = 5 * PerFoot;

    /// <summary>The square this spot lies in (a spot on a border belongs to the square to its right or below).</summary>
    public GridPos Cell => new(FloorDiv(X), FloorDiv(Y));

    /// <summary>The centre of a square.</summary>
    public static Spot CenterOf(GridPos square) => new(square.X * PerSquare + PerSquare / 2, square.Y * PerSquare + PerSquare / 2);

    /// <summary>Straight-line distance in inches.</summary>
    public double DistanceTo(Spot other) => Math.Sqrt(Square(other.X - (long)X) + Square(other.Y - (long)Y));

    public override string ToString() => $"({X},{Y})\"";

    private static int FloorDiv(int v) => v >= 0 ? v / PerSquare : (v - PerSquare + 1) / PerSquare;
    private static double Square(long v) => (double)(v * v);
}

/// <summary>
/// P1: what a <see cref="MoveToSpotCommand"/> would do, computed by the same code as the walk, without touching the
/// world.
/// </summary>
public sealed record SpotMovePreview
{
    public required bool Valid { get; init; }
    public RejectionReason Reason { get; init; }
    public string Message { get; init; } = "";

    /// <summary>The way as its turning points, from where the walker stands to the goal.</summary>
    public IReadOnlyList<Spot> Way { get; init; } = Array.Empty<Spot>();

    /// <summary>Its length in feet, rounded up to a tenth (a diagonal is as long as it is: about 7.1 feet a square).</summary>
    public double Feet { get; init; }

    /// <summary>Seconds the walk takes at the walker's pace.</summary>
    public long Seconds { get; init; }
}
