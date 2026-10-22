using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using Xunit.Abstractions;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

public class PlayableSliceTests(ITestOutputHelper output)
{
    private static readonly GridPos Lookout = new(16, 7);

    [Fact]
    public void Entry_is_reproducible_and_precedes_the_first_theft()
    {
        var first = PlayableSlice.CreateSession();
        var second = PlayableSlice.CreateSession();
        Assert.Equal(first.SaveToString(), second.SaveToString());
        Assert.Equal(At(1, 19, 27), first.Now);
        Assert.Equal(PlayerStart, first.GetPlayerView().Position);
        Assert.Equal(40, first.GetWorldView().Store(GranaryStore).Food);
        Assert.Empty(first.GetPlayerView().Observations);
        Assert.Null(first.GetWorldView().Actor(Guard).GuardDuty);
        Assert.DoesNotContain(first.GetWorldView().RecentFacts, f => f.Kind == "FoodStolen");
    }

    [Fact]
    public void Reporting_an_observed_theft_changes_the_guard_at_the_same_checkpoint()
    {
        var report = PlayableSlice.CreateSession();
        var ignore = PlayableSlice.CreateSession();
        Assert.Equal(report.SaveToString(), ignore.SaveToString());
        Observe(report);
        Observe(ignore);
        Assert.Equal(report.SaveToString(), ignore.SaveToString());
        var observation = Assert.Single(report.GetPlayerView().Observations);
        output.WriteLine($"Observation: {observation}");
        Assert.Equal(GranaryStore, observation.Store);
        Assert.Equal("Theft", observation.Kind);
        Assert.Equal("Seen", observation.Perceived);
        Assert.Equal(Raider, observation.Culprit);
        Assert.Null(observation.Source);
        Assert.Equal(8, observation.Amount);
        Assert.Equal(new GameTime(19 * 3600 + 33 * 60 + 12), observation.ObservedAt);
        Assert.Equal(32, report.GetWorldView().Store(GranaryStore).Food);
        Assert.Null(report.GetWorldView().Actor(Guard).GuardDuty);

        Walk(report, new GridPos(4, 2));
        Walk(ignore, new GridPos(4, 2));
        report.FacePlayer(1, 0);
        ignore.FacePlayer(1, 0);
        Assert.Equal(report.SaveToString(), ignore.SaveToString());
        var person = Assert.Single(report.GetPlayerView().PeopleHere, p => p.Id == Guard);
        Assert.Contains(person.Topics, t => t.Observation == observation.Id);
        var telling = report.Execute(new ReportCommand { Actor = report.Player, Recipient = Guard, Observation = observation.Id });
        Assert.True(telling.Success, telling.Message);
        report.Advance(Duration.FromTicks(telling.CompletesAt!.Value.Since(report.Now).Ticks - GameTime.TicksPerSecond));
        Assert.Empty(report.GetWorldView().Actor(Guard).Knowledge);
        Assert.Null(report.GetWorldView().Actor(Guard).GuardDuty);
        Assert.Equal(AdvanceOutcome.Completed,
            report.AdvanceUntilCompleted(telling.Action!.Value, Duration.FromSeconds(1)).Outcome);
        Assert.Contains(report.GetWorldView().Actor(Guard).Knowledge, o => o.Origin == observation.Origin && o.Source == Player);
        output.WriteLine($"Report completed: {report.Now}");
        AdvanceUntil(report, () => report.GetWorldView().Actor(Guard).Position == GuardPost, At(1, 20, 0));
        output.WriteLine($"Guard at post: {report.Now}");

        var checkpoint = At(1, 20, 0);
        report.AdvanceTo(checkpoint);
        ignore.AdvanceTo(checkpoint);
        var informed = report.GetWorldView().Actor(Guard);
        var uninformed = ignore.GetWorldView().Actor(Guard);
        Assert.Equal(GranaryStore, informed.GuardDuty!.Store);
        Assert.Equal(GuardPost, informed.Position);
        Assert.Contains(informed.Knowledge, o => o.Origin == observation.Origin && o.Source == Player);
        Assert.Null(uninformed.GuardDuty);
        Assert.Empty(uninformed.Knowledge);
        Assert.Equal(GuardStart, uninformed.Position);
        Assert.Equal(32, report.GetWorldView().Store(GranaryStore).Food);
        Assert.Equal(32, ignore.GetWorldView().Store(GranaryStore).Food);
        Assert.Null(report.GetPlayerView().Combat);
    }

