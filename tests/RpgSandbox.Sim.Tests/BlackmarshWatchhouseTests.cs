using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using Xunit.Abstractions;
using static RpgSandbox.Sim.Scenarios.BlackmarshWatchhouse.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>
/// Astra Blackmarsh Task 1: the watch island's store shed is a situation, not a script. The raid comes from the Vasan's own
/// upkeep and policy; the player's commands alone tell the outcomes apart. The world view is only an oracle here.
/// </summary>
public class BlackmarshWatchhouseTests(ITestOutputHelper output)
{
    private static readonly GameTime Checkpoint = At(1, 20, 30);

    /// <summary>The food of the island at the start: the player's 10, the shed's 4, the Vasan cache's 4.</summary>
    private const int FoodAtStart = 18;

    [Fact]
    public void Entry_is_reproducible_and_precedes_the_raid()
    {
        var first = BlackmarshWatchhouse.CreateSession();
        var second = BlackmarshWatchhouse.CreateSession();
        Assert.Equal(first.SaveToString(), second.SaveToString());
        Assert.Equal(At(1, 19, 57), first.Now);

        var world = first.GetWorldView();
        Assert.All(world.Actors, a => Assert.StartsWith("bm_watch_", a.Id.Value));
        Assert.All(world.Stores, s => Assert.StartsWith("bm_watch_", s.Id.Value));
        Assert.Equal(Player, first.Player);
        Assert.Equal(4, world.Store(ShedStore).Food);
        Assert.Equal(4, world.Store(VasanCache).Food);
        Assert.Equal(Islanders, world.Store(ShedStore).Owner);
        Assert.Null(world.Actor(Raider).Assignment);
        Assert.DoesNotContain(world.RecentFacts, f => f.Kind is "RaidOrdered" or "FoodConsumed");

        var view = first.GetPlayerView();
        Assert.Equal(PlayerStart, view.Position);
        Assert.Equal(10, view.Food);
        Assert.Contains(view.Inventory, i => i.Item.Value == ShedKey);
        Assert.Empty(view.Observations);
        Assert.DoesNotContain(view.VisibleActors, a => a.Id == Raider);
    }

    [Fact]
    public void Left_alone_the_raider_breaks_in_through_the_window_and_takes_the_rations_home()
    {
        var session = BlackmarshWatchhouse.CreateSession();
        var trail = Follow(session, Checkpoint);

        var ordered = Assert.Single(session.GetWorldView().RecentFacts, f => f.Kind == "RaidOrdered");
        Assert.Equal(At(1, 20, 0), ordered.At); // the evaluation after the 19:59 upkeep emptied the cache
        Assert.Contains(session.GetWorldView().RecentFacts, f => f.Kind == "GlassBroken");
        Assert.Contains(session.GetWorldView().RecentFacts, f => f.Kind == "Climbed");
        Assert.DoesNotContain(trail, t => t.Position == ShedDoor); // the locked door held: in by the window
        Assert.DoesNotContain(trail, t => t.Position == Table); // the Vasan goes around the same obstacle as the player
        Assert.Equal(WindowGlass.Broken, session.Engine.World.Maps[Island].GlassOf(WindowInside, WindowOutside));
        Assert.True(session.Engine.World.Maps[Island].IsClosedDoor(ShedDoor));
        output.WriteLine($"At the store {Reached(trail)}, home with the rations by {Deposited(session)}.");

        var world = session.GetWorldView();
        Assert.Equal(0, world.Store(ShedStore).Food);
        Assert.Equal(4, world.Store(VasanCache).Food);
        Assert.Equal(10, world.Actor(Player).Food);
        Assert.Equal(0, world.Actor(Raider).Food);
        Assert.Null(world.Actor(Raider).Assignment);
        Assert.Contains(world.RecentFacts, f => f.Kind == "FoodStolen");
        Assert.Contains(world.RecentFacts, f => f.Kind == "FoodDeposited");
        AssertFoodConserved(session);

        // On the path, far from the shed, the player learned nothing of it.
        var view = session.GetPlayerView();
        Assert.Equal(PlayerStart, view.Position);
        Assert.Empty(view.Observations);
        Assert.DoesNotContain(view.VisibleActors, a => a.Id == Raider);
    }

