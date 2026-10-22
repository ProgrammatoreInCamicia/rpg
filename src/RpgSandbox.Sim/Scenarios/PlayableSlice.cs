using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim.Scenarios;

/// <summary>The existing village, shortly before its first evening raid. No world state is staged by hand.</summary>
public static class PlayableSlice
{
    public static SimulationSession CreateSession()
    {
        var session = SimulationSession.Create(ScenarioFile.Load("living-village"));
        session.Advance(Duration.FromMinutes(19 * 60 + 27));
        return session;
    }
}
