using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>P2b: shots on real positions along a straight line; windows open and closed.</summary>
public class ShotTests
{
    private static readonly ActorId Foe = new("foe");
    private static readonly AreaId Area = new("a");
    private const string Javelin = "Giavellotto (lanciato)";

    /// <summary>A 16×8 yard at noon: the player (Paladin, javelins) at (2,3), a guard at (6,3); a window down x = 4|5 if asked.</summary>
    private static SimulationSession Yard(ulong seed = 1, bool window = false, bool openWindow = false,
        Action<ScenarioBuilder>? more = null)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat(new string('Y', 16), 8).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: (2, 3))
            .AddActor("foe", "Guardia", "yard", combat: "guard", at: (6, 3));
        if (window)
            for (var y = 0; y < 8; y++)
                b.AddEdge("a", (4, y), (5, y), MapEdgeKind.Window, open: openWindow);
        more?.Invoke(b);
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, 12));
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static AttackPreview Aim(SimulationSession s, string attack = Javelin) => s.PreviewAttack(s.Player, Foe, attack);

    private static CommandResult Throw(SimulationSession s, string attack = Javelin) =>
        s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = attack });

    [Fact]
    public void A_shot_does_not_go_through_a_wall_even_where_the_target_is_seen_round_its_end_and_can_be_walked_to()
    {
        // A short wall down x = 4|5 over rows 2–4. Sight is traced square to square and slips past a corner; a shot flies
        // straight from centre to centre. Somewhere the guard is seen but the straight line touches the wall.
        var s = Yard(more: b =>
        {
            for (var y = 2; y <= 4; y++)
                b.AddEdge("a", (4, y), (5, y), MapEdgeKind.Wall);
        });
        var map = s.Engine.World.Maps[Area];
        var found = false;
        for (var y = 0; y < 8 && !found; y++)
        for (var x = 5; x < 12 && !found; x++)
        {
            Of(s, Foe).PlaceAt(Spot.CenterOf(new GridPos(x, y)));
            if (!s.Engine.SeesActor(Of(s, s.Player), Of(s, Foe)) || SpotPlanner.LineClear(map, Of(s, s.Player).Spot!.Value, Of(s, Foe).Spot!.Value))
                continue;
            found = true;
            var aim = Aim(s);
            Assert.False(aim.Valid);
            Assert.Equal(RejectionReason.NoLineOfFire, aim.Reason);
            Assert.Equal(RejectionReason.NoLineOfFire, Throw(s).Rejection);
            Assert.True(s.PreviewMoveToSpot(s.Player, new Spot(Of(s, Foe).Spot!.Value.X - 60, Of(s, Foe).Spot!.Value.Y)).Valid
                        || s.PreviewMoveToSpot(s.Player, new Spot(Of(s, Foe).Spot!.Value.X, Of(s, Foe).Spot!.Value.Y + 60)).Valid,
                "a walk to it exists");
        }
        Assert.True(found, "no square is seen round the wall's end without a straight line");
    }

    [Fact]
    public void Through_a_closed_window_the_target_is_seen_but_not_hit_open_it_and_both_a_shot_and_a_blow_pass()
    {
        var s = Yard(window: true);
        Assert.True(s.Engine.SeesActor(Of(s, s.Player), Of(s, Foe)));
        Assert.Equal(RejectionReason.NoLineOfFire, Aim(s).Reason);

        // Too far to open it from (2,3); not a window between (2,3) and (3,3); from (4,3) it opens.
        var open = new WindowCommand { Actor = s.Player, A = new GridPos(4, 3), B = new GridPos(5, 3), Open = true };
        Assert.Equal(RejectionReason.OutOfReach, s.Execute(open).Rejection);
        Assert.Equal(RejectionReason.NotAWindow,
            s.Execute(open with { A = new GridPos(2, 3), B = new GridPos(3, 3) }).Rejection);
        Of(s, s.Player).PlaceAt(Spot.CenterOf(new GridPos(4, 3)));
        Assert.True(s.Execute(open).Success);
        Assert.Equal(RejectionReason.AlreadyDone, s.Execute(open).Rejection);
        Assert.Contains(s.GetWorldView().RecentFacts, f => f.Kind == "WindowOpened");
        Assert.True(s.GetPlayerView().Map!.Edges.Single(e => e.A == new GridPos(4, 3) && e.B == new GridPos(5, 3)).Open);
        Assert.False(s.GetPlayerView().Map!.Edges.Single(e => e.A == new GridPos(4, 2)).Open);

        Assert.True(Aim(s).Valid, Aim(s).Message);
        // A blow too, from next to it: the guard steps up to the window.
        Of(s, Foe).PlaceAt(Spot.CenterOf(new GridPos(5, 3)));
        Assert.True(Aim(s, "Spada lunga").Valid, Aim(s, "Spada lunga").Message);
        Assert.True(s.Execute(open with { Open = false }).Success);
        Assert.Equal(RejectionReason.TooFar, Aim(s, "Spada lunga").Reason);
        Assert.Equal(RejectionReason.NoLineOfFire, Aim(s).Reason);
    }

    [Fact]
    public void Two_targets_in_the_same_square_can_stand_either_side_of_the_normal_range()
    {
        // Javelin: 30 feet normal, 120 long. 360 inches from centre to centre is within the normal range, 361 beyond.
        var s = Yard();
        Of(s, s.Player).PlaceAt(new Spot(100, 210));
        Of(s, Foe).PlaceAt(new Spot(460, 210));
        Assert.Equal(Of(s, Foe).Position, new Spot(461, 210).Cell);
        var near = Aim(s);
        Of(s, Foe).PlaceAt(new Spot(461, 210));
        var far = Aim(s);

        Assert.True(near.Valid && far.Valid);
        Assert.False(near.LongRange);
        Assert.True(far.LongRange);
        Assert.True(far.Disadvantage);
        Assert.Equal(new Spot(100, 210), far.From);
        Assert.Equal(new Spot(461, 210), far.To);
    }

    [Fact]
    public void An_enemy_within_5_feet_body_to_body_gives_disadvantage_to_a_shot_one_farther_does_not_whatever_the_squares()
    {
        var s = Yard();
        // Chosen so the squares would say the opposite: next squares but 6.75 feet apart; two squares apart but 4 feet.
        Of(s, s.Player).PlaceAt(new Spot(121, 210));
        Of(s, Foe).PlaceAt(new Spot(121 + 36 + 81, 210));
        Assert.Equal(1, Of(s, s.Player).Position!.Value.StepsTo(Of(s, Foe).Position!.Value));
        Assert.False(Aim(s).Disadvantage);
        Of(s, s.Player).PlaceAt(new Spot(179, 210));
        Of(s, Foe).PlaceAt(new Spot(179 + 36 + 48, 210));
        Assert.Equal(2, Of(s, s.Player).Position!.Value.StepsTo(Of(s, Foe).Position!.Value));
        Assert.True(Aim(s).Disadvantage);
    }

    [Fact]
    public void The_preview_and_the_command_agree_on_a_sample_of_positions()
    {
        var spots = new[] { new Spot(450, 210), new Spot(270, 90), new Spot(930, 450), new Spot(500, 300), new Spot(310, 215), new Spot(250, 210) };
        foreach (var window in new[] { false, true })
        foreach (var at in spots)
        {
            var s = Yard(window: window);
            Of(s, Foe).PlaceAt(at);
            var aim = Aim(s);
            var done = Throw(s);
            Assert.Equal(aim.Valid, done.Success);
            Assert.Equal(aim.Reason, done.Rejection);
        }
    }

    [Fact]
    public void A_shot_flies_from_the_attackers_centre_to_the_target_if_it_hits_and_past_it_to_the_first_wall_if_it_misses()
    {
        // A wall down x = 12|13: a miss flies on until it.
        bool hit = false, miss = false;
        for (ulong seed = 1; seed <= 200 && !(hit && miss); seed++)
        {
            var s = Yard(seed, more: b =>
            {
                for (var y = 0; y < 8; y++)
                    b.AddEdge("a", (12, y), (13, y), MapEdgeKind.Wall);
            });
            Throw(s);
            var shot = s.Engine.World.Encounters.Values.Single().Log.LastOrDefault(l => l.Kind == CombatLogKind.Attack && l.Actor == s.Player);
            if (shot is null)
                continue;
            var map = s.Engine.World.Maps[Area];
            Assert.Equal(Spot.CenterOf(new GridPos(2, 3)), shot.ShotFrom);
            if (shot.Hit == true)
            {
                hit = true;
                Assert.Equal(Spot.CenterOf(new GridPos(6, 3)), shot.ShotTo);
            }
            else
            {
                miss = true;
                var to = shot.ShotTo!.Value;
                Assert.True(SpotPlanner.LineClear(map, shot.ShotFrom!.Value, to));
                Assert.InRange(to.X, 13 * 60 - 2, 13 * 60); // stopped at the wall, between columns 12 and 13
                Assert.NotEqual(210, to.Y); // beside the line to the target, not through it
                Assert.Equal(shot.D20.Count == 0 ? 0 : shot.Kept!.Value % 2 == 0 ? 1 : -1, Math.Sign(to.Y - 210) * 1);
            }
        }
        Assert.True(hit && miss);
    }

    [Fact]
    public void A_window_state_and_a_shot_survive_save_and_load_and_older_saves_have_closed_windows()
    {
        var s = Yard(window: true, openWindow: true);
        Assert.True(Throw(s).Success);
        Assert.True(s.Engine.World.Maps[Area].IsOpenWindow(new GridPos(4, 3), new GridPos(5, 3)));

        var loaded = s.SaveAndReload();
        Assert.Equal(s.SaveToString(), loaded.SaveToString());
        Assert.True(loaded.Engine.World.Maps[Area].IsOpenWindow(new GridPos(4, 3), new GridPos(5, 3)));
        Assert.Equal(s.GetPlayerView().Combat!.Log.Last(l => l.Kind == CombatLogKind.Attack).ShotTo,
            loaded.GetPlayerView().Combat!.Log.Last(l => l.Kind == CombatLogKind.Attack).ShotTo);

        var data = JsonNode.Parse(s.SaveToString())!;
        AsSchema(data, 26);
        foreach (var edge in data["Maps"]![0]!["Edges"]!.AsArray())
            edge!.AsObject().Remove("Open");
        foreach (var entry in data["Encounters"]![0]!["Log"]!.AsArray())
        {
            entry!.AsObject().Remove("ShotFrom");
            entry.AsObject().Remove("ShotTo");
        }
        var old = LoadFromString(data.ToJsonString());
        Assert.True(old.Success, old.Error);
        Assert.False(old.Session!.Engine.World.Maps[Area].IsOpenWindow(new GridPos(4, 3), new GridPos(5, 3)));

        var bad = JsonNode.Parse(s.SaveToString())!;
        bad["Maps"]![0]!["Edges"]![0]!.AsObject().Remove("Open");
        Assert.False(LoadFromString(bad.ToJsonString()).Success);
    }

    [Fact]
    public void The_last_shot_of_a_fight_stays_readable_once_the_fight_is_over_and_survives_save_and_load()
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var s = Yard(seed);
            s.Engine.World.Actors[Foe].Vitality = new Vitality(11, false, 1);
            Throw(s);
            if (s.GetPlayerView().Combat is not null || Of(s, Foe).Conscious)
                continue; // the javelin missed
            var last = s.GetPlayerView().LastFight!;
            var shot = last.Last(l => l.Kind == CombatLogKind.Attack);
            Assert.Equal(Spot.CenterOf(new GridPos(2, 3)), shot.ShotFrom);
            Assert.Equal(Spot.CenterOf(new GridPos(6, 3)), shot.ShotTo);
            Assert.Equal(CombatLogKind.Ended, last[^1].Kind);
            Assert.Equal(last.Count, s.SaveAndReload().GetPlayerView().LastFight!.Count);
            Assert.Equal(s.SaveToString(), s.SaveAndReload().SaveToString());
            return;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    [Fact]
    public void An_npcs_last_blow_stays_readable_too_with_who_struck_although_it_knocked_the_player_out()
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Yard(seed);
            Of(s, Foe).PlaceAt(Spot.CenterOf(new GridPos(3, 3)));
            s.Engine.World.Actors[s.Player].Vitality = new Vitality(11, true, 1);
            s.Execute(new AttackCommand { Actor = Foe, Target = s.Player, Attack = "Lancia" });
            for (var i = 0; i < 30 && s.GetPlayerView().Combat is not null; i++)
            {
                if (s.GetPlayerView().Combat!.IsYourTurn)
                    s.Execute(new EndTurnCommand { Actor = s.Player });
                s.Advance(Duration.FromSeconds(1));
            }
            if (s.GetPlayerView().Combat is not null || Of(s, s.Player).Conscious)
                continue;
            var last = s.GetPlayerView().LastFight;
            Assert.NotNull(last);
            Assert.Contains(last!, l => l.Kind == CombatLogKind.Attack && l.Actor == Foe && l.Hit == true);
            return;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    [Fact]
    public void The_attack_preview_never_gives_away_where_an_unseen_creature_stands()
    {
        var s = Yard(more: b =>
        {
            for (var y = 0; y < 8; y++)
                b.AddEdge("a", (4, y), (5, y), MapEdgeKind.Wall);
        });
        var aim = Aim(s);
        Assert.Equal(RejectionReason.NotVisible, aim.Reason);
        Assert.Null(aim.To);
        Assert.Equal(Spot.CenterOf(new GridPos(2, 3)), aim.From);
        Assert.Null(s.PreviewAttack(Foe, s.Player, "Lancia").From);

        Of(s, Foe).PlaceAt(Spot.CenterOf(new GridPos(3, 3)));
        Assert.Equal(Spot.CenterOf(new GridPos(3, 3)), Aim(s).To);
    }
}
