using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>R5c: falls, a pit, a poisoned needle, Basic Poison (SRD 5.2.1).</summary>
public class TrapTests
{
    private static readonly AreaId Area = new("a");
    private static readonly GridPos Door = new(4, 2), Pit = new(1, 2);
    private static readonly ActorId Foe = new("foe");
    private const string Key = "farmhouse-key";

    /// <summary>
    /// A yard at noon split by a wall down x = 3|4 with a locked door at (4,2) (a needle in its lock if
    /// <paramref name="needle"/>), and a hidden pit at (1,2) if <paramref name="pit"/>. The player at (3,2).
    /// </summary>
    private static SimulationSession Yard(bool pit = true, bool needle = false, ulong seed = 1, bool foe = false)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, at: (3, 2));
        if (foe)
            b.AddActor("foe", "Guardia", "yard", combat: "guard", at: (2, 1));
        for (var y = 0; y < 6; y++)
            b.AddEdge("a", (3, y), (4, y), y == 2 ? MapEdgeKind.Door : MapEdgeKind.Wall, y == 2 ? (4, 2) : null,
                lockSpec: y == 2 ? new LockSpec(Key, Locked: true) : null);
        if (pit)
            b.AddTrap("a", (Pit.X, Pit.Y), "pit");
        if (needle)
            b.AddTrap("a", (Door.X, Door.Y), "needle");
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, 12));
        return s;
    }

    private static Actor Me(SimulationSession s) => s.Engine.World.Actors[s.Player];

    private static void Work(SimulationSession s, Command command)
    {
        var started = s.Execute(command);
        Assert.True(started.Success, started.Message);
        Assert.Equal(s.Now.Plus(Duration.FromMinutes(1)), started.CompletesAt);
        s.AdvanceUntilCompleted(started.Action!.Value, Duration.FromMinutes(5));
    }

    private static void Walk(SimulationSession s, GridPos to)
    {
        var walk = s.Execute(new MoveCommand { Actor = s.Player, To = to });
        Assert.True(walk.Success, walk.Message);
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(1));
    }

    /// <summary>Repeats a minute's work until a fact of one of these kinds comes, at most 40 times.</summary>
    private static void Until(SimulationSession s, Command command, params string[] kinds)
    {
        for (var i = 0; i < 40; i++)
        {
            Work(s, command);
            if (kinds.Contains(s.GetWorldView().RecentFacts.Last().Kind))
                return;
        }
        Assert.Fail($"no {string.Join("/", kinds)} in 40 tries");
    }

    [Fact]
    public void A_hidden_pit_opens_under_the_walker_who_falls_ten_feet_and_must_climb_out()
    {
        var s = Yard();
        var full = Me(s).Vitality.Current;
        var walk = s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(0, 2) });
        s.Advance(Duration.FromSeconds(30));

        Assert.Null(Me(s).CurrentAction); // the walk ended in the pit
        Assert.Equal(Pit, Me(s).Position);
        var view = s.GetPlayerView();
        Assert.True(view.InPit);
        Assert.InRange(full - Me(s).Vitality.Current, 1, 6); // 1d6 for 10 feet
        Assert.Contains(view.Conditions, c => c.Kind == ConditionKind.Prone);
        Assert.Equal(new TrapView { At = Pit, Kind = TrapKind.Pit, Sprung = true, Disarmed = false }, Assert.Single(view.KnownTraps));
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "PitTrap");
        Assert.Equal(RejectionReason.InPit, s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(0, 2) }).Rejection);
        Assert.Equal(RejectionReason.InPit, s.Execute(new SearchCommand { Actor = s.Player }).Rejection);

        Until(s, new ClimbOutCommand { Actor = s.Player }, "ClimbedOut");
        Assert.False(s.GetPlayerView().InPit);
        Assert.True(Me(s).Position!.Value.IsAdjacentOrSame(Pit) && Me(s).Position != Pit);
        Assert.Equal(RejectionReason.NotInPit, s.Execute(new ClimbOutCommand { Actor = s.Player }).Rejection);

        if (Me(s).Position != new GridPos(2, 2))
            Walk(s, new GridPos(2, 2));
        Walk(s, new GridPos(0, 2)); // round the open pit, not through it
        Assert.Equal(new GridPos(0, 2), Me(s).Position);
        Assert.False(Me(s).InPit);
        Assert.Single(s.GetWorldView().RecentFacts, f => f.Kind == "PitTrap");
    }

    [Fact]
    public void A_search_finds_the_pit_and_an_iron_spike_under_its_lid_makes_it_safe_to_cross()
    {
        var s = Yard();
        Until(s, new SearchCommand { Actor = s.Player }, "TrapFound");
        Assert.Equal(new TrapView { At = Pit, Kind = TrapKind.Pit, Sprung = false, Disarmed = false }, Assert.Single(s.GetPlayerView().KnownTraps));

        Assert.Equal(RejectionReason.TooFar, s.Execute(new DisarmCommand { Actor = s.Player, At = Pit }).Rejection);
        Walk(s, new GridPos(2, 2));
        Assert.Equal(RejectionReason.NoTools, s.Execute(new DisarmCommand { Actor = s.Player, At = Pit }).Rejection);
        Me(s).Give("iron-spike", 2);
        Work(s, new DisarmCommand { Actor = s.Player, At = Pit });
        Assert.Equal(1, Me(s).Carried("iron-spike"));
        Assert.True(Assert.Single(s.GetPlayerView().KnownTraps).Disarmed);

        Walk(s, Pit);
        Assert.False(Me(s).InPit);
        Assert.Equal(RejectionReason.NoTrap, s.Execute(new DisarmCommand { Actor = s.Player, At = Pit }).Rejection);
    }

    [Fact]
    public void A_search_with_nothing_there_finds_nothing_and_a_trap_not_found_cannot_be_disarmed()
    {
        var s = Yard();
        Assert.Equal(RejectionReason.NoTrap, s.Execute(new DisarmCommand { Actor = s.Player, At = Pit }).Rejection);
        var clear = Yard(pit: false);
        Work(clear, new SearchCommand { Actor = clear.Player });
        Assert.Equal("TrapNotFound", clear.GetWorldView().RecentFacts.Last().Kind);
    }

    [Fact]
    public void Picking_a_lock_with_a_needle_stings_once_constitution_save_against_poison()
    {
        var s = Yard(pit: false, needle: true);
        Me(s).Give("thieves-tools", 1);
        var full = Me(s).Vitality.Current;
        Work(s, new PickLockCommand { Actor = s.Player, Door = Door });

        var facts = s.GetWorldView().RecentFacts;
        Assert.Contains(facts, f => f.Kind == "NeedleTrap");
        var save = facts.Single(f => f.Kind == "SavingThrow" && f.Description.Contains("ago"));
        var lost = full - Me(s).Vitality.Current;
        if (save.Description.Contains("fallito"))
        {
            Assert.InRange(lost, 1, 10);
            Assert.Contains(Me(s).Conditions, c => c.Kind == ConditionKind.Poisoned && c.Until == s.Now.Plus(Duration.FromHours(1)));
        }
        else
            Assert.InRange(lost, 0, 5);
        Assert.True(Assert.Single(s.GetPlayerView().KnownTraps).Sprung);

        Me(s).Vitality.Heal(100);
        if (s.Engine.World.Maps[Area].BarrierOf(BarrierKey.Door(Door)) is { Locked: true })
            Work(s, new PickLockCommand { Actor = s.Player, Door = Door });
        Assert.Single(s.GetWorldView().RecentFacts, f => f.Kind == "NeedleTrap");
    }

    [Fact]
    public void A_needle_found_is_disarmed_with_sleight_of_hand_and_a_failure_springs_it()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var s = Yard(pit: false, needle: true, seed: seed);
            Until(s, new SearchCommand { Actor = s.Player }, "TrapFound");
            Work(s, new DisarmCommand { Actor = s.Player, At = Door });
            var trap = Assert.Single(s.GetPlayerView().KnownTraps);
            var stung = s.GetWorldView().RecentFacts.Any(f => f.Kind == "NeedleTrap");
            Assert.True(trap.Disarmed != stung);
            Assert.Equal(stung, trap.Sprung);
            if (!trap.Disarmed)
                continue;
            Me(s).Give("thieves-tools", 1);
            Work(s, new PickLockCommand { Actor = s.Player, Door = Door });
            Assert.DoesNotContain(s.GetWorldView().RecentFacts, f => f.Kind == "NeedleTrap");
            return;
        }
        Assert.Fail("never disarmed");
    }

    [Fact]
    public void Basic_poison_coats_the_weapon_for_a_minute_and_is_spent_by_a_slashing_hit()
    {
        var s = Yard(pit: false);
        Assert.Equal(RejectionReason.NoTools, s.Execute(new ApplyPoisonCommand { Actor = s.Player }).Rejection);
        Me(s).Give("basic-poison", 1);
        Assert.True(s.Execute(new ApplyPoisonCommand { Actor = s.Player }).Success);
        Assert.Equal(0, Me(s).Carried("basic-poison"));
        Assert.Equal(s.Now.Plus(Duration.FromMinutes(1)), s.GetPlayerView().WeaponPoisonedUntil);
        s.Advance(Duration.FromMinutes(2));
        Assert.Null(s.GetPlayerView().WeaponPoisonedUntil);

        for (ulong seed = 1; seed <= 60; seed++)
        {
            var f = Yard(pit: false, seed: seed, foe: true);
            f.Engine.World.Actors[Foe].Vitality.RaiseMaximum(100);
            f.Engine.World.Actors[Foe].Vitality.Heal(100);
            Me(f).Give("basic-poison", 1);
            f.Execute(new ApplyPoisonCommand { Actor = f.Player });
            f.Execute(new AttackCommand { Actor = f.Player, Target = Foe, Attack = "Spada lunga" });
            var blow = f.GetPlayerView().Combat?.Log.LastOrDefault(l => l.Kind == CombatLogKind.Attack && l.Actor == f.Player);
            if (blow is not { Hit: true })
            {
                Assert.NotNull(Me(f).PoisonedWeapon); // a miss keeps it
                continue;
            }
            Assert.Null(Me(f).PoisonedWeapon);
            return;
        }
        Assert.Fail("never hit");
    }

    [Fact]
    public void Traps_a_pit_and_poison_survive_a_save()
    {
        var s = Yard(needle: true);
        s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(0, 2) });
        s.Advance(Duration.FromSeconds(30));
        Me(s).Give("basic-poison", 1);
        s.Execute(new ApplyPoisonCommand { Actor = s.Player });
        Assert.True(s.Execute(new ClimbOutCommand { Actor = s.Player }).Success);
        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.True(loaded.GetPlayerView().InPit);
        Assert.NotNull(loaded.GetPlayerView().WeaponPoisonedUntil);
    }

    [Fact]
    public void In_a_fight_a_walk_onto_the_pit_stops_there_with_a_fall()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var s = Yard(seed: seed, foe: true);
            s.Engine.World.Actors[Foe].Vitality.RaiseMaximum(100);
            s.Engine.World.Actors[Foe].Vitality.Heal(100);
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Spada lunga" });
            if (s.GetPlayerView().Combat is not { IsYourTurn: true } || !Me(s).Conscious)
                continue;
            var move = s.Execute(new MoveToSpotCommand { Actor = s.Player, To = Spot.CenterOf(new GridPos(0, 2)) });
            Assert.True(move.Success, move.Message);
            Assert.Equal(Pit, Me(s).Position);
            Assert.True(Me(s).InPit);
            Assert.Equal(RejectionReason.InPit, s.Execute(new MoveToSpotCommand { Actor = s.Player, To = Spot.CenterOf(new GridPos(0, 2)) }).Rejection);
            // Climbing out takes the action, which the opening blow already took.
            Assert.Equal(RejectionReason.NoActionLeft, s.Execute(new ClimbOutCommand { Actor = s.Player }).Rejection);
            return;
        }
        Assert.Fail("never the player's turn");
    }
}
