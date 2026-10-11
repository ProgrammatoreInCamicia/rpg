using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>A small, isolated playable house for testing thin walls, a window, an angle and indoor light.</summary>
public static class InteriorPrototypeScenario
{
    public static readonly AreaId Area = new("interior_prototype");
    public static readonly LocationId Yard = new("prototype_yard");
    public static readonly GridPos Door = new(7, 4);
    public static readonly GridPos WindowInside = new(6, 3);
    public static readonly GridPos WindowOutside = new(6, 2);

    private static readonly string[] Rows =
    {
        "##############",
        "#YYYYYYYYYYYY#",
        "#YYYYYYYYYYYY#",
        "#YYYHHHHYYYYY#",
        "#YYYHHHHYYYYY#",
        "#YYYHHHHYYYYY#",
        "#YYYYYYYYYYYY#",
        "#YYYYYYYYYYYY#",
        "#YYYYYYYYYYYY#",
        "##############",
    };

    public static Scenario Create()
    {
        var b = new ScenarioBuilder()
            .AddArea(Area.Value, "Prova interni")
            .AddLocation(Yard.Value, "Cortile", Area.Value)
            .AddLocation(MappedVillageScenario.Ids.FarmerHouse.Value, "Casa", Area.Value)
            .AddMap(Area.Value, Rows, new Dictionary<char, string>
            {
                ['H'] = MappedVillageScenario.Ids.FarmerHouse.Value, ['Y'] = Yard.Value,
            })
            .AddInterior(Area.Value, MappedVillageScenario.Ids.FarmerHouse.Value)
            .AddActor(MappedVillageScenario.Ids.Player.Value, "Protagonista", Yard.Value, isPlayer: true,
                food: 10, build: "paladin", at: (9, 4), torches: 3)
            .AddActor(MappedVillageScenario.Ids.Farmer.Value, "Abitante", MappedVillageScenario.Ids.FarmerHouse.Value,
                build: "farmer", at: (5, 3));

        for (var x = 4; x <= 7; x++)
        {
            b.AddEdge(Area.Value, (x, 2), (x, 3), x == 6 ? MapEdgeKind.Window : MapEdgeKind.Wall);
            b.AddEdge(Area.Value, (x, 5), (x, 6), MapEdgeKind.Wall);
        }
        for (var y = 3; y <= 5; y++)
        {
            b.AddEdge(Area.Value, (3, y), (4, y), MapEdgeKind.Wall);
            if (y == 4)
                b.AddEdge(Area.Value, (7, y), (8, y), MapEdgeKind.Door, (7, y));
            else
                b.AddEdge(Area.Value, (7, y), (8, y), MapEdgeKind.Wall);
        }
        // A doorway between the rooms is offset from the window: moving past the corner changes what is visible.
        b.AddEdge(Area.Value, (5, 3), (6, 3), MapEdgeKind.Wall);
        b.AddEdge(Area.Value, (5, 5), (6, 5), MapEdgeKind.Wall);
        return b.Build();
    }
}