    [Theory]
    [InlineData(false, true)] // closed inn, no torch: the player hears but cannot identify the thief
    [InlineData(true, false)] // torch, open inn: the guard sees it too, without a report
    public void Light_and_the_inn_door_are_real_preconditions_not_staged_knowledge(bool torch, bool closeDoor)
    {
        var session = PlayableSlice.CreateSession();
        PrepareLookout(session, torch, closeDoor);
        AdvanceUntil(session, () => session.Now >= At(1, 19, 35), At(1, 19, 35));
        Assert.Equal(32, session.GetWorldView().Store(GranaryStore).Food);
        var guard = session.GetWorldView().Actor(Guard);
        if (torch)
        {
            Assert.Single(session.GetPlayerView().Observations);
            var witnessed = Assert.Single(guard.Knowledge);
            Assert.Null(witnessed.Source);
            Assert.Equal("Seen", witnessed.Perceived);
            Assert.Equal(GranaryStore, guard.GuardDuty!.Store);
        }
        else
        {
            var heard = Assert.Single(session.GetPlayerView().Observations);
            Assert.Equal("Heard", heard.Perceived);
            Assert.Null(heard.Culprit);
            Assert.Empty(guard.Knowledge);
            Assert.Null(guard.GuardDuty);
        }
    }

    [Fact]
    public void Remaining_inside_the_inn_does_not_reveal_the_autonomous_theft()
    {
        var session = PlayableSlice.CreateSession();
        session.FacePlayer(1, 0);
        AdvanceUntil(session, () => session.Now >= At(1, 19, 35), At(1, 19, 35));
        Assert.Equal(32, session.GetWorldView().Store(GranaryStore).Food);
        Assert.Empty(session.GetPlayerView().Observations);
        Assert.DoesNotContain(session.GetPlayerView().VisibleActors, a => a.Id == Raider);
        Assert.All(session.GetPlayerView().PeopleHere, p => Assert.Empty(p.Topics));
        Assert.Null(session.GetWorldView().Actor(Guard).GuardDuty);
    }

    private void Observe(SimulationSession session)
    {
        PrepareLookout(session, torch: true, closeDoor: true);
        output.WriteLine($"Lookout reached: {session.Now}");
        AdvanceUntil(session, () => session.GetPlayerView().Observations.Count > 0, At(1, 19, 35));
        output.WriteLine($"Theft observed: {session.Now}");
    }

    private static void PrepareLookout(SimulationSession session, bool torch, bool closeDoor)
    {
        Walk(session, new GridPos(8, 3));
        if (closeDoor)
        {
            var door = session.Execute(new DoorCommand { Actor = session.Player, At = InnDoor, Open = false });
            Assert.True(door.Success, door.Message);
        }
        Walk(session, Lookout);
        session.FacePlayer(0, -1); // toward the granary access at (16, 5)
        if (torch)
        {
            var lit = session.Execute(new TorchCommand { Actor = session.Player, Lit = true, StowShield = true });
            Assert.True(lit.Success, lit.Message);
        }
    }

    private static void Walk(SimulationSession session, GridPos to) =>
        Complete(session, new MoveToSpotCommand { Actor = session.Player, To = Spot.CenterOf(to) });

    private static void Complete(SimulationSession session, Command command)
    {
        var result = session.Execute(command);
        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Action);
        var end = session.AdvanceUntilCompleted(result.Action.Value, Duration.FromMinutes(10));
        Assert.Equal(AdvanceOutcome.Completed, end.Outcome);
    }

    private static void AdvanceUntil(SimulationSession session, Func<bool> condition, GameTime limit)
    {
        while (!condition() && session.Now < limit)
        {
            var before = session.Now;
            session.Advance(Duration.FromTicks(Math.Min(GameTime.TicksPerSecond, limit.Since(session.Now).Ticks)));
            Assert.True(session.Now > before, "Time stopped before the expected event.");
        }
        Assert.True(condition(), $"Expected event missing at {session.Now}.");
    }
}
