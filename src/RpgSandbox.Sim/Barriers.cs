using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>
/// P4a: which door or window: a door by its square (<see cref="A"/> = <see cref="B"/> = the door's square, also for a
/// door on a boundary), a window by the two squares it lies between (in reading order).
/// </summary>
internal readonly record struct BarrierKey(GridPos A, GridPos B)
{
    public bool IsDoor => A == B;

    public static BarrierKey Door(GridPos at) => new(at, at);

    public static BarrierKey Window(GridPos a, GridPos b) => (a.Y, a.X).CompareTo((b.Y, b.X)) <= 0 ? new(a, b) : new(b, a);

    public static readonly IComparer<BarrierKey> Order = Comparer<BarrierKey>.Create((x, y) =>
        (x.A.Y, x.A.X, x.B.Y, x.B.X).CompareTo((y.A.Y, y.A.X, y.B.Y, y.B.X)));

    public override string ToString() => IsDoor ? $"porta in {A}" : $"finestra fra {A} e {B}";
}

/// <summary>
/// P4a: a door or window that does not simply open: locked (its key, an item, opens it; it can be picked at DC 15 or
/// forced), stuck (only forced), or broken (forced open while locked: it never locks again). Hidden from the player
/// until it tries.
/// </summary>
internal sealed class Barrier
{
    /// <summary>The item that locks and unlocks it; null if no key fits (a door that only sticks).</summary>
    public string? Key { get; init; }

    public bool Locked { get; set; }
    public bool Stuck { get; set; }
    public bool Broken { get; set; }

    /// <summary>The Strength (Athletics) DC to force it.</summary>
    public required int ForceDc { get; init; }

    /// <summary>What the world shows of how hard it is (never a number); a default by <see cref="ForceDc"/> if none.</summary>
    public string? Clue { get; init; }

    /// <summary>P4b: a window with bars: nobody climbs through it until they are forced off.</summary>
    public bool Barred { get; set; }

    /// <summary>P4b: the Strength (Athletics) DC to force the bars off (SRD: 25 very hard; 20 rusty).</summary>
    public int BarsDc { get; init; } = 25;

    public string? BarsClue { get; init; }

    public string BarsClueText => BarsClue ?? (BarsDc <= 20 ? "le sbarre sono arrugginite" : "le sbarre sono robuste, murate nella pietra");

    /// <summary>It does not open by hand now.</summary>
    public bool Holds => Locked || Stuck;

    public string ClueText => Clue ?? ForceDc switch
    {
        <= 10 => "il legno è gonfio e marcio",
        <= 15 => "è robusta",
        _ => "è rinforzata con bande di ferro",
    };
}
