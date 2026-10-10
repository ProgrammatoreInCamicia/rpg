using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>T6c-3: the raider on the village map (waiting for the dark, the exit, sneaking, physical guarding).</summary>
public class RaidOnMapTests
{
    private static int Food(SimulationSession s, StoreId store) => s.GetWorldView().Store(store).Food;

    [Fact]
    public void A_night_raid_comes_in_by_the_road_steals_at_the_granary_entrance_and_goes_back()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());

        s.AdvanceTo(At(1, 12)); // the camp ran low at 08:00; the raid is ordered, but it is broad daylight
        var waiting = s.GetWorldView().Actor(Raider);
        Assert.Equal((BanditCamp, "Aspetta il buio"), (waiting.Location!.Value, waiting.LastDecision!.Rule));
        Assert.Equal(40, Food(s, GranaryStore));

        // At dusk the raider sets off; once on the map it walks sneaking, at the slow pace.
        s.AdvanceTo(At(1, 19, 30));
        for (var i = 0; i < 60 && s.GetWorldView().Actor(Raider).Move is null; i++)
            s.Advance(Duration.FromSeconds(1));
        var onTheMap = s.GetWorldView().Actor(Raider);
        Assert.NotNull(onTheMap.Position);
        Assert.True(onTheMap.Move!.Stealthy);
        Assert.Equal(GranaryAccess, onTheMap.Move.Path[^1]); // heading for the entrance, not anywhere in the yard

        s.AdvanceTo(At(1, 21));
        Assert.Equal(32, Food(s, GranaryStore));
        Assert.Equal(16, Food(s, CampStore));
        var back = s.GetWorldView().Actor(Raider);
        Assert.Equal((BanditCamp, (GridPos?)null), (back.Location!.Value, back.Position));
    }

    /// <summary>
    /// After the first raid of the evening (19:30), the camp is emptied by hand at 20:00 so that a second raid is ordered
    /// at 21:00, in the dark. The guard is put on duty at 20:00 and stands at the granary's guard post, next to the
    /// entrance, from 21:00. With <paramref name="torch"/> he lights a torch at 21:15 (it burns an hour).
    /// </summary>
    private static SimulationSession GuardedNight(bool torch)
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        s.AdvanceTo(At(1, 20));
        s.Engine.World.Stores[CampStore].Food = 0;
        var guard = s.Engine.World.Actors[Guard];
        guard.GuardDuty = new GuardDuty { Store = GranaryStore, Since = s.Now, Until = s.Now.Plus(Duration.FromHours(10)) };
        s.AdvanceTo(At(1, 21, 15));
        Assert.Equal(GuardPost, s.GetWorldView().Actor(Guard).Position);
        if (torch)
        {
            guard.Torches = 1;
            Assert.True(s.Execute(new TorchCommand { Actor = Guard, Lit = true }).Success);
        }
        return s;
    }

    [Fact]
    public void A_raider_who_sees_the_guard_by_the_entrance_gives_up_and_the_band_avoids_the_granary()
    {
        var s = GuardedNight(torch: true);

        s.AdvanceTo(At(2, 1));

        Assert.Equal(GuardPost, s.GetWorldView().Actor(Guard).Position);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "RaidDeterred");
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "RaidAborted");
        Assert.True(s.Engine.World.Factions[Bandits].AvoidUntil.ContainsKey(GranaryStore));
        Assert.Equal(40 - 8, Food(s, GranaryStore)); // only the first, unguarded raid of the evening took food
    }

    [Fact]
    public void A_guard_in_the_dark_is_not_seen_but_stops_the_theft_at_the_store()
    {
        var s = GuardedNight(torch: false);

        s.AdvanceTo(At(2, 1));

        Assert.DoesNotContain(s.GetWorldView().RecentFacts, f => f.Kind == "RaidDeterred"); // he did not see the guard
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "RaidFoiled");          // but was stopped at the store
        Assert.True(s.Engine.World.Factions[Bandits].AvoidUntil.ContainsKey(GranaryStore));  // so the band learns it
        // Stopped, he flees at once: on his way out within moments, not after hesitating at the entrance.
        var facts = s.GetWorldView().RecentFacts;
        var foiled = facts.Single(f => f.Kind == "RaidFoiled").At;
        var leaves = facts.First(f => f.Kind == "TravelStarted" && f.At >= foiled).At;
        Assert.True(leaves.Since(foiled).Seconds < 120, $"left {leaves.Since(foiled).Seconds} s after being stopped");
        Assert.Equal(40 - 8, Food(s, GranaryStore)); // the second attempt was refused at the store
        Assert.Equal(BanditCamp, s.GetWorldView().Actor(Raider).Location);
    }

    [Fact]
    public void Guarding_on_a_map_is_physical()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var guard = s.Engine.World.Actors[Guard];
        guard.GuardDuty = new GuardDuty { Store = GranaryStore, Since = s.Now, Until = s.Now.Plus(Duration.FromHours(10)) };
        var raider = s.Engine.World.Actors[Raider];

        // On duty but still at the inn: the granary is not covered.
        Assert.False(s.Engine.IsGuarded(s.Engine.World.Stores[GranaryStore]));

        s.Advance(Duration.FromMinutes(65)); // ends his rest, then walks to the guard post
        Assert.Equal(GuardPost, s.GetWorldView().Actor(Guard).Position);
        Assert.True(s.Engine.IsGuarded(s.Engine.World.Stores[GranaryStore]));
        Assert.Null(raider.Position);

        // In the granary yard but on the far side, away from the entrance: still on duty, but nothing is covered.
        guard.Position = FarmerWork;
        Assert.Equal(Granary, s.Engine.World.Maps[Village].ZoneAt(FarmerWork));
        Assert.False(s.Engine.IsGuarded(s.Engine.World.Stores[GranaryStore]));
    }

    [Fact]
    public void Two_days_in_one_advance_equal_the_same_hour_by_hour()
    {
        var once = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        var hourly = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());

        once.Advance(Duration.FromHours(48));
        for (var hour = 0; hour < 48; hour++)
            hourly.Advance(Duration.FromHours(1));

        Assert.Equal(once.SaveToString(), hourly.SaveToString());
    }

    [Fact]
    public void A_raid_saved_on_the_map_goes_on_after_loading()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        s.AdvanceTo(At(1, 19, 30));
        for (var i = 0; i < 60 && s.GetWorldView().Actor(Raider).Move is null; i++)
            s.Advance(Duration.FromSeconds(1));
        s.Advance(Duration.FromSeconds(3)); // mid-walk, sneaking

        var loaded = s.SaveAndReload();
        s.AdvanceTo(At(1, 22));
        loaded.AdvanceTo(At(1, 22));

        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.Equal(32, Food(loaded, GranaryStore));
    }
}
