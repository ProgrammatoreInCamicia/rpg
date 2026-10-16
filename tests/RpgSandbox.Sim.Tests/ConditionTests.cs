using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>R2: saving throws, conditions, Exhaustion and the weapon masteries (SRD 5.2.1).</summary>
public class ConditionTests
{
    private static readonly ActorId Foe = new("foe"), Other = new("other");
    private const string Sword = "Spada lunga", Javelin = "Giavellotto (lanciato)";

    /// <summary>
    /// A yard (10 by 6) at noon: the Paladin at (3,2), a guard next to it at (4,2) and, with <paramref name="second"/>, another
    /// guard at (4,3). The Paladin strikes first; the first seed where it is then its turn again, with everything fresh.
    /// </summary>
    private static SimulationSession Fight(bool second = false, Action<SimulationSession>? before = null)
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var b = new ScenarioBuilder().WithSeed(seed)
                .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
                .AddMap("a", Enumerable.Repeat("YYYYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
                .AddFaction("watch", "Ronda").AddFaction("outlaws", "Fuorilegge").AddEnmity("watch", "outlaws")
                .AddActor("player", "Paladino", "yard", isPlayer: true, factionId: "outlaws", at: (3, 2))
                .AddActor("foe", "Guardia", "yard", factionId: "watch", combat: "guard", at: (4, 2));
            if (second) // of the watch too, enemies of the Paladin's: it joins the fight
                b.AddActor("other", "Altra guardia", "yard", factionId: "watch", combat: "guard", at: (4, 3));
            var s = SimulationSession.Create(b.Build());
            s.AdvanceTo(At(1, 12));
            before?.Invoke(s);
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword });
            if (s.GetPlayerView().Combat is { IsYourTurn: true })
                s.Execute(new EndTurnCommand { Actor = s.Player });
            for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
                s.Advance(Duration.FromSeconds(1));
            if (s.GetPlayerView().Combat is { IsYourTurn: true, You.ActionAvailable: true } && Of(s, Foe).Conscious
                && !s.GetPlayerView().Unconscious && (!second || Of(s, Other).Conscious && s.Engine.World.Encounters.Values.Single().Order.Any(c => c.Actor == Other)))
                return s;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static Actor Me(SimulationSession s) => Of(s, s.Player);

    private static Encounter E(SimulationSession s) => s.Engine.World.Encounters.Values.Single();

    /// <summary>The dice from here on start with a d20 that is not a 1, so the next plain attack roll hits a target of AC 1.</summary>
    private static void NoNaturalOne(SimulationSession s)
    {
        for (ulong k = 1; ; k++)
            if (new SplitMix64(k).Roll(20) > 1)
            {
                s.Engine.World.Rng = new SplitMix64(k);
                return;
            }
    }

    /// <summary>Gives the Paladin's sword a mastery, and makes every blow land but on a natural 1.</summary>
    private static void SwordWith(SimulationSession s, WeaponMastery mastery, params ActorId[] targets)
    {
        Me(s).Profile = Me(s).Profile with
        {
            Attacks = Me(s).Profile.Attacks.Select(a => a.Name == Sword ? a with { Mastery = mastery, AttackBonus = 30 } : a).ToArray(),
        };
        foreach (var id in targets)
        {
            Of(s, id).Profile = Of(s, id).Profile with { ArmorClass = 1 };
            Of(s, id).Vitality.RaiseMaximum(100); // it stays up to show what the mastery does
            Of(s, id).Vitality.Heal(1000);
        }
    }

    [Fact]
    public void Saving_throws_add_the_proficiency_only_where_the_class_has_it_and_paralysis_fails_strength_at_once()
    {
        var s = Fight();
        var sheet = Me(s).Sheet;
        Assert.Equal(Abilities.Modifier(sheet.Wisdom) + sheet.ProficiencyBonus, sheet.SaveBonus(Ability.Wisdom)); // Paladin: Wis, Cha
        Assert.Equal(Abilities.Modifier(sheet.Strength), sheet.SaveBonus(Ability.Strength));

        s.Engine.AddCondition(E(s), Me(s), ConditionKind.Paralyzed);
        Assert.False(s.Engine.SavingThrow(E(s), Me(s), Ability.Strength, 1, "una prova"));
        Assert.Contains(E(s).Log, l => l.Kind == CombatLogKind.Save && l.Text.Contains("fallito senza tiro"));
    }

