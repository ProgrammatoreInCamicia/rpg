using Godot;

namespace RpgSandbox.Game;

/// <summary>Isometric projection helpers (2:1 diamonds). Presentation only: the simulation never sees coordinates.</summary>
public static class Iso
{
    public const float TileWidth = 128f;
    public const float TileHeight = 64f;

    /// <summary>Screen position of the centre of a grid cell (fractional cells allowed).</summary>
    public static Vector2 CellToScreen(Vector2 cell) =>
        new((cell.X - cell.Y) * TileWidth / 2f, (cell.X + cell.Y) * TileHeight / 2f);

    /// <summary>Diamond outline of a footprint of <paramref name="size"/> cells centred on the origin.</summary>
    public static Vector2[] Diamond(float size = 1f)
    {
        var hw = TileWidth / 2f * size;
        var hh = TileHeight / 2f * size;
        return new[] { new Vector2(0, -hh), new Vector2(hw, 0), new Vector2(0, hh), new Vector2(-hw, 0) };
    }

    /// <summary>
    /// The three visible faces (top, left, right) of a box standing on a diamond footprint, with the
    /// origin at the centre of the footprint, so that Y-sorting uses the base of the box.
    /// </summary>
    public static (Vector2[] Top, Vector2[] Left, Vector2[] Right) Box(float size, float height)
    {
        var d = Diamond(size);
        Vector2 up = new(0, -height);
        Vector2[] top = { d[0] + up, d[1] + up, d[2] + up, d[3] + up };
        Vector2[] left = { d[3], d[2], d[2] + up, d[3] + up };
        Vector2[] right = { d[2], d[1], d[1] + up, d[2] + up };
        return (top, left, right);
    }
}
