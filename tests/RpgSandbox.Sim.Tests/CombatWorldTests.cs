using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>C2: the fight in the world.</summary>
public class CombatWorldTests
{
    private static readonly ActorId Foe = new("foe");
    private static readonly ActorId Watchman = new("watchman");
    private const string Sword = "Spada lunga";
    private const string Javelin = "Giavellotto (lanciato)";

    /// <summary>
    /// A 10×6 yard, at noon unless said otherwise (bright daylight everywhere). The player, a Paladin, stands at (2,2);
    /// the foe at (3,2) by default, a raider (holding the player hostile) or a plain villager.
    /// </summary>
    private static SimulationSession Yard(ulong seed = 1, string foe = "guard", bool raider = false, (int X, int Y)? foeAt = null,
        int hour = 12, Action<ScenarioBuilder>? more = null)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "Villaggio").AddArea("woods", "Bosco")
            .AddLocation("yard", "Cortile", "a").AddLocation("camp", "Campo", "woods")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddStore("campstore", "Scorte del campo", "camp", 100, ownerFactionId: "bandits")
            .AddRaidingFaction("bandits", "Banditi", "campstore", dailyUpkeep: 1, upkeepTimeOfDay: Duration.FromHours(8),
                foodThreshold: 0, raidAmount: 1, evaluationInterval: Duration.FromHours(24))
            .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: (2, 2))
            .AddActor("foe", "Avversario", "yard", factionId: raider ? "bandits" : null, combat: foe, sheet: Sheets.Raider(),
                at: foeAt ?? (3, 2));
        more?.Invoke(b);
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, hour));
        return s;
    }

    /// <summary>The first seed, from 1, for which <paramref name="ok"/> holds on the scene it builds.</summary>
    private static SimulationSession FirstSeed(Func<ulong, SimulationSession> scene, Func<SimulationSession, bool> ok)
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var s = scene(seed);
            if (ok(s))
                return s;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    private static CommandResult Attack(SimulationSession s, ActorId target, string attack = Sword, bool nonLethal = false, ActorId? by = null) =>
        s.Execute(new AttackCommand { Actor = by ?? s.Player, Target = target, Attack = attack, NonLethal = nonLethal });

    private static CombatView Fight(SimulationSession s) => s.GetPlayerView().Combat ?? throw new InvalidOperationException("No fight.");

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    // ---------------------------------------------------------------- opening

    [Fact]
    public void Outside_a_fight_there_is_no_combat_view_and_turn_commands_are_refused()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());

        Assert.Null(s.GetPlayerView().Combat);
        Assert.False(s.GetPlayerView().Dead);
        Assert.Equal(RejectionReason.NotInCombat, s.Execute(new EndTurnCommand { Actor = s.Player }).Rejection);
        Assert.Equal(RejectionReason.NotInCombat, s.PreviewMove(s.Player, PlayerStart).Reason);
    }

    [Fact]
    public void A_villager_struck_without_warning_takes_the_blow_before_initiative_and_is_surprised()
    {
        // The guard profile (11 HP) so it survives the blow and the fight goes on.
        var s = FirstSeed(seed => { var x = Yard(seed); Assert.True(Attack(x, Foe).Success); return x; },
            x => x.GetPlayerView().Combat is not null);

        var fight = Fight(s);
        Assert.Equal(new[] { CombatLogKind.Started, CombatLogKind.Attack, CombatLogKind.Initiative, CombatLogKind.Initiative },
            fight.Log.Take(4).Select(l => l.Kind));
        Assert.Equal(s.Player, fight.Log[1].Actor);
        Assert.Equal(2, fight.Log.Single(l => l.Kind == CombatLogKind.Initiative && l.Actor == Foe).D20.Count); // surprised: Disadvantage
        Assert.Single(fight.Log.Single(l => l.Kind == CombatLogKind.Initiative && l.Actor == s.Player).D20);
        // Whoever went first, it is now the player's first turn, with the action already spent on that blow.
        Assert.True(fight.IsYourTurn);
        Assert.Equal(1, fight.Round);
        Assert.False(fight.You.ActionAvailable);
        Assert.Equal(30, fight.You.MovementLeftFeet);
        Assert.Contains(s.Player, Of(s, Foe).FoughtWith);
    }

    [Fact]
    public void A_raider_who_sees_the_player_coming_rolls_initiative_first_and_the_blow_waits_for_the_players_turn()
    {
        var s = FirstSeed(seed => { var x = Yard(seed, foe: "bandit", raider: true); Assert.True(Attack(x, Foe).Success); return x; },
            x => x.GetPlayerView().Combat is not null);

        var log = Fight(s).Log;
        Assert.Equal(new[] { CombatLogKind.Started, CombatLogKind.OpeningAttackPending, CombatLogKind.Initiative, CombatLogKind.Initiative },
            log.Take(4).Select(l => l.Kind));
        Assert.All(log.Where(l => l.Kind == CombatLogKind.Initiative), l => Assert.Single(l.D20)); // nobody surprised
        // The player's blow comes only once its turn has started.
        var turn = log.ToList().FindIndex(l => l.Kind == CombatLogKind.TurnStarted && l.Actor == s.Player);
        var blow = log.ToList().FindIndex(l => l.Kind == CombatLogKind.Attack && l.Actor == s.Player);
        Assert.True(turn >= 0 && blow > turn);
        Assert.Null(Fight(s).PendingOpeningAttack);
        Assert.False(Fight(s).You.ActionAvailable);
    }

    [Fact]
    public void A_raider_who_cannot_see_the_player_in_the_dark_is_struck_at_once_with_advantage()
    {
        // 01:00: only a tiny lamp on the raider's own square. The player, next to it, stands in the dark.
        var s = Yard(foe: "bandit", raider: true, hour: 1, more: b => b.AddLight("a", (3, 2), brightFeet: 0, dimFeet: 0));
        Assert.False(s.Engine.SeesActor(Of(s, Foe), Of(s, s.Player)));

        Assert.True(Attack(s, Foe).Success);

        var attack = s.Engine.World.Encounters.Values.Single().Log.First(l => l.Kind == CombatLogKind.Attack);
        Assert.Equal(s.Player, attack.Actor);
        Assert.Equal(2, attack.D20.Count); // Advantage: the target does not see the attacker
        Assert.DoesNotContain(s.Engine.World.Encounters.Values.Single().Log, l => l.Kind == CombatLogKind.OpeningAttackPending);
    }

    [Fact]
    public void An_npc_that_expects_the_attack_can_be_seen_waiting_for_its_turn()
    {
        // The raider attacks the player, who sees it and holds it hostile: initiative first; the player goes first.
        var s = FirstSeed(seed => { var x = Yard(seed, foe: "bandit", raider: true); Assert.True(Attack(x, x.Player, "Scimitarra", by: Foe).Success); return x; },
            x => x.GetPlayerView().Combat is { IsYourTurn: true, PendingOpeningAttack: not null });

        var pending = Fight(s).PendingOpeningAttack!;
        Assert.Equal((Foe, s.Player, "Scimitarra"), (pending.Attacker!.Value, pending.Target, pending.Attack));
        Assert.True(Fight(s).You.ActionAvailable);
    }

    // ---------------------------------------------------------------- time

    [Fact]
    public void The_world_waits_for_the_players_turn_and_moves_on_six_seconds_a_round()
    {
        var s = FirstSeed(seed => { var x = Yard(seed); Attack(x, Foe); return x; }, x => x.GetPlayerView().Combat is not null);
        var start = s.Now;

        s.Advance(Duration.FromMinutes(1));
        Assert.Equal(start, s.Now); // the player's turn: no time passes

        Assert.True(s.Execute(new EndTurnCommand { Actor = s.Player }).Success);
        var between = Fight(s);
        Assert.False(between.IsYourTurn);
        Assert.Equal(start.Plus(Duration.FromSeconds(6)), between.NextRoundAt);

        s.Advance(Duration.FromSeconds(6));
        Assert.Equal(start.Plus(Duration.FromSeconds(6)), s.Now);
        Assert.Equal((2, true), (Fight(s).Round, Fight(s).IsYourTurn));
        Assert.True(Fight(s).You.ActionAvailable);
    }

    [Fact]
    public void Advancing_until_someone_elses_action_completes_stops_at_the_players_turn()
    {
        // A villager far off, resting an hour at a time, outside the fight.
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed, more: b => b.AddActor("watchman", "Guardiano", "yard", at: (9, 5)));
            Attack(x, Foe);
            return x;
        }, x => x.GetPlayerView().Combat is not null);
        var start = s.Now;
        s.Execute(new EndTurnCommand { Actor = s.Player });

        var result = s.AdvanceUntilCompleted(s.GetWorldView().Actor(Watchman).Action!.Id, Duration.FromHours(2));

        Assert.Equal((AdvanceOutcome.YourTurn, start.Plus(Duration.FromSeconds(6))), (result.Outcome, result.Now));
        Assert.True(Fight(s).IsYourTurn);
    }

    [Fact]
    public void World_commands_are_refused_during_a_fight_and_turn_commands_out_of_turn()
    {
        var s = FirstSeed(seed => { var x = Yard(seed); Attack(x, Foe); return x; }, x => x.GetPlayerView().Combat is not null);

        Assert.Equal(RejectionReason.InCombat, s.Execute(new WaitCommand { Actor = s.Player, Duration = Duration.FromMinutes(1) }).Rejection);
        Assert.Equal(RejectionReason.InCombat, s.Execute(new TorchCommand { Actor = s.Player, Lit = true }).Rejection);
        Assert.Equal(RejectionReason.NoActionLeft, Attack(s, Foe).Rejection);
        Assert.Equal(RejectionReason.InCombat, s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(0, 0), Stealthy = true }).Rejection);

        s.Execute(new EndTurnCommand { Actor = s.Player });
        Assert.Equal(RejectionReason.NotYourTurn, s.Execute(new EndTurnCommand { Actor = s.Player }).Rejection);
        Assert.Equal(RejectionReason.NotYourTurn, s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(0, 0) }).Rejection);
    }

    // ---------------------------------------------------------------- movement

    /// <summary>The foe is 6 squares away; the player opens with a javelin and then has its movement.</summary>
    private static SimulationSession JavelinOpening() =>
        FirstSeed(seed => { var x = Yard(seed, foeAt: (8, 2)); Assert.True(Attack(x, Foe, Javelin).Success); return x; },
            x => x.GetPlayerView().Combat is { IsYourTurn: true });

    [Fact]
    public void Moving_in_a_fight_is_instant_spends_the_turns_movement_and_can_be_split()
    {
        var s = JavelinOpening();
        var now = s.Now;
        var from = s.GetPlayerView().Position!.Value; // (2,2)

        var away = new GridPos(2, 5);
        Assert.True(s.Execute(new MoveCommand { Actor = s.Player, To = away }).Success);
        Assert.Equal((away, now), (s.GetPlayerView().Position!.Value, s.Now));
        Assert.Equal(15, Fight(s).You.MovementLeftFeet);
        Assert.All(Fight(s).Reachable, r => Assert.True(r.CostFeet <= 15));

        Assert.True(s.Execute(new MoveCommand { Actor = s.Player, To = from }).Success);
        Assert.Equal(0, Fight(s).You.MovementLeftFeet);
        Assert.Empty(Fight(s).Reachable);
        Assert.Equal(RejectionReason.NoMovementLeft, s.Execute(new MoveCommand { Actor = s.Player, To = away }).Rejection);
    }

    [Fact]
    public void A_move_beyond_the_movement_left_is_refused_and_nobody_ends_on_another_creature()
    {
        var s = JavelinOpening();
        var foe = Fight(s).Combatants.Single().Position;

        Assert.Equal(RejectionReason.TooFar, s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(9, 5) }).Rejection);
        Assert.Equal(RejectionReason.Unreachable, s.Execute(new MoveCommand { Actor = s.Player, To = foe }).Rejection);
        Assert.DoesNotContain(Fight(s).Reachable, r => r.At == foe);
    }

    [Fact]
    public void Walking_out_of_an_enemys_reach_provokes_an_opportunity_attack_shown_in_the_preview()
    {
        var s = FirstSeed(seed => { var x = Yard(seed); Attack(x, Foe); return x; },
            x => x.GetPlayerView().Combat is { IsYourTurn: true } && x.GetPlayerView().Position == new GridPos(2, 2));

        var stay = s.PreviewMove(s.Player, new GridPos(2, 1)); // still next to the foe at (3,2)
        Assert.True(stay.Valid);
        Assert.Empty(stay.Provokes);
        var leave = s.PreviewMove(s.Player, new GridPos(0, 2));
        Assert.Equal((0, Foe), (leave.Provokes.Single().Step, leave.Provokes.Single().By));
        Assert.Equal(new[] { 5, 10 }, leave.CostFeet);
        Assert.Equal(new GridPos(2, 2), s.GetPlayerView().Position); // the preview touched nothing

        s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(0, 2) });

        // The fight may have ended if the blow dropped the player: the world's facts keep it anyway.
        Assert.Single(s.GetWorldView().RecentFacts, f => f.Kind == "Attack" && f.Description.StartsWith("Avversario coglie l'occasione"));
        if (s.Engine.World.Encounters.Values.SingleOrDefault() is { } fight)
            Assert.True(fight.Order.Single(c => c.Actor == Foe).ReactionUsed);
    }

    [Fact]
    public void The_enemys_space_cannot_be_crossed_but_a_fallen_ones_can_at_double_cost()
    {
        // A corridor one square wide: player (1,0), foe (2,0), and beyond it (3,0).
        var s = FirstSeed(seed =>
        {
            var x = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
                .AddArea("a", "A").AddLocation("c", "Corridoio", "a")
                .AddMap("a", new[] { "CCCCCC" }, new Dictionary<char, string> { ['C'] = "c" })
                .AddActor("player", "Paladino", "c", isPlayer: true, at: (1, 0))
                .AddActor("foe", "Avversario", "c", combat: "guard", at: (2, 0))
                .Build());
            x.AdvanceTo(At(1, 12));
            Attack(x, Foe);
            return x;
        }, x => x.GetPlayerView().Combat is { IsYourTurn: true } && x.GetPlayerView().Position == new GridPos(1, 0));

        Assert.Equal(RejectionReason.Unreachable, s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(3, 0) }).Rejection);

        Of(s, Foe).Vitality = new Vitality(11, makesDeathSaves: false, current: 1);
        Of(s, Foe).Vitality.TakeDamage(1, knockOut: true); // lying knocked out in the corridor
        var across = s.PreviewMove(s.Player, new GridPos(3, 0));
        Assert.Equal(new[] { 10, 15 }, across.CostFeet); // its square is Difficult Terrain
    }

    // ---------------------------------------------------------------- saving, determinism

    [Fact]
    public void A_fight_saved_during_the_players_turn_goes_on_the_same_after_loading()
    {
        var s = FirstSeed(seed => { var x = Yard(seed, foe: "bandit", raider: true); Attack(x, Foe); return x; },
            x => x.GetPlayerView().Combat is { IsYourTurn: true });
        s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(2, 1) });
        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());

        foreach (var x in new[] { s, loaded })
        {
            x.Execute(new EndTurnCommand { Actor = x.Player });
            x.Advance(Duration.FromSeconds(6));
            if (x.GetPlayerView().Combat is { IsYourTurn: true })
                Attack(x, Foe);
            x.Advance(Duration.FromMinutes(1));
        }

        Assert.Equal(s.SaveToString(), loaded.SaveToString());
    }

    [Fact]
    public void A_fight_between_npcs_runs_on_its_own_and_one_advance_equals_many()
    {
        SimulationSession Scene() => Yard(foe: "bandit", raider: true, foeAt: (6, 4),
            more: b => b.AddActor("watchman", "Guardiano", "yard", combat: "guard", at: (7, 4)));
        var once = Scene();
        var stepped = Scene();
        Assert.True(Attack(once, Foe, "Lancia", by: Watchman).Success);
        Assert.True(Attack(stepped, Foe, "Lancia", by: Watchman).Success);

        once.Advance(Duration.FromMinutes(5));
        for (var i = 0; i < 300; i++)
            stepped.Advance(Duration.FromSeconds(1));

        Assert.Equal(once.SaveToString(), stepped.SaveToString());
        Assert.Empty(once.Engine.World.Encounters); // monsters die at 0: it ended
        Assert.Contains(new[] { Foe, Watchman }, id => Of(once, id).Vitality.State == LifeState.Dead);
    }

    // ---------------------------------------------------------------- falling

    [Fact]
    public void A_knocked_out_foe_ends_the_fight_lies_an_hour_and_gets_up()
    {
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed);
            Of(x, Foe).Vitality = new Vitality(11, makesDeathSaves: false, current: 1);
            Attack(x, Foe, nonLethal: true);
            return x;
        }, x => Of(x, Foe).Vitality.KnockedOut);

        Assert.Null(s.GetPlayerView().Combat);
        Assert.Equal("tramortito", s.GetPlayerView().VisibleActors.Single().Condition);
        Assert.Equal("Downed", s.GetWorldView().Actor(Foe).Action!.Kind);

        s.Advance(Duration.FromHours(1));

        Assert.True(Of(s, Foe).Conscious);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "Recovered");
    }

    [Fact]
    public void A_foe_killed_lies_dead_and_never_acts_again()
    {
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed);
            Of(x, Foe).Vitality = new Vitality(11, makesDeathSaves: false, current: 1);
            Attack(x, Foe);
            return x;
        }, x => Of(x, Foe).Vitality.State == LifeState.Dead);

        s.Advance(Duration.FromHours(3));

        Assert.Null(s.GetWorldView().Actor(Foe).Action);
        Assert.Equal("morto", s.GetPlayerView().VisibleActors.Single().Condition);
        Assert.Equal(RejectionReason.AlreadyDone, Attack(s, Foe).Rejection);
    }

    [Fact]
    public void A_player_dropped_outside_a_fight_makes_a_death_save_every_six_seconds_until_it_is_settled()
    {
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed, foe: "bandit", raider: true);
            Of(x, x.Player).Vitality = new Vitality(11, makesDeathSaves: true, current: 1);
            Attack(x, x.Player, "Scimitarra", by: Foe);
            return x;
        }, x => Of(x, x.Player).Vitality.State == LifeState.Dying && x.GetPlayerView().Combat is null);

        Assert.Equal(("Downed", "Morente"), (s.GetPlayerView().Action!.Kind, s.GetPlayerView().Action!.Description));
        Assert.Equal(RejectionReason.ActorBusy, s.Execute(new WaitCommand { Actor = s.Player, Duration = Duration.FromMinutes(1) }).Rejection);

        s.Advance(Duration.FromSeconds(6));
        Assert.Single(s.GetWorldView().RecentFacts, f => f.Kind == "DeathSave");

        s.Advance(Duration.FromSeconds(30)); // at most five saves settle it
        var state = Of(s, s.Player).Vitality.State;
        Assert.NotEqual(LifeState.Dying, state);
        Assert.Equal(state == LifeState.Dead, s.GetPlayerView().Dead);
    }

    [Fact]
    public void Fallen_actors_see_nothing()
    {
        var s = Yard();
        Of(s, Foe).Vitality = new Vitality(11, makesDeathSaves: false, current: 1);
        Of(s, Foe).Vitality.TakeDamage(1, knockOut: true);

        Assert.False(s.Engine.SeesActor(Of(s, Foe), Of(s, s.Player)));
        Assert.True(s.Engine.SeesActor(Of(s, s.Player), Of(s, Foe)));
    }

    // ---------------------------------------------------------------- what the player sees

    [Fact]
    public void Targets_say_what_can_be_attacked_now_and_how()
    {
        var s = FirstSeed(seed => { var x = Yard(seed, foe: "bandit", raider: true); Attack(x, x.Player, "Scimitarra", by: Foe); return x; },
            x => x.GetPlayerView().Combat is { IsYourTurn: true, You.ActionAvailable: true });

        var targets = Fight(s).Targets;
        var sword = targets.Single(t => t.Attack == Sword);
        Assert.True(sword.Valid);
        Assert.False(sword.Disadvantage);
        var javelin = targets.Single(t => t.Attack == Javelin);
        Assert.True(javelin.Valid);
        Assert.True(javelin.Disadvantage); // a ranged attack with an enemy next to you
        Assert.Equal("illeso", Fight(s).Combatants.Single().Condition);
    }

    [Fact]
    public void An_attacker_the_player_cannot_see_is_only_someone()
    {
        // 01:00: a tiny lamp on the player's own square; the raider, next to it in the dark, strikes.
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed, foe: "bandit", raider: true, hour: 1, more: b => b.AddLight("a", (2, 2), brightFeet: 0, dimFeet: 0));
            Assert.True(Attack(x, x.Player, "Scimitarra", by: Foe).Success);
            return x;
        }, x => x.GetPlayerView().Combat is not null);

        var fight = Fight(s);
        Assert.Contains(fight.Order, o => o.Actor is null && o.Name == "Qualcuno" && o.Total is null);
        var blow = fight.Log.First(l => l.Kind == CombatLogKind.Attack);
        Assert.Null(blow.Actor);
        Assert.StartsWith("Qualcuno attacca Paladino", blow.Text);
        Assert.Empty(fight.Combatants);
        // Nothing in the player's events names the raider either.
        Assert.DoesNotContain(s.GetPlayerView().RecentEvents, f => f.Description.Contains("Avversario"));
    }

    [Fact]
    public void A_guard_with_a_claim_on_the_player_expects_its_attack()
    {
        var s = Yard();
        var theft = s.Engine.World.RecordFact("FoodStolen", "Furto di prova.");
        Of(s, Foe).Claims.Add(new Claim { Thief = s.Player, Store = new StoreId("campstore"), Theft = theft, Owed = 2 });

        Assert.True(Attack(s, Foe).Success);

        Assert.Contains(s.Engine.World.Encounters.Values.Single().Log, l => l.Kind == CombatLogKind.OpeningAttackPending);
    }

    [Fact]
    public void A_fight_ends_after_a_whole_round_with_no_attack_and_no_enemy_in_sight()
    {
        // A wall splits the yard in two; the player is taken to the other side, out of reach and out of sight.
        var s = FirstSeed(seed =>
        {
            var x = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
                .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
                .AddMap("a", Enumerable.Repeat("YYYYY#YYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
                .AddActor("player", "Paladino", "yard", isPlayer: true, at: (2, 2))
                .AddActor("foe", "Avversario", "yard", combat: "guard", at: (3, 2))
                .Build());
            x.AdvanceTo(At(1, 12));
            Attack(x, Foe);
            return x;
        }, x => x.GetPlayerView().Combat is { IsYourTurn: true });
        Of(s, s.Player).Position = new GridPos(8, 2);
        s.Execute(new EndTurnCommand { Actor = s.Player });

        s.Advance(Duration.FromSeconds(6)); // a round in which the foe may still have struck before the player left
        s.Execute(new EndTurnCommand { Actor = s.Player });
        s.Advance(Duration.FromSeconds(6));

        Assert.Null(s.GetPlayerView().Combat);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "CombatEnded" && f.Description.Contains("Nessuno vede"));
    }

    [Fact]
    public void A_save_with_a_fight_waiting_on_someone_else_or_impossible_hit_points_is_refused()
    {
        var s = FirstSeed(seed => { var x = Yard(seed); Attack(x, Foe); return x; }, x => x.GetPlayerView().Combat is { IsYourTurn: true });
        var json = System.Text.Json.Nodes.JsonNode.Parse(s.SaveToString())!;
        var fight = json["Encounters"]![0]!;
        var foeIndex = fight["Combatants"]!.AsArray().Select((c, i) => (c, i)).Single(x => (string)x.c!["Actor"]! == "foe").i;
        fight["Turn"] = foeIndex;
        Assert.Contains("fermo sul turno", LoadFromString(json.ToJsonString()).Error);

        json = System.Text.Json.Nodes.JsonNode.Parse(s.SaveToString())!;
        var vitality = json["Actors"]!.AsArray().Single(a => (string)a!["Id"]! == "foe")!["Vitality"]!;
        vitality["State"] = "Dying";
        vitality["Current"] = 0;
        Assert.Contains("punti ferita di 'foe'", LoadFromString(json.ToJsonString()).Error);
    }

    /// <summary>Night: a lamp lights the squares around (2,2); a villager the player cannot see rests at (2,4), in the dark.</summary>
    private static SimulationSession DarkYardWithLurker() =>
        FirstSeed(seed =>
        {
            var x = Yard(seed, hour: 1, more: b => b
                .AddLight("a", (2, 2), brightFeet: 5, dimFeet: 0)
                .AddActor("lurker", "Ombra", "yard", at: (2, 4)));
            Assert.True(Attack(x, Foe).Success);
            return x;
        }, x => x.GetPlayerView().Combat is { IsYourTurn: true } && x.GetPlayerView().Position == new GridPos(2, 2));

    [Fact]
    public void Green_squares_and_the_preview_never_reveal_a_creature_the_player_cannot_see()
    {
        var s = DarkYardWithLurker();
        Assert.False(s.Engine.SeesActor(Of(s, s.Player), Of(s, new ActorId("lurker"))));

        Assert.Contains(Fight(s).Reachable, r => r.At == new GridPos(2, 4));
        var preview = s.PreviewMove(s.Player, new GridPos(2, 5));
        Assert.True(preview.Valid);
        Assert.Equal(new[] { new GridPos(2, 3), new GridPos(2, 4), new GridPos(2, 5) }, preview.Path);
    }

    [Fact]
    public void The_real_move_stops_before_a_creature_the_player_did_not_see()
    {
        var s = DarkYardWithLurker();

        var move = s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(2, 5) });

        Assert.True(move.Success);
        Assert.Equal(new GridPos(2, 3), s.GetPlayerView().Position);
        Assert.Contains("sbarra la strada", move.Message);
        Assert.Equal(25, Fight(s).You.MovementLeftFeet); // only the square walked is spent
    }

    [Fact]
    public void An_ally_out_of_sight_is_named_in_the_order_but_its_square_is_not_shown()
    {
        var s = DarkYardWithLurker();
        // A companion fighting on the player's side, far off in the dark (companions come later: set by hand).
        var fight = s.Engine.World.Encounters.Values.Single();
        var side = fight.Order.Single(c => c.Actor == s.Player).Side;
        fight.Order.Add(new Combatant { Actor = new ActorId("lurker"), Side = side, Initiative = 0, InitiativeBonus = 0 });

        var view = Fight(s);
        Assert.Contains(view.Order, o => o.Actor == new ActorId("lurker") && o.Name == "Ombra");
        Assert.DoesNotContain(view.Combatants, c => c.Id == new ActorId("lurker"));
        Assert.Contains(view.Reachable, r => r.At == new GridPos(2, 4)); // its square does not give it away either
    }

    [Fact]
    public void A_move_ending_on_an_unseen_ally_steps_back_and_the_log_shows_every_square_entered()
    {
        var s = DarkYardWithLurker();
        var fight = s.Engine.World.Encounters.Values.Single();
        fight.Order.Add(new Combatant { Actor = new ActorId("lurker"), Side = fight.Order.Single(c => c.Actor == s.Player).Side,
            Initiative = 0, InitiativeBonus = 0 });

        var move = s.Execute(new MoveCommand { Actor = s.Player, To = new GridPos(2, 4) });

        Assert.Contains("torna indietro", move.Message);
        Assert.Equal(new GridPos(2, 3), s.GetPlayerView().Position);
        Assert.Equal(20, Fight(s).You.MovementLeftFeet); // both steps forward were paid
        Assert.Equal(new[] { new GridPos(2, 3), new GridPos(2, 4), new GridPos(2, 3) },
            Fight(s).Log.Last(l => l.Kind == CombatLogKind.Move).Path);
    }

    [Fact]
    public void The_village_profiles_are_the_srd_stat_blocks()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());

        Assert.Equal(("paladin", 11), (Of(s, s.Player).Combat, Of(s, s.Player).Vitality.Maximum));
        Assert.Equal(("guard", 11), (Of(s, Guard).Combat, Of(s, Guard).Vitality.Maximum));
        Assert.Equal(("commoner", 4), (Of(s, Farmer).Combat, Of(s, Farmer).Vitality.Maximum));
        Assert.Equal(("bandit", 11), (Of(s, Raider).Combat, Of(s, Raider).Vitality.Maximum));
    }

    [Fact]
    public void The_player_knows_its_own_attacks_outside_a_fight()
    {
        var attacks = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage()).GetPlayerView().Attacks;

        Assert.Equal(new[] { Sword, Javelin, "Colpo senz'armi" }, attacks.Select(a => a.Name));
        Assert.Equal((true, 5, (int?)null), (attacks[0].Melee, attacks[0].ReachFeet, attacks[0].NormalRange));
        Assert.Equal((false, (int?)30, (int?)120), (attacks[1].Melee, attacks[1].NormalRange, attacks[1].LongRange));
    }
}
