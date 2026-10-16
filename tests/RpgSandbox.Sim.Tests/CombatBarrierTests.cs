using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>P4c: doors, windows and locks in a fight: one free interaction a turn, the Utilize action, climbing as movement.</summary>
public class CombatBarrierTests
{
    private static readonly AreaId Area = new("a");
    private static readonly GridPos Door = new(4, 2);
    private static readonly GridPos WindowA = new(3, 4), WindowB = new(4, 4);
    private static readonly ActorId Foe = new("foe");

    /// <summary>
    /// A yard at noon split by a wall down x = 3|4 with a door between (3,2) and (4,2) and a window between (3,4) and (4,4).
    /// The player at (3,2), a guard at (2,2) next to it: the player strikes first; the first seed where it is then its turn.
    /// </summary>
    private static SimulationSession Fight(LockSpec? door = null, bool windowOpen = false, WindowGlass glass = WindowGlass.Intact,
        bool secondDoor = false)
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var b = new ScenarioBuilder().WithSeed(seed)
                .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
                .AddMap("a", Enumerable.Repeat("YYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
                .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: (3, 2))
                .AddActor("foe", "Guardia", "yard", combat: "guard", at: (2, 2));
            for (var y = 0; y < 6; y++)
                b.AddEdge("a", (3, y), (4, y), y switch { 2 => MapEdgeKind.Door, 4 => MapEdgeKind.Window, _ => MapEdgeKind.Wall },
                    y == 2 ? (4, 2) : null, open: y == 4 && windowOpen, lockSpec: y == 2 ? door : null,
                    glass: y == 4 ? glass : WindowGlass.Intact);
            if (secondDoor) // a second wall down x = 5|6 with its own door at (6,2)
                for (var y = 0; y < 6; y++)
                    b.AddEdge("a", (5, y), (6, y), y == 2 ? MapEdgeKind.Door : MapEdgeKind.Wall, y == 2 ? (6, 2) : null);
            var s = SimulationSession.Create(b.Build());
            s.AdvanceTo(At(1, 12));
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Spada lunga" });
            // The opening blow spent this turn's action: on to the player's next turn, everything fresh.
            if (s.GetPlayerView().Combat is { IsYourTurn: true })
                s.Execute(new EndTurnCommand { Actor = s.Player });
            for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
                s.Advance(Duration.FromSeconds(1));
            if (s.GetPlayerView().Combat is { IsYourTurn: true, You: { ActionAvailable: true, InteractionAvailable: true } } c
                && c.You.MovementLeftInches == 30 * 12 && s.Engine.World.Actors[Foe].Conscious && s.GetPlayerView().Unconscious == false)
                return s;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static CombatSelfView You(SimulationSession s) => s.GetPlayerView().Combat!.You;

    private static GridMap Map(SimulationSession s) => s.Engine.World.Maps[Area];

    [Fact]
    public void A_door_is_the_free_interaction_of_the_turn_a_second_takes_the_action_and_then_there_is_none_left()
    {
        var s = Fight();
        Assert.True(s.Execute(new DoorCommand { Actor = s.Player, At = Door, Open = true }).Success);
        Assert.False(You(s).InteractionAvailable);
        Assert.True(You(s).ActionAvailable);

        var shut = s.Execute(new DoorCommand { Actor = s.Player, At = Door, Open = false });
        Assert.True(shut.Success, shut.Message);
        Assert.Contains("usi l'azione", shut.Message);
        Assert.False(You(s).ActionAvailable);

        Assert.Equal(RejectionReason.NoActionLeft, s.Execute(new DoorCommand { Actor = s.Player, At = Door, Open = true }).Rejection);
        Assert.Equal(s.SaveToString(), s.SaveAndReload().SaveToString());
        Assert.False(s.SaveAndReload().GetPlayerView().Combat!.You.InteractionAvailable);
    }

    [Fact]
    public void Forcing_in_a_fight_takes_the_action_and_is_settled_at_once()
    {
        var s = Fight(door: new LockSpec("farmhouse-key", Locked: true, ForceDc: 1));
        var forced = s.Execute(new ForceCommand { Actor = s.Player, Door = Door });

        Assert.True(forced.Success, forced.Message);
        Assert.Contains("forza", forced.Message);
        Assert.Null(Of(s, s.Player).CurrentAction); // no minute's work
        Assert.False(You(s).ActionAvailable);
        Assert.True(Map(s).Doors[Door]);
        Assert.True(Map(s).BarrierOf(BarrierKey.Door(Door))!.Broken);
        Assert.Equal(RejectionReason.NoActionLeft, s.Execute(new ForceCommand { Actor = s.Player, Door = Door }).Rejection);
    }

    [Fact]
    public void Picking_a_lock_in_a_fight_is_the_utilize_action_one_dexterity_check_at_once()
    {
        var s = Fight(door: new LockSpec("farmhouse-key", Locked: true));
        Of(s, s.Player).Give("thieves-tools", 1);
        var picked = s.Execute(new PickLockCommand { Actor = s.Player, Door = Door });

        Assert.True(picked.Success, picked.Message);
        Assert.Contains("Destrezza d20", picked.Message);
        Assert.Null(Of(s, s.Player).CurrentAction);
        Assert.False(You(s).ActionAvailable);
    }

    [Fact]
    public void Breaking_the_glass_in_a_fight_takes_the_action_and_lets_shots_through_at_once()
    {
        var s = Fight();
        Of(s, s.Player).PlaceAt(Spot.CenterOf(WindowA));
        var broke = s.Execute(new BreakGlassCommand { Actor = s.Player, A = WindowA, B = WindowB });

        Assert.True(broke.Success, broke.Message);
        Assert.Equal(WindowGlass.Broken, Map(s).GlassOf(WindowA, WindowB));
        Assert.True(Map(s).LetsThrough(WindowA, WindowB));
        Assert.False(You(s).ActionAvailable);
        Assert.Equal(RejectionReason.InCombat, s.Execute(new ClearGlassCommand { Actor = s.Player, A = WindowA, B = WindowB }).Rejection);
    }

    [Fact]
    public void Climbing_over_a_window_in_a_fight_costs_its_walk_and_five_feet_more_and_provokes_on_leaving_reach()
    {
        var s = Fight(windowOpen: true, glass: WindowGlass.Broken);
        Of(s, s.Player).PlaceAt(Spot.CenterOf(WindowA));
        Of(s, Foe).PlaceAt(Spot.CenterOf(new GridPos(2, 4))); // next to the player: climbing away leaves its reach
        var hp = Of(s, s.Player).Vitality.Current;

        var climbed = s.Execute(new ClimbCommand { Actor = s.Player, A = WindowA, B = WindowB });

        Assert.True(climbed.Success, climbed.Message);
        if (!Of(s, s.Player).Conscious)
            return; // the opportunity attack or the shards dropped it: nothing more to check
        Assert.Equal(WindowB, s.GetPlayerView().Position);
        Assert.Equal(30 * 12 - 60 - 60, You(s).MovementLeftInches); // 5 feet of walking, 5 more for the window
        Assert.True(You(s).ActionAvailable); // movement, not the action
        Assert.Contains(s.Engine.World.Encounters.Values.Single().Log, l => l.Kind == CombatLogKind.OpportunityAttack && l.Actor == Foe);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "CutByGlass");
        Assert.True(Of(s, s.Player).Vitality.Current < hp);
    }

