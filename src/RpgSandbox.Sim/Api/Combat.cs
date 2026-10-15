using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Sim.Api;

// C2: the fight in the world. The rules live in the Sim; the client shows the snapshot and sends commands, it never rolls
// dice nor subtracts Hit Points. In a fight, MoveCommand is an instant move within the turn's movement.

/// <summary>
/// Attack with one of the attacks of the actor's combat profile. In a fight, on the actor's turn: it uses the action.
/// Outside a fight it starts one: against a target that does not expect it (it does not see the attacker, or does not
/// hold it hostile) the attack is resolved at once, before initiative; against one that does, initiative comes first
/// and the attack waits for the attacker's turn (<see cref="CombatView.PendingOpeningAttack"/>).
/// </summary>
public sealed record AttackCommand : Command
{
    public required ActorId Target { get; init; }

    /// <summary>The attack's name, as in <see cref="TargetView.Attack"/>.</summary>
    public required string Attack { get; init; }

    /// <summary>SRD Knocking Out: a melee hit that would drop the target to 0 leaves it at 1 HP, unconscious.</summary>
    public bool NonLethal { get; init; }
}

/// <summary>End the actor's turn; the other turns are then resolved up to the player's next one.</summary>
public sealed record EndTurnCommand : Command;

/// <summary>C3a: actions other than attacking (SRD 5.2.1 Actions).</summary>
public enum CombatAction
{
    /// <summary>Dash: for the rest of the turn, extra movement equal to one's Speed.</summary>
    Dash,

    /// <summary>Disengage: for the rest of the turn, one's movement provokes no Opportunity Attacks.</summary>
    Disengage,

    /// <summary>Dodge: until the start of one's next turn, attacks against one have Disadvantage if one sees the attacker.</summary>
    Dodge,
}

/// <summary>C3a: take one of the actions other than attacking, on the actor's turn in a fight. It uses the action.</summary>
public sealed record CombatActionCommand : Command
{
    public required CombatAction Action { get; init; }
}

/// <summary>C3a: whether an action other than attacking can be taken now, and why not.</summary>
public sealed record ActionOptionView
{
    public required CombatAction Action { get; init; }
    public required bool Valid { get; init; }
    public RejectionReason Reason { get; init; }
    public string? Why { get; init; }
}

/// <summary>Seen from the player: on its side or against it.</summary>
public enum CombatSide { Ally, Enemy }

/// <summary>The fight the player is in, as the player knows it.</summary>
public sealed record CombatView
{
    public required int Round { get; init; }

    /// <summary>Whose turn it is; null if the player cannot see that combatant.</summary>
    public ActorId? Turn { get; init; }
    public required bool IsYourTurn { get; init; }

    /// <summary>
    /// Between rounds (every turn played, the world catching up its 6 seconds): when the next round begins. The client
    /// keeps advancing time as usual, or jumps straight to it. Null during a turn.
    /// </summary>
    public GameTime? NextRoundAt { get; init; }

    /// <summary>Initiative order, first to last; the same through the fight.</summary>
    public required IReadOnlyList<InitiativeView> Order { get; init; }

    public required CombatSelfView You { get; init; }

    /// <summary>The other combatants the player can see.</summary>
    public required IReadOnlyList<CombatantView> Combatants { get; init; }

    /// <summary>Squares the player can move to right now, with their cost. Empty when it is not the player's turn.</summary>
    public required IReadOnlyList<ReachableSquareView> Reachable { get; init; }

    /// <summary>C3a: the actions other than attacking (Dash, Disengage, Dodge): can each be taken now.</summary>
    public IReadOnlyList<ActionOptionView> Actions { get; init; } = Array.Empty<ActionOptionView>();

    /// <summary>For each visible enemy and each of the player's attacks: can it be made now, and how.</summary>
    public required IReadOnlyList<TargetView> Targets { get; init; }

    /// <summary>An opening attack waiting for its attacker's turn (against a target that expected it), if the player knows of it.</summary>
    public PendingAttackView? PendingOpeningAttack { get; init; }

    /// <summary>What the player perceived of the fight, oldest first.</summary>
    public required IReadOnlyList<CombatLogEntry> Log { get; init; }
}

/// <summary>C2: one of the player's attacks, to choose from (also outside a fight, to start one).</summary>
public sealed record AttackOptionView
{
    /// <summary>The name an <see cref="AttackCommand"/> takes.</summary>
    public required string Name { get; init; }

