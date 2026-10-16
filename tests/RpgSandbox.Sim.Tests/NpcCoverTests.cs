using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>P3b: an NPC about to shoot may first step into cover, within its movement and without provoking.</summary>
public class NpcCoverTests
{
    private static readonly ActorId Bandit = new("foe");
    private const string Javelin = "Giavellotto (lanciato)";

    /// <summary>A yard at noon: the player (Paladin, javelins) and a bandit (scimitar and light crossbow).</summary>
    private static SimulationSession Yard(ulong seed, int width, (int X, int Y) player, (int X, int Y) bandit,
        Action<ScenarioBuilder>? more = null)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "A").AddLocation("yard", "Cortile", "a")
            .AddMap("a", Enumerable.Repeat(new string('Y', width), 8).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddActor("player", "Paladino", "yard", isPlayer: true, sheet: Sheets.Paladin(), at: player)
            .AddActor("foe", "Bandito", "yard", combat: "bandit", at: bandit);
        more?.Invoke(b);
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, 12));
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static List<CombatRecord> Log(SimulationSession s) =>
        s.Engine.World.Encounters.Values.SingleOrDefault()?.Log ?? s.Engine.World.LastFight?.Log.ToList() ?? new();

    /// <summary>
    /// The first seed where the player's javelin opens the fight, the bandit stands and it is the player's turn; then the
    /// player ends it and the round runs. Returns the session and the bandit's lines of that turn.
    /// </summary>
    private static (SimulationSession S, List<CombatRecord> Turn) BanditsTurn(Func<ulong, SimulationSession> scene)
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var s = scene(seed);
            s.Execute(new AttackCommand { Actor = s.Player, Target = Bandit, Attack = Javelin });
            if (s.GetPlayerView().Combat is not { IsYourTurn: true } || !Of(s, Bandit).Conscious
                || Log(s).Any(l => l.Actor == Bandit && l.Kind is CombatLogKind.Move or CombatLogKind.Attack))
                continue; // the bandit must not have acted yet
            var before = Log(s).Count;
            s.Execute(new EndTurnCommand { Actor = s.Player });
            for (var i = 0; i < 12 && s.GetPlayerView().Combat is { IsYourTurn: false }; i++)
                s.Advance(Duration.FromSeconds(1));
            var turn = Log(s).Skip(before).Where(l => l.Actor == Bandit && l.Kind is CombatLogKind.Move or CombatLogKind.Attack or CombatLogKind.Dash).ToList();
            if (turn.Any(l => l.Kind == CombatLogKind.Attack) && Of(s, Bandit).Conscious)
                return (s, turn);
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    [Fact]
    public void A_bandit_with_a_wall_end_at_hand_steps_behind_it_and_shoots_from_cover()
    {
        // A wall down x = 9|10 from the top to row 2 (it ends at y = 180). In the open at (10,3) the bandit has no cover
        // from the player at (2,3); a little north, the wall's end hides part of its body.
        var (s, turn) = BanditsTurn(seed => Yard(seed, 16, player: (2, 3), bandit: (10, 3), b =>
        {
            for (var y = 0; y <= 2; y++)
                b.AddEdge("a", (9, y), (10, y), MapEdgeKind.Wall);
        }));

        Assert.Equal(CombatLogKind.Move, turn[0].Kind);
        Assert.Equal(CombatLogKind.Attack, turn[1].Kind);
        Assert.Equal("Balestra leggera", turn[1].Attack);
        Assert.NotEqual(Spot.CenterOf(new GridPos(10, 3)), Of(s, Bandit).Spot);
        Assert.NotEqual(CoverDegree.None, s.PreviewAttack(s.Player, Bandit, Javelin).Cover);
        // Review P3b: a net gain, not a shelter it shares with its target.
        Assert.True(s.PreviewAttack(s.Player, Bandit, Javelin).Cover > s.PreviewAttack(Bandit, s.Player, "Balestra leggera").Cover);
        Assert.Equal("Scontro: si ripara", Of(s, Bandit).LastDecision!.Rule); // the reason survives the shot
    }

    [Fact]
    public void With_no_cover_anywhere_a_bandit_shoots_from_where_it_stands_as_before()
    {
        var (s, turn) = BanditsTurn(seed => Yard(seed, 16, player: (2, 3), bandit: (10, 3)));

        Assert.DoesNotContain(turn, l => l.Kind == CombatLogKind.Move);
        Assert.Contains(turn, l => l.Kind == CombatLogKind.Attack && l.Attack == "Balestra leggera");
        Assert.Equal(Spot.CenterOf(new GridPos(10, 3)), Of(s, Bandit).Spot);
    }

    [Fact]
    public void Cover_beyond_its_movement_is_not_sought_with_a_dash()
    {
        // The only wall is over 30 feet away from the bandit: out of its movement, so it shoots from where it stands.
        var (s, turn) = BanditsTurn(seed => Yard(seed, 24, player: (2, 3), bandit: (10, 3), b =>
        {
            for (var y = 0; y <= 2; y++)
                b.AddEdge("a", (17, y), (18, y), MapEdgeKind.Wall);
        }));

        Assert.DoesNotContain(turn, l => l.Kind == CombatLogKind.Move || l.Kind == CombatLogKind.Dash);
        Assert.Equal(Spot.CenterOf(new GridPos(10, 3)), Of(s, Bandit).Spot);
    }

    [Fact]
    public void At_long_range_a_bandit_steps_in_to_its_normal_range_before_shooting()
    {
        // Light crossbow: 80 feet normal (960 inches). From 990 inches it would shoot at long range; 60 inches closer, not.
        var (s, turn) = BanditsTurn(seed =>
        {
            var x = Yard(seed, 24, player: (1, 3), bandit: (17, 3));
            x.Engine.World.Actors[Bandit].PlaceAt(new Spot(90 + 990, 210));
            return x;
        });

        Assert.Equal(CombatLogKind.Move, turn[0].Kind);
        var shot = turn.Single(l => l.Kind == CombatLogKind.Attack);
        Assert.DoesNotContain("gittata lunga", shot.Text);
        Assert.True(Of(s, Bandit).Spot!.Value.DistanceTo(Of(s, s.Player).Spot!.Value) <= 960);
    }

    [Fact]
    public void A_shelter_it_would_share_with_its_target_is_no_reason_to_move()
    {
        // A wall down x = 8|9 with a gap at row 5 and open windows over rows 0–2. Through the gap neither has cover; through a
        // window both have half (its sill). Moving there would cover the bandit as much as the player: no net gain.
        var (s, turn) = BanditsTurn(seed => Yard(seed, 16, player: (2, 5), bandit: (12, 5), b =>
        {
            for (var y = 0; y < 8; y++)
                if (y != 5)
                    b.AddEdge("a", (8, y), (9, y), y <= 2 ? MapEdgeKind.Window : MapEdgeKind.Wall, open: y <= 2);
        }));

        var own = s.PreviewAttack(s.Player, Bandit, Javelin).Cover;
        var theirs = s.PreviewAttack(Bandit, s.Player, "Balestra leggera").Cover;
        Assert.True(!turn.Any(l => l.Kind == CombatLogKind.Move) || own > theirs, $"moved to {Of(s, Bandit).Spot}: its cover {own}, the player's {theirs}");
    }

    [Fact]
    public void It_does_not_step_into_cover_if_the_way_provokes_an_opportunity_attack()
    {
        // A bandit without its scimitar, next to the player: it can only shoot. The cover past the wall's end lies out of the
        // player's reach, so the way there would provoke its Opportunity Attack: it shoots from where it stands.
        var (s, turn) = BanditsTurn(seed =>
        {
            var x = Yard(seed, 16, player: (2, 3), bandit: (3, 3), b =>
            {
                for (var y = 0; y <= 2; y++)
                    b.AddEdge("a", (7, y), (8, y), MapEdgeKind.Wall);
            });
            x.Engine.World.Actors[Bandit].Give("scimitar", -1);
            x.Engine.World.Actors[Bandit].PlaceAt(new Spot(210, 182)); // due east of it lies a strip of cover, out of reach
            return x;
        });

        Assert.Contains(turn, l => l.Kind == CombatLogKind.Attack && l.Attack == "Balestra leggera");
        var log = Log(s);
        Assert.DoesNotContain(log, l => l.Kind == CombatLogKind.OpportunityAttack);
        Assert.True(Of(s, Bandit).Spot!.Value.DistanceTo(Of(s, s.Player).Spot!.Value) - 36 <= 60, "it stayed within the player's reach");
    }

    [Fact]
    public void A_thin_shelter_far_off_between_the_sixteen_directions_is_found()
    {
        // P5d: one wall section down x = 14|15 at row 2, about 25 feet away and off the 16 directions from the bandit at
        // (10,3): only the denser rings far out reach behind it.
        var (s, turn) = BanditsTurn(seed => Yard(seed, 20, player: (2, 3), bandit: (10, 3), b =>
            b.AddEdge("a", (14, 2), (15, 2), MapEdgeKind.Wall)));

        Assert.Equal(CombatLogKind.Move, turn[0].Kind);
        Assert.Equal(CombatLogKind.Attack, turn[1].Kind);
        Assert.True(Of(s, Bandit).Spot!.Value.X > 15 * 60); // behind the wall section, seen from the player
        Assert.True(s.PreviewAttack(s.Player, Bandit, Javelin).Cover > s.PreviewAttack(Bandit, s.Player, "Balestra leggera").Cover);
    }
}
