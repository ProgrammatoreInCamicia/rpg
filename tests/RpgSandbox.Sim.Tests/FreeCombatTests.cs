using System.Text.Json.Nodes;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using static RpgSandbox.Sim.Tests.TestHelpers;

namespace RpgSandbox.Sim.Tests;

/// <summary>P1b: moving to an exact point on one's turn in a fight (free movement prototype).</summary>
public class FreeCombatTests
{
    private static readonly ActorId Foe = new("foe");
    private const string Sword = "Spada lunga";

    /// <summary>
    /// A 12×8 yard at noon: the player (Paladin, Speed 30) at (2,3), a bandit raider at (3,3); a thin wall with a closed
    /// door runs down x = 8|9 (the door between (8,3) and (9,3)).
    /// </summary>
    private static SimulationSession Arena(ulong seed)
    {
        var b = new ScenarioBuilder().WithSeed(seed)
            .AddArea("a", "Cortile").AddArea("woods", "Bosco")
            .AddLocation("yard", "Cortile", "a").AddLocation("camp", "Campo", "woods")
            .AddMap("a", Enumerable.Repeat("YYYYYYYYYYYY", 8).ToArray(), new Dictionary<char, string> { ['Y'] = "yard" })
            .AddStore("campstore", "Scorte del campo", "camp", 100, ownerFactionId: "bandits")
            .AddRaidingFaction("bandits", "Banditi", "campstore", dailyUpkeep: 1, upkeepTimeOfDay: Duration.FromHours(8),
                foodThreshold: 0, raidAmount: 1, evaluationInterval: Duration.FromHours(24))
            .AddActor("player", "Paladino", "yard", isPlayer: true, at: (2, 3))
            .AddActor("foe", "Bandito", "yard", factionId: "bandits", combat: "bandit", at: (3, 3));
        for (var y = 0; y < 8; y++)
            b.AddEdge("a", (8, y), (9, y), y == 3 ? MapEdgeKind.Door : MapEdgeKind.Wall, y == 3 ? (8, 3) : null);
        var s = SimulationSession.Create(b.Build());
        s.AdvanceTo(At(1, 12));
        return s;
    }

    private static Actor Of(SimulationSession s, ActorId id) => s.Engine.World.Actors[id];

    private static Spot In(int x, int y, int dx = 30, int dy = 30) => new(x * Spot.PerSquare + dx, y * Spot.PerSquare + dy);

