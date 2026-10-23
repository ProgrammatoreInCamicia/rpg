using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>G15 visual prototype. All directions and animation timing are presentation only.</summary>
public partial class VillageMap
{
	private readonly Dictionary<string, AtlasTexture[]> _figureAtlases = new();
	private readonly Dictionary<ActorId, int> _figureFacing = new();
	private AtlasTexture[]? _paladin3dFrames;
	private float _paladin3dWalkDistance;
	private Vector2 _paladin3dDirectionTravel;
	private int _paladin3dFacing = 1;
	private bool _paladin3dPrototype;
	private Paladin3DPrototype? _paladinLiveModel;
	private readonly Dictionary<ActorId, Paladin3DPrototype> _npcLiveModels = new();
	private readonly Dictionary<ActorId, float> _npcYaw = new();
	private readonly Dictionary<ActorId, float> _npcWalkDistance = new();
	private bool _paladinLive3d = true;
	private float _paladinLiveYaw;
	private float _paladinLiveTargetYaw;
	private readonly Dictionary<ActorId, double> _attackVisualUntil = new();
	private readonly Dictionary<ActorId, double> _attackVisualStarted = new();
	private readonly Dictionary<ActorId, double> _slashVisualUntil = new();
	private readonly Dictionary<ActorId, double> _hitVisualUntil = new();
	private int _seenCombatVisuals;
	private bool _observingLastFight;
	private Texture2D? _roadTexture;
	private Texture2D? _woodTexture;
	private Texture2D? _grassTexture;
	private Texture2D? _stoneWallTexture;
	private Texture2D? _pilotFlagstoneTexture;
	private static Texture2D? _pilotPlasterTexture;

	private void LoadGroundTextures()
	{
		_roadTexture = GD.Load<Texture2D>("res://Assets/G15/Ground/road.png");
		_woodTexture = GD.Load<Texture2D>("res://Assets/G15/Ground/wood.png");
		_grassTexture = GD.Load<Texture2D>("res://Assets/G15/Ground/grass.png");
		_stoneWallTexture = GD.Load<Texture2D>("res://Assets/G15/Ground/stone-wall.png");
		if (StyledInterior)
		{
			_pilotFlagstoneTexture = GD.Load<Texture2D>("res://Assets/StyleStudy/interior-flagstone.png");
			_pilotPlasterTexture = GD.Load<Texture2D>("res://Assets/StyleStudy/interior-plaster.png");
		}
	}

	private void ApplyGroundTexture(Polygon2D tile, char symbol, FloorLook look, GridPos cell, float shade)
	{
		if (symbol == '#')
			return;
		var texture = look.Material switch
		{
			GroundMaterial.Road => _roadTexture,
			GroundMaterial.Wood => _woodTexture,
			GroundMaterial.Flagstone => _pilotFlagstoneTexture,
			_ => _grassTexture,
		};
		if (texture is null)
			return;
		// Sample a different patch of the source on each square so the same stone or knot does not repeat in a grid.
		var radius = Math.Max(1, texture.GetWidth() * 90 / 1254);
		var x = radius + (cell.X * 101 + cell.Y * 59) % (texture.GetWidth() - 2 * radius);
		var y = radius + (cell.X * 47 + cell.Y * 113) % (texture.GetHeight() - 2 * radius);
		tile.Texture = texture;
		tile.UV = new[]
		{
			new Vector2(x, y - radius), new Vector2(x + radius, y),
			new Vector2(x, y + radius), new Vector2(x - radius, y),
		};
		tile.Color = look.Material == GroundMaterial.Flagstone
			? new Color(0.94f, 0.88f, 0.79f).Darkened(shade * 0.35f)
			: Colors.White.Darkened(shade * 0.5f);
	}

	private static void DressPilotFloor(Polygon2D tile)
	{
		var rug = Diamond(0.70f);
		tile.AddChild(new Polygon2D { Polygon = rug, Color = new Color(0.39f, 0.19f, 0.16f, 0.92f) });
		tile.AddChild(new Line2D
		{
			Points = new[] { rug[0], rug[1], rug[2], rug[3], rug[0] },
			Width = 2f, DefaultColor = new Color(0.67f, 0.43f, 0.24f), Antialiased = true,
		});
		var inset = Diamond(0.51f);
		tile.AddChild(new Line2D
		{
			Points = new[] { inset[0], inset[1], inset[2], inset[3], inset[0] },
			Width = 1f, DefaultColor = new Color(0.65f, 0.39f, 0.25f, 0.8f), Antialiased = true,
		});
	}

