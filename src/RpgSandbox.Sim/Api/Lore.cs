namespace RpgSandbox.Sim.Api;

/// <summary>
/// C5a: look closely at a creature one sees, as a GM would ask for the fitting checks: Insight always (what it shows),
/// and Study when proficient in the knowledge its type calls for (what one recalls about its kind). Each check reveals
/// what its total reaches; a failure reveals nothing. One Insight per creature, one Study per kind. In a fight it takes
/// the action; outside it is immediate (ADAPTATION). What is learned is the player's own: no one else hears of it.
/// </summary>
public sealed record ObserveCommand : Command
{
    public required ActorId Target { get; init; }
}

/// <summary>C5a: what the player knows of one creature it sees: only what it saw and made out, never its numbers.</summary>
public sealed record CreatureCardView
{
    /// <summary>What kind of creature it looks like ("Umano"); null if it is of no known kind.</summary>
    public string? Kind { get; init; }

    /// <summary>What a careful look made out of it.</summary>
    public IReadOnlyList<string> Tells { get; init; } = Array.Empty<string>();

    /// <summary>The attacks the player saw it make.</summary>
    public IReadOnlyList<string> Attacks { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Its Armor Class as deduced from the player's own blows: greater than <see cref="ArmorClassAbove"/> (the highest total
    /// that missed), at most <see cref="ArmorClassAtMost"/> (the lowest that hit); <see cref="ArmorClass"/> once pinned down.
    /// </summary>
    public int? ArmorClassAbove { get; init; }
    public int? ArmorClassAtMost { get; init; }
    public int? ArmorClass { get; init; }

    /// <summary>Already looked at closely (no further Insight on it).</summary>
    public bool Observed { get; init; }

    /// <summary>At least one permitted Insight or proficient Study attempt remains.</summary>
    public bool CanObserve { get; init; }

    /// <summary>What the player knows of its kind; null if it is of no known kind.</summary>
    public KindCardView? KindLore { get; init; }
}

/// <summary>C5a: what the player knows of a kind of creature.</summary>
public sealed record KindCardView
{
    public required string Name { get; init; }

    /// <summary>What the Study action recalled about the kind.</summary>
    public IReadOnlyList<string> Tells { get; init; } = Array.Empty<string>();

    /// <summary>Natural attacks (bite, claws) seen used by creatures of this kind.</summary>
    public IReadOnlyList<string> Attacks { get; init; } = Array.Empty<string>();

    /// <summary>Already tried to recall what one knows of it (no further Study on it).</summary>
    public bool Studied { get; init; }
}
