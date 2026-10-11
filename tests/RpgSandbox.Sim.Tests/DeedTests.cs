using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>C4a: deeds of any kind (a fight, a killing) are seen, heard, remembered and told like thefts.</summary>
public class DeedTests
{
    private static readonly ActorId Miller = new("miller");
    private static readonly ActorId Farmer = new("farmer");
    private static readonly ActorId Guard = new("guard");
    private static readonly ActorId Raider = new("raider");

    /// <summary>
    /// Noon. A yard (x 0-4) and a guardhouse (x 6-9) split by a wall with one gap at (5,3). In the yard the player (2,2)
    /// and the miller (3,2), a villager; the farmer, a villager, watches from (1,4). The guard, the village authority,
    /// is in the guardhouse at (8,0): it sees nothing of the yard, but it is within earshot through the gap.
    /// </summary>
    private static SimulationSession Village(ulong seed = 1, Action<ScenarioBuilder>? more = null)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "Villaggio")
            .AddLocation("yard", "Cortile", "a").AddLocation("house", "Corpo di guardia", "a")
            .AddMap("a", new[] { "YYYYY#GGGG", "YYYYY#GGGG", "YYYYY#GGGG", "YYYYYYGGGG", "YYYYY#GGGG", "YYYYY#GGGG" },
                new Dictionary<char, string> { ['Y'] = "yard", ['G'] = "house" })
            .AddFaction("village", "Villaggio", authorityId: "guard")
            .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: (2, 2))
            .AddActor("miller", "Mugnaio", "yard", factionId: "village", combat: "guard", at: (3, 2))
            .AddActor("farmer", "Contadino", "yard", factionId: "village", at: (1, 4))
            .AddActor("guard", "Guardia", "house", factionId: "village", combat: "guard", at: (8, 0));
        more?.Invoke(b);
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, 12));
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static IEnumerable<Observation> Deeds(SimulationSession s, ActorId who, DeedKind kind) =>
        Of(s, who).Knowledge.Where(o => o.Kind == kind);

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

    private static CommandResult Strike(SimulationSession s, ActorId target, ActorId? by = null, string attack = "Spada lunga") =>
        s.Execute(new AttackCommand { Actor = by ?? s.Player, Target = target, Attack = attack });

    [Fact]
    public void Who_sees_a_fight_start_knows_who_started_it_and_against_whom()
    {
        var s = Village();
        Assert.True(Strike(s, Miller).Success);

        var seen = Deeds(s, Farmer, DeedKind.Assault).Single();
        Assert.Equal((PerceptionMode.Seen, (ActorId?)s.Player, (ActorId?)Miller), (seen.Perceived, seen.Culprit, seen.Victim));
        Assert.Equal(new[] { s.Player, Miller }, seen.Involved.Select(i => i.Id));
        Assert.Equal(new GridPos(3, 2), seen.Square);
        Assert.Equal(new LocationId("yard"), seen.Location);
    }

    [Fact]
    public void Who_starts_watching_later_sees_who_fights_but_not_who_started()
    {
        var s = FirstSeed(seed =>
        {
            var x = Village(seed);
            Of(x, Farmer).Vitality = new Vitality(4, makesDeathSaves: false, current: 1);
            Of(x, Farmer).Vitality.TakeDamage(1, knockOut: true); // out cold when the fight starts...
            Strike(x, Miller);
            return x;
        }, x => x.GetPlayerView().Combat is { IsYourTurn: true });
        Assert.Empty(Deeds(s, Farmer, DeedKind.Assault));
        Of(s, Farmer).Vitality.Wake(); // ...and back on its feet for the miller's blow

        s.Execute(new EndTurnCommand { Actor = s.Player });
        s.Advance(Duration.FromSeconds(6));

        var late = Deeds(s, Farmer, DeedKind.Assault).Single();
        Assert.Null(late.Culprit);
        Assert.Null(late.Victim);
        Assert.Equal(new[] { Miller, s.Player }.Order().ToArray(), late.Involved.Select(i => i.Id).Order().ToArray());
    }

    [Fact]
    public void Who_only_hears_a_fight_knows_roughly_where_and_no_names()
    {
        var s = Village();
        Assert.True(Strike(s, Miller).Success);

        var heard = Deeds(s, Guard, DeedKind.Assault).Single();
        Assert.Equal(PerceptionMode.Heard, heard.Perceived);
        Assert.Equal(new GridPos(2, 2), heard.Square); // the centre of the 5×5 block the fight at (3,2) is in
        Assert.Null(heard.Culprit);
        Assert.Empty(heard.Involved);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "FightHeard" && f.Description.StartsWith("Guardia sente"));
    }

    [Fact]
    public void The_player_hears_a_fight_it_cannot_see_and_is_told_so()
    {
        // The player stands in the guardhouse; the miller and the farmer come to blows in the yard.
        var s = Village(more: b => b.AddActor("brawler", "Attaccabrighe", "yard", combat: "guard", at: (2, 1)));
        Of(s, s.Player).Position = new GridPos(7, 1);
        Of(s, s.Player).Location = new LocationId("house");
        Assert.True(Strike(s, Miller, by: new ActorId("brawler"), attack: "Lancia").Success);

        var heard = s.GetPlayerView().Observations.Single(o => o.Kind == "Assault");
        Assert.Equal(("Heard", (ActorId?)null), (heard.Perceived, heard.Culprit));
        Assert.Contains(s.GetPlayerView().RecentEvents, f => f.Kind == "FightHeard");
    }

    [Fact]
    public void A_killing_is_a_deed_of_its_own_and_its_author_is_not_necessarily_who_started()
    {
        // The raider jumps the miller; the miller strikes it dead. The farmer sees it all.
        var s = FirstSeed(seed =>
        {
            var x = Village(seed, b => b
                .AddRaidingFactionFor("bandits")
                .AddActor("raider", "Razziatore", "yard", factionId: "bandits", combat: "bandit", at: (4, 2)));
            Of(x, Raider).Vitality = new Vitality(11, makesDeathSaves: false, current: 1);
            Assert.True(Strike(x, Miller, by: Raider, attack: "Scimitarra").Success);
            x.Advance(Duration.FromMinutes(1));
            return x;
        }, x => Of(x, Raider).Vitality.State == LifeState.Dead);

        var fight = Deeds(s, Farmer, DeedKind.Assault).Single();
        var killing = Deeds(s, Farmer, DeedKind.Killing).Single();
        Assert.Equal(((ActorId?)Raider, (ActorId?)Miller), (fight.Culprit, fight.Victim));
        Assert.Equal(((ActorId?)Miller, (ActorId?)Raider), (killing.Culprit, killing.Victim));
        Assert.Equal(fight.Encounter, killing.Encounter);
        Assert.NotEqual(fight.Origin, killing.Origin);
    }

    [Fact]
    public void A_villager_tells_the_guard_about_a_fight_it_saw()
    {
        // The player kills the miller (1 HP left) in front of the farmer, who then goes to tell the guard.
        var s = FirstSeed(seed =>
        {
            var x = Village(seed);
            Of(x, Miller).Vitality = new Vitality(11, makesDeathSaves: false, current: 1);
            Strike(x, Miller);
            return x;
        }, x => Of(x, Miller).Vitality.State == LifeState.Dead);

        s.Advance(Duration.FromMinutes(10));

        var told = Of(s, Guard).Knowledge.Single(o => o.Kind == DeedKind.Assault && o.Source == Farmer);
        Assert.Equal(((ActorId?)s.Player, (ActorId?)Miller, PerceptionMode.Seen), (told.Culprit, told.Victim, told.Perceived));
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "InformationShared" && f.Description.StartsWith("Contadino racconta a Guardia dello scontro"));
    }

    [Fact]
    public void Deeds_of_fights_survive_save_and_load()
    {
        var s = Village();
        Strike(s, Miller);

        var loaded = s.SaveAndReload();

        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(s.GetPlayerView().Observations, loaded.GetPlayerView().Observations);
    }

    [Fact]
    public void The_player_reads_a_fight_it_saw_in_its_observations()
    {
        var s = Village(more: b => b.AddActor("brawler", "Attaccabrighe", "yard", combat: "guard", at: (4, 2)));

        Assert.True(Strike(s, Miller, by: new ActorId("brawler"), attack: "Lancia").Success);

        var o = s.GetPlayerView().Observations.Single(x => x.Kind == "Assault");
        Assert.Equal(("Attaccabrighe", "Mugnaio", (int?)null, (StoreId?)null), (o.CulpritName, o.VictimName, o.Amount, o.Store));
        Assert.Equal(new[] { "Attaccabrighe", "Mugnaio" }, o.InvolvedNames);
        Assert.Null(o.Thief); // the deprecated alias is for thefts only
    }
}

internal static class DeedScenarioExtensions
{
    /// <summary>A raiding faction with a camp in the woods (no map), for a raider on the village map.</summary>
    public static ScenarioBuilder AddRaidingFactionFor(this ScenarioBuilder b, string id) => b
        .AddArea("woods", "Bosco").AddLocation("camp", "Campo", "woods")
        .AddStore("campstore", "Scorte del campo", "camp", 100, ownerFactionId: id)
        .AddRaidingFaction(id, "Banditi", "campstore", dailyUpkeep: 1, upkeepTimeOfDay: Duration.FromHours(8),
            foodThreshold: 0, raidAmount: 1, evaluationInterval: Duration.FromHours(24));
}
