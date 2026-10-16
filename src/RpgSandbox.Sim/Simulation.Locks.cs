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
            actor.KnownBarriers[(map.Area, key)] = new KnownBarrier(barrier.Locked, barrier.Stuck, barrier.Broken,
                barrier.Barred ? barrier.BarsClueText : barrier.ClueText, World.Now, // P4b: bars show first
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
        var (key, map, time, rejected) = CheckBarrierWork(actor, command, work);
        if (rejected is not null)
            return rejected;
        var barrier = map!.BarrierOf(key);
        var action = new BarrierAction
        {
            Id = World.AllocateActionId(), Actor = actor.Id, StartedAt = World.Now, CompletesAt = World.Now.Plus(time),
            Work = work, Target = key,
            Description = work switch
            {
                BarrierWork.PickLock => $"Scassina {Name(key)}",
                BarrierWork.BreakGlass => "Rompe il vetro",
                BarrierWork.ClearGlass => "Toglie i frammenti di vetro",
                BarrierWork.Climb => "Scavalca la finestra",
                _ => barrier is { Barred: true } ? "Forza le sbarre" : $"Forza {Name(key)}",
            },
        };
        if (actor.CurrentAction is WaitAction)
            CancelAction(actor);
        Begin(actor, action);
        if (work is BarrierWork.Force or BarrierWork.BreakGlass)
            Bangs(actor, map, key, glass: work == BarrierWork.BreakGlass); // loud from the first blow
        return CommandResult.Started(action.Id, action.CompletesAt, $"{actor.Name}: {action.Description.ToLowerInvariant()}.");
    }

    /// <summary>Whether a work on a door or window can start, on which, and how long it takes outside a fight.</summary>
    private (BarrierKey Key, GridMap? Map, Duration Time, CommandResult? Rejected) CheckBarrierWork(Actor actor, BarrierCommand command, BarrierWork work)
    {
        var (key, map, rejected) = Target(actor, command);
        if (rejected is not null)
            return (key, map, default, rejected);
        var barrier = map!.BarrierOf(key);
        if (work is BarrierWork.BreakGlass or BarrierWork.ClearGlass or BarrierWork.Climb && key.IsDoor)
            return (key, map, default, CommandResult.Rejected(RejectionReason.NotAWindow, "Si fa solo con una finestra."));
        var glass = key.IsDoor ? WindowGlass.Intact : map.GlassOf(key.A, key.B);
        var time = BarrierWorkTime;
        switch (work)
        {
            case BarrierWork.PickLock:
                if (barrier is not { Locked: true })
                    return (key, map, default, CommandResult.Rejected(RejectionReason.NotHeld, $"{Capital(Name(key))} non è chiusa a chiave."));
                if (actor.Carried(ThievesTools) == 0)
                    return (key, map, default, CommandResult.Rejected(RejectionReason.NoTools, "Ti servono degli attrezzi da scasso."));
                break;
            case BarrierWork.Force when barrier is { Barred: true }: // P4b: the bars come first
                break;
            case BarrierWork.Force:
                if (barrier is not { Holds: true })
                    return (key, map, default, CommandResult.Rejected(RejectionReason.NotHeld, $"{Capital(Name(key))} si apre normalmente."));
                break;
            case BarrierWork.BreakGlass: // P4b: fragile, it breaks without a check (SRD Breaking Objects)
                if (glass != WindowGlass.Intact)
                    return (key, map, default, CommandResult.Rejected(RejectionReason.AlreadyDone, "Il vetro è già rotto."));
                time = Duration.FromSeconds(6);
                break;
            case BarrierWork.ClearGlass:
                if (glass != WindowGlass.Broken)
                    return (key, map, default, CommandResult.Rejected(RejectionReason.NotHeld, "Non ci sono frammenti da togliere."));
                break;
            case BarrierWork.Climb:
                if (ClimbRefused(actor, map, key) is { } refused)
                    return (key, map, default, refused);
                // ADAPTATION, SRD Climbing: the 5 feet of the window count double, 10 feet of walking at its Speed.
                time = Duration.FromTicks(MoveAction.TicksFor(2, actor.Sheet.Speed));
                break;
        }

        return (key, map, time, null);
    }

    /// <summary>
    /// P4b: why one cannot climb through this window from where it stands: closed with its glass whole, barred, or someone
    /// (or something solid) on the square beyond. Null if it can.
    /// </summary>
    private CommandResult? ClimbRefused(Actor actor, GridMap map, BarrierKey key)
    {
        if (!map.LetsThrough(key.A, key.B))
            return CommandResult.Rejected(RejectionReason.Closed, "La finestra è chiusa.");
        if (map.BarrierOf(key) is { Barred: true })
            return CommandResult.Rejected(RejectionReason.Barred, "Ci sono le sbarre.");
        var beyond = Beyond(actor, key);
        if (!map.IsWalkable(beyond) || World.Actors.Values.Any(a => a.Id != actor.Id && a.MapArea == map.Area && a.Vitality.State != LifeState.Dead
                && CurrentSpot(a) is { } there && there.DistanceTo(Spot.CenterOf(beyond)) < 2 * SpotPlanner.BodyRadius))
            return CommandResult.Rejected(RejectionReason.NoRoom, "Dall'altra parte non c'è spazio.");
        return null;
    }

    /// <summary>P4b: the square on the other side of a window from the actor.</summary>
    private GridPos Beyond(Actor actor, BarrierKey key) => CurrentSpot(actor)!.Value.Cell == key.A ? key.B : key.A;

    /// <summary>The end of a minute's work: the check, against what the door or window is now.</summary>
    private void CompleteBarrierWork(Actor actor, BarrierAction action) => ResolveBarrierWork(actor, action.Work, action.Target);

    /// <summary>P4a/P4b: the outcome of a work on a door or window: at the end of its minute, or at once in a fight (P4c).</summary>
    private void ResolveBarrierWork(Actor actor, BarrierWork work, BarrierKey key)
    {
        if (MapOf(actor) is not { } map || CurrentSpot(actor) is not { } here
            || !(key.IsDoor ? DoorInReach(map, here.Cell, key.A) : WindowInReach(here, key.A, key.B)))
        {
            World.RecordFact("BarrierWorkFailed", $"{actor.Name} non è più presso {Name(key)}.", actor.Id);
            return;
        }
        var barrier = map.BarrierOf(key);
        switch (work)
        {
            case BarrierWork.BreakGlass: // P4b
                if (map.GlassOf(key.A, key.B) == WindowGlass.Intact)
                    map.SetGlass(key.A, key.B, WindowGlass.Broken);
                World.RecordFact("GlassBroken", $"{actor.Name} ha rotto il vetro della finestra.", actor.Id);
                return;
            case BarrierWork.ClearGlass:
                if (map.GlassOf(key.A, key.B) == WindowGlass.Broken)
                    map.SetGlass(key.A, key.B, WindowGlass.Cleared);
                World.RecordFact("GlassCleared", $"{actor.Name} toglie i frammenti di vetro dalla finestra.", actor.Id);
                return;
            case BarrierWork.Climb:
                ClimbThrough(actor, map, key);
                return;
            case BarrierWork.Force when barrier is { Barred: true }:
                ForceBars(actor, map, key, barrier);
                return;
        }
        if (work == BarrierWork.PickLock)
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
    /// P4b: over the sill to the square beyond, if it still can (the window may have shut, the square be taken). Shards
    /// left in the frame cut: 1d4 slashing (ADAPTATION; a Dexterity save once saving throws exist).
    /// </summary>
    private void ClimbThrough(Actor actor, GridMap map, BarrierKey key)
    {
        if (ClimbRefused(actor, map, key) is { } refused)
        {
            World.RecordFact("ClimbFailed", $"{actor.Name} non riesce a scavalcare la finestra: {refused.Message}", actor.Id);
            return;
        }
        var beyond = Beyond(actor, key);
        Place(actor, beyond);
        World.RecordFact("Climbed", $"{actor.Name} scavalca la finestra.", actor.Id);
        if (map.GlassOf(key.A, key.B) != WindowGlass.Broken)
            return;
        var cut = World.Rng.Roll(4);
        actor.Vitality.TakeDamage(cut, critical: false);
        Harmed(actor, "si taglia"); // R1
        World.RecordFact("CutByGlass", $"{actor.Name} si taglia sui vetri rotti: {cut} danni taglienti.", actor.Id);
        if (!actor.Conscious)
            BeginDowned(actor);
    }

    /// <summary>P4b: a Strength (Athletics) check against the bars' DC; they come off for good. Loud, like any forcing.</summary>
    private void ForceBars(Actor actor, GridMap map, BarrierKey key, Barrier barrier)
    {
        var force = D20.Roll(World.Rng, actor.Sheet.Bonus(Skill.Athletics));
        var torn = force.Total >= barrier.BarsDc;
        Learn(actor, map, key, barrier); // as it found them: the bars' clue
        if (torn)
            barrier.Barred = false;
        World.RecordFact(torn ? "BarsTorn" : "ForceFailed",
            $"{actor.Name} {(torn ? "stacca le sbarre della finestra" : "non riesce a staccare le sbarre")}: Forza (Atletica) {force.Describe()}.", actor.Id);
    }

    /// <summary>
    /// P4a: the blows of someone forcing a door or window, heard like a fight (C4): who sees the forcer witnesses a
    /// break-in; who only hears it knows roughly where (an authority goes to look). The player hears it as a fact.
    /// </summary>
    private void Bangs(Actor forcer, GridMap map, BarrierKey key, bool glass = false)
    {
        var square = key.A;
        // The deed's fact, for the witnesses; nobody's own event (the outcome is, when the work is over).
        var fact = glass
            ? World.RecordFact("GlassBreaking", $"{forcer.Name} rompe il vetro della finestra.")
            : World.RecordFact("Forcing", $"{forcer.Name} forza {Name(key)}.");
        foreach (var witness in World.Actors.Values.Where(a => a.Id != forcer.Id && a.MapArea == map.Area && a.Conscious)
                     .OrderBy(a => a.Id.Value, StringComparer.Ordinal).ToList())
        {
            var sees = SeesActor(witness, forcer);
            if (!sees && (CurrentPosition(witness) is not { } ear || !WithinEarshot(map, ear, square)))
                continue;
            if (witness.IsPlayer && Asleep(witness))
                CancelAction(witness, glass ? "lo svegliano vetri infranti" : "lo svegliano dei colpi"); // R1: a noise wakes it
            if (witness.IsPlayer)
            {
                if (!sees)
                    World.RecordFact(glass ? "HeardGlass" : "HeardForcing",
                        $"{(glass ? "Senti vetri infrangersi" : "Senti colpi contro una porta o una finestra")}{Towards(map, Approximate(map, square))}.", witness.Id);
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

// P4c: doors, windows and locks in a fight, on one's turn (SRD 5.2.1 "Interacting with Things": one object for free a
// turn, a second takes the Utilize action).
internal sealed partial class Simulation
{
    private CommandResult CombatBarrier(Encounter e, Actor actor, Command command)
    {
        if (!IsTurnOf(e, actor))
            return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
        var me = CombatantOf(e, actor.Id);
        switch (command)
        {
            case DoorCommand or WindowCommand or LockCommand:
            {
                // The free interaction of the turn; a second one takes the action.
                var takesAction = me.Interacted;
                if (takesAction && me.ActionUsed)
                    return CommandResult.Rejected(RejectionReason.NoActionLeft, "Hai già usato l'interazione con un oggetto e l'azione in questo turno.");
                var result = command switch
                {
                    DoorCommand door => UseDoor(door),
                    WindowCommand window => UseWindow(window),
                    _ => UseLock((LockCommand)command),
                };
                if (!result.Success)
                    return result;
                if (takesAction)
                    me.ActionUsed = true;
                else
                    me.Interacted = true;
                return takesAction ? result with { Message = $"{result.Message} (usi l'azione)" } : result;
            }
            case ClimbCommand climb:
                return CombatClimb(e, actor, me, climb);
            case ClearGlassCommand:
                return CommandResult.Rejected(RejectionReason.InCombat, "Togliere i frammenti richiede un minuto: non durante uno scontro.");
            case BarrierCommand work:
            {
                if (me.ActionUsed)
                    return CommandResult.Rejected(RejectionReason.NoActionLeft, "Hai già usato l'azione in questo turno.");
                var kind = work switch
                {
                    PickLockCommand => BarrierWork.PickLock,
                    BreakGlassCommand => BarrierWork.BreakGlass,
                    _ => BarrierWork.Force,
                };
                var (key, map, _, rejected) = CheckBarrierWork(actor, work, kind);
                if (rejected is not null)
                    return rejected;
                // The Utilize action: one check at once, no minute's work (SRD Thieves' Tools: Utilize).
                me.ActionUsed = true;
                if (kind is BarrierWork.Force or BarrierWork.BreakGlass)
                    Bangs(actor, map!, key, glass: kind == BarrierWork.BreakGlass);
                ResolveBarrierWork(actor, kind, key);
                return new CommandResult { Message = World.RecentFacts.Last!.Value.Description };
            }
            default:
                return CommandResult.Rejected(RejectionReason.InCombat, $"{actor.Name} sta combattendo.");
        }
    }

    /// <summary>
    /// P4c: over a window in a fight: walked straight to the centre of the square beyond, the window's 5 feet counting
    /// double (SRD Climbing: 60 inches more). A real walk: opportunity attacks on leaving reach, a stop before someone
    /// unseen. Shards cut once over.
    /// </summary>
    private CommandResult CombatClimb(Encounter e, Actor actor, Combatant me, ClimbCommand command)
    {
        var (key, map, rejected) = Target(actor, command);
        if (rejected is not null)
            return rejected;
        if (key.IsDoor)
            return CommandResult.Rejected(RejectionReason.NotAWindow, "Si fa solo con una finestra.");
        if (ClimbRefused(actor, map!, key) is { } refused)
            return refused;
        var from = CurrentSpot(actor)!.Value;
        var to = Spot.CenterOf(Beyond(actor, key));
        var way = new SpotPath(new[] { from, to }, from.DistanceTo(to));
        var cost = (int)Math.Ceiling(way.Length - 1e-9) + 5 * Spot.PerFoot;
        var left = MovementLeftInches(e, actor);
        if (cost > left)
            return CommandResult.Rejected(RejectionReason.TooFar,
                $"Troppo lontano: servono {FeetText(cost)} piedi, te ne restano {FeetLeftText(left)}.");
        var (_, stopped) = WalkFreeInCombat(e, actor, way);
        if (stopped is not null)
            return new CommandResult { Message = $"{actor.Name} non scavalca: {stopped}." };
        me.MovedInches += 5 * Spot.PerFoot;
        World.RecordFact("Climbed", $"{actor.Name} scavalca la finestra.", actor.Id);
        if (map!.GlassOf(key.A, key.B) == WindowGlass.Broken)
        {
            var cut = World.Rng.Roll(4);
            var outcome = actor.Vitality.TakeDamage(cut, critical: false);
            Harmed(actor, "si taglia"); // R1
            World.RecordFact("CutByGlass", $"{actor.Name} si taglia sui vetri rotti: {cut} danni taglienti.", actor.Id);
            if (!actor.Conscious)
                Fell(e, actor, null, outcome);
            CheckEnd(e);
        }
        return new CommandResult { Message = $"{actor.Name} scavalca la finestra ({FeetText(cost)} piedi)." };
    }
}