	private Node2D MakeEdgePanel(MapEdgeView edge, bool styledInterior)
	{
		var origin = CellToScreen(edge.A);
		var diamond = Diamond();
		var first = edge.B.X > edge.A.X ? diamond[1] : edge.B.X < edge.A.X ? diamond[3]
			: edge.B.Y > edge.A.Y ? diamond[2] : diamond[0];
		var second = edge.B.X > edge.A.X ? diamond[2] : edge.B.X < edge.A.X ? diamond[0]
			: edge.B.Y > edge.A.Y ? diamond[3] : diamond[1];
		var center = (first + second) / 2f;
		var node = new Node2D { Position = origin + center };
		first -= center;
		second -= center;
		if (styledInterior)
			return MakeStyledEdge(node, edge, first, second);
		var height = edge.Kind == MapEdgeKind.Window ? 17f : WallHeight;
		var color = edge.Kind == MapEdgeKind.Door ? DoorWood : Wall;
		node.AddChild(new Polygon2D
		{
			Name = edge.Kind == MapEdgeKind.Window ? "Glass" : "Panel",
			Polygon = new[] { first, second, second + Vector2.Up * height, first + Vector2.Up * height },
			Color = edge.Kind == MapEdgeKind.Window ? new Color(0.38f, 0.62f, 0.67f, 0.46f) : color,
		});
		var frame = new Line2D { Points = new[] { first + Vector2.Up * height, second + Vector2.Up * height },
			Width = edge.Kind == MapEdgeKind.Window ? 3f : 2f, DefaultColor = color.Lightened(0.18f) };
		node.AddChild(frame);
		if (edge.Kind == MapEdgeKind.Window)
		{
			node.AddChild(new Line2D { Points = new[] { first, second }, Width = 5f, DefaultColor = Wall });
			var shards = new Node2D { Name = "Shards", Visible = false };
			for (var i = 0; i < 4; i++)
			{
				var along = first.Lerp(second, (i + 0.5f) / 4f);
				shards.AddChild(new Line2D { Points = new[] { along, along + new Vector2(i % 2 == 0 ? -3 : 3, -7 - i % 2 * 3) },
					Width = 1.5f, DefaultColor = new Color(0.68f, 0.90f, 0.94f, 0.9f) });
			}
			node.AddChild(shards);
			var bars = new Node2D { Name = "Bars", Visible = edge.Barred == true };
			for (var i = 1; i <= 3; i++)
			{
				var along = first.Lerp(second, i / 4f);
				bars.AddChild(new Line2D { Points = new[] { along, along + Vector2.Up * height },
					Width = 2.3f, DefaultColor = new Color(0.27f, 0.24f, 0.20f) });
			}
			node.AddChild(bars);
			node.AddChild(new Line2D { Name = "OpenSash",
				Points = new[] { first + Vector2.Up * height, first + new Vector2(-12, -height - 9) },
				Width = 3f, DefaultColor = Wall.Lightened(0.2f), Visible = false });
		}
		return node;
	}

