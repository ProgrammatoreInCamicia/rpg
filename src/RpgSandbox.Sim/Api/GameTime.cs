namespace RpgSandbox.Sim.Api;

/// <summary>A span of game time. The unit is the second (a 5e round lasts 6 seconds).</summary>
public readonly record struct Duration(long Seconds) : IComparable<Duration>
{
    public static readonly Duration Zero = new(0);

    public static Duration FromSeconds(long seconds) => new(seconds);
    public static Duration FromMinutes(long minutes) => new(checked(minutes * 60));
    public static Duration FromHours(long hours) => new(checked(hours * 3600));
    public static Duration FromDays(long days) => new(checked(days * 86_400));

    public int CompareTo(Duration other) => Seconds.CompareTo(other.Seconds);
    public override string ToString() => $"{Seconds}s";
}

/// <summary>An instant of game time: seconds elapsed since the start of the scenario.</summary>
public readonly record struct GameTime(long Seconds) : IComparable<GameTime>
{
    public static readonly GameTime Start = new(0);

    public GameTime Plus(Duration duration) => new(checked(Seconds + duration.Seconds));
    public Duration Since(GameTime earlier) => new(Seconds - earlier.Seconds);

    public long Day => Seconds / 86_400;
    public int Hour => (int)(Seconds % 86_400 / 3600);
    public int Minute => (int)(Seconds % 3600 / 60);
    public int Second => (int)(Seconds % 60);

    public int CompareTo(GameTime other) => Seconds.CompareTo(other.Seconds);
    public static bool operator <(GameTime a, GameTime b) => a.Seconds < b.Seconds;
    public static bool operator >(GameTime a, GameTime b) => a.Seconds > b.Seconds;
    public static bool operator <=(GameTime a, GameTime b) => a.Seconds <= b.Seconds;
    public static bool operator >=(GameTime a, GameTime b) => a.Seconds >= b.Seconds;

    public override string ToString() => $"Day {Day} {Hour:00}:{Minute:00}:{Second:00}";
}