    [Fact]
    public void Without_enough_movement_left_one_cannot_climb()
    {
        var s = Fight(windowOpen: true);
        Of(s, s.Player).PlaceAt(Spot.CenterOf(WindowA));
        s.Engine.World.Encounters.Values.Single().Order.Single(c => c.Actor == s.Player).MovedInches = 30 * 12 - 100;
        var refused = s.Execute(new ClimbCommand { Actor = s.Player, A = WindowA, B = WindowB });
        Assert.Equal(RejectionReason.TooFar, refused.Rejection);
        Assert.Contains("servono 10 piedi", refused.Message);
    }

    [Fact]
    public void In_a_fight_a_move_opens_a_closed_door_on_the_way_as_the_free_interaction_and_a_second_one_is_a_wall()
    {
        var s = Fight();
        var to = Spot.CenterOf(new GridPos(5, 2));
        Assert.Equal(Door, s.PreviewMoveToSpot(s.Player, to).OpensDoor);
        var moved = s.Execute(new MoveToSpotCommand { Actor = s.Player, To = to });
        Assert.True(moved.Success, moved.Message);
        if (!Of(s, s.Player).Conscious)
            return; // dropped by the opportunity attack on the way
        Assert.Equal(to, Of(s, s.Player).Spot);
        Assert.True(Map(s).Doors[Door]);
        Assert.False(You(s).InteractionAvailable);
        Assert.Contains(s.GetPlayerView().RecentEvents, f => f.Kind == "DoorOpened");

        // Shut behind it (that takes the action now): back through it, the interaction is spent, the door is a wall.
        Assert.True(s.Execute(new DoorCommand { Actor = s.Player, At = Door, Open = false }).Success);
        Assert.Null(s.PreviewMoveToSpot(s.Player, Spot.CenterOf(new GridPos(2, 3))).OpensDoor);
        Assert.Equal(RejectionReason.Unreachable, s.Execute(new MoveToSpotCommand { Actor = s.Player, To = Spot.CenterOf(new GridPos(2, 3)) }).Rejection);
    }

