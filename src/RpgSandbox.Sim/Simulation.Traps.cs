using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim;

// R5c: falls, traps and Basic Poison (SRD 5.2.1 Rules Glossary: Falling; Gameplay Toolbox: Pit, Poisoned Needle;
// Equipment: Basic Poison, Iron Spikes).
internal sealed partial class Simulation
{
    private const int TrapDc = 15;
    private const int NeedleSaveDc = 11;
    private const int PitFeet = 10;
    private const string IronSpike = "iron-spike";
    private const string BasicPoison = "basic-poison";
    private static readonly Duration TrapWorkTime = Duration.FromMinutes(1);
    private static readonly Duration PoisonLasts = Duration.FromMinutes(1);

    /// <summary>SRD Falling: 1d6 Bludgeoning damage for every 10 feet, at most 20d6; Prone on landing unless it took no damage.</summary>
    private void Fall(Actor actor, int feet)
    {
        var dice = Enumerable.Range(0, Math.Min(feet / 10, 20)).Select(_ => World.Rng.Roll(6)).ToArray();
        var damage = dice.Sum();
        World.RecordFact("Fell", $"{actor.Name} cade per {feet} piedi: [{string.Join(", ", dice)}] = {damage} danni contundenti.", actor.Id);
        Hazard(actor, damage, "si ferisce cadendo");
        if (damage > 0 && actor.Conscious)
            AddCondition(EncounterOf(actor), actor, ConditionKind.Prone);
    }

    /// <summary>Damage from no one's blow (a fall, a needle): in a fight it may end it; outside, one downed starts dying.</summary>
    private void Hazard(Actor actor, int damage, string why)
    {
        if (damage <= 0)
            return;
        var outcome = actor.Vitality.TakeDamage(damage, critical: false);
        Harmed(actor, why, damage);
        if (actor.Conscious)
            return;
        if (EncounterOf(actor) is { } e)
        {
            Fell(e, actor, null, outcome);
            CheckEnd(e);
        }
        else
            BeginDowned(actor);
    }

    /// <summary>
    /// SRD Pit: moving onto its lid, it swings open and the creature falls 10 feet; the lid stays open. Walking into an
    /// open pit is a fall too. Returns whether it fell.
    /// </summary>
    private bool StepOnPit(Actor actor)
    {
        if (actor.InPit || MapOf(actor) is not { } map || CurrentPosition(actor) is not { } here || !map.PitAt(here))
            return false;
        var trap = map.Traps[here];
        var hidden = trap.Live;
        trap.Sprung = true;
        trap.Found = true;
        actor.InPit = true;
        World.RecordFact("PitTrap", hidden
            ? $"Il pavimento cede sotto {actor.Name}: una botola si apre su una fossa."
            : $"{actor.Name} cade nella fossa aperta.", actor.Id);
        Fall(actor, PitFeet);
        return true;
    }

    /// <summary>SRD Poisoned Needle: DC 11 Constitution save; failed, 1d10 Poison damage and Poisoned for 1 hour; succeeded, half.</summary>
    private void Sting(Actor actor, Trap trap)
    {
        trap.Sprung = true;
        trap.Found = true;
        var roll = World.Rng.Roll(10);
        World.RecordFact("NeedleTrap", $"Un ago scatta dalla serratura e punge {actor.Name}.", actor.Id);
        var saved = SavingThrow(EncounterOf(actor), actor, Ability.Constitution, NeedleSaveDc, "il veleno dell'ago");
        var damage = saved ? roll / 2 : roll;
        World.RecordFact("PoisonDamage", $"{actor.Name} subisce {damage} danni da veleno.", actor.Id);
        Hazard(actor, damage, "è avvelenato");
        if (!saved && actor.Vitality.State != LifeState.Dead)
            AddCondition(EncounterOf(actor), actor, ConditionKind.Poisoned, until: World.Now.Plus(Duration.FromHours(1)));
    }

