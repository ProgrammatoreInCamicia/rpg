using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>T6b: on maps, walls block sight, light decides what can be seen, sound travels a few squares around walls.</summary>
public class SensesTests
{
    // A room (R) and a yard (Y) separated by a wall with a door at (5,3). The store is in the yard at (8,1).
    private static readonly string[] Rows =
    {
        "##########",
        "#RRRR#YYY#",
        "#RRRR#YYY#",
        "#RRRR.YYY#",
        "##########",
    };

    private static readonly ActorId Watcher = new("watcher");

    private static Scenario Yard((int X, int Y) watcherAt, ulong seed = 1, (int X, int Y)? playerAt = null) =>
        new ScenarioBuilder()
            .WithSeed(seed)
            .AddArea("a", "A")
            .AddLocation("room", "Stanza", "a")
            .AddLocation("yard", "Cortile", "a")
            .AddMap("a", Rows, new Dictionary<char, string> { ['R'] = "room", ['Y'] = "yard" })
            .AddLight("a", (2, 2), brightFeet: 15, dimFeet: 30) // a lamp in the room
            .AddFaction("village", "Villaggio", homeStoreId: "store", authorityId: Watcher.Value)
            .AddStore("store", "Scorte", "yard", 40, "village", at: (8, 1))
            .AddActor("player", "Protagonista", (playerAt ?? (7, 2)).X <= 4 ? "room" : "yard", isPlayer: true,
                sheet: Sheets.Paladin(), at: playerAt ?? (7, 2)) // (7,2): diagonally next to the store
            .AddActor(Watcher.Value, "Guardia", watcherAt.X <= 4 ? "room" : "yard", factionId: "village",
                sheet: Sheets.VillageGuard(), at: watcherAt)
            .Build();

