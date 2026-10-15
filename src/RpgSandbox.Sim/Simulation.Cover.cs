using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// P3: cover (SRD 5.2.1 "Cover"), measured on the real positions.
internal sealed partial class Simulation
{
    /// <summary>Points across a target's body, in the order an attacker aims at them: the centre first.</summary>
    private static readonly int[] AimOffsets = { 0, -9, 9, -18, 18 };

    /// <summary>
    /// P3: how an attacker centred at <paramref name="from"/> can strike a body centred at <paramref name="target"/>. Five
    /// points on each body along the diameter square to the line of centres (0, −9, +9, −18, +18 inches; negative to the
    /// left looking from the attacker), the same on both: the attacker leans out from the point of its body that sees most
    /// points of the target (the first in that order on a tie), and aims at the first point it sees from there. Blocked:
    /// the lines from that origin stopped by something solid. All null when no line from any point is clear (total
    /// cover). Review P3b: the same points both ways, so a shot is possible one way exactly when it is the other way.
    /// </summary>
    internal static (Spot? Origin, Spot? Aim, int Blocked) AimAt(GridMap map, Spot from, Spot target)
    {
        double dx = target.X - from.X, dy = target.Y - from.Y, length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0)
            return (from, target, 0);
        // Left of the direction (dx, dy) on a map whose y grows downwards.
        double lx = dy / length, ly = -dx / length;
        Spot Across(Spot centre, int offset) => new((int)Math.Round(centre.X - lx * offset, MidpointRounding.AwayFromZero),
            (int)Math.Round(centre.Y - ly * offset, MidpointRounding.AwayFromZero));
        var targets = AimOffsets.Select(o => Across(target, o)).ToArray();
        (Spot? Origin, Spot? Aim, int Blocked) best = (null, null, AimOffsets.Length);
        foreach (var offset in AimOffsets)
        {
            var origin = Across(from, offset);
            Spot? aim = null;
            var blocked = 0;
            foreach (var point in targets)
            {
                if (SpotPlanner.LineClear(map, origin, point))
                    aim ??= point;
                else
                    blocked++;
            }
            if (aim is not null && (best.Aim is null || blocked < best.Blocked))
                best = (origin, aim, blocked);
            if (best.Blocked == 0)
                break; // nothing better than all five
        }
        return best;
    }

    /// <summary>P3: some point of the body is in the open (the first clear line is enough; for reach and threat).</summary>
    internal static bool BodyInTheOpen(GridMap map, Spot from, Spot target) =>
        SpotPlanner.LineClear(map, from, target) || AimAt(map, from, target).Aim is not null;

    /// <summary>
    /// P3: the cover a target has against an attack along <paramref name="from"/>→<paramref name="aim"/>: by the lines
    /// stopped (2: half; 3–4: three-quarters), at least half if another creature's body cuts that line or the line
    /// passes an open window (its sill and jambs, the user's decision). The most protective applies; they do not add up.
    /// </summary>
    private CoverDegree CoverOf(GridMap map, Actor attacker, Actor target, Spot from, Spot aim, int blocked)
    {
        var cover = blocked >= 3 ? CoverDegree.ThreeQuarters : blocked == 2 ? CoverDegree.Half : CoverDegree.None;
        if (cover >= CoverDegree.Half)
            return cover;
        var someoneBetween = World.Actors.Values.Any(o => o.Id != attacker.Id && o.Id != target.Id && o.MapArea == map.Area
            && o.Vitality.State != LifeState.Dead && CurrentSpot(o) is { } at && SpotPlanner.PointSegment(at, from, aim) < SpotPlanner.BodyRadius);
        return someoneBetween || ThroughOpenWindow(map, from, aim) ? CoverDegree.Half : CoverDegree.None;
    }

    /// <summary>P3: the line crosses an open window.</summary>
    private static bool ThroughOpenWindow(GridMap map, Spot from, Spot to) =>
        map.Boundaries.Any(e => e.Kind == MapEdgeKind.Window && map.LetsThrough(e.A, e.B)
            && SpotPlanner.Border(e.A, e.B) is var (p, q) && SpotPlanner.Intersect(from, to, p, q));

    /// <summary>P3: the bonus to AC a degree of cover gives (SRD: +2 half, +5 three-quarters).</summary>
    private static int CoverBonus(CoverDegree cover) => cover switch
    {
        CoverDegree.Half => 2,
        CoverDegree.ThreeQuarters => 5,
        _ => 0,
    };

    private static string CoverText(CoverDegree cover) => cover switch
    {
        CoverDegree.Half => " (mezza copertura)",
        CoverDegree.ThreeQuarters => " (tre quarti di copertura)",
        _ => "",
    };
}
