using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// C2: the fight in the world (SRD 5.2.1 Combat; the ADAPTATIONS are in the contract). A round's turns all happen at the
// instant it begins; the world catches up 6 seconds later, when the next round begins. While it is the player's turn the
// world waits.
internal sealed partial class Simulation
{
    private const int FeetPerSquare = 5;

    internal static CombatProfile ProfileOf(Actor actor) => actor.Profile; // D1a: derived from the build and equipment

    internal Encounter? EncounterOf(Actor actor) =>
        World.Encounters.Values.FirstOrDefault(e => e.Order.Any(c => c.Actor == actor.Id));

    private static Combatant CombatantOf(Encounter e, ActorId id) => e.Order.First(c => c.Actor == id);

    private static bool IsTurnOf(Encounter e, Actor actor) => !e.RoundClosed && e.Order[e.Turn].Actor == actor.Id;

    /// <summary>The world is waiting for the player to play its turn: time cannot pass.</summary>
    internal bool PlayerTurnPending =>
        World.Encounters.Values.Any(e => !e.RoundClosed && e.Order[e.Turn].Actor == World.Player);

    private static bool Allies(Encounter e, Actor a, Actor b) =>
        e.Order.FirstOrDefault(c => c.Actor == a.Id) is { } ca && e.Order.FirstOrDefault(c => c.Actor == b.Id) is { } cb
        && ca.Side == cb.Side;

    private IEnumerable<Actor> EnemiesOf(Encounter e, Actor actor)
    {
        var side = CombatantOf(e, actor.Id).Side;
        return e.Order.Where(c => c.Side != side).Select(c => World.Actors[c.Actor]);
    }

    /// <summary>
    /// Whether <paramref name="who"/> holds <paramref name="other"/> hostile (C4b: a relation, no longer "raider or
    /// not"): they fought before; it has an open claim on it for a theft; its faction holds the other hostile (hostile to
    /// strangers, at war with the other's faction, or wanting the other); or, the other way round, the other's faction
    /// holds it hostile (one expects trouble from those who are after it).
    /// </summary>
    internal bool HoldsHostile(Actor who, Actor other) =>
        who.Id != other.Id
        && (who.FoughtWith.Contains(other.Id)
            || who.Claims.Any(c => c.Thief == other.Id && c.Owed > 0)
            || FactionHolds(who.Faction, other)
            || FactionHolds(other.Faction, who));

    /// <summary>A faction holds someone hostile: it wants them, or they are a stranger to a hostile faction or of an enemy one.</summary>
    private bool FactionHolds(FactionId? faction, Actor someone)
    {
        if (faction is not { } id)
            return false;
        var f = World.Factions[id];
        return f.Wanted.ContainsKey(someone.Id)
               || (someone.Faction != id && (f.HostileToStrangers || (someone.Faction is { } other && f.Enemies.Contains(other))));
    }

    /// <summary>
    /// C4b: an authority goes for a wanted person's capture, not its death: its melee blows knock out, unless the
    /// person is also an enemy of the faction anyway (a raider).
    /// </summary>
    private bool Spares(Actor npc, Actor target)
    {
        if (npc.Faction is not { } id || World.Factions[id].Authority != npc.Id || !World.Factions[id].Wanted.ContainsKey(target.Id))
            return false;
        var f = World.Factions[id];
        return target.Faction is not { } other || (!f.Enemies.Contains(other) && !World.Factions[other].HostileToStrangers);
    }

    // ---------------------------------------------------------------- commands

    private CommandResult Attack(AttackCommand command)
    {
        var (rejected, attack, _) = GateAttack(command.Actor, command.Target, command.Attack, command.NonLethal);
        if (rejected is not null)
            return rejected;
        var actor = World.Actors[command.Actor];
        var target = World.Actors[command.Target];
        if (SmiteRefused(EncounterOf(actor), actor, attack!, command.SmiteSlot, command.SmiteFree) is { } noSmite)
            return CommandResult.Rejected(RejectionReason.NoSlot, noSmite); // R3b
        if (command.SacredWeapon && SacredWeaponRefused(EncounterOf(actor), actor, attack!) is { } noSacred)
            return CommandResult.Rejected(RejectionReason.NoSlot, noSacred); // R3c
        if (command.Maneuver is { } maneuver)
        {
            if (ManeuverRefused(EncounterOf(actor), actor, target, attack!, maneuver) is { } noManeuver)
                return CommandResult.Rejected(RejectionReason.UnknownAttack, noManeuver); // R4a
            var fight = EncounterOf(actor)!;
            TakeAttack(fight, actor);
            var done = ResolveManeuver(fight, actor, target, maneuver, command.ShoveProne);
            CheckEnd(fight);
            return new CommandResult { Message = done };
        }
        if (EncounterOf(actor) is not { } e)
            return OpenFight(actor, target, attack!, command.NonLethal);
        if (attack!.OffHand != command.OffHand || command.OffHand && OffHandRefused(e, actor, attack) is not null) // R4b
            return CommandResult.Rejected(RejectionReason.NoActionLeft, command.OffHand
                ? OffHandRefused(e, actor, attack) ?? "Non è l'arma della mano secondaria." : "Quell'arma attacca solo come attacco extra (mano secondaria).");
        if (command.OffHand)
            TakeOffHandAttack(e, actor, attack);
        else
            TakeAttack(e, actor, attack); // R3a: the Attack action, or one of its Extra Attacks
        if (command.SacredWeapon)
            TakeUpSacredWeapon(e, actor, attack!); // R3c: part of the Attack action
        var text = ResolveAttack(e, actor, target, attack!, command.NonLethal, CombatLogKind.Attack,
            smite: command.SmiteFree ? 1 : command.SmiteSlot, smiteFree: command.SmiteFree);
        CheckEnd(e);
        return new CommandResult { Message = text };
    }

