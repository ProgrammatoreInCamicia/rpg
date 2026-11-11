using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using Island = RpgSandbox.Sim.Scenarios.BlackmarshWatchIsland.Ids;

namespace RpgSandbox.Game;

/// <summary>Client-only appearance. Scenario geometry, identity and collision always come from the Sim.</summary>
public partial class VillageMap
{
	private enum GroundMaterial { Grass, Road, Wood, Flagstone }
	private readonly record struct FloorLook(Color Color, GroundMaterial Material, bool WarmTint = false);
	private readonly record struct PropLook(string Asset, GridPos Cell, float Width, Vector2 FloorOffset);
	private readonly record struct LandmarkLook(GridPos Cell, Color Color, float Height);
	private sealed record PresentationProfile(
		bool StyledInterior,
		IReadOnlyDictionary<LocationId, Func<GridPos, FloorLook>> Floors,
		IReadOnlyDictionary<ActorId, Paladin3DPrototype.CharacterStyle> Actors,
		GridPos? FloorAccent = null,
		IReadOnlyList<PropLook>? Props = null,
		Vector2? CameraOffset = null,
		float CaptionOffset = -58f,
		IReadOnlyDictionary<LocationId, string>? NearbyLabels = null,
		IReadOnlyList<LandmarkLook>? Landmarks = null);

	private PresentationProfile _presentation = null!;
	private bool StyledInterior => _presentation.StyledInterior;

