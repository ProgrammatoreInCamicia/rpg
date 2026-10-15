using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>P3: cover (SRD 5.2.1) on the real positions: walls by the lines to the body, creatures, open windows.</summary>
public class CoverTests
{
    private static readonly ActorId Foe = new("foe");
    private static readonly ActorId Passerby = new("passerby");
    private static readonly AreaId Area = new("a");
    private const string Javelin = "Giavellotto (lanciato)";

    /// <summary>A 16×8 yard at noon: the player (Paladin) at (2,3), a guard (AC 16) at (8,3), a passer-by out of the way.</summary>
    private static SimulationSession Yard(ulong seed = 1, Action<ScenarioBuilder>? more = null)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat(new string('Y', 16), 8).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: (2, 3))
            .AddActor("foe", "Guardia", "yard", combat: "guard", at: (8, 3))
            .AddActor("passerby", "Passante", "yard", at: (14, 7));
        more?.Invoke(b);
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, 12));
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static AttackPreview Aim(SimulationSession s, string attack = Javelin) => s.PreviewAttack(s.Player, Foe, attack);

    private static CombatRecord? LastShot(SimulationSession s) =>
        s.Engine.World.Encounters.Values.SingleOrDefault()?.Log.LastOrDefault(l => l.Kind == CombatLogKind.Attack && l.Actor == s.Player)
        ?? s.Engine.World.LastFight?.Log.LastOrDefault(l => l.Kind == CombatLogKind.Attack && l.Actor == s.Player);

    [Fact]
    public void In_the_open_there_is_no_cover_and_the_aim_is_the_centre()
    {
        var aim = Aim(Yard());
        Assert.Equal(CoverDegree.None, aim.Cover);
        Assert.Equal(Spot.CenterOf(new GridPos(8, 3)), aim.To);
    }

    [Fact]
    public void A_creature_on_the_line_gives_half_cover_and_not_once_it_stands_aside()
    {
        var s = Yard();
        Of(s, Passerby).PlaceAt(new Spot(330, 210)); // on the line of centres, midway
        Assert.Equal(CoverDegree.Half, Aim(s).Cover);
        Of(s, Passerby).PlaceAt(new Spot(330, 210 + 20)); // its body 20 inches aside: the line passes clear of it
        Assert.Equal(CoverDegree.None, Aim(s).Cover);
    }

    [Fact]
    public void Through_an_open_window_the_target_has_half_cover_both_ways_and_a_closed_one_stops_the_shot()
    {
        var s = Yard(more: b =>
        {
            for (var y = 0; y < 8; y++)
                b.AddEdge("a", (5, y), (6, y), MapEdgeKind.Window, open: true);
        });
        Assert.Equal(CoverDegree.Half, Aim(s).Cover);
        Assert.Equal(CoverDegree.Half, s.PreviewAttack(Foe, s.Player, "Lancia (lanciata)").Cover);

        var shut = Yard(more: b =>
        {
            for (var y = 0; y < 8; y++)
                b.AddEdge("a", (5, y), (6, y), MapEdgeKind.Window);
        });
        Assert.Equal(RejectionReason.NoLineOfFire, Aim(shut).Reason);
    }

    [Fact]
    public void Behind_a_wall_and_a_creature_the_most_protective_cover_applies_not_the_sum()
    {
        // A wall down x = 6|7 over rows 0–3 hides most of a guard just past its end, off the line's side.
        var s = Yard(more: b =>
        {
            for (var y = 0; y <= 3; y++)
                b.AddEdge("a", (6, y), (7, y), MapEdgeKind.Wall);
        });
        var map = s.Engine.World.Maps[Area];
        var from = Of(s, s.Player).Spot!.Value;
        Spot? spot = null;
        for (var y = 180; y < 330 && spot is null; y += 2)
        for (var x = 430; x < 560 && spot is null; x += 2)
        {
            var at = new Spot(x, y);
            Of(s, Foe).PlaceAt(at);
            if (s.Engine.SeesActor(Of(s, s.Player), Of(s, Foe)) && Simulation.AimAt(map, from, at) is { Aim: not null, Blocked: >= 3 })
                spot = at;
        }
        Assert.NotNull(spot);
        Of(s, Foe).PlaceAt(spot!.Value);
        Assert.Equal(CoverDegree.ThreeQuarters, Aim(s).Cover);

        var aim = Aim(s).To!.Value;
        Of(s, Passerby).PlaceAt(new Spot((from.X + aim.X) / 2, (from.Y + aim.Y) / 2));
        Assert.Equal(CoverDegree.ThreeQuarters, Aim(s).Cover); // not three-quarters + half
    }

    [Fact]
    public void Cover_raises_the_armor_class_the_roll_must_beat_but_the_player_learns_the_targets_own()
    {
        // A passer-by on the line: half cover, so the guard's AC 16 is 18 to beat. What the player learns stays about 16.
        var hits = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var s = Yard(seed);
            Of(s, Passerby).PlaceAt(new Spot(330, 210));
            if (!s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Javelin }).Success || LastShot(s) is not { } shot)
                continue;
            Assert.Equal(CoverDegree.Half, shot.Cover);
            Assert.Equal(16 + 2, shot.ArmorClass);
            Assert.Equal(shot.Kept == 20 || shot.Kept != 1 && shot.Total >= 18, shot.Hit);
            if (shot.Hit == true)
                hits++;
            var lore = Of(s, s.Player).Lore.Of(Foe);
            Assert.True(lore.ArmorClassAbove is null || lore.ArmorClassAbove < 16, $"learned above {lore.ArmorClassAbove}");
            Assert.True(lore.ArmorClassAtMost is null || lore.ArmorClassAtMost >= 16, $"learned at most {lore.ArmorClassAtMost}");
            Assert.Contains("(mezza copertura)", shot.Text);
        }
        Assert.True(hits > 0);
    }

    [Fact]
    public void The_preview_the_targets_in_a_fight_and_the_log_agree_on_cover()
    {
        foreach (var by in new[] { new Spot(330, 210), new Spot(330, 240), new Spot(500, 215), new Spot(870, 450) })
        {
            var s = Yard();
            Of(s, Passerby).PlaceAt(by);
            var aim = Aim(s);
            Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Javelin }).Success);
            Assert.Equal(aim.Cover, LastShot(s)!.Cover);
        }
        // In a fight, on the player's turn with its action free: the panel's targets say what the preview says.
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var s = Yard(seed);
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Javelin });
            if (s.GetPlayerView().Combat is not { IsYourTurn: true } || !Of(s, Foe).Conscious)
                continue;
            s.Execute(new EndTurnCommand { Actor = s.Player });
            for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
                s.Advance(Duration.FromSeconds(1));
            if (s.GetPlayerView().Combat is not { IsYourTurn: true, You.ActionAvailable: true } || !Of(s, Foe).Conscious)
                continue;
            var from = Of(s, s.Player).Spot!.Value;
            var to = Of(s, Foe).Spot!.Value;
            Of(s, Passerby).PlaceAt(new Spot((from.X + to.X) / 2, (from.Y + to.Y) / 2));
            var aim = Aim(s);
            Assert.True(aim.Valid, aim.Message);
            Assert.All(s.GetPlayerView().Combat!.Targets.Where(t => t.Target == Foe && t.Attack == Javelin), t => Assert.Equal(aim.Cover, t.Cover));
            return;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    [Fact]
    public void A_record_keeps_its_cover_through_save_and_load_and_older_saves_have_none()
    {
        var s = Yard();
        Of(s, Passerby).PlaceAt(new Spot(330, 210));
        Assert.True(s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Javelin }).Success);
        Assert.Equal(s.SaveToString(), s.SaveAndReload().SaveToString());

        var data = JsonNode.Parse(s.SaveToString())!;
        var log = (data["Encounters"]!.AsArray().FirstOrDefault()?["Log"] ?? data["LastFight"]!["Log"])!.AsArray();
        foreach (var entry in log)
            entry!.AsObject().Remove("Cover");
        Assert.False(LoadFromString(data.ToJsonString()).Success);
        AsSchema(data, 27);
        Assert.True(LoadFromString(data.ToJsonString()).Success);
    }
}
