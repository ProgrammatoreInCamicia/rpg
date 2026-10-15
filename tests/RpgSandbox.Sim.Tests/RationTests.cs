using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>D1c: rations are items (SRD Rations), and stores are containers of items.</summary>
public class RationTests
{
    private static readonly ItemId Ration = new("ration");

    private static SimulationSession Village() => SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());

    [Fact]
    public void Rations_are_the_srd_item_carried_like_any_other()
    {
        Assert.Equal(("Razione", 2.0, 50L), (Items.Ration.Name, Items.Ration.Pounds, Items.Ration.CostCopper)); // SRD: 2 lb., 5 SP

        var me = Village().GetPlayerView();
        Assert.Equal(10, me.Food);
        Assert.Contains(me.Inventory, i => i.Item == Ration && i.Name == "Razione" && i.Count == 10 && i.Equipped == 0);
    }

    [Fact]
    public void Rations_left_on_the_ground_are_no_longer_carried_food_until_picked_up()
    {
        var s = Village();

        Assert.True(s.Execute(new DropCommand { Actor = s.Player, Item = Ration, Count = 3 }).Success);
        Assert.Equal(7, s.GetPlayerView().Food);
        Assert.Contains(s.GetPlayerView().GroundItems, g => g.At == PlayerStart && g.Item == Ration && g.Count == 3);

        Assert.True(s.Execute(new PickUpCommand { Actor = s.Player, At = PlayerStart, Item = Ration, Count = 3 }).Success);
        Assert.Equal(10, s.GetPlayerView().Food);
        Assert.DoesNotContain(s.GetPlayerView().GroundItems, g => g.Item == Ration); // P4a: thieves' tools lie in the inn
    }

    [Fact]
    public void A_store_is_a_container_its_rations_counted_in_food()
    {
        var s = Village();
        var granary = s.GetWorldView().Store(GranaryStore);
        Assert.Equal(40, granary.Food);
        Assert.Equal(new[] { (Ration, "Razione", 40) }, granary.Contents.Select(i => (i.Item, i.Name, i.Count)));

        // Two days on, the raid included: what is counted and what is held never part ways, and no ration is made or lost.
        var total = s.GetWorldView().TotalFood();
        s.AdvanceTo(At(3, 0));
        var world = s.GetWorldView();
        Assert.All(world.Stores, st => Assert.Equal(st.Food, st.Contents.Where(i => i.Item == Ration).Sum(i => i.Count)));
        Assert.All(world.Actors, a => Assert.Equal(a.Food, a.Inventory.Where(i => i.Item == Ration).Sum(i => i.Count)));
        Assert.Contains(world.RecentFacts, f => f.Kind is "FoodStolen" or "FoodTaken");
        Assert.True(world.TotalFood() < total); // the bandits eat theirs every morning
    }

    [Fact]
    public void Rations_and_store_contents_survive_save_and_load()
    {
        var s = Village();
        s.Execute(new DropCommand { Actor = s.Player, Item = Ration, Count = 2 });
        s.AdvanceTo(At(2, 12));

        var loaded = s.SaveAndReload();

        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(s.GetWorldView().Stores, loaded.GetWorldView().Stores, new StoreComparer());
        Assert.Equal(s.GetPlayerView().Food, loaded.GetPlayerView().Food);
    }

    [Theory]
    [InlineData("dragon-egg", 1)]
    [InlineData("ration", 0)]
    [InlineData("ration", -5)]
    public void A_save_with_a_store_holding_what_cannot_be_is_refused(string item, int count)
    {
        var json = JsonNode.Parse(Village().SaveToString())!;
        var granary = json["Stores"]!.AsArray().First(st => (string)st!["Id"]! == GranaryStore.Value)!;
        granary["Contents"] = new JsonArray(new JsonObject { ["Item"] = item, ["Count"] = count });

        Assert.Contains("deposito", LoadFromString(json.ToJsonString()).Error);
    }

    [Fact]
    public void A_save_missing_store_contents_is_refused_instead_of_erasing_the_store()
    {
        var json = JsonNode.Parse(Village().SaveToString())!;
        var granary = json["Stores"]!.AsArray().First(st => (string)st!["Id"]! == GranaryStore.Value)!.AsObject();
        Assert.True(granary.Remove("Contents"));

        Assert.Contains("contenuto del deposito", LoadFromString(json.ToJsonString()).Error);
    }

    [Fact]
    public void A_save_missing_actor_inventory_is_refused_instead_of_erasing_rations()
    {
        var json = JsonNode.Parse(Village().SaveToString())!;
        var player = json["Actors"]!.AsArray().First(actor => (string)actor!["Id"]! == Player.Value)!.AsObject();
        Assert.True(player.Remove("Inventory"));

        Assert.Contains("inventario di 'player' mancante", LoadFromString(json.ToJsonString()).Error);
    }

    [Fact]
    public void Contents_without_rations_cannot_feed_anyone()
    {
        var files = ContentPack.Default.Files.ToDictionary(f => f.Name, f => f.Text);
        var items = JsonNode.Parse(files["srd/items.json"])!;
        var list = items["items"]!.AsArray();
        list.Remove(list.First(i => (string)i!["id"]! == "ration"));
        files["srd/items.json"] = items.ToJsonString();
        var pack = ContentPack.Load(files);

        var error = Assert.ThrowsAny<Exception>(() => ScenarioFile.Load("living-village", pack));
        Assert.Contains("ration", error.Message);
    }

    /// <summary>Store views hold lists: compared by their contents.</summary>
    private sealed class StoreComparer : IEqualityComparer<StoreView>
    {
        public bool Equals(StoreView? a, StoreView? b) => a is not null && b is not null
            && a with { Contents = b.Contents, Access = b.Access } == b && a.Contents.SequenceEqual(b.Contents) && a.Access.SequenceEqual(b.Access);

        public int GetHashCode(StoreView s) => s.Id.GetHashCode();
    }
}
