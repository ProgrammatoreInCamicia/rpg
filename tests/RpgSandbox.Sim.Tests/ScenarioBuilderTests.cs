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
