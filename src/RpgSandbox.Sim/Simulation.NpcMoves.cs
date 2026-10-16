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
        if (FearAhead(npc, aim) is not null)
            return null; // R2, SRD Frightened: it does not come closer to what it fears
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
        var open = new SpotPlanner(map, Array.Empty<Spot>(), Array.Empty<Spot>(), closedDoorsBlock: CombatantOf(e, npc.Id).Interacted,
            barredDoors: BarredDoors(npc, map)); // P4d
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

    /// <summary>How far an NPC looks for cover, in inches: rings 30 inches apart (within its movement left).</summary>
    private static readonly int[] CoverSteps = Enumerable.Range(1, 12).Select(k => 30 * k).ToArray();

    /// <summary>
    /// P5d: the points tried on a ring of cover: a multiple of the 16 directions (so those are always among them), no more
    /// than <see cref="CoverGap"/> inches apart along it, so a thin shelter far out (a wall end) falls between two less often.
    /// </summary>
    private static int CoverRingPoints(int radius) => RingPoints * (int)Math.Ceiling(2 * Math.PI * radius / CoverGap / RingPoints);

    /// <summary>P5d: at most a body's width between the points of a ring of cover.</summary>
    private const int CoverGap = 2 * SpotPlanner.BodyRadius;

    /// <summary>
    /// P3b: an NPC about to shoot (no blow in reach, a shot valid from here) may first step, within the movement left and
    /// without a Dash, to a point from which the shot is still valid and it is better covered against its target (as if
    /// the target shot back) than the target is, or from which the shot is no longer at long range. Ranked by the net
    /// advantage (its own cover less the target's; review P3b: a shelter both share is no gain), then no long range, then
    /// the shorter way. A way that would provoke an Opportunity Attack from an enemy it perceives is never taken. Returns
    /// whether it moved.
    /// </summary>
    private bool SeekCover(Encounter e, Actor npc, Actor target, AttackProfile shot)
    {
        var left = MovementLeftInches(e, npc);
        if (left <= 0)
            return false;
        var map = World.Maps[e.Area];
        var from = CurrentSpot(npc)!.Value;
        var there = CurrentSpot(target)!.Value;
        var planner = CombatPlanner(e, npc);
        var candidates = CoverSteps.Where(d => d <= left)
            .SelectMany(d => Enumerable.Range(0, CoverRingPoints(d)).Select(k => k * 2 * Math.PI / CoverRingPoints(d)).Select(a => new Spot(
                (int)Math.Round(from.X + d * Math.Cos(a), MidpointRounding.AwayFromZero),
                (int)Math.Round(from.Y + d * Math.Sin(a), MidpointRounding.AwayFromZero))))
            .Where(p => map.InBounds(p.Cell) && planner.Fits(p)).Distinct().ToList();
        var lengths = planner.LengthsTo(from, candidates);
        var (normal, longest) = shot.Range!.Value;

        // How a point would do: null if the shot would not be valid from there.
        (int Net, bool Long)? Score(Spot p)
        {
            if (p.DistanceTo(there) > longest * Spot.PerFoot
                || !Sees(map, npc, p.Cell, target, there.Cell, LightOn(map, there.Cell), fromSpot: p)
                || AimAt(map, p, there) is not { Origin: { } origin, Aim: { } aim, Blocked: var blocked }
                || AimAt(map, there, p) is not { Origin: { } back, Aim: { } at, Blocked: var hidden })
                return null; // review P3b: lines are mutual, so the second never fails where the first holds
            var own = (int)CoverOf(map, target, npc, back, at, hidden);
            return (own - (int)CoverOf(map, npc, target, origin, aim, blocked), p.DistanceTo(there) > normal * Spot.PerFoot);
        }

        if (Score(from) is not { } here)
            return false;
        var best = candidates.Select((p, i) => (Spot: p, Index: i, Length: lengths[i]))
            .Where(c => c.Length <= left) // only what it can reach is weighed
            .Select(c => (c.Spot, c.Index, c.Length, Score: Score(c.Spot)))
            .Where(c => c.Score is not null)
            .Select(c => (c.Spot, c.Index, c.Length, Score: c.Score!.Value))
            .Where(c => c.Score.Net > here.Net || here.Long && !c.Score.Long && c.Score.Net >= here.Net)
            .OrderByDescending(c => c.Score.Net).ThenBy(c => c.Score.Long).ThenBy(c => c.Length).ThenBy(c => c.Index);
        foreach (var c in best)
        {
            if (planner.Find(from, c.Spot) is not { } way || Math.Ceiling(way.Length - 1e-9) > left || Provokes(e, npc, way))
                continue;
            npc.LastDecision = new DecisionTrace(World.Now, "Scontro: si ripara",
                $"Si sposta per tirare a {target.Name} più al riparo.", Array.Empty<string>());
            WalkFreeInCombat(e, npc, way);
            return true;
        }
        return false;
    }

    /// <summary>P3b: walking this way would let an enemy the mover perceives make an Opportunity Attack.</summary>
    private bool Provokes(Encounter e, Actor mover, SpotPath way)
    {
        var knows = Perceived(e, mover);
        var points = InchByInch(way);
        var standing = StandingOf(mover);
        for (var k = 1; k < points.Count; k++)
        {
            var next = (points[k].Cell, points[k]);
            if (Threats(e, mover, standing, next).Any(knows))
                return true;
            standing = next;
        }
        return false;
    }
}
