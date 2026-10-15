using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>
/// T6a: the village as a walkable map (one character per 5-ft square). The inn ('I') has a door on the road;
/// the granary yard ('G') is open, with its store at <see cref="Ids.GranaryStoreSquare"/>. No bandits yet:
/// this scenario is about walking, reach and talking. The place-based <see cref="SliceScenario"/> is unchanged.
/// </summary>
public static class MappedVillageScenario
{
    public static class Ids
    {
        public static readonly AreaId Village = new("village");
        public static readonly AreaId Forest = new("forest");
        public static readonly LocationId Inn = new("inn");
        public static readonly LocationId Granary = new("granary");
        public static readonly LocationId FarmerHouse = new("farmer_house");
        public static readonly LocationId ForestRoad = new("forest_road");
        public static readonly LocationId BanditCamp = new("bandit_camp");
        public static readonly ActorId Player = new("player");
        public static readonly ActorId Guard = new("guard");
        public static readonly ActorId Farmer = new("farmer");
        public static readonly ActorId Raider = new("raider");
        public static readonly StoreId GranaryStore = new("granary_store");
        public static readonly StoreId CampStore = new("camp_store");
        public static readonly FactionId VillageFaction = new("village");
        public static readonly FactionId Bandits = new("bandits");

        public static readonly GridPos PlayerStart = new(2, 2);
        public static readonly GridPos GuardStart = new(5, 2);
        public static readonly GridPos InnDoor = new(7, 3);
        public static readonly GridPos GranaryStoreSquare = new(17, 5);
        public static readonly GridPos InnLamp = new(3, 2);
        public static readonly GridPos FarmerHome = new(3, 13);
        public static readonly GridPos FarmerWork = new(18, 7);
        public static readonly GridPos GuardPost = new(15, 5);
        public static readonly GridPos GranaryAccess = new(16, 5);
        public static readonly GridPos HouseDoor = new(8, 13);
        public static readonly GridPos HouseLamp = new(4, 13);
        public static readonly GridPos ForestExit = new(23, 12);
    }

    public static readonly IReadOnlyList<string> Map = new[]
    {
        "########################",
        "#IIIIII#...............#",
        "#IIIIII#...............#",
        "#IIIIII................#",
        "#IIIIII#.....GGGGGGGG..#",
        "########.....GGGGGGGG..#",
        "#............GGGGGGGG..#",
        "#............GGGGGGGG..#",
        "#............GGGGGGGG..#",
        "#......................#",
        "########################",
    };

    /// <summary>
    /// Living village. The inn and farmhouse use thin edges around walkable floor; the granary yard stays open.
    /// The old 24-by-11 Map remains the T6a/T6b regression fixture, and the village posts keep their coordinates.
    /// </summary>
    public static readonly IReadOnlyList<string> LivingMap = new[]
    {
        "........################",
        ".IIIIII................#",
        ".IIIIII................#",
        ".IIIIII................#",
        ".IIIIII......GGGGGGGG..#",
        ".............GGGGGGGG..#",
        "#............GGGGGGGG..#",
        "#............GGGGGGGG..#",
        "#............GGGGGGGG..#",
        "#......................#",
        "#......................#",
        ".......................#",
        ".HHHHHHH..............EE",
        ".HHHHHHH...............#",
        ".HHHHHHH...............#",
        ".......................#",
        "########################",
    };

