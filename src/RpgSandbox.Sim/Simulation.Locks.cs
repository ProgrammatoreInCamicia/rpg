using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// P4a: doors and windows that are locked or stuck: keys, picking a lock, forcing (SRD 5.2.1 Thieves' Tools, Lock,
// ability checks).
internal sealed partial class Simulation
{
    /// <summary>SRD Thieves' Tools: "Pick a lock (DC 15)".</summary>
    private const int PickLockDc = 15;

    private const string ThievesTools = "thieves-tools";

    /// <summary>ADAPTATION: a try at picking or forcing takes a minute.</summary>
    private static readonly Duration BarrierWorkTime = Duration.FromMinutes(1);

    /// <summary>The door square an actor may open or close from: next to it (or in a door on a boundary), not past a corner.</summary>
    private bool DoorInReach(GridMap map, GridPos here, GridPos door) =>
        (map.IsEdgeDoor(door) || here != door) && here.IsAdjacentOrSame(door) && (here == door || OpenDiagonal(map, here, door));

    /// <summary>P2b: a window an actor may open or close: from one of its squares, the body within 5 feet of it.</summary>
    private static bool WindowInReach(Spot here, GridPos a, GridPos b)
    {
        var (p, q) = SpotPlanner.Border(a, b);
        return (here.Cell == a || here.Cell == b) && SpotPlanner.PointSegment(here, p, q) - SpotPlanner.BodyRadius <= 5 * Spot.PerFoot;
    }

