using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>R1: Short and Long Rests, Hit Point Dice (SRD 5.2.1).</summary>
public class RestTests
{
    private static readonly ActorId Guard = new("guard");

    /// <summary>A yard (8 by 6) at noon: the player (a level 1 Paladin), a guard of the village at (6,5).</summary>
    private static SimulationSession Yard(int hour = 12)
    {
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(3)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, at: (1, 1))
            .AddFaction("village", "Villaggio")
            .AddActor("guard", "Guardia", "yard", factionId: "village", combat: "guard", at: (6, 5))
            .Build());
        s.AdvanceTo(At(1, hour));
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static Actor Me(SimulationSession s) => Of(s, s.Player);

    private static IEnumerable<string> Facts(SimulationSession s) => s.GetWorldView().RecentFacts.Select(f => f.Kind);

    private static void Rest(SimulationSession s, bool longRest, int dice = 0)
    {
        var started = s.Execute(new RestCommand { Actor = s.Player, Long = longRest, HitDice = dice });
        Assert.True(started.Success, started.Message);
        s.AdvanceUntilCompleted(started.Action!.Value, Duration.FromHours(9));
    }

    [Fact]
    public void A_long_rest_takes_eight_hours_gives_back_everything_and_not_again_for_sixteen_hours()
    {
        var s = Yard();
        Me(s).Vitality.TakeDamage(6);
        Me(s).HitDiceSpent = 1;
        Assert.Equal(new HitDiceView { Remaining = 0, Max = 1, Die = 10 }, s.GetPlayerView().HitDice);

        var start = s.Now;
        var started = s.Execute(new RestCommand { Actor = s.Player, Long = true });
        Assert.Equal(start.Plus(Duration.FromHours(8)), started.CompletesAt);
        Assert.Equal("Rest", s.GetPlayerView().Action!.Kind);
        s.AdvanceUntilCompleted(started.Action!.Value, Duration.FromHours(9));

        Assert.Equal(Me(s).Vitality.Maximum, Me(s).Vitality.Current);
        Assert.Equal(Me(s).Vitality.Maximum, s.GetPlayerView().HitPoints);
        Assert.Equal(Me(s).Vitality.Maximum, s.GetPlayerView().MaxHitPoints);
        Assert.Equal(1, s.GetPlayerView().HitDice!.Remaining);
        Assert.Contains("RestCompleted", Facts(s));
        Assert.Equal(s.Now.Plus(Duration.FromHours(16)), s.GetPlayerView().LongRestFrom);
        Assert.Equal(RejectionReason.TooSoon, s.Execute(new RestCommand { Actor = s.Player, Long = true }).Rejection);
        s.Advance(Duration.FromHours(16));
        Assert.Null(s.GetPlayerView().LongRestFrom);
    }

    [Fact]
    public void A_short_rest_spends_hit_point_dice_each_the_die_plus_constitution()
    {
        var s = Yard();
        Me(s).Experience = 900; // level 3: three Hit Point Dice
        s.Engine.World.Refresh(Me(s));
        Me(s).Vitality.RaiseMaximum(Me(s).Profile.MaxHitPoints - Me(s).Vitality.Maximum);
        Me(s).Vitality.TakeDamage(Me(s).Vitality.Maximum - 1); // 1 Hit Point left: two dice cannot overshoot
        Assert.Equal(3, s.GetPlayerView().HitDice!.Max);
        Assert.Equal(RejectionReason.InvalidAmount, s.Execute(new RestCommand { Actor = s.Player, Long = false, HitDice = 4 }).Rejection);

        var started = s.Execute(new RestCommand { Actor = s.Player, Long = false, HitDice = 2 });
        Assert.Equal(s.Now.Plus(Duration.FromHours(1)), started.CompletesAt);
        s.AdvanceUntilCompleted(started.Action!.Value, Duration.FromHours(2));

        var con = Abilities.Modifier(Me(s).Sheet.Constitution);
        Assert.InRange(Me(s).Vitality.Current - 1, 2 * Math.Max(0, 1 + con), 2 * (10 + con));
        Assert.Equal(1, s.GetPlayerView().HitDice!.Remaining);
    }

    [Fact]
    public void Stopped_a_short_rest_gives_nothing_and_a_long_one_does_not_count()
    {
        var s = Yard();
        Me(s).Experience = 300; // level 2
        s.Engine.World.Refresh(Me(s));
        Me(s).Vitality.TakeDamage(5);
        var hp = Me(s).Vitality.Current;
        Assert.True(s.Execute(new RestCommand { Actor = s.Player, Long = false, HitDice = 2 }).Success);
        s.Advance(Duration.FromMinutes(30));
        Assert.True(s.Execute(new StopCommand { Actor = s.Player }).Success);
        Assert.Null(s.GetPlayerView().Action);
        Assert.Equal(hp, Me(s).Vitality.Current);
        Assert.Equal(2, s.GetPlayerView().HitDice!.Remaining);

        Assert.True(s.Execute(new RestCommand { Actor = s.Player, Long = true }).Success);
        s.Advance(Duration.FromHours(2));
        Assert.True(s.Execute(new StopCommand { Actor = s.Player }).Success);
        Assert.Equal(hp, Me(s).Vitality.Current);
        Assert.Null(s.GetPlayerView().LongRestFrom); // not a Long Rest: another can start at once
        Assert.Equal(2, s.GetWorldView().RecentFacts.Count(f => f.Kind == "RestInterrupted"));
    }

    [Fact]
    public void Asleep_one_sees_nothing_and_an_attack_wakes_and_interrupts()
    {
        var s = Yard();
        Assert.Contains(s.GetPlayerView().VisibleActors, a => a.Id == Guard);
        Assert.True(s.Execute(new RestCommand { Actor = s.Player, Long = true }).Success);
        Assert.DoesNotContain(s.GetPlayerView().VisibleActors, a => a.Id == Guard);
        Assert.False(s.Engine.SeesActor(Me(s), Of(s, Guard))); // nor for the rules (who it saw strike, a theft)
        Assert.DoesNotContain('2', string.Concat(s.GetPlayerView().MapVisibility ?? Array.Empty<string>()));

        Assert.True(s.Execute(new AttackCommand { Actor = Guard, Target = s.Player, Attack = "Lancia (lanciata)" }).Success);
        Assert.NotEqual("Rest", s.GetPlayerView().Action?.Kind);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "RestInterrupted" && f.Description.Contains("scontro"));
        Assert.Contains(s.GetPlayerView().VisibleActors, a => a.Id == Guard);
        Assert.Equal(RejectionReason.InCombat, s.Execute(new RestCommand { Actor = s.Player, Long = false }).Rejection);
    }

    [Fact]
    public void Blows_on_a_door_nearby_wake_the_sleeper()
    {
        // A wall down x = 4|5 with a stuck door at row 3 (on the edge, at (5,3)); the guard forces it, the player sleeps
        // at (7,1) on the same side, a few steps away.
        var b = new ScenarioBuilder().WithSeed(3)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, at: (7, 1))
            .AddActor("guard", "Guardia", "yard", combat: "guard", at: (5, 3));
        for (var y = 0; y < 6; y++)
            b.AddEdge("a", (4, y), (5, y), y == 3 ? MapEdgeKind.Door : MapEdgeKind.Wall, y == 3 ? (5, 3) : null,
                lockSpec: y == 3 ? new LockSpec(Stuck: true) : null);
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, 23));
        Assert.True(s.Execute(new RestCommand { Actor = s.Player, Long = true }).Success);

        Assert.True(s.Execute(new ForceCommand { Actor = Guard, Door = new GridPos(5, 3) }).Success);

        Assert.Null(s.GetPlayerView().Action);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "RestInterrupted" && f.Description.Contains("colpi"));
    }

    [Fact]
    public void A_wounded_npc_heals_after_eight_hours_without_harm()
    {
        var s = Yard();
        var guard = Of(s, Guard);
        guard.Vitality.TakeDamage(4);
        guard.LastHarmAt = s.Now;
        s.Advance(Duration.FromHours(7));
        Assert.True(guard.Vitality.Current < guard.Vitality.Maximum);
        s.Advance(Duration.FromHours(3));
        Assert.Equal(guard.Vitality.Maximum, guard.Vitality.Current);
    }

    [Fact]
    public void A_rest_survives_save_and_load()
    {
        var s = Yard();
        Me(s).Vitality.TakeDamage(3);
        Me(s).HitDiceSpent = 1;
        Assert.True(s.Execute(new RestCommand { Actor = s.Player, Long = true }).Success);
        s.Advance(Duration.FromHours(3));
        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal("Rest", loaded.GetPlayerView().Action!.Kind);
        loaded.Advance(Duration.FromHours(6));
        Assert.Equal(1, loaded.GetPlayerView().HitDice!.Remaining);
    }
}
