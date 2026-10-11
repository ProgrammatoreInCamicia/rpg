namespace RpgSandbox.Sim.Rules;

// C3b, D1a: the items that can be carried, worn, wielded, thrown, spent or left on the ground. They are data
// (game/content/srd/items.json): weights in pounds, costs in copper pieces (1 gp = 10 sp = 100 cp).

/// <summary>A kind of item; a weapon or a piece of armor carries its rules.</summary>
public sealed record ItemDefinition(string Id, string Name, double Pounds, long CostCopper)
{
    public Weapon? Weapon { get; init; }
    public ArmorDefinition? Armor { get; init; }
}

/// <summary>Shortcuts to the default pack's items (for tests and tools; the engine reads its world's pack).</summary>
public static class Items
{
    public static ItemDefinition Torch => ContentPack.Default.Items["torch"];
    public static ItemDefinition Ration => ContentPack.Default.Items["ration"];
    public static ItemDefinition Javelin => ContentPack.Default.Items["javelin"];
    public static ItemDefinition Spear => ContentPack.Default.Items["spear"];
    public static ItemDefinition Bolt => ContentPack.Default.Items["bolt"];

    public static IReadOnlyList<ItemDefinition> All => ContentPack.Default.Items.Values.ToArray();

    public static ItemDefinition? ById(string id) => ContentPack.Default.Item(id);
}

/// <summary>What happens to the item an attack consumes (C3b).</summary>
public enum ItemFate
{
    /// <summary>The attack consumes no item.</summary>
    None,

    /// <summary>A thrown weapon: it leaves the hand and lands on the target's square, hit or miss.</summary>
    Thrown,

    /// <summary>Ammunition: it is spent (the SRD lets one recover half after the fight by searching; later).</summary>
    Spent,
}
