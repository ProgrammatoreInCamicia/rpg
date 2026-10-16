using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

public class DirectionalVisionTests
{
    private static readonly ActorId Foe = new("foe");
    private static readonly AreaId Area = new("field");

    private static SimulationSession Field((int X, int Y) playerAt, (int X, int Y) foeAt,
        string playerBuild = "bandit", string foeBuild = "guard")
    {
        var s = SimulationSession.Create(new ScenarioBuilder().WithSeed(7)
            .AddArea("field", "Campo").AddLocation("field", "Campo", "field")
            .AddLocation("house", "Casa", "field")
            .AddMap("field", Enumerable.Repeat(new string('F', 40), 5).ToArray(),
                new Dictionary<char, string> { ['F'] = "field", ['H'] = "house" })
            .AddInterior("field", "house")
            .AddActor("player", "Giocatore", "field", isPlayer: true, at: playerAt, build: playerBuild)
            .AddActor("foe", "Nemico", "field", at: foeAt, build: foeBuild)
            .Build());
        s.AdvanceTo(At(1, 12));
        return s;
    }

    [Fact]
    public void Daylight_view_reaches_a_distant_target_ahead_but_not_behind()
    {
        var s = Field((2, 2), (32, 2)); // 150 feet: within the crossbow's long range
        s.FacePlayer(1, 0);
        Assert.Contains(s.GetPlayerView().VisibleActors, a => a.Id == Foe);
        Assert.Equal('2', s.GetPlayerView().MapVisibility![2][32]);
        var preview = s.PreviewAttack(s.Player, Foe, "Balestra leggera");
        Assert.True(preview.Valid, preview.Message);
        Assert.True(preview.LongRange);

        s.FacePlayer(-1, 0);
        Assert.DoesNotContain(s.GetPlayerView().VisibleActors, a => a.Id == Foe);
        Assert.Equal('1', s.GetPlayerView().MapVisibility![2][32]);
    }

    [Fact]
    public void Combat_removes_the_cone_even_for_an_opening_shot()
    {
        var s = Field((2, 2), (32, 2)); // 150 feet: within the crossbow's long range
        s.FacePlayer(-1, 0);
        Assert.DoesNotContain(s.GetPlayerView().VisibleActors, a => a.Id == Foe);
        Assert.NotEqual('2', s.GetPlayerView().MapVisibility![2][32]);

        var preview = s.PreviewAttack(s.Player, Foe, "Balestra leggera");
        Assert.True(preview.Valid, preview.Message);
        Assert.True(preview.LongRange);
        var attack = s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Balestra leggera" });
        Assert.True(attack.Success, attack.Message);

        var view = s.GetPlayerView();
        Assert.NotNull(view.Combat);
        Assert.Contains(view.VisibleActors, a => a.Id == Foe);
        Assert.Contains(view.Combat!.Combatants, a => a.Id == Foe);
        Assert.Equal('2', view.MapVisibility![2][32]); // exploration updates when combat opens
    }

    [Fact]
    public void An_attacker_behind_the_player_has_no_cone_advantage_when_starting_combat()
    {
        var s = Field((8, 2), (2, 2)); // 30 feet behind, beyond peripheral awareness
        s.FacePlayer(1, 0);
        Assert.DoesNotContain(s.GetPlayerView().VisibleActors, a => a.Id == Foe);

        var preview = s.PreviewAttack(Foe, s.Player, "Lancia (lanciata)");
        Assert.True(preview.Valid, preview.Message);
        Assert.False(preview.Advantage);
        Assert.True(s.Execute(new AttackCommand { Actor = Foe, Target = s.Player, Attack = "Lancia (lanciata)" }).Success);
        Assert.Contains(s.GetPlayerView().VisibleActors, a => a.Id == Foe);
    }

    [Fact]
    public void Projected_sight_uses_the_passed_square_instead_of_the_targets_stored_spot()
    {
        var s = Field((20, 2), (10, 2));
        s.FacePlayer(1, 0);
        var map = s.Engine.World.Maps[Area];
        var watcher = s.Engine.World.Actors[s.Player];
        var foe = s.Engine.World.Actors[Foe];
        Assert.False(s.Engine.SeesActor(watcher, foe));
        Assert.True(s.Engine.Sees(map, watcher, new GridPos(20, 2), foe, new GridPos(25, 2), Light.Bright));
        Assert.False(s.Engine.Sees(map, watcher, new GridPos(20, 2), foe, new GridPos(10, 2), Light.Bright));
    }
}
