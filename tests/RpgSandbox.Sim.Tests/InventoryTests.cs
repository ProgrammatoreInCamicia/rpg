using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>C3b: inventory, coins, items thrown, spent, left on the ground and picked up.</summary>
public class InventoryTests
{
    private static readonly ActorId Foe = new("foe");
    private static readonly ItemId Javelin = new("javelin");
    private const string Throw = "Giavellotto (lanciato)";

    /// <summary>Noon, a 16×6 yard: the player (a Paladino, with its kit) at (2,2), the foe at <paramref name="foeAt"/>.</summary>
    private static SimulationSession Yard(ulong seed = 1, (int X, int Y)? foeAt = null, string foe = "guard",
        IEnumerable<(string, int)>? foeItems = null)
    {
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYYYYYYYY", 6).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: (2, 2))
            .AddActor("foe", "Avversario", "yard", combat: foe, at: foeAt ?? (6, 2), items: foeItems)
            .Build());
        s.AdvanceTo(At(1, 12));
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

    private static int Carried(SimulationSession s, ItemId item) =>
        s.GetPlayerView().Inventory.SingleOrDefault(i => i.Item == item)?.Count ?? 0;

    [Fact]
    public void Everyone_starts_with_the_kit_of_its_profile()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());

        var me = s.GetPlayerView();
        Assert.Equal(900, me.Coins); // 9 gp
        Assert.Equal(6, Carried(s, Javelin));
        Assert.Equal(3, me.Torches); // the torches are items like any other
        Assert.Contains(me.Inventory, i => i.Item == new ItemId("torch") && i.Count == 3 && i.Name == "Torcia");
        Assert.Equal(6, me.Attacks.Single(a => a.Name == Throw).Remaining);
        Assert.Equal(1, s.GetWorldView().Actor(Guard).Inventory.Single(i => i.Item == new ItemId("spear")).Count);
        Assert.Equal(20, s.GetWorldView().Actor(Raider).Inventory.Single(i => i.Item == new ItemId("bolt")).Count);
    }

    [Fact]
    public void A_thrown_javelin_lands_by_the_target_and_without_one_left_there_is_no_throwing()
    {
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed);
            x.Engine.World.Actors[x.Player].Give("javelin", -5); // one left
            x.Execute(new AttackCommand { Actor = x.Player, Target = Foe, Attack = Throw });
            return x;
        }, x => x.GetPlayerView().Combat is { IsYourTurn: true });

        Assert.Equal(0, Carried(s, Javelin));
        Assert.Single(s.GetPlayerView().GroundItems, g => g.Item == Javelin && g.Count == 1);
        s.Execute(new EndTurnCommand { Actor = s.Player });
        s.Advance(Duration.FromSeconds(6));
        if (s.GetPlayerView().Combat is { IsYourTurn: true } fight)
        {
            var target = fight.Targets.Single(t => t.Attack == Throw);
            Assert.Equal((false, RejectionReason.NoAmmo), (target.Valid, target.Reason));
            Assert.Equal(RejectionReason.NoAmmo,
                s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Throw }).Rejection);
        }
    }

    [Fact]
    public void In_a_fight_picking_something_up_takes_the_turns_one_free_interaction()
    {
        // Two javelins lie next to the player; the fight is on.
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed);
            x.Engine.World.Maps[new AreaId("a")].PutOnGround(new GridPos(2, 3), Javelin, 2);
            x.Execute(new AttackCommand { Actor = x.Player, Target = Foe, Attack = Throw });
            return x;
        }, x => x.GetPlayerView().Combat is { IsYourTurn: true });
        var before = Carried(s, Javelin);

        Assert.True(s.Execute(new PickUpCommand { Actor = s.Player, At = new GridPos(2, 3), Item = Javelin, Count = 1 }).Success);
        Assert.Equal(RejectionReason.NoActionLeft,
            s.Execute(new PickUpCommand { Actor = s.Player, At = new GridPos(2, 3), Item = Javelin, Count = 1 }).Rejection);
        Assert.Equal(before + 1, Carried(s, Javelin));
    }

    [Fact]
    public void Outside_a_fight_one_drops_and_picks_up_freely_but_only_within_reach_and_what_is_there()
    {
        var s = Yard();

        Assert.True(s.Execute(new DropCommand { Actor = s.Player, Item = Javelin, Count = 2 }).Success);
        Assert.Equal(4, Carried(s, Javelin));
        Assert.Contains(s.GetPlayerView().GroundItems, g => g.At == new GridPos(2, 2) && g.Item == Javelin && g.Count == 2);

        Assert.Equal(RejectionReason.NothingThere,
            s.Execute(new PickUpCommand { Actor = s.Player, At = new GridPos(2, 2), Item = Javelin, Count = 3 }).Rejection);
        Assert.Equal(RejectionReason.NotCarried, s.Execute(new DropCommand { Actor = s.Player, Item = Javelin, Count = 9 }).Rejection);
        Assert.True(s.Execute(new PickUpCommand { Actor = s.Player, At = new GridPos(2, 2), Item = Javelin, Count = 2 }).Success);
        Assert.Equal(6, Carried(s, Javelin));
        Assert.Empty(s.GetPlayerView().GroundItems);

        s.Engine.World.Maps[new AreaId("a")].PutOnGround(new GridPos(9, 5), Javelin, 1);
        Assert.Equal(RejectionReason.OutOfReach,
            s.Execute(new PickUpCommand { Actor = s.Player, At = new GridPos(9, 5), Item = Javelin, Count = 1 }).Rejection);
    }

    [Fact]
    public void A_bolt_shot_is_spent()
    {
        var s = Yard(foe: "bandit", foeAt: (12, 2));

        Assert.True(s.Execute(new AttackCommand { Actor = Foe, Target = s.Player, Attack = "Balestra leggera" }).Success);

        Assert.Equal(19, s.GetWorldView().Actor(Foe).Inventory.Single(i => i.Item == new ItemId("bolt")).Count);
        Assert.Empty(s.Engine.World.Maps[new AreaId("a")].Ground);
    }

    [Fact]
    public void A_guard_with_one_spear_does_not_throw_it()
    {
        // 9 squares off: within the thrown spear's long range, beyond its melee. It comes closer instead.
        var s = FirstSeed(seed =>
        {
            var x = Yard(seed, foeAt: (11, 2));
            x.Execute(new AttackCommand { Actor = x.Player, Target = Foe, Attack = Throw });
            return x;
        }, x => x.GetPlayerView().Combat is { IsYourTurn: true } c && c.Round >= 1
                && x.Engine.World.Encounters.Values.Single().Log.Any(l => l.Kind == CombatLogKind.TurnStarted && l.Actor == Foe));

        Assert.Equal(1, s.GetWorldView().Actor(Foe).Inventory.Single(i => i.Item == new ItemId("spear")).Count);
        Assert.DoesNotContain(s.GetWorldView().RecentFacts, f => f.Kind == "Attack" && f.Description.Contains("Lancia (lanciata)"));
    }

    [Fact]
    public void Inventories_coins_and_items_on_the_ground_survive_save_and_load()
    {
        var s = Yard();
        s.Execute(new DropCommand { Actor = s.Player, Item = Javelin, Count = 2 });

        var loaded = s.SaveAndReload();

        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(s.GetPlayerView().Inventory, loaded.GetPlayerView().Inventory);
        Assert.Equal(s.GetPlayerView().GroundItems, loaded.GetPlayerView().GroundItems);
        Assert.Equal(900, loaded.GetPlayerView().Coins);
    }
}
