using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>P1c: one fine clock (60 ticks a second); a walker is at an exact point at every tick and stops there.</summary>
public class FineClockTests
{
    /// <summary>The prototype house at 13:00; the player at the centre of (9,4), (570, 270) inches.</summary>
    private static SimulationSession House()
    {
        var s = SimulationSession.Create(InteriorPrototypeScenario.Create());
        s.AdvanceTo(At(1, 13));
        return s;
    }

    private static readonly Spot Start = new(570, 270);

    private static Duration Ticks(long n) => Duration.FromTicks(n);

    [Fact]
    public void Seconds_are_sixty_ticks_and_whole_seconds_keep_their_meaning()
    {
        Assert.Equal(60, Duration.FromSeconds(1).Ticks);
        Assert.Equal(new GameTime(90), GameTime.FromTicks(5400));
        var t = new GameTime(3661).Plus(Ticks(30));
        Assert.Equal((3661L, 1, 1, 1), (t.Seconds, t.Hour, t.Minute, t.Second));
        Assert.True(t > new GameTime(3661) && t < new GameTime(3662));
    }

    [Fact]
    public void Stopping_half_a_second_into_a_free_walk_leaves_the_walker_where_it_is_not_a_step_back()
    {
        var s = House();
        var to = new Spot(570, 470); // straight down, 200 inches
        s.Execute(new MoveToSpotCommand { Actor = s.Player, To = to });
        s.Advance(Ticks(75)); // a second and a quarter at an inch a tick, between two checkpoints (60 and 90)

        Assert.Equal(new Spot(570, 345), s.GetPlayerView().Spot); // not the checkpoint behind it
        Assert.True(s.Execute(new StopCommand { Actor = s.Player }).Success);

        Assert.Equal(new Spot(570, 345), s.GetPlayerView().Spot);
        Assert.Equal(new GridPos(9, 5), s.GetPlayerView().Position);
        Assert.Null(s.GetPlayerView().Move);
    }

    [Fact]
    public void Stopping_between_two_squares_of_a_walk_on_squares_leaves_the_walker_between_them()
    {
        var s = House();
        s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(11, 4) });
        s.Advance(Ticks(75)); // one square and a quarter at 60 ticks a square

        Assert.True(s.Execute(new StopCommand { Actor = s.Player }).Success);

        Assert.Equal(new Spot(645, 270), s.GetPlayerView().Spot); // a quarter of the way from (10,4) to (11,4)
        Assert.Equal(new GridPos(10, 4), s.GetPlayerView().Position);
    }


    [Fact]
    public void A_diagonal_step_on_squares_still_takes_its_60_ticks_and_is_halfway_at_30()
    {
        // On squares a diagonal is one step (60 ticks at Speed 30) although the centres are about 85 inches apart: the
        // walker moves between them in step with those ticks, not an inch a tick (review P1c).
        var s = House();
        s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(10, 5) });

        s.Advance(Ticks(30));
        Assert.Equal(new Spot(600, 300), s.GetPlayerView().Spot);
        s.Advance(Ticks(30));
        Assert.Equal(Spot.CenterOf(new GridPos(10, 5)), s.GetPlayerView().Spot);
        Assert.Null(s.GetPlayerView().Move);
    }
    [Fact]
    public void A_new_order_in_the_same_tick_sets_off_from_the_point_reached()
    {
        var s = House();
        s.Execute(new MoveToSpotCommand { Actor = s.Player, To = new Spot(570, 470) });
        s.Advance(Ticks(45));
        var here = s.GetPlayerView().Spot!.Value;
        Assert.Equal(new Spot(570, 315), here);

        Assert.True(s.Execute(new MoveToSpotCommand { Actor = s.Player, To = new Spot(700, 315) }).Success);

        Assert.Equal(here, s.GetPlayerView().Move!.FromSpot);
        Assert.Equal(here, s.GetPlayerView().Spot);
    }

    [Fact]
    public void Advancing_in_one_go_or_tick_by_tick_gives_the_same_world_even_through_a_door()
    {
        SimulationSession Walking()
        {
            var s = House();
            // Into the house: the closed door is opened on the way, and the walk turns at the doorway.
            Assert.True(s.Execute(new MoveToSpotCommand { Actor = s.Player, To = new Spot(265, 205) }).Success);
            return s;
        }
        var once = Walking();
        var steps = Walking();

        once.Advance(Ticks(613));
        for (var i = 0; i < 613; i += 7)
            steps.Advance(Ticks(Math.Min(7, 613 - i)));

        Assert.Equal(once.Now, steps.Now);
        Assert.Equal(once.SaveToString(), steps.SaveToString());
        Assert.Contains(once.GetPlayerView().RecentEvents, f => f.Kind == "DoorOpened");
    }

    [Fact]
    public void A_save_half_a_second_into_a_walk_then_a_stop_gives_the_same_as_without_the_save()
    {
        var a = House();
        a.Execute(new MoveToSpotCommand { Actor = a.Player, To = new Spot(265, 205) });
        a.Advance(Ticks(150));
        var b = a.SaveAndReload();

        a.Advance(Ticks(31));
        b.Advance(Ticks(31));
        a.Execute(new StopCommand { Actor = a.Player });
        b.Execute(new StopCommand { Actor = b.Player });

        Assert.Equal(a.GetPlayerView().Spot, b.GetPlayerView().Spot);
        Assert.Equal(a.SaveToString(), b.SaveToString());
    }

    [Fact]
    public void A_wait_lasts_exactly_its_ticks_and_a_torch_its_hour_from_any_tick()
    {
        var s = House();
        var start = s.Now.Plus(Ticks(17));
        s.Advance(Ticks(17));

        var wait = s.Execute(new WaitCommand { Actor = s.Player, Duration = Ticks(90) });

        Assert.Equal(start.Plus(Ticks(90)), wait.CompletesAt);
        s.AdvanceUntilCompleted(wait.Action!.Value, Duration.FromMinutes(1));
        Assert.Equal(start.Plus(Ticks(90)), s.Now);
        // A torch lit now burns its hour to the tick.
        Assert.True(s.Execute(new TorchCommand { Actor = s.Player, Lit = true }).Success);
        Assert.Equal(Duration.FromHours(1), s.GetPlayerView().TorchLitUntil!.Value.Since(s.Now));
    }
}
