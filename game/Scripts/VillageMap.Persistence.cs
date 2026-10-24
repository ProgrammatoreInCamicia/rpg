using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	/// <summary>A loaded Sim is safe to attach only to the map art and interactions this scene already built.</summary>
	private bool SaveMatchesBuiltScene(SimulationSession loaded)
	{
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
}
