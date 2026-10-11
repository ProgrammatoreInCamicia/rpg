using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>C3a: Dash, Disengage, Dodge (SRD 5.2.1 Actions).</summary>
public class CombatActionTests
{
    private static readonly ActorId Foe = new("foe");

    /// <summary>Noon, a 16×6 yard. The player (a Paladino) at (2,2), the foe (a villager with the guard's profile) next to it.</summary>
    private static SimulationSession Yard(ulong seed, (int X, int Y)? foeAt = null, string foe = "guard", int foeHp = 11)
    {
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: (2, 2))
            .AddActor("foe", "Avversario", "yard", combat: foe, at: foeAt ?? (3, 2))
            .Build());
        s.AdvanceTo(At(1, 12));
        if (foeHp < CombatProfiles.ById(foe)!.MaxHitPoints)
            s.Engine.World.Actors[Foe].Vitality = new Vitality(CombatProfiles.ById(foe)!.MaxHitPoints, false, foeHp);
        return s;
    }

    private static SimulationSession FirstSeed(Func<ulong, SimulationSession> scene, Func<SimulationSession, bool> ok)
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = scene(seed);
            if (ok(s))
                return s;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    /// <summary>The foe expects nothing; the player's opening blow is followed by the first round, until the player's turn.</summary>
    private static SimulationSession OpenedOn(ulong seed, (int X, int Y)? foeAt = null, string attack = "Spada lunga")
    {
        var s = Yard(seed, foeAt);
        s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = attack });
        return s;
    }

    /// <summary>The next round, at the player's turn, with its action back.</summary>
    private static bool NextRound(SimulationSession s)
    {
        if (s.GetPlayerView().Combat is not { IsYourTurn: true })
            return false;
        s.Execute(new EndTurnCommand { Actor = s.Player });
        s.Advance(Duration.FromSeconds(6));
        return s.GetPlayerView().Combat is { IsYourTurn: true, You.ActionAvailable: true };
    }

    private static CombatView Fight(SimulationSession s) => s.GetPlayerView().Combat!;

    /// <summary>The fight as it really went (C5a: the player does not see a foe's dice).</summary>
    private static List<CombatRecord> TrueLog(SimulationSession s) => s.Engine.World.Encounters.Values.Single().Log;

    private static CommandResult Do(SimulationSession s, CombatAction action) =>
        s.Execute(new CombatActionCommand { Actor = s.Player, Action = action });

    [Fact]
    public void Dash_adds_the_speed_to_this_turns_movement_and_uses_the_action()
    {
        var s = FirstSeed(seed => OpenedOn(seed), NextRound);

        Assert.True(Do(s, CombatAction.Dash).Success);

        Assert.Equal(60, Fight(s).You.MovementLeftFeet);
        Assert.False(Fight(s).You.ActionAvailable);
        Assert.Equal(RejectionReason.NoActionLeft, s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Spada lunga" }).Rejection);
        Assert.Equal(RejectionReason.NoActionLeft, Do(s, CombatAction.Dodge).Rejection);
        Assert.Contains(Fight(s).Log, l => l.Kind == CombatLogKind.Dash && l.Actor == s.Player);
        // And the extra movement is really walked: 9 squares (45 feet) in one go.
        Assert.True(s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(11, 5) }).Success);
        Assert.Equal(new GridPos(11, 5), s.GetPlayerView().Position);
    }

    [Fact]
    public void After_a_disengage_walking_away_provokes_no_opportunity_attack()
    {
        var s = FirstSeed(seed => OpenedOn(seed), x => NextRound(x) && x.GetPlayerView().Position == new GridPos(2, 2)
                                                      && x.Engine.World.Actors[Foe].Position == new GridPos(3, 2));
        Assert.NotEmpty(s.PreviewMove(s.Player, new GridPos(0, 2)).Provokes);

        Assert.True(Do(s, CombatAction.Disengage).Success);

        Assert.True(Fight(s).You.Disengaged);
        Assert.Empty(s.PreviewMove(s.Player, new GridPos(0, 2)).Provokes);
        Assert.True(s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(0, 2) }).Success);
        Assert.DoesNotContain(Fight(s).Log, l => l.Kind == CombatLogKind.OpportunityAttack);
    }

    [Fact]
    public void Attacks_against_someone_dodging_have_disadvantage_until_its_next_turn()
    {
        var s = FirstSeed(seed => OpenedOn(seed), x =>
        {
            if (!NextRound(x) || !Do(x, CombatAction.Dodge).Success)
                return false;
            x.Execute(new EndTurnCommand { Actor = x.Player });
            x.Advance(Duration.FromSeconds(6));
            // The foe struck while the player was dodging, and the fight goes on to the player's next turn.
            return x.GetPlayerView().Combat is { IsYourTurn: true } c
                   && c.Log.SkipWhile(l => l.Kind != CombatLogKind.Dodge).Any(l => l.Kind == CombatLogKind.Attack && l.Actor == Foe);
        });

        var round = Fight(s).Round;
        var dodge = Fight(s).Log.Single(l => l.Kind == CombatLogKind.Dodge);
        var truth = TrueLog(s);
        var blow = truth.SkipWhile(l => l.Kind != CombatLogKind.Dodge).First(l => l.Kind == CombatLogKind.Attack && l.Actor == Foe); // the first blow after the Dodge
        Assert.Equal(round - 1, dodge.Round);
        Assert.Equal(2, blow.D20.Count); // Disadvantage: two dice
        Assert.False(Fight(s).You.Dodging); // over at the start of the player's next turn
    }

    [Fact]
    public void The_actions_say_when_they_can_be_taken()
    {
        var s = FirstSeed(seed => OpenedOn(seed), NextRound);

        Assert.All(Fight(s).Actions, a => Assert.True(a.Valid));
        Assert.Equal(new[] { CombatAction.Dash, CombatAction.Disengage, CombatAction.Dodge }, Fight(s).Actions.Select(a => a.Action));

        s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Spada lunga" });
        if (s.GetPlayerView().Combat is { } after)
            Assert.All(after.Actions, a => Assert.Equal(RejectionReason.NoActionLeft, a.Reason));
        Assert.Equal(RejectionReason.NotInCombat, Do(SimulationSession.Create(MappedVillageScenario.CreateLivingVillage()), CombatAction.Dash).Rejection);
    }

    [Fact]
    public void A_commoner_running_from_an_enemy_next_to_it_disengages_first()
    {
        // The player strikes a commoner left at 1 HP who survives the blow: on its turn it runs, without being struck.
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed, foe: "commoner", foeHp: 1);
            x.Execute(new AttackCommand { Actor = x.Player, Target = Foe, Attack = "Spada lunga" });
            return x;
        }, x => x.Engine.World.Actors[Foe].Conscious && x.GetWorldView().RecentFacts.Any(f => f.Kind == "Fled"));

        var log = s.Engine.World.Encounters.Values.SingleOrDefault()?.Log ?? new();
        var facts = s.GetWorldView().RecentFacts;
        Assert.DoesNotContain(facts, f => f.Kind == "Attack" && f.Description.Contains("coglie l'occasione"));
        if (log.Count > 0)
            Assert.Contains(log, l => l.Kind == CombatLogKind.Disengage && l.Actor == Foe);
    }

    [Fact]
    public void An_npc_too_far_to_strike_dashes_to_close_in()
    {
        // The foe is 13 squares off: beyond its thrown spear (60 feet). The player opens with a javelin (long range).
        var s = FirstSeed(seed => OpenedOn(seed, foeAt: (15, 2), attack: "Giavellotto (lanciato)"),
            x => x.GetPlayerView().Combat is { IsYourTurn: true } c && c.Log.Any(l => l.Kind == CombatLogKind.Dash && l.Actor == Foe));

        var foe = s.Engine.World.Actors[Foe].Position!.Value;
        Assert.True(foe.X <= 15 - 7, $"the foe only reached {foe}"); // further than its normal 6 squares
    }

    [Fact]
    public void A_fight_saved_after_a_dash_and_a_disengage_loads_the_same()
    {
        var s = FirstSeed(seed => OpenedOn(seed), NextRound);
        Do(s, CombatAction.Disengage);
        s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(0, 4) });

        var loaded = s.SaveAndReload();

        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.True(loaded.GetPlayerView().Combat!.You.Disengaged);
    }
}
