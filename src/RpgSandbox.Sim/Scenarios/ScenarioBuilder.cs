using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>
/// Immutable initial definition of a world. A scenario is never mutated by the simulation:
/// each <see cref="SimulationSession"/> builds its own mutable state from it.
/// </summary>
public sealed class Scenario
{
    internal Scenario(
        IReadOnlyList<AreaDefinition> areas,
        IReadOnlyList<LocationDefinition> locations,
        IReadOnlyList<RouteDefinition> routes,
        IReadOnlyList<FactionDefinition> factions,
        IReadOnlyList<ActorDefinition> actors,
        IReadOnlyList<StoreDefinition> stores,
        IReadOnlyList<MapDefinition> maps,
        ActorId player,
        ulong seed)
    {
        Seed = seed;
        Maps = maps;
        Areas = areas;
        Locations = locations;
        Routes = routes;
        Factions = factions;
        Actors = actors;
        Stores = stores;
        Player = player;
    }

    internal IReadOnlyList<AreaDefinition> Areas { get; }
    internal IReadOnlyList<LocationDefinition> Locations { get; }
    internal IReadOnlyList<RouteDefinition> Routes { get; }
    internal IReadOnlyList<FactionDefinition> Factions { get; }
    internal IReadOnlyList<ActorDefinition> Actors { get; }
    internal IReadOnlyList<StoreDefinition> Stores { get; }
    internal IReadOnlyList<MapDefinition> Maps { get; }
    internal ActorId Player { get; }

    /// <summary>Initial state of the random number generator: same scenario and seed, same game.</summary>
    internal ulong Seed { get; }
}

internal sealed record AreaDefinition(AreaId Id, string Name);
internal sealed record LocationDefinition(LocationId Id, string Name, AreaId Area, bool Lit);
internal sealed record RouteDefinition(LocationId A, LocationId B, Duration TravelTime);
internal sealed record ActorDefinition(
    ActorId Id, string Name, LocationId Location, bool IsPlayer, int Food, FactionId? Faction, WorkShift? Shift,
    CharacterSheet Sheet, GridPos? At);
internal sealed record StoreDefinition(StoreId Id, string Name, LocationId Location, int Food, FactionId? Owner, GridPos? At);
internal sealed record MapDefinition(AreaId Area, IReadOnlyList<string> Rows, IReadOnlyDictionary<char, LocationId> Zones);
internal sealed record FactionDefinition(
    FactionId Id, string Name, StoreId? HomeStore, int DailyUpkeep, Duration UpkeepTimeOfDay, RaidPolicy? Policy,
    ActorId? Authority);

/// <summary>Builds a <see cref="Scenario"/> in C#. Validates references when <see cref="Build"/> is called.</summary>
public sealed class ScenarioBuilder
{
    private readonly List<AreaDefinition> _areas = new();
    private readonly List<LocationDefinition> _locations = new();
    private readonly List<RouteDefinition> _routes = new();
    private readonly List<FactionDefinition> _factions = new();
    private readonly List<ActorDefinition> _actors = new();
    private readonly List<StoreDefinition> _stores = new();
    private readonly List<MapDefinition> _maps = new();
    private ulong _seed = 0x5EED_2026_1009UL;

    /// <summary>Sets the random seed. Scenarios have a fixed default, so tests and new games are reproducible.</summary>
    public ScenarioBuilder WithSeed(ulong seed)
    {
        _seed = seed;
        return this;
    }

    public ScenarioBuilder AddArea(string id, string name)
    {
        _areas.Add(new AreaDefinition(new AreaId(id), name));
        return this;
    }

    /// <summary>Adds a place. <paramref name="lit"/>: indoors with lamps, never darker than dim light at night.</summary>
    public ScenarioBuilder AddLocation(string id, string name, string areaId, bool lit = false)
    {
        _locations.Add(new LocationDefinition(new LocationId(id), name, new AreaId(areaId), lit));
        return this;
    }

    /// <summary>Connects two locations in both directions.</summary>
    public ScenarioBuilder AddRoute(string fromId, string toId, Duration travelTime)
    {
        _routes.Add(new RouteDefinition(new LocationId(fromId), new LocationId(toId), travelTime));
        return this;
    }

    /// <summary>A faction without a policy: it owns things and has members, but takes no initiative.</summary>
    public ScenarioBuilder AddFaction(string id, string name, string? homeStoreId = null,
        int dailyUpkeep = 0, Duration upkeepTimeOfDay = default, string? authorityId = null)
    {
        _factions.Add(new FactionDefinition(new FactionId(id), name, ToStore(homeStoreId), dailyUpkeep, upkeepTimeOfDay, null,
            authorityId is null ? null : new ActorId(authorityId)));
        return this;
    }