	private Node2D MakeStyledEdge(Node2D node, MapEdgeView edge, Vector2 first, Vector2 second)
	{
		var up = Vector2.Up * StyledWallHeight;
		var beam = new Color(0.22f, 0.13f, 0.08f);
		var litWood = new Color(0.50f, 0.32f, 0.18f);
		var stone = new Color(0.70f, 0.67f, 0.60f);
		var depth = (CellToScreen(edge.B) - CellToScreen(edge.A)).Normalized() * 8f;

		void Stroke(Node2D parent, Vector2 a, Vector2 b, float width, Color color) =>
			parent.AddChild(new Line2D { Points = new[] { a, b }, Width = width,
				DefaultColor = color, Antialiased = true });
		void Plaster(Polygon2D face)
		{
			face.Color = new Color(0.84f, 0.78f, 0.68f);
			if (_pilotPlasterTexture is null) return;
			var u = 40 + (edge.A.X * 97 + edge.A.Y * 53) % 900;
			var v = 60 + (edge.A.X * 31 + edge.A.Y * 79) % 900;
			face.Texture = _pilotPlasterTexture;
			// A wider texture sample keeps the exposed stones legible at 32 screen pixels per segment.
			face.UV = new[] { new Vector2(u, v + 120), new Vector2(u + 190, v + 120),
				new Vector2(u + 190, v), new Vector2(u, v) };
		}
		void WallSurface()
		{
			var face = new Polygon2D { Name = "Panel",
				Polygon = new[] { first, second, second + up, first + up } };
			Plaster(face);
			node.AddChild(face);
			var baseFace = new Polygon2D
			{
				Polygon = new[] { first, second, second + Vector2.Up * 14f, first + Vector2.Up * 14f },
				Color = stone.Lightened(0.16f),
			};
			if (_stoneWallTexture is not null)
			{
				var u = 18 + (edge.A.X * 83 + edge.A.Y * 47) % Math.Max(1, _stoneWallTexture.GetWidth() - 90);
				var v = 18 + (edge.A.X * 29 + edge.A.Y * 61) % Math.Max(1, _stoneWallTexture.GetHeight() - 40);
				baseFace.Texture = _stoneWallTexture;
				baseFace.UV = new[] { new Vector2(u, v + 65), new Vector2(u + 160, v + 65),
					new Vector2(u + 160, v), new Vector2(u, v) };
			}
			node.AddChild(baseFace);
			Stroke(node, first + Vector2.Up * 14f, second + Vector2.Up * 14f, 2f, new Color(0.33f, 0.29f, 0.25f));
			Stroke(node, first + Vector2.Up * 2f, second + Vector2.Up * 2f, 2f, new Color(0.35f, 0.32f, 0.29f));
		}
		void Timbers()
		{
			// Recessed plaster, a stone foot, and broad timber members follow the painted reference.
			Stroke(node, first + up + Vector2.Down * 5f, second + up + Vector2.Down * 5f, 7f, beam);
			Stroke(node, first + up + Vector2.Down * 7f, second + up + Vector2.Down * 7f, 1.6f, litWood);
			var cap = new Polygon2D { Polygon = new[] { first + up, second + up,
				second + up + depth, first + up + depth }, Color = new Color(0.34f, 0.21f, 0.12f) };
			node.AddChild(cap);
			Stroke(node, first + up + depth, second + up + depth, 2f, litWood);
			var firstIndex = edge.B.X != edge.A.X ? edge.A.Y : edge.A.X;
			for (var i = 0; i < 2; i++)
			{
				if (edge.Kind == MapEdgeKind.Wall && (firstIndex + i) % 2 != 0) continue;
				var end = i == 0 ? first : second;
				Stroke(node, end, end + up, 7f, beam);
				Stroke(node, end + new Vector2(1f, 0), end + up + new Vector2(1f, 0), 1.6f, litWood);
				node.AddChild(new Polygon2D { Polygon = new[] { end + up + new Vector2(-3, 0),
					end + up + new Vector2(3, 0), end + up + depth + new Vector2(3, 0),
					end + up + depth + new Vector2(-3, 0) }, Color = beam.Lightened(0.08f) });
			}
		}

		if (edge.Kind == MapEdgeKind.Wall)
		{
			WallSurface();
			Timbers();
			if (edge.A == new GridPos(12, 5) && edge.B == new GridPos(13, 5))
			{
				var mount = first.Lerp(second, 0.5f) + Vector2.Up * 18f;
				Stroke(node, mount, mount + Vector2.Down * 8f, 3f, beam);
				node.AddChild(new Polygon2D { Polygon = new[] { mount + new Vector2(0, -7),
					mount + new Vector2(4, 0), mount + new Vector2(0, 2), mount + new Vector2(-4, 0) },
					Color = new Color(1f, 0.73f, 0.31f) });
			}
			if (edge.A == new GridPos(6, 3) && edge.B == new GridPos(6, 4))
			{
				var left = first.Lerp(second, 0.16f) + Vector2.Up * 18f;
				var right = first.Lerp(second, 0.84f) + Vector2.Up * 18f;
				Stroke(node, left, right, 4f, litWood);
				for (var i = 0; i < 2; i++)
				{
					var middle = left.Lerp(right, (i + 1f) / 3f);
					node.AddChild(new Polygon2D { Polygon = new[] { middle + new Vector2(-3, 0),
						middle + new Vector2(3, 0), middle + new Vector2(2, -8), middle + new Vector2(-2, -8) },
						Color = new Color(0.44f, 0.29f, 0.19f) });
				}
			}
			return node;
		}

		if (edge.Kind == MapEdgeKind.Window)
		{
			WallSurface();
			var left = first.Lerp(second, 0.20f);
			var right = first.Lerp(second, 0.80f);
			var low = Vector2.Up * 15f;
			var high = Vector2.Up * 34f;
			// Deep dark reveal around a small, four-pane view, with outward shutters.
			node.AddChild(new Polygon2D { Polygon = new[] { left + low, right + low,
				right + high, left + high }, Color = new Color(0.13f, 0.11f, 0.09f) });
			var paneLeft = first.Lerp(second, 0.26f);
			var paneRight = first.Lerp(second, 0.74f);
			var paneLow = Vector2.Up * 17f;
			var paneHigh = Vector2.Up * 32f;
			node.AddChild(new Polygon2D { Name = "Glass",
				Polygon = new[] { paneLeft + paneLow, paneRight + paneLow,
					paneRight + paneHigh, paneLeft + paneHigh },
				Color = new Color(0.30f, 0.48f, 0.37f) });
			var horizon = paneLeft.Lerp(paneRight, 0.50f);
			Stroke(node, paneLeft + Vector2.Up * 23f, paneRight + Vector2.Up * 23f, 1f,
				new Color(0.69f, 0.76f, 0.48f, 0.65f));
			Stroke(node, horizon + paneLow, horizon + paneHigh, 2f, litWood);
			Stroke(node, paneLeft + Vector2.Up * 24f, paneRight + Vector2.Up * 24f, 2f, litWood);
			foreach (var end in new[] { left, right })
				Stroke(node, end + low, end + high, 4f, beam);
			Stroke(node, left + high, right + high, 4f, beam);
			var sillA = first.Lerp(second, 0.15f) + Vector2.Up * 14f;
			var sillB = first.Lerp(second, 0.85f) + Vector2.Up * 14f;
			node.AddChild(new Polygon2D { Polygon = new[] { sillA, sillB,
				sillB + depth * -0.55f, sillA + depth * -0.55f }, Color = stone.Lightened(0.16f) });
			Stroke(node, sillA, sillB, 3f, new Color(0.43f, 0.39f, 0.32f));
			foreach (var shutter in new[] { (From: first.Lerp(second, 0.04f), To: left),
				(From: right, To: first.Lerp(second, 0.96f)) })
			{
				node.AddChild(new Polygon2D { Polygon = new[] { shutter.From + low,
					shutter.To + low, shutter.To + high, shutter.From + high }, Color = beam });
				for (var i = 0; i < 3; i++)
					Stroke(node, shutter.From.Lerp(shutter.To, 0.20f) + Vector2.Up * (19 + i * 5),
						shutter.From.Lerp(shutter.To, 0.80f) + Vector2.Up * (19 + i * 5), 1.5f, litWood);
			}
			Timbers();
			var shards = new Node2D { Name = "Shards", Visible = false };
			for (var i = 0; i < 4; i++)
			{
				var along = paneLeft.Lerp(paneRight, (i + 0.5f) / 4f);
				Stroke(shards, along + paneLow, along + paneLow + new Vector2(i % 2 == 0 ? -3 : 3, -6 - i % 2 * 3),
					1.3f, new Color(0.76f, 0.91f, 0.88f));
			}
			node.AddChild(shards);
			var bars = new Node2D { Name = "Bars", Visible = edge.Barred == true };
			for (var i = 1; i <= 3; i++)
			{
				var along = paneLeft.Lerp(paneRight, i / 4f);
				Stroke(bars, along + paneLow, along + paneHigh, 1.8f, new Color(0.25f, 0.24f, 0.20f));
			}
			node.AddChild(bars);
			node.AddChild(new Line2D { Name = "OpenSash", Visible = false,
				Points = new[] { paneLeft + paneHigh, paneLeft + paneHigh + new Vector2(-9, -4),
					paneLeft + paneLow + new Vector2(-9, -4), paneLeft + paneLow },
				Width = 2f, DefaultColor = litWood, Antialiased = true });
			return node;
		}

		// The frame stays in place; the leaf pivots into the room instead of vanishing.
		Stroke(node, first + up, second + up, 8f, beam);
		Stroke(node, first + up + Vector2.Down * 3f, second + up + Vector2.Down * 3f, 2f, litWood);
		foreach (var end in new[] { first, second })
		{
			Stroke(node, end, end + up, 7f, beam);
			Stroke(node, end + new Vector2(1, 0), end + up + new Vector2(1, 0), 1.5f, litWood);
		}
		var closed = new Node2D { Name = "ClosedLeaf" };
		var open = new Node2D { Name = "OpenLeaf", Visible = false };
		var swing = (CellToScreen(edge.A) - CellToScreen(edge.B)).Normalized() * 23f;
		DoorLeaf(closed, first, second, "Panel");
		DoorLeaf(open, first, first + swing, "OpenPanel");
		node.AddChild(closed);
		node.AddChild(open);
		return node;

		void DoorLeaf(Node2D parent, Vector2 a, Vector2 b, string name)
		{
			parent.AddChild(new Polygon2D { Name = name,
				Polygon = new[] { a + Vector2.Up * 2f, b + Vector2.Up * 2f,
					b + up + Vector2.Down * 4f, a + up + Vector2.Down * 4f },
				Color = new Color(0.38f, 0.22f, 0.12f) });
			for (var i = 1; i <= 3; i++)
			{
				var along = a.Lerp(b, i / 4f);
				Stroke(parent, along + Vector2.Up * 3f, along + up + Vector2.Down * 5f, 1.4f, beam);
			}
			Stroke(parent, a + Vector2.Up * 9f, b + Vector2.Up * 9f, 3f, litWood);
			Stroke(parent, a + up + Vector2.Down * 10f, b + up + Vector2.Down * 10f, 3f, litWood);
			Stroke(parent, a + Vector2.Up * 10f, b + up + Vector2.Down * 11f, 1.5f, beam);
			var latch = a.Lerp(b, 0.78f) + Vector2.Up * 21f;
			parent.AddChild(new Polygon2D { Polygon = new[] { latch + new Vector2(-2, 0),
				latch + new Vector2(0, -2), latch + new Vector2(2, 0), latch + new Vector2(0, 2) },
				Color = new Color(0.81f, 0.66f, 0.38f) });
		}
	}

