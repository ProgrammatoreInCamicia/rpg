using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>A spacious, playable house for evaluating interior art with the real movement and sight rules.</summary>
public static class InteriorArtPilotScenario
{
    public static readonly AreaId Area = new("interior_art_pilot");
    public static readonly LocationId Yard = new("interior_art_yard");
    public static readonly GridPos Entrance = new(12, 7);
    public static readonly GridPos RoomDoor = new(8, 7);

    public static Scenario Create()
    {
        var rows = Enumerable.Range(0, 14).Select(y =>
        {
            var cells = Enumerable.Range(0, 19)
                .Select(x => x is 0 or 18 || y is 0 or 13 ? '#' : 'Y').ToArray();
            if (y is >= 4 and <= 9)
                for (var x = 5; x <= 12; x++) cells[x] = 'H';
            return new string(cells);
        }).ToArray();

        var b = new ScenarioBuilder()
            .AddArea(Area.Value, "Prova grafica interni")
            .AddLocation(Yard.Value, "Cortile", Area.Value)
            .AddLocation(MappedVillageScenario.Ids.FarmerHouse.Value, "Casa", Area.Value)
            .AddMap(Area.Value, rows, new Dictionary<char, string>
            {
                ['H'] = MappedVillageScenario.Ids.FarmerHouse.Value,
                ['Y'] = Yard.Value,
            })
            .AddInterior(Area.Value, MappedVillageScenario.Ids.FarmerHouse.Value)
            .AddLight(Area.Value, (11, 6), brightFeet: 20, dimFeet: 30)
            .AddActor(MappedVillageScenario.Ids.Player.Value, "Protagonista", Yard.Value,
                isPlayer: true, food: 10, build: "paladin", at: (15, 7), torches: 3);

        for (var x = 5; x <= 12; x++)
        {
            b.AddEdge(Area.Value, (x, 3), (x, 4), x == 7 || x == 11 ? MapEdgeKind.Window : MapEdgeKind.Wall);
            b.AddEdge(Area.Value, (x, 9), (x, 10), MapEdgeKind.Wall);
        }
        for (var y = 4; y <= 9; y++)
        {
            b.AddEdge(Area.Value, (4, y), (5, y), MapEdgeKind.Wall);
            b.AddEdge(Area.Value, (12, y), (13, y), y == 7 ? MapEdgeKind.Door : MapEdgeKind.Wall,
                y == 7 ? (12, y) : null);
            b.AddEdge(Area.Value, (8, y), (9, y), y == 7 ? MapEdgeKind.Door : MapEdgeKind.Wall,
                y == 7 ? (8, y) : null);
        }
        return b.Build();
    }
}
