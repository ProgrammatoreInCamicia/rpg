namespace RpgSandbox.Sim.Rules;

// Weapons from the SRD 5.2.1 "Weapons" table (CC-BY-4.0, see CREDITS.md): damage, properties and mastery as printed.
// Only the weapons the game uses so far are listed.

public enum DamageType { Bludgeoning, Piercing, Slashing }

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

    public bool Has(WeaponProperty property) => (Properties & property) == property;
}

/// <summary>The SRD 5.2.1 weapons the game uses, with the table's values.</summary>
public static class Weapons
{
    public static readonly Weapon Club = new()
    {
        Name = "Bastone", Category = WeaponCategory.Simple, Ranged = false, Damage = new Dice(1, 4),
        Type = DamageType.Bludgeoning, Properties = WeaponProperty.Light, Mastery = WeaponMastery.Slow,
    };

    public static readonly Weapon Dagger = new()
    {
        Name = "Pugnale", Category = WeaponCategory.Simple, Ranged = false, Damage = new Dice(1, 4),
        Type = DamageType.Piercing, Properties = WeaponProperty.Finesse | WeaponProperty.Light | WeaponProperty.Thrown,
        Range = (20, 60), Mastery = WeaponMastery.Nick,
    };

    public static readonly Weapon Javelin = new()
    {
        Name = "Giavellotto", Category = WeaponCategory.Simple, Ranged = false, Damage = new Dice(1, 6),
        Type = DamageType.Piercing, Properties = WeaponProperty.Thrown, Range = (30, 120), Mastery = WeaponMastery.Slow,
    };

    public static readonly Weapon Mace = new()
    {
        Name = "Mazza", Category = WeaponCategory.Simple, Ranged = false, Damage = new Dice(1, 6),
        Type = DamageType.Bludgeoning, Mastery = WeaponMastery.Sap,
    };

    public static readonly Weapon Spear = new()
    {
        Name = "Lancia", Category = WeaponCategory.Simple, Ranged = false, Damage = new Dice(1, 6),
        Type = DamageType.Piercing, Properties = WeaponProperty.Thrown | WeaponProperty.Versatile,
        TwoHandedDamage = new Dice(1, 8), Range = (20, 60), Mastery = WeaponMastery.Sap,
    };

    public static readonly Weapon LightCrossbow = new()
    {
        Name = "Balestra leggera", Category = WeaponCategory.Simple, Ranged = true, Damage = new Dice(1, 8),
        Type = DamageType.Piercing,
        Properties = WeaponProperty.Ammunition | WeaponProperty.Loading | WeaponProperty.TwoHanded,
        Range = (80, 320), Mastery = WeaponMastery.Slow,
    };

    public static readonly Weapon Shortbow = new()
    {
        Name = "Arco corto", Category = WeaponCategory.Simple, Ranged = true, Damage = new Dice(1, 6),
        Type = DamageType.Piercing, Properties = WeaponProperty.Ammunition | WeaponProperty.TwoHanded,
        Range = (80, 320), Mastery = WeaponMastery.Vex,
    };

    public static readonly Weapon Longsword = new()
    {
        Name = "Spada lunga", Category = WeaponCategory.Martial, Ranged = false, Damage = new Dice(1, 8),
        Type = DamageType.Slashing, Properties = WeaponProperty.Versatile, TwoHandedDamage = new Dice(1, 10),
        Mastery = WeaponMastery.Sap,
    };

    public static readonly Weapon Scimitar = new()
    {
        Name = "Scimitarra", Category = WeaponCategory.Martial, Ranged = false, Damage = new Dice(1, 6),
        Type = DamageType.Slashing, Properties = WeaponProperty.Finesse | WeaponProperty.Light, Mastery = WeaponMastery.Nick,
    };

    public static readonly Weapon Shortsword = new()
    {
        Name = "Spada corta", Category = WeaponCategory.Martial, Ranged = false, Damage = new Dice(1, 6),
        Type = DamageType.Piercing, Properties = WeaponProperty.Finesse | WeaponProperty.Light, Mastery = WeaponMastery.Vex,
    };

    public static readonly Weapon Greatsword = new()
    {
        Name = "Spadone", Category = WeaponCategory.Martial, Ranged = false, Damage = new Dice(2, 6),
        Type = DamageType.Slashing, Properties = WeaponProperty.Heavy | WeaponProperty.TwoHanded, Mastery = WeaponMastery.Graze,
    };
}
