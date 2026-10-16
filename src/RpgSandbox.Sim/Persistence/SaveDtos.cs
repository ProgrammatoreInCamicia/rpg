using System.Text.Json.Serialization;

namespace RpgSandbox.Sim.Persistence;

// Plain serialization shapes for the save file (schema version 21). Times are seconds since the
// scenario start; ids are their string/number values. Kept separate from the domain on purpose:
// the domain can change shape while the file format changes only deliberately.

internal sealed class SaveDto
{
    public int Version { get; set; }
    public long Now { get; set; }
    public string Player { get; set; } = "";
    public long NextActionId { get; set; }
    public long NextFactId { get; set; }
    public long NextSequence { get; set; }
    public long NextObservationId { get; set; }
    public string RngAlgorithm { get; set; } = "";
    public ulong RngState { get; set; }
    public List<AreaDto> Areas { get; set; } = new();
    public List<LocationDto> Locations { get; set; } = new();
    public List<RouteDto> Routes { get; set; } = new();
    public List<FactionDto> Factions { get; set; } = new();
    public List<ActorDto> Actors { get; set; } = new();
    public List<StoreDto> Stores { get; set; } = new();
    public List<ScheduledDto> Schedule { get; set; } = new();
    public List<FactDto> Facts { get; set; } = new();
    public List<MapDto> Maps { get; set; } = new();
    public ContentDto? Content { get; set; }
    public List<EncounterDto> Encounters { get; set; } = new();
    public long NextEncounterId { get; set; }

    /// <summary>P2b (schema 27): the last fight the player was in, once over; null if none.</summary>
    public LastFightDto? LastFight { get; set; }
}

internal sealed class AreaDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

internal sealed class LocationDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Area { get; set; } = "";
    public bool Lit { get; set; }
}

internal sealed class RouteDto
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public long Seconds { get; set; }
}

internal sealed class RaidPolicyDto
{
    public int FoodThreshold { get; set; }
    public int RaidAmount { get; set; }
    public long EvaluationInterval { get; set; }
}

internal sealed class FactionDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? HomeStore { get; set; }
    public int DailyUpkeep { get; set; }
    public long UpkeepTimeOfDay { get; set; }
    public RaidPolicyDto? Policy { get; set; }
    public long? NextEvaluation { get; set; }
    public DecisionDto? LastDecision { get; set; }
    public string? Authority { get; set; }
    public List<AvoidDto> AvoidUntil { get; set; } = new();
    public bool HostileToStrangers { get; set; }
    public List<string> Enemies { get; set; } = new();
    public List<WantedDto> Wanted { get; set; } = new();
}

internal sealed class ActorDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPlayer { get; set; }
    public string? Faction { get; set; }
    public string? Location { get; set; }
    public ActionDto? Action { get; set; }
    public RaidAssignmentDto? Assignment { get; set; }
    public DecisionDto? LastDecision { get; set; }
    public string? Home { get; set; }
    public WorkShiftDto? Shift { get; set; }
    public long ArrivedAt { get; set; }
    public GuardDutyDto? GuardDuty { get; set; }
    public VigilDto? Vigil { get; set; }
    public SheetDto? Sheet { get; set; }
    public List<ClaimDto> Claims { get; set; } = new();
    public List<long> SettledThefts { get; set; } = new();
    public List<CargoDto> Cargo { get; set; } = new();
    public PosDto? Position { get; set; }
    public string? MapArea { get; set; }
    public int? Sneak { get; set; }
    public List<ItemCountDto>? Inventory { get; set; }
    public long Coins { get; set; }
    public long? TorchLitUntil { get; set; }
    public List<RememberedDoorDto> Doors { get; set; } = new();
    /// <summary>P4a (schema 29): the doors and windows the player found locked, stuck or broken.</summary>
    public List<KnownBarrierDto>? KnownBarriers { get; set; }
    /// <summary>P4a (schema 29): a door to lock again behind a walk.</summary>
    public PendingRelockDto? PendingRelock { get; set; }
    public List<ObservationDto> Knowledge { get; set; } = new();
    public List<long> ActedOn { get; set; } = new();
    public string Build { get; set; } = "";
    public bool SheetOverride { get; set; }
    public EquipmentDto Equipment { get; set; } = new();
    public VitalityDto? Vitality { get; set; }
    public List<string> FoughtWith { get; set; } = new();
    public LoreDto? Lore { get; set; }

    /// <summary>C5b (schema 22): Experience Points, and (the player only) the creatures it was rewarded for defeating.</summary>
    public long? Experience { get; set; }
    /// <summary>Schema 33 (R1): Hit Point Dice spent, the end of the last Long Rest, the last damage or fight.</summary>
    public int HitDiceSpent { get; set; }
    public long? LastLongRestEnd { get; set; }
    public long? LastHarmAt { get; set; }
    /// <summary>Schema 34 (R2): conditions, Exhaustion and the marks of Sap, Slow and Vex.</summary>
    public List<ConditionDto>? Conditions { get; set; }
    public int Exhaustion { get; set; }
    public string? SappedBy { get; set; }
    public string? SlowedBy { get; set; }
    public string? Vexing { get; set; }
    public bool VexFresh { get; set; }
    public List<string>? Defeated { get; set; }

    /// <summary>P1 (schema 23): the exact point where it stands, when not the centre of its square.</summary>
    public PosDto? Spot { get; set; }
    /// <summary>Schema 31: direction of the player's gaze, in map coordinates.</summary>
    public PosDto? Facing { get; set; }
}