    /// <summary>
    /// Whether <paramref name="attackName"/> can be made now against <paramref name="targetId"/>, and how: the checks of
    /// <see cref="AttackCommand"/>, touching nothing (P2b: also <see cref="SimulationSession.PreviewAttack"/>).
    /// </summary>
    private (CommandResult? Rejected, AttackProfile? Attack, AttackCheck? Check) GateAttack(ActorId actorId, ActorId targetId,
        string attackName, bool nonLethal)
    {
        if (!World.Actors.TryGetValue(actorId, out var actor))
            return (CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {actorId}."), null, null);
        if (!World.Actors.TryGetValue(targetId, out var target))
            return (CommandResult.Rejected(RejectionReason.ActorNotFound, $"Bersaglio sconosciuto: {targetId}."), null, null);
        if (ProfileOf(actor).Attacks.FirstOrDefault(a => a.Name == attackName) is not { } attack)
            return (CommandResult.Rejected(RejectionReason.UnknownAttack, $"{actor.Name} non ha l'attacco «{attackName}»."), null, null);
        var e = EncounterOf(actor);
        if (e is null && EncounterOf(target) is not null)
            return (CommandResult.Rejected(RejectionReason.InCombat, $"{target.Name} sta già combattendo."), null, null);
        if (e is not null)
        {
            if (!IsTurnOf(e, actor))
                return (CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno."), null, null);
            if (!attack.OffHand && CombatantOf(e, actor.Id) is { ActionUsed: true, AttacksLeft: 0 }) // R4b: the extra attack comes after
                return (CommandResult.Rejected(RejectionReason.NoActionLeft, "Hai già usato l'azione in questo turno."), null, null);
            if (EncounterOf(target) != e)
                return (CommandResult.Rejected(RejectionReason.InCombat, $"{target.Name} non fa parte di questo scontro."), null, null);
        }
        var check = CheckAttack(e, actor, target, attack, nonLethal);
        return check.Valid ? (null, attack, check) : (CommandResult.Rejected(check.Reason, check.Why!), null, null);
    }

    /// <summary>P2b: what an <see cref="AttackCommand"/> would be now, by the same checks; no dice, no state.</summary>
    internal AttackPreview PreviewAttack(ActorId actorId, ActorId targetId, string attackName, bool nonLethal)
    {
        var player = World.Actors[World.Player];
        bool Shown(Actor a) => a.IsPlayer || SeesActor(player, a, ignoreFacing: EncounterOf(player) is null);
        var (rejected, _, check) = GateAttack(actorId, targetId, attackName, nonLethal);
        var actor = World.Actors.GetValueOrDefault(actorId);
        var target = World.Actors.GetValueOrDefault(targetId);
        return new AttackPreview
        {
            Valid = rejected is null, Reason = rejected?.Rejection ?? RejectionReason.None, Message = rejected?.Message ?? "",
            Advantage = check?.Advantage ?? false, Disadvantage = check?.Disadvantage ?? false,
            LongRange = check?.LongRange ?? false, AutoCritical = check?.AutoCritical ?? false, Cover = check?.Cover ?? CoverDegree.None, Reasons = check?.Reasons ?? Array.Empty<string>(),
            // Review P2b: never where the player does not see someone (the preview would give away a hidden position).
            From = actor is not null && Shown(actor) ? check?.Origin ?? CurrentSpot(actor) : null,
            To = target is not null && Shown(target) ? check?.Aim ?? CurrentSpot(target) : null, // P3: the point aimed at
        };
    }

    /// <summary>
    /// The attack that starts a fight. Against a target that does not expect it (it does not see the attacker, or does
    /// not hold it hostile) it lands at once and the target is surprised; otherwise initiative comes first and the
    /// attack waits for the attacker's turn (ADAPTATION, see the contract).
    /// </summary>
    private CommandResult OpenFight(Actor actor, Actor target, AttackProfile attack, bool nonLethal)
    {
        if (EncounterOf(target) is not null)
            return CommandResult.Rejected(RejectionReason.InCombat, $"{target.Name} sta già combattendo.");
        var check = CheckAttack(null, actor, target, attack, nonLethal);
        if (!check.Valid)
            return CommandResult.Rejected(check.Reason, check.Why!);

        var expects = SeesActor(target, actor, ignoreFacing: true) && HoldsHostile(target, actor);
        CancelAction(actor, "inizia uno scontro"); // R1: a rest stops when initiative is rolled
        CancelAction(target, "inizia uno scontro");
        actor.LastHarmAt = target.LastHarmAt = World.Now;
        actor.FoughtWith.Add(target.Id);
        target.FoughtWith.Add(actor.Id);

        // SRD Initiative: a Dexterity check each; the surprised one rolls with Disadvantage.
        var rolls = new[]
        {
            InitiativeRoll(actor, surprised: false),
            InitiativeRoll(target, surprised: !expects),
        };
        var e = new Encounter { Id = World.NextEncounterId++, Area = actor.MapArea!.Value, RoundStart = World.Now };
        foreach (var roll in Initiative.Order(rolls))
            e.Order.Add(new Combatant
            {
                Actor = new ActorId(roll.Id), Side = roll.Id == actor.Id.Value ? 0 : 1,
                Initiative = roll.Roll.Total, InitiativeBonus = roll.Bonus, OpeningSpent = !expects && roll.Id == actor.Id.Value,
            });
        var map = World.Maps[e.Area];
        NoteSensesChange(map);
        World.Encounters.Add(e.Id, e);
        NoteSensesChange(map);
        RememberDoors();
        Record(e, CombatLogKind.Started, actor, target,
            (a, t) => expects ? $"{a} attacca {t}, che se lo aspettava: scontro!" : $"{a} attacca {t} di sorpresa!");
        var started = World.RecordFact("CombatStarted", $"{actor.Name} attacca {target.Name}{(expects ? "" : " di sorpresa")}.",
            Participants(actor, target));
        WitnessFight(e, actor, target, opening: true, started); // C4a: who sees it start knows who started it

        string message;
        if (!expects)
        {
            message = ResolveAttack(e, actor, target, attack, nonLethal, CombatLogKind.Attack);
        }
        else
        {
            e.Opening = new OpeningAttack(actor.Id, target.Id, attack.Name, nonLethal);
            Record(e, CombatLogKind.OpeningAttackPending, actor, target,
                (a, t) => $"{t} se lo aspettava: prima l'iniziativa, poi {a} attaccherà al suo turno.");
            message = $"{target.Name} se lo aspettava: si tira l'iniziativa.";
        }
        foreach (var roll in rolls)
        {
            var who = World.Actors[new ActorId(roll.Id)];
            Record(e, CombatLogKind.Initiative, who, null, (a, _) => $"Iniziativa di {a}: {roll.Roll.Describe()}.", roll.Roll,
                playerText: who.IsPlayer ? null : (a, _) => $"{a} tira l'iniziativa."); // C5a: a foe's roll stays behind the screen
        }
        if (!CheckEnd(e))
            RunTurns(e);
        return new CommandResult { Message = message };
    }

    private CommandResult EndTurn(EndTurnCommand command)
    {
        var actor = World.Actors[command.Actor];
        if (EncounterOf(actor) is not { } e)
            return CommandResult.Rejected(RejectionReason.NotInCombat, "Non stai combattendo.");
        if (!IsTurnOf(e, actor))
            return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
        ConditionsAtTurnEnd(actor); // R2
        e.Turn++;
        RunTurns(e);
        return new CommandResult { Message = "Fine del turno." };
    }

    private CommandResult CombatMove(Encounter e, Actor actor, MoveCommand command)
    {
        if (!IsTurnOf(e, actor))
            return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
        if (command.Stealthy)
            return CommandResult.Rejected(RejectionReason.InCombat, "Durante uno scontro non puoi muoverti di soppiatto.");
        var left = MovementLeft(e, actor);
        if (left <= 0)
            return CommandResult.Rejected(RejectionReason.NoMovementLeft, "Non ti resta movimento in questo turno.");
        if (FearAhead(actor, Spot.CenterOf(command.To)) is { } feared)
            return CommandResult.Rejected(RejectionReason.Unreachable, $"Hai troppa paura per avvicinarti a {feared.Name}."); // R2
        if (PlanCombatMove(e, actor, command.To, command.StopNextTo, out var twoDoors) is not { } plan)
            return CommandResult.Rejected(RejectionReason.Unreachable, twoDoors ? TwoDoors : "Non si può raggiungere quel punto.");
        if (plan.Path.Count == 0)
            return CommandResult.Rejected(RejectionReason.AlreadyThere, $"{actor.Name} è già lì.");
        if (plan.Feet > left)
            return CommandResult.Rejected(RejectionReason.TooFar, $"Troppo lontano: servono {plan.Feet} piedi, te ne restano {left}.");
        var (walked, stopped) = WalkInCombat(e, actor, plan);
        CheckEnd(e);
        return new CommandResult
        {
            Message = stopped is not null ? $"{actor.Name} si ferma dopo {walked} caselle: {stopped}." : $"{actor.Name} si sposta di {walked} caselle.",
        };
    }

    /// <summary>C2: what a move to <paramref name="to"/> would do, by the same code as the command, without touching anything.</summary>
    internal MovePreview PreviewMove(ActorId actorId, GridPos to)
    {
        if (!World.Actors.TryGetValue(actorId, out var actor))
            return new MovePreview { Valid = false, Reason = RejectionReason.ActorNotFound, Message = "Attore sconosciuto." };
        if (EncounterOf(actor) is not { } e)
            return new MovePreview { Valid = false, Reason = RejectionReason.NotInCombat, Message = "Non stai combattendo." };
        if (!IsTurnOf(e, actor))
            return new MovePreview { Valid = false, Reason = RejectionReason.NotYourTurn, Message = "Non è il tuo turno." };
        if (PlanCombatMove(e, actor, to, stopNextTo: false, out var twoDoors) is not { } plan)
            return new MovePreview { Valid = false, Reason = RejectionReason.Unreachable, Message = twoDoors ? TwoDoors : "Non si può raggiungere quel punto." };
        var left = MovementLeft(e, actor);
        var (reason, message) = plan.Path.Count == 0 ? (RejectionReason.AlreadyThere, "Sei già lì.")
            : plan.Feet > left ? (RejectionReason.TooFar, $"Troppo lontano: servono {plan.Feet} piedi, te ne restano {left}.")
            : (RejectionReason.None, $"{plan.Feet} piedi di movimento.");

        // Opportunity attacks along the way, by the threats the player knows of (each has one Reaction).
        var player = World.Actors[World.Player];
        var provokes = new List<ProvocationView>();
        var used = e.Order.Where(c => c.ReactionUsed).Select(c => c.Actor).ToHashSet();
        var from = StandingOf(actor);
        for (var i = 0; i < plan.Path.Count; i++)
        {
            foreach (var foe in Threats(e, actor, from, Centre(plan.Path[i])).Where(f => !used.Contains(f.Id) && SeesActor(player, f)))
            {
                used.Add(foe.Id);
                provokes.Add(new ProvocationView { Step = i, By = foe.Id, Name = foe.Name });
            }
            from = Centre(plan.Path[i]);
        }
        return new MovePreview
        {
            Valid = reason == RejectionReason.None, Reason = reason, Message = message,
            Path = plan.Path, CostFeet = plan.Cost, TotalFeet = plan.Feet, Provokes = provokes, OpensDoor = plan.OpensDoor,
        };
    }

    // ---------------------------------------------------------------- turns and rounds

    /// <summary>Plays the turns from the current one: NPCs at once, until the player's turn (the world waits) or the round ends.</summary>
    private void RunTurns(Encounter e)
    {
        while (World.Encounters.ContainsKey(e.Id))
        {
            if (e.Turn >= e.Order.Count)
            {
                CloseRound(e);
                return;
            }
            var c = e.Order[e.Turn];
            var actor = World.Actors[c.Actor];
            var acts = BeginTurn(e, c, actor);
            if (!World.Encounters.ContainsKey(e.Id))
                return;
            if (acts)
            {
                if (actor.IsPlayer)
                    return; // the world waits for the player's commands, then EndTurn
                NpcTurn(e, c, actor);
                ConditionsAtTurnEnd(actor); // R2
                if (!World.Encounters.ContainsKey(e.Id))
                    return;
            }
            e.Turn++;
        }
    }

    /// <summary>The start of a turn: movement and Reaction back, a Death Saving Throw if dying, the opening attack due.</summary>
    private bool BeginTurn(Encounter e, Combatant c, Actor actor)
    {
        c.MovedInches = 0;
        c.ReactionUsed = false; // SRD: a Reaction comes back at the start of one's own turn
        c.Dashes = 0;
        c.Disengaged = false;
        c.Dodging = false; // C3a: a Dodge lasts until the start of one's next turn
        c.Interacted = false; // C3b
        c.Cleaved = false; // R2
        c.BonusActionUsed = false; // R3a
        c.AttacksLeft = 0;
        c.SpellSlotSpent = false; // R3b
        c.AttackedWithLight = c.OffHandUsed = false; // R4b
        ConditionsAtTurnStart(e, actor); // R2: what lasts until the start of its turn ends
        c.ActionUsed = c.OpeningSpent;
        c.OpeningSpent = false;
        if (c.JoinedInRound == e.Round)
            return false; // C4c: who joined during this round acts from the next one
        if (actor.Vitality.State == LifeState.Dying)
        {
            DeathSave(e, actor);
            if (CheckEnd(e))
                return false;
        }
        if (!actor.Conscious)
            return false;
        Record(e, CombatLogKind.TurnStarted, actor, null, (a, _) => $"Round {e.Round}: tocca a {a}.");
        if (Incapacitated(actor)) // R2, SRD Incapacitated: no action; ADAPTATION: its turn passes
        {
            Record(e, CombatLogKind.TurnStarted, actor, null, (a, _) => $"{a} non può agire.");
            return false;
        }

        if (e.Opening is { } opening && opening.Attacker == actor.Id)
        {
            e.Opening = null;
            var target = World.Actors[opening.Target];
            var attack = ProfileOf(actor).Attacks.First(a => a.Name == opening.Attack);
            var check = CheckAttack(e, actor, target, attack, opening.NonLethal);
            if (check.Valid)
            {
                c.ActionUsed = true;
                ResolveAttack(e, actor, target, attack, opening.NonLethal, CombatLogKind.Attack);
                if (CheckEnd(e))
                    return false;
            }
            else
                Record(e, CombatLogKind.OpeningAttackCancelled, actor, target,
                    (a, t) => $"L'attacco di {a} contro {t} non parte: {check.Why} L'azione resta libera.");
        }
        return true;
    }

    private void DeathSave(Encounter? e, Actor actor)
    {
        var outcome = actor.Vitality.RollDeathSave(World.Rng, out var rolled, ExhaustionPenalty(actor));
        var text = outcome switch
        {
            DeathSaveOutcome.RegainedConsciousness => "20 naturale: riprende i sensi con 1 punto ferita",
            DeathSaveOutcome.Stabilized => "successo: è stabile",
            DeathSaveOutcome.Died => "fallimento: muore",
            DeathSaveOutcome.Success => $"successo ({actor.Vitality.DeathSaveSuccesses} su 3)",
            _ => $"fallimento ({actor.Vitality.DeathSaveFailures} su 3)",
        };
        if (e is not null)
            Record(e, CombatLogKind.DeathSave, actor, null, (a, _) => $"{a}, tiro contro la morte: d20 {rolled}, {text}.",
                d20: new[] { rolled }, kept: rolled);
        World.RecordFact("DeathSave", $"{actor.Name}, tiro contro la morte: d20 {rolled}, {text}.", actor.Id);
        if (outcome == DeathSaveOutcome.Died)
            Fell(e, actor, null, DamageOutcome.Killed);
    }

    /// <summary>Every turn of the round is played: a round with no attack and no enemy in sight ends the fight; otherwise the next round in 6 s.</summary>
    private void CloseRound(Encounter e)
    {
        if (Escapes(e))
            return; // the fight ended: a side has nobody left in it
        CloseRoundAfterEscapes(e);
    }

    private void CloseRoundAfterEscapes(Encounter e)
    {
        if (!e.AttackedThisRound && !AnyoneSeesAnEnemy(e))
        {
            EndEncounter(e, "Nessuno vede più l'avversario: lo scontro finisce.");
            return;
        }
        e.RoundClosed = true;
        World.Scheduler.Schedule(e.RoundStart.Plus(Duration.FromSeconds(6)), new CombatRound(e.Id));
    }

    /// <summary>The scheduled start of the next round.</summary>
    private void StartRound(long id)
    {
        if (!World.Encounters.TryGetValue(id, out var e))
            return;
        e.Round++;
        e.RoundStart = World.Now;
        e.Turn = 0;
        e.RoundClosed = false;
        e.AttackedThisRound = false;
        RunTurns(e);
    }

    // Review P3b: a foe lying unconscious keeps no fight going on its own (nobody strikes the fallen).
    private bool AnyoneSeesAnEnemy(Encounter e) =>
        e.Order.Select(c => World.Actors[c.Actor]).Any(a => a.Conscious && EnemiesOf(e, a).Any(foe => foe.Conscious && SeesActor(a, foe)));

    /// <summary>A side with nobody conscious left: the fight ends. Returns true if it ended.</summary>
    private bool CheckEnd(Encounter e)
    {
        if (!World.Encounters.ContainsKey(e.Id))
            return true;
        var down = e.Order.GroupBy(c => c.Side).FirstOrDefault(side => side.All(c => !World.Actors[c.Actor].Conscious));
        if (down is null)
            return false;
        EndEncounter(e, "Un lato non ha più nessuno in piedi: lo scontro finisce.");
        return true;
    }

    private void EndEncounter(Encounter e, string why)
    {
        var actors = e.Order.Select(c => World.Actors[c.Actor]).ToList();
        foreach (var fought in actors)
            fought.LastHarmAt = World.Now; // R1: an NPC's 8 hours of recovery start from the end of the fight
        Record(e, CombatLogKind.Ended, null, null, (_, _) => why);
        if (actors.Any(a => a.IsPlayer)) // P2b: its last lines stay readable once it is over
            World.LastFight = new LastFight(e.Area, e.Log.ToList());
        World.RecordFact("CombatEnded", why, actors.Where(a => a.IsPlayer || actors.Any(o => o.IsPlayer && SeesActor(o, a)))
            .Select(a => a.Id).ToArray());
        World.Scheduler.RemoveRound(e.Id);
        var map = World.Maps[e.Area];
        NoteSensesChange(map);
        World.Encounters.Remove(e.Id);
        NoteSensesChange(map);
        RememberDoors();
        foreach (var actor in actors.Where(a => !a.Conscious && a.Vitality.State != LifeState.Dead))
            BeginDowned(actor);
        DecideForFreeNpcs(); // the others go back to their lives at once
    }

    // ---------------------------------------------------------------- lying unconscious outside a fight

    /// <summary>Dying: a Death Saving Throw every 6 s; Stable: 1 HP back after 1d4 hours; knocked out: awake after 1 hour.</summary>
    private void BeginDowned(Actor actor)
    {
        var state = actor.Vitality.State;
        var wait = state == LifeState.Dying ? Duration.FromSeconds(6)
            : state == LifeState.Stable ? Duration.FromHours(World.Rng.Roll(4))
            : Duration.FromHours(1);
        Begin(actor, new DownedAction
        {
            Id = World.AllocateActionId(), Actor = actor.Id, StartedAt = World.Now, CompletesAt = World.Now.Plus(wait),
            Description = state == LifeState.Dying ? "Morente" : "Privo di sensi",
        });
    }

    private void CompleteDowned(Actor actor)
    {
        switch (actor.Vitality.State)
        {
            case LifeState.Dying:
                DeathSave(null, actor);
                break;
            case LifeState.Stable:
                actor.Vitality.Heal(1);
                World.RecordFact("Recovered", $"{actor.Name} riprende i sensi con 1 punto ferita.", actor.Id);
                break;
            case LifeState.Alive when actor.Vitality.KnockedOut:
                actor.Vitality.Wake();
                World.RecordFact("Recovered", $"{actor.Name} si riprende dopo un'ora priva di sensi.", actor.Id);
                break;
        }
        if (!actor.Conscious && actor.Vitality.State != LifeState.Dead)
            BeginDowned(actor);
    }

    // ---------------------------------------------------------------- attacks

    private sealed record AttackCheck(RejectionReason Reason, string? Why, bool Advantage = false, bool Disadvantage = false,
        bool LongRange = false, bool AutoCritical = false, CoverDegree Cover = CoverDegree.None, Spot? Aim = null, Spot? Origin = null)
    {
        public bool Valid => Reason == RejectionReason.None;

        /// <summary>R2: why the roll has Advantage or Disadvantage, in short words.</summary>
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Whether an attack can be made now, and how (SRD): melee within reach; ranged in sight and, P2b, along a clear
    /// straight line, with Disadvantage beyond the normal range or with a conscious enemy within 5 feet that sees it; Advantage against a target that
    /// does not see the attacker or is unconscious; every hit critical on an unconscious target within 5 feet. In C2
    /// only a target in sight can be attacked.
    /// </summary>
    private AttackCheck CheckAttack(Encounter? e, Actor attacker, Actor target, AttackProfile attack, bool nonLethal)
    {
        if (target.Id == attacker.Id || (e is not null && Allies(e, attacker, target)))
            return new(RejectionReason.NotHostile, $"{target.Name} è dalla tua parte.");
        if (target.Vitality.State == LifeState.Dead)
            return new(RejectionReason.AlreadyDone, $"{target.Name} è già morto.");
        if (attacker.Conditions.Any(c => c.Kind == ConditionKind.Charmed && c.Source == target.Id) && Has(attacker, ConditionKind.Charmed))
            return new(RejectionReason.NotHostile, $"Non puoi attaccare {target.Name}: ti ha affascinato."); // R2, SRD Charmed
        if (Incapacitated(attacker))
            return new(RejectionReason.NoActionLeft, $"{attacker.Name} non può agire."); // R2, SRD Incapacitated
        if (!SeesActor(attacker, target, ignoreFacing: e is null))
            return new(RejectionReason.NotVisible, $"Non vedi {target.Name}.");
        if (attack.Item is { } needed && attacker.Carried(needed) == 0) // C3b
            return new(RejectionReason.NoAmmo, $"Non ne hai più: {World.Content.Item(needed)?.Name ?? needed}.");
        if (attack.Requires is { } weapon && attacker.Carried(weapon) == 0) // D1a: no weapon in hand, no attack with it
            return new(RejectionReason.NoWeapon, $"Non hai più: {World.Content.Item(weapon)?.Name ?? weapon}.");
        var map = World.Maps[attacker.MapArea!.Value];
        var from = CurrentSpot(attacker)!.Value;
        var to = CurrentSpot(target)!.Value;
        var (origin, aim, blocked) = AimAt(map, from, to); // P3: where the blow or shot leaves from and goes, and the cover
        bool disadvantage = false, longRange = false;
        if (attack.Melee)
        {
            if (!InReach(map, StandingOf(attacker), attack, StandingOf(target)))
                return new(RejectionReason.TooFar, $"{target.Name} è fuori portata: avvicinati.");
        }
        else
        {
            if (nonLethal)
                return new(RejectionReason.UnknownAttack, "Si può tramortire solo con un attacco in mischia.");
            // P2b: the range from centre to centre, to the inch (along a row it is the squares' count; ADAPTATION: on a
            // diagonal the true distance, not 5 feet a square); the shot flies straight and needs a clear line.
            var (normal, longest) = attack.Range!.Value;
            var inches = from.DistanceTo(to);
            if (inches > longest * Spot.PerFoot)
                return new(RejectionReason.TooFar, $"{target.Name} è oltre la gittata ({longest} piedi).");
            if (aim is null)
                return new(RejectionReason.NoLineOfFire, $"Non hai un tiro libero verso {target.Name}.");
            longRange = inches > normal * Spot.PerFoot;
            var threats = e is null ? new[] { target } : EnemiesOf(e, attacker);
            var closeCombat = threats.Any(foe => foe.Conscious && CurrentSpot(foe) is { } p && WithinFiveFeet(map, p, from)
                && SeesActor(foe, attacker, ignoreFacing: e is null));
            disadvantage = longRange || closeCombat;
        }
        var reasons = new List<string>();
        if (longRange)
            reasons.Add("gittata lunga");
        else if (disadvantage)
            reasons.Add("un nemico ti è addosso");
        // C3a, SRD Dodge: attacks against a dodging target that sees the attacker have Disadvantage (R2: not while Incapacitated).
        if (e?.Order.FirstOrDefault(c => c.Actor == target.Id) is { Dodging: true } && target.Conscious && !Incapacitated(target)
            && SeesActor(target, attacker))
        {
            disadvantage = true;
            reasons.Add("schiva");
        }
        var unseen = !SeesActor(target, attacker, ignoreFacing: e is null); // also an unconscious target: it sees nothing
        if (unseen)
            reasons.Add(target.Conscious ? "non ti vede" : "è privo di sensi");
        var close = WithinFiveFeet(map, from, to);
        var (advantage, worse, why) = ConditionEdges(attacker, target, close); // R2
        reasons.AddRange(why);
        return new(RejectionReason.None, null,
            Advantage: unseen || advantage,
            Disadvantage: disadvantage || worse, LongRange: longRange,
            AutoCritical: (target.Vitality.Unconscious || Has(target, ConditionKind.Paralyzed)) && close, // R2: Paralyzed too
            Cover: CoverOf(map, attacker, target, origin!.Value, aim!.Value, blocked), Aim: aim, Origin: origin) { Reasons = reasons };
    }

    /// <summary>
    /// P1b: a melee attack reaches between two creatures when their bodies are no more than the reach apart, edge to
    /// edge, with nothing solid in between (review P1b: never by the squares alone, or a body seven feet away would be
    /// within five). At the centres of their squares this is the grid rule for a 5-foot reach: next squares, diagonals
    /// included, unless a wall or a corner is in the way; two squares apart is out of reach.
    /// </summary>
    internal static bool InReach(GridMap map, (GridPos Cell, Spot At) from, AttackProfile attack, (GridPos Cell, Spot At) to) =>
        from.At.DistanceTo(to.At) - 2 * SpotPlanner.BodyRadius <= attack.ReachFeet * Spot.PerFoot && BodyInTheOpen(map, from.At, to.At); // P3: a point of the body in the open will do

    /// <summary>
    /// P2b: where a missed shot ends: aimed 24 inches beside the target's centre (beyond its body), on the side the
    /// kept d20 sets (even to the left of the line, odd to the right: no second roll), and flown straight on to the first
    /// solid thing or the long range, to the inch.
    /// </summary>
    private static Spot MissPoint(GridMap map, Spot from, Spot target, int kept, int longFeet)
    {
        double dx = target.X - from.X, dy = target.Y - from.Y, length = Math.Sqrt(dx * dx + dy * dy);
        var side = kept % 2 == 0 ? 1 : -1;
        double aimX = target.X - dy / length * 24 * side, aimY = target.Y + dx / length * 24 * side;
        double ux = aimX - from.X, uy = aimY - from.Y, aim = Math.Sqrt(ux * ux + uy * uy);
        Spot At(double along) => new((int)Math.Round(from.X + ux / aim * along, MidpointRounding.AwayFromZero),
            (int)Math.Round(from.Y + uy / aim * along, MidpointRounding.AwayFromZero));
        double clear = 0, blocked = longFeet * Spot.PerFoot;
        if (SpotPlanner.LineClear(map, from, At(blocked)))
            return At(blocked);
        while (blocked - clear > 0.5)
        {
            var middle = (clear + blocked) / 2;
            if (SpotPlanner.LineClear(map, from, At(middle)))
                clear = middle;
            else
                blocked = middle;
        }
        return At(clear);
    }

    /// <summary>P2b: two bodies are within 5 feet of each other, edge to edge, with nothing solid between them.</summary>
    private static bool WithinFiveFeet(GridMap map, Spot a, Spot b) =>
        a.DistanceTo(b) - 2 * SpotPlanner.BodyRadius <= 5 * Spot.PerFoot && BodyInTheOpen(map, a, b);

    /// <summary>P1b: where an actor stands now, as its square and its exact point.</summary>
    private (GridPos Cell, Spot At) StandingOf(Actor actor) => (CurrentPosition(actor)!.Value, CurrentSpot(actor)!.Value);

    private static (GridPos Cell, Spot At) Centre(GridPos square) => (square, Spot.CenterOf(square));

    /// <summary>Rolls the attack and applies the damage. Returns the line for the dice log.</summary>
    private string ResolveAttack(Encounter e, Actor attacker, Actor target, AttackProfile attack, bool nonLethal, CombatLogKind kind,
        int? smite = null, bool smiteFree = false)
    {
        var check = CheckAttack(e, attacker, target, attack, nonLethal);
        var cover = CoverBonus(check.Cover); // P3: cover raises the AC to beat
        var blessed = BlessRoll(attacker); // R3b: Bless, 1d4 more
        var result = Attacks.Roll(World.Rng, attack with { AttackBonus = attack.AttackBonus + ExhaustionPenalty(attacker) + blessed
                + SacredWeaponBonus(attacker, attack) }, // R2, R3b, R3c
            ProfileOf(target).ArmorClass + cover + SpellArmorBonus(target), check.Advantage, check.Disadvantage, check.AutoCritical);
        // R3b: Divine Favor and Divine Smite add Radiant damage to a hit; the smite's slot (or free use) and Bonus Action are spent then.
        var (spellDamage, spellText) = result.Hit ? ExtraOnHit(attacker, target, attack, result.Critical, smite) : (0, "");
        if (result.Hit && smite is { } smiteLevel)
        {
            if (smiteFree)
                attacker.FreeSmiteUsed = true;
            else
            {
                SpendSlot(attacker, smiteLevel);
                CombatantOf(e, attacker.Id).SpellSlotSpent = true;
            }
            CombatantOf(e, attacker.Id).BonusActionUsed = true;
        }
        if (blessed > 0)
            spellText = $" (Benedizione +{blessed})" + spellText;
        attacker.SappedBy = null; // R2: Sap and Vex are for the next attack roll only
        if (attacker.Vexing == target.Id)
            attacker.Vexing = null;
        // P2b: who struck is seen as the blow comes, before it lands (one knocked out by it still saw who did it).
        var player = World.Actors[World.Player];
        var sawAttacker = attacker.IsPlayer || SeesActor(player, attacker);
        var sawTarget = target.IsPlayer || SeesActor(player, target);
        var outcome = target.Vitality.TakeDamage(result.Damage + spellDamage, result.Critical, knockOut: nonLethal && attack.Melee);
        Harmed(target, "è ferito", result.Damage + spellDamage); // R1, R3b: Concentration
        // P2b: a shot flies straight from the attacker to its impact: the target if it hits, past it if it misses.
        var map = World.Maps[e.Area];
        Spot? shotFrom = attack.Melee ? null : check.Origin ?? CurrentSpot(attacker), shotTo = null; // P3: from where it leans out
        if (shotFrom is { } origin)
            shotTo = result.Hit ? check.Aim : MissPoint(map, origin, check.Aim!.Value, result.Roll.Kept, attack.Range!.Value.Long);
        if (attack.Item is { } item) // C3b: the javelin leaves the hand and lands by the target; the bolt is spent
        {
            attacker.Give(item, -1);
            Unequip(attacker, item); // D1a: a weapon thrown away leaves the hand
            if (attack.Fate == ItemFate.Thrown)
                World.Maps[e.Area].PutOnGround(CurrentPosition(target)!.Value, new ItemId(item), 1);
        }
        attacker.Sneak = null; // attacking gives one away (SRD Hide)
        attacker.FoughtWith.Add(target.Id);
        target.FoughtWith.Add(attacker.Id);
        e.AttackedThisRound = true;
        var how = kind == CombatLogKind.OpportunityAttack ? "coglie l'occasione e attacca" : "attacca";
        var extra = (check.Advantage ? " (vantaggio)" : "") + (check.Disadvantage ? " (svantaggio)" : "") + (check.LongRange ? " (gittata lunga)" : "")
            + CoverText(check.Cover);
        // Review P3: the player reads the cover only of a target it sees (it would give away the ground around it).
        var extraSeen = sawTarget ? extra : extra[..^CoverText(check.Cover).Length];
        // C5a: the player reads it from its side of the GM screen.
        var seen = result.DescribeFor(attacker.IsPlayer ? AttackPerspective.Attacker : target.IsPlayer ? AttackPerspective.Target : AttackPerspective.Witness);
        Record(e, kind, attacker, target, (a, t) => $"{a} {how} {t}{extra}. {result.Describe()}{spellText}.", result.Roll,
            attack: attack.Name, armorClass: result.TargetArmorClass, hit: result.Hit, critical: result.Critical, damage: result.Damage + spellDamage,
            playerText: (a, t) => $"{a} {how} {t}{extraSeen}. {seen}{spellText}.", shotFrom: shotFrom, shotTo: shotTo, sawMover: sawAttacker, cover: check.Cover);
        var blow = World.RecordFact("Attack", $"{attacker.Name} {how} {target.Name}: {result.Describe()}{spellText}.",
            $"{attacker.Name} {how} {target.Name}: {seen}{spellText}.", Participants(attacker, target));
        LearnFrom(attacker, target, attack, result, cover); // C5a
        WitnessFight(e, attacker, target, opening: false, blow);
        Fell(e, target, attacker, outcome);
        if (World.Encounters.ContainsKey(e.Id))
            ApplyMastery(e, attacker, target, attack, result); // R2
        return $"{attacker.Name} {how} {target.Name}{extra}. {seen}{spellText}.";
    }

    /// <summary>Records a creature dropping (down, knocked out, killed).</summary>
    private void Fell(Encounter? e, Actor target, Actor? by, DamageOutcome outcome)
    {
        var (kind, words) = outcome switch
        {
            DamageOutcome.Down => (CombatLogKind.Downed, "cade a terra, morente"),
            DamageOutcome.KnockedOut => (CombatLogKind.KnockedOut, "cade a terra, tramortito"),
            DamageOutcome.Killed => (CombatLogKind.Died, "muore"),
            _ => ((CombatLogKind?)null, ""),
        };
        if (kind is not { } k)
            return;
        if (e is not null)
            Record(e, k, target, by, (a, _) => $"{a} {words}.");
        var fell = World.RecordFact(k.ToString(), $"{target.Name} {words}.", by is null ? new[] { target.Id } : Participants(target, by));
        Defeat(e, target, by); // C5b
        if (k == CombatLogKind.Died && by is not null && e is not null)
            WitnessKilling(e, by, target, fell); // C4a: a death blow is a deed of its own
        // C4b, until there is a prison: an authority that knocks out someone it wants has settled the matter.
        if (k == CombatLogKind.KnockedOut && by?.Faction is { } faction && World.Factions[faction].Authority == by.Id
            && World.Factions[faction].Wanted.Remove(target.Id))
            World.RecordFact("WantedSettled", $"{by.Name} ha preso {target.Name}: per {World.Factions[faction].Name} la questione è chiusa.", by.Id, target.Id);
    }

    /// <summary>
    /// Fact participants: the player takes part only in what it can name, so the facts it is shown never reveal someone
    /// it did not see.
    /// </summary>
    private ActorId[] Participants(Actor a, Actor b)
    {
        bool Knows(Actor x, Actor other) => !x.IsPlayer || SeesActor(x, other);
        return new[] { a, b }.Where(x => Knows(x, x == a ? b : a)).Select(x => x.Id).ToArray();
    }

    // ---------------------------------------------------------------- movement

    private sealed record CombatPlan(IReadOnlyList<GridPos> Path, IReadOnlyList<int> Cost, int Feet)
    {
        /// <summary>P5b: the closed door the move opens on the way, as the turn's free interaction.</summary>
        public GridPos? OpensDoor { get; init; }
    }

    /// <summary>Whole feet of movement left this turn (P1b: rounded down from the inches left).</summary>
    private int MovementLeft(Encounter e, Actor actor) => (int)Math.Floor(MovementLeftInches(e, actor) / (double)Spot.PerFoot);

    /// <summary>P1b: inches of movement left this turn.</summary>
    private int MovementLeftInches(Encounter e, Actor actor)
    {
        var left = CombatSpeed(actor) * Spot.PerFoot * (1 + CombatantOf(e, actor.Id).Dashes) - CombatantOf(e, actor.Id).MovedInches // C3a: Dash adds Speed
                   - (Has(actor, ConditionKind.Prone) ? CombatSpeed(actor) * Spot.PerFoot / 2 : 0); // R2: standing up first costs half the Speed
        return Dragging(actor) ? left / 2 : left; // R4a: dragging, every foot costs 2
    }

    /// <summary>R2: the Speed in a fight, after conditions, Exhaustion and Slow.</summary>
    private int CombatSpeed(Actor actor) => SpeedNow(actor, ProfileOf(actor).Speed);

    /// <summary>R2, SRD Prone: a prone mover stands up first, for half its Speed (already left out of the movement left).</summary>
    private void StandUp(Encounter e, Actor actor)
    {
        if (!Has(actor, ConditionKind.Prone))
            return;
        CombatantOf(e, actor.Id).MovedInches += CombatSpeed(actor) * Spot.PerFoot / 2;
        RemoveCondition(e, actor, ConditionKind.Prone, "si rialza");
    }

    /// <summary>
    /// The creatures a mover takes into account when planning: those it sees, allies included (there is no memory of
    /// where an unseen ally stands). Nobody plans around what it cannot perceive, so the client's green squares never
    /// reveal a hidden creature; the real move then deals with the creatures really there.
    /// </summary>
    private Func<Actor, bool> Perceived(Encounter e, Actor mover) => a => SeesActor(mover, a);

    /// <summary>
    /// P5b: as P4d for free moves, one closed door may be opened on the way while the turn's free interaction is unused
    /// (one that holds only with the mover's key). A way through two is replaced by one through none, if there is one;
    /// otherwise <paramref name="twoDoors"/> says why there is no plan.
    /// </summary>
    private CombatPlan? PlanCombatMove(Encounter e, Actor mover, GridPos to, bool stopNextTo, out bool twoDoors,
        Func<GridPos, bool>? endOk = null)
    {
        twoDoors = false;
        var map = World.Maps[e.Area];
        var opens = Opens(mover, map);
        Func<GridPos, bool>? doorOk = CombatantOf(e, mover.Id).Interacted ? null : door => !map.DoorHolds(door) || opens(door);
        var plan = PlanCombatMoveThrough(e, mover, to, stopNextTo, endOk, doorOk);
        if (plan is null || doorOk is null)
            return plan;
        var doors = DoorsOnGridWay(map, CurrentPosition(mover)!.Value, plan.Path);
        if (doors.Count <= 1)
            return plan with { OpensDoor = doors.Count == 1 ? doors[0] : null };
        var shut = PlanCombatMoveThrough(e, mover, to, stopNextTo, endOk, doorOk: null);
        twoDoors = shut is null;
        return shut;
    }

    /// <summary>The closed doors a way on squares goes through, in order.</summary>
    private static List<GridPos> DoorsOnGridWay(GridMap map, GridPos from, IReadOnlyList<GridPos> path)
    {
        var doors = new List<GridPos>();
        foreach (var square in path)
        {
            if (map.ClosedDoorBetween(from, square) is { } door && !doors.Contains(door))
                doors.Add(door);
            from = square;
        }
        return doors;
    }

    /// <summary>
    /// The cheapest way to <paramref name="to"/> (or, with <paramref name="stopNextTo"/>, to a square next to it) by the
    /// SRD rules, around the creatures the mover perceives: 5 feet a square, diagonals included, no cutting corners;
    /// closed doors block, but those <paramref name="doorOk"/> lets be opened on the way (P5b); an ally's space can be crossed, an incapacitated creature's space is
    /// Difficult Terrain, any other creature blocks; nobody ends on another creature. Null when there is no way. The
    /// movement left is not checked here. P1b: <paramref name="endOk"/>, if given, also judges the squares one may stop on.
    /// </summary>
    private CombatPlan? PlanCombatMoveThrough(Encounter e, Actor mover, GridPos to, bool stopNextTo, Func<GridPos, bool>? endOk,
        Func<GridPos, bool>? doorOk)
    {
        var map = World.Maps[e.Area];
        var from = CurrentPosition(mover)!.Value;
        var here = new CombatPlan(Array.Empty<GridPos>(), Array.Empty<int>(), 0);
        if (stopNextTo ? from.StepsTo(to) == 1 && OpenDiagonal(map, from, to) && (endOk?.Invoke(from) ?? true) : from == to)
            return here; // already there
        var knows = Perceived(e, mover);
        var occupants = OccupantsOf(map, mover, knows);
        var reach = CombatReach(e, mover, map, from, to, knows, doorOk: doorOk);
        var ends = stopNextTo
            ? reach.Keys.Where(p => p.StepsTo(to) == 1 && OpenDiagonal(map, p, to) && (endOk?.Invoke(p) ?? true))
            : reach.Keys.Where(p => p == to);
        var goal = ends.Where(p => !occupants.Contains(p))
            .OrderBy(p => reach[p].Feet).ThenBy(p => reach[p].Drift).ThenBy(p => p.Y).ThenBy(p => p.X)
            .Select(p => (GridPos?)p).FirstOrDefault();
        if (goal is not { } end)
            return null;
        var path = new List<GridPos>();
        for (var p = end; p != from; p = reach[p].Prev)
            path.Add(p);
        path.Reverse();
        return new CombatPlan(path, path.Select(p => reach[p].Feet).ToArray(), reach[end].Feet);
    }

    /// <summary>Other creatures on the map that <paramref name="counts"/> keeps, by square.</summary>
    private ILookup<GridPos, Actor> CreaturesOn(GridMap map, Actor mover, Func<Actor, bool> counts) =>
        World.Actors.Values.Where(a => a.Id != mover.Id && a.MapArea == map.Area && CurrentPosition(a) is not null && counts(a))
            .ToLookup(a => CurrentPosition(a)!.Value);

    /// <summary>Squares where a creature other than the mover stands, among those it counts (nobody ends a move there).</summary>
    private HashSet<GridPos> OccupantsOf(GridMap map, Actor mover, Func<Actor, bool> counts) =>
        CreaturesOn(map, mover, counts).Select(g => g.Key).ToHashSet();

    /// <summary>
    /// SRD Moving Around Other Creatures: an ally's space is free to cross, an incapacitated creature's is Difficult
    /// Terrain, any other creature's blocks.
    /// </summary>
    private static (int Feet, bool Blocked) EnterCost(Encounter e, Actor mover, IEnumerable<Actor> there)
    {
        var feet = FeetPerSquare;
        foreach (var other in there)
        {
            if (Allies(e, mover, other))
                continue;
            if (other.Conscious)
                return (feet, true);
            feet = 2 * FeetPerSquare; // Difficult Terrain
        }
        return (feet, false);
    }

    /// <summary>Dijkstra over feet from <paramref name="from"/>; ties go to the path closest to the straight line to <paramref name="aim"/>.</summary>
    private Dictionary<GridPos, (int Feet, long Drift, GridPos Prev)> CombatReach(Encounter e, Actor mover, GridMap map, GridPos from,
        GridPos aim, Func<Actor, bool> knows, int? maxFeet = null, Func<GridPos, bool>? doorOk = null)
    {
        long Drift(GridPos p) => Math.Abs((long)(aim.X - from.X) * (p.Y - from.Y) - (long)(aim.Y - from.Y) * (p.X - from.X));
        var others = CreaturesOn(map, mover, knows);
        var best = new Dictionary<GridPos, (int Feet, long Drift, GridPos Prev)>();
        var open = new SortedSet<(int Feet, long Drift, int Y, int X)> { (0, 0, from.Y, from.X) };
        var dist = new Dictionary<GridPos, (int Feet, long Drift)> { [from] = (0, 0) };
        while (open.Count > 0)
        {
            var current = open.Min;
            open.Remove(current);
            var here = new GridPos(current.X, current.Y);
            foreach (var (dx, dy) in Steps)
            {
                var next = new GridPos(here.X + dx, here.Y + dy);
                if (!map.IsWalkable(next) || !map.DiagonalOpen(here, next))
                    continue;
                if (map.ClosedDoorBetween(here, next) is { } door && doorOk?.Invoke(door) != true)
                    continue; // P5b: a closed door is crossed only where it may be opened on the way
                var (cost, blocked) = EnterCost(e, mover, others[next]);
                if (blocked)
                    continue;
                var candidate = (Feet: current.Feet + cost, Drift: current.Drift + Drift(next));
                if (maxFeet is { } max && candidate.Feet > max)
                    continue;
                if (dist.TryGetValue(next, out var known))
                {
                    if (known.CompareTo(candidate) <= 0)
                        continue;
                    open.Remove((known.Feet, known.Drift, next.Y, next.X));
                }
                dist[next] = candidate;
                best[next] = (candidate.Feet, candidate.Drift, here);
                open.Add((candidate.Feet, candidate.Drift, next.Y, next.X));
            }
        }
        return best;
    }

    private static readonly (int Dx, int Dy)[] Steps =
        { (0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1) };

    /// <summary>The squares the player can move to now, with their cost, around the creatures it perceives.</summary>
    private IReadOnlyList<ReachableSquareView> ReachableSquares(Encounter e, Actor actor)
    {
        var left = MovementLeft(e, actor);
        if (left <= 0)
            return Array.Empty<ReachableSquareView>();
        var map = World.Maps[e.Area];
        var from = CurrentPosition(actor)!.Value;
        var knows = Perceived(e, actor);
        var occupants = OccupantsOf(map, actor, knows);
        var reach = CombatReach(e, actor, map, from, from, knows, left).ToDictionary(r => r.Key, r => r.Value.Feet);
        // P5b: beyond a closed door too, while the turn's interaction is free, where the way opens only that one (the cheaper of the two ways, as the move plans it).
        if (!CombatantOf(e, actor.Id).Interacted)
        {
            var opens = Opens(actor, map);
            var through = CombatReach(e, actor, map, from, from, knows, left, door => !map.DoorHolds(door) || opens(door));
            foreach (var (square, way) in through.Where(r => !reach.TryGetValue(r.Key, out var shut) || r.Value.Feet < shut))
            {
                var path = new List<GridPos>();
                for (var p = square; p != from; p = through[p].Prev)
                    path.Add(p);
                path.Reverse();
                if (DoorsOnGridWay(map, from, path).Count <= 1)
                    reach[square] = way.Feet;
            }
        }
        return reach
            .Where(r => !occupants.Contains(r.Key))
            .OrderBy(r => r.Key.Y).ThenBy(r => r.Key.X)
            .Select(r => new ReachableSquareView { At = r.Key, CostFeet = r.Value })
            .ToArray();
    }

    /// <summary>Enemies whose reach <paramref name="mover"/> leaves stepping from <paramref name="from"/> to <paramref name="to"/>, seeing it, with a Reaction to spend.</summary>
    private IEnumerable<Actor> Threats(Encounter e, Actor mover, (GridPos Cell, Spot At) from, (GridPos Cell, Spot At) to)
    {
        if (CombatantOf(e, mover.Id).Disengaged)
            yield break; // C3a: Disengage
        var map = World.Maps[e.Area];
        foreach (var c in e.Order)
        {
            var foe = World.Actors[c.Actor];
            if (foe.Id == mover.Id || Allies(e, foe, mover) || !foe.Conscious || c.ReactionUsed || Incapacitated(foe)
                || MeleeAttack(foe) is not { } attack || CurrentPosition(foe) is null)
                continue;
            var at = StandingOf(foe);
            if (InReach(map, at, attack, from) && !InReach(map, at, attack, to)
                && Sees(map, foe, at.Cell, mover, from.Cell, LightOn(map, from.Cell), atSpot: from.At))
                yield return foe;
        }
    }

    internal static AttackProfile? MeleeAttack(Actor actor) => ProfileOf(actor).Attacks.FirstOrDefault(a => a.Melee);

    /// <summary>
    /// Walks a planned path square by square, against the creatures really there: one the mover did not perceive stops
    /// it before its square (and nobody stays on another creature: it steps back). Before each step out of an enemy's
    /// reach, that enemy may make an Opportunity Attack (its Reaction); a mover that drops stops there. Returns the
    /// squares walked, and why it stopped short (null if it did not).
    /// </summary>
    private (int Walked, string? Stopped) WalkInCombat(Encounter e, Actor actor, CombatPlan plan)
    {
        var me = CombatantOf(e, actor.Id);
        var map = World.Maps[e.Area];
        StandUp(e, actor); // R2
        var budget = CombatSpeed(actor) * (1 + me.Dashes); // C3a: a Dash counts
        var dragFrom = CurrentSpot(actor)!.Value; // R4a
        var dragging = Dragging(actor);
        var everyone = CreaturesOn(map, actor, _ => true);
        actor.Sneak = null;
        var start = CurrentPosition(actor)!.Value;
        var startAt = CurrentSpot(actor)!.Value;
        var walked = new List<GridPos>();
        var from = StandingOf(actor);
        string? stopped = null;
        foreach (var square in plan.Path)
        {
            var (feet, blocked) = EnterCost(e, actor, everyone[square]);
            if (blocked)
            {
                stopped = "qualcuno gli sbarra la strada";
                break;
            }
            if (me.MovedInches + feet * Spot.PerFoot * (dragging ? 2 : 1) > budget * Spot.PerFoot)
            {
                stopped = "il passo costa più del previsto";
                break;
            }
            // P5b: a closed door on the way opens as the turn's free interaction; with it used, or one that holds, it stops here.
            if (map.ClosedDoorBetween(from.Cell, square) is { } door)
            {
                if (me.Interacted || map.DoorHolds(door) && !Opens(actor, map)(door))
                {
                    stopped = "la porta è chiusa";
                    break;
                }
                OpenOnTheWayInFight(actor, map, door);
                me.Interacted = true;
            }
            foreach (var foe in Threats(e, actor, from, Centre(square)).ToList())
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
            me.MovedInches += feet * Spot.PerFoot * (dragging ? 2 : 1); // R4a
            Place(actor, square);
            walked.Add(square);
            from = Centre(square);
        }
        // Nobody ends a move on another creature: step back to the last free square walked. ADAPTATION: the steps back
        // cost nothing more (the feet of the steps forward stay spent). The log's path is every square really entered,
        // in order, steps back included, so it always ends where the mover stands.
        var trail = new List<GridPos>(walked);
        var back = 0;
        while (walked.Count > 0 && everyone[walked[^1]].Any())
        {
            walked.RemoveAt(walked.Count - 1);
            var to = walked.Count > 0 ? walked[^1] : start;
            Place(actor, to);
            trail.Add(to);
            back++;
        }
        if (dragging)
            DragAlong(e, actor, dragFrom, CurrentSpot(actor)!.Value); // R4a
        EvaluateContacts();
        var why = back > 0 ? $"c'è qualcuno, torna indietro di {back} {(back == 1 ? "casella" : "caselle")}"
            : stopped;
        if (trail.Count > 0 || why is not null)
            Record(e, CombatLogKind.Move, actor, null,
                (a, _) => $"{a} si sposta di {walked.Count} caselle{(why is null ? "" : $" e si ferma: {why}")}.", path: trail,
                spots: trail.Select(Spot.CenterOf).Prepend(startAt).ToList()); // only the player walks on squares in a fight (P2a)
        return (walked.Count, why);
    }

    // ---------------------------------------------------------------- NPCs in a fight

    /// <summary>
    /// An NPC's turn (C2, readable rather than clever): the nearest conscious enemy in sight; melee if in reach, else a
    /// ranged attack if it can make one, else it walks up and attacks if it gets there. With nobody in sight it goes
    /// where it last saw an enemy. Fleeing and surrender come with C4.
    /// </summary>
    private void NpcTurn(Encounter e, Combatant c, Actor npc)
    {
        if (FleeTurn(e, c, npc))
            return; // C4c: a commoner badly hurt runs instead of fighting
        Actor? Target() => EnemiesOf(e, npc).Where(f => f.Conscious && SeesActor(npc, f))
            .OrderBy(f => CurrentSpot(npc)!.Value.DistanceTo(CurrentSpot(f)!.Value)).ThenBy(f => f.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();

        AttackProfile? Usable(Actor target) =>
            ProfileOf(npc).Attacks.Where(a => a.Melee).Concat(ProfileOf(npc).Attacks.Where(a => !a.Melee))
                .FirstOrDefault(a => !a.OffHand && CheckAttack(e, npc, target, a, nonLethal: false).Valid && !KeepsLastWeapon(npc, a));

        bool TryAttack(Actor target, bool tookCover = false)
        {
            if (c.ActionUsed || Usable(target) is not { } attack)
                return false;
            c.ActionUsed = true;
            ResolveAttack(e, npc, target, attack, nonLethal: attack.Melee && Spares(npc, target), CombatLogKind.Attack);
            // Review P3b: a shot from cover keeps the reason it moved in its trace.
            npc.LastDecision = tookCover
                ? new DecisionTrace(World.Now, "Scontro: si ripara", $"Si mette al riparo, poi attacca {target.Name} con {attack.Name}.", Array.Empty<string>())
                : new DecisionTrace(World.Now, "Scontro: attacca", $"Attacca {target.Name} con {attack.Name}.", Array.Empty<string>());
            CheckEnd(e);
            return true;
        }

        var target = Target();
        if (target is not null)
        {
            c.LastSeenAt = CurrentSpot(target);
            // P3b: about to shoot (no blow in reach), it may first step into cover.
            var tookCover = !c.ActionUsed && Usable(target) is { Melee: false } shot && SeekCover(e, npc, target, shot);
            if (tookCover && (CheckEnd(e) || !npc.Conscious))
                return;
            if (TryAttack(target, tookCover) || !World.Encounters.ContainsKey(e.Id))
                return;
        }
        if (Has(npc, ConditionKind.Grappled) && !c.ActionUsed) // R4a: held and nobody in reach, it struggles free
        {
            Escape(e, npc);
            return;
        }
        // P2a: on exact points, by the same planning and walk as the player's free moves.
        var aim = target is not null ? CurrentSpot(target) : c.LastSeenAt;
        if (aim is not { } spot || spot == CurrentSpot(npc))
        {
            npc.LastDecision = new DecisionTrace(World.Now, "Scontro: aspetta", "Nessun avversario in vista.", Array.Empty<string>());
            return;
        }
        if (PlanNpcApproach(e, npc, target, spot) is not var (way, limit))
        {
            if (target is null || MeleeAttack(npc) is not { } m || !InReach(World.Maps[e.Area], StandingOf(npc), m, StandingOf(target)))
                npc.LastDecision = new DecisionTrace(World.Now, "Scontro: aspetta", "Nessuna via verso l'avversario.", Array.Empty<string>());
            return;
        }
        // C3a: out of reach this turn, and nothing better to do with the action: Dash to close in.
        if (Math.Min(limit, way.Length) > MovementLeftInches(e, npc) && !c.ActionUsed)
            Act(e, npc, CombatAction.Dash);
        if (NpcStep(e, npc, way, limit) is { } step)
        {
            npc.LastDecision = new DecisionTrace(World.Now, "Scontro: si avvicina",
                target is not null ? $"Va verso {target.Name}." : "Va dove ha visto l'avversario l'ultima volta.", Array.Empty<string>());
            WalkFreeInCombat(e, npc, step);
            if (CheckEnd(e) || !npc.Conscious)
                return;
        }
        if (Target() is { } now)
        {
            c.LastSeenAt = CurrentSpot(now);
            TryAttack(now);
        }
    }

    // ---------------------------------------------------------------- the log, as the player perceives it

    /// <summary>
    /// Records an event of the fight. <paramref name="text"/> builds the line from the two names: the real ones for the
    /// record, "Qualcuno" for those the player does not see in its version. The player perceives what involves it or
    /// someone it sees; the start, turns and end of a fight it is in; nothing else (hearing a fight is C4).
    /// </summary>
    private void Record(Encounter e, CombatLogKind kind, Actor? actor, Actor? target, Func<string, string, string> text,
        D20Roll? roll = null, string? attack = null, int? armorClass = null, bool? hit = null, bool critical = false,
        int? damage = null, IReadOnlyList<GridPos>? path = null, IReadOnlyList<int>? d20 = null, int? kept = null,
        Func<string, string, string>? playerText = null, IReadOnlyList<Spot>? spots = null, bool? sawMover = null,
        Spot? shotFrom = null, Spot? shotTo = null, CoverDegree cover = CoverDegree.None)
    {
        var player = World.Actors[World.Player];
        bool Seen(Actor? x) => x is not null && (x.IsPlayer || SeesActor(player, x));
        var inFight = e.Order.Any(c => c.Actor == player.Id);
        var sawActor = sawMover ?? Seen(actor); // P2a: a mover seen anywhere along its move; P2b: an attacker as it struck
        var sawTarget = Seen(target);
        var perceived = sawActor || sawTarget
                        || (inFight && kind is CombatLogKind.Started or CombatLogKind.TurnStarted or CombatLogKind.Initiative or CombatLogKind.Ended
                            or CombatLogKind.OpeningAttackPending or CombatLogKind.OpeningAttackCancelled);
        e.Log.Add(new CombatRecord
        {
            Round = e.Round, Kind = kind, Actor = actor?.Id, Target = target?.Id, Attack = attack,
            D20 = d20 ?? (roll is null ? Array.Empty<int>() : roll.Second is { } second ? new[] { roll.First, second } : new[] { roll.First }),
            Kept = kept ?? roll?.Kept, Bonus = roll?.Bonus, Total = roll?.Total, ArmorClass = armorClass, Hit = hit, Critical = critical,
            Damage = damage, Path = path?.ToArray() ?? Array.Empty<GridPos>(), Spots = spots?.ToArray() ?? Array.Empty<Spot>(), ShotFrom = shotFrom, ShotTo = shotTo, Cover = cover,
            Text = text(actor?.Name ?? "", target?.Name ?? ""),
            PlayerText = perceived ? (playerText ?? text)(sawActor ? actor?.Name ?? "" : "Qualcuno", sawTarget ? target?.Name ?? "" : "qualcuno") : null,
            PlayerSawActor = sawActor, PlayerSawTarget = sawTarget,
        });
    }
}

// C2: the fight as the player knows it.
internal sealed partial class Simulation
{
    internal CombatView? CombatViewOf(Actor player)
    {
        if (EncounterOf(player) is not { } e)
            return null;
        // Allies are known by name (one knows who fights on its side); where anyone stands, only if the player sees it.
        bool Known(Actor x) => x.Id == player.Id || Allies(e, player, x) || SeesActor(player, x);
        var me = CombatantOf(e, player.Id);
        var yourTurn = IsTurnOf(e, player);
        var current = e.RoundClosed ? null : World.Actors[e.Order[e.Turn].Actor];
        var enemies = EnemiesOf(e, player).Where(x => SeesActor(player, x) && x.Vitality.State != LifeState.Dead).ToList();
        return new CombatView
        {
            Round = e.Round,
            Turn = current is not null && Known(current) ? current.Id : null,
            IsYourTurn = yourTurn,
            NextRoundAt = e.RoundClosed ? e.RoundStart.Plus(Duration.FromSeconds(6)) : null,
            Order = e.Order.Select((c, i) =>
            {
                var who = World.Actors[c.Actor];
                var known = Known(who);
                return new InitiativeView
                {
                    Actor = known ? who.Id : null, Name = known ? who.Name : "Qualcuno", Total = who.IsPlayer ? c.Initiative : null, // C5a
                    Current = !e.RoundClosed && i == e.Turn,
                };
            }).ToArray(),
            You = new CombatSelfView
            {
                HitPoints = player.Vitality.Current, MaxHitPoints = player.Vitality.Maximum, State = player.Vitality.State,
                KnockedOut = player.Vitality.KnockedOut, DeathSaveSuccesses = player.Vitality.DeathSaveSuccesses,
                DeathSaveFailures = player.Vitality.DeathSaveFailures,
                MovementLeftFeet = Math.Max(0, MovementLeft(e, player)), MovementLeftInches = Math.Max(0, MovementLeftInches(e, player)), SquaresLeft = Math.Max(0, MovementLeft(e, player)) / FeetPerSquare,
                ActionAvailable = !me.ActionUsed, InteractionAvailable = !me.Interacted, BonusActionAvailable = !me.BonusActionUsed && HasBonusAction(player), AttacksLeft = me.AttacksLeft, SpellSlotSpent = me.SpellSlotSpent,
                OffHandAttackAvailable = ProfileOf(player).Attacks.Any(a => a.OffHand && OffHandRefused(e, player, a) is null), ReactionAvailable = !me.ReactionUsed, Disengaged = me.Disengaged, Dodging = me.Dodging,
            },
            Combatants = e.Order.Select(c => World.Actors[c.Actor]).Where(x => x.Id != player.Id && SeesActor(player, x))
                .Select(x => new CombatantView
                {
                    Id = x.Id, Name = x.Name, Position = CurrentPosition(x)!.Value,
                    Side = Allies(e, player, x) ? CombatSide.Ally : CombatSide.Enemy, Condition = x.Vitality.Describe(),
                    Dodging = CombatantOf(e, x.Id).Dodging,
                }).ToArray(),
            Reachable = yourTurn ? ReachableSquares(e, player) : Array.Empty<ReachableSquareView>(),
            Actions = Enum.GetValues<CombatAction>().Select(a => ActionOption(e, player, a)).ToArray(),
            Targets = enemies.SelectMany(x => ProfileOf(player).Attacks.Select(a =>
            {
                var check = CheckAttack(e, player, x, a, nonLethal: false);
                var (reason, why) = !yourTurn ? (RejectionReason.NotYourTurn, "Non è il tuo turno.")
                    : a.OffHand ? (OffHandRefused(e, player, a) is { } noExtra ? (RejectionReason.NoActionLeft, noExtra) : (check.Reason, check.Why)) // R4b
                    : me is { ActionUsed: true, AttacksLeft: 0 } ? (RejectionReason.NoActionLeft, "Hai già usato l'azione in questo turno.")
                    : (check.Reason, check.Why);
                return new TargetView
                {
                    Target = x.Id, Attack = a.Name, Valid = reason == RejectionReason.None, Reason = reason, Why = why,
                    Advantage = check.Advantage, Disadvantage = check.Disadvantage, LongRange = check.LongRange, AutoCritical = check.AutoCritical, Reasons = check.Reasons,
                    Cover = check.Cover, // P3
                };
            })).ToArray(),
            PendingOpeningAttack = e.Opening is { } o && (o.Attacker == player.Id || o.Target == player.Id)
                ? new PendingAttackView
                {
                    Attacker = Known(World.Actors[o.Attacker]) ? o.Attacker : null, Target = o.Target, Attack = o.Attack,
                }
                : null,
            Log = PublicLog(e.Log, player),
        };
    }

    /// <summary>P2b: the last fight the player was in, once over, while it is not fighting and still on that map.</summary>
    internal IReadOnlyList<CombatLogEntry>? LastFightOf(Actor player) =>
        EncounterOf(player) is null && World.LastFight is { } last && player.MapArea == last.Area ? PublicLog(last.Log, player) : null;

    /// <summary>A fight's log as the player perceived it.</summary>
    private static IReadOnlyList<CombatLogEntry> PublicLog(IEnumerable<CombatRecord> log, Actor player) =>
            log.Where(r => r.PlayerText is not null).Select(r => new CombatLogEntry
            {
                Round = r.Round, Kind = r.Kind, Actor = r.PlayerSawActor ? r.Actor : null, Target = r.PlayerSawTarget ? r.Target : null,
                // C5a: only the player's own rolls are shown; a target's AC never (it is learned, see Lore).
                Attack = r.Attack, D20 = r.Actor == player.Id ? r.D20 : Array.Empty<int>(), Kept = r.Actor == player.Id ? r.Kept : null,
                Bonus = r.Actor == player.Id ? r.Bonus : null, Total = r.Actor == player.Id ? r.Total : null, ArmorClass = null, Hit = r.Hit,
                Critical = r.Critical, Damage = r.Actor == player.Id || r.Target == player.Id ? r.Damage : null,
                Path = r.PlayerSawActor ? r.Path : Array.Empty<GridPos>(),
                // Review R2: a push moves the target, so its points are shown only if the player saw the target.
                Spots = (r.Kind == CombatLogKind.Mastery ? r.PlayerSawTarget : r.PlayerSawActor) ? r.Spots : Array.Empty<Spot>(),
                // P2b: the shot's origin only if the player saw who shot, its impact only if it saw the target.
                ShotFrom = r.PlayerSawActor ? r.ShotFrom : null, ShotTo = r.PlayerSawTarget ? r.ShotTo : null,
                Cover = r.PlayerSawTarget ? r.Cover : CoverDegree.None, Text = r.PlayerText!, // P3: the cover only if the target was seen
            }).ToArray();
}

// C3a: Dash, Disengage, Dodge (SRD 5.2.1 Actions).
internal sealed partial class Simulation
{
    private CommandResult TakeAction(Encounter e, Actor actor, CombatActionCommand command)
    {
        if (ActionOption(e, actor, command.Action) is { Valid: false } refused)
            return CommandResult.Rejected(refused.Reason, refused.Why!);
        if (command.Action == CombatAction.Escape)
            return Escape(e, actor); // R4a
        Act(e, actor, command.Action);
        return new CommandResult { Message = $"{actor.Name} {Verb(command.Action)}." };
    }

    /// <summary>Whether an action other than attacking can be taken now: on one's own turn, with the action unused.</summary>
    private ActionOptionView ActionOption(Encounter e, Actor actor, CombatAction action)
    {
        var (reason, why) = !Enum.IsDefined(action) ? (RejectionReason.UnknownCommand, "Azione sconosciuta.")
            : action > CombatAction.Escape ? (RejectionReason.UnknownCommand, "Non ancora disponibile.") // R4: until its rules are in
            : action == CombatAction.Escape && !Has(actor, ConditionKind.Grappled) ? (RejectionReason.NotHeld, "Non sei afferrato.")
            : !IsTurnOf(e, actor) ? (RejectionReason.NotYourTurn, "Non è il tuo turno.")
            : CombatantOf(e, actor.Id).ActionUsed ? (RejectionReason.NoActionLeft, "Hai già usato l'azione in questo turno.")
            : (RejectionReason.None, (string?)null);
        return new ActionOptionView { Action = action, Valid = reason == RejectionReason.None, Reason = reason, Why = why };
    }

    /// <summary>Uses the action for a Dash, a Disengage or a Dodge, and records it.</summary>
    private void Act(Encounter e, Actor actor, CombatAction action)
    {
        var c = CombatantOf(e, actor.Id);
        c.ActionUsed = true;
        switch (action)
        {
            case CombatAction.Dash: c.Dashes++; break;
            case CombatAction.Disengage: c.Disengaged = true; break;
            case CombatAction.Dodge: c.Dodging = true; break;
        }
        var kind = action switch { CombatAction.Dash => CombatLogKind.Dash, CombatAction.Disengage => CombatLogKind.Disengage, _ => CombatLogKind.Dodge };
        Record(e, kind, actor, null, (a, _) => $"{a} {Verb(action)}.");
    }

    private static string Verb(CombatAction action) => action switch
    {
        CombatAction.Dash => "scatta: il movimento raddoppia per questo turno",
        CombatAction.Disengage => "si disimpegna: in questo turno muoversi non provoca attacchi di opportunità",
        _ => "si mette in guardia: fino al suo prossimo turno chi lo attacca, se lo vede arrivare, ha svantaggio",
    };
}

// C3b: NPCs and the items their attacks use.
internal sealed partial class Simulation
{
    /// <summary>An NPC does not throw its last weapon of a kind while it has a melee attack to fight with (a Guard keeps its Spear).</summary>
    private static bool KeepsLastWeapon(Actor npc, AttackProfile attack) =>
        attack.Fate == ItemFate.Thrown && npc.Carried(attack.Item!) <= 1 && ProfileOf(npc).Attacks.Any(a => a.Melee && a.Requires == attack.Item);
}

// D1a: equipment.
internal sealed partial class Simulation
{
    /// <summary>When an actor no longer carries an item it wore or wielded, the slot is empty and its profile is derived again.</summary>
    private void Unequip(Actor actor, string item)
    {
        if (actor.Carried(item) > 0 || !actor.Equipment.Items.Contains(item))
            return;
        var e = actor.Equipment;
        actor.Equipment = new Equipment(e.Armor == item ? null : e.Armor, e.Shield == item ? null : e.Shield, e.MainHand == item ? null : e.MainHand,
            e.OffHand == item ? null : e.OffHand);
        World.Refresh(actor);
    }
}