    /// <summary>Melee (only these can knock out: <see cref="AttackCommand.NonLethal"/>), or ranged.</summary>
    public required bool Melee { get; init; }
    public required int ReachFeet { get; init; }

    /// <summary>For a ranged attack: normal and long range in feet.</summary>
    public int? NormalRange { get; init; }
    public int? LongRange { get; init; }

    /// <summary>C3b: the item the attack needs (a javelin, a bolt), if any, and how many the player has left.</summary>
    public ItemId? Uses { get; init; }
    public int? Remaining { get; init; }
}

public sealed record InitiativeView
{
    /// <summary>Null for a combatant the player cannot see: it shows as "Qualcuno".</summary>
    public ActorId? Actor { get; init; }
    public required string Name { get; init; }

    /// <summary>The initiative total, for combatants the player can see.</summary>
    public int? Total { get; init; }
    public required bool Current { get; init; }
}

/// <summary>The player in the fight: its own numbers are exact.</summary>
public sealed record CombatSelfView
{
    public required int HitPoints { get; init; }
    public required int MaxHitPoints { get; init; }
    public required LifeState State { get; init; }
    public required bool KnockedOut { get; init; }
    public required int DeathSaveSuccesses { get; init; }
    public required int DeathSaveFailures { get; init; }
    public required int MovementLeftFeet { get; init; }

    /// <summary>P1b: the movement left to the inch (a free move spends fractions of a foot).</summary>
    public int MovementLeftInches { get; init; }
    public required int SquaresLeft { get; init; }
    public required bool ActionAvailable { get; init; }

    /// <summary>SRD: one Reaction, regained at the start of one's own turn.</summary>
    public required bool ReactionAvailable { get; init; }

    /// <summary>C3a: this turn's movement provokes no Opportunity Attacks (Disengage).</summary>
    public bool Disengaged { get; init; }

    /// <summary>C3a: dodging until the start of its next turn: attacks it sees coming have Disadvantage.</summary>
    public bool Dodging { get; init; }
}

/// <summary>Another combatant the player sees: never its exact Hit Points.</summary>
public sealed record CombatantView
{
    public required ActorId Id { get; init; }
    public required string Name { get; init; }
    public required GridPos Position { get; init; }
    public required CombatSide Side { get; init; }

    /// <summary>C3a: it is dodging (a defensive stance one can see).</summary>
    public bool Dodging { get; init; }

    /// <summary>The C1 descriptor: "illeso", "ferito", "malconcio", "tramortito", "a terra", "morto".</summary>
    public required string Condition { get; init; }
}

public sealed record ReachableSquareView
{
    public required GridPos At { get; init; }
    public required int CostFeet { get; init; }
}

public sealed record TargetView
{
    public required ActorId Target { get; init; }
    public required string Attack { get; init; }
    public required bool Valid { get; init; }

    /// <summary>Why it cannot be made now (None when valid), and the text for the UI.</summary>
    public RejectionReason Reason { get; init; }
    public string? Why { get; init; }

    public bool Advantage { get; init; }
    public bool Disadvantage { get; init; }
    public bool LongRange { get; init; }

    /// <summary>Every hit is a Critical Hit (an unconscious target within 5 feet).</summary>
    public bool AutoCritical { get; init; }

    /// <summary>P3: the target's cover against this attack (it depends only on where the two stand).</summary>
    public CoverDegree Cover { get; init; }
}

public sealed record PendingAttackView
{
    /// <summary>Null when the player cannot see the attacker.</summary>
    public ActorId? Attacker { get; init; }
    public required ActorId Target { get; init; }
    public required string Attack { get; init; }
}

public enum CombatLogKind
{
    Started,
    Initiative,
    TurnStarted,
    Move,
    Attack,
    OpportunityAttack,
    OpeningAttackPending,
    OpeningAttackCancelled,

    /// <summary>C4: someone joins the fight (it acts from the next round on).</summary>
    Joined,

    /// <summary>C4: someone runs from the fight; out of every enemy's sight for a round, it is out of it.</summary>
    Fled,

    /// <summary>C3a: someone Dashes, Disengages or Dodges.</summary>
    Dash,
    Disengage,
    Dodge,
    DeathSave,
    Downed,
    KnockedOut,
    Died,
    Ended,
}

/// <summary>One thing the player perceived in the fight, with the dice when there are any.</summary>
public sealed record CombatLogEntry
{
    public required int Round { get; init; }
    public required CombatLogKind Kind { get; init; }

