using System.Text.Json.Serialization;

namespace RpgSandbox.Sim.Persistence;

// Plain serialization shapes for the save file (schema version 5). Times are seconds since the
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
}

internal sealed class ActorDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPlayer { get; set; }
    public string? Faction { get; set; }
    public string? Location { get; set; }
    public int Food { get; set; }
    public ActionDto? Action { get; set; }
    public RaidAssignmentDto? Assignment { get; set; }
    public DecisionDto? LastDecision { get; set; }
    public string? Home { get; set; }
    public WorkShiftDto? Shift { get; set; }
    public long ArrivedAt { get; set; }
    public GuardDutyDto? GuardDuty { get; set; }
    public VigilDto? Vigil { get; set; }
    public SheetDto? Sheet { get; set; }
    public List<ObservationDto> Knowledge { get; set; } = new();
    public List<long> ActedOn { get; set; } = new();
}

internal sealed class StoreDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string? Owner { get; set; }
    public int Food { get; set; }
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
    public string? Faction { get; set; }
}

internal sealed class FactDto
{
    public long Id { get; set; }
    public long At { get; set; }
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
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
    public string Store { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string Location { get; set; } = "";
    public int Amount { get; set; }
    public string? Thief { get; set; }
    public string? ThiefName { get; set; }
    public long ObservedAt { get; set; }
    public long LearnedAt { get; set; }
    public string? Source { get; set; }
    public long? Fact { get; set; }
    public List<string> ToldTo { get; set; } = new();
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
    public int ArmorClass { get; set; }
    public bool StealthDisadvantage { get; set; }
}
