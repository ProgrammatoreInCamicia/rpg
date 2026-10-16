using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

public class InteriorPrototypeTests
{
    private static SimulationSession House(int hour = 13)
    {
        var s = SimulationSession.Create(InteriorPrototypeScenario.Create());
        s.AdvanceTo(At(1, hour));
        return s;
    }

    private static char LightAt(SimulationSession s, GridPos p) => s.GetPlayerView().MapLight![p.Y][p.X];

    private static void WalkTo(SimulationSession s, GridPos to)
    {
        var walk = s.Execute(new MoveCommand { Actor = s.Player, To = to });
        Assert.True(walk.Success, walk.Message);
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(1));
    }

    [Fact]
    public void Player_gaze_is_a_round_forward_cone_and_survives_save_load()
    {
        var s = House();
        var ahead = new GridPos(9, 2);
        var behind = new GridPos(9, 6);
        s.FacePlayer(0, -1);
        Assert.Equal('2', s.GetPlayerView().MapVisibility![ahead.Y][ahead.X]);
        Assert.NotEqual('2', s.GetPlayerView().MapVisibility![behind.Y][behind.X]);

        s.FacePlayer(0, 1);
        Assert.Equal('2', s.GetPlayerView().MapVisibility![behind.Y][behind.X]);
        Assert.Equal('1', s.GetPlayerView().MapVisibility![ahead.Y][ahead.X]);

        var loaded = s.SaveAndReload();
        Assert.Equal(new GridPos(0, 1), loaded.GetPlayerView().Facing);
        Assert.Equal('2', loaded.GetPlayerView().MapVisibility![behind.Y][behind.X]);
    }

    [Fact]
    public void Daylight_indoors_follows_the_door_as_it_opens_and_shuts_again_after_being_looked_up()
    {
        // Daylight and sight are remembered per map (village speed-up): opening or shutting a door must forget them.
        var map = House().Engine.World.Maps[InteriorPrototypeScenario.Area];
        var indoors = Enumerable.Range(0, map.Height).SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new GridPos(x, y)))
            .Where(p => map.ZoneAt(p) is { } z && map.IndoorZones.Contains(z)).ToList();
        string Daylight() => string.Concat(indoors.Select(p => (int)map.DaylightOn(p, Light.Bright)));

        var shut = Daylight();
        map.SetDoor(InteriorPrototypeScenario.Door, !map.Doors[InteriorPrototypeScenario.Door]);
        var other = Daylight();
        map.SetDoor(InteriorPrototypeScenario.Door, !map.Doors[InteriorPrototypeScenario.Door]);

        Assert.NotEqual(shut, other);
        Assert.Equal(shut, Daylight());
    }

    [Fact]
    public void Window_admits_sight_and_daylight_but_not_movement()
    {
        var s = House();
        var map = s.Engine.World.Maps[InteriorPrototypeScenario.Area];
        var outside = InteriorPrototypeScenario.WindowOutside;
        var inside = InteriorPrototypeScenario.WindowInside;

        Assert.True(map.HasLineOfSight(outside, inside));
        Assert.False(map.DiagonalOpen(outside, inside));
        Assert.Equal(Light.Bright, map.DaylightOn(inside, Light.Bright));
        WalkTo(s, outside);
        Assert.Equal('2', LightAt(s, inside));
        Assert.Equal('0', LightAt(s, new GridPos(5, 3)));
        Assert.False(map.HasLineOfSight(new GridPos(6, 3), new GridPos(5, 3)));
    }

    [Fact]
    public void Closed_edge_door_hides_the_room_but_walk_opens_it()
    {
        var s = House(hour: 2);
        var map = s.Engine.World.Maps[InteriorPrototypeScenario.Area];
        var door = InteriorPrototypeScenario.Door;
        Assert.True(map.IsClosedDoor(door));
        Assert.False(map.HasLineOfSight(new GridPos(8, 4), door));

        var walk = s.Execute(new MoveCommand { Actor = s.Player, To = door });
        Assert.True(walk.Success, walk.Message);
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(1));

        Assert.False(map.IsClosedDoor(door));
        Assert.Equal(door, s.GetPlayerView().Position);
        Assert.True(map.HasLineOfSight(new GridPos(8, 4), door));
    }

    [Fact]
    public void Torch_and_boundaries_survive_a_save_round_trip()
    {
        var s = House(hour: 2);
        Assert.True(s.Execute(new TorchCommand { Actor = s.Player, Lit = true }).Success);
        Assert.Equal('0', LightAt(s, InteriorPrototypeScenario.Door));

        var loaded = s.SaveAndReload();
        Assert.Equal(s.GetPlayerView().Map!.Edges, loaded.GetPlayerView().Map!.Edges);
        Assert.Equal('0', LightAt(loaded, InteriorPrototypeScenario.Door));
        Assert.True(loaded.Engine.World.Maps[InteriorPrototypeScenario.Area].IsClosedDoor(InteriorPrototypeScenario.Door));
    }

    [Fact]
    public void Seen_room_is_remembered_after_leaving_and_after_loading()
    {
        var s = House();
        WalkTo(s, InteriorPrototypeScenario.WindowOutside);
        Assert.Equal('2', s.GetPlayerView().MapVisibility![3][6]);
        WalkTo(s, new GridPos(9, 4));
        Assert.Equal('1', s.GetPlayerView().MapVisibility![3][6]);

        var loaded = s.SaveAndReload();
        Assert.Equal('1', loaded.GetPlayerView().MapVisibility![3][6]);
    }

    [Fact]
    public void Closing_an_edge_door_while_inside_can_be_saved_and_exited()
    {
        var s = House(hour: 2);
        WalkTo(s, InteriorPrototypeScenario.Door);
        Assert.Equal(InteriorPrototypeScenario.Door, s.GetPlayerView().Position);
        var close = s.Execute(new DoorCommand { Actor = s.Player, At = InteriorPrototypeScenario.Door, Open = false });
        Assert.True(close.Success, close.Message);

        var loaded = s.SaveAndReload();
        Assert.True(loaded.Engine.World.Maps[InteriorPrototypeScenario.Area].IsClosedDoor(InteriorPrototypeScenario.Door));
        WalkTo(loaded, new GridPos(8, 4));
        Assert.False(loaded.Engine.World.Maps[InteriorPrototypeScenario.Area].IsClosedDoor(InteriorPrototypeScenario.Door));
    }

    [Fact]
    public void A_schema_21_save_cannot_silently_lose_its_house_edges()
    {
        var data = JsonNode.Parse(House().SaveToString())!;
        data["Maps"]![0]!.AsObject().Remove("Edges");
        var result = LoadFromString(data.ToJsonString());
        Assert.False(result.Success);
        Assert.Contains("confini", result.Error);
    }

    [Fact]
    public void A_schema_20_village_save_without_interior_fields_still_loads()
    {
        var data = JsonNode.Parse(SimulationSession.Create(MappedVillageScenario.CreateLivingVillage()).SaveToString())!;
        AsSchema(data, 20);
        foreach (var map in data["Maps"]!.AsArray())
        {
            map!.AsObject().Remove("Edges");
            map.AsObject().Remove("IndoorZones");
            map.AsObject().Remove("Explored");
        }
        var result = LoadFromString(data.ToJsonString());
        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void A_step_never_cuts_a_wall_corner_while_sight_slips_past_it()
    {
        // A wall square at (1,1): from (1,2) to (2,1) one side of the corner, (1,1), is wall; the other, (2,2), is open.
        var map = new GridMap(new AreaId("a"), new[] { "....", ".#..", "....", "...." },
            new Dictionary<char, LocationId> { ['.'] = new LocationId("y") }, Array.Empty<GridPos>());
        Assert.False(map.DiagonalOpen(new GridPos(1, 2), new GridPos(2, 1))); // no step, no reach, no door across it
        Assert.True(map.HasLineOfSight(new GridPos(1, 2), new GridPos(2, 1))); // but one sees past it
        Assert.True(map.DiagonalOpen(new GridPos(2, 2), new GridPos(3, 1))); // an open corner stays open
    }

    [Fact]
    public void What_is_explored_does_not_depend_on_how_often_the_view_is_read()
    {
        var watched = House();
        var unwatched = House();
        foreach (var s in new[] { watched, unwatched })
        {
            var walk = s.Execute(new MoveCommand { Actor = s.Player, To = InteriorPrototypeScenario.Door });
            Assert.True(walk.Success, walk.Message);
        }
        for (var i = 0; i < 60; i++)
        {
            watched.Advance(Duration.FromSeconds(1));
            watched.GetPlayerView();
        }
        unwatched.Advance(Duration.FromSeconds(60)); // never looked at on the way

        Assert.Equal(watched.SaveToString(), unwatched.SaveToString());
        Assert.Contains('1', string.Concat(unwatched.GetPlayerView().MapVisibility!)); // the way in was remembered
    }
}