    /// <summary>Null when the player does not see who did it ("Qualcuno").</summary>
    public ActorId? Actor { get; init; }
    public ActorId? Target { get; init; }
    public string? Attack { get; init; }

    /// <summary>The d20s rolled (two with Advantage or Disadvantage), the one kept, the bonus, the total and the AC.</summary>
    public IReadOnlyList<int> D20 { get; init; } = Array.Empty<int>();
    public int? Kept { get; init; }
    public int? Bonus { get; init; }
    public int? Total { get; init; }
    public int? ArmorClass { get; init; }
    public bool? Hit { get; init; }
    public bool Critical { get; init; }

    /// <summary>Damage dealt (never the Hit Points left).</summary>
    public int? Damage { get; init; }

    /// <summary>
    /// For Move: the squares actually walked, in order (it may stop short, for an opportunity attack); for another's
    /// move only those the player saw it on (see <see cref="Spots"/>).
    /// </summary>
    public IReadOnlyList<GridPos> Path { get; init; } = Array.Empty<GridPos>();

    /// <summary>
    /// P2a: for Move, the way walked as exact points (start, turning points, end), to animate. The player's own move
    /// in full; another's only as far as the player saw it: the last stretch seen without a break up to where it stopped,
    /// or, if the player did not see it stop, the first stretch seen; empty if it was never seen.
    /// </summary>
    public IReadOnlyList<Spot> Spots { get; init; } = Array.Empty<Spot>();

    /// <summary>
    /// P2b: for a ranged attack, the straight flight to animate: from the attacker's centre (only if the player saw who
    /// shot) to where the shot ended (only if it saw the target): the target's centre on a hit; on a miss, past the
    /// target on one side, to the first solid thing or the long range. The roll decides the hit, never the drawing.
    /// </summary>
    public Spot? ShotFrom { get; init; }
    public Spot? ShotTo { get; init; }

    /// <summary>P3: for an attack, the target's cover (only if the player saw the target; otherwise None).</summary>
    public CoverDegree Cover { get; init; }

    /// <summary>Ready-made text for the UI.</summary>
    public required string Text { get; init; }
}

/// <summary>
/// What a MoveCommand to that square would do, computed by the same code and without touching the world: no dice, no
/// state. Only threats the player knows of are listed; a hidden creature may still react to the real move.
/// </summary>
public sealed record MovePreview
{
    public required bool Valid { get; init; }
    public RejectionReason Reason { get; init; }
    public string Message { get; init; } = "";

    /// <summary>The squares to walk, in order (the start excluded), and the feet spent once on each.</summary>
    public IReadOnlyList<GridPos> Path { get; init; } = Array.Empty<GridPos>();
    public IReadOnlyList<int> CostFeet { get; init; } = Array.Empty<int>();
    public int TotalFeet { get; init; }

    /// <summary>Opportunity attacks the move would provoke, by threats the player knows of.</summary>
    public IReadOnlyList<ProvocationView> Provokes { get; init; } = Array.Empty<ProvocationView>();
}

public sealed record ProvocationView
{
    /// <summary>The step of <see cref="MovePreview.Path"/> that leaves the reach: the attack comes before it.</summary>
    public required int Step { get; init; }
    public required ActorId By { get; init; }
    public required string Name { get; init; }
}

/// <summary>
/// P2b: what an <see cref="AttackCommand"/> would be now, by the same checks, without touching the world: no dice, no
/// state. Whether it can be made (and why not), and how: Advantage, Disadvantage, long range, a sure critical.
/// </summary>
public sealed record AttackPreview
{
    public required bool Valid { get; init; }
    public RejectionReason Reason { get; init; }
    public string Message { get; init; } = "";
    public bool Advantage { get; init; }
    public bool Disadvantage { get; init; }
    public bool LongRange { get; init; }
    public bool AutoCritical { get; init; }

    /// <summary>P3: the target's cover against this attack.</summary>
    public CoverDegree Cover { get; init; }

    /// <summary>The line aimed along: from the attacker's centre to the point of the target's body aimed at (P3).</summary>
    public Spot? From { get; init; }
    public Spot? To { get; init; }
}

/// <summary>
/// P3: a target's cover against an attack (SRD 5.2.1): half +2 to AC, three-quarters +5. Total cover is no degree:
/// the target cannot be targeted (the attack is refused).
/// </summary>
public enum CoverDegree { None, Half, ThreeQuarters }
