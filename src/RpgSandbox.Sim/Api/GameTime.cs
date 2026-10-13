namespace RpgSandbox.Sim.Api;

/// <summary>
/// A span of game time, counted in ticks: <see cref="GameTime.TicksPerSecond"/> to the second (P1c), so a walk can
/// stop between two seconds exactly where it is. Built from seconds as before (a 5e round lasts 6 seconds).
/// </summary>
public readonly record struct Duration : IComparable<Duration>
{
    public static readonly Duration Zero = FromTicks(0);

    /// <summary>A span of <paramref name="seconds"/> seconds.</summary>
    public Duration(long seconds) => Ticks = checked(seconds * GameTime.TicksPerSecond);

    private Duration(long ticks, bool _) => Ticks = ticks;

    public long Ticks { get; }

    /// <summary>Whole seconds, rounded down.</summary>
    public long Seconds => GameTime.FloorDiv(Ticks, GameTime.TicksPerSecond);

    public static Duration FromTicks(long ticks) => new(ticks, true);
    public static Duration FromSeconds(long seconds) => new(seconds);
    public static Duration FromMinutes(long minutes) => new(checked(minutes * 60));
    public static Duration FromHours(long hours) => new(checked(hours * 3600));
    public static Duration FromDays(long days) => new(checked(days * 86_400));

    public int CompareTo(Duration other) => Ticks.CompareTo(other.Ticks);
    public override string ToString() => Ticks % GameTime.TicksPerSecond == 0 ? $"{Seconds}s" : $"{Ticks}t";
}

/// <summary>An instant of game time since the start of the scenario, in ticks (P1c); built from seconds as before.</summary>
public readonly record struct GameTime : IComparable<GameTime>
{
    /// <summary>P1c: the clock's grain. At Speed 30 a walker covers one inch a tick.</summary>
    public const long TicksPerSecond = 60;

    public static readonly GameTime Start = FromTicks(0);

    /// <summary>The instant <paramref name="seconds"/> seconds after the start.</summary>
    public GameTime(long seconds) => Ticks = checked(seconds * TicksPerSecond);

    private GameTime(long ticks, bool _) => Ticks = ticks;

    public long Ticks { get; }

    /// <summary>Whole seconds since the start, rounded down.</summary>
    public long Seconds => FloorDiv(Ticks, TicksPerSecond);

    public static GameTime FromTicks(long ticks) => new(ticks, true);

    public GameTime Plus(Duration duration) => FromTicks(checked(Ticks + duration.Ticks));
    public Duration Since(GameTime earlier) => Duration.FromTicks(Ticks - earlier.Ticks);

    public long Day => Seconds / 86_400;
    public int Hour => (int)(Seconds % 86_400 / 3600);
    public int Minute => (int)(Seconds % 3600 / 60);
    public int Second => (int)(Seconds % 60);

    public int CompareTo(GameTime other) => Ticks.CompareTo(other.Ticks);
    public static bool operator <(GameTime a, GameTime b) => a.Ticks < b.Ticks;
    public static bool operator >(GameTime a, GameTime b) => a.Ticks > b.Ticks;
    public static bool operator <=(GameTime a, GameTime b) => a.Ticks <= b.Ticks;
    public static bool operator >=(GameTime a, GameTime b) => a.Ticks >= b.Ticks;

    public override string ToString() => $"Day {Day} {Hour:00}:{Minute:00}:{Second:00}" + (Ticks % TicksPerSecond == 0 ? "" : $"+{Ticks % TicksPerSecond}t");

    internal static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}