    public static Scenario Create() =>
        new ScenarioBuilder()
            .AddArea(Ids.Village.Value, "Villaggio")
            .AddLocation(Ids.Inn.Value, "Locanda", Ids.Village.Value)
            .AddLocation(Ids.Granary.Value, "Granaio", Ids.Village.Value)
            .AddMap(Ids.Village.Value, Map, new Dictionary<char, string> { ['I'] = Ids.Inn.Value, ['G'] = Ids.Granary.Value })
            // A lamp in the middle of the inn (SRD Lamp: Bright Light 15 ft, Dim Light 30 ft more): through the open
            // door its light spills dimly onto the road. The granary yard stays dark at night.
            .AddLight(Ids.Village.Value, (Ids.InnLamp.X, Ids.InnLamp.Y), brightFeet: 15, dimFeet: 30)
            .AddDoor(Ids.Village.Value, (Ids.InnDoor.X, Ids.InnDoor.Y), open: true) // the inn door, open for now
            .AddFaction(Ids.VillageFaction.Value, "Villaggio", homeStoreId: Ids.GranaryStore.Value, authorityId: Ids.Guard.Value)
            .AddStore(Ids.GranaryStore.Value, "Scorte del granaio", Ids.Granary.Value, food: 40, ownerFactionId: Ids.VillageFaction.Value,
                at: (Ids.GranaryStoreSquare.X, Ids.GranaryStoreSquare.Y))
            .AddActor(Ids.Player.Value, "Protagonista", Ids.Inn.Value, isPlayer: true, food: 10, build: "paladin",
                at: (Ids.PlayerStart.X, Ids.PlayerStart.Y), torches: 3) // from an explorer's pack
            .AddActor(Ids.Guard.Value, "Guardia", Ids.Inn.Value, factionId: Ids.VillageFaction.Value, build: "village-guard",
                at: (Ids.GuardStart.X, Ids.GuardStart.Y))
            .Build();