    private static SimulationSession StealAtNoon(Scenario scenario)
    {
        var s = SimulationSession.Create(scenario);
        s.AdvanceTo(At(1, 12));
        var take = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = new StoreId("store"), Amount = 2 });
        Assert.True(take.Success, take.Message);
        s.AdvanceUntilCompleted(take.Action!.Value, Duration.FromHours(1));
        return s;
    }

    // ---------------------------------------------------------------- geometry

    private static GridMap Map() => new(new AreaId("a"), Rows,
        new Dictionary<char, LocationId> { ['R'] = new("room"), ['Y'] = new("yard") }, Array.Empty<GridPos>());

    [Fact]
    public void Walls_block_sight_and_doors_let_it_through()
    {
        var map = Map();

        Assert.False(map.HasLineOfSight(new GridPos(2, 2), new GridPos(7, 2))); // wall at x=5
        Assert.True(map.HasLineOfSight(new GridPos(4, 3), new GridPos(7, 2)));  // through the door
        Assert.True(map.HasLineOfSight(new GridPos(7, 2), new GridPos(4, 3)));  // and back (symmetric)
        Assert.False(map.HasLineOfSight(new GridPos(7, 1), new GridPos(4, 3))); // that line grazes the wall
    }

    [Fact]
    public void Sound_walks_around_walls_within_the_hearing_radius()
    {
        var map = Map();

        Assert.Equal(5, map.SoundSteps(new GridPos(2, 2), new GridPos(7, 2), 6)); // through the door
        Assert.Null(map.SoundSteps(new GridPos(1, 1), new GridPos(8, 1), 6));      // too far around the wall
    }

    [Fact]
    public void Squeezing_diagonally_between_two_walls_blocks_sight()
    {
        var map = new GridMap(new AreaId("a"), new[] { ".#", "#." }, new Dictionary<char, LocationId>(), Array.Empty<GridPos>());

        Assert.False(map.HasLineOfSight(new GridPos(0, 0), new GridPos(1, 1)));
    }

    // ---------------------------------------------------------------- thefts

    [Fact]
    public void A_watcher_behind_the_wall_can_hear_but_never_sees_or_recognises()
    {
        var heard = 0;
        for (ulong seed = 0; seed < 30; seed++)
        {
            var s = StealAtNoon(Yard((2, 2), seed));
            var knowledge = s.GetWorldView().Actor(Watcher).Knowledge;
            if (knowledge.Count == 0)
                continue;
            heard++;
            Assert.Equal("Heard", knowledge.Single().Perceived);
            Assert.Null(knowledge.Single().Thief);
        }
        Assert.True(heard > 0, "never heard through the door");
    }

    [Fact]
    public void A_watcher_with_a_view_through_the_door_sees_and_recognises()
    {
        var s = StealAtNoon(Yard((4, 3)));

        var seen = s.GetWorldView().Actor(Watcher).Knowledge.Single();
        Assert.Equal("Seen", seen.Perceived);
        Assert.Equal(new ActorId("player"), seen.Thief);
    }

    [Fact]
    public void A_watcher_who_saw_the_start_still_recognises_after_leaving_and_returning()
    {
        var s = SimulationSession.Create(Yard((4, 3)));
        s.AdvanceTo(At(1, 12));
        var take = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = new StoreId("store"), Amount = 2 });
        Assert.True(take.Success, take.Message);

        foreach (var to in new[] { new GridPos(6, 3), new GridPos(4, 3) })
        {
            s.Engine.CancelAction(s.Engine.World.Actors[Watcher]); // free the scripted witness from its routine wait
            var walk = s.Execute(new MoveCommand { Actor = Watcher, To = to });
            Assert.True(walk.Success, walk.Message);
            Assert.Equal(AdvanceOutcome.Completed, s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(1)).Outcome);
        }
        s.AdvanceUntilCompleted(take.Action!.Value, Duration.FromMinutes(5));

        Assert.Equal(s.Player, Assert.Single(s.GetWorldView().Actor(Watcher).Knowledge).Thief);
    }

    [Fact]
    public void Too_far_and_out_of_sight_means_nothing_is_noticed()
    {
        var s = StealAtNoon(Yard((1, 1), playerAt: (8, 2))); // 7 steps of sound away, behind the wall

        Assert.Empty(s.GetWorldView().Actor(Watcher).Knowledge);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "FoodTheftUnnoticed" && f.Description.Contains("fuori vista"));
    }

    [Fact]
    public void In_the_mapped_village_the_guard_notices_a_granary_theft_only_if_the_inn_door_gives_a_view()
    {
        foreach (var square in new[] { new GridPos(16, 5), new GridPos(16, 4), new GridPos(18, 6) })
        {
            var s = SimulationSession.Create(MappedVillageScenario.Create());
            s.AdvanceTo(At(1, 12));
            var walk = s.Execute(new MoveCommand { Actor = s.Player, To = square });
            s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromHours(1));
            var map = s.Engine.World.Maps[MappedVillageScenario.Ids.Village];
            var inView = map.HasLineOfSight(MappedVillageScenario.Ids.GuardStart, square);

            var take = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = MappedVillageScenario.Ids.GranaryStore, Amount = 3 });
            s.AdvanceUntilCompleted(take.Action!.Value, Duration.FromHours(1));

            var knowledge = s.GetWorldView().Actor(MappedVillageScenario.Ids.Guard).Knowledge;
            if (inView)
                Assert.Equal("Seen", Assert.Single(knowledge).Perceived); // broad daylight, through the door
            else
                Assert.Empty(knowledge); // out of sight, and far beyond earshot
        }
    }

    // ---------------------------------------------------------------- the player's view

    [Fact]
    public void The_player_sees_only_what_is_in_line_of_sight()
    {
        var hidden = SimulationSession.Create(Yard((2, 2)));
        hidden.AdvanceTo(At(1, 12));
        Assert.DoesNotContain(hidden.GetPlayerView().VisibleActors, a => a.Id == Watcher);

        var inView = SimulationSession.Create(Yard((4, 3)));
        inView.AdvanceTo(At(1, 12));
        var watcher = Assert.Single(inView.GetPlayerView().VisibleActors, a => a.Id == Watcher);
        Assert.Equal("riposa", watcher.Doing); // seen from afar: the gesture is visible
    }

    [Fact]
    public void At_night_the_dark_yard_hides_its_people_but_the_lit_room_does_not()
    {
        // The player stands in the dark yard at 02:00; the watcher in the lit room, in view through the door.
        var s = SimulationSession.Create(Yard((4, 3)));
        s.AdvanceTo(At(1, 2));
        Assert.Contains(s.GetPlayerView().VisibleActors, a => a.Id == Watcher);

        // From the lit room, looking out at the dark yard, the player cannot be seen... and vice versa for the yard.
        var t = SimulationSession.Create(Yard((7, 2)));
        t.AdvanceTo(At(1, 2));
        Assert.DoesNotContain(t.GetPlayerView().VisibleActors, a => a.Id == Watcher);
        Assert.Equal(1, t.GetPlayerView().UnseenNearby); // adjacent in the dark: only a presence
    }

    [Fact]
    public void Stores_are_seen_when_in_view()
    {
        var inRoom = SimulationSession.Create(Yard((1, 1), playerAt: (2, 2)));
        inRoom.AdvanceTo(At(1, 12));
        Assert.DoesNotContain(inRoom.GetPlayerView().VisibleStores, x => x.Id == new StoreId("store")); // behind the wall

        var inYard = SimulationSession.Create(Yard((1, 1), playerAt: (6, 3)));
        inYard.AdvanceTo(At(1, 12));
        Assert.Contains(inYard.GetPlayerView().VisibleStores, x => x.Id == new StoreId("store"));
    }

    // ---------------------------------------------------------------- light sources

    [Fact]
    public void A_lamp_is_bright_nearby_dim_farther_and_dark_beyond_its_radius()
    {
        var map = new GridMap(new AreaId("a"), new[] { "............" }, new Dictionary<char, LocationId>(), Array.Empty<GridPos>(),
            new[] { new LightSource(new GridPos(0, 0), 15, 30) }); // SRD Lamp

        Assert.Equal(Light.Bright, map.SourceLight(new GridPos(3, 0)));  // 15 ft
        Assert.Equal(Light.Dim, map.SourceLight(new GridPos(4, 0)));     // 20 ft
        Assert.Equal(Light.Dim, map.SourceLight(new GridPos(9, 0)));     // 45 ft
        Assert.Equal(Light.Dark, map.SourceLight(new GridPos(10, 0)));   // 50 ft
    }

    [Fact]
    public void Lamplight_leaks_out_of_the_inn_door_only_and_fades_along_the_road()
    {
        var s = SimulationSession.Create(MappedVillageScenario.Create());
        s.AdvanceTo(At(1, 2)); // night
        var lights = s.GetPlayerView().MapLight!;
        char LightAt(int x, int y) => lights[y][x];

        Assert.Equal('2', LightAt(3, 2));  // by the lamp
        Assert.Equal('1', LightAt(8, 3));  // on the road, just outside the door
        Assert.Equal('0', LightAt(9, 1));  // on the road, but the inn wall is in the way
        Assert.Equal('0', LightAt(17, 6)); // the granary yard stays dark
    }

    [Fact]
    public void By_night_from_the_dark_yard_the_player_still_sees_the_guard_in_the_lamplit_inn()
    {
        var lit = SimulationSession.Create(MappedVillageScenario.Create());
        lit.AdvanceTo(At(1, 2));
        var view = lit.GetPlayerView();
        Assert.Contains(view.VisibleActors, a => a.Id == MappedVillageScenario.Ids.Guard); // both in the lit inn

        var walk = lit.Execute(new MoveCommand { Actor = lit.Player, To = new GridPos(16, 5) });
        lit.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromHours(1));
        Assert.Equal(Light.Dark, lit.GetPlayerView().Light);
        Assert.Contains(lit.GetPlayerView().VisibleActors, a => a.Id == MappedVillageScenario.Ids.Guard); // she is in the light
    }

    [Fact]
    public void Light_sources_survive_save_and_load()
    {
        var s = SimulationSession.Create(MappedVillageScenario.Create());
        s.AdvanceTo(At(1, 2));
        var before = s.GetPlayerView().MapLight!;

        Assert.Equal(before, s.SaveAndReload().GetPlayerView().MapLight!);
    }
}