    /// <summary>A faction that raids other factions' stores when its home store falls below a threshold.</summary>
    public ScenarioBuilder AddRaidingFaction(string id, string name, string homeStoreId,
        int dailyUpkeep, Duration upkeepTimeOfDay, int foodThreshold, int raidAmount, Duration evaluationInterval)
    {
        var policy = new RaidPolicy(foodThreshold, raidAmount, evaluationInterval);
        _factions.Add(new FactionDefinition(new FactionId(id), name, new StoreId(homeStoreId), dailyUpkeep, upkeepTimeOfDay, policy, null));
        return this;
    }

    /// <summary>
    /// Adds an actor. NPCs live where they start. An optional daily shift sends them to
    /// <paramref name="workLocationId"/> between <paramref name="shiftStart"/> and <paramref name="shiftEnd"/> (times of day).
    /// </summary>
    public ScenarioBuilder AddActor(string id, string name, string locationId, bool isPlayer = false, int food = 0,
        string? factionId = null, string? workLocationId = null, Duration shiftStart = default, Duration shiftEnd = default,
        CharacterSheet? sheet = null, (int X, int Y)? at = null)
    {
        var shift = workLocationId is null ? null : new WorkShift(new LocationId(workLocationId), shiftStart, shiftEnd);
        _actors.Add(new ActorDefinition(new ActorId(id), name, new LocationId(locationId), isPlayer, food, ToFaction(factionId), shift,
            sheet ?? CharacterSheet.Commoner(), at is { } p ? new GridPos(p.X, p.Y) : null));
        return this;
    }

    /// <summary>Adds a store. On a mapped area, <paramref name="at"/> is the square it occupies (used from an adjacent square).</summary>
    public ScenarioBuilder AddStore(string id, string name, string locationId, int food, string? ownerFactionId = null,
        (int X, int Y)? at = null)
    {
        _stores.Add(new StoreDefinition(new StoreId(id), name, new LocationId(locationId), food, ToFaction(ownerFactionId),
            at is { } p ? new GridPos(p.X, p.Y) : null));
        return this;
    }

    /// <summary>
    /// Gives an area a walkable map: one character per 5-ft square. '#' blocked, '.' open ground (no place),
    /// a letter an open square of the place given in <paramref name="zones"/>. The places must belong to the area.
    /// </summary>
    public ScenarioBuilder AddMap(string areaId, IReadOnlyList<string> rows, IReadOnlyDictionary<char, string> zones)
    {
        _maps.Add(new MapDefinition(new AreaId(areaId), rows.ToArray(),
            zones.ToDictionary(z => z.Key, z => new LocationId(z.Value))));
        return this;
    }

    public Scenario Build()
    {
        RequireUnique(_areas.Select(a => a.Id.Value), "area");
        RequireUnique(_locations.Select(l => l.Id.Value), "location");
        RequireUnique(_actors.Select(a => a.Id.Value), "actor");
        RequireUnique(_stores.Select(s => s.Id.Value), "store");
        RequireUnique(_factions.Select(f => f.Id.Value), "faction");

        var areaIds = _areas.Select(a => a.Id).ToHashSet();
        var locationIds = _locations.Select(l => l.Id).ToHashSet();
        var factionIds = _factions.Select(f => f.Id).ToHashSet();
        var storeIds = _stores.Select(s => s.Id).ToHashSet();

        foreach (var location in _locations)
            Require(areaIds.Contains(location.Area), $"Location '{location.Id}' references unknown area '{location.Area}'.");

        var routePairs = new HashSet<(LocationId, LocationId)>();
        foreach (var route in _routes)
        {
            Require(locationIds.Contains(route.A) && locationIds.Contains(route.B),
                $"Route '{route.A}'-'{route.B}' references an unknown location.");
            Require(route.A != route.B, $"Route from '{route.A}' to itself.");
            Require(route.TravelTime.Seconds > 0, $"Route '{route.A}'-'{route.B}' must take positive time.");
            var key = route.A.CompareTo(route.B) < 0 ? (route.A, route.B) : (route.B, route.A);
            Require(routePairs.Add(key), $"Duplicate route '{route.A}'-'{route.B}'.");
        }

        foreach (var actor in _actors)
        {
            Require(locationIds.Contains(actor.Location), $"Actor '{actor.Id}' starts at unknown location '{actor.Location}'.");
            Require(actor.Food >= 0, $"Actor '{actor.Id}' starts with negative food.");
            Require(actor.Faction is null || factionIds.Contains(actor.Faction.Value),
                $"Actor '{actor.Id}' belongs to unknown faction '{actor.Faction}'.");
            Require(Invariants.Sheet(actor.Id.Value, actor.Sheet));
            if (actor.Shift is { } shift)
            {
                Require(Invariants.Shift(actor.Id.Value, actor.IsPlayer, shift.Start, shift.End));
                Require(locationIds.Contains(shift.Location), $"Actor '{actor.Id}' works at unknown location '{shift.Location}'.");
            }
        }

        foreach (var store in _stores)
        {
            Require(locationIds.Contains(store.Location), $"Store '{store.Id}' is at unknown location '{store.Location}'.");
            Require(store.Food >= 0, $"Store '{store.Id}' starts with negative food.");
            Require(store.Owner is null || factionIds.Contains(store.Owner.Value),
                $"Store '{store.Id}' is owned by unknown faction '{store.Owner}'.");
        }

        foreach (var faction in _factions)
        {
            Require(faction.HomeStore is null || storeIds.Contains(faction.HomeStore.Value),
                $"Faction '{faction.Id}' has unknown home store '{faction.HomeStore}'.");
            Require(Invariants.Faction(faction.Id.Value, faction.DailyUpkeep, faction.HomeStore is not null,
                faction.UpkeepTimeOfDay, faction.Policy));
            Require(faction.Authority is null ||
                    _actors.Any(a => a.Id == faction.Authority && a.Faction == faction.Id && !a.IsPlayer),
                $"Faction '{faction.Id}' authority must be one of its NPC members.");
            if (faction.Policy is not null)
                Require(_stores.Any(s => s.Id == faction.HomeStore && s.Owner == faction.Id),
                    $"Raiding faction '{faction.Id}' must own its home store.");
        }

        ValidateMaps();

        var players = _actors.Where(a => a.IsPlayer).ToList();
        Require(players.Count == 1, $"A scenario needs exactly one player actor, found {players.Count}.");

        return new Scenario(_areas.ToArray(), _locations.ToArray(), _routes.ToArray(), _factions.ToArray(),
            _actors.ToArray(), _stores.ToArray(), _maps.ToArray(), players[0].Id, _seed);
    }

