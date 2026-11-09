using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Sim.Tests;

/// <summary>
/// Region Task 6A: a fighter of a faction that defends a place answers the hostile people it sees there, from sizing them
/// up. These cases build their own small camp: the island tests cover the Vasan landing itself.
/// </summary>
public class DefenseTests
{
    private static readonly ActorId Player = new("player");
    private static readonly ActorId Raider = new("raider");
    private static readonly StoreId Camp = new("camp");

    /// <summary>
    /// A 16×6 map at noon: a beach (x 0–7) and the bandits' camp (x 8–15) with their rations at (14,2), used from (13,2). The
    /// paladin on the beach, a village guard behind him if asked, and the raider at (12,4).
    /// </summary>
    private static SimulationSession CampSession(bool defends = true, bool withGuard = false, bool sightWall = false)
    {
        var b = new ScenarioBuilder().WithSeed(1)
            .AddArea("a", "Costa")
            .AddLocation("beach", "Spiaggia", "a", lit: true).AddLocation("camp", "Accampamento", "a", lit: true)
            .AddMap("a", Enumerable.Repeat("BBBBBBBBCCCCCCCC", 6).ToArray(), new Dictionary<char, string> { ['B'] = "beach", ['C'] = "camp" })
            .AddFaction("village", "Villaggio")
            .AddRaidingFaction("bandits", "Banditi", "camp", dailyUpkeep: 0, upkeepTimeOfDay: default,
                foodThreshold: 0, raidAmount: 1, evaluationInterval: Duration.FromHours(24))
            .AddEnmity("village", "bandits")
            .AddStore("camp", "Bottino", "camp", food: 4, ownerFactionId: "bandits", at: (14, 2), access: new[] { (13, 2) })
            .AddActor("player", "Paladino", "beach", isPlayer: true, factionId: "village", at: (1, 2))
            .AddActor("raider", "Razziatore", "camp", factionId: "bandits", build: "raider", at: (12, 4));
        if (withGuard)
            b.AddActor("guard", "Guardia", "beach", factionId: "village", build: "village-guard", at: (0, 3));
        if (sightWall)
            for (var y = 0; y < 6; y++)
                b.AddEdge("a", (9, y), (10, y), MapEdgeKind.Wall);
        if (defends)
            b.AddDefense("bandits", "camp");
        var session = SimulationSession.Create(b.Build());
        session.Advance(Duration.FromHours(12)); // in daylight (the world starts at midnight)
        return session;
    }

    private static List<FactView> Facts(SimulationSession s) => s.GetWorldView().RecentFacts.ToList();

    private static void Walk(SimulationSession s, ActorId who, (int X, int Y) to)
    {
        var result = s.Execute(new MoveToSpotCommand { Actor = who, To = Spot.CenterOf(new GridPos(to.X, to.Y)) });
        Assert.True(result.Success, result.Message);
        Assert.Equal(AdvanceOutcome.Completed, s.AdvanceUntilCompleted(result.Action!.Value, Duration.FromMinutes(5)).Outcome);
    }

    [Fact]
    public void Facing_two_it_cannot_beat_it_backs_away_and_only_watches_them_take_its_rations()
    {
        var s = CampSession(withGuard: true);
        // The paladin walks in first; the guard follows a step behind, and the raider sizes the two of them up again.
        var guard = s.Execute(new MoveToSpotCommand { Actor = new ActorId("guard"), To = Spot.CenterOf(new GridPos(12, 2)) });
        Assert.True(guard.Success, guard.Message);
        Walk(s, Player, (13, 2));
        s.Advance(Duration.FromSeconds(10));

        Assert.Contains(Facts(s), f => f.Kind == "DefenseWithdrawal" && f.Description == "Razziatore arretra davanti a Guardia, Paladino.");
        var take = s.Execute(new TakeFoodCommand { Actor = Player, Store = Camp, Amount = 4 });
        Assert.True(take.Success, take.Message);
        Assert.Equal(AdvanceOutcome.Completed, s.AdvanceUntilCompleted(take.Action!.Value, Duration.FromMinutes(10)).Outcome);
        s.Advance(Duration.FromMinutes(2));

        Assert.Equal(0, s.GetWorldView().Store(Camp).Food);
        Assert.Contains(Facts(s), f => f.Kind == "DefenseHelpless" && f.Description.StartsWith("Razziatore guarda ") && f.Description.EndsWith("Paladino prendere le provviste e non interviene."));
        Assert.DoesNotContain(Facts(s), f => f.Kind is "DefenseAttack" or "CombatStarted");
        var raider = s.GetWorldView().Actor(Raider);
        Assert.Equal("Tieni le distanze", raider.LastDecision!.Rule);
    }

    [Fact]
    public void An_intruder_in_the_defended_place_behind_a_wall_is_not_sized_up()
    {
        var s = CampSession(sightWall: true);
        Walk(s, Player, (8, 2)); // in the camp, with a continuous wall between player and raider
        s.Advance(Duration.FromMinutes(3));
        Assert.DoesNotContain(Facts(s), f => f.Kind == "SizedUp" || f.Kind.StartsWith("Defense"));
        Assert.Null(s.Engine.World.Actors[Raider].Defense);
    }

    [Fact]
    public void Only_the_defended_place_counts()
    {
        var s = CampSession();
        Walk(s, Player, (7, 2)); // the edge of the beach, in plain sight of the camp
        s.Advance(Duration.FromMinutes(10));
        Assert.DoesNotContain(Facts(s), f => f.Kind.StartsWith("Defense") || f.Kind == "SizedUp");

        Walk(s, Player, (8, 2)); // one step onto the camp
        s.Advance(Duration.FromSeconds(2));
        Assert.Contains(Facts(s), f => f.Kind == "SizedUp" && f.Description == "Razziatore squadra Paladino.");
    }

    [Fact]
    public void Without_a_defended_place_the_raider_lets_an_intruder_be()
    {
        var s = CampSession(defends: false);
        Walk(s, Player, (10, 2));
        s.Advance(Duration.FromMinutes(10));
        Assert.DoesNotContain(Facts(s), f => f.Kind.StartsWith("Defense") || f.Kind is "SizedUp" or "CombatStarted");
    }

    [Fact]
    public void Leaving_the_ground_ends_the_standoff()
    {
        var s = CampSession();
        Walk(s, Player, (9, 2));
        s.Advance(Duration.FromSeconds(2));
        Assert.Contains(Facts(s), f => f.Kind.StartsWith("Defense"));
        Walk(s, Player, (2, 2));
        s.Advance(Duration.FromMinutes(5));
        Assert.Null(s.Engine.World.Actors[Raider].Defense);
        Assert.DoesNotContain(Facts(s), f => f.Kind == "CombatStarted");
    }

    [Fact]
    public void The_defended_places_and_a_standoff_survive_a_save()
    {
        var s = CampSession();
        Walk(s, Player, (9, 2));
        s.Advance(Duration.FromSeconds(2));
        Assert.NotNull(s.Engine.World.Actors[Raider].Defense);
        var reloaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), reloaded.SaveToString());
        Assert.Contains(new LocationId("camp"), reloaded.Engine.World.Factions[new FactionId("bandits")].Defends);
        Assert.Equal(s.Engine.World.Actors[Raider].Defense!.Verdict, reloaded.Engine.World.Actors[Raider].Defense!.Verdict);
    }
}
