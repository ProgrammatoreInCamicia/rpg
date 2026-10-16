using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>R4a: grappling and shoving with the Unarmed Strike (SRD 5.2.1 Rules Glossary).</summary>
public class GrappleTests
{
    private static readonly ActorId Foe = new("foe");
    private const string Unarmed = "Colpo senz'armi", Sword = "Spada lunga";

    /// <summary>The Paladin strikes the guard next to it first; the first seed where it is then its fresh turn with the guard up.</summary>
    private static SimulationSession Fight(bool freeHands = true)
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
                .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
                .AddMap("a", Enumerable.Repeat("YYYYYYYYYYYY", 8).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
                .AddActor("player", "Paladino", "yard", isPlayer: true, at: (3, 3))
                .AddActor("foe", "Guardia", "yard", combat: "guard", at: (4, 3))
                .Build());
            s.AdvanceTo(At(1, 12));
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword });
            if (s.GetPlayerView().Combat is { IsYourTurn: true })
                s.Execute(new EndTurnCommand { Actor = s.Player });
            for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
                s.Advance(Duration.FromSeconds(1));
            if (s.GetPlayerView().Combat is not { IsYourTurn: true, You.ActionAvailable: true } || !Me(s).Conscious || !Of(s, Foe).Conscious
                || s.Engine.Has(Me(s), ConditionKind.Prone) || s.GetPlayerView().Combat!.You.MovementLeftInches < 30 * 12)
                continue;
            if (freeHands)
            {
                Me(s).Equipment = Me(s).Equipment with { Shield = null, MainHand = null };
                s.Engine.World.Refresh(Me(s));
            }
            return s;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    private static Actor Me(SimulationSession s) => s.Engine.World.Actors[s.Player];

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static Encounter E(SimulationSession s) => s.Engine.World.Encounters.Values.Single();

    [Fact]
    public void Grappling_takes_a_free_hand_and_a_failed_save_leaves_the_target_grappled_with_no_speed()
    {
        var armed = Fight(freeHands: false);
        var refused = armed.Execute(new AttackCommand { Actor = armed.Player, Target = Foe, Attack = Unarmed, Maneuver = "Grapple" });
        Assert.Equal(RejectionReason.UnknownAttack, refused.Rejection);
        Assert.Contains("mano libera", refused.Message);

        var s = Fight();
        s.Engine.AddCondition(E(s), Of(s, Foe), ConditionKind.Paralyzed); // it fails Strength and Dexterity saves
        var grabbed = s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Unarmed, Maneuver = "Grapple" });
        Assert.True(grabbed.Success, grabbed.Message);
        var hold = Assert.Single(Of(s, Foe).Conditions, c => c.Kind == ConditionKind.Grappled);
        Assert.Equal(s.Player, hold.Source);
        Assert.Equal(0, s.Engine.SpeedNow(Of(s, Foe), 30));
        Assert.Contains(E(s).Log, l => l.Kind == CombatLogKind.Save && l.Text.Contains("la presa"));
        Assert.False(s.GetPlayerView().Combat!.You.ActionAvailable); // it was the Attack action
    }

    [Fact]
    public void A_shove_pushes_five_feet_away_or_knocks_prone()
    {
        var s = Fight();
        s.Engine.AddCondition(E(s), Of(s, Foe), ConditionKind.Paralyzed);
        var start = s.Engine.CurrentSpot(Of(s, Foe))!.Value;
        Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Unarmed, Maneuver = "Shove" }).Success);
        Assert.Equal(60, (int)Math.Round(start.DistanceTo(s.Engine.CurrentSpot(Of(s, Foe))!.Value)));

        var p = Fight();
        p.Engine.AddCondition(E(p), Of(p, Foe), ConditionKind.Paralyzed);
        Assert.True(p.Execute(new AttackCommand { Actor = p.Player, Target = Foe, Attack = Unarmed, Maneuver = "Shove", ShoveProne = true }).Success);
        Assert.True(p.Engine.Has(Of(p, Foe), ConditionKind.Prone));
    }

    [Fact]
    public void Dragging_the_grappled_halves_the_movement_and_takes_it_along_and_letting_go_is_free()
    {
        var s = Fight();
        s.Engine.AddCondition(E(s), Of(s, Foe), ConditionKind.Grappled, Me(s));
        Assert.Equal(15 * 12, s.GetPlayerView().Combat!.You.MovementLeftInches); // 30 feet, every foot costs 2
        var foeAt = s.Engine.CurrentSpot(Of(s, Foe))!.Value;
        var moved = s.Execute(new MoveToSpotCommand { Actor = s.Player, To = Spot.CenterOf(new GridPos(3, 1)) });
        Assert.True(moved.Success, moved.Message);
        Assert.Equal(foeAt.Y - 120, s.Engine.CurrentSpot(Of(s, Foe))!.Value.Y); // dragged along, 10 feet
        Assert.True(s.Engine.Has(Of(s, Foe), ConditionKind.Grappled));
        Assert.Contains(E(s).Log, l => l.Kind == CombatLogKind.Mastery && l.Text.Contains("trascina"));
        Assert.True(s.GetPlayerView().Combat!.You.MovementLeftInches <= 15 * 12 - 120);

        Assert.True(s.Execute(new ReleaseCommand { Actor = s.Player }).Success);
        Assert.False(s.Engine.Has(Of(s, Foe), ConditionKind.Grappled));
        Assert.Equal(RejectionReason.NotHeld, s.Execute(new ReleaseCommand { Actor = s.Player }).Rejection);
    }

    [Fact]
    public void The_grappled_one_escapes_with_athletics_or_acrobatics_and_an_incapacitated_grappler_lets_go()
    {
        var s = Fight();
        Of(s, Foe).Sheet = Of(s, Foe).Sheet with { Strength = 1 }; // escape DC 8 − 5 + 2 = 5: any roll does it
        s.Engine.AddCondition(E(s), Me(s), ConditionKind.Grappled, Of(s, Foe));
        Assert.Contains(s.GetPlayerView().Combat!.Actions, a => a.Action == CombatAction.Escape && a.Valid);
        var escaped = s.Execute(new CombatActionCommand { Actor = s.Player, Action = CombatAction.Escape });
        Assert.True(escaped.Success, escaped.Message);
        Assert.Contains("si libera", escaped.Message);
        Assert.False(s.Engine.Has(Me(s), ConditionKind.Grappled));
        Assert.Contains(s.GetPlayerView().Combat!.Actions, a => a.Action == CombatAction.Escape && a.Reason == RejectionReason.NotHeld);

        var g = Fight();
        g.Engine.AddCondition(E(g), Me(g), ConditionKind.Grappled, Of(g, Foe));
        g.Engine.AddCondition(E(g), Of(g, Foe), ConditionKind.Stunned);
        Assert.False(g.Engine.Has(Me(g), ConditionKind.Grappled));
    }

    [Fact]
    public void A_grapple_survives_save_and_load()
    {
        var s = Fight();
        s.Engine.AddCondition(E(s), Of(s, Foe), ConditionKind.Grappled, Me(s));
        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(15 * 12, loaded.GetPlayerView().Combat!.You.MovementLeftInches);
    }
}