    /// <summary>Maps, and the squares of everything placed on them, are checked by building the actual grids.</summary>
    private void ValidateMaps()
    {
        RequireUnique(_maps.Select(m => m.Area.Value), "map area");
        var areaOf = _locations.ToDictionary(l => l.Id, l => l.Area);
        foreach (var map in _maps)
        {
            Require(_areas.Any(a => a.Id == map.Area), $"Map for unknown area '{map.Area}'.");
            foreach (var (letter, zone) in map.Zones)
            {
                Require(char.IsLetter(letter), $"Zone '{zone}' in area '{map.Area}' must use a letter, not '{letter}'.");
                Require(areaOf.TryGetValue(zone, out var zoneArea) && zoneArea == map.Area,
                    $"Zone '{zone}' of map '{map.Area}' is not a place of that area.");
            }
        }

        var maps = new Dictionary<AreaId, GridMap>();
        foreach (var map in _maps)
        {
            var storeSquares = _stores.Where(s => areaOf[s.Location] == map.Area && s.At is not null).Select(s => s.At!.Value);
            try
            {
                maps[map.Area] = new GridMap(map.Area, map.Rows, map.Zones, storeSquares);
            }
            catch (InvalidDataException e)
            {
                throw new InvalidOperationException($"Invalid map: {e.Message}", e);
            }
        }

        // On a mapped area everything has a square, and the square's zone is its place; elsewhere nothing has one.
        foreach (var store in _stores)
            RequirePlaced($"Store '{store.Id}'", store.Location, store.At, maps, areaOf, mustBeWalkable: false);
        foreach (var actor in _actors)
            RequirePlaced($"Actor '{actor.Id}'", actor.Location, actor.At, maps, areaOf, mustBeWalkable: true);
    }

    private static void RequirePlaced(string what, LocationId location, GridPos? at, IReadOnlyDictionary<AreaId, GridMap> maps,
        IReadOnlyDictionary<LocationId, AreaId> areaOf, bool mustBeWalkable)
    {
        if (!maps.TryGetValue(areaOf[location], out var map))
        {
            Require(at is null, $"{what} has a square but its area has no map.");
            return;
        }
        Require(at is not null, $"{what} is on a mapped area and needs a square.");
        Require(map.InBounds(at!.Value), $"{what} is outside the map.");
        Require(!mustBeWalkable || map.IsWalkable(at.Value), $"{what} stands on a blocked square {at}.");
        Require(map.ZoneAt(at.Value) == location, $"{what} square {at} is not inside '{location}'.");
    }

    private static FactionId? ToFaction(string? id) => id is null ? null : new FactionId(id);
    private static StoreId? ToStore(string? id) => id is null ? null : new StoreId(id);

    /// <summary>Fails with the shared invariant's message, if any.</summary>
    private static void Require(string? problem)
    {
        if (problem is not null)
            throw new InvalidOperationException(problem);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void RequireUnique(IEnumerable<string> ids, string kind)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            Require(!string.IsNullOrWhiteSpace(id), $"Empty {kind} id.");
            Require(seen.Add(id), $"Duplicate {kind} id '{id}'.");
        }
    }
}
