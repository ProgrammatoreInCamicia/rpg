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
    public void The_shed_keeps_the_arrangement_of_the_watchhouse()
    {
        // Region Task 6B: on its own squares now, the shed is still the watchhouse's: a locked door into a front room, an
        // inner door to the store's room, a stuck window there, and the lamp by the crate.
        var view = BlackmarshWatchIsland.CreateSession().GetWorldView();
        var shed = ScenarioFile.Load(BlackmarshWatchIsland.ScenarioId).Maps.Single(m => m.Area == ShedArea);
        var doors = shed.Edges.Where(e => e.Kind == MapEdgeKind.Door).Select(e => e.DoorAt!.Value).Order().ToArray();
        Assert.Equal(new[] { ShedDoor, InnerDoor }.Order(), doors);
        var window = Assert.Single(shed.Edges, e => e.Kind == MapEdgeKind.Window);
        Assert.Equal((ShedWindowInside, ShedWindowOutside), (window.A, window.B));

        var map = BlackmarshWatchIsland.CreateSession().Engine.World.Maps[ShedArea];
        Assert.Equal(Shed, map.ZoneAt(ShedDoor));
        Assert.Equal(Shed, map.ZoneAt(StoreAccess));
        Assert.Null(map.ZoneAt(ShedWindowOutside));
        Assert.True(map.IsClosedDoor(ShedDoor) && map.IsClosedDoor(InnerDoor));
        var barriers = BlackmarshWatchIsland.CreateSession().Engine.World.Maps[ShedArea].Barriers;
        Assert.Contains(barriers, b => b.Key == BarrierKey.Door(ShedDoor) && b.Value.Locked && b.Value.Key == ShedKey);
        Assert.Contains(barriers, b => b.Key == BarrierKey.Window(ShedWindowInside, ShedWindowOutside) && b.Value.Stuck);
        // The crate is in the inner room, past the inner door, with the lamp beside it.
        Assert.True(StoreAccess.X > InnerDoor.X && view.Store(ShedStore).Position!.Value.X > InnerDoor.X);
        Assert.Contains(map.Lights, l => l.At.X > InnerDoor.X);
    }

    [Fact]
    public void On_each_large_map_every_walkable_square_is_reached_on_foot_from_the_fork()
    {
        // Region Task 6B: 48×32 places with rocks, reeds and buildings, but no pocket cut off: through doors (walking
        // opens them; the shed's needs the key the player carries), never through walls or a window.
        var world = BlackmarshWatchIsland.CreateSession().Engine.World;
        foreach (var (area, exit) in new[] { (Hamlet, HamletExit), (ShedArea, ShedExit), (Cove, CoveExit) })
        {
            var map = world.Maps[area];
            Assert.Equal((48, 32), (map.Width, map.Height));
            var reached = new HashSet<GridPos> { exit };
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
            var walkable = Enumerable.Range(0, map.Height).SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new GridPos(x, y)))
                .Where(map.IsWalkable).ToList();
            Assert.All(walkable, p => Assert.Contains(p, reached));
            Assert.True(walkable.Count > 1_000, $"{area}: {walkable.Count} walkable squares");
        }
    }

    [Fact]
    public void The_cove_is_explored_like_the_other_places_though_it_has_no_building()
    {
        // Playtest of 11 November: on the cove, with no interior, the player saw the whole map and no fog of war.
        var session = BlackmarshWatchIsland.CreateSession();
        GoTo(session, CoveFork);
        session.FacePlayer(1, 0);
        var view = session.GetPlayerView();
        Assert.True(session.Engine.World.Maps[Cove].DeclaresExploration);
        Assert.Empty(session.Engine.World.Maps[Cove].IndoorZones);
        Assert.NotNull(view.MapVisibility);
        var seen = view.MapVisibility!.Sum(r => r.Count(c => c == '2'));
        var unknown = view.MapVisibility!.Sum(r => r.Count(c => c == '0'));
        Assert.True(seen > 100 && unknown > 100, $"seen {seen}, unknown {unknown}: a cone over the beach, the rest in the fog");

        var reloaded = session.SaveAndReload();
        Assert.True(reloaded.Engine.World.Maps[Cove].UsesExploration);
        Assert.Equal(view.MapVisibility, reloaded.GetPlayerView().MapVisibility);
    }

    [Fact]
    public void Every_guard_post_covers_its_store_without_standing_on_the_way_to_it()
    {
        // A guard stands at its place's guard post and covers a store only from beside a square the store is used from
        // (Covers): a post anywhere else would leave the store guarded in name only.
        var world = BlackmarshWatchIsland.CreateSession().Engine.World;
        var checkedPosts = 0;
        foreach (var store in world.Stores.Values.Where(s => s.Position is not null))
        {
            var map = world.Maps[world.Locations[store.Location].Area];
            if (!map.Posts.TryGetValue((store.Location, PostKind.Guard), out var post))
                continue;
            checkedPosts++;
            Assert.DoesNotContain(post, store.Access);
            Assert.Contains(store.Access, a => post.IsAdjacentOrSame(a) && map.HasLineOfSight(post, a));
        }
        Assert.Equal(2, checkedPosts); // the pantry and the shed's crate
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
        Assert.Equal(new GridPos(12, 15), fisher.Position);
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
