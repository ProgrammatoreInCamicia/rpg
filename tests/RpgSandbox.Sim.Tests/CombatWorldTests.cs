using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids;

namespace RpgSandbox.Sim.Tests;

/// <summary>C2: the fight in the world.</summary>
public class CombatWorldTests
{
    [Fact]
    public void Outside_a_fight_there_is_no_combat_view_and_turn_commands_are_refused()
    {
        var s = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());

        Assert.Null(s.GetPlayerView().Combat);
        Assert.False(s.GetPlayerView().Dead);
        Assert.Equal(RejectionReason.NotInCombat, s.Execute(new EndTurnCommand { Actor = s.Player }).Rejection);
        Assert.Equal(RejectionReason.NotInCombat, s.PreviewMove(s.Player, PlayerStart).Reason);
    }
}