    /// <summary>The door or window a command names, if the actor can reach it; or why not.</summary>
    private (BarrierKey Key, GridMap? Map, CommandResult? Rejected) Target(Actor actor, BarrierCommand command)
    {
        // As a walk does, this interrupts an NPC idling in its routine; anything else keeps it busy.
        if (actor.CurrentAction is not null and not WaitAction { Interruptible: true })
            return (default, null, CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato."));
        if (MapOf(actor) is not { } map || CurrentSpot(actor) is not { } here)
            return (default, null, CommandResult.Rejected(RejectionReason.NoMap, "Qui non ci sono porte né finestre."));
        switch (command)
        {
            case { Door: { } door, A: null, B: null }:
                if (!map.IsDoor(door))
                    return (default, map, CommandResult.Rejected(RejectionReason.NotADoor, "Lì non c'è una porta."));
                if (!DoorInReach(map, here.Cell, door))
                    return (default, map, CommandResult.Rejected(RejectionReason.OutOfReach, "La porta è troppo lontana: avvicinati."));
                return (BarrierKey.Door(door), map, null);
            case { Door: null, A: { } a, B: { } b }:
                if (!map.IsWindow(a, b))
                    return (default, map, CommandResult.Rejected(RejectionReason.NotAWindow, "Lì non c'è una finestra."));
                if (!WindowInReach(here, a, b))
                    return (default, map, CommandResult.Rejected(RejectionReason.OutOfReach, "La finestra è troppo lontana: avvicinati."));
                return (BarrierKey.Window(a, b), map, null);
            default:
                return (default, map, CommandResult.Rejected(RejectionReason.UnknownCommand, "Indica una porta o una finestra."));
        }
    }

    private static bool IsClosed(GridMap map, BarrierKey key) => key.IsDoor ? map.IsClosedDoor(key.A) : !map.IsOpenWindow(key.A, key.B);

    private static string Name(BarrierKey key) => key.IsDoor ? "la porta" : "la finestra";

    /// <summary>
    /// P4a: before a door or window is opened by hand: a locked one opens with its key carried (unlocked, a fact); one
    /// still held refuses, and the player learns how (with its clue). Null if it may open.
    /// </summary>
    private CommandResult? OpensByHand(Actor actor, GridMap map, BarrierKey key)
    {
        if (map.BarrierOf(key) is not { Holds: true } barrier)
            return null;
        if (barrier is { Locked: true, Stuck: false, Key: { } item } && actor.Carried(item) > 0)
        {
            barrier.Locked = false;
            Learn(actor, map, key, barrier);
            World.RecordFact(key.IsDoor ? "DoorUnlocked" : "WindowUnlocked", $"{actor.Name} apre con la chiave {Name(key)}.", actor.Id);
            return null;
        }
        Learn(actor, map, key, barrier);
        World.RecordFact(barrier.Locked ? "FoundLocked" : "FoundStuck",
            $"{actor.Name} trova {Name(key)} {(barrier.Locked ? "chiusa a chiave" : "bloccata")}.", actor.Id);
        var what = key.IsDoor ? "La porta" : "La finestra";
        return barrier.Locked
            ? CommandResult.Rejected(RejectionReason.Locked, $"{what} è chiusa a chiave: {barrier.ClueText}.")
            : CommandResult.Rejected(RejectionReason.Stuck, $"{what} è bloccata: {barrier.ClueText}.");
    }

    /// <summary>The player remembers how a door or window holds, once it has found out (never a number).</summary>
    private void Learn(Actor actor, GridMap map, BarrierKey key, Barrier barrier)
    {
        if (actor.IsPlayer)
            actor.KnownBarriers[(map.Area, key)] = new KnownBarrier(barrier.Locked, barrier.Stuck, barrier.Broken, barrier.ClueText, World.Now,
                HasLock: barrier.Key is not null); // a lock shows, once one tries it (review P4a)
    }

    private CommandResult UseLock(LockCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        var (key, map, rejected) = Target(actor, command);
        if (rejected is not null)
            return rejected;
        if (map!.BarrierOf(key) is not { Key: { } item } barrier)
            return CommandResult.Rejected(RejectionReason.NoLock, $"{Capital(Name(key))} non ha serratura.");
        if (barrier.Broken)
        {
            Learn(actor, map, key, barrier);
            return CommandResult.Rejected(RejectionReason.Broken, $"{Capital(Name(key))} è stata forzata: la serratura è rotta.");
        }
        if (actor.Carried(item) == 0)
            return CommandResult.Rejected(RejectionReason.NoKey, "Non hai la chiave.");
        if (!IsClosed(map, key))
            return CommandResult.Rejected(RejectionReason.NotClosed, $"Prima chiudi {Name(key)}.");
        if (barrier.Locked == command.Locked)
            return CommandResult.Rejected(RejectionReason.AlreadyDone, command.Locked ? "È già chiusa a chiave." : "Non è chiusa a chiave.");
        barrier.Locked = command.Locked;
        Learn(actor, map, key, barrier);
        World.RecordFact(command.Locked ? (key.IsDoor ? "DoorLocked" : "WindowLocked") : (key.IsDoor ? "DoorUnlocked" : "WindowUnlocked"),
            $"{actor.Name} {(command.Locked ? "chiude a chiave" : "apre con la chiave")} {Name(key)}.", actor.Id);
        return new CommandResult { Message = command.Locked ? $"Chiudi a chiave {Name(key)}." : $"Apri la serratura con la chiave." };
    }

    private static string Capital(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    private CommandResult StartBarrierWork(BarrierCommand command, BarrierWork work)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        var (key, map, rejected) = Target(actor, command);
        if (rejected is not null)
            return rejected;
        var barrier = map!.BarrierOf(key);
        if (work == BarrierWork.PickLock)
        {
            if (barrier is not { Locked: true })
                return CommandResult.Rejected(RejectionReason.NotHeld, $"{Capital(Name(key))} non è chiusa a chiave.");
            if (actor.Carried(ThievesTools) == 0)
                return CommandResult.Rejected(RejectionReason.NoTools, "Ti servono degli attrezzi da scasso.");
        }
        else if (barrier is not { Holds: true })
            return CommandResult.Rejected(RejectionReason.NotHeld, $"{Capital(Name(key))} si apre normalmente.");

        var action = new BarrierAction
        {
            Id = World.AllocateActionId(), Actor = actor.Id, StartedAt = World.Now, CompletesAt = World.Now.Plus(BarrierWorkTime),
            Work = work, Target = key,
            Description = work == BarrierWork.PickLock ? $"Scassina {Name(key)}" : $"Forza {Name(key)}",
        };
        if (actor.CurrentAction is WaitAction)
            CancelAction(actor);
        Begin(actor, action);
        if (work == BarrierWork.Force)
            Bangs(actor, map, key); // loud from the first blow
        return CommandResult.Started(action.Id, action.CompletesAt,
            work == BarrierWork.PickLock ? $"{actor.Name} si mette a scassinare {Name(key)}." : $"{actor.Name} si mette a forzare {Name(key)}.");
    }

    /// <summary>The end of a minute's work: the check, against what the door or window is now.</summary>
    private void CompleteBarrierWork(Actor actor, BarrierAction work)
    {
        var key = work.Target;
        if (MapOf(actor) is not { } map || CurrentSpot(actor) is not { } here
            || !(key.IsDoor ? DoorInReach(map, here.Cell, key.A) : WindowInReach(here, key.A, key.B)))
        {
            World.RecordFact("BarrierWorkFailed", $"{actor.Name} non è più presso {Name(key)}.", actor.Id);
            return;
        }
        var barrier = map.BarrierOf(key);
        if (work.Work == BarrierWork.PickLock)
        {
            if (barrier is not { Locked: true } || actor.Carried(ThievesTools) == 0)
            {
                World.RecordFact("LockPickFailed", $"{actor.Name} smette di scassinare {Name(key)}.", actor.Id);
                return;
            }
            // SRD tools: proficiency with the tool adds the Proficiency Bonus; with the skill too, Advantage.
            var sheet = actor.Sheet;
            var proficient = sheet.IsProficientWith(ThievesTools);
            var roll = D20.Roll(World.Rng, Abilities.Modifier(sheet.Dexterity) + (proficient ? sheet.ProficiencyBonus : 0),
                advantage: proficient && sheet.IsProficient(Skill.SleightOfHand));
            var picked = roll.Total >= PickLockDc;
            if (picked)
                barrier.Locked = false;
            Learn(actor, map, key, barrier);
            World.RecordFact(picked ? "LockPicked" : "LockPickFailed",
                $"{actor.Name} {(picked ? "scassina" : "non riesce a scassinare")} {Name(key)}: Destrezza {roll.Describe()}.", actor.Id);
            return;
        }

        if (barrier is not { Holds: true })
        {
            World.RecordFact("ForceFailed", $"{actor.Name} smette di forzare {Name(key)}: ormai si apre.", actor.Id);
            return;
        }
        var force = D20.Roll(World.Rng, actor.Sheet.Bonus(Skill.Athletics));
        var forced = force.Total >= barrier.ForceDc;
        if (forced)
        {
            // A locked one gives way and stays broken; a stuck one comes free and opens as any other from now on.
            barrier.Broken |= barrier.Locked;
            barrier.Locked = barrier.Stuck = false;
            if (key.IsDoor)
            {
                NoteSensesChange(map);
                map.SetDoor(key.A, open: true);
                Touched(actor, map, key.A);
                NoteSensesChange(map);
            }
            else
                map.SetWindow(key.A, key.B, open: true);
        }
        Learn(actor, map, key, barrier);
        World.RecordFact(forced ? "Forced" : "ForceFailed",
            $"{actor.Name} {(forced ? "forza" : "non riesce a forzare")} {Name(key)}: Forza (Atletica) {force.Describe()}.", actor.Id);
    }

    /// <summary>
    /// P4a: the blows of someone forcing a door or window, heard like a fight (C4): who sees the forcer witnesses a
    /// break-in; who only hears it knows roughly where (an authority goes to look). The player hears it as a fact.
    /// </summary>
    private void Bangs(Actor forcer, GridMap map, BarrierKey key)
    {
        var square = key.A;
        var fact = World.RecordFact("Forcing", $"{forcer.Name} forza {Name(key)}.", forcer.Id);
        foreach (var witness in World.Actors.Values.Where(a => a.Id != forcer.Id && a.MapArea == map.Area && a.Conscious)
                     .OrderBy(a => a.Id.Value, StringComparer.Ordinal).ToList())
        {
            var sees = SeesActor(witness, forcer);
            if (!sees && (CurrentPosition(witness) is not { } ear || !WithinEarshot(map, ear, square)))
                continue;
            if (witness.IsPlayer)
            {
                if (!sees)
                    World.RecordFact("HeardForcing", $"Senti colpi contro una porta o una finestra{Towards(map, Approximate(map, square))}.", witness.Id);
                continue;
            }
            var id = new ObservationId(World.NextObservationId++);
            witness.Knowledge.Add(new Observation
            {
                Id = id, Origin = id, Kind = DeedKind.BreakIn, ObservedAt = World.Now, LearnedAt = World.Now, Fact = fact,
                Perceived = sees ? PerceptionMode.Seen : PerceptionMode.Heard,
                Square = sees ? square : Approximate(map, square), Location = map.ZoneAt(square),
                Culprit = sees ? forcer.Id : null, CulpritName = sees ? forcer.Name : null,
            });
            Alarm(witness, firstNews: true); // it stops to decide: an authority goes to look
        }
    }

    private string Towards(GridMap map, GridPos square) => map.ZoneAt(square) is { } zone ? $", verso {World.Locations[zone].Name}" : "";

    /// <summary>
    /// P4a: a walker that unlocked a door with its key locks it again once past it (the door is left as it was found),
    /// unless someone stands in it. A walk given up before then leaves it unlocked.
    /// </summary>
    private void RelockBehind(Actor actor, GridMap map, MoveAction move, int reached)
    {
        if (actor.PendingRelock is not { } relock)
            return;
        if (relock.Walk != move.Id)
        {
            actor.PendingRelock = null; // from a walk given up
            return;
        }
        if (reached < relock.AfterStep)
            return;
        actor.PendingRelock = null;
        var key = BarrierKey.Door(relock.Door);
        if (map.BarrierOf(key) is not { Key: { } item } barrier || barrier.Broken || actor.Carried(item) == 0
            || !map.IsEdgeDoor(relock.Door) && World.Actors.Values.Any(a => a.MapArea == map.Area && CurrentPosition(a) == relock.Door))
            return;
        if (!map.IsClosedDoor(relock.Door))
        {
            NoteSensesChange(map);
            map.SetDoor(relock.Door, open: false);
            NoteSensesChange(map);
        }
        barrier.Locked = true;
        World.RecordFact("DoorLocked", $"{actor.Name} richiude a chiave la porta in {relock.Door}.", actor.Id);
    }

    /// <summary>P4a: the doors a walker cannot open on its way: closed, and locked without its key or stuck.</summary>
    private Func<GridPos, bool> Opens(Actor actor, GridMap map) => door =>
        map.BarrierOf(BarrierKey.Door(door)) is not { Holds: true } barrier
        || barrier is { Stuck: false, Locked: true, Key: { } item } && actor.Carried(item) > 0;

    private IEnumerable<GridPos> BarredDoors(Actor actor, GridMap map)
    {
        var opens = Opens(actor, map);
        return map.Doors.Keys.Where(d => map.DoorHolds(d) && !opens(d)).ToList();
    }
}