    [Fact]
    public void Recovering_the_rations_first_leaves_the_raider_empty_handed_at_the_same_checkpoint()
    {
        var recover = BlackmarshWatchhouse.CreateSession();
        var renounce = BlackmarshWatchhouse.CreateSession();
        Assert.Equal(recover.SaveToString(), renounce.SaveToString());

        Recover(recover);
        output.WriteLine($"Rations recovered at {recover.Now}.");
        Walk(recover, PlayerStart);
        output.WriteLine($"Back on the path at {recover.Now}.");
        recover.AdvanceTo(Checkpoint);
        renounce.AdvanceTo(Checkpoint);

        var kept = recover.GetWorldView();
        Assert.Equal(14, kept.Actor(Player).Food);
        Assert.Equal(PlayerStart, kept.Actor(Player).Position);
        Assert.Equal(0, kept.Store(ShedStore).Food);
        Assert.Equal(0, kept.Store(VasanCache).Food); // the upkeep ate it and the raid brought nothing back
        Assert.Equal(0, kept.Actor(Raider).Food);
        Assert.Null(kept.Actor(Raider).Assignment);
        Assert.Contains(kept.RecentFacts, f => f.Kind == "RaidOrdered");
        Assert.Contains(kept.RecentFacts, f => f.Kind == "FoodTaken"); // the islanders' own store: no theft
        Assert.DoesNotContain(kept.RecentFacts, f => f.Kind == "FoodStolen");
        Assert.DoesNotContain(kept.RecentFacts, f => f.Kind == "FoodDeposited");
        AssertFoodConserved(recover);

        var lost = renounce.GetWorldView();
        Assert.Equal(10, lost.Actor(Player).Food);
        Assert.Equal(0, lost.Store(ShedStore).Food);
        Assert.Equal(4, lost.Store(VasanCache).Food);
        AssertFoodConserved(renounce);
    }

    [Fact]
    public void Recovering_does_not_need_an_instant_start()
    {
        var session = BlackmarshWatchhouse.CreateSession();
        session.AdvanceTo(At(1, 19, 59).Plus(Duration.FromSeconds(45))); // nearly three minutes on the path before setting off
        Recover(session);
        session.AdvanceTo(Checkpoint);
        Assert.Equal(14, session.GetWorldView().Actor(Player).Food);
        Assert.Equal(0, session.GetWorldView().Store(VasanCache).Food);
        AssertFoodConserved(session);
    }

    [Fact]
    public void A_door_left_open_changes_how_and_when_the_raider_gets_in()
    {
        var locked = BlackmarshWatchhouse.CreateSession();
        var open = BlackmarshWatchhouse.CreateSession();
        Walk(open, new GridPos(5, 6));
        Do(open, new DoorCommand { Actor = open.Player, At = ShedDoor, Open = true }); // the key turns as it opens
        Walk(open, PlayerStart);

        var forced = Follow(locked, Checkpoint);
        var walked = Follow(open, Checkpoint);
        output.WriteLine($"Locked: at the store {Reached(forced)}. Left open: at the store {Reached(walked)}.");

        Assert.DoesNotContain(forced, t => t.Position == ShedDoor);
        Assert.Contains(walked, t => t.Position == ShedDoor);
        Assert.DoesNotContain(walked, t => t.Position == Table);
        Assert.Equal(WindowGlass.Broken, locked.Engine.World.Maps[Island].GlassOf(WindowInside, WindowOutside));
        Assert.Equal(WindowGlass.Intact, open.Engine.World.Maps[Island].GlassOf(WindowInside, WindowOutside));
        Assert.DoesNotContain(open.GetWorldView().RecentFacts, f => f.Kind is "GlassBroken" or "Climbed");
        Assert.NotEqual(Reached(forced), Reached(walked));
        // Either way the rations end at the landing: an open door is not a defence, only another way in.
        Assert.Equal(4, locked.GetWorldView().Store(VasanCache).Food);
        Assert.Equal(4, open.GetWorldView().Store(VasanCache).Food);
    }

