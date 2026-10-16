using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>P5a: a raider with no way on foot gets through the doors and windows that hold.</summary>
public class BreakInTests
{
    private static readonly ActorId Raider = new("raider");
    private static readonly StoreId Box = new("box");
    private static readonly GridPos Door = new(6, 2), WindowA = new(7, 0), WindowB = new(7, 1);

    /// <summary>
    /// A yard (10 by 6) with a house of squares x 6..8, y 1..3 inside thin walls: a door on its west wall between (5,2)
    /// and (6,2) (<paramref name="door"/>: its lock; null: a wall there) and a window on its north wall between (7,0) and
    /// (7,1) (<paramref name="window"/>: its lock; null: a wall there). A box of food at (8,2), used from (7,2). The
    /// raider at (1,2), on a raid for the box, its camp's store in the yard at (0,5).
    /// </summary>
    private static SimulationSession House(LockSpec? door, LockSpec? window = null, bool tools = true, ulong seed = 1)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a").AddLocation("house", "Casa", "a")
            .AddMap("a", new[] { "YYYYYYYYYY", "YYYYYYHHHY", "YYYYYYHHHY", "YYYYYYHHHY", "YYYYYYYYYY", "YYYYYYYYYY" },
                new Dictionary<char, string> { ['Y'] = "yard", ['H'] = "house" })
            .AddInterior("a", "house")
            .AddFaction("bandits", "Banditi", homeStoreId: "camp")
            .AddStore("box", "Cassa", "house", food: 6, at: (8, 2), access: new[] { (7, 2) })
            .AddStore("camp", "Bottino", "yard", food: 0, ownerFactionId: "bandits", at: (0, 5))
            .AddActor("player", "Paladino", "yard", isPlayer: true, at: (1, 5))
            .AddActor("raider", "Razziatore", "yard", factionId: "bandits", build: "raider", at: (1, 2),
                items: tools ? new[] { ("thieves-tools", 1), ("bolt", 20) } : new[] { ("bolt", 20) });
        for (var x = 6; x <= 8; x++)
        {
            b.AddEdge("a", (x, 0), (x, 1), x == 7 && window is not null ? MapEdgeKind.Window : MapEdgeKind.Wall,
                lockSpec: x == 7 ? window : null);
            b.AddEdge("a", (x, 3), (x, 4), MapEdgeKind.Wall);
        }
        for (var y = 1; y <= 3; y++)
        {
            b.AddEdge("a", (5, y), (6, y), y == 2 && door is not null ? MapEdgeKind.Door : MapEdgeKind.Wall,
                y == 2 && door is not null ? (6, 2) : null, lockSpec: y == 2 ? door : null);
            b.AddEdge("a", (8, y), (9, y), MapEdgeKind.Wall);
        }
        var s = SimulationSession.Create(b.Build()); // at midnight
        s.Engine.World.Actors[Raider].Assignment = new RaidAssignment
        {
            Faction = new FactionId("bandits"), Target = Box, Home = new StoreId("camp"), Amount = 4, AssignedAt = s.Now,
        };
        s.Engine.CancelAction(s.Engine.World.Actors[Raider]); // as a faction does: the routine gives way to the raid
        s.Engine.StartWait(Raider, Duration.FromSeconds(1), interruptible: true, description: null); // it decides in a moment
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static GridMap Map(SimulationSession s) => s.Engine.World.Maps[new AreaId("a")];

    private static IEnumerable<string> Facts(SimulationSession s) => s.GetWorldView().RecentFacts.Select(f => f.Kind);

    [Fact]
    public void A_raider_with_thieves_tools_picks_the_locked_door_takes_the_food_and_goes_home()
    {
        var s = House(new LockSpec(Locked: true));
        s.AdvanceTo(At(2, 3));

        Assert.Contains("LockPicked", Facts(s));
        Assert.DoesNotContain("Forcing", Facts(s)); // picking is quiet
        Assert.False(Map(s).BarrierOf(BarrierKey.Door(Door))!.Locked);
        Assert.Equal(2, s.Engine.World.Stores[Box].Food);
        Assert.Equal(4, s.Engine.World.Stores[new StoreId("camp")].Food);
        Assert.Null(Of(s, Raider).Assignment);
    }

