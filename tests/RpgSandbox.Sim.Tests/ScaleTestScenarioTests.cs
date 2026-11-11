using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Sim.Tests;

/// <summary>The two maps of the size playtest: same density of things, every square reachable, the same map every time.</summary>
public class ScaleTestScenarioTests
{
    [Theory]
    [InlineData(96, 64)]
    [InlineData(128, 96)]
    public void A_test_country_is_open_to_walk_end_to_end_and_as_full_as_the_other_size(int width, int height)
    {
        var session = ScaleTestScenario.CreateSession(width, height);
        var map = session.Engine.World.Maps[ScaleTestScenario.Area];
        Assert.Equal((width, height), (map.Width, map.Height));
        Assert.True(map.UsesExploration);
        Assert.Equal(ScaleTestScenario.Start(height), session.GetPlayerView().Position);

        // Every walkable square is reached on foot from the start, through the cottage doors.
        var start = ScaleTestScenario.Start(height);
        var reached = new HashSet<GridPos> { start };
        var queue = new Queue<GridPos>(reached);
        while (queue.Count > 0)
        {
            var here = queue.Dequeue();
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var next = new GridPos(here.X + dx, here.Y + dy);
                if (next != here && map.IsWalkable(next) && map.StepOpen(here, next, sight: false) && reached.Add(next))
                    queue.Enqueue(next);
            }
        }
        var walkable = Enumerable.Range(0, height).SelectMany(y => Enumerable.Range(0, width).Select(x => new GridPos(x, y)))
            .Where(map.IsWalkable).ToList();
        Assert.All(walkable, p => Assert.Contains(p, reached));

        // Things per square stay the same whatever the size: about a cottage every 1,200 squares, a cluster every 100.
        var cottages = map.IndoorZones.Count;
        Assert.InRange(cottages, width * height / 1_200 - 1, width * height / 1_200);
        var rocks = Enumerable.Range(1, height - 2).Sum(y => Enumerable.Range(1, width - 2).Count(x => map.BlocksSight(new GridPos(x, y)) && map.ZoneAt(new GridPos(x, y)) is null));
        Assert.InRange(rocks / (double)(width * height), 0.03, 0.09);
        Assert.Equal(cottages + 1, session.GetWorldView().Actors.Count);

        Assert.Equal(session.SaveToString(), ScaleTestScenario.CreateSession(width, height).SaveToString());
    }

    [Fact]
    public void The_player_crosses_the_larger_country_on_foot()
    {
        var session = ScaleTestScenario.CreateSession(128, 96);
        var to = new GridPos(123, 48);
        var map = session.Engine.World.Maps[ScaleTestScenario.Area];
        if (!map.IsWalkable(to))
            to = new GridPos(123, 47);
        var walk = session.Execute(new MoveToSpotCommand { Actor = session.Player, To = Spot.CenterOf(to) });
        Assert.True(walk.Success, walk.Message);
        Assert.Equal(AdvanceOutcome.Completed, session.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(10)).Outcome);
        Assert.Equal(to, session.GetPlayerView().Position);
    }
}
