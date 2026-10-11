using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>C5a: what the player knows of the creatures it meets, and nothing more.</summary>
public class LoreTests
{
    private static readonly ActorId Foe = new("foe");

    /// <summary>Noon, a 12×4 yard: the Paladin at (1,1), a foe next to it.</summary>
    private static SimulationSession Yard(ulong seed, string foe = "guard") =>
        SimulationSession.Create(new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYYYY", 4).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, build: "paladin", at: (1, 1))
            .AddActor("foe", "Avversario", "yard", build: foe, at: (2, 1))
            .Build());

    /// <summary>A fight where both sides have struck at least once: the player opens, then rounds pass.</summary>
    private static SimulationSession Exchanged(ulong seed = 1)
    {
        for (; seed < 200; seed++)
        {
            var s = Yard(seed);
            s.AdvanceTo(At(1, 12));
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Spada lunga" });
            for (var i = 0; i < 6 && s.GetPlayerView().Combat is { } c && !c.Log.Any(l => l.Kind == CombatLogKind.Attack && l.Actor == Foe); i++)
            {
                if (c.IsYourTurn)
                    s.Execute(new EndTurnCommand { Actor = s.Player });
                s.Advance(Duration.FromSeconds(6));
            }
            var log = s.GetPlayerView().Combat?.Log;
            if (log is not null && log.Any(l => l.Kind == CombatLogKind.Attack && l.Actor == Foe)
                && log.Any(l => l.Kind == CombatLogKind.Attack && l.Actor == s.Player))
                return s;
        }
        throw new InvalidOperationException("No seed gives an exchange of blows.");
    }

    [Fact]
    public void The_player_sees_its_own_rolls_but_never_the_foes_armor_class_nor_the_foes_dice()
    {
        var s = Exchanged();
        var log = s.GetPlayerView().Combat!.Log;

        var mine = log.First(l => l.Kind == CombatLogKind.Attack && l.Actor == s.Player);
        Assert.NotNull(mine.Total);
        Assert.Null(mine.ArmorClass);
        Assert.Contains("d20", mine.Text);
        Assert.DoesNotContain("CA", mine.Text);

        var theirs = log.First(l => l.Kind == CombatLogKind.Attack && l.Actor == Foe);
        Assert.Empty(theirs.D20);
        Assert.Null(theirs.Total);
        Assert.Null(theirs.Bonus);
        Assert.Null(theirs.ArmorClass);
        Assert.DoesNotContain("d20", theirs.Text);
        Assert.DoesNotContain("CA", theirs.Text);
        Assert.Equal(s.Player, theirs.Target);
        Assert.NotNull(theirs.Damage); // what it suffers, it knows

        var foeInitiative = log.Single(l => l.Kind == CombatLogKind.Initiative && l.Actor == Foe);
        Assert.Empty(foeInitiative.D20);
        Assert.DoesNotContain("d20", foeInitiative.Text);
        Assert.Null(s.GetPlayerView().Combat!.Order.Single(o => o.Actor == Foe).Total);
        Assert.NotNull(s.GetPlayerView().Combat!.Order.Single(o => o.Actor == s.Player).Total);
    }

    [Fact]
    public void Recent_events_carry_no_armor_class_while_the_true_record_keeps_it()
    {
        var s = Exchanged();

        Assert.All(s.GetPlayerView().RecentEvents.Where(f => f.Kind == "Attack"), f => Assert.DoesNotContain("CA", f.Description));
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "Attack" && f.Description.Contains("contro CA"));

        var reloaded = s.SaveAndReload(); // the player's text is saved with the fact
        Assert.Equal(s.GetPlayerView().RecentEvents, reloaded.GetPlayerView().RecentEvents);
    }
}
