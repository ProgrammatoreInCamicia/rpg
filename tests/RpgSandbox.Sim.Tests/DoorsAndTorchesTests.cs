using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>F2: doors that block sight, light and (partly) sound; torches carried as moving light.</summary>
public class DoorsAndTorchesTests
{
    private static SimulationSession Village(int hour = 2)
    {
        var s = SimulationSession.Create(MappedVillageScenario.Create());
        s.AdvanceTo(At(1, hour));
        return s;
    }

    private static void WalkTo(SimulationSession s, GridPos to, bool stealthy = false)
    {
        var walk = s.Execute(new MoveCommand { Actor = s.Player, To = to, Stealthy = stealthy });
        Assert.True(walk.Success, walk.Message);
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromHours(1));
    }

    private static CommandResult Door(SimulationSession s, bool open) =>
        s.Execute(new DoorCommand { Actor = s.Player, At = InnDoor, Open = open });

    private static char LightAt(SimulationSession s, int x, int y) => s.GetPlayerView().MapLight![y][x];

    [Fact]
    public void Closing_the_inn_door_keeps_the_lamplight_and_the_view_inside()
    {
        var s = Village();
        WalkTo(s, new GridPos(8, 3)); // on the road, just outside the door
        Assert.Equal('1', LightAt(s, 9, 3));
        Assert.Contains(s.GetPlayerView().VisibleActors, a => a.Id == Guard);

        Assert.True(Door(s, open: false).Success);

        Assert.Equal('0', LightAt(s, 9, 3));
        Assert.DoesNotContain(s.GetPlayerView().VisibleActors, a => a.Id == Guard);
        Assert.Contains(s.GetPlayerView().Doors, d => d.At == InnDoor && !d.Open);
    }

    [Fact]
    public void A_closed_door_muffles_sound()
    {
        var map = SimulationSession.Create(MappedVillageScenario.Create()).Engine.World.Maps[MappedVillageScenario.Ids.Village];
        var inside = new GridPos(6, 3);
        var outside = new GridPos(9, 3);
        Assert.Equal(3, map.SoundSteps(inside, outside, Tuning.HearingRadius));

        map.SetDoor(InnDoor, open: false);

        Assert.Equal(3 + Tuning.ClosedDoorSoundSteps, map.SoundSteps(inside, outside, Tuning.HearingRadius));
    }

    [Fact]
    public void Walking_through_a_closed_door_opens_it_on_the_square_before()
    {
        var s = Village();
        WalkTo(s, new GridPos(6, 3));
        Assert.True(Door(s, open: false).Success);
        WalkTo(s, new GridPos(4, 3));

        var walk = s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(10, 3) });
        Assert.True(walk.Success, walk.Message);
        s.Advance(Duration.FromSeconds(1)); // (5,3): the door is two squares ahead
        Assert.False(s.GetPlayerView().Doors.Single().Open);
        s.Advance(Duration.FromSeconds(1)); // (6,3): next to the door, it opens
        Assert.True(s.GetPlayerView().Doors.Single().Open);
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromHours(1));
        Assert.Equal(new GridPos(10, 3), s.GetPlayerView().Position);
    }

    [Fact]
    public void A_door_is_used_from_next_to_it_and_never_shut_on_someone()
    {
        var s = Village();
        Assert.Equal(RejectionReason.OutOfReach, Door(s, open: false).Rejection); // from (2,2)
        Assert.Equal(RejectionReason.NotADoor,
            s.Execute(new DoorCommand { Actor = s.Player, At = new GridPos(3, 3), Open = false }).Rejection);

        WalkTo(s, new GridPos(6, 3));
        Assert.Equal(RejectionReason.AlreadyDone, Door(s, open: true).Rejection);

        var guardOnTheDoor = s.Engine.World.Actors[Guard];
        guardOnTheDoor.Position = InnDoor; // standing in the doorway
        guardOnTheDoor.Location = null;
        Assert.Equal(RejectionReason.DoorBlocked, Door(s, open: false).Rejection);
    }

    [Fact]
    public void A_torch_burns_for_an_hour_around_its_bearer_and_is_spent()
    {
        var s = Village();
        WalkTo(s, new GridPos(16, 7)); // the dark granary yard
        Assert.Equal('0', LightAt(s, 16, 7));

        var lit = s.Execute(new TorchCommand { Actor = s.Player, Lit = true });
        Assert.True(lit.Success, lit.Message);
        var view = s.GetPlayerView();
        Assert.Equal(2, view.Torches);
        Assert.Equal(s.Now.Plus(Duration.FromHours(1)), view.TorchLitUntil);
        Assert.Equal('2', LightAt(s, 16, 7));
        Assert.Equal('2', LightAt(s, 20, 7));  // 20 ft: bright
        Assert.Equal('1', LightAt(s, 21, 7));  // 25 ft: dim
        Assert.Equal(Light.Bright, view.Light);

        WalkTo(s, new GridPos(12, 7));
        Assert.Equal('2', LightAt(s, 12, 7)); // the light goes with the bearer
        Assert.Equal('0', LightAt(s, 21, 7));

        s.Advance(Duration.FromHours(1));
        Assert.Null(s.GetPlayerView().TorchLitUntil);
        Assert.Equal('0', LightAt(s, 12, 7)); // burnt out
    }

    [Fact]
    public void Putting_out_a_torch_spends_it_and_running_out_is_refused()
    {
        var s = Village();
        for (var i = 0; i < 3; i++)
        {
            Assert.True(s.Execute(new TorchCommand { Actor = s.Player, Lit = true }).Success);
            Assert.True(s.Execute(new TorchCommand { Actor = s.Player, Lit = false }).Success);
        }
        Assert.Equal(RejectionReason.NoTorch, s.Execute(new TorchCommand { Actor = s.Player, Lit = true }).Rejection);
        Assert.Equal(RejectionReason.AlreadyDone, s.Execute(new TorchCommand { Actor = s.Player, Lit = false }).Rejection);
    }

    [Fact]
    public void Sneaking_with_a_lit_torch_gives_you_away()
    {
        var s = Village();
        // A dark square of the road in view of the lamplit guard, through the door.
        var map = s.Engine.World.Maps[MappedVillageScenario.Ids.Village];
        var dark = Enumerable.Range(8, 15).SelectMany(x => Enumerable.Range(1, 9).Select(y => new GridPos(x, y)))
            .First(p => map.IsWalkable(p) && map.SourceLight(p) == Light.Dark && map.HasLineOfSight(GuardStart, p));
        WalkTo(s, dark, stealthy: true);
        var guard = s.GetPlayerView().VisibleActors.Single(a => a.Id == Guard);
        Assert.False(guard.SeesYou);

        Assert.True(s.Execute(new TorchCommand { Actor = s.Player, Lit = true }).Success);

        Assert.NotNull(s.GetPlayerView().Sneaking); // a torch is not noise...
        Assert.True(s.GetPlayerView().VisibleActors.Single(a => a.Id == Guard).SeesYou); // ...but light
    }

    [Fact]
    public void Doors_and_torches_survive_save_and_load()
    {
        var s = Village();
        WalkTo(s, new GridPos(8, 3));
        Door(s, open: false);
        s.Execute(new TorchCommand { Actor = s.Player, Lit = true });

        var loaded = s.SaveAndReload();

        Assert.False(loaded.GetPlayerView().Doors.Single().Open);
        Assert.Equal(s.GetPlayerView().TorchLitUntil, loaded.GetPlayerView().TorchLitUntil);
        Assert.Equal(2, loaded.GetPlayerView().Torches);
        Assert.Equal(s.GetPlayerView().MapLight, loaded.GetPlayerView().MapLight);
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
    }
}