internal sealed class StoreDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string? Owner { get; set; }
    public List<ItemCountDto>? Contents { get; set; }
    public PosDto? Position { get; set; }
    public List<PosDto> Access { get; set; } = new();
}

internal sealed class RaidAssignmentDto
{
    public string Faction { get; set; } = "";
    public string Target { get; set; } = "";
    public string Home { get; set; } = "";
    public int Amount { get; set; }
    public long AssignedAt { get; set; }
    public bool TakeAttempted { get; set; }
    public bool Aborted { get; set; }

    /// <summary>Schema 32 (P5a): the door or window it is breaking through and its tries there; given up on a barrier.</summary>
    public PosDto? BreakInA { get; set; }
    public PosDto? BreakInB { get; set; }
    public int BreakInTries { get; set; }
    public bool Balked { get; set; }
}

internal sealed class DecisionDto
{
    public long At { get; set; }
    public string Rule { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<string> Inputs { get; set; } = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TravelActionDto), "Travel")]
[JsonDerivedType(typeof(DepositFoodActionDto), "DepositFood")]
[JsonDerivedType(typeof(TakeFoodActionDto), "TakeFood")]
[JsonDerivedType(typeof(WaitActionDto), "Wait")]
[JsonDerivedType(typeof(ReportActionDto), "Report")]
[JsonDerivedType(typeof(GuardActionDto), "Guard")]
[JsonDerivedType(typeof(ConfiscateActionDto), "Confiscate")]
[JsonDerivedType(typeof(MoveActionDto), "Move")]
[JsonDerivedType(typeof(DownedActionDto), "Downed")]
[JsonDerivedType(typeof(BarrierActionDto), "Barrier")]
[JsonDerivedType(typeof(RestActionDto), "Rest")]
internal abstract class ActionDto
{
    public long Id { get; set; }
    public long StartedAt { get; set; }
    public long CompletesAt { get; set; }
    public string Description { get; set; } = "";
}

internal sealed class TravelActionDto : ActionDto
{
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
}

internal sealed class DepositFoodActionDto : ActionDto
{
    public string Store { get; set; } = "";
    public int Amount { get; set; }
}

internal sealed class TakeFoodActionDto : ActionDto
{
    public string Store { get; set; } = "";
    public int Amount { get; set; }
    public int? StealthTotal { get; set; }
    public List<string> SeenAtStart { get; set; } = new();
    public List<LightChangeDto> SourceLightLog { get; set; } = new();
    public List<SightDto> Sight { get; set; } = new();
}

internal sealed class RestActionDto : ActionDto
{
    public bool Long { get; set; }
    public int HitDice { get; set; }
}

internal sealed class WaitActionDto : ActionDto
{
    public bool Interruptible { get; set; }
}

internal sealed class ScheduledDto
{
    public long Due { get; set; }
    public long Sequence { get; set; }
    public string Job { get; set; } = "";
    public long? Action { get; set; }
    public int? Step { get; set; }
    public string? Faction { get; set; }
    public long? Encounter { get; set; }
}

