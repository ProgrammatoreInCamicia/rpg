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
        public static readonly LocationId Inn = new("inn");
        public static readonly LocationId Granary = new("granary");
        public static readonly ActorId Player = new("player");
        public static readonly ActorId Guard = new("guard");
        public static readonly StoreId GranaryStore = new("granary_store");
        public static readonly FactionId VillageFaction = new("village");

        public static readonly GridPos PlayerStart = new(2, 2);
        public static readonly GridPos GuardStart = new(5, 2);
        public static readonly GridPos InnDoor = new(7, 3);
        public static readonly GridPos GranaryStoreSquare = new(17, 5);
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

    public static Scenario Create() =>
        new ScenarioBuilder()
            .AddArea(Ids.Village.Value, "Villaggio")
            .AddLocation(Ids.Inn.Value, "Locanda", Ids.Village.Value, lit: true)
            .AddLocation(Ids.Granary.Value, "Granaio", Ids.Village.Value)
            .AddMap(Ids.Village.Value, Map, new Dictionary<char, string> { ['I'] = Ids.Inn.Value, ['G'] = Ids.Granary.Value })
            .AddFaction(Ids.VillageFaction.Value, "Villaggio", homeStoreId: Ids.GranaryStore.Value, authorityId: Ids.Guard.Value)
            .AddStore(Ids.GranaryStore.Value, "Scorte del granaio", Ids.Granary.Value, food: 40, ownerFactionId: Ids.VillageFaction.Value,
                at: (Ids.GranaryStoreSquare.X, Ids.GranaryStoreSquare.Y))
            .AddActor(Ids.Player.Value, "Protagonista", Ids.Inn.Value, isPlayer: true, food: 10, sheet: Sheets.Paladin(),
                at: (Ids.PlayerStart.X, Ids.PlayerStart.Y))
            .AddActor(Ids.Guard.Value, "Guardia", Ids.Inn.Value, factionId: Ids.VillageFaction.Value, sheet: Sheets.VillageGuard(),
                at: (Ids.GuardStart.X, Ids.GuardStart.Y))
            .Build();
}