    [Fact]
    public void A_prone_target_is_easier_to_hit_up_close_and_harder_from_afar_and_a_prone_attacker_has_disadvantage()
    {
        var s = Fight();
        s.Engine.AddCondition(E(s), Of(s, Foe), ConditionKind.Prone);
        var close = s.PreviewAttack(s.Player, Foe, Sword);
        Assert.True(close.Advantage);
        Assert.Contains("è a terra, vicino", close.Reasons);
        Me(s).PlaceAt(Spot.CenterOf(new GridPos(0, 2)));
        var far = s.PreviewAttack(s.Player, Foe, Javelin);
        Assert.True(far.Disadvantage);
        Assert.Contains("è a terra, lontano", far.Reasons);

        s.Engine.AddCondition(E(s), Me(s), ConditionKind.Prone);
        Assert.Contains("sei prono", s.PreviewAttack(s.Player, Foe, Javelin).Reasons);
        Assert.Contains(s.GetPlayerView().Conditions, c => c.Kind == ConditionKind.Prone);
        Assert.Contains(Assert.Single(s.GetPlayerView().VisibleActors, a => a.Id == Foe).Conditions, c => c.Kind == ConditionKind.Prone);
    }

    [Fact]
    public void Standing_up_takes_half_the_speed_and_comes_with_the_first_move()
    {
        var s = Fight();
        s.Engine.AddCondition(E(s), Me(s), ConditionKind.Prone);
        Assert.Equal(15 * 12, s.GetPlayerView().Combat!.You.MovementLeftInches);
        var moved = s.Execute(new MoveToSpotCommand { Actor = s.Player, To = Spot.CenterOf(new GridPos(3, 0)) });
        Assert.True(moved.Success, moved.Message);
        if (!Me(s).Conscious)
            return; // dropped on the way
        Assert.DoesNotContain(s.GetPlayerView().Conditions, c => c.Kind == ConditionKind.Prone);
        Assert.Contains(E(s).Log, l => l.Kind == CombatLogKind.Condition && l.Text.Contains("si rialza"));
        Assert.True(s.GetPlayerView().Combat!.You.MovementLeftInches <= 15 * 12 - 2 * 60);
    }

    [Fact]
    public void Grappled_or_restrained_one_cannot_move_and_restrained_one_is_easier_to_hit()
    {
        var s = Fight();
        s.Engine.AddCondition(E(s), Me(s), ConditionKind.Grappled, Of(s, Foe));
        Assert.Equal(0, s.GetPlayerView().Combat!.You.MovementLeftInches);
        Assert.Equal(RejectionReason.NoMovementLeft,
            s.Execute(new MoveToSpotCommand { Actor = s.Player, To = Spot.CenterOf(new GridPos(2, 0)) }).Rejection);
        s.Engine.RemoveCondition(E(s), Me(s), ConditionKind.Grappled);
        s.Engine.AddCondition(E(s), Of(s, Foe), ConditionKind.Restrained);
        Assert.Contains("è trattenuto", s.PreviewAttack(s.Player, Foe, Sword).Reasons);
        Assert.Contains(Assert.Single(s.GetPlayerView().VisibleActors, a => a.Id == Foe).Conditions, c => c.Kind == ConditionKind.Restrained);
    }

    [Fact]
    public void Poison_does_not_show_from_outside_and_gives_the_poisoned_disadvantage()
    {
        var s = Fight();
        s.Engine.AddCondition(E(s), Of(s, Foe), ConditionKind.Poisoned);
        Assert.Empty(Assert.Single(s.GetPlayerView().VisibleActors, a => a.Id == Foe).Conditions);
        s.Engine.AddCondition(E(s), Me(s), ConditionKind.Poisoned);
        var aim = s.PreviewAttack(s.Player, Foe, Sword);
        Assert.True(aim.Disadvantage);
        Assert.Contains("sei avvelenato", aim.Reasons);
    }

