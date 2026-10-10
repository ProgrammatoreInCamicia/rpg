using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.SliceScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>Regressions for Codex's review of T5/T4 (docs/REVIEW_T5_T4.md) and the decisions D1–D8 in chat.txt.</summary>
public class ReviewT4RegressionTests
{
    private static readonly ActorId Watcher = new("watcher");
    private static readonly StoreId MainStore = new("main");
    private static readonly StoreId Annex = new("annex");

    /// <summary>
    /// A village square ("x") with the guard and another villager living there, a main store there and an annex
    /// ("y"); the player stands in the square with 10 rations.
    /// </summary>
    private static ScenarioBuilder Square(int watcherFood = 0, string start = "x") =>
        new ScenarioBuilder()
            .AddArea("v", "Villaggio")
            .AddLocation("x", "Piazza", "v")
            .AddLocation("y", "Magazzino", "v")
            .AddRoute("x", "y", Duration.FromMinutes(5))
            .AddFaction("village", "Villaggio", homeStoreId: MainStore.Value, authorityId: Guard.Value)
            .AddStore(MainStore.Value, "Scorte principali", "x", 40, "village")
            .AddStore(Annex.Value, "Scorte del magazzino", "y", 40, "village")
            .AddActor(Player.Value, "Protagonista", start, isPlayer: true, food: 10, sheet: Sheets.Paladin())
            .AddActor(Guard.Value, "Guardia", start, factionId: "village", sheet: Sheets.VillageGuard())
            .AddActor(Watcher.Value, "Passante", "x", factionId: "village", food: watcherFood, sheet: Sheets.Farmer());

