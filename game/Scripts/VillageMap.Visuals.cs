using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>G15 visual prototype. All directions and animation timing are presentation only.</summary>
public partial class VillageMap
{
	private readonly Dictionary<string, AtlasTexture[]> _figureAtlases = new();
	private readonly Dictionary<ActorId, int> _figureFacing = new();
	private readonly Dictionary<ActorId, double> _attackVisualUntil = new();
	private readonly Dictionary<ActorId, double> _hitVisualUntil = new();
	private int _seenCombatVisuals;
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

	private void UpdateFigureSprite(Node2D token, ActorId id, Vector2 previousPosition, MoveView? move, bool prone)
	{
		if (token.GetNodeOrNull<Sprite2D>("Figure") is not { } sprite)
			return;
		token.GetNode<Polygon2D>("Body").Visible = false;
		token.GetNode<Polygon2D>("Head").Visible = false;
		token.GetNode<Polygon2D>("Prone").Visible = false;
		var delta = token.Position - previousPosition;
		if (move is not null && delta.LengthSquared() > 0.01f)
			_figureFacing[id] = delta.Y >= 0 ? (delta.X < 0 ? 0 : 1) : (delta.X < 0 ? 2 : 3);
		var asset = FigureAsset(id)!;
		var facing = _figureFacing.GetValueOrDefault(id);
		var frame = _figureAtlases[asset][facing];
		if (sprite.Texture != frame)
			sprite.Texture = frame;
		var now = Time.GetTicksMsec() / 1000.0;
		var attacking = _attackVisualUntil.GetValueOrDefault(id) > now;
		var hit = _hitVisualUntil.GetValueOrDefault(id) > now;
		var bob = move is null || prone ? 0f : MathF.Sin((float)(now * 19)) * 1.2f;
		sprite.Position = prone ? new Vector2(0, -5) : new Vector2(attacking ? (facing is 0 or 2 ? -3 : 3) : 0, bob);
		sprite.RotationDegrees = prone ? -78f : attacking ? (facing is 0 or 2 ? -8f : 8f) : 0f;
		sprite.Modulate = hit ? new Color(1f, 0.52f, 0.48f) : Colors.White;
	}

	private void ObserveCombatVisuals()
	{
		if (_view.Combat is not { } combat)
		{
			_seenCombatVisuals = 0;
			return;
		}
		if (combat.Log.Count < _seenCombatVisuals)
			_seenCombatVisuals = 0;
		var now = Time.GetTicksMsec() / 1000.0;
		foreach (var entry in combat.Log.Skip(_seenCombatVisuals))
		{
			if (entry.Kind is not (CombatLogKind.Attack or CombatLogKind.OpportunityAttack))
				continue;
			if (entry.Actor is { } attacker)
				_attackVisualUntil[attacker] = now + 0.3;
			if (entry.Hit == true && entry.Target is { } target)
				_hitVisualUntil[target] = now + 0.35;
		}
		_seenCombatVisuals = combat.Log.Count;
	}

	internal bool HasFigureSpriteForSmoke(ActorId id) => _tokens.TryGetValue(id, out var token)
		&& token.GetNodeOrNull<Sprite2D>("Figure") is { Texture: not null };

	internal bool AttackVisualActiveForSmoke(ActorId id) => _attackVisualUntil.GetValueOrDefault(id)
		> Time.GetTicksMsec() / 1000.0;
}