internal sealed class FactDto
{
    public long Id { get; set; }
    public long At { get; set; }
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public string? PlayerDescription { get; set; }
    public List<string> Participants { get; set; } = new();
}

internal sealed class ReportActionDto : ActionDto
{
    public string Recipient { get; set; } = "";
    public long Observation { get; set; }
}

internal sealed class GuardActionDto : ActionDto
{
    public string Store { get; set; } = "";
}

internal sealed class AvoidDto
{
    public string Store { get; set; } = "";
    public long Until { get; set; }
}

internal sealed class WorkShiftDto
{
    public string Location { get; set; } = "";
    public long Start { get; set; }
    public long End { get; set; }
}

internal sealed class GuardDutyDto
{
    public string Store { get; set; } = "";
    public long Since { get; set; }
    public long Until { get; set; }
}

internal sealed class ObservationDto
{
    public long Id { get; set; }
    public long Origin { get; set; }
    public string Kind { get; set; } = "Theft";
    public string? Store { get; set; }
    public string? StoreName { get; set; }
    public string? Location { get; set; }
    public PosDto? Square { get; set; }
    public int Amount { get; set; }
    public string? Culprit { get; set; }
    public string? CulpritName { get; set; }
    public string? Victim { get; set; }
    public string? VictimName { get; set; }
    public List<InvolvedDto> Involved { get; set; } = new();
    public long? Encounter { get; set; }
    public long ObservedAt { get; set; }
    public long LearnedAt { get; set; }
    public string? Source { get; set; }
    public long? Fact { get; set; }
    public string Perceived { get; set; } = "Seen";
    public List<string> ToldTo { get; set; } = new();
}

internal sealed class InvolvedDto
{
    public string Actor { get; set; } = "";
    public string Name { get; set; } = "";
}

internal sealed class VigilDto
{
    public string Store { get; set; } = "";
    public long Until { get; set; }
}

internal sealed class SheetDto
{
    public string Title { get; set; } = "";
    public int Strength { get; set; }
    public int Dexterity { get; set; }
    public int Constitution { get; set; }
    public int Intelligence { get; set; }
    public int Wisdom { get; set; }
    public int Charisma { get; set; }
    public int ProficiencyBonus { get; set; }
    public List<string> SkillProficiencies { get; set; } = new();
    /// <summary>P4a (schema 29): tool proficiencies by item id; absent before.</summary>
    public List<string>? ToolProficiencies { get; set; }
    public List<string>? SaveProficiencies { get; set; }
    public int ArmorClass { get; set; }
    public bool StealthDisadvantage { get; set; }
    public int Speed { get; set; } = 30;
}

internal sealed class ConfiscateActionDto : ActionDto
{
    public string Target { get; set; } = "";
    public string Store { get; set; } = "";
}

internal sealed class ClaimDto
{
    public string Thief { get; set; } = "";
    public string Store { get; set; } = "";
    public long Theft { get; set; }
    public int Owed { get; set; }
}

internal sealed class CargoDto
{
    public string Store { get; set; } = "";
    public int Amount { get; set; }
}

internal sealed class PosDto
{
    public int X { get; set; }
    public int Y { get; set; }
}

internal sealed class MapDto
{
    public string Area { get; set; } = "";
    public List<string> Rows { get; set; } = new();
    public Dictionary<string, string> Zones { get; set; } = new();
    public List<LightDto> Lights { get; set; } = new();
    public List<DoorDto> Doors { get; set; } = new();
    public List<MapEdgeDto>? Edges { get; set; }
    public List<string>? IndoorZones { get; set; }
    public List<PosDto>? Explored { get; set; }
    public List<ExitDto> Exits { get; set; } = new();
    public List<PostDto> Posts { get; set; } = new();
    public List<GroundItemDto> Ground { get; set; } = new();

    /// <summary>P4a (schema 29): doors and windows that lock or stick, as they hold now.</summary>
    public List<BarrierDto>? Barriers { get; set; }

    /// <summary>P4b (schema 30): windows whose glass is not whole.</summary>
    public List<GlassDto>? Glass { get; set; }
}

internal sealed class BarrierDto
{
    public int AX { get; set; }
    public int AY { get; set; }
    public int BX { get; set; }
    public int BY { get; set; }
    public string? Key { get; set; }
    public bool Locked { get; set; }
    public bool Stuck { get; set; }
    public bool Broken { get; set; }
    public int ForceDc { get; set; }
    public string? Clue { get; set; }

