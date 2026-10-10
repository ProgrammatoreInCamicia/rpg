namespace RpgSandbox.Sim.Api;

/// <summary>A square on an area map. Each square is 5 feet (SRD 5.2.1, "Playing on a Grid").</summary>
public readonly record struct GridPos(int X, int Y) : IComparable<GridPos>
{
    /// <summary>Squares of movement between two squares when diagonals cost 1 (SRD grid rule): Chebyshev distance.</summary>
    public int StepsTo(GridPos other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    /// <summary>Same square or one of the eight around it.</summary>
    public bool IsAdjacentOrSame(GridPos other) => StepsTo(other) <= 1;

    public int CompareTo(GridPos other) => Y != other.Y ? Y.CompareTo(other.Y) : X.CompareTo(other.X);
    public override string ToString() => $"({X},{Y})";
}
