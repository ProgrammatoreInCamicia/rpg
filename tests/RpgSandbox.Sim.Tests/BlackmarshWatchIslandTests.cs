using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.BlackmarshWatchIsland.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>
/// Region Task 2: the watch island as one world of three maps joined by paths. Everything is reached through the player's
/// commands and the world's own schedule; the world view is only an oracle here.
/// </summary>
public class BlackmarshWatchIslandTests
{
    [Fact]
    public void The_region_is_one_world_of_three_places()
    {
        var session = BlackmarshWatchIsland.CreateSession();
        var world = session.GetWorldView();

        Assert.Equal(At(1, 19, 35), session.Now);
        Assert.Equal(Player, session.Player);
        Assert.Equal(new[] { Cove, Hamlet, ShedArea }, ScenarioFile.Load(BlackmarshWatchIsland.ScenarioId).Maps.Select(m => m.Area).OrderBy(a => a.Value));
        Assert.Equal(4, world.Actors.Count);
        Assert.Single(world.Actors, a => a.IsPlayer);
        Assert.Equal(new[] { Pantry, ShedStore, VasanCache }, world.Stores.Select(s => s.Id).OrderBy(s => s.Value));
        Assert.Equal(new[] { Islanders, Vasan }, world.Factions.Select(f => f.Id).OrderBy(f => f.Value));
        // A new world, not the old shed situation renamed: one raider, one Vasan store, and that at the cove.
        Assert.All(world.Actors, a => Assert.StartsWith("bm_isle_", a.Id.Value));
        Assert.All(world.Stores, s => Assert.StartsWith("bm_isle_", s.Id.Value));
        Assert.Equal(Landing, world.Store(VasanCache).Location);
        Assert.Equal(Keeper, world.Factions.Single(f => f.Id == Islanders).Authority);
        Assert.Equal(CommonHouse, world.Actor(Fisher).Home);
        Assert.Equal(Well, world.Actor(Keeper).Home);
        Assert.Equal(Landing, world.Actor(Raider).Home);
    }

    [Fact]
    public void The_three_forks_are_joined_both_ways_and_each_direct_path_beats_the_detour()
    {
        var routes = BlackmarshWatchIsland.CreateSession().GetWorldView().Routes;
        Duration Path(LocationId a, LocationId b) => routes.Single(r => r.From == a && r.To == b).TravelTime;

        Assert.Equal(6, routes.Count); // three paths, each both ways
        foreach (var (a, b) in new[] { (HamletFork, ShedFork), (ShedFork, CoveFork), (HamletFork, CoveFork) })
            Assert.Equal(Path(a, b), Path(b, a));
        Assert.True(Path(HamletFork, CoveFork).Ticks < Path(HamletFork, ShedFork).Ticks + Path(ShedFork, CoveFork).Ticks);
        Assert.True(Path(HamletFork, ShedFork).Ticks < Path(HamletFork, CoveFork).Ticks + Path(CoveFork, ShedFork).Ticks);
        Assert.True(Path(ShedFork, CoveFork).Ticks < Path(ShedFork, HamletFork).Ticks + Path(HamletFork, CoveFork).Ticks);
    }

    [Fact]
    public void The_shed_keeps_the_plan_of_the_watchhouse()
    {
        var old = ScenarioFile.Load(BlackmarshWatchhouse.ScenarioId).Maps.Single();
        var shed = ScenarioFile.Load(BlackmarshWatchIsland.ScenarioId).Maps.Single(m => m.Area == ShedArea);

        // Only the zones change: the old path becomes the fork, the old landing open ground.
        Assert.Equal(old.Rows.Select(r => r.Replace('P', 'E').Replace('A', '.')[1..]), shed.Rows.Select(r => r[1..]));
        Assert.Equal(Describe(old.Edges), Describe(shed.Edges));
        Assert.Equal(old.Obstacles, shed.Obstacles);
        Assert.Equal(old.Lights, shed.Lights);
        Assert.Equal(old.Locks, shed.Locks);
        static string[] Describe(IEnumerable<MapEdgeView> edges) => edges.Select(e => $"{e.A}{e.B}{e.Kind}{e.DoorAt}").Order().ToArray();
    }

    [Fact]
    public void A_new_world_is_the_same_every_time_and_survives_save_and_load()
    {
        var first = BlackmarshWatchIsland.CreateSession();
        Assert.Equal(first.SaveToString(), BlackmarshWatchIsland.CreateSession().SaveToString());

        var reloaded = first.SaveAndReload();
        first.AdvanceTo(At(1, 21));
        reloaded.AdvanceTo(At(1, 21));
        Assert.Equal(first.SaveToString(), reloaded.SaveToString());
    }