	private PresentationProfile PresentationFor(SceneEntry entry, AreaId area)
	{
		if (entry == SceneEntry.BlackmarshRegion) return LargeRegionMap
			? IslandPilotPresentationFor(area) : IslandPresentationFor(area);
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
					[BlackmarshWatchhouse.Ids.Shed] = _ => new(HouseFloor, GroundMaterial.Flagstone, true),
					[BlackmarshWatchhouse.Ids.Landing] = _ => new(Yard, GroundMaterial.Road),
				},
				new Dictionary<ActorId, Paladin3DPrototype.CharacterStyle>
				{
					[BlackmarshWatchhouse.Ids.Raider] = Paladin3DPrototype.CharacterStyle.Raider,
				}, Props: new[] { new PropLook("interior-table", BlackmarshWatchhouse.Ids.Table, 75f, Vector2.Zero) },
				CameraOffset: new Vector2(50f, 0f), CaptionOffset: -80f,
				NearbyLabels: new Dictionary<LocationId, string>
				{
					[BlackmarshWatchhouse.Ids.Landing] = "Presso l'approdo",
				});
		if (entry == SceneEntry.InteriorArtPilot)
			villageFloors[MappedVillageScenario.Ids.FarmerHouse] = cell => cell.X <= 8
				? new FloorLook(HouseFloor, GroundMaterial.Flagstone, true)
				: new FloorLook(HouseFloor, GroundMaterial.Wood, true);
		return new PresentationProfile(entry == SceneEntry.InteriorArtPilot, villageFloors, villageActors,
			entry == SceneEntry.InteriorArtPilot ? new GridPos(11, 7) : null,
			entry == SceneEntry.InteriorArtPilot ? new[]
			{
				new PropLook("interior-bed", new GridPos(11, 5), 110f, new Vector2(16, 8)),
				new PropLook("interior-table", new GridPos(10, 8), 75f, Vector2.Zero),
				new PropLook("interior-chest", new GridPos(12, 8), 49f, Vector2.Zero),
				new PropLook("interior-table", new GridPos(6, 6), 75f, Vector2.Zero),
				new PropLook("interior-cabinet", new GridPos(7, 5), 57f, Vector2.Zero),
			} : null);
	}

	private static PresentationProfile IslandPresentationFor(AreaId area)
	{
		// The identity/style of an actor is regional, even when its routine takes it to another map.
		var actors = new Dictionary<ActorId, Paladin3DPrototype.CharacterStyle>
		{
			[Island.Keeper] = Paladin3DPrototype.CharacterStyle.Guard,
			[Island.Fisher] = Paladin3DPrototype.CharacterStyle.Farmer,
			[Island.Raider] = Paladin3DPrototype.CharacterStyle.Raider,
		};
		if (area == Island.Hamlet)
			return new(true, new Dictionary<LocationId, Func<GridPos, FloorLook>>
			{
				[Island.CommonHouse] = _ => new(HouseFloor, GroundMaterial.Wood, true),
				[Island.Well] = _ => new(Yard, GroundMaterial.Flagstone),
				[Island.HamletFork] = _ => new(ForestRoad, GroundMaterial.Grass),
			}, actors, CaptionOffset: -80f, Landmarks: new[]
			{
				new LandmarkLook(new GridPos(12, 6), new Color(0.48f, 0.48f, 0.43f), 24),
				new LandmarkLook(new GridPos(2, 8), new Color(0.63f, 0.31f, 0.18f), 16),
			});
		if (area == Island.ShedArea)
			return new(true, new Dictionary<LocationId, Func<GridPos, FloorLook>>
			{
				[Island.Shed] = _ => new(HouseFloor, GroundMaterial.Flagstone, true),
				[Island.ShedFork] = _ => new(ForestRoad, GroundMaterial.Grass),
			}, actors, Props: new[] { new PropLook("interior-table", BlackmarshWatchhouse.Ids.Table, 75f, Vector2.Zero) },
				CameraOffset: new Vector2(50f, 0), CaptionOffset: -80f);
		if (area == Island.Cove)
			return new(true, new Dictionary<LocationId, Func<GridPos, FloorLook>>
			{
				[Island.Landing] = _ => new(Yard, GroundMaterial.Road),
				[Island.CoveFork] = _ => new(ForestRoad, GroundMaterial.Grass),
			}, actors, CaptionOffset: -80f,
				NearbyLabels: new Dictionary<LocationId, string> { [Island.Landing] = "Presso l'approdo" });
		throw new ArgumentOutOfRangeException(nameof(area), area, "No presentation for this island area.");
	}

	private static PresentationProfile IslandPilotPresentationFor(AreaId area)
	{
		var actors = new Dictionary<ActorId, Paladin3DPrototype.CharacterStyle>
		{
			[Island.Keeper] = Paladin3DPrototype.CharacterStyle.Guard,
			[Island.Fisher] = Paladin3DPrototype.CharacterStyle.Farmer,
			[Island.Raider] = Paladin3DPrototype.CharacterStyle.Raider,
		};
		if (area == Island.Hamlet)
			return new(true, new Dictionary<LocationId, Func<GridPos, FloorLook>>
			{
				[Island.CommonHouse] = _ => new(HouseFloor, GroundMaterial.Wood, true),
				[Island.Well] = _ => new(Yard, GroundMaterial.Flagstone),
				[Island.HamletFork] = _ => new(ForestRoad, GroundMaterial.Grass),
			}, actors, Props: new[]
			{
				new PropLook("interior-table", new GridPos(13, 14), 75f, Vector2.Zero),
				new PropLook("interior-bed", new GridPos(10, 16), 105f, Vector2.Zero),
			}, CaptionOffset: -80f, Landmarks: new[]
			{
				new LandmarkLook(new GridPos(25, 17), new Color(0.48f, 0.48f, 0.43f), 24),
			});
		if (area == Island.ShedArea)
			return new(true, new Dictionary<LocationId, Func<GridPos, FloorLook>>
			{
				[Island.Shed] = _ => new(HouseFloor, GroundMaterial.Flagstone, true),
				[Island.ShedFork] = _ => new(ForestRoad, GroundMaterial.Grass),
			}, actors, Props: new[]
			{
				new PropLook("interior-table", new GridPos(25, 15), 75f, Vector2.Zero),
			}, CaptionOffset: -80f);
		if (area == Island.Cove)
			return new(true, new Dictionary<LocationId, Func<GridPos, FloorLook>>
			{
				[Island.Landing] = _ => new(Yard, GroundMaterial.Road),
				[Island.CoveFork] = _ => new(ForestRoad, GroundMaterial.Grass),
			}, actors, CaptionOffset: -80f, Landmarks: new[]
			{
				new LandmarkLook(new GridPos(40, 27), new Color(0.52f, 0.31f, 0.17f), 17),
			});
		throw new ArgumentOutOfRangeException(nameof(area), area, "No presentation for the island map pilot.");
	}

	private FloorLook FloorFor(char symbol, LocationId? zone, GridPos cell)
	{
		if (LargeRegionMap && _view.Map is { } pilotMap)
		{
			if (symbol == '#') return IsMapBoundary(cell, pilotMap)
				? new(new Color(0.17f, 0.28f, 0.32f), GroundMaterial.Road)
				: new(new Color(0.36f, 0.37f, 0.35f), GroundMaterial.Grass);
			if (symbol == '.')
			{
				var onTrail = pilotMap.Area == Island.Cove
					? Math.Abs(cell.Y - 16) <= 2 || cell.X > 20 && Math.Abs(cell.Y - 25) <= 2
					: Math.Abs(cell.Y - 16) <= 2;
				var flecks = (cell.X * 31 + cell.Y * 17) % 11;
				return onTrail
					? new(new Color(0.80f, 0.76f, 0.67f), GroundMaterial.Road)
					: new(new Color(0.79f - flecks * 0.01f, 0.85f - flecks * 0.008f,
						0.74f - flecks * 0.008f), GroundMaterial.Grass);
			}
		}
		if (symbol == '#') return new(Wall.Darkened(0.2f), GroundMaterial.Grass);
		if (symbol == '.') return new(Road, GroundMaterial.Road);
		return zone is { } id && _presentation.Floors.TryGetValue(id, out var floor)
			? floor(cell) : new(Yard, GroundMaterial.Grass);
	}

	private static bool IsMapBoundary(GridPos cell, MapView map) =>
		cell.X == 0 || cell.Y == 0 || cell.X == map.Width - 1 || cell.Y == map.Height - 1;

	private Paladin3DPrototype.CharacterStyle ActorStyle(ActorId id) =>
		_presentation.Actors.TryGetValue(id, out var style)
			? style : Paladin3DPrototype.CharacterStyle.Raider;

	private string? NearbyPlaceLabel(GridPos position)
	{
		if (_presentation.NearbyLabels is null || _view.Map is not { } map) return null;
		foreach (var cell in new[]
		{
			new GridPos(position.X + 1, position.Y), new GridPos(position.X - 1, position.Y),
			new GridPos(position.X, position.Y + 1), new GridPos(position.X, position.Y - 1),
		})
			if (cell.X >= 0 && cell.Y >= 0 && cell.X < map.Width && cell.Y < map.Height
				&& map.Zones.TryGetValue(map.Rows[cell.Y][cell.X], out var zone)
				&& _presentation.NearbyLabels.TryGetValue(zone, out var label))
				return label;
		return null;
	}
}