    [Fact]
    public void Without_tools_it_forces_the_door_loudly()
    {
        var s = House(new LockSpec(Locked: true, ForceDc: 1), tools: false);
        s.AdvanceTo(At(2, 3));

        Assert.Contains("Forcing", Facts(s));
        Assert.Contains("Forced", Facts(s));
        Assert.True(Map(s).BarrierOf(BarrierKey.Door(Door))!.Broken);
        Assert.Equal(2, s.Engine.World.Stores[Box].Food);
    }

    [Fact]
    public void A_door_that_will_not_give_way_is_tried_twice_then_the_raid_is_given_up()
    {
        var s = House(new LockSpec(Locked: true, ForceDc: 30), tools: false);
        s.AdvanceTo(At(2, 3));

        Assert.Equal(2, s.GetWorldView().RecentFacts.Count(f => f.Kind == "ForceFailed"));
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "RaidBalked" && f.Description.Contains("non cede"));
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "RaidAborted" && f.Description.Contains("chiuso troppo bene"));
        Assert.Equal(6, s.Engine.World.Stores[Box].Food);
        Assert.True(s.Engine.World.Factions[new FactionId("bandits")].AvoidUntil.ContainsKey(Box));
    }

    [Fact]
    public void Through_a_stuck_window_it_breaks_the_glass_climbs_in_and_out_again()
    {
        var s = House(door: null, window: new LockSpec(Stuck: true));
        s.AdvanceTo(At(2, 3));

        Assert.Contains("FoundStuck", Facts(s));
        Assert.Contains("GlassBroken", Facts(s));
        Assert.True(s.GetWorldView().RecentFacts.Count(f => f.Kind == "Climbed") >= 2); // in, and out with the food
        Assert.Equal(2, s.Engine.World.Stores[Box].Food);
        Assert.Null(Of(s, Raider).Assignment);
    }

    [Fact]
    public void Bars_stop_a_raider_it_does_not_force_them()
    {
        var s = House(door: null, window: new LockSpec(Stuck: true));
        Map(s).BarrierOf(BarrierKey.Window(WindowA, WindowB))!.Barred = true;
        s.AdvanceTo(At(2, 3));

        Assert.DoesNotContain("GlassBroken", Facts(s));
        Assert.DoesNotContain("BarsTorn", Facts(s));
        Assert.Equal(6, s.Engine.World.Stores[Box].Food);
    }

    [Fact]
    public void The_player_who_sees_it_at_work_sees_the_gesture_and_the_door_and_the_tries_survive_a_save()
    {
        var s = House(new LockSpec(Locked: true, ForceDc: 30), tools: false);
        var player = Of(s, s.Player);
        player.Torches = 1;
        Assert.True(s.Execute(new TorchCommand { Actor = s.Player, Lit = true }).Success);
        player.PlaceAt(Spot.CenterOf(new GridPos(4, 3)));
        for (var i = 0; i < 1800 && Of(s, Raider).CurrentAction is not BarrierAction; i++) // after its idle wait
            s.Advance(Duration.FromSeconds(1));

        Assert.True(Of(s, Raider).CurrentAction is BarrierAction, $"{Of(s, Raider).CurrentAction?.Description} {Of(s, Raider).LastDecision?.Rule} {Of(s, Raider).LastDecision?.Reason}");
        var seen = Assert.Single(s.GetPlayerView().VisibleActors, a => a.Id == Raider);
        Assert.Equal(new BarrierRefView { A = Door, B = Door }, seen.AtBarrier);
        Assert.Equal("spinge con la spalla contro la porta", seen.Doing);
        Assert.Equal(1, Of(s, Raider).Assignment!.BreakInTries);
        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(1, loaded.Engine.World.Actors[Raider].Assignment!.BreakInTries);
    }

    [Fact]
    public void In_the_living_village_with_the_granary_to_be_avoided_the_raider_breaks_into_the_farmhouse_pantry()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var w = s.Engine.World;
        w.Factions[MappedVillageScenario.Ids.Bandits].AvoidUntil[MappedVillageScenario.Ids.GranaryStore] = At(9, 0);
        w.Stores[MappedVillageScenario.Ids.CampStore].Food = 4; // hungry: a raid at the next evaluation
        s.AdvanceTo(At(2, 8));

        var facts = s.GetWorldView().RecentFacts.Select(f => f.Kind).ToList();
        Assert.True(facts.Contains("LockPicked") || facts.Contains("Forced"), string.Join(", ", facts));
        Assert.True(w.Stores[MappedVillageScenario.Ids.Pantry].Food < 6);
        Assert.Equal(40, w.Stores[MappedVillageScenario.Ids.GranaryStore].Food);
    }
}
