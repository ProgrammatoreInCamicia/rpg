using Godot;
using RpgSandbox.Sim;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Cutaway presentation of indoor zones. Sight and movement remain authoritative in the Sim.</summary>
public partial class VillageMap
{
	private Node2D _roofs = null!;
	private static readonly Color RoofColor = new(0.36f, 0.39f, 0.43f);
	private readonly Dictionary<GridPos, Polygon2D> _roofTiles = new();
	private readonly List<(GridPos Covered, GridPos Exposed, Polygon2D Blend)> _roofBlends = new();
	private readonly List<(GridPos Cell, Line2D Rim)> _roofRims = new();
	private readonly List<(MapEdgeView Edge, Node2D Panel)> _interiorEdges = new();
	private readonly List<(Node2D Panel, Rect2 Bounds)> _occludingWalls = new();

	private void BuildRoofLayer()
	{
		_roofs = new Node2D { ZIndex = -9 };
		AddChild(_roofs);
		var map = _view.Map!;
		for (var y = 0; y < map.Height; y++)
		for (var x = 0; x < map.Width; x++)
		{
			var cell = new GridPos(x, y);
			if (!IsIndoor(cell)) continue;
			// An opaque, continuous roof silhouette covers the floor of rooms outside current sight.
			// It is independent of the room's light: a dark interior does not make the exterior disappear.
			var tile = new Polygon2D
			{
				Polygon = Diamond(1.012f), Position = CellToScreen(cell),
				Color = RoofColor,
			};
			_roofs.AddChild(tile);
			_roofTiles[cell] = tile;
		}
		// One colour across all diamonds reads as a single roof. Only its outer contour is inked;
		// the seams between 5-foot squares are deliberately absent.
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
						RoofColor, RoofColor,
						RoofColor with { A = 0 }, RoofColor with { A = 0 },
					},
					Color = Colors.White,
				};
				_roofs.AddChild(blend);
				_roofBlends.Add((cell, nearby, blend));
			}
			else
			{
				var rim = new Line2D { Points = new[] { from, to }, Width = 2f,
					DefaultColor = RoofColor.Darkened(0.32f) };
				_roofs.AddChild(rim);
				_roofRims.Add((cell, rim));
			}
		}
	}

	private static (Vector2 From, Vector2 To) RoofEdge(GridPos cell, GridPos nearby)
	{
		var diamond = Diamond();
		var from = nearby.X > cell.X ? diamond[1] : nearby.X < cell.X ? diamond[3]
			: nearby.Y > cell.Y ? diamond[2] : diamond[0];
		var to = nearby.X > cell.X ? diamond[2] : nearby.X < cell.X ? diamond[0]
			: nearby.Y > cell.Y ? diamond[3] : diamond[1];
		var center = CellToScreen(cell);
		return (center + from, center + to);
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

	private void RefreshBuildingVisibility()
	{
		if (_view.Position is null || _view.MapVisibility is not { } visibility)
		{
			_roofs.Visible = false;
			return;
		}
		_roofs.Visible = true;
		var daylight = _view.Daylight switch { Light.Bright => 1f, Light.Dim => 0.72f, _ => 0.42f };
		foreach (var (cell, tile) in _roofTiles)
		{
			tile.Visible = visibility[cell.Y][cell.X] != '2';
			tile.Modulate = new Color(daylight, daylight, daylight);
		}
		foreach (var (covered, exposed, blend) in _roofBlends)
		{
			blend.Visible = visibility[covered.Y][covered.X] != '2' && visibility[exposed.Y][exposed.X] == '2';
			blend.Modulate = new Color(daylight, daylight, daylight);
		}
		foreach (var (cell, rim) in _roofRims)
		{
			rim.Visible = visibility[cell.Y][cell.X] != '2';
			rim.Modulate = new Color(daylight, daylight, daylight);
		}
		// The outside keeps its facade. Internal partitions are only drawn where the player can
		// currently see either side; remembered rooms must not leak their floor plan.
		foreach (var (edge, panel) in _interiorEdges)
		{
			var inSight = visibility[edge.A.Y][edge.A.X] == '2' || visibility[edge.B.Y][edge.B.X] == '2';
			var closedDoor = edge.DoorAt is not { } doorAt || _view.Doors.Any(d => d.At == doorAt
				&& d.Open == false && d.Known != DoorKnowledge.Unknown);
			panel.Visible = inSight && closedDoor;
		}
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
	internal bool InteriorEdgeVisibleForSmoke(GridPos a, GridPos b) =>
		_interiorEdges.Single(p => p.Edge.A == a && p.Edge.B == b).Panel.Visible;
}
