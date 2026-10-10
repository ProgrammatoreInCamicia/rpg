using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>T6c-4: reports, confiscation and contact on the map, by adjacency and sight, never from afar.</summary>
public class MapContactTests
{
    private static void Walk(SimulationSession s, GridPos to)
    {
        var walk = s.Execute(new MoveCommand { Actor = s.Player, To = to });
        Assert.True(walk.Success, walk.Message);
        s.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(5));
    }

    /// <summary>13:30, broad daylight: the player steals 3 rations at the granary entrance next to the working farmer, then steps back onto the road.</summary>
    private static SimulationSession DaylightTheft()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        s.AdvanceTo(At(1, 13, 30));
        Walk(s, GranaryAccess);
        var take = s.Execute(new TakeFoodCommand { Actor = s.Player, Store = GranaryStore, Amount = 3 });
        Assert.True(take.Success, take.Message);
        s.AdvanceUntilCompleted(take.Action!.Value, Duration.FromMinutes(10));
        Walk(s, new GridPos(12, 9));
        return s;
    }

    [Fact]
    public void A_thief_seen_in_daylight_is_reached_made_to_give_back_and_the_store_is_refilled()
    {
        var s = DaylightTheft();
        Assert.Equal(13, s.GetPlayerView().Food);

        s.AdvanceTo(At(1, 14));

        Assert.Equal(10, s.GetPlayerView().Food);
        Assert.Equal(40, s.GetWorldView().Store(GranaryStore).Food);
        var guard = s.GetWorldView().Actor(Guard);
        Assert.Equal(GuardPost, guard.Position);
        Assert.Equal("Presidia il deposito", guard.LastDecision!.Rule);
        Assert.Contains(s.GetPlayerView().RecentEvents, e => e.Kind == "FoodConfiscated"); // the player knows
    }

    [Fact]
    public void A_report_needs_the_listener_next_to_the_teller_until_the_end()
    {
        var s = DaylightTheft();

        s.AdvanceTo(At(1, 14));

        // The guard walked off to put the rations back while the farmer was talking: that report failed. The farmer
        // went after her (seen, so aimed at where she was) and told her again, next to her.
        var facts = s.GetWorldView().RecentFacts;
        var failed = facts.First(f => f.Kind == "ReportFailed" && f.Description.Contains("Contadino")).At;
        var told = facts.First(f => f.Kind == "InformationShared" && f.Description.Contains("Contadino racconta a Guardia")).At;
        Assert.True(told > failed);
    }

    /// <summary>The guard owes nothing to the scene: a claim on the player is set by hand, then the player stays out of her sight.</summary>
    private static SimulationSession GuardWithClaim(GridPos playerAt)
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
        s.AdvanceTo(At(1, 12));
        Walk(s, playerAt);
        var theft = s.Engine.World.RecordFact("FoodStolen", "Furto di prova.");
        s.Engine.World.Actors[Guard].Claims.Add(new Claim { Thief = s.Player, Store = GranaryStore, Theft = theft, Owed = 2 });
        return s;
    }

    [Fact]
    public void Nobody_takes_rations_back_from_afar_or_out_of_sight()
    {
        var s = GuardWithClaim(FarmerHome); // inside the farmhouse, walls between the player and the inn
        Assert.False(s.Engine.SeesActor(s.Engine.World.Actors[Guard], s.Engine.World.Actors[s.Player]));

        s.Advance(Duration.FromHours(2));

        Assert.Equal(10, s.GetPlayerView().Food);
        Assert.Equal(GuardStart, s.GetWorldView().Actor(Guard).Position); // she never set off: she cannot know where he is
    }

    [Fact]
    public void Walking_up_to_the_guard_who_has_a_claim_is_a_contact_she_reacts_to_at_once()
    {
        var s = GuardWithClaim(FarmerHome);
        s.Advance(Duration.FromMinutes(30)); // she settles into an hour of rest at her post

        Walk(s, new GridPos(GuardStart.X + 1, GuardStart.Y)); // right next to her
        s.Advance(Duration.FromMinutes(3));

        Assert.Equal(8, s.GetPlayerView().Food); // 2 taken back within minutes, not at the end of her rest
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "FoodConfiscated");
    }

    [Fact]
    public void The_scene_in_one_advance_equals_the_scene_minute_by_minute()
    {
        var once = DaylightTheft();
        var stepped = DaylightTheft();

        once.Advance(Duration.FromMinutes(30));
        for (var m = 0; m < 30; m++)
            stepped.Advance(Duration.FromMinutes(1));

        Assert.Equal(once.SaveToString(), stepped.SaveToString());
    }
}
