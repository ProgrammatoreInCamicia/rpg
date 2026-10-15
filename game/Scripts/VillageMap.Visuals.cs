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
	private readonly Dictionary<ActorId, double> _attackVisualUntil = new();
	private readonly Dictionary<ActorId, double> _hitVisualUntil = new();
	private int _seenCombatVisuals;
	private bool _observingLastFight;
	private Texture2D? _roadTexture;
	private Texture2D? _woodTexture;
	private Texture2D? _grassTexture;
	private Texture2D? _stoneWallTexture;

	private void LoadGroundTextures()
	{
		_roadTexture = GD.Load<Texture2D>("res://Assets/G15/Ground/road.png");
		_woodTexture = GD.Load<Texture2D>("res://Assets/G15/Ground/wood.png");
		_grassTexture = GD.Load<Texture2D>("res://Assets/G15/Ground/grass.png");
		_stoneWallTexture = GD.Load<Texture2D>("res://Assets/G15/Ground/stone-wall.png");
	}

	private void ApplyGroundTexture(Polygon2D tile, char symbol, LocationId? zone, GridPos cell, float shade)
	{
		if (symbol == '#')
			return;
		var texture = symbol == '.' ? _roadTexture
			: zone == MappedVillageScenario.Ids.Inn || zone == MappedVillageScenario.Ids.FarmerHouse ? _woodTexture
			: _grassTexture;
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
		tile.Color = Colors.White.Darkened(shade * 0.5f);
	}

	private static Node2D MakeEdgePanel(MapEdgeView edge)
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
			node.AddChild(new Line2D { Name = "OpenSash",
				Points = new[] { first + Vector2.Up * height, first + new Vector2(-12, -height - 9) },
				Width = 3f, DefaultColor = Wall.Lightened(0.2f), Visible = false });
		}
		return node;
	}

	private static void SetWindowPanelOpen(Node2D panel, bool open)
	{
		panel.GetNode<Polygon2D>("Glass").Visible = !open;
		panel.GetNode<Line2D>("OpenSash").Visible = open;
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

	internal bool WindowPanelOpenForSmoke(GridPos a, GridPos b) =>
		!_windowPanels[(a, b)].GetNode<Polygon2D>("Glass").Visible;

	private static string? FigureAsset(ActorId id) => id == MappedVillageScenario.Ids.Player
		? "paladin" : id == MappedVillageScenario.Ids.Guard ? "guard"
		: id == MappedVillageScenario.Ids.Farmer ? "farmer" : null;

	private void AddFigureSprite(Node2D token, ActorId id)
	{
		var asset = FigureAsset(id);
		if (asset is null)
			return;
		if (!_figureAtlases.TryGetValue(asset, out var directions))
		{
			var source = GD.Load<Texture2D>($"res://Assets/G15/Characters/{asset}-directions.png");
			if (source is null)
				return;
			var width = source.GetWidth() / 2;
			var height = source.GetHeight() / 2;
			directions = Enumerable.Range(0, 4).Select(i => new AtlasTexture
			{
				Atlas = source,
				Region = new Rect2((i % 2) * width, (i / 2) * height, width, height),
			}).ToArray();
			_figureAtlases[asset] = directions;
		}
		token.AddChild(new Sprite2D
		{
			Name = "Figure",
			Texture = directions[0],
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
			Scale = Vector2.One * (50f / directions[0].GetWidth()),
			Offset = new Vector2(0, -directions[0].GetHeight() * 0.40f),
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
		var asset = FigureAsset(id)!;
		var prototype = id == MappedVillageScenario.Ids.Player && _paladin3dPrototype && LoadPaladin3dFrames() is { };
		var plannedTangent = prototype && moving ? WalkTangent(walk, token.Position) : null;
		if (plannedTangent is { } tangent)
		{
			_paladin3dFacing = PrototypeFacing(tangent);
			_paladin3dDirectionTravel = Vector2.Zero;
		}
		if (moving && delta.LengthSquared() is > 0.01f and < 10000f)
		{
			_figureFacing[id] = delta.Y >= 0 ? (delta.X < 0 ? 0 : 1) : (delta.X < 0 ? 2 : 3);
			if (prototype)
			{
				if (plannedTangent is null)
				{
					_paladin3dDirectionTravel += delta;
					if (_paladin3dDirectionTravel.LengthSquared() >= 4f)
					{
						_paladin3dFacing = PrototypeFacing(_paladin3dDirectionTravel);
						_paladin3dDirectionTravel = Vector2.Zero;
					}
				}
				_paladin3dWalkDistance += delta.Length();
			}
		}
		if (!moving)
			_paladin3dDirectionTravel = Vector2.Zero;
		var facing = _figureFacing.GetValueOrDefault(id);
		var frame = prototype
			? _paladin3dFrames![_paladin3dFacing * 8 + (moving ? (int)(_paladin3dWalkDistance / 5f) % 8 : 0)]
			: _figureAtlases[asset][facing];
		if (sprite.Texture != frame)
			sprite.Texture = frame;
		sprite.Scale = Vector2.One * (prototype ? 0.32f : 50f / _figureAtlases[asset][0].GetWidth());
		sprite.Offset = prototype ? new Vector2(0, -64) : new Vector2(0, -_figureAtlases[asset][0].GetHeight() * 0.40f);
		var now = Time.GetTicksMsec() / 1000.0;
		var attacking = _attackVisualUntil.GetValueOrDefault(id) > now;
		var hit = _hitVisualUntil.GetValueOrDefault(id) > now;
		var bob = !moving || prone || prototype ? 0f : MathF.Sin((float)(now * 13)) * 0.6f;
		sprite.Position = prone ? new Vector2(0, -5) : new Vector2(attacking ? (facing is 0 or 2 ? -3 : 3) : 0, bob);
		sprite.RotationDegrees = prone ? -78f : attacking ? (facing is 0 or 2 ? -8f : 8f) : 0f;
		sprite.Modulate = hit ? new Color(1f, 0.52f, 0.48f) : Colors.White;
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

	private void TogglePaladin3dPrototype()
	{
		_paladin3dPrototype = !_paladin3dPrototype;
		_paladin3dWalkDistance = 0;
		_paladin3dDirectionTravel = Vector2.Zero;
		ShowMessage(_paladin3dPrototype ? "Prova paladino 3D attiva (P per tornare alla sprite)."
			: "Sprite originale ripristinata.");
		PlaceTokens();
	}

	internal void TogglePaladin3dPrototypeForSmoke() => TogglePaladin3dPrototype();
	internal bool Paladin3dLoadedForSmoke => _paladin3dFrames?.Length == 64
		&& _tokens.TryGetValue(MappedVillageScenario.Ids.Player, out var token)
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
			if (entry.Kind is not (CombatLogKind.Attack or CombatLogKind.OpportunityAttack))
				continue;
			if (entry.Actor is { } attacker)
			{
				_attackVisualUntil[attacker] = now + 0.3;
				if (entry.ShotFrom is { } from && entry.ShotTo is { } to)
					StartShotVisual(from, to);
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
