using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

/// <summary>
/// C5a: what the player has learned of creatures, as at the table: never their numbers, only what it saw and made out.
/// Two levels: each individual met, and each kind (what holds for every wolf).
/// </summary>
internal sealed class Lore
{
    public SortedDictionary<ActorId, CreatureLore> Creatures { get; } =
        new(Comparer<ActorId>.Create((a, b) => string.CompareOrdinal(a.Value, b.Value)));

    public SortedDictionary<string, KindLore> Kinds { get; } = new(StringComparer.Ordinal);

    public bool IsEmpty => Creatures.Count == 0 && Kinds.Count == 0;

    public CreatureLore Of(ActorId creature) =>
        Creatures.TryGetValue(creature, out var lore) ? lore : Creatures[creature] = new CreatureLore();

    public KindLore OfKind(string kind) =>
        Kinds.TryGetValue(kind, out var lore) ? lore : Kinds[kind] = new KindLore();
}

/// <summary>C5a: what the player knows of one creature.</summary>
internal sealed class CreatureLore
{
    /// <summary>Its Armor Class is greater than this: the highest total that missed it.</summary>
    public int? ArmorClassAbove { get; set; }

    /// <summary>Its Armor Class is at most this: the lowest total that hit it.</summary>
    public int? ArmorClassAtMost { get; set; }

    /// <summary>The attacks seen it make, by name.</summary>
    public SortedSet<string> Attacks { get; } = new(StringComparer.Ordinal);

    /// <summary>What a careful look (Insight) made out of it, in the order found.</summary>
    public List<string> Tells { get; } = new();

    /// <summary>Already looked at closely: one attempt per creature.</summary>
    public bool Observed { get; set; }

    /// <summary>
    /// One more of the player's own attack totals against it (a natural 1 or 20 says nothing: the caller leaves them out).
    /// A miss at t: AC &gt; t; a hit at t: AC ≤ t. Evidence that contradicts what was known (its armor changed) starts over.
    /// </summary>
    public void LearnArmorClass(int total, bool hit)
    {
        if (hit)
        {
            var atMost = Math.Min(ArmorClassAtMost ?? int.MaxValue, total);
            if (ArmorClassAbove is { } above && above >= atMost)
                (ArmorClassAbove, atMost) = (null, total);
            ArmorClassAtMost = atMost;
        }
        else
        {
            var above = Math.Max(ArmorClassAbove ?? int.MinValue, total);
            if (ArmorClassAtMost is { } atMost && above >= atMost)
                (ArmorClassAtMost, above) = (null, total);
            ArmorClassAbove = above;
        }
    }

    /// <summary>The Armor Class, once the evidence pins it down.</summary>
    public int? ArmorClass => ArmorClassAbove is { } above && ArmorClassAtMost == above + 1 ? above + 1 : null;
}

/// <summary>C5a: what the player knows of a kind of creature.</summary>
internal sealed class KindLore
{
    /// <summary>Natural attacks (bite, claws) seen used by creatures of this kind.</summary>
    public SortedSet<string> Attacks { get; } = new(StringComparer.Ordinal);

    /// <summary>What the Study action recalled about the kind, in the order found.</summary>
    public List<string> Tells { get; } = new();

    /// <summary>Already tried to recall what one knows: one attempt per kind.</summary>
    public bool Studied { get; set; }
}