    /// <summary>P4b (schema 30): bars on a window.</summary>
    public bool Barred { get; set; }
    public int BarsDc { get; set; } = 25;
    public string? BarsClue { get; set; }
}

internal sealed class GlassDto
{
    public int AX { get; set; }
    public int AY { get; set; }
    public int BX { get; set; }
    public int BY { get; set; }
    public string State { get; set; } = "";
}

internal sealed class KnownBarrierDto
{
    public string Area { get; set; } = "";
    public int AX { get; set; }
    public int AY { get; set; }
    public int BX { get; set; }
    public int BY { get; set; }
    public bool Locked { get; set; }
    public bool Stuck { get; set; }
    public bool Broken { get; set; }
    public string Clue { get; set; } = "";
    public long LearnedAt { get; set; }
    public bool HasLock { get; set; }
}

internal sealed class PendingRelockDto
{
    public int X { get; set; }
    public int Y { get; set; }
    public long Walk { get; set; }
    public int AfterStep { get; set; }
}

internal sealed class BarrierActionDto : ActionDto
{
    public string Work { get; set; } = "";
    public int AX { get; set; }
    public int AY { get; set; }
    public int BX { get; set; }
    public int BY { get; set; }
}

internal sealed class MoveActionDto : ActionDto
{
    public PosDto From { get; set; } = new();
    public List<PosDto> Path { get; set; } = new();
    public int Speed { get; set; }
    public bool Stealthy { get; set; }

    /// <summary>P1 (schema 23): a free walk's start, the exact point of each step and the second each is reached.</summary>
    public PosDto? FromSpot { get; set; }
    public List<PosDto>? Spots { get; set; }
    /// <summary>Schemas 23–24: the seconds of each step (schema 25: <see cref="Ticks"/>).</summary>
    public List<long>? Seconds { get; set; }

    /// <summary>P1c (schema 25): the tick of each step of a free walk, and its way.</summary>
    public List<long>? Ticks { get; set; }
    public List<PosDto>? Vertices { get; set; }
}

internal sealed class MapEdgeDto
{
    public int AX { get; set; }
    public int AY { get; set; }
    public int BX { get; set; }
    public int BY { get; set; }
    public string Kind { get; set; } = "";
    public int? DoorX { get; set; }
    public int? DoorY { get; set; }

    /// <summary>P2b (schema 27): for a window, whether it is open.</summary>
    public bool? Open { get; set; }
}

internal sealed class LightDto
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Bright { get; set; }
    public int Dim { get; set; }
}

internal sealed class DoorDto
{
    public int X { get; set; }
    public int Y { get; set; }
    public bool Open { get; set; }
}

internal sealed class ExitDto
{
    public string Location { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
}

internal sealed class PostDto
{
    public string Location { get; set; } = "";
    public string Kind { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
}

internal sealed class LightChangeDto
{
    public long At { get; set; }
    public int Light { get; set; }
}

internal sealed class SightDto
{
    public string Actor { get; set; } = "";
    public List<SpanDto> Spans { get; set; } = new();
}

internal sealed class SpanDto
{
    public long From { get; set; }
    public long? To { get; set; }
}

internal sealed class RememberedDoorDto
{
    public string Area { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public bool Open { get; set; }
    public long SeenAt { get; set; }
}

// ---------------------------------------------------------------- C2 (schema 14): the fight in the world

internal sealed class VitalityDto
{
    public int Maximum { get; set; }
    public bool MakesDeathSaves { get; set; }
    public int Current { get; set; }
    public string State { get; set; } = "";
    public bool KnockedOut { get; set; }
    public int DeathSaveSuccesses { get; set; }
    public int DeathSaveFailures { get; set; }
}

internal sealed class DownedActionDto : ActionDto;

internal sealed class EncounterDto
{
    public long Id { get; set; }
    public string Area { get; set; } = "";
    public int Round { get; set; }
    public int Turn { get; set; }
    public long RoundStart { get; set; }
    public bool RoundClosed { get; set; }
    public bool AttackedThisRound { get; set; }
    public OpeningDto? Opening { get; set; }
    public List<CombatantDto> Combatants { get; set; } = new();
    public List<CombatRecordDto> Log { get; set; } = new();
}

internal sealed class CombatantDto
{
    public string Actor { get; set; } = "";
    public int Side { get; set; }
    public int Initiative { get; set; }
    public int InitiativeBonus { get; set; }
    public int MovedFeet { get; set; }

