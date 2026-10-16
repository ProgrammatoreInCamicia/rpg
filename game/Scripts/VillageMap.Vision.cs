using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Client controls for the player's stored gaze; the Sim decides what enters the cone.</summary>
public partial class VillageMap
{
	private Node2D _visionFeather = null!;
	private readonly List<(GridPos Cell, GridPos Neighbor, Polygon2D Strip)> _visionStrips = new();
	private string? _visionFeatherKey;

	private void BuildVisionFeather()
	{
		_visionFeather = new Node2D { ZIndex = -8 };
		AddChild(_visionFeather);
		var map = _view.Map!;
		for (var y = 0; y < map.Height; y++)
		for (var x = 0; x < map.Width; x++)
		{
			var cell = new GridPos(x, y);
			foreach (var neighbor in new[]
			{
				new GridPos(x + 1, y), new GridPos(x - 1, y),
				new GridPos(x, y + 1), new GridPos(x, y - 1),
			})
			{
				if (neighbor.X < 0 || neighbor.Y < 0 || neighbor.X >= map.Width || neighbor.Y >= map.Height)
					continue;
				var (from, to) = RoofEdge(cell, neighbor);
				var inward = (CellToScreen(cell) - CellToScreen(neighbor)).Normalized() * 13f;
				var clear = new Color(0.025f, 0.025f, 0.035f, 0f);
				var dark = new Color(0.025f, 0.025f, 0.035f, 0.9f);
				var strip = new Polygon2D
				{
					Polygon = new[] { from + inward, to + inward, to, from },
					VertexColors = new[] { clear, clear, dark, dark },
					Color = Colors.White, Visible = false,
				};
				_visionFeather.AddChild(strip);
				_visionStrips.Add((cell, neighbor, strip));
			}
		}
	}

	private void RefreshVisionFeather()
	{
		if (_view.MapVisibility is not { } visibility || _view.Position is null)
		{
			_visionFeather.Visible = false;
			return;
		}
		_visionFeather.Visible = true;
		var key = string.Concat(visibility);
		if (key == _visionFeatherKey) return;
		_visionFeatherKey = key;
		foreach (var (cell, neighbor, strip) in _visionStrips)
			strip.Visible = visibility[cell.Y][cell.X] == '2' && visibility[neighbor.Y][neighbor.X] != '2';
	}

	private void FaceToward(Vector2 worldPoint)
	{
		if (_view.Position is null) return;
		var from = _view.Spot ?? Spot.CenterOf(_view.Position.Value);
		var to = ScreenToSpot(worldPoint);
		var dx = to.X - from.X;
		var dy = to.Y - from.Y;
		if (dx == 0 && dy == 0) return;
		_sim.FacePlayer(dx, dy);
		SyncFigureFacing(new GridPos(dx, dy));
		Refresh();
		PlaceTokens();
	}

	private void SyncFigureFacing(GridPos? facing)
	{
		if (facing is not { } direction) return;
		var screen = new Vector2((direction.X - direction.Y) * TileW / 2f,
			(direction.X + direction.Y) * TileH / 2f);
		if (screen.LengthSquared() <= 0.01f) return;
		_paladinLiveTargetYaw = LiveYaw(screen);
		_paladin3dFacing = PrototypeFacing(screen);
		_figureFacing[_sim.Player] = screen.Y >= 0 ? (screen.X < 0 ? 0 : 1) : (screen.X < 0 ? 2 : 3);
	}

	internal void FaceForSmoke(int x, int y)
	{
		_sim.FacePlayer(x, y);
		SyncFigureFacing(new GridPos(x, y));
		Refresh();
	}
}