    [Fact]
    public void A_locked_door_stays_a_wall_in_a_fight_without_its_key_and_opens_on_the_way_with_it()
    {
        var s = Fight(door: new LockSpec("farmhouse-key", Locked: true));
        var to = Spot.CenterOf(new GridPos(5, 2));
        Assert.Equal(RejectionReason.Unreachable, s.Execute(new MoveToSpotCommand { Actor = s.Player, To = to }).Rejection);
        Of(s, s.Player).Give("farmhouse-key", 1);
        Assert.True(s.Execute(new MoveToSpotCommand { Actor = s.Player, To = to }).Success);
        if (Of(s, s.Player).Conscious)
            Assert.False(Map(s).BarrierOf(BarrierKey.Door(Door))!.Locked);
    }

    [Fact]
    public void A_foe_seen_through_a_closed_window_is_reached_through_the_door_it_opens_on_the_way()
    {
        // The guard at (2,4) sees the player at (5,4) through the closed window, but no blow passes it: the way round is
        // through the closed door, which it opens walking, then it strikes.
        var s = Fight();
        Of(s, s.Player).PlaceAt(Spot.CenterOf(new GridPos(5, 4)));
        Of(s, Foe).PlaceAt(Spot.CenterOf(new GridPos(2, 4)));
        Assert.True(s.Engine.SeesActor(Of(s, Foe), Of(s, s.Player)));
        var before = s.Engine.World.Encounters.Values.Single().Log.Count;

        s.Execute(new EndTurnCommand { Actor = s.Player });
        for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
            s.Advance(Duration.FromSeconds(1));

        Assert.True(Map(s).Doors[Door]);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "DoorOpened" && f.Description.StartsWith("Guardia"));
        Assert.Contains(s.Engine.World.Encounters.Values.SingleOrDefault()?.Log.Skip(before) ?? s.Engine.World.LastFight!.Log,
            l => l.Kind == CombatLogKind.Attack && l.Actor == Foe);
    }

    [Fact]
    public void A_move_that_would_open_two_doors_in_one_turn_is_refused_before_it_starts_preview_included()
    {
        var s = Fight(secondDoor: true);
        var beyond = Spot.CenterOf(new GridPos(7, 2));
        var preview = s.PreviewMoveToSpot(s.Player, beyond);
        Assert.False(preview.Valid);
        Assert.Equal(RejectionReason.Unreachable, preview.Reason);
        var refused = s.Execute(new MoveToSpotCommand { Actor = s.Player, To = beyond });
        Assert.Equal(RejectionReason.Unreachable, refused.Rejection);
        Assert.Contains("più di una porta", refused.Message);
        Assert.True(You(s).InteractionAvailable); // nothing done
    }

    [Fact]
    public void Unlocking_a_door_walking_through_it_with_the_key_updates_what_the_player_knows()
    {
        var s = Fight(door: new LockSpec("farmhouse-key", Locked: true));
        Assert.Equal(RejectionReason.Locked, s.Execute(new DoorCommand { Actor = s.Player, At = Door, Open = true }).Rejection);
        Assert.True(Assert.Single(s.GetPlayerView().KnownBarriers).Locked);
        Of(s, s.Player).Give("farmhouse-key", 1);
        Assert.True(s.Execute(new MoveToSpotCommand { Actor = s.Player, To = Spot.CenterOf(new GridPos(5, 2)) }).Success);
        if (Of(s, s.Player).Conscious)
            Assert.False(Assert.Single(s.GetPlayerView().KnownBarriers).Locked);
    }

    [Fact]
    public void A_move_on_squares_opens_a_closed_door_on_the_way_as_the_free_interaction_and_a_second_one_is_a_wall()
    {
        var s = Fight();
        var to = new GridPos(5, 2);
        Assert.Contains(s.GetPlayerView().Combat!.Reachable, r => r.At == to);
        var preview = s.PreviewMove(s.Player, to);
        Assert.True(preview.Valid, preview.Message);
        Assert.Equal(Door, preview.OpensDoor);
        var moved = s.Execute(new MoveCommand { Actor = s.Player, To = to });
        Assert.True(moved.Success, moved.Message);
        if (!Of(s, s.Player).Conscious)
            return; // dropped by the opportunity attack on the way
        Assert.Equal(to, s.GetPlayerView().Position);
        Assert.True(Map(s).Doors[Door]);
        Assert.False(You(s).InteractionAvailable);
        Assert.Contains(s.GetPlayerView().RecentEvents, f => f.Kind == "DoorOpened");

        // Shut behind it (that takes the action now): the interaction is spent, the door is a wall again.
        Assert.True(s.Execute(new DoorCommand { Actor = s.Player, At = Door, Open = false }).Success);
        var back = new GridPos(2, 3);
        Assert.DoesNotContain(s.GetPlayerView().Combat!.Reachable, r => r.At == back);
        Assert.Equal(RejectionReason.Unreachable, s.PreviewMove(s.Player, back).Reason);
        Assert.Equal(RejectionReason.Unreachable, s.Execute(new MoveCommand { Actor = s.Player, To = back }).Rejection);
    }

    [Fact]
    public void On_squares_a_locked_door_is_a_wall_without_its_key_and_two_doors_in_a_turn_are_refused()
    {
        var s = Fight(door: new LockSpec("farmhouse-key", Locked: true));
        var to = new GridPos(5, 2);
        Assert.DoesNotContain(s.GetPlayerView().Combat!.Reachable, r => r.At == to);
        Assert.Equal(RejectionReason.Unreachable, s.Execute(new MoveCommand { Actor = s.Player, To = to }).Rejection);
        Of(s, s.Player).Give("farmhouse-key", 1);
        Assert.True(s.Execute(new MoveCommand { Actor = s.Player, To = to }).Success);
        if (Of(s, s.Player).Conscious)
            Assert.False(Map(s).BarrierOf(BarrierKey.Door(Door))!.Locked);

        var two = Fight(secondDoor: true);
        var beyond = new GridPos(7, 2);
        Assert.DoesNotContain(two.GetPlayerView().Combat!.Reachable, r => r.At == beyond);
        var preview = two.PreviewMove(two.Player, beyond);
        Assert.Equal(RejectionReason.Unreachable, preview.Reason);
        Assert.Contains("più di una porta", preview.Message);
        var refused = two.Execute(new MoveCommand { Actor = two.Player, To = beyond });
        Assert.Equal(RejectionReason.Unreachable, refused.Rejection);
        Assert.Contains("più di una porta", refused.Message);
        Assert.True(two.GetPlayerView().Combat!.You.InteractionAvailable); // nothing done
    }
}
