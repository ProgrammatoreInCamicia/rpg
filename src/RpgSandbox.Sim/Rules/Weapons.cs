namespace RpgSandbox.Sim.Rules;

// Weapons from the SRD 5.2.1 "Weapons" table (CC-BY-4.0, see CREDITS.md): damage, properties and mastery as printed,
// now data (game/content/srd/items.json, D1a).

public enum DamageType { Bludgeoning, Piercing, Slashing, Fire }

public enum WeaponCategory { Simple, Martial }

[Flags]
public enum WeaponProperty
{
    None = 0,
    Ammunition = 1,
    Finesse = 2,
    Heavy = 4,
    Light = 8,
    Loading = 16,
    Reach = 32,
    Thrown = 64,
    TwoHanded = 128,
    Versatile = 256,
}

/// <summary>SRD Mastery Properties. Usable only by someone whose features unlock that weapon's mastery.</summary>
public enum WeaponMastery { Cleave, Graze, Nick, Push, Sap, Slow, Topple, Vex }

/// <summary>A number of dice of one kind, e.g. 1d8. Zero dice is a flat amount (an Unarmed Strike).</summary>
public sealed record Dice(int Count, int Sides)
{
    public static readonly Dice None = new(0, 0);

    public override string ToString() => Count == 0 ? "0" : $"{Count}d{Sides}";
}

public sealed record Weapon
{
    /// <summary>D1a: the item this weapon is (its id in the content pack).</summary>
    public string Id { get; init; } = "";
    public required string Name { get; init; }
    public required WeaponCategory Category { get; init; }

    /// <summary>A Ranged weapon (bows, crossbows); a Thrown melee weapon is still a Melee weapon.</summary>
    public required bool Ranged { get; init; }

    public required Dice Damage { get; init; }
    public required DamageType Type { get; init; }
    public WeaponProperty Properties { get; init; }
    public required WeaponMastery Mastery { get; init; }

    /// <summary>Damage with two hands (Versatile), if any.</summary>
    public Dice? TwoHandedDamage { get; init; }

    /// <summary>Normal and long range in feet (Ammunition or Thrown), if any.</summary>
    public (int Normal, int Long)? Range { get; init; }

    /// <summary>D1a: the ammunition it shoots (an item id), for Ammunition weapons.</summary>
    public string? Ammunition { get; init; }

    public bool Has(WeaponProperty property) => (Properties & property) == property;
}

/// <summary>Shortcuts to the default pack's weapons (game/content/srd/items.json), for tests and tools.</summary>
public static class Weapons
{
    public static Weapon Club => ContentPack.Default.Weapon("club");
    public static Weapon Dagger => ContentPack.Default.Weapon("dagger");
    public static Weapon Javelin => ContentPack.Default.Weapon("javelin");
    public static Weapon Mace => ContentPack.Default.Weapon("mace");
    public static Weapon Spear => ContentPack.Default.Weapon("spear");
    public static Weapon LightCrossbow => ContentPack.Default.Weapon("light-crossbow");
    public static Weapon Shortbow => ContentPack.Default.Weapon("shortbow");
    public static Weapon Longsword => ContentPack.Default.Weapon("longsword");
    public static Weapon Scimitar => ContentPack.Default.Weapon("scimitar");
    public static Weapon Shortsword => ContentPack.Default.Weapon("shortsword");
    public static Weapon Greatsword => ContentPack.Default.Weapon("greatsword");
}