    /// <summary>P1b (schema 24): movement spent in inches; before, whole feet in MovedFeet.</summary>
    public int? MovedInches { get; set; }
    public bool ActionUsed { get; set; }
    public bool ReactionUsed { get; set; }
    public bool OpeningSpent { get; set; }
    /// <summary>Before schema 26: the square where it last saw an enemy.</summary>
    public PosDto? LastSeenEnemy { get; set; }

    /// <summary>P2a (schema 26): the exact point, in inches.</summary>
    public PosDto? LastSeenAt { get; set; }
    public int? JoinedInRound { get; set; }
    public bool Fleeing { get; set; }
    public int Dashes { get; set; }
    public bool Disengaged { get; set; }
    public bool Dodging { get; set; }
    public bool Cleaved { get; set; }
    public bool Interacted { get; set; }
}

internal sealed class OpeningDto
{
    public string Attacker { get; set; } = "";
    public string Target { get; set; } = "";
    public string Attack { get; set; } = "";
    public bool NonLethal { get; set; }
}

internal sealed class CombatRecordDto
{
    public int Round { get; set; }
    public string Kind { get; set; } = "";
    public string? Actor { get; set; }
    public string? Target { get; set; }
    public string? Attack { get; set; }
    public List<int> D20 { get; set; } = new();
    public int? Kept { get; set; }
    public int? Bonus { get; set; }
    public int? Total { get; set; }
    public int? ArmorClass { get; set; }
    public bool? Hit { get; set; }
    public bool Critical { get; set; }
    public int? Damage { get; set; }
    public List<PosDto> Path { get; set; } = new();

    /// <summary>P2a (schema 26): the points of a move as the player saw them, in inches.</summary>
    public List<PosDto>? Spots { get; set; }

    /// <summary>P2b (schema 27): a shot's flight, in inches.</summary>
    public PosDto? ShotFrom { get; set; }
    public PosDto? ShotTo { get; set; }

    /// <summary>P3 (schema 28): the target's cover, by name; null before.</summary>
    public string? Cover { get; set; }
    public string Text { get; set; } = "";
    public string? PlayerText { get; set; }
    public bool PlayerSawActor { get; set; }
    public bool PlayerSawTarget { get; set; }
}

internal sealed class WantedDto
{
    public string Actor { get; set; } = "";
    public long Fact { get; set; }
}

internal sealed class ItemCountDto
{
    public string Item { get; set; } = "";
    public int Count { get; set; }
}

internal sealed class GroundItemDto
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Item { get; set; } = "";
    public int Count { get; set; }
}

internal sealed class EquipmentDto
{
    public string? Armor { get; set; }
    public string? Shield { get; set; }
    public string? MainHand { get; set; }
}

/// <summary>D1a: the content pack the game is played with, carried whole (with its identity) so the save stands alone.</summary>
internal sealed class ContentDto
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Hash { get; set; } = "";
    public List<ContentFileDto> Files { get; set; } = new();
}

internal sealed class ContentFileDto
{
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>C5a: what the player knows of creatures and kinds.</summary>
internal sealed class LoreDto
{
    public List<CreatureLoreDto> Creatures { get; set; } = new();
    public List<KindLoreDto> Kinds { get; set; } = new();
}

internal sealed class CreatureLoreDto
{
    public string Creature { get; set; } = "";
    public int? ArmorClassAbove { get; set; }
    public int? ArmorClassAtMost { get; set; }
    public List<string> Attacks { get; set; } = new();
    public List<string> Tells { get; set; } = new();
    public bool Observed { get; set; }
}

internal sealed class KindLoreDto
{
    public string Kind { get; set; } = "";
    public List<string> Attacks { get; set; } = new();
    public List<string> Tells { get; set; } = new();
    public bool Studied { get; set; }
}

internal sealed class LastFightDto
{
    public string Area { get; set; } = "";
    public List<CombatRecordDto> Log { get; set; } = new();
}

internal sealed class ConditionDto
{
    public string Kind { get; set; } = "";
    public string? Source { get; set; }
    public long? Until { get; set; }
    public string? EndsAtTurnOf { get; set; }
}
