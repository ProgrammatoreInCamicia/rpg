using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// P2a: NPCs move in a fight on exact points, by the same planning and walk as the player's free moves.
internal sealed partial class Simulation
{
    /// <summary>How many directions around a target an NPC tries for a point to strike from (east first, clockwise).</summary>
    private const int RingPoints = 16;

    /// <summary>
    /// P2a: the way an NPC would take towards <paramref name="target"/> (seen now) or towards <paramref name="aim"/> (where it
    /// last saw an enemy), whole, even beyond this turn's movement; and how far along it the NPC may go at most (it may
    /// not walk into a body it cannot cross). Null when it need not move, or has no way.
    /// <list type="bullet">
    /// <item>With a melee attack and a target: a point of a ring around the target from which the blow really reaches
    /// (centres 36 + reach × 12 − 6 inches apart, the body fits, <see cref="InReach"/>), the one with the shortest way;
    /// on a tie, the first direction. Nothing if it already reaches from where it stands.</item>
    /// <item>Otherwise (no point of the ring has a way, a ranged NPC, no target): a way to the target's point around the
    /// walls alone, walked only up to just before it would touch a body it cannot cross. This is a
    /// heuristic, not a search of the whole space: a point in reach between two directions may be missed.</item>
    /// </list>
    /// </summary>
    private (SpotPath Way, int Limit)? PlanNpcApproach(Encounter e, Actor npc, Actor? target, Spot aim)
    {
        var map = World.Maps[e.Area];
        var from = CurrentSpot(npc)!.Value;
        var planner = CombatPlanner(e, npc);
        if (target is not null && MeleeAttack(npc) is { } melee)
        {
            if (InReach(map, StandingOf(npc), melee, StandingOf(target)))
                return null;
            var radius = 2 * SpotPlanner.BodyRadius + melee.ReachFeet * Spot.PerFoot - 6;
            var ring = Enumerable.Range(0, RingPoints)
                .Select(k => k * 2 * Math.PI / RingPoints)
                .Select(a => new Spot((int)Math.Round(aim.X + radius * Math.Cos(a), MidpointRounding.AwayFromZero),
                    (int)Math.Round(aim.Y + radius * Math.Sin(a), MidpointRounding.AwayFromZero)))
                .Where(p => planner.Fits(p) && InReach(map, (p.Cell, p), melee, StandingOf(target)))
                .ToList();
            if (planner.FindNearest(from, ring) is { } best)
                return (best.Way, (int)Math.Ceiling(best.Way.Length - 1e-9));
        }

        // The fallback: towards the point itself along the walls alone; stop before touching a body that cannot be
        // crossed (review P2a: never cross one to stand beyond it).
        var open = new SpotPlanner(map, Array.Empty<Spot>(), Array.Empty<Spot>(), closedDoorsBlock: true);
        if (!open.Fits(aim) || open.Find(from, aim) is not { } way || way.Length < 1)
            return null;
        var points = InchByInch(way);
        var walls = World.Actors.Values
            .Where(o => o.Id != npc.Id && o.MapArea == map.Area && o.Conscious && CurrentSpot(o) is not null
                        && !Allies(e, npc, o) && Perceived(e, npc)(o))
            .Select(o => CurrentSpot(o)!.Value).ToList();
        var limit = points.Count - 1;
        for (var k = 1; k < points.Count; k++)
            if (walls.Any(o => o.DistanceTo(points[k]) < 2 * SpotPlanner.BodyRadius))
            {
                limit = k - 1;
                break;
            }
        return limit > 0 ? (way, limit) : null;
    }

    /// <summary>
    /// P2a: the longest start of <paramref name="way"/>, up to its point <paramref name="limit"/>, that this turn's
    /// movement covers and that ends where the body fits (null if not even an inch).
    /// </summary>
    private SpotPath? NpcStep(Encounter e, Actor npc, SpotPath way, int limit)
    {
        var left = MovementLeftInches(e, npc);
        var planner = CombatPlanner(e, npc);
        var points = InchByInch(way);
        for (var k = Math.Min(limit, Math.Min(left, points.Count - 1)); k > 0; k--)
        {
            if (!planner.Fits(points[k]))
                continue;
            var step = Prefix(way, points, k);
            if (Math.Ceiling(step.Length - 1e-9) <= left)
                return step;
        }
        return null;
    }

    /// <summary>The start of a way up to its inch point <paramref name="k"/>.</summary>
    private static SpotPath Prefix(SpotPath way, IReadOnlyList<Spot> points, int k)
    {
        if (k == points.Count - 1)
            return way;
        var vertices = new List<Spot> { way.Vertices[0] };
        var along = 0.0;
        for (var i = 1; i < way.Vertices.Count; i++)
        {
            along += way.Vertices[i - 1].DistanceTo(way.Vertices[i]);
            if (along >= k)
                break;
            vertices.Add(way.Vertices[i]);
        }
        if (vertices[^1] != points[k])
            vertices.Add(points[k]);
        var length = 0.0;
        for (var i = 1; i < vertices.Count; i++)
            length += vertices[i - 1].DistanceTo(vertices[i]);
        return new SpotPath(vertices, length);
    }
}