    private static void Steal(SimulationSession s, StoreId store, int amount)
    {
        var take = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = store, Amount = amount });
        Assert.True(take.Success, take.Message);
        s.AdvanceUntilCompleted(take.Action!.Value, Duration.FromHours(1));
    }

    // ---------------------------------------------------------------- R1

    [Fact]
    public void R1_two_witnesses_of_one_theft_make_one_claim_and_one_confiscation()
    {
        var s = SimulationSession.Create(Square().Build());
        s.AdvanceTo(At(1, 12));

        Steal(s, MainStore, 3); // the guard and the watcher both see it; the watcher also tells the guard
        s.AdvanceTo(At(1, 15));

        var view = s.GetWorldView();
        Assert.Equal(10, s.GetPlayerView().Food);
        Assert.Equal(40, view.Store(MainStore).Food);
        Assert.Single(view.RecentFacts, f => f.Kind == "FoodConfiscated");
        Assert.Empty(view.Actor(Guard).Claims);
    }

    [Fact]
    public void R1_a_late_testimony_does_not_reopen_a_settled_theft()
    {
        var s = SimulationSession.Create(Square().Build());
        s.AdvanceTo(At(1, 12));
        Steal(s, MainStore, 3);
        s.AdvanceTo(At(1, 15)); // settled

        // A late copy of the same theft, with a new origin, reaches the guard (staged through engine internals).
        var engine = s.Engine;
        var guard = engine.World.Actors[Guard];
        var original = guard.Knowledge.First();
        guard.Knowledge.Add(new Observation
        {
            Id = new ObservationId(engine.World.NextObservationId++),
            Origin = new ObservationId(engine.World.NextObservationId++),
            Store = original.Store, StoreName = original.StoreName, Location = original.Location, Amount = original.Amount,
            Thief = original.Thief, ThiefName = original.ThiefName, ObservedAt = original.ObservedAt, LearnedAt = s.Now,
            Source = Watcher, Fact = original.Fact,
        });
        engine.CancelAction(guard);
        s.AdvanceTo(At(1, 18));

        Assert.Empty(s.GetWorldView().Actor(Guard).Claims);
        Assert.Equal(10, s.GetPlayerView().Food);
        Assert.Single(s.GetWorldView().RecentFacts, f => f.Kind == "FoodConfiscated");
    }

    // ---------------------------------------------------------------- R2

    [Fact]
    public void R2_personal_food_is_never_taken_for_confiscated_food()
    {
        var s = SimulationSession.Create(Square(watcherFood: 5).Build());

        s.AdvanceTo(At(2, 12));

        Assert.Equal(5, s.GetWorldView().Actor(Watcher).Food);
        Assert.Equal(40, s.GetWorldView().Store(MainStore).Food);
    }

    [Fact]
    public void R2_confiscated_rations_go_back_to_the_store_they_were_stolen_from()
    {
        // Player and guard stand at the annex; the faction's home store is the main one, elsewhere.
        var s = SimulationSession.Create(Square(start: "y").Build());
        s.AdvanceTo(At(1, 12));

        Steal(s, Annex, 3); // under the guard's eyes
        s.AdvanceTo(At(1, 16));

        var view = s.GetWorldView();
        Assert.Equal(40, view.Store(Annex).Food);
        Assert.Equal(40, view.Store(MainStore).Food);
        Assert.Equal(10, s.GetPlayerView().Food);
        Assert.Empty(view.Actor(Guard).Claims);
    }

    // ---------------------------------------------------------------- R3

    [Fact]
    public void R3_a_sheet_without_a_title_is_rejected_when_building() =>
        Assert.Throws<InvalidOperationException>(() =>
            new ScenarioBuilder().AddArea("a", "A").AddLocation("x", "X", "a")
                .AddActor("p", "P", "x", isPlayer: true, sheet: CharacterSheet.Commoner("")).Build());

    [Fact]
    public void R3_a_sheet_with_an_unknown_skill_is_rejected_when_building() =>
        Assert.Throws<InvalidOperationException>(() =>
            new ScenarioBuilder().AddArea("a", "A").AddLocation("x", "X", "a")
                .AddActor("p", "P", "x", isPlayer: true, sheet: Sheets.Paladin() with { SkillProficiencies = new[] { (Skill)999 } })
                .Build());

    [Fact]
    public void R3_every_scenario_that_builds_can_be_saved_and_loaded()
    {
        var s = SimulationSession.Create(Square(watcherFood: 3).Build());
        s.AdvanceTo(At(1, 12));
        Steal(s, MainStore, 2);

        Assert.True(TestHelpers.LoadFromString(s.SaveToString()).Success);
    }

    // ---------------------------------------------------------------- R5

    [Fact]
    public void R5_in_the_dark_the_player_only_senses_that_someone_is_there()
    {
        var s = SimulationSession.Create(SliceScenario.Create());
        var go = s.Execute(new TravelCommand { Actor = s.Player, Destination = Granary });
        s.AdvanceUntilCompleted(go.Action!.Value, Duration.FromHours(1));

        // At night the farmer (staged) stands at the dark granary with the player.
        var farmer = s.Engine.World.Actors[Farmer];
        s.Engine.CancelAction(farmer);
        farmer.Location = Granary;
        s.Engine.StartWait(Farmer, Duration.FromHours(1), interruptible: false, description: "Riposa");

        var view = s.GetPlayerView();
        Assert.Equal(Light.Dark, view.Light);
        Assert.DoesNotContain(view.VisibleActors, a => a.Id == Farmer);
        Assert.DoesNotContain(view.PeopleHere, p => p.Id == Farmer);
        Assert.Equal(1, view.UnseenNearby);
    }

    [Fact]
    public void R5_at_night_the_lit_inn_is_visible_and_dark_places_are_not()
    {
        var s = SimulationSession.Create(SliceScenario.Create()); // 00:00, the player at the inn

        var view = s.GetPlayerView();

        Assert.Equal(Light.Dim, view.Light);
        Assert.Contains(view.PeopleHere, p => p.Id == Guard);
        Assert.DoesNotContain(view.VisibleStores, x => x.Id == GranaryStore); // the granary lies in the dark
    }

    // ---------------------------------------------------------------- D5, D7

    [Fact]
    public void D5_a_theft_partly_in_daylight_is_seen_even_if_it_ends_at_dusk()
    {
        var s = SimulationSession.Create(Square().Build());
        s.AdvanceTo(At(1, 18, 58)); // 18:58–19:01: two minutes of full light, then dim

        Steal(s, MainStore, 1);

        var seen = s.GetWorldView().Actor(Watcher).Knowledge.Single();
        Assert.Equal(Player, seen.Thief);
        Assert.Equal("Seen", seen.Perceived);
    }

    [Fact]
    public void D7_a_theft_heard_in_the_dark_is_recorded_as_heard_and_stays_so_when_told()
    {
        // The player steals at night in the square; the watcher may only hear it. Find a seed where it is heard.
        for (ulong seed = 0; seed < 40; seed++)
        {
            var s = SimulationSession.Create(Square().WithSeed(seed).Build());
            s.AdvanceTo(At(1, 2));
            Steal(s, MainStore, 1);
            var knowledge = s.GetWorldView().Actor(Watcher).Knowledge;
            if (knowledge.Count == 0)
                continue;

            Assert.Equal("Heard", knowledge.Single().Perceived);
            Assert.Null(knowledge.Single().Thief);
            return;
        }
        Assert.Fail("No seed where the watcher hears the theft");
    }

    // ---------------------------------------------------------------- D8

    [Fact]
    public void D8_a_known_thief_walking_up_to_the_guard_is_stopped_at_once()
    {
        var s = SimulationSession.Create(SliceScenario.Create());
        var go = s.Execute(new TravelCommand { Actor = s.Player, Destination = Granary });
        s.AdvanceUntilCompleted(go.Action!.Value, Duration.FromHours(1));
        s.AdvanceTo(At(1, 13, 10));
        Steal(s, GranaryStore, 3); // the farmer sees it and tells the guard at the inn
        var away = s.Execute(new TravelCommand { Actor = s.Player, Destination = Inn });
        s.AdvanceUntilCompleted(away.Action!.Value, Duration.FromHours(1));
        s.AdvanceTo(At(1, 15));
        Assert.Equal(13, s.GetPlayerView().Food); // the guard went to the granary meanwhile

        var back = s.Execute(new TravelCommand { Actor = s.Player, Destination = Granary });
        s.AdvanceUntilCompleted(back.Action!.Value, Duration.FromHours(1)); // arrives 15:05

        s.AdvanceTo(At(1, 15, 8)); // not at the end of the guard's watch, but on contact
        Assert.Equal(10, s.GetPlayerView().Food);
    }
}
