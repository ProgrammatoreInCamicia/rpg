using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Client-only appearance. Scenario geometry, identity and collision always come from the Sim.</summary>
public partial class VillageMap
{
	private enum GroundMaterial { Grass, Road, Wood, Flagstone }
	private readonly record struct FloorLook(Color Color, GroundMaterial Material);
	private sealed record PresentationProfile(
		bool StyledInterior,
		IReadOnlyDictionary<LocationId, Func<GridPos, FloorLook>> Floors,
		IReadOnlyDictionary<ActorId, Paladin3DPrototype.CharacterStyle> Actors,
		GridPos? FloorAccent = null);

	private PresentationProfile _presentation = null!;
	private bool StyledInterior => _presentation.StyledInterior;

	private static PresentationProfile PresentationFor(SceneEntry entry)
	{
		var villageFloors = new Dictionary<LocationId, Func<GridPos, FloorLook>>
		{
			[MappedVillageScenario.Ids.Inn] = _ => new(InnFloor, GroundMaterial.Wood),
			[MappedVillageScenario.Ids.FarmerHouse] = _ => new(HouseFloor, GroundMaterial.Wood),
			[MappedVillageScenario.Ids.ForestRoad] = _ => new(ForestRoad, GroundMaterial.Grass),
		};
		var villageActors = new Dictionary<ActorId, Paladin3DPrototype.CharacterStyle>
		{
			[MappedVillageScenario.Ids.Guard] = Paladin3DPrototype.CharacterStyle.Guard,
			[MappedVillageScenario.Ids.Farmer] = Paladin3DPrototype.CharacterStyle.Farmer,
		};
		if (entry == SceneEntry.BlackmarshWatchhouse)
			return new PresentationProfile(true,
				new Dictionary<LocationId, Func<GridPos, FloorLook>>
				{
					[BlackmarshWatchhouse.Ids.Path] = _ => new(ForestRoad, GroundMaterial.Grass),
					[BlackmarshWatchhouse.Ids.Shed] = _ => new(HouseFloor, GroundMaterial.Flagstone),
					[BlackmarshWatchhouse.Ids.Landing] = _ => new(Yard, GroundMaterial.Road),
				},
				new Dictionary<ActorId, Paladin3DPrototype.CharacterStyle>
				{
					[BlackmarshWatchhouse.Ids.Raider] = Paladin3DPrototype.CharacterStyle.Raider,
				});
		if (entry == SceneEntry.InteriorArtPilot)
			villageFloors[MappedVillageScenario.Ids.FarmerHouse] = cell => cell.X <= 8
				? new FloorLook(HouseFloor, GroundMaterial.Flagstone)
				: new FloorLook(HouseFloor, GroundMaterial.Wood);
		return new PresentationProfile(entry == SceneEntry.InteriorArtPilot, villageFloors, villageActors,
			entry == SceneEntry.InteriorArtPilot ? new GridPos(11, 7) : null);
	}

	private FloorLook FloorFor(char symbol, LocationId? zone, GridPos cell)
	{
		if (symbol == '#') return new(Wall.Darkened(0.2f), GroundMaterial.Grass);
		if (symbol == '.') return new(Road, GroundMaterial.Road);
		return zone is { } id && _presentation.Floors.TryGetValue(id, out var floor)
			? floor(cell) : new(Yard, GroundMaterial.Grass);
	}

	private Paladin3DPrototype.CharacterStyle ActorStyle(ActorId id) =>
		_presentation.Actors.TryGetValue(id, out var style)
			? style : Paladin3DPrototype.CharacterStyle.Raider;
}
