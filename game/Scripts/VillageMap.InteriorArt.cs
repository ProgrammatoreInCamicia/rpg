using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Static artwork for the playable interior sample. Collision is authored in its scenario.</summary>
public partial class VillageMap
{
	private readonly List<(GridPos Cell, Sprite2D Sprite)> _interiorProps = new();

	private void BuildInteriorArt()
	{
		if (!InteriorArtPilot) return;
		AddProp("interior-bed", new GridPos(11, 5), 110f);
		AddProp("interior-table", new GridPos(10, 8), 75f);
		AddProp("interior-chest", new GridPos(12, 8), 49f);
		AddProp("interior-table", new GridPos(6, 6), 75f);
		AddProp("interior-cabinet", new GridPos(7, 5), 57f);
		AddProp("interior-cabinet", new GridPos(10, 5), 53f);
	}

	private void AddProp(string asset, GridPos cell, float width)
	{
		var source = GD.Load<Texture2D>($"res://Assets/StyleStudy/{asset}.png");
		if (source is null) throw new InvalidOperationException($"Missing art prop {asset}");
		var rect = source.GetImage().GetUsedRect();
		if (rect.Size.X <= 0 || rect.Size.Y <= 0) throw new InvalidOperationException($"Empty art prop {asset}");
		var sprite = new Sprite2D
		{
			Name = asset,
			Position = CellToScreen(cell),
			Texture = new AtlasTexture { Atlas = source, Region = rect },
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
			Scale = Vector2.One * (width / rect.Size.X),
			Offset = new Vector2(0, -rect.Size.Y * 0.5f),
		};
		_sorted.AddChild(sprite);
		_interiorProps.Add((cell, sprite));
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
