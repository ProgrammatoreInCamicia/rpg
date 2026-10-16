using RpgSandbox.Sim.Api;

namespace RpgSandbox.Sim;

// P5a: a raider with no way on foot gets through the doors and windows that hold, by the player's own actions (same
// times, checks, noise and BreakIn deeds).
internal sealed partial class Simulation
{
    /// <summary>P5a: tries at picking or forcing one door or window before a raider gives the raid up (a failed one is repeated once).</summary>
    private const int BreakInTriesAllowed = 2;

    /// <summary>
    /// P5a: the next step of a raider that cannot walk to any of <paramref name="goals"/> on its map: the shortest way that
    /// also goes through the doors that hold and the windows without bars (a window counts as
    /// <see cref="GridMap.WindowCrossingSteps"/> squares), walked up to its first barrier, then the work on it:
    /// <list type="bullet">
    /// <item>a door: picked if locked and the raider has thieves' tools, otherwise forced;</item>
    /// <item>a window: climbed through if open or broken; otherwise opened (<see cref="ExecuteBreakIn"/> breaks the glass
    /// if it holds).</item>
    /// </list>
    /// With <paramref name="counting"/> (the way in), picking and forcing are counted per barrier: past
    /// <see cref="BreakInTriesAllowed"/> on the same one, <paramref name="balked"/> is set and nothing is returned. Null
    /// also when there is no way even through the barriers.
    /// </summary>
    private Command? BreakInStep(Actor npc, IEnumerable<GridPos> goals, RaidAssignment? counting, out bool balked)
    {
        balked = false;
        if (MapOf(npc) is not { } map || CurrentPosition(npc) is not { } from)
            return null;
        var opens = Opens(npc, map);
        var route = goals
            .Select(g => (Goal: g, Path: map.FindPath(from, g, opens: _ => true,
                climbs: (a, b) => map.BarrierOf(BarrierKey.Window(a, b)) is not { Barred: true })))
            .Where(c => c.Path is not null)
            .OrderBy(c => WayCost(map, from, c.Path!)).ThenBy(c => c.Goal.Y).ThenBy(c => c.Goal.X) // review P5a: by cost, not squares
            .Select(c => c.Path)
            .FirstOrDefault();
        if (route is null || route.Count == 0)
            return null;

        var standing = from;
        foreach (var next in route)
        {
            BarrierKey? key = map.IsWindow(standing, next) ? BarrierKey.Window(standing, next)
                : map.ClosedDoorBetween(standing, next) is { } door && map.DoorHolds(door) && !opens(door) ? BarrierKey.Door(door)
                : null;
            if (key is not { } barrier)
            {
                standing = next;
                continue;
            }
            if (standing != from)
                return new MoveCommand { Actor = npc.Id, To = standing, Stealthy = true };
            return BarrierWorkFor(npc, map, barrier, counting, out balked);
        }
        return new MoveCommand { Actor = npc.Id, To = route[^1], Stealthy = true }; // no barrier left on the way
    }

    /// <summary>Review P5a: what a way costs as <see cref="GridMap.FindPath"/> counts it: a square a step, a window crossed more.</summary>
    private static int WayCost(GridMap map, GridPos from, IReadOnlyList<GridPos> path)
    {
        var cost = 0;
        foreach (var next in path)
        {
            cost += map.IsWindow(from, next) ? GridMap.WindowCrossingSteps : 1;
            from = next;
        }
        return cost;
    }

    /// <summary>P5a: what a raider standing at <paramref name="key"/> does to get through it.</summary>
    private Command? BarrierWorkFor(Actor npc, GridMap map, BarrierKey key, RaidAssignment? counting, out bool balked)
    {
        balked = false;
        if (!key.IsDoor)
        {
            return map.LetsThrough(key.A, key.B)
                ? new ClimbCommand { Actor = npc.Id, A = key.A, B = key.B }
                : new WindowCommand { Actor = npc.Id, A = key.A, B = key.B, Open = true }; // if it holds, the glass goes
        }
        if (counting is not null)
        {
            if (counting.BreakIn != key)
                (counting.BreakIn, counting.BreakInTries) = (key, 0);
            if (counting.BreakInTries >= BreakInTriesAllowed)
            {
                balked = true;
                return null;
            }
            counting.BreakInTries++;
        }
        var barrier = map.BarrierOf(key);
        return barrier is { Locked: true, Stuck: false } && npc.Carried(ThievesTools) > 0
            ? new PickLockCommand { Actor = npc.Id, Door = key.A }
            : new ForceCommand { Actor = npc.Id, Door = key.A };
    }

    /// <summary>P5a: where a raider heads home on its map: its home store's squares if it is on this map, else the exits.</summary>
    private IEnumerable<GridPos> WaysOut(GridMap map, Store home) =>
        home.Position is not null && World.Locations[home.Location].Area == map.Area ? UseSquares(home, map) : map.Exits.Values;

    /// <summary>P5a: runs a break-in step; a window that will not open by hand (locked or stuck) has its glass broken instead.</summary>
    private CommandResult ExecuteBreakIn(Actor npc, Command step)
    {
        var result = Execute(step);
        if (step is WindowCommand window && result.Rejection is RejectionReason.Locked or RejectionReason.Stuck)
            return Execute(new BreakGlassCommand { Actor = npc.Id, A = window.A, B = window.B });
        return result;
    }
}