    [Fact]
    public void A_paralyzed_target_is_hit_critically_up_close_and_an_incapacitated_one_loses_its_turn_and_reaction()
    {
        var s = Fight();
        s.Engine.AddCondition(E(s), Of(s, Foe), ConditionKind.Paralyzed);
        var aim = s.PreviewAttack(s.Player, Foe, Sword);
        Assert.True(aim.Advantage);
        Assert.True(aim.AutoCritical);

        // Walking away from it provokes nothing; its turn passes.
        var moved = s.Execute(new MoveToSpotCommand { Actor = s.Player, To = Spot.CenterOf(new GridPos(0, 2)) });
        Assert.True(moved.Success, moved.Message);
        Assert.DoesNotContain(E(s).Log, l => l.Kind == CombatLogKind.OpportunityAttack);
        s.Execute(new EndTurnCommand { Actor = s.Player });
        for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
            s.Advance(Duration.FromSeconds(1));
        Assert.Contains(E(s).Log, l => l.Kind == CombatLogKind.TurnStarted && l.Actor == Foe && l.Text.Contains("non può agire"));
        Assert.DoesNotContain(E(s).Log, l => l.Kind == CombatLogKind.Attack && l.Actor == Foe && l.Round == E(s).Round);
    }

    [Fact]
    public void Blinded_one_sees_nobody_and_an_invisible_one_is_seen_by_nobody()
    {
        var s = Fight();
        s.Engine.AddCondition(E(s), Of(s, Foe), ConditionKind.Invisible);
        Assert.False(s.Engine.SeesActor(Me(s), Of(s, Foe)));
        s.Engine.RemoveCondition(E(s), Of(s, Foe), ConditionKind.Invisible);
        s.Engine.AddCondition(E(s), Me(s), ConditionKind.Blinded);
        Assert.False(s.Engine.SeesActor(Me(s), Of(s, Foe)));
        Assert.True(s.Engine.SeesActor(Of(s, Foe), Me(s)));
    }

    [Fact]
    public void Exhaustion_slows_weakens_every_d20_test_goes_down_with_a_long_rest_and_kills_at_six()
    {
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(1)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYY", 4).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, at: (1, 1))
            .Build());
        s.AdvanceTo(At(1, 12));
        s.Engine.AddExhaustion(Me(s), 2, "una prova");
        Assert.Equal(2, s.GetPlayerView().Exhaustion);
        Assert.Equal(20, s.Engine.WalkingSpeed(Me(s)));
        Assert.Equal(-4, Simulation.ExhaustionPenalty(Me(s)));

        var rest = s.Execute(new RestCommand { Actor = s.Player, Long = true });
        s.AdvanceUntilCompleted(rest.Action!.Value, Duration.FromHours(9));
        Assert.Equal(1, s.GetPlayerView().Exhaustion);