	private static void SetStyledDoorPanel(Node2D panel, bool open)
	{
		panel.GetNode<Node2D>("ClosedLeaf").Visible = !open;
		panel.GetNode<Node2D>("OpenLeaf").Visible = open;
	}

	internal bool PilotDoorOpenForSmoke(GridPos at) =>
		_doorPanels[at].Visible && _doorPanels[at].GetNode<Node2D>("OpenLeaf").Visible;

	internal void ClickPilotOpenDoorForSmoke(GridPos at)
	{
		var panel = _doorPanels[at];
		var leaf = panel.GetNode<Node2D>("OpenLeaf").GetNode<Polygon2D>("OpenPanel");
		var point = panel.Position + leaf.Polygon.Aggregate(Vector2.Zero, (sum, vertex) => sum + vertex)
			/ leaf.Polygon.Length;
		_UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
			Position = GetGlobalTransformWithCanvas() * point });
	}

	private static void SetWindowPanel(Node2D panel, MapEdgeView edge)
	{
		panel.GetNode<Polygon2D>("Glass").Visible = edge.Open == false && edge.Glass == WindowGlass.Intact;
		panel.GetNode<Node2D>("Shards").Visible = edge.Glass == WindowGlass.Broken;
		panel.GetNode<Node2D>("Bars").Visible = edge.Barred == true;
		panel.GetNode<Line2D>("OpenSash").Visible = edge.Open == true && edge.Glass == WindowGlass.Intact;
	}

	private MapEdgeView? WindowAt(Vector2 point)
	{
		if (_view.Map is not { } map)
			return null;
		foreach (var edge in map.Edges.Where(e => e.Kind == MapEdgeKind.Window))
		{
			if (_view.MapVisibility is { } visibility
				&& visibility[edge.A.Y][edge.A.X] != '2' && visibility[edge.B.Y][edge.B.X] != '2')
				continue;
			if (!_windowPanels.TryGetValue((edge.A, edge.B), out var panel))
				continue;
			var local = point - panel.Position;
			var glass = panel.GetNode<Polygon2D>("Glass");
			if (StyledInterior)
			{
				var min = new Vector2(glass.Polygon.Min(p => p.X) - 10f,
					glass.Polygon.Min(p => p.Y) - 6f);
				var max = new Vector2(glass.Polygon.Max(p => p.X) + 10f,
					glass.Polygon.Max(p => p.Y) + 6f);
				if (new Rect2(min, max - min).HasPoint(local))
					return edge;
			}
			if (Geometry2D.IsPointInPolygon(local, glass.Polygon)
				|| DistanceToSegment(local, glass.Polygon[0], glass.Polygon[1]) <= 7f
				|| DistanceToSegment(local, glass.Polygon[2], glass.Polygon[3]) <= 7f)
				return edge;
		}
		return null;
	}

	private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
	{
		var line = to - from;
		var fraction = line.LengthSquared() == 0 ? 0 : Mathf.Clamp((point - from).Dot(line) / line.LengthSquared(), 0, 1);
		return point.DistanceTo(from + line * fraction);
	}

	internal void ClickWindowForSmoke(GridPos a, GridPos b)
	{
		var panel = _windowPanels[(a, b)];
		var glass = panel.GetNode<Polygon2D>("Glass").Polygon;
		var at = panel.Position + glass.Aggregate(Vector2.Zero, (sum, point) => sum + point) / glass.Length;
		_UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
			Position = GetGlobalTransformWithCanvas() * at });
	}

	internal void ClickWindowFrameForSmoke(GridPos a, GridPos b)
	{
		var panel = _windowPanels[(a, b)];
		var frame = panel.GetNode<Polygon2D>("Glass").Polygon[0] + new Vector2(-5, -2);
		_UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
			Position = GetGlobalTransformWithCanvas() * (panel.Position + frame) });
	}

	internal void RightClickWindowForSmoke(GridPos a, GridPos b)
	{
		var panel = _windowPanels[(a, b)];
		var glass = panel.GetNode<Polygon2D>("Glass").Polygon;
		var at = panel.Position + glass.Aggregate(Vector2.Zero, (sum, point) => sum + point) / glass.Length;
		_UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true,
			Position = GetGlobalTransformWithCanvas() * at });
	}

	internal bool WindowPanelOpenForSmoke(GridPos a, GridPos b) =>
		_windowPanels[(a, b)].GetNode<Line2D>("OpenSash").Visible;
	internal bool WindowShardsForSmoke(GridPos a, GridPos b) =>
		_windowPanels[(a, b)].GetNode<Node2D>("Shards").Visible;
	internal bool WindowBarsForSmoke(GridPos a, GridPos b) =>
		_windowPanels[(a, b)].GetNode<Node2D>("Bars").Visible;

	private string? FigureAsset(ActorId id) => id == _sim.Player
		? "paladin" : ActorStyle(id) switch
		{
			Paladin3DPrototype.CharacterStyle.Guard => "guard",
			Paladin3DPrototype.CharacterStyle.Farmer => "farmer",
			_ => null,
		};

	private void AddFigureSprite(Node2D token, ActorId id)
	{
		var asset = FigureAsset(id);
		AtlasTexture[]? directions = null;
		if (asset is not null && !_figureAtlases.TryGetValue(asset, out directions))
		{
			var source = GD.Load<Texture2D>($"res://Assets/G15/Characters/{asset}-directions.png");
			if (source is not null)
			{
				var width = source.GetWidth() / 2;
				var height = source.GetHeight() / 2;
				directions = Enumerable.Range(0, 4).Select(i => new AtlasTexture
				{
					Atlas = source,
					Region = new Rect2((i % 2) * width, (i / 2) * height, width, height),
				}).ToArray();
				_figureAtlases[asset] = directions;
			}
		}
		token.AddChild(new Sprite2D
		{
			Name = "Figure",
			Texture = directions?[0],
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
			Scale = Vector2.One * (directions is null ? 0.32f : 50f / directions[0].GetWidth()),
			Offset = directions is null ? new Vector2(0, -64) : new Vector2(0, -directions[0].GetHeight() * 0.40f),
		});
		_figureFacing[id] = 0;
	}

	private void UpdateFigureSprite(Node2D token, ActorId id, Vector2 previousPosition, bool moving, bool prone,
		MoveView? walk = null)
	{
		if (token.GetNodeOrNull<Sprite2D>("Figure") is not { } sprite)
			return;
		token.GetNode<Polygon2D>("Body").Visible = false;
		token.GetNode<Polygon2D>("Head").Visible = false;
		token.GetNode<Polygon2D>("Prone").Visible = false;
		var delta = token.Position - previousPosition;
		var asset = FigureAsset(id);
		var live = id == _sim.Player && _paladinLive3d && EnsurePaladinLiveModel() is { };
		var liveNpc = id != _sim.Player;
		var prototype = id == _sim.Player && _paladin3dPrototype && LoadPaladin3dFrames() is { };
		var plannedTangent = (prototype || live || liveNpc) && moving ? WalkTangent(walk, token.Position) : null;
		if (plannedTangent is { } tangent)
		{
			if (prototype) _paladin3dFacing = PrototypeFacing(tangent);
			if (live) _paladinLiveTargetYaw = LiveYaw(tangent);
			if (liveNpc) _npcYaw[id] = LiveYaw(tangent);
			_paladin3dDirectionTravel = Vector2.Zero;
		}
		if (moving && delta.LengthSquared() is > 0.01f and < 10000f)
		{
			_figureFacing[id] = delta.Y >= 0 ? (delta.X < 0 ? 0 : 1) : (delta.X < 0 ? 2 : 3);
			if (prototype || live)
			{
				if (plannedTangent is null)
				{
					_paladin3dDirectionTravel += delta;
					if (_paladin3dDirectionTravel.LengthSquared() >= 4f)
					{
						if (prototype) _paladin3dFacing = PrototypeFacing(_paladin3dDirectionTravel);
						if (live) _paladinLiveTargetYaw = LiveYaw(_paladin3dDirectionTravel);
						_paladin3dDirectionTravel = Vector2.Zero;
					}
				}
				_paladin3dWalkDistance += delta.Length();
			}
			if (liveNpc)
			{
				if (plannedTangent is null) _npcYaw[id] = LiveYaw(delta);
				_npcWalkDistance[id] = _npcWalkDistance.GetValueOrDefault(id) + delta.Length();
			}
		}
		if (!moving)
			_paladin3dDirectionTravel = Vector2.Zero;
		var now = Time.GetTicksMsec() / 1000.0;
		var attacking = _attackVisualUntil.GetValueOrDefault(id) > now;
		var attackProgress = _slashVisualUntil.GetValueOrDefault(id) > now
			? (float)Math.Clamp((now - _attackVisualStarted.GetValueOrDefault(id)) / 0.42, 0, 1) : 0f;
		if (live)
		{
			var weight = 1f - Mathf.Exp(-12f * (float)GetProcessDeltaTime());
			_paladinLiveYaw = Mathf.LerpAngle(_paladinLiveYaw, _paladinLiveTargetYaw, weight);
			_paladinLiveModel!.PoseLive(_paladinLiveYaw, moving ? _paladin3dWalkDistance / 40f : 0f, attackProgress);
		}
		var npcModel = liveNpc ? EnsureNpcLiveModel(id) : null;
		if (npcModel is not null)
			npcModel.PoseLive(_npcYaw.GetValueOrDefault(id), moving ? _npcWalkDistance.GetValueOrDefault(id) / 40f : 0f,
				attackProgress);
		var facing = _figureFacing.GetValueOrDefault(id);
		Texture2D? frame = live ? _paladinLiveModel!.LiveTexture : npcModel is not null ? npcModel.LiveTexture : prototype
			? _paladin3dFrames![_paladin3dFacing * 8 + (moving ? (int)(_paladin3dWalkDistance / 5f) % 8 : 0)]
			: asset is not null && _figureAtlases.TryGetValue(asset, out var frames) ? frames[facing] : null;
		if (sprite.Texture != frame)
			sprite.Texture = frame;
		sprite.Scale = Vector2.One * (prototype || live || liveNpc || frame is null ? 0.32f : 50f / frame.GetWidth());
		sprite.Offset = prototype || live || liveNpc || frame is null ? new Vector2(0, -64) : new Vector2(0, -frame.GetHeight() * 0.40f);
		var hit = _hitVisualUntil.GetValueOrDefault(id) > now;
		var bob = !moving || prone || prototype || live || liveNpc ? 0f : MathF.Sin((float)(now * 13)) * 0.6f;
		sprite.Position = prone ? new Vector2(0, -5) : new Vector2(attacking ? (facing is 0 or 2 ? -3 : 3) : 0, bob);
		sprite.RotationDegrees = prone ? -78f : live || liveNpc ? 0f : attacking ? (facing is 0 or 2 ? -8f : 8f) : 0f;
		sprite.Modulate = hit ? new Color(1f, 0.52f, 0.48f) : Colors.White;
		var slash = token.GetNode<Line2D>("Slash");
		slash.Visible = !prone && _slashVisualUntil.GetValueOrDefault(id) > now;
		if (slash.Visible)
		{
			var sweep = Mathf.Clamp(attackProgress, 0f, 1f);
			var side = facing is 0 or 2 ? -1f : 1f;
			slash.Points = new[]
			{
				new Vector2(side * (-15 + 10 * sweep), -43 + 8 * sweep),
				new Vector2(side * (-5 + 15 * sweep), -48 + 9 * sweep),
				new Vector2(side * (7 + 15 * sweep), -43 + 12 * sweep),
				new Vector2(side * (17 + 12 * sweep), -31 + 13 * sweep),
			};
			slash.Modulate = slash.Modulate with { A = MathF.Sin(MathF.PI * sweep) };
		}
	}

	private AtlasTexture[]? LoadPaladin3dFrames()
	{
		if (_paladin3dFrames is not null)
			return _paladin3dFrames;
		var source = GD.Load<Texture2D>("res://Assets/G15/Characters/paladin-3d-walk-8x8.png");
		if (source is null || source.GetWidth() != 1536 || source.GetHeight() != 1536)
			return null;
		_paladin3dFrames = Enumerable.Range(0, 64).Select(i => new AtlasTexture
		{
			Atlas = source,
			Region = new Rect2(i % 8 * 192, i / 8 * 192, 192, 192),
			FilterClip = true,
		}).ToArray();
		return _paladin3dFrames;
	}

	private static Vector2? WalkTangent(MoveView? walk, Vector2 at)
	{
		if (walk?.Vertices is not { Count: > 1 } vertices)
			return null;
		var nearest = float.PositiveInfinity;
		Vector2? tangent = null;
		for (var i = 1; i < vertices.Count; i++)
		{
			var from = SpotToScreen(vertices[i - 1]);
			var to = SpotToScreen(vertices[i]);
			var leg = to - from;
			if (leg.LengthSquared() < 0.01f)
				continue;
			var fraction = Mathf.Clamp((at - from).Dot(leg) / leg.LengthSquared(), 0f, 1f);
			var distance = at.DistanceSquaredTo(from + leg * fraction);
			if (distance >= nearest)
				continue;
			nearest = distance;
			tangent = leg;
		}
		return tangent;
	}

	private static int PrototypeFacing(Vector2 travel)
	{
		var normalized = travel.Normalized();
		var best = float.NegativeInfinity;
		var facing = 0;
		for (var row = 0; row < 8; row++)
		{
			var yaw = row * Mathf.Pi / 4f;
			// The render camera is fixed at (3.6, 2.9, 4.2); these are the screen axes of its ground plane.
			var projected = new Vector2(0.759f * Mathf.Sin(yaw) - 0.651f * Mathf.Cos(yaw),
				0.249f * Mathf.Sin(yaw) + 0.291f * Mathf.Cos(yaw)).Normalized();
			var score = normalized.Dot(projected);
			if (score <= best)
				continue;
			best = score;
			facing = row;
		}
		return facing;
	}

	/// <summary>Invert the fixed orthographic camera's ground-plane projection, retaining a continuous 3D heading.</summary>
	private static float LiveYaw(Vector2 travel)
	{
		var sin = 0.291f * travel.X + 0.651f * travel.Y;
		var cos = -0.249f * travel.X + 0.759f * travel.Y;
		return Mathf.Atan2(sin, cos);
	}

	private Paladin3DPrototype EnsurePaladinLiveModel()
	{
		if (_paladinLiveModel is not null)
			return _paladinLiveModel;
		_paladinLiveModel = new Paladin3DPrototype { Name = "LivePaladinRenderer", LiveMode = true };
		AddChild(_paladinLiveModel);
		return _paladinLiveModel;
	}

	private Paladin3DPrototype EnsureNpcLiveModel(ActorId id)
	{
		if (_npcLiveModels.TryGetValue(id, out var model)) return model;
		var style = ActorStyle(id);
		model = new Paladin3DPrototype { Name = $"LiveFigure_{id.Value}", LiveMode = true, Style = style };
		AddChild(model);
		_npcLiveModels[id] = model;
		return model;
	}

	private void TogglePaladinLive3dPrototype()
	{
		_paladinLive3d = !_paladinLive3d;
		if (_paladinLive3d)
		{
			_paladin3dPrototype = false;
			_paladinLiveYaw = _paladinLiveTargetYaw = _paladin3dFacing * Mathf.Pi / 4f;
			EnsurePaladinLiveModel().SetLiveRendering(true);
		}
		else
			_paladinLiveModel?.SetLiveRendering(false);
		_paladin3dWalkDistance = 0;
		_paladin3dDirectionTravel = Vector2.Zero;
		ShowMessage(_paladinLive3d ? "Paladino 3D in tempo reale attivo."
			: "Sprite dipinta di confronto attiva (O per tornare al 3D).");
		PlaceTokens();
	}

	private void TogglePaladin3dPrototype()
	{
		_paladin3dPrototype = !_paladin3dPrototype;
		if (_paladin3dPrototype)
		{
			_paladinLive3d = false;
			_paladinLiveModel?.SetLiveRendering(false);
		}
		else
		{
			_paladinLive3d = true;
			EnsurePaladinLiveModel().SetLiveRendering(true);
		}
		_paladin3dWalkDistance = 0;
		_paladin3dDirectionTravel = Vector2.Zero;
		ShowMessage(_paladin3dPrototype ? "Atlante 3D di confronto attivo (P per tornare al 3D live)."
			: "Paladino 3D in tempo reale ripristinato.");
		PlaceTokens();
	}

	internal void TogglePaladin3dPrototypeForSmoke() => TogglePaladin3dPrototype();
	internal void TogglePaladinLive3dPrototypeForSmoke() => TogglePaladinLive3dPrototype();
	internal bool PaladinLive3dLoadedForSmoke => _paladinLive3d && _paladinLiveModel is not null
		&& _tokens.TryGetValue(_sim.Player, out var token)
		&& token.GetNodeOrNull<Sprite2D>("Figure") is { Texture: { } texture }
		&& texture == _paladinLiveModel.LiveTexture;
	internal bool NpcLive3dLoadedForSmoke(ActorId id) => _npcLiveModels.TryGetValue(id, out var model)
		&& _tokens.TryGetValue(id, out var token)
		&& token.GetNodeOrNull<Sprite2D>("Figure") is { Texture: { } texture }
		&& texture == model.LiveTexture;
	internal bool AttackSwingForSmoke(ActorId id) => id == _view.Id
		? _paladinLiveModel?.Swinging == true : _npcLiveModels.TryGetValue(id, out var model) && model.Swinging;
	internal bool Paladin3dLoadedForSmoke => _paladin3dFrames?.Length == 64
		&& _tokens.TryGetValue(_sim.Player, out var token)
		&& token.GetNodeOrNull<Sprite2D>("Figure") is { Texture: { } texture }
		&& _paladin3dFrames.Contains(texture);

	private void ObserveCombatVisuals()
	{
		var lastFight = _view.Combat is null && _view.LastFight is not null;
		var log = _view.Combat?.Log ?? _view.LastFight;
		if (log is null)
		{
			_seenCombatVisuals = 0;
			_observingLastFight = false;
			return;
		}
		// Combat.Log becomes LastFight with its old entries intact plus the final ones.
		// A fresh Combat after LastFight instead begins a new register.
		if ((!lastFight && _observingLastFight) || log.Count < _seenCombatVisuals)
			_seenCombatVisuals = 0;
		var now = Time.GetTicksMsec() / 1000.0;
		var lastNpcMoves = new Dictionary<ActorId, CombatLogEntry>();
		foreach (var entry in log.Skip(_seenCombatVisuals))
		{
			if (entry.Kind == CombatLogKind.Move && entry.Actor is { } mover && mover != _view.Id)
				lastNpcMoves[mover] = entry;
			if (entry.Kind == CombatLogKind.Mastery && entry.Spots.Count >= 2 && entry.Target is { } pushed)
				StartCombatVisual(pushed, entry.Spots);
			if (entry.Kind is not (CombatLogKind.Attack or CombatLogKind.OpportunityAttack))
				continue;
			if (entry.Actor is { } attacker)
			{
				_attackVisualStarted[attacker] = now;
				_attackVisualUntil[attacker] = now + 0.42;
				if (entry.ShotFrom is { } from && entry.ShotTo is { } to)
					StartShotVisual(from, to);
				else
					_slashVisualUntil[attacker] = now + 0.42;
			}
			if (entry.Hit == true && entry.Target is { } target)
				_hitVisualUntil[target] = now + 0.35;
		}
		foreach (var (actor, move) in lastNpcMoves)
		{
			if (move.Spots.Count < 2)
				continue;
			var visible = _view.VisibleActors.FirstOrDefault(a => a.Id == actor);
			// A later unseen movement may have changed the actor's current position.
			if (visible is not null && visible.Spot != move.Spots[^1])
				continue;
			StartCombatVisual(actor, move.Spots);
		}
		_seenCombatVisuals = log.Count;
		_observingLastFight = lastFight;
	}

	internal bool HasFigureSpriteForSmoke(ActorId id) => _tokens.TryGetValue(id, out var token)
		&& token.GetNodeOrNull<Sprite2D>("Figure") is { Texture: not null };

	internal bool AttackVisualActiveForSmoke(ActorId id) => _attackVisualUntil.GetValueOrDefault(id)
		> Time.GetTicksMsec() / 1000.0;
}
