using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>The vertical-slice scenario. Grows with each increment; increment 0 has two connected places.</summary>
public static class SliceScenario
{
    public static class Ids
    {
        public static readonly AreaId Village = new("village");
        public static readonly LocationId Inn = new("inn");
        public static readonly LocationId Granary = new("granary");
        public static readonly ActorId Player = new("player");
    }

    public static Scenario Create() =>
        new ScenarioBuilder()
            .AddArea(Ids.Village.Value, "Villaggio")
            .AddLocation(Ids.Inn.Value, "Locanda", Ids.Village.Value)
            .AddLocation(Ids.Granary.Value, "Granaio", Ids.Village.Value)
            .AddRoute(Ids.Inn.Value, Ids.Granary.Value, Duration.FromMinutes(5))
            .AddActor(Ids.Player.Value, "Protagonista", Ids.Inn.Value, isPlayer: true)
            .Build();
}
