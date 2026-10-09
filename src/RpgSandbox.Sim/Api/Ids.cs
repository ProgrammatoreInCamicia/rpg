namespace RpgSandbox.Sim.Api;

// Stable identifiers shared by the core and the client.
// String-based IDs come from scenario definitions; action IDs are allocated by the simulation.

public readonly record struct AreaId(string Value) : IComparable<AreaId>
{
    public int CompareTo(AreaId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

public readonly record struct LocationId(string Value) : IComparable<LocationId>
{
    public int CompareTo(LocationId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

public readonly record struct ActorId(string Value) : IComparable<ActorId>
{
    public int CompareTo(ActorId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

public readonly record struct ActionId(long Value) : IComparable<ActionId>
{
    public int CompareTo(ActionId other) => Value.CompareTo(other.Value);
    public override string ToString() => Value.ToString();
}

public readonly record struct FactId(long Value) : IComparable<FactId>
{
    public int CompareTo(FactId other) => Value.CompareTo(other.Value);
    public override string ToString() => Value.ToString();
}
