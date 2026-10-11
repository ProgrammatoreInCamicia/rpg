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
        var actor = World.Actors[command.Actor];
        if (!World.Actors.TryGetValue(command.Target, out var target))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Bersaglio sconosciuto: {command.Target}.");
        if (ProfileOf(actor).Attacks.FirstOrDefault(a => a.Name == command.Attack) is not { } attack)
            return CommandResult.Rejected(RejectionReason.UnknownAttack, $"{actor.Name} non ha l'attacco «{command.Attack}».");

        if (EncounterOf(actor) is not { } e)
            return OpenFight(actor, target, attack, command.NonLethal);

        if (!IsTurnOf(e, actor))
            return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
        var me = CombatantOf(e, actor.Id);
        if (me.ActionUsed)
            return CommandResult.Rejected(RejectionReason.NoActionLeft, "Hai già usato l'azione in questo turno.");
        if (EncounterOf(target) != e)
            return CommandResult.Rejected(RejectionReason.InCombat, $"{target.Name} non fa parte di questo scontro.");
        var check = CheckAttack(e, actor, target, attack, command.NonLethal);
        if (!check.Valid)
            return CommandResult.Rejected(check.Reason, check.Why!);
        me.ActionUsed = true;
        var text = ResolveAttack(e, actor, target, attack, command.NonLethal, CombatLogKind.Attack);
        CheckEnd(e);
        return new CommandResult { Message = text };
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

        var expects = SeesActor(target, actor) && HoldsHostile(target, actor);
        CancelAction(actor);
        CancelAction(target);
        actor.FoughtWith.Add(target.Id);
        target.FoughtWith.Add(actor.Id);

        // SRD Initiative: a Dexterity check each; the surprised one rolls with Disadvantage.
        var rolls = new[]
        {
            Initiative.Roll(World.Rng, actor.Id.Value, ProfileOf(actor).InitiativeBonus),
            Initiative.Roll(World.Rng, target.Id.Value, ProfileOf(target).InitiativeBonus, surprised: !expects),
        };
        var e = new Encounter { Id = World.NextEncounterId++, Area = actor.MapArea!.Value, RoundStart = World.Now };
        foreach (var roll in Initiative.Order(rolls))
            e.Order.Add(new Combatant
            {
                Actor = new ActorId(roll.Id), Side = roll.Id == actor.Id.Value ? 0 : 1,
                Initiative = roll.Roll.Total, InitiativeBonus = roll.Bonus, OpeningSpent = !expects && roll.Id == actor.Id.Value,
            });
        World.Encounters.Add(e.Id, e);
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
        if (PlanCombatMove(e, actor, command.To, command.StopNextTo) is not { } plan)
            return CommandResult.Rejected(RejectionReason.Unreachable, "Non si può raggiungere quel punto.");
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
        if (PlanCombatMove(e, actor, to, stopNextTo: false) is not { } plan)
            return new MovePreview { Valid = false, Reason = RejectionReason.Unreachable, Message = "Non si può raggiungere quel punto." };
        var left = MovementLeft(e, actor);
        var (reason, message) = plan.Path.Count == 0 ? (RejectionReason.AlreadyThere, "Sei già lì.")
            : plan.Feet > left ? (RejectionReason.TooFar, $"Troppo lontano: servono {plan.Feet} piedi, te ne restano {left}.")
            : (RejectionReason.None, $"{plan.Feet} piedi di movimento.");

        // Opportunity attacks along the way, by the threats the player knows of (each has one Reaction).
        var player = World.Actors[World.Player];
        var provokes = new List<ProvocationView>();
        var used = e.Order.Where(c => c.ReactionUsed).Select(c => c.Actor).ToHashSet();
        var from = CurrentPosition(actor)!.Value;
        for (var i = 0; i < plan.Path.Count; i++)
        {
            foreach (var foe in Threats(e, actor, from, plan.Path[i]).Where(f => !used.Contains(f.Id) && SeesActor(player, f)))
            {
                used.Add(foe.Id);
                provokes.Add(new ProvocationView { Step = i, By = foe.Id, Name = foe.Name });
            }
            from = plan.Path[i];
        }
        return new MovePreview
        {
            Valid = reason == RejectionReason.None, Reason = reason, Message = message,
            Path = plan.Path, CostFeet = plan.Cost, TotalFeet = plan.Feet, Provokes = provokes,
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
                if (!World.Encounters.ContainsKey(e.Id))
                    return;
            }
            e.Turn++;
        }
    }

    /// <summary>The start of a turn: movement and Reaction back, a Death Saving Throw if dying, the opening attack due.</summary>
    private bool BeginTurn(Encounter e, Combatant c, Actor actor)
    {
        c.MovedFeet = 0;
        c.ReactionUsed = false; // SRD: a Reaction comes back at the start of one's own turn
        c.Dashes = 0;
        c.Disengaged = false;
        c.Dodging = false; // C3a: a Dodge lasts until the start of one's next turn
        c.Interacted = false; // C3b
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
        var outcome = actor.Vitality.RollDeathSave(World.Rng, out var rolled);
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

    private bool AnyoneSeesAnEnemy(Encounter e) =>
        e.Order.Select(c => World.Actors[c.Actor]).Any(a => a.Conscious && EnemiesOf(e, a).Any(foe => SeesActor(a, foe)));

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
        Record(e, CombatLogKind.Ended, null, null, (_, _) => why);
        World.RecordFact("CombatEnded", why, actors.Where(a => a.IsPlayer || actors.Any(o => o.IsPlayer && SeesActor(o, a)))
            .Select(a => a.Id).ToArray());
        World.Scheduler.RemoveRound(e.Id);
        World.Encounters.Remove(e.Id);
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
        bool LongRange = false, bool AutoCritical = false)
    {
        public bool Valid => Reason == RejectionReason.None;
    }

    /// <summary>
    /// Whether an attack can be made now, and how (SRD): melee within reach; ranged in sight, with Disadvantage beyond
    /// the normal range or with a conscious enemy next to the attacker that sees it; Advantage against a target that
    /// does not see the attacker or is unconscious; every hit critical on an unconscious target within 5 feet. In C2
    /// only a target in sight can be attacked.
    /// </summary>
    private AttackCheck CheckAttack(Encounter? e, Actor attacker, Actor target, AttackProfile attack, bool nonLethal)
    {
        if (target.Id == attacker.Id || (e is not null && Allies(e, attacker, target)))
            return new(RejectionReason.NotHostile, $"{target.Name} è dalla tua parte.");
        if (target.Vitality.State == LifeState.Dead)
            return new(RejectionReason.AlreadyDone, $"{target.Name} è già morto.");
        if (!SeesActor(attacker, target))
            return new(RejectionReason.NotVisible, $"Non vedi {target.Name}.");
        if (attack.Item is { } needed && attacker.Carried(needed) == 0) // C3b
            return new(RejectionReason.NoAmmo, $"Non ne hai più: {World.Content.Item(needed)?.Name ?? needed}.");
        if (attack.Requires is { } weapon && attacker.Carried(weapon) == 0) // D1a: no weapon in hand, no attack with it
            return new(RejectionReason.NoWeapon, $"Non hai più: {World.Content.Item(weapon)?.Name ?? weapon}.");
        var map = World.Maps[attacker.MapArea!.Value];
        var at = CurrentPosition(attacker)!.Value;
        var there = CurrentPosition(target)!.Value;
        var feet = at.StepsTo(there) * FeetPerSquare;
        bool disadvantage = false, longRange = false;
        if (attack.Melee)
        {
            if (!InReach(map, at, attack, there))
                return new(RejectionReason.TooFar, $"{target.Name} è fuori portata: avvicinati.");
        }
        else
        {
            if (nonLethal)
                return new(RejectionReason.UnknownAttack, "Si può tramortire solo con un attacco in mischia.");
            var (normal, longest) = attack.Range!.Value;
            if (feet > longest)
                return new(RejectionReason.TooFar, $"{target.Name} è oltre la gittata ({longest} piedi).");
            longRange = feet > normal;
            var threats = e is null ? new[] { target } : EnemiesOf(e, attacker);
            var closeCombat = threats.Any(foe => foe.Conscious && CurrentPosition(foe) is { } p && p.StepsTo(at) == 1
                                                 && OpenDiagonal(map, p, at) && SeesActor(foe, attacker));
            disadvantage = longRange || closeCombat;
        }
        // C3a, SRD Dodge: attacks against a dodging target that sees the attacker have Disadvantage.
        if (e?.Order.FirstOrDefault(c => c.Actor == target.Id) is { Dodging: true } && target.Conscious && SeesActor(target, attacker))
            disadvantage = true;
        return new(RejectionReason.None, null,
            Advantage: !SeesActor(target, attacker), // also an unconscious target: it sees nothing
            Disadvantage: disadvantage, LongRange: longRange,
            AutoCritical: target.Vitality.Unconscious && feet <= FeetPerSquare);
    }

    /// <summary>A melee attack from <paramref name="from"/> reaches <paramref name="square"/> (no squeezing past a corner).</summary>
    private static bool InReach(GridMap map, GridPos from, AttackProfile attack, GridPos square)
    {
        var steps = from.StepsTo(square);
        return steps >= 1 && steps * FeetPerSquare <= attack.ReachFeet && map.HasLineOfSight(from, square)
               && (steps > 1 || OpenDiagonal(map, from, square));
    }

    /// <summary>Rolls the attack and applies the damage. Returns the line for the dice log.</summary>
    private string ResolveAttack(Encounter e, Actor attacker, Actor target, AttackProfile attack, bool nonLethal, CombatLogKind kind)
    {
        var check = CheckAttack(e, attacker, target, attack, nonLethal);
        var result = Attacks.Roll(World.Rng, attack, ProfileOf(target).ArmorClass, check.Advantage, check.Disadvantage, check.AutoCritical);
        var outcome = target.Vitality.TakeDamage(result.Damage, result.Critical, knockOut: nonLethal && attack.Melee);
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
        var extra = (check.Advantage ? " (vantaggio)" : "") + (check.Disadvantage ? " (svantaggio)" : "") + (check.LongRange ? " (gittata lunga)" : "");
        // C5a: the player reads it from its side of the GM screen.
        var seen = result.DescribeFor(attacker.IsPlayer ? AttackPerspective.Attacker : target.IsPlayer ? AttackPerspective.Target : AttackPerspective.Witness);
        Record(e, kind, attacker, target, (a, t) => $"{a} {how} {t}{extra}. {result.Describe()}.", result.Roll,
            attack: attack.Name, armorClass: result.TargetArmorClass, hit: result.Hit, critical: result.Critical, damage: result.Damage,
            playerText: (a, t) => $"{a} {how} {t}{extra}. {seen}.");
        var blow = World.RecordFact("Attack", $"{attacker.Name} {how} {target.Name}: {result.Describe()}.",
            $"{attacker.Name} {how} {target.Name}: {seen}.", Participants(attacker, target));
        WitnessFight(e, attacker, target, opening: false, blow);
        Fell(e, target, attacker, outcome);
        return $"{attacker.Name} {how} {target.Name}{extra}. {seen}.";
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

    private sealed record CombatPlan(IReadOnlyList<GridPos> Path, IReadOnlyList<int> Cost, int Feet);

    private int MovementLeft(Encounter e, Actor actor) =>
        ProfileOf(actor).Speed * (1 + CombatantOf(e, actor.Id).Dashes) - CombatantOf(e, actor.Id).MovedFeet; // C3a: Dash adds Speed

    /// <summary>
    /// The creatures a mover takes into account when planning: those it sees, allies included (there is no memory of
    /// where an unseen ally stands). Nobody plans around what it cannot perceive, so the client's green squares never
    /// reveal a hidden creature; the real move then deals with the creatures really there.
    /// </summary>
    private Func<Actor, bool> Perceived(Encounter e, Actor mover) => a => SeesActor(mover, a);

    /// <summary>
    /// The cheapest way to <paramref name="to"/> (or, with <paramref name="stopNextTo"/>, to a square next to it) by the
    /// SRD rules, around the creatures the mover perceives: 5 feet a square, diagonals included, no cutting corners;
    /// closed doors block (opening them is C3); an ally's space can be crossed, an incapacitated creature's space is
    /// Difficult Terrain, any other creature blocks; nobody ends on another creature. Null when there is no way. The
    /// movement left is not checked here.
    /// </summary>
    private CombatPlan? PlanCombatMove(Encounter e, Actor mover, GridPos to, bool stopNextTo)
    {
        var map = World.Maps[e.Area];
        var from = CurrentPosition(mover)!.Value;
        var here = new CombatPlan(Array.Empty<GridPos>(), Array.Empty<int>(), 0);
        if (stopNextTo ? from.StepsTo(to) == 1 && OpenDiagonal(map, from, to) : from == to)
            return here; // already there
        var knows = Perceived(e, mover);
        var occupants = OccupantsOf(map, mover, knows);
        var reach = CombatReach(e, mover, map, from, to, knows);
        var ends = stopNextTo
            ? reach.Keys.Where(p => p.StepsTo(to) == 1 && OpenDiagonal(map, p, to))
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
        GridPos aim, Func<Actor, bool> knows, int? maxFeet = null)
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
                if (!map.IsWalkable(next) || map.IsClosedDoor(next) || !map.DiagonalOpen(here, next))
                    continue;
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
        return CombatReach(e, actor, map, from, from, knows, left)
            .Where(r => !occupants.Contains(r.Key))
            .OrderBy(r => r.Key.Y).ThenBy(r => r.Key.X)
            .Select(r => new ReachableSquareView { At = r.Key, CostFeet = r.Value.Feet })
            .ToArray();
    }

    /// <summary>Enemies whose reach <paramref name="mover"/> leaves stepping from <paramref name="from"/> to <paramref name="to"/>, seeing it, with a Reaction to spend.</summary>
    private IEnumerable<Actor> Threats(Encounter e, Actor mover, GridPos from, GridPos to)
    {
        if (CombatantOf(e, mover.Id).Disengaged)
            yield break; // C3a: Disengage
        var map = World.Maps[e.Area];
        foreach (var c in e.Order)
        {
            var foe = World.Actors[c.Actor];
            if (foe.Id == mover.Id || Allies(e, foe, mover) || !foe.Conscious || c.ReactionUsed
                || MeleeAttack(foe) is not { } attack || CurrentPosition(foe) is not { } at)
                continue;
            if (InReach(map, at, attack, from) && !InReach(map, at, attack, to) && Sees(map, foe, at, mover, from, LightOn(map, from)))
                yield return foe;
        }
    }

    private static AttackProfile? MeleeAttack(Actor actor) => ProfileOf(actor).Attacks.FirstOrDefault(a => a.Melee);

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
        var budget = ProfileOf(actor).Speed * (1 + me.Dashes); // C3a: a Dash counts
        var everyone = CreaturesOn(map, actor, _ => true);
        actor.Sneak = null;
        var start = CurrentPosition(actor)!.Value;
        var walked = new List<GridPos>();
        var from = start;
        string? stopped = null;
        foreach (var square in plan.Path)
        {
            var (feet, blocked) = EnterCost(e, actor, everyone[square]);
            if (blocked)
            {
                stopped = "qualcuno gli sbarra la strada";
                break;
            }
            if (me.MovedFeet + feet > budget)
            {
                stopped = "il passo costa più del previsto";
                break;
            }
            foreach (var foe in Threats(e, actor, from, square).ToList())
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
            me.MovedFeet += feet;
            Place(actor, square);
            walked.Add(square);
            from = square;
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
        EvaluateContacts();
        var why = back > 0 ? $"c'è qualcuno, torna indietro di {back} {(back == 1 ? "casella" : "caselle")}"
            : stopped;
        if (trail.Count > 0 || why is not null)
            Record(e, CombatLogKind.Move, actor, null,
                (a, _) => $"{a} si sposta di {walked.Count} caselle{(why is null ? "" : $" e si ferma: {why}")}.", path: trail);
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
            .OrderBy(f => CurrentPosition(npc)!.Value.StepsTo(CurrentPosition(f)!.Value)).ThenBy(f => f.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();

        AttackProfile? Usable(Actor target) =>
            ProfileOf(npc).Attacks.Where(a => a.Melee).Concat(ProfileOf(npc).Attacks.Where(a => !a.Melee))
                .FirstOrDefault(a => CheckAttack(e, npc, target, a, nonLethal: false).Valid && !KeepsLastWeapon(npc, a));

        bool TryAttack(Actor target)
        {
            if (c.ActionUsed || Usable(target) is not { } attack)
                return false;
            c.ActionUsed = true;
            ResolveAttack(e, npc, target, attack, nonLethal: attack.Melee && Spares(npc, target), CombatLogKind.Attack);
            npc.LastDecision = new DecisionTrace(World.Now, "Scontro: attacca", $"Attacca {target.Name} con {attack.Name}.", Array.Empty<string>());
            CheckEnd(e);
            return true;
        }

        var target = Target();
        if (target is not null)
        {
            c.LastSeenEnemy = CurrentPosition(target);
            if (TryAttack(target) || !World.Encounters.ContainsKey(e.Id))
                return;
        }
        var aim = target is not null ? CurrentPosition(target) : c.LastSeenEnemy;
        if (aim is not { } spot || spot == CurrentPosition(npc))
        {
            npc.LastDecision = new DecisionTrace(World.Now, "Scontro: aspetta", "Nessun avversario in vista.", Array.Empty<string>());
            return;
        }
        var occupied = OccupantsOf(World.Maps[e.Area], npc, Perceived(e, npc)).Contains(spot);
        var way = PlanCombatMove(e, npc, spot, stopNextTo: occupied);
        // C3a: out of reach this turn, and nothing better to do with the action: Dash to close in.
        if (way is not null && way.Feet > MovementLeft(e, npc) && !c.ActionUsed)
            Act(e, npc, CombatAction.Dash);
        if (way is { } plan && Truncate(e, npc, plan) is { } step)
        {
            npc.LastDecision = new DecisionTrace(World.Now, "Scontro: si avvicina",
                target is not null ? $"Va verso {target.Name}." : "Va dove ha visto l'avversario l'ultima volta.", Array.Empty<string>());
            WalkInCombat(e, npc, step);
            if (CheckEnd(e) || !npc.Conscious)
                return;
        }
        if (Target() is { } now)
        {
            c.LastSeenEnemy = CurrentPosition(now);
            TryAttack(now);
        }
    }

    /// <summary>The longest part of a plan the NPC can walk this turn, ending on a free square (null if not even one step).</summary>
    private CombatPlan? Truncate(Encounter e, Actor npc, CombatPlan plan)
    {
        var left = MovementLeft(e, npc);
        var occupants = OccupantsOf(World.Maps[e.Area], npc, Perceived(e, npc));
        for (var n = plan.Path.Count; n > 0; n--)
            if (plan.Cost[n - 1] <= left && !occupants.Contains(plan.Path[n - 1]))
                return new CombatPlan(plan.Path.Take(n).ToArray(), plan.Cost.Take(n).ToArray(), plan.Cost[n - 1]);
        return null;
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
        Func<string, string, string>? playerText = null)
    {
        var player = World.Actors[World.Player];
        bool Seen(Actor? x) => x is not null && (x.IsPlayer || SeesActor(player, x));
        var inFight = e.Order.Any(c => c.Actor == player.Id);
        var sawActor = Seen(actor);
        var sawTarget = Seen(target);
        var perceived = sawActor || sawTarget
                        || (inFight && kind is CombatLogKind.Started or CombatLogKind.TurnStarted or CombatLogKind.Initiative or CombatLogKind.Ended
                            or CombatLogKind.OpeningAttackPending or CombatLogKind.OpeningAttackCancelled);
        e.Log.Add(new CombatRecord
        {
            Round = e.Round, Kind = kind, Actor = actor?.Id, Target = target?.Id, Attack = attack,
            D20 = d20 ?? (roll is null ? Array.Empty<int>() : roll.Second is { } second ? new[] { roll.First, second } : new[] { roll.First }),
            Kept = kept ?? roll?.Kept, Bonus = roll?.Bonus, Total = roll?.Total, ArmorClass = armorClass, Hit = hit, Critical = critical,
            Damage = damage, Path = path?.ToArray() ?? Array.Empty<GridPos>(),
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
                MovementLeftFeet = Math.Max(0, MovementLeft(e, player)), SquaresLeft = Math.Max(0, MovementLeft(e, player)) / FeetPerSquare,
                ActionAvailable = !me.ActionUsed, ReactionAvailable = !me.ReactionUsed, Disengaged = me.Disengaged, Dodging = me.Dodging,
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
                    : me.ActionUsed ? (RejectionReason.NoActionLeft, "Hai già usato l'azione in questo turno.")
                    : (check.Reason, check.Why);
                return new TargetView
                {
                    Target = x.Id, Attack = a.Name, Valid = reason == RejectionReason.None, Reason = reason, Why = why,
                    Advantage = check.Advantage, Disadvantage = check.Disadvantage, LongRange = check.LongRange, AutoCritical = check.AutoCritical,
                };
            })).ToArray(),
            PendingOpeningAttack = e.Opening is { } o && (o.Attacker == player.Id || o.Target == player.Id)
                ? new PendingAttackView
                {
                    Attacker = Known(World.Actors[o.Attacker]) ? o.Attacker : null, Target = o.Target, Attack = o.Attack,
                }
                : null,
            Log = e.Log.Where(r => r.PlayerText is not null).Select(r => new CombatLogEntry
            {
                Round = r.Round, Kind = r.Kind, Actor = r.PlayerSawActor ? r.Actor : null, Target = r.PlayerSawTarget ? r.Target : null,
                // C5a: only the player's own rolls are shown; a target's AC never (it is learned, see Lore).
                Attack = r.Attack, D20 = r.Actor == player.Id ? r.D20 : Array.Empty<int>(), Kept = r.Actor == player.Id ? r.Kept : null,
                Bonus = r.Actor == player.Id ? r.Bonus : null, Total = r.Actor == player.Id ? r.Total : null, ArmorClass = null, Hit = r.Hit,
                Critical = r.Critical, Damage = r.Actor == player.Id || r.Target == player.Id ? r.Damage : null,
                Path = r.PlayerSawActor ? r.Path : Array.Empty<GridPos>(), Text = r.PlayerText!,
            }).ToArray(),
        };
    }
}

// C3a: Dash, Disengage, Dodge (SRD 5.2.1 Actions).
internal sealed partial class Simulation
{
    private CommandResult TakeAction(Encounter e, Actor actor, CombatActionCommand command)
    {
        if (ActionOption(e, actor, command.Action) is { Valid: false } refused)
            return CommandResult.Rejected(refused.Reason, refused.Why!);
        Act(e, actor, command.Action);
        return new CommandResult { Message = $"{actor.Name} {Verb(command.Action)}." };
    }

    /// <summary>Whether an action other than attacking can be taken now: on one's own turn, with the action unused.</summary>
    private ActionOptionView ActionOption(Encounter e, Actor actor, CombatAction action)
    {
        var (reason, why) = !Enum.IsDefined(action) ? (RejectionReason.UnknownCommand, "Azione sconosciuta.")
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
        actor.Equipment = new Equipment(e.Armor == item ? null : e.Armor, e.Shield == item ? null : e.Shield, e.MainHand == item ? null : e.MainHand);
        World.Refresh(actor);
    }
}
