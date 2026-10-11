namespace RpgSandbox.Sim.Rules;

// C3b: the items that can be carried, thrown, spent or left on the ground (SRD 5.2.1 Equipment: weights in pounds,
// costs in copper pieces, 1 gp = 10 sp = 100 cp). The catalog grows as items are needed.

/// <summary>A kind of item.</summary>
public sealed record ItemDefinition(string Id, string Name, double Pounds, long CostCopper);

public static class Items
{
    public static readonly ItemDefinition Torch = new("torch", "Torcia", 1, 1);
    public static readonly ItemDefinition Javelin = new("javelin", "Giavellotto", 2, 50);
    public static readonly ItemDefinition Spear = new("spear", "Lancia", 3, 100);

    /// <summary>One bolt: the SRD sells them by the case of 20 (1 gp, 1.5 lb).</summary>
    public static readonly ItemDefinition Bolt = new("bolt", "Dardo da balestra", 0.075, 5);

    public static IReadOnlyList<ItemDefinition> All { get; } = new[] { Torch, Javelin, Spear, Bolt };

    public static ItemDefinition? ById(string id) => All.FirstOrDefault(i => i.Id == id);
}

/// <summary>What happens to the item an attack needs (C3b).</summary>
public enum ItemFate
{
    /// <summary>The attack needs no item.</summary>
    None,

    /// <summary>A thrown weapon: it leaves the hand and lands on the target's square, hit or miss.</summary>
    Thrown,

    /// <summary>Ammunition: it is spent (the SRD lets one recover half after the fight by searching; later).</summary>
    Spent,
}
