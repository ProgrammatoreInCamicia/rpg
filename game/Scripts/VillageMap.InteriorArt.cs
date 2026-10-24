using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Static artwork for the playable interior sample. Collision is authored in its scenario.</summary>
public partial class VillageMap
{
	private readonly List<(GridPos Cell, Node2D Sprite)> _interiorProps = new();

	private void BuildInteriorArt()
	{
		foreach (var prop in _presentation.Props ?? Array.Empty<PropLook>())
			AddProp(prop.Asset, prop.Cell, prop.Width, prop.FloorOffset);
	}

	private void AddProp(string asset, GridPos cell, float width, Vector2? floorOffset = null)
	{
		var source = GD.Load<Texture2D>($"res://Assets/StyleStudy/{asset}.png");
		if (source is null) throw new InvalidOperationException($"Missing art prop {asset}");
		var rect = source.GetImage().GetUsedRect();
		if (rect.Size.X <= 0 || rect.Size.Y <= 0) throw new InvalidOperationException($"Empty art prop {asset}");
		var anchor = new Node2D { Name = asset, Position = CellToScreen(cell) + (floorOffset ?? Vector2.Zero) };
		var shadowRadius = asset switch
		{
			"interior-bed" => new Vector2(46, 11),
			"interior-table" => new Vector2(26, 8),
			"interior-chest" => new Vector2(17, 6),
			_ => new Vector2(18, 6),
		};
		foreach (var (radius, alpha) in new[] { (1.25f, 0.09f), (1f, 0.19f), (0.72f, 0.15f) })
		{
			anchor.AddChild(new Polygon2D
			{
				Polygon = Enumerable.Range(0, 24).Select(i =>
				{
					var angle = i * Mathf.Tau / 24f;
					return new Vector2(Mathf.Cos(angle) * shadowRadius.X * radius,
						Mathf.Sin(angle) * shadowRadius.Y * radius);
				}).ToArray(),
				Color = new Color(0.08f, 0.06f, 0.045f, alpha),
			});
		}
		var sprite = new Sprite2D
		{
			Name = "Artwork",
			Texture = new AtlasTexture { Atlas = source, Region = rect },
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
			Scale = Vector2.One * (width / rect.Size.X),
			Offset = new Vector2(0, -rect.Size.Y * 0.5f),
		};
		anchor.AddChild(sprite);
		_sorted.AddChild(anchor);
		_interiorProps.Add((cell, anchor));
	}

	private void RefreshInteriorArtVisibility()
	{
		if (_view.MapVisibility is not { } visibility) return;
		var indoors = _view.Position is { } player && IsIndoor(player);
		foreach (var (cell, sprite) in _interiorProps)
			sprite.Visible = visibility[cell.Y][cell.X] == '2'
				|| indoors && visibility[cell.Y][cell.X] == '1';
	}
}
