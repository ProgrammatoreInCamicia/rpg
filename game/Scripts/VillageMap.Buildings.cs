using Godot;
using RpgSandbox.Sim;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Cutaway presentation of indoor zones. Sight and movement remain authoritative in the Sim.</summary>
public partial class VillageMap
{
	private Node2D _roofs = null!;
	private static readonly Color RoofColor = new(0.31f, 0.36f, 0.43f);
	private readonly Dictionary<GridPos, Node2D> _roofTiles = new();
	private readonly Dictionary<LocationId, (int MinX, int MaxX, int MinY, int MaxY)> _roofBounds = new();
	private readonly List<(GridPos Covered, GridPos Exposed, Polygon2D Blend)> _roofBlends = new();
	private readonly List<(GridPos Cell, Node2D Rim)> _roofRims = new();
	private readonly List<(MapEdgeView Edge, Node2D Panel)> _interiorEdges = new();
	private readonly List<(Node2D Panel, Rect2 Bounds)> _occludingWalls = new();

	private void BuildRoofLayer()
	{
		// Above wall panels and figures: the roof is the outside of the building, not a floor tint.
		var roofColor = InteriorArtPilot ? new Color(0.33f, 0.28f, 0.25f) : RoofColor;
		var roofTexture = InteriorArtPilot
			? GD.Load<Texture2D>("res://Assets/StyleStudy/interior-roof-slate.png") : null;
		var gableTexture = InteriorArtPilot
			? GD.Load<Texture2D>("res://Assets/StyleStudy/interior-plaster.png") : null;
		_roofs = new Node2D { ZIndex = 20 };
		AddChild(_roofs);
		var map = _view.Map!;
		var indoorCells = new List<(GridPos Cell, LocationId Zone)>();
		for (var y = 0; y < map.Height; y++)
		for (var x = 0; x < map.Width; x++)
		{
			var cell = new GridPos(x, y);
			if (IsIndoor(cell) && ZoneAt(cell) is { } zone) indoorCells.Add((cell, zone));
		}
		foreach (var group in indoorCells.GroupBy(entry => entry.Zone))
			_roofBounds[group.Key] = (group.Min(e => e.Cell.X), group.Max(e => e.Cell.X),
				group.Min(e => e.Cell.Y), group.Max(e => e.Cell.Y));
		foreach (var (cell, _) in indoorCells)
		{
			var tile = new Node2D { Position = CellToScreen(cell) };
			var corners = RoofCorners(cell).Select(p => p - tile.Position).ToArray();
			var peak = new Vector2(0, -RoofHeight(cell, cell.X, cell.Y));
			var bounds = _roofBounds[ZoneAt(cell)!.Value];
			var acrossX = bounds.MaxX - bounds.MinX < bounds.MaxY - bounds.MinY;
			var across = acrossX ? cell.X : cell.Y;
			var middle = acrossX ? (bounds.MinX + bounds.MaxX) / 2f : (bounds.MinY + bounds.MaxY) / 2f;
			var plane = across < middle ? roofColor.Lightened(0.08f) : roofColor.Darkened(0.07f);
			if (roofTexture is not null)
			{
				var u = (cell.X - bounds.MinX + 1) * 96f;
				var v = (cell.Y - bounds.MinY + 1) * 96f;
				var face = new Polygon2D
				{
					Polygon = corners,
					Texture = roofTexture,
					UV = new[] { new Vector2(u - 48, v - 48), new Vector2(u + 48, v - 48),
						new Vector2(u + 48, v + 48), new Vector2(u - 48, v + 48) },
					Color = across < middle
						? new Color(0.92f, 0.87f, 0.81f) : new Color(0.76f, 0.73f, 0.70f),
				};
				tile.AddChild(face);
			}
			else
				for (var side = 0; side < 4; side++)
					tile.AddChild(new Polygon2D
					{
						Polygon = new[] { corners[side], corners[(side + 1) % 4], peak },
						Color = plane,
					});
			_roofs.AddChild(tile);
			_roofTiles[cell] = tile;
		}
		// Outside, keep the per-cell cutaway; draw only the building perimeter, never grid lines.
		foreach (var cell in _roofTiles.Keys)
		foreach (var nearby in new[]
		{
			new GridPos(cell.X + 1, cell.Y), new GridPos(cell.X - 1, cell.Y),
			new GridPos(cell.X, cell.Y + 1), new GridPos(cell.X, cell.Y - 1),
		})
		{
			var (from, to) = RoofEdge(cell, nearby);
			if (IsIndoor(nearby) && ZoneAt(cell) == ZoneAt(nearby))
			{
				// Feather only into a cell already visible according to the Sim. An unseen
				// room remains fully opaque, even at the edge of the cutaway.
				var inward = (CellToScreen(nearby) - CellToScreen(cell)).Normalized() * 9f;
				var blend = new Polygon2D
				{
					Polygon = new[] { from, to, to + inward, from + inward },
					VertexColors = new[]
					{
						roofColor, roofColor,
						roofColor with { A = 0 }, roofColor with { A = 0 },
					},
					Color = Colors.White,
				};
				_roofs.AddChild(blend);
				_roofBlends.Add((cell, nearby, blend));
			}
			else
			{
				var rim = new Node2D();
				if (InteriorArtPilot)
				{
					// The pitched roof and the level wall have different top edges. Fill the gable
					// between them instead of exposing the dark void under the roof plane.
					var d = Diamond(1.012f);
					var center = CellToScreen(cell) + Vector2.Up * PilotWallHeight;
					var wallFrom = center + (nearby.X > cell.X ? d[1] : nearby.X < cell.X ? d[3]
						: nearby.Y > cell.Y ? d[2] : d[0]);
					var wallTo = center + (nearby.X > cell.X ? d[2] : nearby.X < cell.X ? d[0]
						: nearby.Y > cell.Y ? d[3] : d[1]);
					var u = 60 + (cell.X * 41 + cell.Y * 67) % 900;
					rim.AddChild(new Polygon2D { Polygon = new[] { wallFrom, wallTo, to, from },
						Texture = gableTexture,
						UV = new[] { new Vector2(u, 400), new Vector2(u + 64, 400),
							new Vector2(u + 64, 350), new Vector2(u, 350) },
						Color = new Color(0.78f, 0.73f, 0.66f) });
					rim.AddChild(new Polygon2D { Polygon = new[] { from, to, to + Vector2.Down * 6f,
						from + Vector2.Down * 6f }, Color = new Color(0.27f, 0.19f, 0.14f) });
				}
				rim.AddChild(new Line2D { Points = new[] { from, to }, Width = 2f,
					DefaultColor = roofColor.Darkened(0.32f) });
				_roofs.AddChild(rim);
				_roofRims.Add((cell, rim));
			}
		}
	}