    [Fact]
    public void The_table_is_walked_around_and_kept_by_a_save()
    {
        var session = BlackmarshWatchhouse.CreateSession();
        Walk(session, new GridPos(5, 6));
        Do(session, new DoorCommand { Actor = session.Player, At = ShedDoor, Open = true });
        Walk(session, new GridPos(8, 6));
        Do(session, new DoorCommand { Actor = session.Player, At = InnerDoor, Open = true });
        Walk(session, InnerDoor);

        var onTable = session.PreviewMoveToSpot(session.Player, Spot.CenterOf(Table));
        Assert.False(onTable.Valid);
        var around = session.PreviewMoveToSpot(session.Player, Spot.CenterOf(StoreAccess));
        Assert.True(around.Valid, around.Message);
        Assert.DoesNotContain(around.Way, spot => spot.Cell == Table);

        var reloaded = session.SaveAndReload();
        Assert.Contains(Table, reloaded.Engine.World.Maps[Island].Occupied);
        Assert.False(reloaded.PreviewMoveToSpot(reloaded.Player, Spot.CenterOf(Table)).Valid);
        Assert.Equal(around.Way, reloaded.PreviewMoveToSpot(reloaded.Player, Spot.CenterOf(StoreAccess)).Way);
    }

    [Fact]
    public void The_fallen_raider_lies_next_to_the_player_but_is_no_one_to_talk_to()
    {
        var session = BlackmarshWatchhouse.CreateSession();
        Walk(session, new GridPos(13, 10));
        Assert.Contains(session.GetPlayerView().PeopleHere, p => p.Id == Raider);

        session.Engine.World.Actors[Raider].Vitality.TakeDamage(1000);

        var view = session.GetPlayerView();
        Assert.Equal("morto", view.VisibleActors.Single(a => a.Id == Raider).Condition);
        Assert.DoesNotContain(view.PeopleHere, p => p.Id == Raider);
        Assert.DoesNotContain(view.ReportOptions, o => o.Recipient == Raider);
    }

    /// <summary>From the path: unlock and open the shed, open the inner door, take the four rations.</summary>
    private static void Recover(SimulationSession session)
    {
        Walk(session, new GridPos(5, 6));
        Do(session, new DoorCommand { Actor = session.Player, At = ShedDoor, Open = true });
        Walk(session, new GridPos(8, 6));
        Do(session, new DoorCommand { Actor = session.Player, At = InnerDoor, Open = true });
        Walk(session, StoreAccess);
        Do(session, new TakeFoodCommand { Actor = session.Player, Store = ShedStore, Amount = 4 });
        Assert.Equal(14, session.GetPlayerView().Food);
    }

    /// <summary>Everything eaten is a recorded upkeep; everything else is still somewhere on the island.</summary>
    private static void AssertFoodConserved(SimulationSession session)
    {
        var world = session.GetWorldView();
        var eaten = world.RecentFacts.Count(f => f.Kind == "FoodConsumed") * 4;
        Assert.Equal(FoodAtStart, eaten + world.Actors.Sum(a => a.Food) + world.Stores.Sum(s => s.Food));
    }

    private sealed record Step(GameTime At, GridPos? Position, string? Action);

    /// <summary>The raider's squares and actions, second by second, up to <paramref name="until"/>.</summary>
    private static List<Step> Follow(SimulationSession session, GameTime until)
    {
        var trail = new List<Step>();
        while (session.Now < until)
        {
            session.Advance(Duration.FromTicks(Math.Min(GameTime.TicksPerSecond, until.Since(session.Now).Ticks)));
            var raider = session.GetWorldView().Actor(Raider);
            trail.Add(new Step(session.Now, raider.Position, raider.Action?.Kind));
        }
        return trail;
    }

    private static GameTime Reached(List<Step> trail) => trail.First(t => t.Position == StoreAccess).At;

    private static GameTime Deposited(SimulationSession session) =>
        session.GetWorldView().RecentFacts.Single(f => f.Kind == "FoodDeposited").At;

    private static void Walk(SimulationSession session, GridPos to) =>
        Do(session, new MoveToSpotCommand { Actor = session.Player, To = Spot.CenterOf(to) });

    private static void Do(SimulationSession session, Command command)
    {
        var result = session.Execute(command);
        Assert.True(result.Success, result.Message);
        if (result.Action is { } action)
            Assert.Equal(AdvanceOutcome.Completed, session.AdvanceUntilCompleted(action, Duration.FromMinutes(10)).Outcome);
    }
}
