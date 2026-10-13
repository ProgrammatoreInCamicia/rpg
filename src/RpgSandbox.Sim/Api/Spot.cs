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

    /// <summary>Seconds the walk takes at the walker's pace (0 in a fight: moves happen within the turn).</summary>
    public long Seconds { get; init; }

    /// <summary>P1b, in a fight: the movement left this turn, in feet (to the inch), before this move.</summary>
    public double FeetLeft { get; init; }

    /// <summary>P1b, in a fight: the Opportunity Attacks the move would provoke, by threats the player knows of.</summary>
    public IReadOnlyList<SpotProvocationView> Provokes { get; init; } = Array.Empty<SpotProvocationView>();
}

/// <summary>P1b: an Opportunity Attack a free move would provoke, and where: the last point of the way still in reach.</summary>
public sealed record SpotProvocationView
{
    public required ActorId By { get; init; }
    public required string Name { get; init; }
    public required Spot At { get; init; }

    /// <summary>Feet walked along the way when it comes.</summary>
    public required double AfterFeet { get; init; }
}