	private float RoofHeight(GridPos cell, float x, float y)
	{
		var bounds = _roofBounds[ZoneAt(cell)!.Value];
		var acrossX = bounds.MaxX - bounds.MinX < bounds.MaxY - bounds.MinY;
		var start = acrossX ? bounds.MinX - 0.5f : bounds.MinY - 0.5f;
		var end = acrossX ? bounds.MaxX + 0.5f : bounds.MaxY + 0.5f;
		var along = acrossX ? x : y;
		var distance = MathF.Max(0, MathF.Min(along - start, end - along));
		return (InteriorArtPilot ? PilotWallHeight : WallHeight + 5f) + MathF.Min(19f, distance * 12f);
	}

	private Vector2[] RoofCorners(GridPos cell)
	{
		var center = CellToScreen(cell);
		var diamond = Diamond(1.012f);
		return new[]
		{
			center + diamond[0] + Vector2.Up * RoofHeight(cell, cell.X - 0.5f, cell.Y - 0.5f),
			center + diamond[1] + Vector2.Up * RoofHeight(cell, cell.X + 0.5f, cell.Y - 0.5f),
			center + diamond[2] + Vector2.Up * RoofHeight(cell, cell.X + 0.5f, cell.Y + 0.5f),
			center + diamond[3] + Vector2.Up * RoofHeight(cell, cell.X - 0.5f, cell.Y + 0.5f),
		};
	}

	private (Vector2 From, Vector2 To) RoofEdge(GridPos cell, GridPos nearby)
	{
		var corners = RoofCorners(cell);
		var from = nearby.X > cell.X ? corners[1] : nearby.X < cell.X ? corners[3]
			: nearby.Y > cell.Y ? corners[2] : corners[0];
		var to = nearby.X > cell.X ? corners[2] : nearby.X < cell.X ? corners[0]
			: nearby.Y > cell.Y ? corners[3] : corners[1];
		return (from, to);
	}

	private bool IsIndoor(GridPos cell)
	{
		var map = _view.Map;
		if (map is null || cell.X < 0 || cell.Y < 0 || cell.X >= map.Width || cell.Y >= map.Height)
			return false;
		return map.Zones.TryGetValue(map.Rows[cell.Y][cell.X], out var zone) && map.IndoorZones.Contains(zone);
	}

	private LocationId? ZoneAt(GridPos cell)
	{
		var map = _view.Map!;
		return map.Zones.TryGetValue(map.Rows[cell.Y][cell.X], out var zone) ? zone : null;
	}

	private void RegisterBuildingEdge(MapEdgeView edge, Node2D panel)
	{
		if (IsIndoor(edge.A) && IsIndoor(edge.B) && ZoneAt(edge.A) == ZoneAt(edge.B))
			_interiorEdges.Add((edge, panel));
		if (edge.Kind == MapEdgeKind.Wall)
		{
			var polygon = panel.GetNode<Polygon2D>("Panel").Polygon;
			var min = new Vector2(polygon.Min(p => p.X), polygon.Min(p => p.Y));
			var max = new Vector2(polygon.Max(p => p.X), polygon.Max(p => p.Y));
			_occludingWalls.Add((panel, new Rect2(panel.Position + min, max - min)));
		}
	}

