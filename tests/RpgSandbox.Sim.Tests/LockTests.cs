using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>P4a: locked and stuck doors and windows: keys, picking a lock, forcing (SRD 5.2.1).</summary>
public class LockTests
{
    private static readonly AreaId Area = new("a");
    private static readonly GridPos Door = new(4, 2);
    private static readonly GridPos WindowA = new(3, 4), WindowB = new(4, 4);
    private static readonly ActorId Guard = new("guard");
    private const string Key = "farmhouse-key";

    /// <summary>
    /// A yard at noon split by a wall down x = 3|4, with a door between (3,2) and (4,2) (at (4,2)) and a window between
    /// (3,4) and (4,4). The player at (3,2), by the door on its west side; the village guard (an authority) east at (6,2).
    /// </summary>
    private static SimulationSession Yard(LockSpec? door = null, LockSpec? window = null, CharacterSheet? sheet = null,
        ulong seed = 1, bool guard = true)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: sheet ?? Sheets.Paladin(), at: (3, 2));
        if (guard)
            b.AddFaction("village", "Villaggio", authorityId: "guard")
             .AddActor("guard", "Guardia", "yard", factionId: "village", combat: "guard", at: (6, 2));
        for (var y = 0; y < 6; y++)
            b.AddEdge("a", (3, y), (4, y), y switch { 2 => MapEdgeKind.Door, 4 => MapEdgeKind.Window, _ => MapEdgeKind.Wall },
                y == 2 ? (4, 2) : null, lockSpec: y switch { 2 => door, 4 => window, _ => null });
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, 12));
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static Barrier BarrierOf(SimulationSession s, BarrierKey key) => s.Engine.World.Maps[Area].BarrierOf(key)!;

    private static CommandResult Open(SimulationSession s) => s.Execute(new DoorCommand { Actor = s.Player, At = Door, Open = true });

    private static void Work(SimulationSession s, CommandResult started)
    {
        Assert.True(started.Success, started.Message);
        Assert.Equal(started.CompletesAt, s.Now.Plus(Duration.FromMinutes(1))); // a minute's work
        s.AdvanceUntilCompleted(started.Action!.Value, Duration.FromMinutes(5));
    }

    [Fact]
    public void A_locked_door_refuses_without_its_key_the_player_learns_it_and_opens_it_with_the_key()
    {
        var s = Yard(door: new LockSpec(Key, Locked: true, Clue: "una porta robusta di quercia"));
        Assert.Empty(s.GetPlayerView().KnownBarriers); // nothing known before trying

        var refused = Open(s);
        Assert.Equal(RejectionReason.Locked, refused.Rejection);
        Assert.Contains("chiusa a chiave", refused.Message);
        var known = Assert.Single(s.GetPlayerView().KnownBarriers);
        Assert.Equal((Door, true, false, false, "una porta robusta di quercia"), (known.Door!.Value, known.Locked, known.Stuck, known.Broken, known.Clue));
        Assert.True(known.HasLock);
        Assert.DoesNotContain(s.GetWorldView().RecentFacts, f => f.Kind == "FoundLocked" && f.Description.Contains("(")); // no coordinates for the player
        Assert.DoesNotMatch("[0-9]", known.Clue);
        Assert.False(s.Engine.World.Maps[Area].Doors[Door]);

        Of(s, s.Player).Give(Key, 1);
        Assert.True(Open(s).Success);
        Assert.True(s.Engine.World.Maps[Area].Doors[Door]);
        Assert.False(BarrierOf(s, BarrierKey.Door(Door)).Locked);
    }

    [Fact]
    public void With_its_key_a_closed_door_locks_and_unlocks_and_an_open_or_keyless_one_does_not()
    {
        var s = Yard(door: new LockSpec(Key));
        var lockIt = new LockCommand { Actor = s.Player, Door = Door, Locked = true };
        Assert.Equal(RejectionReason.NoKey, s.Execute(lockIt).Rejection);
        Of(s, s.Player).Give(Key, 1);
        Assert.True(s.Execute(lockIt).Success);
        Assert.Equal(RejectionReason.AlreadyDone, s.Execute(lockIt).Rejection);
        Assert.True(s.Execute(lockIt with { Locked = false }).Success);
        Assert.True(Open(s).Success);
        Assert.Equal(RejectionReason.NotClosed, s.Execute(lockIt).Rejection);

        var plain = Yard();
        Of(plain, plain.Player).Give(Key, 1);
        Assert.Equal(RejectionReason.NoLock, plain.Execute(new LockCommand { Actor = plain.Player, Door = Door, Locked = true }).Rejection);
    }

    [Fact]
    public void Picking_a_lock_needs_thieves_tools_takes_a_minute_and_a_dexterity_check_against_15()
    {
        var s = Yard(door: new LockSpec(Key, Locked: true));
        var pick = new PickLockCommand { Actor = s.Player, Door = Door };
        Assert.Equal(RejectionReason.NoTools, s.Execute(pick).Rejection);

        Of(s, s.Player).Give("thieves-tools", 1);
        Work(s, s.Execute(pick));
        var fact = s.GetWorldView().RecentFacts.Last(f => f.Kind is "LockPicked" or "LockPickFailed");
        Assert.Contains("Destrezza d20", fact.Description);
        Assert.Contains("+0 =", fact.Description); // the Paladin: Dex 10, no proficiency with the tools
        Assert.Equal(fact.Kind == "LockPicked", !BarrierOf(s, BarrierKey.Door(Door)).Locked);
        Assert.False(s.Engine.World.Maps[Area].Doors[Door]); // picked, it stays closed
    }

    [Fact]
    public void Proficiency_with_the_tools_adds_the_bonus_and_with_sleight_of_hand_too_gives_advantage()
    {
        var rogue = Sheets.Paladin() with
        {
            Dexterity = 16, ToolProficiencies = new[] { "thieves-tools" }, SkillProficiencies = new[] { Skill.SleightOfHand },
        };
        var s = Yard(door: new LockSpec(Key, Locked: true), sheet: rogue);
        Of(s, s.Player).Give("thieves-tools", 1);
        Work(s, s.Execute(new PickLockCommand { Actor = s.Player, Door = Door }));

        var fact = s.GetWorldView().RecentFacts.Last(f => f.Kind is "LockPicked" or "LockPickFailed");
        Assert.Contains("+5 =", fact.Description); // Dex +3, Proficiency Bonus +2
        Assert.Contains("vantaggio", fact.Description);
    }

    [Fact]
    public void Forcing_a_locked_door_breaks_it_open_for_good_and_the_blows_are_heard()
    {
        var s = Yard(door: new LockSpec(Key, Locked: true, ForceDc: 1));
        Work(s, s.Execute(new ForceCommand { Actor = s.Player, Door = Door }));

        var barrier = BarrierOf(s, BarrierKey.Door(Door));
        Assert.True(barrier.Broken);
        Assert.False(barrier.Locked);
        Assert.True(s.Engine.World.Maps[Area].Doors[Door]);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "Forced" && f.Description.Contains("Forza (Atletica)"));
        Assert.True(Assert.Single(s.GetPlayerView().KnownBarriers).Broken);

        Of(s, s.Player).Give(Key, 1);
        Assert.True(s.Execute(new DoorCommand { Actor = s.Player, At = Door, Open = false }).Success);
        Assert.Equal(RejectionReason.Broken, s.Execute(new LockCommand { Actor = s.Player, Door = Door, Locked = true }).Rejection);

        // The guard beyond the wall did not see it, but heard the blows, and goes to look.
        var heard = Assert.Single(Of(s, Guard).Knowledge, o => o.Kind == DeedKind.BreakIn);
        Assert.Equal(PerceptionMode.Heard, heard.Perceived);
        Assert.Null(heard.Culprit);
        var looked = false;
        looked = Of(s, Guard).LastDecision?.Rule == "Va a vedere";
        for (var i = 0; i < 60 && !looked; i++)
        {
            s.Advance(Duration.FromSeconds(1));
            looked = Of(s, Guard).LastDecision?.Rule == "Va a vedere";
        }
        Assert.True(looked, "the guard never went to look");
    }

    [Fact]
    public void Someone_forcing_a_door_out_of_sight_is_heard_by_the_player()
    {
        // The guard forces the stuck door from the far side; the player, behind the closed door, only hears it.
        var s = Yard(door: new LockSpec(Stuck: true, ForceDc: 1));
        Of(s, Guard).PlaceAt(Spot.CenterOf(new GridPos(5, 2)));
        var force = s.Execute(new ForceCommand { Actor = Guard, Door = Door });
        Assert.True(force.Success, force.Message);
        Assert.False(s.Engine.SeesActor(Of(s, s.Player), Of(s, Guard)));

        Assert.Contains(s.GetPlayerView().RecentEvents, f => f.Kind == "HeardForcing" && f.Description.StartsWith("Senti colpi"));
    }

    [Fact]
    public void A_stuck_window_refuses_and_once_forced_opens_and_shuts_as_any_other()
    {
        var s = Yard(window: new LockSpec(Stuck: true, ForceDc: 1, Clue: "il telaio è gonfio di umidità"));
        Of(s, s.Player).PlaceAt(Spot.CenterOf(WindowA));
        var open = new WindowCommand { Actor = s.Player, A = WindowA, B = WindowB, Open = true };
        Assert.Equal(RejectionReason.Stuck, s.Execute(open).Rejection);
        Assert.True(Assert.Single(s.GetPlayerView().KnownBarriers).Stuck);
        Assert.False(Assert.Single(s.GetPlayerView().KnownBarriers).HasLock); // it only sticks: nothing to lock

        Work(s, s.Execute(new ForceCommand { Actor = s.Player, A = WindowA, B = WindowB }));
        Assert.True(s.Engine.World.Maps[Area].IsOpenWindow(WindowA, WindowB));
        Assert.False(BarrierOf(s, BarrierKey.Window(WindowA, WindowB)).Stuck);
        Assert.True(s.Execute(open with { Open = false }).Success);
        Assert.True(s.Execute(open).Success);
    }

    [Fact]
    public void Nothing_to_pick_or_force_on_a_door_that_opens_by_hand()
    {
        var s = Yard(door: new LockSpec(Key));
        Of(s, s.Player).Give("thieves-tools", 1);
        Assert.Equal(RejectionReason.NotHeld, s.Execute(new PickLockCommand { Actor = s.Player, Door = Door }).Rejection);
        Assert.Equal(RejectionReason.NotHeld, s.Execute(new ForceCommand { Actor = s.Player, Door = Door }).Rejection);
        var far = Yard(door: new LockSpec(Key, Locked: true));
        Of(far, far.Player).PlaceAt(new Spot(30, 30));
        Assert.Equal(RejectionReason.OutOfReach, far.Execute(new ForceCommand { Actor = far.Player, Door = Door }).Rejection);
    }

    [Fact]
    public void A_walker_without_the_key_goes_round_a_locked_door_and_one_with_it_passes()
    {
        var s = Yard(door: new LockSpec(Key, Locked: true), guard: false);
        Assert.Equal(RejectionReason.Unreachable, s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(6, 2) }).Rejection);
        Assert.Equal(RejectionReason.Unreachable, s.Execute(new MoveToSpotCommand { Actor = s.Player, To = new Spot(390, 150) }).Rejection);

        Of(s, s.Player).Give(Key, 1);
        var walk = s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(6, 2) });
        Assert.True(walk.Success, walk.Message);
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(1));
        Assert.Equal(new GridPos(6, 2), s.GetPlayerView().Position);
        Assert.False(BarrierOf(s, BarrierKey.Door(Door)).Locked); // the player locks it again only if it wants to
    }

    [Fact]
    public void In_the_living_village_the_farmer_locks_his_door_behind_him_and_the_guard_cannot_get_in()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var village = MappedVillageScenario.Ids.Village;
        var houseDoor = MappedVillageScenario.Ids.HouseDoor;
        Assert.Equal(RejectionReason.Unreachable,
            s.Execute(new MoveCommand { Actor = MappedVillageScenario.Ids.Guard, To = MappedVillageScenario.Ids.FarmerHome }).Rejection);

        s.AdvanceTo(At(1, 13, 10)); // he went out to work
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "DoorLocked" && f.Description.Contains("Contadino richiude a chiave"));
        Assert.True(s.Engine.World.Maps[village].BarrierOf(BarrierKey.Door(houseDoor))!.Locked);
        Assert.False(s.Engine.World.Maps[village].Doors[houseDoor]);
        Assert.Equal(1, s.Engine.World.Maps[village].OnGround(new GridPos(2, 3), new ItemId("thieves-tools"))); // in the inn, for trying
    }

    [Fact]
    public void Locks_what_the_player_knows_and_a_minute_of_work_half_done_survive_save_and_load()
    {
        var s = Yard(door: new LockSpec(Key, Locked: true, ForceDc: 30));
        Open(s); // learns it is locked
        var started = s.Execute(new ForceCommand { Actor = s.Player, Door = Door });
        Assert.True(started.Success, started.Message);
        s.Advance(Duration.FromSeconds(20));

        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Single(loaded.GetPlayerView().KnownBarriers);
        loaded.AdvanceUntilCompleted(started.Action!.Value, Duration.FromMinutes(5));
        Assert.Contains(loaded.GetWorldView().RecentFacts, f => f.Kind is "Forced" or "ForceFailed");

        // An older save has no locks: the door opens by hand.
        var data = JsonNode.Parse(Yard(door: new LockSpec(Key, Locked: true)).SaveToString())!;
        AsSchema(data, 28);
        var old = LoadFromString(data.ToJsonString());
        Assert.True(old.Success, old.Error);
        Assert.True(old.Session!.Execute(new DoorCommand { Actor = old.Session.Player, At = Door, Open = true }).Success);
    }
}
