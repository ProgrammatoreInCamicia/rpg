using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// P1b (free movement prototype): moving to an exact point during one's turn in a fight.
internal sealed partial class Simulation
{
    /// <summary>
    /// P1b: a move to an exact point on one's turn, along a short way for the body (<see cref="SpotPlanner"/>) around the
    /// creatures one perceives (an ally can be crossed, nobody stopped on) and closed doors. It costs its length, rounded
    /// up to the inch, out of the turn's movement. It is walked inch by inch against the creatures really there: one the
    /// mover did not perceive stops it before reaching it; leaving an enemy's reach provokes its Opportunity Attack at
    /// the last point still in reach; a mover that drops stays there.
    /// </summary>
    private CommandResult CombatMoveToSpot(Encounter e, Actor actor, MoveToSpotCommand command)
    {
        if (command.Stealthy)
            return CommandResult.Rejected(RejectionReason.InCombat, "Durante uno scontro non puoi muoverti di soppiatto.");
        if (!IsTurnOf(e, actor))
            return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
        var (reason, message, way) = PlanFreeCombatMove(e, actor, command.To);
        if (reason != RejectionReason.None)
            return CommandResult.Rejected(reason, message);
        var (inches, stopped) = WalkFreeInCombat(e, actor, way!);
        CheckEnd(e);
        var feet = FeetText(inches);
        return new CommandResult
        {
            Message = stopped is not null ? $"{actor.Name} si ferma dopo {feet} piedi: {stopped}." : $"{actor.Name} si sposta di {feet} piedi.",
        };
    }

