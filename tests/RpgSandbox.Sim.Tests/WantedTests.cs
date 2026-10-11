using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>C4b: hostility as a relation between factions and people; the wanted.</summary>
public class WantedTests
{
    private static readonly ActorId Miller = new("miller");
    private static readonly ActorId Farmer = new("farmer");
    private static readonly ActorId Guard = new("guard");
    private static readonly ActorId Raider = new("raider");
    private static readonly FactionId Village = new("village");

    /// <summary>
    /// Noon, one open yard. The player (2,2), the miller (3,2) and the farmer (1,4) are villagers but the player; the
    /// guard, the village authority, stands at (8,4) and sees everything. The village is at war with the bandits.
    /// </summary>
    private static SimulationSession Yard(ulong seed = 1, Action<ScenarioBuilder>? more = null)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "Villaggio").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddFaction("village", "Villaggio", authorityId: "guard")
            .AddRaidingFactionFor("bandits")
            .AddEnmity("village", "bandits")
            .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: (2, 2))
            .AddActor("miller", "Mugnaio", "yard", factionId: "village", combat: "guard", at: (3, 2))
            .AddActor("farmer", "Contadino", "yard", factionId: "village", at: (1, 4))
            .AddActor("guard", "Guardia", "yard", factionId: "village", combat: "guard", at: (8, 4));
        more?.Invoke(b);
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, 12));
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static Faction VillageOf(SimulationSession s) => s.Engine.World.Factions[Village];

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

    private static bool Hostile(SimulationSession s, ActorId who) => s.GetPlayerView().VisibleActors.Single(a => a.Id == who).Hostile;

    /// <summary>Lets the round close and the next begin; true if the fight is still on.</summary>
    private static bool AdvanceToYourTurn(SimulationSession s)
    {
        s.Advance(Duration.FromSeconds(6));
        return s.GetPlayerView().Combat is not null;
    }

    [Fact]
    public void Who_is_hostile_comes_from_the_factions_not_from_being_a_raider()
    {
        var s = Yard(more: b => b.AddActor("raider", "Razziatore", "yard", factionId: "bandits", combat: "bandit", at: (6, 0)));

        Assert.True(Hostile(s, Raider));  // bandits are hostile to every stranger
        Assert.False(Hostile(s, Farmer)); // the village is neutral to strangers
        Assert.True(s.Engine.HoldsHostile(Of(s, Guard), Of(s, Raider))); // the village is at war with the bandits
        Assert.True(s.Engine.HoldsHostile(Of(s, s.Player), Of(s, Raider))); // one expects trouble from those against it
    }

    [Fact]
    public void Whoever_starts_an_assault_on_a_villager_in_front_of_the_guard_is_wanted()
    {
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed);
            Of(x, Miller).Vitality = new Vitality(11, makesDeathSaves: false, current: 1);
            x.Execute(new AttackCommand { Actor = x.Player, Target = Miller, Attack = "Spada lunga" });
            return x;
        }, x => Of(x, Miller).Vitality.State == LifeState.Dead);
        // The guard saw it all: as soon as the fight is over it is free to judge.

        s.Advance(Duration.FromSeconds(6));

        Assert.True(VillageOf(s).Wanted.ContainsKey(s.Player));
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "Wanted" && f.Description.Contains("Paladino"));
        Assert.True(Hostile(s, Farmer)); // the villagers now treat the player as an enemy
    }

    [Fact]
    public void Who_kills_in_a_fight_it_did_not_start_is_not_guilty()
    {
        // The raider jumps the miller in front of the guard; the miller strikes it dead.
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed, b => b.AddActor("raider", "Razziatore", "yard", factionId: "bandits", combat: "bandit", at: (4, 2)));
            Of(x, Raider).Vitality = new Vitality(11, makesDeathSaves: false, current: 1);
            x.Execute(new AttackCommand { Actor = Raider, Target = Miller, Attack = "Scimitarra" });
            x.Advance(Duration.FromMinutes(1));
            return x;
        }, x => Of(x, Raider).Vitality.State == LifeState.Dead);

        Assert.DoesNotContain(Miller, VillageOf(s).Wanted.Keys);
        Assert.Contains(Of(s, Guard).Knowledge, o => o.Kind == DeedKind.Killing && o.Culprit == Miller);
    }

    [Fact]
    public void Hearing_a_fight_or_seeing_it_from_the_middle_makes_nobody_wanted()
    {
        // Nobody saw who started: the guard only joins its view after the first blows.
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed);
            Of(x, Guard).Vitality = new Vitality(11, makesDeathSaves: false, current: 1);
            Of(x, Guard).Vitality.TakeDamage(1, knockOut: true);
            Of(x, Farmer).Vitality = new Vitality(4, makesDeathSaves: false, current: 1);
            Of(x, Farmer).Vitality.TakeDamage(1, knockOut: true);
            x.Execute(new AttackCommand { Actor = x.Player, Target = Miller, Attack = "Spada lunga" });
            return x;
        }, x => x.GetPlayerView().Combat is { IsYourTurn: true }
                && x.Execute(new EndTurnCommand { Actor = x.Player }).Success && AdvanceToYourTurn(x) && x.GetPlayerView().Combat is { IsYourTurn: true });
        // The guard comes to just as the player strikes again: the first blow it sees is the player's, yet that does not
        // tell it who started.
        Of(s, Guard).Vitality.Wake();
        Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Miller, Attack = "Spada lunga" }).Success);
        s.Execute(new EndTurnCommand { Actor = s.Player }); // the world moves on between rounds: the guard can judge
        s.Advance(Duration.FromSeconds(6));

        var seen = Of(s, Guard).Knowledge.Single(o => o.Kind == DeedKind.Assault);
        Assert.Equal(((ActorId?)null, s.Player), (seen.Culprit, seen.Involved[0].Id));
        Assert.Empty(VillageOf(s).Wanted);
    }

    [Fact]
    public void The_guard_goes_for_a_wanted_man_on_sight_and_knocks_him_out_instead_of_killing_him()
    {
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed);
            var theft = x.Engine.World.RecordFact("Assault", "Aggressione di prova.");
            VillageOf(x).Wanted[x.Player] = theft;
            Of(x, x.Player).Vitality = new Vitality(11, makesDeathSaves: true, current: 1);
            // He takes a step in the guard's sight: it stops resting, walks up and strikes.
            x.Execute(new MoveCommand { Actor = x.Player, To = new GridPos(2, 3) });
            x.Advance(Duration.FromMinutes(5));
            return x;
        }, x => !Of(x, x.Player).Conscious);

        Assert.Equal((LifeState.Alive, true), (Of(s, s.Player).Vitality.State, Of(s, s.Player).Vitality.KnockedOut));
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "CombatStarted" && f.Description.StartsWith("Guardia attacca Paladino"));
        Assert.Empty(VillageOf(s).Wanted); // until there is a prison, that settles it
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "WantedSettled");
    }

    [Fact]
    public void Relations_and_the_wanted_survive_save_and_load()
    {
        var s = Yard();
        VillageOf(s).Wanted[s.Player] = s.Engine.World.RecordFact("Assault", "Aggressione di prova.");

        var loaded = s.SaveAndReload();

        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.True(loaded.Engine.World.Factions[Village].Wanted.ContainsKey(loaded.Player));
        Assert.Contains(new FactionId("bandits"), loaded.Engine.World.Factions[Village].Enemies);
    }
}
