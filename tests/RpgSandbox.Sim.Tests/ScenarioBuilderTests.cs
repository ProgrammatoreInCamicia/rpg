using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Sim.Tests;

public class ScenarioBuilderTests
{
    private static ScenarioBuilder Minimal() =>
        new ScenarioBuilder()
            .AddArea("a", "A")
            .AddLocation("x", "X", "a")
            .AddLocation("y", "Y", "a");

    [Fact]
    public void Slice_scenario_is_valid()
    {
        var view = SimulationSession.Create(SliceScenario.Create()).GetWorldView();

        Assert.Equal(3, view.Locations.Count);
        Assert.Equal(4, view.Routes.Count); // two routes, one entry per direction
        Assert.Single(view.Actors, a => a.IsPlayer);
        Assert.Equal(2, view.Factions.Count);
    }

    [Fact]
    public void Requires_exactly_one_player() =>
        Assert.Throws<InvalidOperationException>(() => Minimal().AddActor("n", "N", "x").Build());

    [Fact]
    public void Rejects_unknown_references() =>
        Assert.Throws<InvalidOperationException>(() =>
            Minimal().AddRoute("x", "nowhere", Duration.FromMinutes(1)).AddActor("p", "P", "x", isPlayer: true).Build());

    [Fact]
    public void Rejects_zero_duration_routes() =>
        Assert.Throws<InvalidOperationException>(() =>
            Minimal().AddRoute("x", "y", Duration.Zero).AddActor("p", "P", "x", isPlayer: true).Build());

    [Fact]
    public void Rejects_duplicate_routes_in_either_direction() =>
        Assert.Throws<InvalidOperationException>(() =>
            Minimal()
                .AddRoute("x", "y", Duration.FromMinutes(1))
                .AddRoute("y", "x", Duration.FromMinutes(2))
                .AddActor("p", "P", "x", isPlayer: true)
                .Build());
}

public class ScenarioBuilderValidationTests
{
    private static ScenarioBuilder Village() =>
        new ScenarioBuilder()
            .AddArea("a", "A")
            .AddLocation("x", "X", "a")
            .AddLocation("y", "Y", "a")
            .AddRoute("x", "y", Duration.FromMinutes(1))
            .AddActor("p", "P", "x", isPlayer: true);

    [Fact]
    public void Shift_must_be_at_a_known_place() =>
        Assert.Throws<InvalidOperationException>(() =>
            Village().AddActor("n", "N", "x", workLocationId: "nowhere",
                shiftStart: Duration.FromHours(8), shiftEnd: Duration.FromHours(12)).Build());

    [Theory]
    [InlineData(12, 8)]   // ends before it starts
    [InlineData(8, 8)]    // empty
    [InlineData(20, 25)]  // past midnight
    public void Shift_must_be_a_valid_span_within_a_day(int start, int end) =>
        Assert.Throws<InvalidOperationException>(() =>
            Village().AddActor("n", "N", "x", workLocationId: "y",
                shiftStart: Duration.FromHours(start), shiftEnd: Duration.FromHours(end)).Build());

    [Fact]
    public void The_player_has_no_shift() =>
        Assert.Throws<InvalidOperationException>(() =>
            new ScenarioBuilder()
                .AddArea("a", "A").AddLocation("x", "X", "a")
                .AddActor("p", "P", "x", isPlayer: true, workLocationId: "x",
                    shiftStart: Duration.FromHours(8), shiftEnd: Duration.FromHours(12))
                .Build());

    [Fact]
    public void Authority_must_be_a_member_npc()
    {
        // Not a member of the faction.
        Assert.Throws<InvalidOperationException>(() =>
            Village().AddFaction("f", "F", authorityId: "n").AddActor("n", "N", "x").Build());
        // The player cannot be the authority.
        Assert.Throws<InvalidOperationException>(() =>
            new ScenarioBuilder()
                .AddArea("a", "A").AddLocation("x", "X", "a")
                .AddFaction("f", "F", authorityId: "p")
                .AddActor("p", "P", "x", isPlayer: true, factionId: "f")
                .Build());
    }

    [Fact]
    public void A_valid_authority_is_accepted()
    {
        var scenario = Village()
            .AddFaction("f", "F", authorityId: "n")
            .AddActor("n", "N", "x", factionId: "f")
            .Build();

        Assert.Equal(new ActorId("n"), SimulationSession.Create(scenario).GetWorldView().Factions.Single().Authority);
    }

    [Fact]
    public void Raiding_faction_must_own_its_home_store() =>
        Assert.Throws<InvalidOperationException>(() =>
            Village()
                .AddRaidingFaction("b", "B", "s", 1, Duration.FromHours(8), 5, 3, Duration.FromHours(3))
                .AddStore("s", "S", "y", 10) // no owner
                .Build());

    [Fact]
    public void Store_owner_must_exist() =>
        Assert.Throws<InvalidOperationException>(() =>
            Village().AddStore("s", "S", "y", 10, ownerFactionId: "ghosts").Build());
}
