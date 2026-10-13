using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>P2a: NPCs move in a fight on exact points, by the same planning and walk as the player's free moves.</summary>
public class NpcFreeMoveTests
{
    private static readonly ActorId Foe = new("foe");
    private static readonly ActorId Passerby = new("passerby");

    /// <summary>A yard at noon with the player (Paladin) and a guard (a spear, kept in hand), and what else is asked for.</summary>
    private static SimulationSession Scene(ulong seed, string[] rows, (int X, int Y) player, (int X, int Y) foe,
        Action<ScenarioBuilder>? more = null)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", rows, new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: player)
            .AddActor("foe", "Guardia", "yard", combat: "guard", at: foe);
        more?.Invoke(b);
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, 12));
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static Combatant CombatantOf(SimulationSession s, ActorId id) =>
        s.Engine.World.Encounters.Values.Single().Order.Single(c => c.Actor == id);

    private static bool YourTurnWhole(SimulationSession s) =>
        s.GetPlayerView().Combat is { IsYourTurn: true } c && c.You.MovementLeftInches == 30 * 12 && Of(s, s.Player).Conscious
        && Of(s, Foe).Conscious;

    private static List<CombatRecord> Records(SimulationSession s) => s.Engine.World.Encounters.Values.Single().Log;

    /// <summary>Ends the player's turn and lets the round run (6 seconds a round) up to its next turn.</summary>
    private static void EndTurn(SimulationSession s)
    {
        Assert.True(s.Execute(new EndTurnCommand { Actor = s.Player }).Success);
        for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
            s.Advance(Duration.FromSeconds(1));
    }

    private static double Length(IReadOnlyList<Spot> spots) => spots.Zip(spots.Skip(1), (a, b) => a.DistanceTo(b)).Sum();

    private static string[] Yard(int width, int height) => Enumerable.Repeat(new string('Y', width), height).ToArray();

    [Fact]
    public void An_npc_goes_round_a_window_it_cannot_strike_through_stops_where_its_blow_reaches_and_strikes()
    {
        // A window runs down x = 4|5 from the top to row 5: sight passes, bodies and blows do not.
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Scene(seed, Yard(12, 8), player: (6, 3), foe: (7, 3), b =>
            {
                for (var y = 0; y <= 5; y++)
                    b.AddEdge("a", (4, y), (5, y), MapEdgeKind.Window);
            });
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Spada lunga" });
            if (!YourTurnWhole(s))
                continue;
            // Round the end of the window to just behind it, across from where the guard stands.
            if (!s.Execute(new MoveToSpotCommand { Actor = s.Player, To = new Spot(270, 270) }).Success || !YourTurnWhole2(s))
                continue;
            var before = Records(s).Count;

            EndTurn(s);

            var log = Records(s).Skip(before).ToList();
            var move = log.Single(l => l.Kind == CombatLogKind.Move && l.Actor == Foe);
            var at = Of(s, Foe).Spot!.Value;
            var map = s.Engine.World.Maps[new AreaId("a")];
            Assert.True(Simulation.InReach(map, (at.Cell, at), Simulation.MeleeAttack(Of(s, Foe))!, (new GridPos(4, 4), new Spot(270, 270))), $"{at}");
            Assert.True(move.Spots.Count >= 3, "it went round the end of the window");
            Assert.Equal(at, move.Spots[^1]);
            // The same cost as the player's free move: its length, rounded up to the inch.
            Assert.Equal((int)Math.Ceiling(Length(move.Spots) - 1e-9), CombatantOf(s, Foe).MovedInches);
            Assert.Contains(log, l => l.Kind == CombatLogKind.Attack && l.Actor == Foe);
            return;
        }
        throw new InvalidOperationException("No seed gives that scene.");

        static bool YourTurnWhole2(SimulationSession s) => s.GetPlayerView().Combat is { IsYourTurn: true } && s.GetPlayerView().Unconscious == false;
    }

    [Fact]
    public void With_a_bystander_in_the_straight_way_an_npc_strikes_from_another_point_round_its_target()
    {
        // The guard east of the player, a passer-by between them: straight on it would stop short of the passer-by, out of
        // reach; a point of the ring round the player, past the passer-by, lets it strike this turn.
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Scene(seed, Yard(12, 8), player: (3, 3), foe: (8, 3), b => b.AddActor("passerby", "Passante", "yard", at: (5, 3)));
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Giavellotto (lanciato)" });
            if (!YourTurnWhole(s) || Of(s, Foe).Spot != Spot.CenterOf(new GridPos(8, 3)) || Of(s, Passerby).Spot != Spot.CenterOf(new GridPos(5, 3)))
                continue;
            var before = Records(s).Count;

            EndTurn(s);

            var at = Of(s, Foe).Spot!.Value;
            Assert.True(at.DistanceTo(new Spot(210, 210)) - 36 <= 60, $"{at}");
            Assert.Contains(Records(s).Skip(before), l => l.Kind == CombatLogKind.Attack && l.Actor == Foe);
            return;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    [Fact]
    public void In_a_corridor_blocked_by_a_bystander_an_npc_closes_in_and_stops_before_the_first_body_without_striking()
    {
        // A corridor one square wide: the player at its west end, a passer-by two squares east of it, the guard far east.
        // No point round the player fits (walls and the passer-by), so the guard walks up to just before the passer-by.
        var rows = new[] { "############", "YYYYYYYYYYYY", "############" };
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Scene(seed, rows, player: (1, 1), foe: (9, 1),
                b => b.AddActor("passerby", "Passante", "yard", at: (3, 1)));
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Giavellotto (lanciato)" });
            if (!YourTurnWhole(s) || Of(s, Passerby).Spot != Spot.CenterOf(new GridPos(3, 1)))
                continue;
            var before = Records(s).Count;

            EndTurn(s);

            var at = Of(s, Foe).Spot!.Value;
            Assert.True(at.X >= 210 + 36 && at.X <= 210 + 36 + 2, $"{at}"); // just short of the passer-by, not beyond
            Assert.Equal(90, at.Y);
            Assert.DoesNotContain(Records(s).Skip(before), l => l.Kind == CombatLogKind.Attack && l.Actor == Foe);
            return;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    [Fact]
    public void The_log_shows_an_npcs_move_only_from_where_the_player_saw_it()
    {
        // A wall down x = 5|6 from the top to row 5. The guard, hidden behind it, goes to where it last saw the player,
        // round the end of the wall: the log keeps only the stretch after it came into the player's sight.
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Scene(seed, Yard(12, 8), player: (3, 3), foe: (4, 3), b =>
            {
                for (var y = 0; y <= 5; y++)
                    b.AddEdge("a", (5, y), (6, y), MapEdgeKind.Wall);
            });
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Spada lunga" });
            if (!YourTurnWhole(s))
                continue;
            var start = new Spot(510, 150);
            Of(s, Foe).PlaceAt(start);
            CombatantOf(s, Foe).LastSeenAt = new Spot(210, 330);
            Assert.False(s.Engine.SeesActor(Of(s, s.Player), Of(s, Foe)), $"{Of(s, s.Player).Position} {Of(s, Foe).Position} {Of(s, Foe).Spot}");
            var before = Records(s).Count;

            EndTurn(s);

            var move = Records(s).Skip(before).Single(l => l.Kind == CombatLogKind.Move && l.Actor == Foe);
            var map = s.Engine.World.Maps[new AreaId("a")];
            Assert.NotEmpty(move.Spots);
            Assert.NotEqual(start, move.Spots[0]);
            Assert.Equal(Of(s, Foe).Spot, move.Spots[^1]);
            Assert.All(move.Spots, p => Assert.True(map.HasLineOfSight(new GridPos(3, 3), p.Cell), $"{p} was out of sight"));
            Assert.All(move.Path, c => Assert.True(map.HasLineOfSight(new GridPos(3, 3), c), $"{c} was out of sight"));
            return;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    [Fact]
    public void Where_an_npc_last_saw_an_enemy_and_the_points_of_a_move_survive_save_and_load()
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Scene(seed, Yard(12, 8), player: (2, 3), foe: (8, 3));
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = "Giavellotto (lanciato)" });
            if (!YourTurnWhole(s) || CombatantOf(s, Foe).LastSeenAt is null
                || !s.GetPlayerView().Combat!.Log.Any(l => l.Kind == CombatLogKind.Move && l.Actor == Foe && l.Spots.Count >= 2))
                continue;
            CombatantOf(s, Foe).LastSeenAt = new Spot(200, 211);

            var loaded = s.SaveAndReload();

            Assert.Equal(s.SaveToString(), loaded.SaveToString());
            Assert.Equal(new Spot(200, 211), CombatantOf(loaded, Foe).LastSeenAt);
            Assert.Equal(s.GetPlayerView().Combat!.Log.Select(l => l.Spots.Count), loaded.GetPlayerView().Combat!.Log.Select(l => l.Spots.Count));

            // Schema 25 kept the square: it is read as its centre, and old moves have no points.
            var data = JsonNode.Parse(s.SaveToString())!;
            AsSchema(data, 25);
            var foe = data["Encounters"]![0]!["Combatants"]!.AsArray().Single(c => (string)c!["Actor"]! == "foe")!.AsObject();
            foe.Remove("LastSeenAt");
            foe["LastSeenEnemy"] = new JsonObject { ["X"] = 3, ["Y"] = 3 };
            foreach (var entry in data["Encounters"]![0]!["Log"]!.AsArray())
                entry!.AsObject().Remove("Spots");
            var old = LoadFromString(data.ToJsonString());
            Assert.True(old.Success, old.Error);
            Assert.Equal(Spot.CenterOf(new GridPos(3, 3)), CombatantOf(old.Session!, Foe).LastSeenAt);
            Assert.All(old.Session!.GetPlayerView().Combat!.Log, l => Assert.Empty(l.Spots));
            return;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }
}