    /// <summary>R5c: the traps the player knows of on its map, found or sprung.</summary>
    internal IReadOnlyList<TrapView> KnownTrapsOf(Actor actor) => MapOf(actor) is not { } map ? Array.Empty<TrapView>()
        : map.Traps.Where(t => t.Value.Found || t.Value.Sprung)
            .Select(t => new TrapView { At = t.Key, Kind = t.Value.Kind, Sprung = t.Value.Sprung, Disarmed = t.Value.Disarmed }).ToList();

    /// <summary>The live needle in the lock of this door, if there is one.</summary>
    private static Trap? NeedleIn(GridMap map, BarrierKey key) =>
        key.IsDoor && map.Traps.TryGetValue(key.A, out var t) && t.Kind == TrapKind.Needle && t.Live ? t : null;

    /// <summary>The pits a walker knows to go round: open ones; for the player, the ones it found too. Not the one it is in.</summary>
    private IEnumerable<GridPos> KnownPits(Actor actor, GridMap map) =>
        map.Traps.Where(t => t.Value.Kind == TrapKind.Pit && !t.Value.Disarmed && (t.Value.Sprung || actor.IsPlayer && t.Value.Found)
                             && t.Key != CurrentPosition(actor))
            .Select(t => t.Key);

    private CommandResult StartTrapWork(Command command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (actor.CurrentAction is not null and not WaitAction)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        if (MapOf(actor) is not { } map || CurrentPosition(actor) is not { } here)
            return CommandResult.Rejected(RejectionReason.NoTrap, "Qui non c'è nulla del genere.");
        var (work, at) = command switch
        {
            SearchCommand => (TrapWork.Search, (GridPos?)null),
            DisarmCommand d => (TrapWork.Disarm, d.At),
            _ => (TrapWork.ClimbOut, (GridPos?)null),
        };
        if (work == TrapWork.ClimbOut != actor.InPit)
            return actor.InPit
                ? CommandResult.Rejected(RejectionReason.InPit, "Sei in fondo a una fossa: prima risali.")
                : CommandResult.Rejected(RejectionReason.NotInPit, "Non sei in una fossa.");
        if (work == TrapWork.Disarm && DisarmRefused(actor, map, here, at!.Value) is { } refused)
            return refused;
        var action = new TrapWorkAction
        {
            Id = World.AllocateActionId(), Actor = actor.Id, StartedAt = World.Now, CompletesAt = World.Now.Plus(TrapWorkTime),
            Work = work, At = at,
            Description = work switch
            {
                TrapWork.Search => "Cerca trappole",
                TrapWork.Disarm => "Disinnesca la trappola",
                _ => "Prova a risalire dalla fossa",
            },
        };
        if (actor.CurrentAction is WaitAction)
            CancelAction(actor);
        Begin(actor, action);
        return CommandResult.Started(action.Id, action.CompletesAt, $"{actor.Name}: {action.Description.ToLowerInvariant()}.");
    }

    private CommandResult? DisarmRefused(Actor actor, GridMap map, GridPos here, GridPos at)
    {
        if (!map.Traps.TryGetValue(at, out var trap) || !trap.Live || actor.IsPlayer && !trap.Found)
            return CommandResult.Rejected(RejectionReason.NoTrap, "Non conosci nessuna trappola da disinnescare lì.");
        if (trap.Kind == TrapKind.Pit ? !here.IsAdjacentOrSame(at) || here == at : !DoorInReach(map, here, at))
            return CommandResult.Rejected(RejectionReason.TooFar, "Devi esserci accanto.");
        if (trap.Kind == TrapKind.Pit && actor.Carried(IronSpike) == 0)
            return CommandResult.Rejected(RejectionReason.NoTools, "Ti serve un chiodo di ferro da incastrare sotto la botola.");
        return null;
    }

