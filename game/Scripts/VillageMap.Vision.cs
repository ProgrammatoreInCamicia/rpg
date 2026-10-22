using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Client controls for the player's stored gaze; the Sim decides what enters the cone.</summary>
public partial class VillageMap
{
	private Sprite2D _visionFeather = null!;
	private ImageTexture? _visionTexture;
	private Vector2 _visionOrigin;
	private int _visionMaskWidth;
	private int _visionMaskHeight;
	private const float VisionMaskPixel = 3f;
	private string? _visionFeatherKey;

	private void BuildVisionFeather()
	{
		var map = _view.Map!;
		var corners = new[]
		{
			CellToScreen(new GridPos(0, 0)), CellToScreen(new GridPos(map.Width - 1, 0)),
			CellToScreen(new GridPos(0, map.Height - 1)), CellToScreen(new GridPos(map.Width - 1, map.Height - 1)),
		};
		_visionOrigin = new Vector2(corners.Min(p => p.X) - TileW / 2f,
			corners.Min(p => p.Y) - TileH / 2f);
		var extent = new Vector2(corners.Max(p => p.X) + TileW / 2f,
			corners.Max(p => p.Y) + TileH / 2f) - _visionOrigin;
		_visionMaskWidth = Mathf.CeilToInt(extent.X / VisionMaskPixel);
		_visionMaskHeight = Mathf.CeilToInt(extent.Y / VisionMaskPixel);
		_visionFeather = new Sprite2D
		{
			ZIndex = -8, Position = _visionOrigin, Centered = false,
			Scale = Vector2.One * VisionMaskPixel,
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
		};
		AddChild(_visionFeather);
	}

	private void RefreshVisionFeather()
	{
		if (_view.MapVisibility is not { } visibility || _view.Position is null)
		{
			_visionFeather.Visible = false;
			return;
		}
		_visionFeather.Visible = true;
		var key = string.Concat(visibility) + $"|{_view.Spot}|{_view.Facing}|{_view.Combat is not null}";
		if (key == _visionFeatherKey) return;
		_visionFeatherKey = key;
		var eye = _view.Spot ?? Spot.CenterOf(_view.Position.Value);
		var eyeCell = new Vector2(eye.X / (float)Spot.PerSquare - 0.5f,
			eye.Y / (float)Spot.PerSquare - 0.5f);
		var face = _view.Facing is { } facing ? new Vector2(facing.X, facing.Y).Normalized() : Vector2.Zero;
		var directional = face != Vector2.Zero && _view.Combat is null;
		int w = visibility[0].Length, h = visibility.Count;
		float Seen(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && visibility[y][x] == '2' ? 1f : 0f;
		var pixels = new byte[_visionMaskWidth * _visionMaskHeight * 4];
		for (var py = 0; py < _visionMaskHeight; py++)
		for (var px = 0; px < _visionMaskWidth; px++)
		{
			var point = _visionOrigin + new Vector2((px + 0.5f) * VisionMaskPixel,
				(py + 0.5f) * VisionMaskPixel);
			var a = point.X / (TileW / 2f);
			var b = point.Y / (TileH / 2f);
			var x = (a + b) / 2f;
			var y = (b - a) / 2f;
			if (Seen(Mathf.RoundToInt(x), Mathf.RoundToInt(y)) == 0f) continue;
			var ix = Mathf.FloorToInt(x);
			var iy = Mathf.FloorToInt(y);
			var fx = x - ix;
			var fy = y - iy;
			var upper = Mathf.Lerp(Seen(ix, iy), Seen(ix + 1, iy), fx);
			var lower = Mathf.Lerp(Seen(ix, iy + 1), Seen(ix + 1, iy + 1), fx);
			var field = Mathf.Lerp(upper, lower, fy);
			var edgeDark = 1f - Mathf.SmoothStep(0.5f, 1f, field);
			var angleDark = 0f;
			if (directional)
			{
				var away = new Vector2(x, y) - eyeCell;
				if (away.LengthSquared() > 1.5f * 1.5f)
				{
					var cosine = face.Dot(away.Normalized());
					angleDark = 1f - Mathf.SmoothStep(0.10f, 0.24f, cosine);
				}
			}
			var alpha = (byte)Mathf.RoundToInt(242f * Mathf.Max(edgeDark, angleDark));
			var i = (py * _visionMaskWidth + px) * 4;
			pixels[i] = 6;
			pixels[i + 1] = 6;
			pixels[i + 2] = 9;
			pixels[i + 3] = alpha;
		}
		var image = Image.CreateFromData(_visionMaskWidth, _visionMaskHeight, false, Image.Format.Rgba8, pixels);
		if (_visionTexture is null)
			_visionFeather.Texture = _visionTexture = ImageTexture.CreateFromImage(image);
		else
			_visionTexture.Update(image);
	}

	private void FaceToward(Vector2 worldPoint)
	{
		if (_view.Position is null) return;
		CancelSliceWait("Attesa interrotta.");
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
