using System.Text.Json;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	private const string RegionSaveIdentity = "rpg-blackmarsh-region";
	private const int RegionSaveRevision = 1;
	private WorldView? _regionStartingWorld;

	private static byte[] RegionSaveBytes(SimulationSession session)
	{
		using var snapshot = new MemoryStream();
		session.Save(snapshot);
		return JsonSerializer.SerializeToUtf8Bytes(new
		{
			Game = RegionSaveIdentity,
			Revision = RegionSaveRevision,
			Snapshot = Convert.ToBase64String(snapshot.ToArray()),
		});
	}

	private static LoadResult ReadRegionSave(string path)
	{
		try
		{
			using var document = JsonDocument.Parse(File.ReadAllBytes(path));
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("Game", out var game) || game.ValueKind != JsonValueKind.String
				|| game.GetString() != RegionSaveIdentity
				|| !root.TryGetProperty("Revision", out var revision) || revision.ValueKind != JsonValueKind.Number
				|| !revision.TryGetInt32(out var number) || number != RegionSaveRevision
				|| !root.TryGetProperty("Snapshot", out var snapshot) || snapshot.ValueKind != JsonValueKind.String)
				return new LoadResult { Error = "file di un'altra partita o revisione non supportata" };
			using var stream = new MemoryStream(Convert.FromBase64String(snapshot.GetString()!));
			return SimulationSession.TryLoad(stream);
		}
		catch (Exception error) when (error is JsonException or FormatException)
		{
			return new LoadResult { Error = "file danneggiato" };
		}
	}

	/// <summary>A loaded Sim is safe to attach only to the map art and interactions this scene already built.</summary>
	private bool SaveMatchesBuiltScene(SimulationSession loaded)
	{
		if (BlackmarshRegionMode) return SaveMatchesRegion(loaded);
		if (loaded.Player != _sim.Player) return false;
		var expected = _sim.GetWorldView();
		var candidate = loaded.GetWorldView();
		if (!expected.Areas.Select(a => a.Id).ToHashSet().SetEquals(candidate.Areas.Select(a => a.Id))
			|| !expected.Stores.Select(s => (s.Id, s.Location, s.Position))
				.ToHashSet().SetEquals(candidate.Stores.Select(s => (s.Id, s.Location, s.Position))))
			return false;
		var map = loaded.GetPlayerView().Map;
		if (map is null) return true; // A same-scenario save can be mid-travel, outside its area map.
		return map.Area == _builtMap.Area && map.Width == _builtMap.Width && map.Height == _builtMap.Height
			&& map.Rows.SequenceEqual(_builtMap.Rows)
			&& map.Zones.Count == _builtMap.Zones.Count
			&& map.Zones.All(zone => _builtMap.Zones.TryGetValue(zone.Key, out var built) && built == zone.Value)
			&& map.IndoorZones.ToHashSet().SetEquals(_builtMap.IndoorZones)
			&& map.Edges.Select(edge => (edge.A, edge.B, edge.Kind, edge.DoorAt)).ToHashSet()
				.SetEquals(_builtMap.Edges.Select(edge => (edge.A, edge.B, edge.Kind, edge.DoorAt)));
	}

	/// <summary>Compare stable regional identity without requiring the player to be on the startup area.</summary>
	private bool SaveMatchesRegion(SimulationSession loaded)
	{
		var expected = _regionStartingWorld
			?? throw new InvalidOperationException("Missing regional world identity at scene startup.");
		var candidate = loaded.GetWorldView();
		return candidate.Player == expected.Player
			&& expected.Areas.Select(x => x.Id).ToHashSet().SetEquals(candidate.Areas.Select(x => x.Id))
			&& expected.Locations.Select(x => (x.Id, x.Area)).ToHashSet()
				.SetEquals(candidate.Locations.Select(x => (x.Id, x.Area)))
			&& expected.Routes.Select(x => (x.From, x.To, x.TravelTime)).ToHashSet()
				.SetEquals(candidate.Routes.Select(x => (x.From, x.To, x.TravelTime)))
			&& expected.Stores.Select(x => (x.Id, x.Location, x.Position)).ToHashSet()
				.SetEquals(candidate.Stores.Select(x => (x.Id, x.Location, x.Position)))
			&& expected.Factions.Select(x => x.Id).ToHashSet().SetEquals(candidate.Factions.Select(x => x.Id))
			&& expected.Actors.Select(x => x.Id).ToHashSet().SetEquals(candidate.Actors.Select(x => x.Id))
			&& (loaded.GetPlayerView().Map is not { } map || expected.Areas.Any(x => x.Id == map.Area));
	}
}