    /// <summary>
    /// G4: the village used by the live client and the T6c routines. This is a separate scenario so T6a/T6b tests
    /// keep exercising their original small map.
    /// </summary>
    public static Scenario CreateLivingVillage()
    {
        var b = new ScenarioBuilder()
            .AddArea(Ids.Village.Value, "Villaggio")
            .AddArea(Ids.Forest.Value, "Bosco")
            .AddLocation(Ids.Inn.Value, "Locanda", Ids.Village.Value)
            .AddLocation(Ids.Granary.Value, "Granaio", Ids.Village.Value)
            .AddLocation(Ids.FarmerHouse.Value, "Casa del contadino", Ids.Village.Value)
            .AddLocation(Ids.ForestRoad.Value, "Strada per il bosco", Ids.Village.Value)
            .AddLocation(Ids.BanditCamp.Value, "Campo dei banditi", Ids.Forest.Value)
            .AddRoute(Ids.ForestRoad.Value, Ids.BanditCamp.Value, Duration.FromMinutes(30))
            .AddMap(Ids.Village.Value, LivingMap, new Dictionary<char, string>
            {
                ['I'] = Ids.Inn.Value,
                ['G'] = Ids.Granary.Value,
                ['H'] = Ids.FarmerHouse.Value,
                ['E'] = Ids.ForestRoad.Value,
            })
            .AddExit(Ids.Village.Value, Ids.ForestRoad.Value, (Ids.ForestExit.X, Ids.ForestExit.Y))
            .AddPost(Ids.Village.Value, Ids.Inn.Value, PostKind.Rest, (Ids.GuardStart.X, Ids.GuardStart.Y))
            .AddPost(Ids.Village.Value, Ids.FarmerHouse.Value, PostKind.Home, (Ids.FarmerHome.X, Ids.FarmerHome.Y))
            .AddPost(Ids.Village.Value, Ids.Granary.Value, PostKind.Work, (Ids.FarmerWork.X, Ids.FarmerWork.Y))
            .AddPost(Ids.Village.Value, Ids.Granary.Value, PostKind.Guard, (Ids.GuardPost.X, Ids.GuardPost.Y))
            .AddLight(Ids.Village.Value, (Ids.InnLamp.X, Ids.InnLamp.Y), brightFeet: 15, dimFeet: 30)
            .AddLight(Ids.Village.Value, (Ids.HouseLamp.X, Ids.HouseLamp.Y), brightFeet: 10, dimFeet: 15)
            .AddInterior(Ids.Village.Value, Ids.Inn.Value)
            .AddInterior(Ids.Village.Value, Ids.FarmerHouse.Value)
            .AddFaction(Ids.VillageFaction.Value, "Villaggio", homeStoreId: Ids.GranaryStore.Value, authorityId: Ids.Guard.Value)
            .AddRaidingFaction(Ids.Bandits.Value, "Banditi", homeStoreId: Ids.CampStore.Value, // T6c-3: as in the slice
                dailyUpkeep: 6, upkeepTimeOfDay: Duration.FromHours(8),
                foodThreshold: 10, raidAmount: 8, evaluationInterval: Duration.FromHours(3))
            .AddEnmity(Ids.VillageFaction.Value, Ids.Bandits.Value) // C4b
            .AddStore(Ids.GranaryStore.Value, "Scorte del granaio", Ids.Granary.Value, food: 40,
                ownerFactionId: Ids.VillageFaction.Value, at: (Ids.GranaryStoreSquare.X, Ids.GranaryStoreSquare.Y),
                access: new[] { (Ids.GranaryAccess.X, Ids.GranaryAccess.Y) })
            .AddStore(Ids.CampStore.Value, "Scorte del campo", Ids.BanditCamp.Value, food: 14,
                ownerFactionId: Ids.Bandits.Value)
            .AddActor(Ids.Player.Value, "Protagonista", Ids.Inn.Value, isPlayer: true, food: 10, build: "paladin",
                at: (Ids.PlayerStart.X, Ids.PlayerStart.Y), torches: 3)
            .AddActor(Ids.Guard.Value, "Guardia", Ids.Inn.Value, factionId: Ids.VillageFaction.Value,
                build: "village-guard",
                at: (Ids.GuardStart.X, Ids.GuardStart.Y))
            .AddActor(Ids.Farmer.Value, "Contadino", Ids.FarmerHouse.Value, factionId: Ids.VillageFaction.Value,
                workLocationId: Ids.Granary.Value, shiftStart: Duration.FromHours(13), shiftEnd: Duration.FromHours(18), // T6c-2
                build: "farmer", at: (Ids.FarmerHome.X, Ids.FarmerHome.Y), items: new[] { ("club", 1), ("farmhouse-key", 1) })
            .AddActor(Ids.Raider.Value, "Razziatore", Ids.BanditCamp.Value, factionId: Ids.Bandits.Value,
                build: "raider");

        // The doors keep the same clickable squares as the old layout, but occupy a boundary instead of a tile.
        for (var x = 1; x <= 6; x++)
        {
            b.AddEdge(Ids.Village.Value, (x, 0), (x, 1), x == 3 ? MapEdgeKind.Window : MapEdgeKind.Wall,
                lockSpec: x == 3 ? new LockSpec(Stuck: true, ForceDc: 10, Clue: "il telaio è gonfio di umidità") : null); // P4a: for trying
            b.AddEdge(Ids.Village.Value, (x, 4), (x, 5), MapEdgeKind.Wall);
        }
        for (var y = 1; y <= 4; y++)
        {
            b.AddEdge(Ids.Village.Value, (0, y), (1, y), MapEdgeKind.Wall);
            b.AddEdge(Ids.Village.Value, (6, y), (7, y), y switch
            {
                3 => MapEdgeKind.Door,
                _ => MapEdgeKind.Wall,
            }, y == 3 ? (7, y) : null, open: y == 3);
        }
        for (var x = 1; x <= 7; x++)
        {
            b.AddEdge(Ids.Village.Value, (x, 11), (x, 12), x == 4 ? MapEdgeKind.Window : MapEdgeKind.Wall);
            b.AddEdge(Ids.Village.Value, (x, 14), (x, 15), x == 3 ? MapEdgeKind.Window : MapEdgeKind.Wall);
        }
        for (var y = 12; y <= 14; y++)
        {
            b.AddEdge(Ids.Village.Value, (0, y), (1, y), MapEdgeKind.Wall);
            b.AddEdge(Ids.Village.Value, (7, y), (8, y), y == 13 ? MapEdgeKind.Door : MapEdgeKind.Wall,
                y == 13 ? (8, y) : null, lockSpec: y == 13 // P4a: the farmer keeps the key
                    ? new LockSpec(Key: "farmhouse-key", Locked: true, ForceDc: 15, Clue: "una porta robusta di quercia") : null);
        }
        // A short partition gives the farmhouse a second room and a real corner to look around.
        b.AddEdge(Ids.Village.Value, (4, 12), (5, 12), MapEdgeKind.Wall);
        b.AddEdge(Ids.Village.Value, (4, 14), (5, 14), MapEdgeKind.Wall);
        b.AddGroundItem(Ids.Village.Value, (2, 3), "thieves-tools"); // P4a: in the inn, for trying them out
        return b.Build();
    }
}
