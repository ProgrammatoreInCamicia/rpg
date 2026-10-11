using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>C4c: who joins a fight, who goes to look, who keeps away or runs.</summary>
public class ReactionTests
{
    private static readonly ActorId Miller = new("miller");
    private static readonly ActorId Farmer = new("farmer");
    private static readonly ActorId Guard = new("guard");
    private static readonly ActorId Raider = new("raider");
    private static readonly ActorId Mate = new("mate");

    /// <summary>
    /// Noon. A 16×6 map: the yard (x 0-9), open ground, and the farmer's house (x 12-15). The player (2,2) and the miller
    /// (3,2, a villager with the guard's profile); the guard, village authority, at (8,4) sees everything. The farmer
    /// lives in the house and works in the yard from 8 to 18. The village is at war with the bandits.
    /// </summary>
    private static SimulationSession Yard(ulong seed = 1, Action<ScenarioBuilder>? more = null, bool withGuard = true)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "Villaggio").AddLocation("yard", "Cortile", "a").AddLocation("house", "Casa", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYY..HHHH", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard", ['H'] = "house" })
            .AddFaction("village", "Villaggio", authorityId: withGuard ? "guard" : null)
            .AddRaidingFactionFor("bandits")
            .AddEnmity("village", "bandits")
            .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: (2, 2))
            .AddActor("miller", "Mugnaio", "yard", factionId: "village", combat: "guard", at: (3, 2))
            .AddActor("farmer", "Contadino", "house", factionId: "village", workLocationId: "yard",
                shiftStart: Duration.FromHours(8), shiftEnd: Duration.FromHours(18), at: (13, 2));
        if (withGuard)
            b.AddActor("guard", "Guardia", "yard", factionId: "village", combat: "guard", at: (8, 4));
        more?.Invoke(b);
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, 12));
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static Encounter Fight(SimulationSession s) => s.Engine.World.Encounters.Values.Single();

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

    private static bool NextRound(SimulationSession s)
    {
        if (s.GetPlayerView().Combat is not { IsYourTurn: true })
            return false;
        s.Execute(new EndTurnCommand { Actor = s.Player });
        s.Advance(Duration.FromSeconds(6));
        return s.GetPlayerView().Combat is { IsYourTurn: true };
    }

    [Fact]
    public void The_guard_who_saw_the_player_attack_the_miller_joins_on_the_millers_side_from_the_next_round()
    {
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed);
            x.Execute(new AttackCommand { Actor = x.Player, Target = Miller, Attack = "Spada lunga" });
            return x;
        }, x => NextRound(x) && Fight(x).Order.FindIndex(c => c.Actor == Guard) > Fight(x).Order.FindIndex(c => c.Actor == x.Player));

        var fight = Fight(s);
        var guard = fight.Order.Single(c => c.Actor == Guard);
        Assert.Equal(fight.Order.Single(c => c.Actor == Miller).Side, guard.Side);
        Assert.Equal(s.Player, fight.Order[fight.Turn].Actor); // joining did not move the turn in progress
        var joined = fight.Log.Single(l => l.Kind == CombatLogKind.Joined);

        // The rest of the round is played: the guard comes after the player in the order, yet it does not act yet.
        s.Execute(new EndTurnCommand { Actor = s.Player });
        Assert.DoesNotContain(fight.Log, l => l.Kind == CombatLogKind.TurnStarted && l.Actor == Guard && l.Round <= joined.Round);

        s.Advance(Duration.FromSeconds(6)); // the next round: the player first (here), then the guard's first turn
        s.Execute(new EndTurnCommand { Actor = s.Player });
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "Attack" && f.Description.StartsWith("Guardia attacca Paladino"));
    }

    [Fact]
    public void A_bandit_who_sees_a_fellow_attacked_joins_on_its_side()
    {
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed, b => b
                .AddActor("raider", "Razziatore", "yard", factionId: "bandits", combat: "bandit", at: (3, 3))
                .AddActor("mate", "Compare", "yard", factionId: "bandits", combat: "bandit", at: (6, 0)), withGuard: false);
            x.Execute(new AttackCommand { Actor = x.Player, Target = Raider, Attack = "Spada lunga" });
            return x;
        }, x => NextRound(x));

        Assert.Equal(Fight(s).Order.Single(c => c.Actor == Raider).Side, Fight(s).Order.Single(c => c.Actor == Mate).Side);
    }

    [Fact]
    public void Nobody_joins_a_fight_between_two_it_holds_hostile()
    {
        // The player is wanted; a raider jumps him in front of the guard: let them fight.
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed, b => b.AddActor("raider", "Razziatore", "yard", factionId: "bandits", combat: "bandit", at: (2, 3)));
            x.Engine.World.Factions[new FactionId("village")].Wanted[x.Player] = x.Engine.World.RecordFact("Assault", "Prova.");
            x.Execute(new AttackCommand { Actor = Raider, Target = x.Player, Attack = "Scimitarra" });
            return x;
        }, x => x.GetPlayerView().Combat is { IsYourTurn: true } && NextRound(x));

        Assert.DoesNotContain(Fight(s).Order, c => c.Actor == Guard || c.Actor == Miller);
    }

    [Fact]
    public void A_farmer_at_work_near_a_fight_goes_home()
    {
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed, withGuard: false);
            Assert.Equal(new LocationId("yard"), Of(x, Farmer).Location); // at work, a few steps from the miller
            x.Execute(new AttackCommand { Actor = x.Player, Target = Miller, Attack = "Spada lunga" });
            return x;
        }, x => NextRound(x) && NextRound(x) && NextRound(x) && NextRound(x)); // four rounds of fighting (24 s)
        // He is home, and stays there while the fight goes on.

        Assert.Equal(new LocationId("house"), Of(s, Farmer).Location);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "FightWitnessed" && f.Description.StartsWith("Contadino"));
    }

    [Fact]
    public void A_commoner_badly_hurt_in_a_fight_runs_and_once_out_of_sight_is_out_of_it()
    {
        // A wall with a gap: the raider jumps a villager at 1 HP who has somewhere to hide.
        var s = FirstSeed(seed =>
        {
            var x = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
                .AddArea("a", "A").AddLocation("yard", "Cortile", "a").AddLocation("barn", "Fienile", "a")
                .AddMap("a", new[] { "YYYYY#BBBB", "YYYYY#BBBB", "YYYYY#BBBB", "YYYYYYBBBB", "YYYYY#BBBB", "YYYYY#BBBB" },
                    new Dictionary<char, string> { ['Y'] = "yard", ['B'] = "barn" })
                .AddFaction("village", "Villaggio")
                .AddRaidingFactionFor("bandits")
                .AddEnmity("village", "bandits")
                .AddActor("player", "Paladino", "barn", isPlayer: true, at: (9, 5))
                .AddActor("farmer", "Contadino", "yard", factionId: "village", at: (4, 3))
                .AddActor("raider", "Razziatore", "yard", factionId: "bandits", combat: "bandit", at: (2, 2))
                .Build());
            x.AdvanceTo(At(1, 12));
            Of(x, Farmer).Vitality = new Vitality(4, makesDeathSaves: false, current: 1);
            x.Execute(new AttackCommand { Actor = Raider, Target = Farmer, Attack = "Balestra leggera" });
            x.Advance(Duration.FromMinutes(1));
            return x;
        }, x => x.GetWorldView().RecentFacts.Any(f => f.Kind == "Escaped"));

        Assert.True(Of(s, Farmer).Conscious);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "Fled" && f.Description.StartsWith("Contadino"));
        Assert.Empty(s.Engine.World.Encounters); // with nobody left on its side, the fight is over
    }

    [Fact]
    public void An_authority_that_only_hears_a_fight_goes_to_look()
    {
        // The guardhouse behind a wall: the guard hears the player and the miller come to blows in the yard.
        var s = FirstSeed(seed =>
        {
            var x = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
                .AddArea("a", "A").AddLocation("yard", "Cortile", "a").AddLocation("house", "Corpo di guardia", "a")
                .AddMap("a", new[] { "YYYYY#GGGG", "YYYYY#GGGG", "YYYYY#GGGG", "YYYYYYGGGG", "YYYYY#GGGG", "YYYYY#GGGG" },
                    new Dictionary<char, string> { ['Y'] = "yard", ['G'] = "house" })
                .AddFaction("village", "Villaggio", authorityId: "guard")
                .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: (2, 2))
                .AddActor("miller", "Mugnaio", "yard", factionId: "village", combat: "guard", at: (3, 2))
                .AddActor("guard", "Guardia", "house", factionId: "village", combat: "guard", at: (8, 0))
                .Build());
            x.AdvanceTo(At(1, 12));
            x.Execute(new AttackCommand { Actor = x.Player, Target = Miller, Attack = "Spada lunga" });
            return x;
        }, x => NextRound(x));

        Assert.Equal("Va a vedere", Of(s, Guard).LastDecision!.Rule);
        Assert.NotNull(s.GetWorldView().Actor(Guard).Move); // on its way (the world waits for the player's turn)
    }

    [Fact]
    public void A_fight_with_a_newcomer_survives_save_and_load()
    {
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed);
            x.Execute(new AttackCommand { Actor = x.Player, Target = Miller, Attack = "Spada lunga" });
            return x;
        }, x => NextRound(x));
        Assert.Contains(Fight(s).Order, c => c.JoinedInRound is not null);

        var loaded = s.SaveAndReload();

        Assert.Equal(s.SaveToString(), loaded.SaveToString());
    }
}