    private void CompleteTrapWork(Actor actor, TrapWorkAction action)
    {
        if (MapOf(actor) is not { } map || CurrentPosition(actor) is not { } here)
            return;
        switch (action.Work)
        {
            case TrapWork.Search:
                Search(actor, map, here);
                break;
            case TrapWork.Disarm:
                if (DisarmRefused(actor, map, here, action.At!.Value) is { } refused)
                    World.RecordFact("DisarmFailed", $"{actor.Name} non può più disinnescare la trappola: {refused.Message}", actor.Id);
                else
                    Disarm(actor, map.Traps[action.At.Value]);
                break;
            default:
                ClimbOut(actor, map, here);
                break;
        }
    }

    /// <summary>SRD: a needle is found with Wisdom (Perception) DC 15, a pit's lid with Intelligence (Investigation) DC 15; within 10 feet.</summary>
    private void Search(Actor actor, GridMap map, GridPos here)
    {
        var found = 0;
        foreach (var (at, trap) in map.Traps.Where(t => t.Value.Live && !t.Value.Found && t.Key.StepsTo(here) * 5 <= 10).ToList())
        {
            var skill = trap.Kind == TrapKind.Pit ? Skill.Investigation : Skill.Perception;
            var roll = D20.Roll(World.Rng, actor.Sheet.Bonus(skill) + CheckEdges(actor).Modifier, disadvantage: CheckEdges(actor).Disadvantage);
            if (roll.Total < TrapDc)
                continue;
            trap.Found = true;
            found++;
            World.RecordFact("TrapFound", trap.Kind == TrapKind.Pit
                ? $"{actor.Name} nota le fessure di una botola nel pavimento in {at}: una fossa."
                : $"{actor.Name} nota un ago nascosto nella serratura della porta in {at}.", actor.Id);
        }
        if (found == 0)
            World.RecordFact("TrapNotFound", $"{actor.Name} cerca trappole: non trova nulla.", actor.Id);
    }

    /// <summary>SRD: an iron spike under the pit's lid; the needle with Dexterity (Sleight of Hand) DC 15, a failure springs it.</summary>
    private void Disarm(Actor actor, Trap trap)
    {
        if (trap.Kind == TrapKind.Pit)
        {
            actor.Give(IronSpike, -1);
            trap.Disarmed = true;
            World.RecordFact("TrapDisarmed", $"{actor.Name} incastra un chiodo di ferro sotto la botola: ora si passa.", actor.Id);
            return;
        }
        var roll = D20.Roll(World.Rng, actor.Sheet.Bonus(Skill.SleightOfHand) + CheckEdges(actor).Modifier, disadvantage: CheckEdges(actor).Disadvantage);
        if (roll.Total >= TrapDc)
        {
            trap.Disarmed = true;
            World.RecordFact("TrapDisarmed", $"{actor.Name} toglie l'ago dalla serratura: Rapidità di mano {roll.Describe()}.", actor.Id);
            return;
        }
        World.RecordFact("DisarmFailed", $"{actor.Name} sbaglia a disinnescare l'ago: Rapidità di mano {roll.Describe()}.", actor.Id);
        Sting(actor, trap);
    }

    /// <summary>R5c, ADAPTATION: Strength (Athletics) DC 15 out of a 10-foot pit, onto a free square beside it.</summary>
    private void ClimbOut(Actor actor, GridMap map, GridPos here)
    {
        var roll = D20.Roll(World.Rng, actor.Sheet.Bonus(Skill.Athletics) + CheckEdges(actor).Modifier, disadvantage: CheckEdges(actor).Disadvantage);
        var taken = World.Actors.Values.Where(o => o.Id != actor.Id && o.MapArea == map.Area && o.Vitality.State != LifeState.Dead)
            .Select(CurrentPosition).OfType<GridPos>().ToHashSet();
        GridPos? ledge = null;
        for (var dy = -1; dy <= 1 && ledge is null; dy++)
        for (var dx = -1; dx <= 1 && ledge is null; dx++)
        {
            var p = new GridPos(here.X + dx, here.Y + dy);
            if (p != here && map.IsWalkable(p) && !map.PitAt(p) && !taken.Contains(p) && map.StepOpen(here, p, sight: false))
                ledge = p;
        }
        if (roll.Total < TrapDc || ledge is null)
        {
            World.RecordFact("ClimbOutFailed", $"{actor.Name} non riesce a risalire dalla fossa: Atletica {roll.Describe()}.", actor.Id);
            return;
        }
        actor.InPit = false;
        Reach(actor, ledge.Value);
        World.RecordFact("ClimbedOut", $"{actor.Name} risale dalla fossa: Atletica {roll.Describe()}.", actor.Id);
    }