    private static string FeetText(double inches) => (Math.Ceiling(inches / Spot.PerFoot * 10 - 1e-9) / 10).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("it-IT"));

    /// <summary>P1b: the way to <paramref name="to"/> this turn, or why there is none. Nothing is touched.</summary>
    private (RejectionReason Reason, string Message, SpotPath? Way) PlanFreeCombatMove(Encounter e, Actor actor, Spot to)
    {
        var left = MovementLeftInches(e, actor);
        if (left <= 0)
            return (RejectionReason.NoMovementLeft, "Non ti resta movimento in questo turno.", null);
        var map = World.Maps[e.Area];
        var from = CurrentSpot(actor)!.Value;
        if (from == to)
            return (RejectionReason.AlreadyThere, $"{actor.Name} è già lì.", null);
        var knows = Perceived(e, actor);
        var others = World.Actors.Values
            .Where(o => o.Id != actor.Id && o.MapArea == map.Area && o.Vitality.State != LifeState.Dead && CurrentSpot(o) is not null && knows(o))
            .ToList();
        // SRD Moving Around Other Creatures: an ally can be crossed; any other conscious creature cannot. ADAPTATION: an
        // incapacitated creature is crossed without the Difficult Terrain it would be on squares.
        var planner = new SpotPlanner(map,
            others.Where(o => o.Conscious && !Allies(e, actor, o)).Select(o => CurrentSpot(o)!.Value),
            others.Select(o => CurrentSpot(o)!.Value), closedDoorsBlock: true);
        if (!planner.Fits(to))
            return (RejectionReason.NoRoom, "Lì non c'è spazio per stare in piedi.", null);
        if (planner.Find(from, to) is not { } way)
            return (RejectionReason.Unreachable, "Non si può raggiungere quel punto.", null);
        if (Math.Ceiling(way.Length - 1e-9) > left)
            return (RejectionReason.TooFar, $"Troppo lontano: servono {FeetText(way.Length)} piedi, te ne restano {FeetText(left)}.", way);
        return (RejectionReason.None, $"{FeetText(way.Length)} piedi di movimento.", way);
    }

    /// <summary>P1b: the points of a way an inch apart, from its start (index 0) to its goal (exactly, last).</summary>
    private static IReadOnlyList<Spot> InchByInch(SpotPath way)
    {
        var points = new List<Spot> { way.Vertices[0] };
        var steps = (int)Math.Ceiling(way.Length - 1e-9);
        for (var k = 1; k < steps; k++)
            points.Add(PointAlong(way, k));
        if (steps > 0)
            points.Add(way.Vertices[^1]);
        return points;
    }

    private static Spot PointAlong(SpotPath way, double along)
    {
        for (var i = 1; i < way.Vertices.Count; i++)
        {
            Spot a = way.Vertices[i - 1], b = way.Vertices[i];
            var length = a.DistanceTo(b);
            if (along <= length)
            {
                var t = length == 0 ? 0 : along / length;
                return new Spot((int)Math.Round(a.X + (b.X - a.X) * t, MidpointRounding.AwayFromZero),
                    (int)Math.Round(a.Y + (b.Y - a.Y) * t, MidpointRounding.AwayFromZero));
            }
            along -= length;
        }
        return way.Vertices[^1];
    }

    /// <summary>
    /// P1b: walks a free way inch by inch (see <see cref="CombatMoveToSpot"/>). Returns the inches spent and why it
    /// stopped short (null if it did not). ADAPTATION, as on squares: a mover that would end on someone it did not see
    /// steps back along its way, and the inches walked forward stay spent.
    /// </summary>
    private (int Inches, string? Stopped) WalkFreeInCombat(Encounter e, Actor actor, SpotPath way)
    {
        var me = CombatantOf(e, actor.Id);
        var map = World.Maps[e.Area];
        actor.Sneak = null;
        var points = InchByInch(way);
        var others = World.Actors.Values
            .Where(o => o.Id != actor.Id && o.MapArea == map.Area && o.Vitality.State != LifeState.Dead && CurrentSpot(o) is not null)
            .ToList();
        var unseen = others.Where(o => o.Conscious && !Allies(e, actor, o) && !SeesActor(actor, o)).Select(o => CurrentSpot(o)!.Value).ToList();
        var standing = StandingOf(actor);
        var reached = 0;
        string? stopped = null;
        for (var k = 1; k < points.Count; k++)
        {
            var p = points[k];
            if (unseen.Any(o => o.DistanceTo(p) < 2 * SpotPlanner.BodyRadius))
            {
                stopped = "qualcuno gli sbarra la strada";
                break;
            }
            var next = (p.Cell, p);
            var threats = Threats(e, actor, standing, next).ToList();
            if (threats.Count > 0)
                Place(actor, standing.Cell, standing.At); // the blow comes at the last point still in reach
            foreach (var foe in threats)
            {
                CombatantOf(e, foe.Id).ReactionUsed = true;
                ResolveAttack(e, foe, actor, MeleeAttack(foe)!, nonLethal: false, CombatLogKind.OpportunityAttack);
                if (!actor.Conscious)
                    break;
            }
            if (!actor.Conscious)
            {
                stopped = "è caduto";
                break;
            }
            standing = next;
            reached = k;
        }
        var forward = reached == points.Count - 1 ? (int)Math.Ceiling(way.Length - 1e-9) : reached;
        var back = reached;
        while (back > 0 && others.Any(o => CurrentSpot(o)!.Value.DistanceTo(points[back]) < 2 * SpotPlanner.BodyRadius))
            back--;
        if (back < reached && stopped is null)
            stopped = "c'è qualcuno, torna un poco indietro";
        Place(actor, points[back].Cell, points[back]);
        me.MovedInches += forward;
        EvaluateContacts();
        var trail = points.Take(back + 1).Select(p => p.Cell).Aggregate(new List<GridPos>(), (cells, c) =>
        {
            if (cells.Count == 0 || cells[^1] != c)
                cells.Add(c);
            return cells;
        }).Skip(1).ToList();
        if (forward > 0 || stopped is not null)
            Record(e, CombatLogKind.Move, actor, null,
                (a, _) => $"{a} si sposta di {FeetText(forward)} piedi{(stopped is null ? "" : $" e si ferma: {stopped}")}.", path: trail);
        return (forward, stopped);
    }

    /// <summary>P1b: the preview of a free move in a fight, by the same planning, with the attacks it would provoke.</summary>
    private SpotMovePreview PreviewFreeCombatMove(Encounter e, Actor actor, Spot to)
    {
        if (!IsTurnOf(e, actor))
            return new SpotMovePreview { Valid = false, Reason = RejectionReason.NotYourTurn, Message = "Non è il tuo turno." };
        var left = MovementLeftInches(e, actor) / (double)Spot.PerFoot;
        var (reason, message, way) = PlanFreeCombatMove(e, actor, to);
        if (way is null)
            return new SpotMovePreview { Valid = false, Reason = reason, Message = message, FeetLeft = Math.Max(0, left) };

        // Opportunity attacks along the way, by the threats the player knows of (each has one Reaction).
        var player = World.Actors[World.Player];
        var used = e.Order.Where(c => c.ReactionUsed).Select(c => c.Actor).ToHashSet();
        var provokes = new List<SpotProvocationView>();
        var points = InchByInch(way);
        var standing = StandingOf(actor);
        for (var k = 1; k < points.Count; k++)
        {
            var next = (points[k].Cell, points[k]);
            foreach (var foe in Threats(e, actor, standing, next).Where(f => !used.Contains(f.Id) && SeesActor(player, f)))
            {
                used.Add(foe.Id);
                provokes.Add(new SpotProvocationView { By = foe.Id, Name = foe.Name, At = standing.At, AfterFeet = (k - 1) / (double)Spot.PerFoot });
            }
            standing = next;
        }
        return new SpotMovePreview
        {
            Valid = reason == RejectionReason.None, Reason = reason, Message = message, Way = way.Vertices,
            Feet = Math.Ceiling(way.Length / Spot.PerFoot * 10 - 1e-9) / 10, FeetLeft = left, Provokes = provokes,
        };
    }
}
