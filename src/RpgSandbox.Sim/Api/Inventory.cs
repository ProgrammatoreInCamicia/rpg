namespace RpgSandbox.Sim.Api;

// C3b: what one carries, what lies on the ground, and coins. Coins are counted in copper pieces (SRD 5.2.1 coins:
// 1 gp = 10 sp = 100 cp; a Blackmarsh silver penny is 1 sp).

/// <summary>Pick up items lying on one's own square or an adjacent one. In a fight it uses the turn's object interaction.</summary>
public sealed record PickUpCommand : Command
{
    public required GridPos At { get; init; }
    public required ItemId Item { get; init; }
    public required int Count { get; init; }
}

/// <summary>Drop carried items on one's own square. Free.</summary>
public sealed record DropCommand : Command
{
    public required ItemId Item { get; init; }
    public required int Count { get; init; }
}

/// <summary>A number of items of one kind.</summary>
public sealed record ItemStackView
{
    public required ItemId Item { get; init; }
    public required string Name { get; init; }
    public required int Count { get; init; }
}

/// <summary>Items of one kind lying on a square of the player's map, as the player sees them.</summary>
public sealed record GroundItemView
{
    public required GridPos At { get; init; }
    public required ItemId Item { get; init; }
    public required string Name { get; init; }
    public required int Count { get; init; }
}
