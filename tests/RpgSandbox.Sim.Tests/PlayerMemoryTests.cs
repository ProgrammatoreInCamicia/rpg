using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>T6c-5: what the player knows of doors it is not looking at, and presences it can only hear.</summary>
public class PlayerMemoryTests
{
    private static DoorView Door(SimulationSession s, GridPos at) => s.GetPlayerView().Doors.Single(d => d.At == at);

    private static void Walk(SimulationSession s, GridPos to)
    {
        var walk = s.Execute(new MoveCommand { Actor = s.Player, To = to });
        Assert.True(walk.Success, walk.Message);
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(5));
    }

    [Fact]
    public void A_door_never_seen_is_unknown_and_one_in_sight_is_seen()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage()); // midnight, in the lit inn

        Assert.Equal((DoorKnowledge.Seen, (bool?)true), (Door(s, InnDoor).Known, Door(s, InnDoor).Open));
        var house = Door(s, HouseDoor);
        Assert.Equal((DoorKnowledge.Unknown, (bool?)null, (GameTime?)null), (house.Known, house.Open, house.SeenAt));
    }

    [Fact]
    public void A_door_that_changes_out_of_sight_is_remembered_as_it_was()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        Walk(s, new GridPos(9, 13)); // in front of the farmhouse door, in the night
        Assert.Equal((DoorKnowledge.Seen, (bool?)false), (Door(s, HouseDoor).Known, Door(s, HouseDoor).Open));
        var seenAt = s.Now;

        Walk(s, PlayerStart); // back into the inn, walls in between
        s.AdvanceTo(At(1, 13, 30));
        // Meanwhile the door is unlocked and left open, out of the player's sight (by hand: since P4a the farmer locks it
        // again behind him).
        s.Engine.World.Maps[Village].BarrierOf(BarrierKey.Door(HouseDoor))!.Locked = false;
        s.Engine.World.Maps[Village].SetDoor(HouseDoor, open: true);
        Assert.True(s.Engine.World.Maps[Village].Doors[HouseDoor]);

        var remembered = Door(s, HouseDoor);
        Assert.Equal(DoorKnowledge.Remembered, remembered.Known);
        Assert.Equal(false, remembered.Open); // as the player last saw it
        Assert.True(remembered.SeenAt >= seenAt && remembered.SeenAt < At(1, 13));
    }

    [Fact]
    public void The_memory_of_doors_survives_save_and_load()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        Walk(s, new GridPos(9, 13));
        Walk(s, PlayerStart);

        var loaded = s.SaveAndReload();

        Assert.Equal(Door(s, HouseDoor), Door(loaded, HouseDoor));
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
    }

    [Theory]
    [InlineData(null, 1)] // not sneaking: heard
    [InlineData(5, 1)]    // sneaking poorly: heard (Passive Perception 11)
    [InlineData(25, 0)]   // sneaking well: not even a presence
    public void Only_a_presence_the_player_can_hear_is_reported_in_the_dark(int? sneak, int expected)
    {
        var s = SimulationSession.Create(new ScenarioBuilder()
            .AddArea("a", "A")
            .AddLocation("yard", "Cortile", "a")
            .AddMap("a", new[] { "YYYYY", "YYYYY" }, new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "P", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: (1, 0))
            .AddActor("shadow", "Ombra", "yard", sheet: Sheets.Raider(), at: (2, 0))
            .Build());
        s.AdvanceTo(At(1, 2));
        s.Engine.World.Actors[new ActorId("shadow")].Sneak = sneak;

        Assert.Empty(s.GetPlayerView().VisibleActors);
        Assert.Equal(expected, s.GetPlayerView().UnseenNearby);
    }

    [Fact]
    public void Standing_still_the_player_keeps_watching_a_door_until_dusk_hides_it()
    {
        // A door three squares away, no lamps. The player gives its last order at noon, then just stands there; a villager
        // living in the yard rests an hour at a time, so the world keeps ticking.
        var s = SimulationSession.Create(new ScenarioBuilder()
            .AddArea("a", "A")
            .AddLocation("yard", "Cortile", "a")
            .AddMap("a", new[] { "YYYYYYY" }, new Dictionary<char, string> { ['Y'] = "yard" })
            .AddDoor("a", (3, 0), open: false)
            .AddActor("player", "P", "yard", isPlayer: true, at: (0, 0))
            .AddActor("villager", "Paesano", "yard", at: (6, 0))
            .Build());
        s.AdvanceTo(At(1, 12));
        s.Execute(new WaitCommand { Actor = s.Player, Duration = Duration.FromSeconds(1) }); // the last command

        s.AdvanceTo(At(1, 22)); // dark: the door is no longer in sight

        var door = Door(s, new GridPos(3, 0));
        Assert.Equal(DoorKnowledge.Remembered, door.Known);
        Assert.True(door.SeenAt > At(1, 18), $"last seen at {door.SeenAt}"); // watched through the afternoon, not just at noon
    }
}
