using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>T6c-2: NPC routines on the map (work and home posts, the guard's rest post, journeys between areas).</summary>
public class NpcRoutineTests
{
    private static GridPos? Square(SimulationSession s, ActorId id) => s.GetWorldView().Actor(id).Position;

    [Fact]
    public void The_farmer_walks_to_work_for_the_shift_and_home_afterwards()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());

        s.AdvanceTo(At(1, 12, 59));
        Assert.Equal(FarmerHome, Square(s, Farmer));
        Assert.False(s.Engine.World.Maps[Village].Doors[HouseDoor]);

        s.AdvanceTo(At(1, 13, 10));
        Assert.Equal(FarmerWork, Square(s, Farmer));
        Assert.Equal(Granary, s.GetWorldView().Actor(Farmer).Location);
        Assert.True(s.Engine.World.Maps[Village].Doors[HouseDoor]); // opened on the way out
        Assert.Equal("Routine: lavora", s.GetWorldView().Actor(Farmer).LastDecision!.Rule);

        s.AdvanceTo(At(1, 18, 10));
        Assert.Equal(FarmerHome, Square(s, Farmer));
        Assert.Equal(FarmerHouse, s.GetWorldView().Actor(Farmer).Location);
    }

    [Fact]
    public void The_guard_goes_back_to_his_post_when_moved_off_it()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var walk = s.Execute(new MoveCommand { Actor = Guard, To = new GridPos(12, 9) }); // out on the road
        Assert.True(walk.Success, walk.Message);
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(5));
        var onTheRoad = s.GetWorldView().Actor(Guard);
        Assert.Null(onTheRoad.Location); // free on open ground, outside every place: no crash, it decides
        Assert.Equal("Routine: torna a casa", onTheRoad.LastDecision!.Rule);

        s.Advance(Duration.FromMinutes(5));

        Assert.Equal(GuardStart, Square(s, Guard)); // his Rest post at the inn
        Assert.Equal("Routine: riposa", s.GetWorldView().Actor(Guard).LastDecision!.Rule);
    }

    [Fact]
    public void An_npc_crosses_between_areas_by_the_map_exit()
    {
        // A woodcutter living in the woods works at the village granary: out by the road at dawn, back at dusk.
        var s = SimulationSession.Create(new ScenarioBuilder()
            .AddArea("village", "Villaggio").AddArea("woods", "Bosco")
            .AddLocation("yard", "Granaio", "village").AddLocation("road", "Strada", "village").AddLocation("hut", "Capanna", "woods")
            .AddRoute("road", "hut", Duration.FromMinutes(10))
            .AddMap("village", new[] { "#######", "#YYYYRR", "#YYYYRR", "#######" },
                new Dictionary<char, string> { ['Y'] = "yard", ['R'] = "road" })
            .AddExit("village", "road", (6, 1))
            .AddPost("village", "yard", PostKind.Work, (2, 2))
            .AddActor("player", "P", "yard", isPlayer: true, at: (1, 1))
            .AddActor("woodcutter", "Taglialegna", "hut", workLocationId: "yard",
                shiftStart: Duration.FromHours(8), shiftEnd: Duration.FromHours(17), sheet: Sheets.Farmer())
            .Build());
        var woodcutter = new ActorId("woodcutter");

        s.AdvanceTo(At(1, 8, 30));
        var atWork = s.GetWorldView().Actor(woodcutter);
        Assert.Equal((new GridPos(2, 2), new LocationId("yard")), (atWork.Position!.Value, atWork.Location!.Value));

        s.AdvanceTo(At(1, 17, 30));
        var home = s.GetWorldView().Actor(woodcutter);
        Assert.Equal((null, new LocationId("hut")), (home.Position, home.Location!.Value)); // off the map, in the woods
    }

    [Fact]
    public void A_day_in_one_advance_equals_the_same_day_hour_by_hour()
    {
        var once = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var hourly = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());

        once.Advance(Duration.FromHours(24));
        for (var hour = 0; hour < 24; hour++)
            hourly.Advance(Duration.FromHours(1));

        Assert.Equal(once.SaveToString(), hourly.SaveToString());
    }

    [Fact]
    public void A_routine_walk_survives_save_and_load()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        s.AdvanceTo(At(1, 13).Plus(Duration.FromSeconds(5))); // the farmer is on his way to work

        var loaded = s.SaveAndReload();
        s.AdvanceTo(At(1, 19));
        loaded.AdvanceTo(At(1, 19));

        Assert.Equal(s.SaveToString(), loaded.SaveToString());
    }

    [Fact]
    public void Inside_his_place_but_off_his_post_the_guard_still_goes_back_to_it()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var walk = s.Execute(new MoveCommand { Actor = Guard, To = new GridPos(1, 1) }); // a corner of the inn
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(5));
        Assert.Equal(Inn, s.GetWorldView().Actor(Guard).Location); // in his place, not on his post

        s.Advance(Duration.FromMinutes(1));

        Assert.Equal(GuardStart, Square(s, Guard));
    }

    // ---------------------------------------------------------------- review of T6c-2: unreachable destinations

    [Fact]
    public void An_npc_leaves_by_an_exit_it_can_reach_even_if_a_walled_off_one_is_cheaper()
    {
        // West road: 60 minutes to the hut, reachable. East road: 5 minutes, but behind a wall.
        var s = SimulationSession.Create(new ScenarioBuilder()
            .AddArea("village", "Villaggio").AddArea("woods", "Bosco")
            .AddLocation("yard", "Cortile", "village").AddLocation("west", "Strada ovest", "village")
            .AddLocation("east", "Strada est", "village").AddLocation("hut", "Capanna", "woods")
            .AddRoute("west", "hut", Duration.FromMinutes(60)).AddRoute("east", "hut", Duration.FromMinutes(5))
            .AddMap("village", new[] { "#######", "WYYYY#E", "#######" },
                new Dictionary<char, string> { ['Y'] = "yard", ['W'] = "west", ['E'] = "east" })
            .AddExit("village", "west", (0, 1)).AddExit("village", "east", (6, 1))
            .AddActor("player", "P", "yard", isPlayer: true, at: (1, 1))
            .AddActor("woodcutter", "Taglialegna", "yard", at: (3, 1), // lives in the yard, works at the hut in the woods
                workLocationId: "hut", shiftStart: Duration.Zero, shiftEnd: Duration.FromHours(23))
            .Build());
        var woodcutter = s.Engine.World.Actors[new ActorId("woodcutter")];

        s.Advance(Duration.FromHours(2));

        Assert.Equal(new LocationId("hut"), s.GetWorldView().Actor(woodcutter.Id).Location);
    }

    [Fact]
    public void A_place_that_cannot_be_reached_makes_the_npc_hesitate_instead_of_failing()
    {
        // The depot's only square is its store: there is nowhere to stand in it.
        var s = SimulationSession.Create(new ScenarioBuilder()
            .AddArea("a", "A")
            .AddLocation("start", "Start", "a").AddLocation("depot", "Depot", "a")
            .AddMap("a", new[] { "AA#", "AAG" }, new Dictionary<char, string> { ['A'] = "start", ['G'] = "depot" })
            .AddStore("store", "Store", "depot", 0, at: (2, 1))
            .AddActor("player", "P", "start", isPlayer: true, at: (0, 0))
            .AddActor("clerk", "Commesso", "start", workLocationId: "depot",
                shiftStart: Duration.Zero, shiftEnd: Duration.FromHours(23), at: (1, 1))
            .Build());

        s.Advance(Duration.FromHours(1));

        var clerk = s.GetWorldView().Actor(new ActorId("clerk"));
        Assert.Equal("Routine: va al lavoro", clerk.LastDecision!.Rule);
        Assert.Contains("Nessuna azione possibile", clerk.LastDecision.Reason);
    }
}