    /// <summary>R5c: in a fight, climbing out takes the action and is tried at once.</summary>
    private CommandResult CombatClimbOut(Encounter e, Actor actor)
    {
        if (!actor.InPit)
            return CommandResult.Rejected(RejectionReason.NotInPit, "Non sei in una fossa.");
        if (!IsTurnOf(e, actor))
            return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
        var me = CombatantOf(e, actor.Id);
        if (me.ActionUsed)
            return CommandResult.Rejected(RejectionReason.NoActionLeft, "Hai già usato l'azione in questo turno.");
        me.ActionUsed = true;
        ClimbOut(actor, World.Maps[e.Area], CurrentPosition(actor)!.Value);
        return new CommandResult { Message = World.RecentFacts.Last!.Value.Description };
    }

    /// <summary>SRD Basic Poison: a vial on the weapon in hand, a Bonus Action in a fight; potent for a minute.</summary>
    private CommandResult ApplyPoison(ApplyPoisonCommand command)
    {
        if (!World.Actors.TryGetValue(command.Actor, out var actor))
            return CommandResult.Rejected(RejectionReason.ActorNotFound, $"Attore sconosciuto: {command.Actor}.");
        if (actor.Carried(BasicPoison) == 0)
            return CommandResult.Rejected(RejectionReason.NoTools, "Non hai veleno.");
        if (actor.Equipment.MainHand is not { } weapon)
            return CommandResult.Rejected(RejectionReason.NoTools, "Non hai un'arma in mano da avvelenare.");
        if (EncounterOf(actor) is { } e)
        {
            if (!IsTurnOf(e, actor))
                return CommandResult.Rejected(RejectionReason.NotYourTurn, "Non è il tuo turno.");
            var me = CombatantOf(e, actor.Id);
            if (me.BonusActionUsed)
                return CommandResult.Rejected(RejectionReason.NoActionLeft, "Hai già usato l'azione bonus in questo turno.");
            me.BonusActionUsed = true;
        }
        else if (actor.CurrentAction is not null and not WaitAction)
            return CommandResult.Rejected(RejectionReason.ActorBusy, $"{actor.Name} è già impegnato.");
        actor.Give(BasicPoison, -1);
        actor.PoisonedWeapon = weapon;
        actor.PoisonedUntil = World.Now.Plus(PoisonLasts);
        var name = World.Content.Item(weapon)?.Name ?? weapon;
        World.RecordFact("WeaponPoisoned", $"{actor.Name} spalma il veleno su {name}.", actor.Id);
        return new CommandResult { Message = $"{actor.Name} spalma il veleno su {name}: dura un minuto." };
    }

    /// <summary>SRD Basic Poison: a Piercing or Slashing hit with the coated weapon deals 1d4 Poison damage more; then it is spent.</summary>
    private (int Damage, string Text) PoisonOnHit(Actor attacker, AttackProfile attack, bool critical)
    {
        var weapon = attack.Requires ?? attack.Item;
        if (attacker.PoisonedWeapon is not { } coated || weapon != coated || attack.Type is not (DamageType.Piercing or DamageType.Slashing))
            return (0, "");
        attacker.PoisonedWeapon = null;
        var potent = attacker.PoisonedUntil is { } until && until > World.Now;
        attacker.PoisonedUntil = null;
        if (!potent)
            return (0, "");
        var dice = Enumerable.Range(0, critical ? 2 : 1).Select(_ => World.Rng.Roll(4)).ToArray();
        return (dice.Sum(), $" + Veleno [{string.Join(", ", dice)}] da veleno");
    }
}
