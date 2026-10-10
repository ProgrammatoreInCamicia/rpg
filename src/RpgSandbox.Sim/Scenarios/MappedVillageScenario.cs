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
    /// G4 layout for the living village. The original 24-by-11 map above remains the T6a/T6b regression scenario;
    /// its first ten rows and fixture coordinates are retained here. The southern extension adds a walled farmhouse
    /// with an east-facing door; E is the road exit to the unmapped forest. The store's only access is west of it,
    /// where the guard can stand nearby without occupying the access square.
    /// </summary>
    public static readonly IReadOnlyList<string> LivingMap = new[]
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
        "#......................#",
        "#########..............#",
        "#HHHHHHH#.............EE",
        "#HHHHHHH...............#",
        "#HHHHHHH#..............#",
        "#########..............#",
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
            .AddActor(Ids.Player.Value, "Protagonista", Ids.Inn.Value, isPlayer: true, food: 10, sheet: Sheets.Paladin(),
                at: (Ids.PlayerStart.X, Ids.PlayerStart.Y), torches: 3) // from an explorer's pack
            .AddActor(Ids.Guard.Value, "Guardia", Ids.Inn.Value, factionId: Ids.VillageFaction.Value, sheet: Sheets.VillageGuard(),
                at: (Ids.GuardStart.X, Ids.GuardStart.Y))
            .Build();

    /// <summary>
    /// G4: the village used by the live client and the T6c routines. This is a separate scenario so T6a/T6b tests
    /// keep exercising their original small map.
    /// </summary>
    public static Scenario CreateLivingVillage() =>
        new ScenarioBuilder()
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
            .AddDoor(Ids.Village.Value, (Ids.InnDoor.X, Ids.InnDoor.Y), open: true)
            .AddDoor(Ids.Village.Value, (Ids.HouseDoor.X, Ids.HouseDoor.Y), open: false)
            .AddFaction(Ids.VillageFaction.Value, "Villaggio", homeStoreId: Ids.GranaryStore.Value, authorityId: Ids.Guard.Value)
            .AddFaction(Ids.Bandits.Value, "Banditi", homeStoreId: Ids.CampStore.Value)
            .AddStore(Ids.GranaryStore.Value, "Scorte del granaio", Ids.Granary.Value, food: 40,
                ownerFactionId: Ids.VillageFaction.Value, at: (Ids.GranaryStoreSquare.X, Ids.GranaryStoreSquare.Y),
                access: new[] { (Ids.GranaryAccess.X, Ids.GranaryAccess.Y) })
            .AddStore(Ids.CampStore.Value, "Scorte del campo", Ids.BanditCamp.Value, food: 14,
                ownerFactionId: Ids.Bandits.Value)
            .AddActor(Ids.Player.Value, "Protagonista", Ids.Inn.Value, isPlayer: true, food: 10, sheet: Sheets.Paladin(),
                at: (Ids.PlayerStart.X, Ids.PlayerStart.Y), torches: 3)
            .AddActor(Ids.Guard.Value, "Guardia", Ids.Inn.Value, factionId: Ids.VillageFaction.Value,
                sheet: Sheets.VillageGuard(), at: (Ids.GuardStart.X, Ids.GuardStart.Y))
            .AddActor(Ids.Farmer.Value, "Contadino", Ids.FarmerHouse.Value, factionId: Ids.VillageFaction.Value,
                sheet: Sheets.Farmer(), at: (Ids.FarmerHome.X, Ids.FarmerHome.Y))
            .AddActor(Ids.Raider.Value, "Razziatore", Ids.BanditCamp.Value, factionId: Ids.Bandits.Value,
                sheet: Sheets.Raider())
            .Build();
}