    [Fact]
    public void The_fisher_walks_to_his_shift_at_the_shed_and_back_home_on_his_own()
    {
        var session = BlackmarshWatchIsland.CreateSession();
        var fisher = session.GetWorldView().Actor(Fisher);
        Assert.Equal(Shed, fisher.Location); // he went there at 16:00, by the path

        session.AdvanceTo(At(1, 20, 5));
        var facts = session.GetWorldView().RecentFacts;
        Assert.Contains(facts, f => f.Kind == "TravelStarted" && f.Description.StartsWith("Pescatore parte da Bivio della rimessa"));
        Assert.Contains(facts, f => f.Kind == "TravelCompleted" && f.Description.StartsWith("Pescatore arriva a Bivio del nucleo"));
        fisher = session.GetWorldView().Actor(Fisher);
        Assert.Equal(CommonHouse, fisher.Location);
        Assert.Equal(new GridPos(5, 7), fisher.Position);
    }

    [Fact]
    public void The_Vasan_raid_sets_off_on_its_own_for_the_nearer_shed()
    {
        var session = BlackmarshWatchIsland.CreateSession();

        session.AdvanceTo(At(1, 20, 1));

        var raider = session.GetWorldView().Actor(Raider);
        Assert.Equal(ShedStore, raider.Assignment?.Target);
        Assert.Contains(session.GetWorldView().RecentFacts, f => f.Kind == "RaidOrdered");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_player_can_go_round_the_island_either_way_on_foot(bool coveFirst)
    {
        var session = BlackmarshWatchIsland.CreateSession();
        var tour = coveFirst ? new[] { CoveFork, ShedFork, HamletFork } : new[] { ShedFork, CoveFork, HamletFork };

        foreach (var fork in tour)
        {
            GoTo(session, fork);
            var view = session.GetPlayerView();
            Assert.Equal(fork, view.Location);
            Assert.Equal(AreaOf(fork), view.Area);
            Assert.Equal(ExitOf(fork), view.Position);
        }
        Assert.Contains(session.GetWorldView().RecentFacts, f => f.Kind == "TravelCompleted" && f.Description.StartsWith("Protagonista arriva a Bivio del nucleo"));
    }

    [Fact]
    public void A_journey_leaves_only_from_the_fork()
    {
        var session = BlackmarshWatchIsland.CreateSession();
        Walk(session, new GridPos(HamletExit.X - 1, HamletExit.Y)); // on the fork, a step short of the edge
        Assert.Equal(HamletFork, session.GetPlayerView().Location);

        var early = session.Execute(new TravelCommand { Actor = session.Player, Destination = ShedFork });

        Assert.False(early.Success);
        Assert.Equal(RejectionReason.NotAtExit, early.Rejection);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Saving_on_the_way_or_on_another_map_resumes_the_same_world(bool onTheWay)
    {
        var continuous = BlackmarshWatchIsland.CreateSession();
        Walk(continuous, HamletExit);
        var journey = continuous.Execute(new TravelCommand { Actor = continuous.Player, Destination = CoveFork });
        Assert.True(journey.Success, journey.Message);
        if (!onTheWay)
            Assert.Equal(AdvanceOutcome.Completed, continuous.AdvanceUntilCompleted(journey.Action!.Value, Duration.FromMinutes(20)).Outcome);
        else
            continuous.Advance(Duration.FromMinutes(5));

        var reloaded = continuous.SaveAndReload();
        continuous.AdvanceTo(At(1, 20, 40));
        reloaded.AdvanceTo(At(1, 20, 40));
        Assert.Equal(continuous.SaveToString(), reloaded.SaveToString());
        Assert.Equal(Cove, reloaded.GetPlayerView().Area);
    }

    private static AreaId AreaOf(LocationId fork) => fork == HamletFork ? Hamlet : fork == ShedFork ? ShedArea : Cove;

    private static GridPos ExitOf(LocationId fork) => fork == HamletFork ? HamletExit : fork == ShedFork ? ShedExit : CoveExit;

    /// <summary>To the fork of the map the player is on, then along the path to <paramref name="fork"/>.</summary>
    private static void GoTo(SimulationSession session, LocationId fork)
    {
        var here = session.GetPlayerView().Area;
        Walk(session, here == Hamlet ? HamletExit : here == ShedArea ? ShedExit : CoveExit);
        Do(session, new TravelCommand { Actor = session.Player, Destination = fork });
    }

    private static void Walk(SimulationSession session, GridPos to)
    {
        if (session.GetPlayerView().Position != to)
            Do(session, new MoveToSpotCommand { Actor = session.Player, To = Spot.CenterOf(to) });
    }

    private static void Do(SimulationSession session, Command command)
    {
        var result = session.Execute(command);
        Assert.True(result.Success, result.Message);
        if (result.Action is { } action)
            Assert.Equal(AdvanceOutcome.Completed, session.AdvanceUntilCompleted(action, Duration.FromMinutes(20)).Outcome);
    }
}