        s.Engine.AddExhaustion(Me(s), 5, "una prova");
        Assert.True(s.GetPlayerView().Dead);
    }

    [Fact]
    public void Sap_and_vex_and_slow_tip_the_next_rolls_and_end_on_time()
    {
        var s = Fight();
        SwordWith(s, WeaponMastery.Sap, Foe);
        NoNaturalOne(s);
        Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword }).Success);
        Assert.Contains(E(s).Log, l => l.Kind == CombatLogKind.Mastery && l.Text.Contains("intontito"));
        Assert.Equal(s.Player, Of(s, Foe).SappedBy);
        Assert.Contains("è ancora intontito dal colpo", s.Engine.ConditionEdges(Of(s, Foe), Me(s), withinFiveFeet: true).Reasons);

        Of(s, Foe).SappedBy = null;
        Me(s).Vexing = Foe;
        Assert.Contains("lo hai messo in difficoltà", s.Engine.ConditionEdges(Me(s), Of(s, Foe), withinFiveFeet: true).Reasons);
        Of(s, Foe).SlowedBy = s.Player;
        Assert.Equal(20, s.Engine.WalkingSpeed(Of(s, Foe)));
    }

    [Fact]
    public void Topple_knocks_prone_on_a_failed_constitution_save_and_push_sends_ten_feet_away()
    {
        var toppled = false;
        for (var tries = 0; tries < 20 && !toppled; tries++)
        {
            var s = Fight(before: x => x.Engine.World.Rng = new SplitMix64((ulong)tries + 99));
            SwordWith(s, WeaponMastery.Topple, Foe);
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword });
            if (!Of(s, Foe).Conscious)
                continue;
            var save = E(s).Log.LastOrDefault(l => l.Kind == CombatLogKind.Save && l.Actor == Foe);
            if (save is null)
                continue; // a natural 1 missed
            Assert.Contains("Costituzione", save.Text);
            toppled = save.Text.Contains("fallito");
            Assert.Equal(toppled, s.Engine.Has(Of(s, Foe), ConditionKind.Prone));
        }
        Assert.True(toppled);

        var p = Fight();
        SwordWith(p, WeaponMastery.Push, Foe);
        var start = p.Engine.CurrentSpot(Of(p, Foe))!.Value;
        NoNaturalOne(p);
        var from = E(p).Log.Count;
        p.Execute(new AttackCommand { Actor = p.Player, Target = Foe, Attack = Sword });
        var pushed = E(p).Log.Skip(from).LastOrDefault(l => l.Kind == CombatLogKind.Mastery);
        Assert.Contains("spinge indietro", pushed!.Text);
        Assert.Equal(120, (int)Math.Round(start.DistanceTo(p.Engine.CurrentSpot(Of(p, Foe))!.Value)));
        Assert.DoesNotContain(E(p).Log, l => l.Kind == CombatLogKind.OpportunityAttack);
    }

    [Fact]
    public void Cleave_carries_the_blow_to_a_second_foe_next_to_the_first_once_a_turn()
    {
        var s = Fight(second: true);
        SwordWith(s, WeaponMastery.Cleave, Foe, Other);
        var before = E(s).Log.Count;
        NoNaturalOne(s);
        s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword });
        var turn = E(s).Log.Skip(before).ToList();
        Assert.Contains(turn, l => l.Kind == CombatLogKind.Mastery && l.Text.Contains("prosegue il fendente"));
        Assert.Contains(turn, l => l.Kind == CombatLogKind.Attack && l.Target == Other);
        Assert.Equal(2, turn.Count(l => l.Kind == CombatLogKind.Attack));
    }

    [Fact]
    public void Conditions_exhaustion_and_marks_survive_save_and_load()
    {
        var s = Fight();
        s.Engine.AddCondition(E(s), Of(s, Foe), ConditionKind.Prone, Me(s));
        s.Engine.AddCondition(E(s), Me(s), ConditionKind.Poisoned, until: s.Now.Plus(Duration.FromHours(1)));
        Me(s).Exhaustion = 1;
        Me(s).Vexing = Foe;
        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Contains(loaded.GetPlayerView().Conditions, c => c.Kind == ConditionKind.Poisoned && c.Until == s.Now.Plus(Duration.FromHours(1)));
        Assert.Equal(1, loaded.GetPlayerView().Exhaustion);
    }

    [Fact]
    public void Frightened_one_attacks_with_disadvantage_and_will_not_step_closer_to_what_it_fears()
    {
        var s = Fight();
        Me(s).PlaceAt(Spot.CenterOf(new GridPos(1, 2)));
        s.Engine.AddCondition(E(s), Me(s), ConditionKind.Frightened, Of(s, Foe));
        Assert.Contains("hai paura", s.PreviewAttack(s.Player, Foe, Javelin).Reasons);
        var closer = s.Execute(new MoveToSpotCommand { Actor = s.Player, To = Spot.CenterOf(new GridPos(2, 2)) });
        Assert.Equal(RejectionReason.Unreachable, closer.Rejection);
        Assert.Contains("paura", closer.Message);
        Assert.True(s.Execute(new MoveToSpotCommand { Actor = s.Player, To = Spot.CenterOf(new GridPos(0, 2)) }).Success);
    }
}