    /// <summary>The first seed where the player's blow opens the fight, the bandit stands, and it is the player's turn with its movement whole.</summary>
    private static SimulationSession Fighting()
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var s = Arena(seed);
            s.Execute(new AttackCommand { Actor = s.Player, Target = Foe, Attack = Sword });
            if (s.GetPlayerView().Combat is { IsYourTurn: true } c && c.You.MovementLeftInches == 30 * 12
                && s.GetPlayerView().Position == new GridPos(2, 3) && s.GetPlayerView().Unconscious == false
                && s.GetWorldView().Actor(Foe).Position == new GridPos(3, 3) && Of(s, Foe).Conscious)
                return s;
        }
        throw new InvalidOperationException("No seed gives that scene.");
    }

    private static CommandResult Move(SimulationSession s, Spot to) => s.Execute(new MoveToSpotCommand { Actor = s.Player, To = to });

    [Fact]
    public void A_free_move_in_a_fight_costs_its_length_to_the_inch_and_no_more_than_is_left()
    {
        var s = Fighting();
        var to = In(1, 5); // one square left, two down: √5 squares, about 11.2 feet

        var preview = s.PreviewMoveToSpot(s.Player, to);
        var moved = Move(s, to);

        Assert.True(moved.Success, moved.Message);
        Assert.Equal(to, s.GetPlayerView().Spot);
        var inches = (int)Math.Ceiling(Spot.CenterOf(new GridPos(2, 3)).DistanceTo(to));
        Assert.Equal(30 * 12 - inches, s.GetPlayerView().Combat!.You.MovementLeftInches);
        Assert.Equal(11.2, preview.Feet);
        Assert.Equal(30, preview.FeetLeft);
        Assert.Equal(0, preview.Seconds); // within the turn

        // What is left is not enough to go back and beyond.
        var far = Move(s, In(7, 7));
        Assert.Equal(RejectionReason.TooFar, far.Rejection);
        Assert.Contains("te ne restano 18,8", far.Message);
    }

    [Fact]
    public void Leaving_the_bandits_reach_provokes_its_opportunity_attack_at_the_last_point_in_reach()
    {
        var s = Fighting();
        var to = In(0, 3); // straight away from it, ten feet

        var preview = s.PreviewMoveToSpot(s.Player, to);
        var provoked = Assert.Single(preview.Provokes);
        Assert.Equal(Foe, provoked.By);
        // In reach while the bodies are no more than 5 feet apart edge to edge: 96 inches between centres... or the
        // squares next to each other; the bandit stands at the centre of (3,3), x = 210.
        Assert.True(provoked.At.X >= 210 - 96 - 1 && provoked.At.X <= 120, $"{provoked.At}");

        Assert.True(Move(s, to).Success);
        Assert.Contains(s.GetPlayerView().Combat!.Log, l => l.Kind == CombatLogKind.OpportunityAttack && l.Actor == Foe);
        Assert.Equal(to, s.GetPlayerView().Spot);
    }

    [Fact]
    public void Walking_round_the_bandit_within_its_reach_provokes_nothing_and_disengaging_never_does()
    {
        var s = Fighting();
        // From west of it to north of it, staying close: never out of reach.
        Assert.Empty(s.PreviewMoveToSpot(s.Player, In(3, 2, 30, 10)).Provokes);

        // The action went on the opening blow: the Disengage is granted here, as if from a later turn.
        s.Engine.World.Encounters.Values.Single().Order.Single(c => c.Actor == s.Player).Disengaged = true;
        Assert.Empty(s.PreviewMoveToSpot(s.Player, In(0, 3)).Provokes);
        Assert.True(Move(s, In(0, 3)).Success);
        Assert.DoesNotContain(s.GetPlayerView().Combat!.Log, l => l.Kind == CombatLogKind.OpportunityAttack);
    }

    [Fact]
    public void In_a_fight_a_closed_door_is_a_wall_and_the_foe_cannot_be_walked_through()
    {
        var s = Fighting();
        Assert.Equal(RejectionReason.Unreachable, Move(s, In(10, 3)).Rejection); // only through the closed door
        // Through the bandit, to the square beyond it: the way goes round it.
        var round = s.PreviewMoveToSpot(s.Player, In(4, 3));
        Assert.True(round.Valid, round.Message);
        Assert.True(round.Way.Count >= 3);
        Assert.True(round.Feet > 10);
        Assert.Equal(RejectionReason.NoRoom, Move(s, In(3, 3, 40, 30)).Rejection); // on the bandit
    }

    [Fact]
    public void Reach_counts_the_bodies_as_well_as_the_squares()
    {
        var s = Fighting();
        var map = s.Engine.World.Maps[new AreaId("a")];
        var sword = Simulation.MeleeAttack(Of(s, s.Player))!;
        (GridPos, Spot) At(Spot p) => (p.Cell, p);

        // Two squares apart at their centres: out of reach, as on squares.
        Assert.False(Simulation.InReach(map, At(In(2, 3)), sword, At(In(4, 3))));
        // Two squares apart, but the bodies 5 feet apart edge to edge (96 inches between centres): in reach.
        Assert.True(Simulation.InReach(map, At(In(2, 3, 54, 30)), sword, At(In(4, 3))));
        Assert.False(Simulation.InReach(map, At(In(2, 3, 53, 30)), sword, At(In(4, 3))));
        // Next squares, wherever one stands in its own: in reach (NPCs still think in squares).
        Assert.True(Simulation.InReach(map, At(In(2, 2, 0, 0)), sword, At(In(3, 3))));
        // Never through a wall: the bodies close but the thin wall between (8,y) and (9,y).
        Assert.False(Simulation.InReach(map, At(In(8, 5, 55, 30)), sword, At(In(9, 5, 5, 30))));
    }

    [Fact]
    public void The_inches_spent_survive_a_save_and_an_older_save_counts_whole_feet()
    {
        var s = Fighting();
        Assert.True(Move(s, In(1, 5)).Success);
        var left = s.GetPlayerView().Combat!.You.MovementLeftInches;

        Assert.Equal(left, s.SaveAndReload().GetPlayerView().Combat!.You.MovementLeftInches);

        var data = JsonNode.Parse(s.SaveToString())!;
        var me = data["Encounters"]![0]!["Combatants"]!.AsArray().Single(c => (string)c!["Actor"]! == "player")!;
        me.AsObject().Remove("MovedInches");
        Assert.Contains("movimento del combattente", LoadFromString(data.ToJsonString()).Error);
        data["Version"] = 23;
        me["MovedFeet"] = 10;
        var old = LoadFromString(data.ToJsonString());
        Assert.True(old.Success, old.Error);
        Assert.Equal(20 * 12, old.Session!.GetPlayerView().Combat!.You.MovementLeftInches);
    }
}