	private void RegisterBlockOccluder(Node2D wall) =>
		_occludingWalls.Add((wall, new Rect2(wall.Position + new Vector2(-TileW / 2, -WallHeight - TileH / 2),
			new Vector2(TileW, WallHeight + TileH))));

	private GridPos FacadeShadeCell(MapEdgeView edge) => IsIndoor(edge.A) && !IsIndoor(edge.B) ? edge.B : edge.A;

	private GridPos BuildingShadeCell(MapEdgeView edge)
	{
		if (!InteriorArtPilot || _view.Position is not { } player || !IsIndoor(player))
			return FacadeShadeCell(edge);
		if (IsIndoor(edge.A) && IsIndoor(edge.B))
		{
			var visibility = _view.MapVisibility;
			return visibility is not null && visibility[edge.B.Y][edge.B.X] > visibility[edge.A.Y][edge.A.X]
				? edge.B : edge.A;
		}
		return IsIndoor(edge.A) ? edge.A : IsIndoor(edge.B) ? edge.B : FacadeShadeCell(edge);
	}

	private void RefreshBuildingVisibility()
	{
		if (_view.Position is null || _view.MapVisibility is not { } visibility)
		{
			_roofs.Visible = false;
			return;
		}
		_roofs.Visible = true;
		var daylight = _view.Daylight switch { Light.Bright => 1f, Light.Dim => 0.72f, _ => 0.42f };
		var interior = IsIndoor(_view.Position.Value) ? ZoneAt(_view.Position.Value) : null;
		bool OpenRoof(GridPos cell) => interior is { } zone && ZoneAt(cell) == zone;
		foreach (var (cell, tile) in _roofTiles)
		{
			tile.Visible = !OpenRoof(cell) && visibility[cell.Y][cell.X] != '2';
			tile.Modulate = new Color(daylight, daylight, daylight);
		}
		foreach (var (covered, exposed, blend) in _roofBlends)
		{
			blend.Visible = !OpenRoof(covered) && visibility[covered.Y][covered.X] != '2'
				&& visibility[exposed.Y][exposed.X] == '2';
			blend.Modulate = new Color(daylight, daylight, daylight);
		}
		foreach (var (cell, rim) in _roofRims)
		{
			rim.Visible = !OpenRoof(cell) && visibility[cell.Y][cell.X] != '2';
			rim.Modulate = new Color(daylight, daylight, daylight);
		}
		// The outside keeps its facade. Internal partitions are only drawn where the player can
		// currently see either side; remembered rooms must not leak their floor plan.
		foreach (var (edge, panel) in _interiorEdges)
		{
			var inSight = visibility[edge.A.Y][edge.A.X] == '2' || visibility[edge.B.Y][edge.B.X] == '2'
				|| interior is not null && (visibility[edge.A.Y][edge.A.X] == '1' || visibility[edge.B.Y][edge.B.X] == '1');
			var closedDoor = edge.DoorAt is not { } doorAt || _view.Doors.Any(d => d.At == doorAt
				&& d.Open == false && d.Known != DoorKnowledge.Unknown);
			panel.Visible = inSight && (InteriorArtPilot || closedDoor);
		}
		RefreshInteriorArtVisibility();
	}

	private void UpdateBuildingOcclusion(float delta)
	{
		if (_view.Position is null || !_tokens.TryGetValue(_view.Id, out var player)) return;
		var playerBounds = new Rect2(player.Position + new Vector2(-15, -66), new Vector2(30, 73));
		Rect2? targetBounds = null;
		var mouse = GetGlobalMousePosition();
		if (PersonAt(mouse) is { } target && _tokens.TryGetValue(target, out var token))
			targetBounds = new Rect2(token.Position + new Vector2(-13, -54), new Vector2(26, 61));
		else if (DoorAt(mouse) is not null || WindowAt(mouse) is not null)
			targetBounds = new Rect2(mouse - new Vector2(6, 6), new Vector2(12, 12));
		var blend = 1f - Mathf.Exp(-12f * delta);
		foreach (var (wall, bounds) in _occludingWalls)
		{
			var coversPlayer = bounds.Intersects(playerBounds) && wall.Position.Y >= player.Position.Y - 4f;
			var coversTarget = targetBounds is { } rect && bounds.Intersects(rect) && wall.Position.Y >= rect.End.Y - 11f;
			var alpha = Mathf.Lerp(wall.Modulate.A, coversPlayer || coversTarget ? 0.23f : 1f, blend);
			wall.Modulate = wall.Modulate with { A = alpha };
		}
	}

	internal bool RoofVisibleForSmoke(GridPos cell) => _roofTiles.TryGetValue(cell, out var tile) && tile.Visible;
	internal bool RoofRaisedForSmoke(GridPos cell) => _roofTiles.TryGetValue(cell, out var tile)
		&& tile.GetChildren().OfType<Polygon2D>().Any(face => face.Polygon.Min(point => point.Y) < -WallHeight);
	internal bool InteriorEdgeVisibleForSmoke(GridPos a, GridPos b) =>
		_interiorEdges.Single(p => p.Edge.A == a && p.Edge.B == b).Panel.Visible;
}
