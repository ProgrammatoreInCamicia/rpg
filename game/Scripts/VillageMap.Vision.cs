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
	private float _visionMaskPixel = 3f;
	private string? _visionFeatherKey;

	private void BuildVisionFeather()
	{
		var map = _view.Map!;
		_visionMaskPixel = BlackmarshRegionMode && map.Width >= 40 ? 4f : 3f;
		var corners = new[]
		{
			CellToScreen(new GridPos(0, 0)), CellToScreen(new GridPos(map.Width - 1, 0)),
			CellToScreen(new GridPos(0, map.Height - 1)), CellToScreen(new GridPos(map.Width - 1, map.Height - 1)),
		};
		_visionOrigin = new Vector2(corners.Min(p => p.X) - TileW / 2f,
			corners.Min(p => p.Y) - TileH / 2f);
		var extent = new Vector2(corners.Max(p => p.X) + TileW / 2f,
			corners.Max(p => p.Y) + TileH / 2f) - _visionOrigin;
		_visionMaskWidth = Mathf.CeilToInt(extent.X / _visionMaskPixel);
		_visionMaskHeight = Mathf.CeilToInt(extent.Y / _visionMaskPixel);
		_visionFeather = new Sprite2D
		{
			ZIndex = -8, Position = _visionOrigin, Centered = false,
			Scale = Vector2.One * _visionMaskPixel,
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
		};
		_mapRoot.AddChild(_visionFeather);
		if (_regionGpuVision) BuildGpuVision();
	}

	private void RefreshVisionFeather()
	{
		if (_view.MapVisibility is not { } visibility || _view.Position is null)
		{
			_visionFeather.Visible = false;
			return;
		}
		_visionFeather.Visible = true;
		var visibilityKey = string.Concat(visibility);
		var key = visibilityKey + $"|{_view.Spot}|{_view.Facing}|{_view.Combat is not null}";
		if (key == _visionFeatherKey) return;
		_visionFeatherKey = key;
		if (_regionGpuVision)
		{
			RefreshGpuVision(visibility, visibilityKey);
			return;
		}
		var eye = _view.Spot ?? Spot.CenterOf(_view.Position.Value);
		var eyeCell = new Vector2(eye.X / (float)Spot.PerSquare - 0.5f,
			eye.Y / (float)Spot.PerSquare - 0.5f);
		var face = _view.Facing is { } facing ? new Vector2(facing.X, facing.Y).Normalized() : Vector2.Zero;
		var directional = face != Vector2.Zero && _view.Combat is null;
		int w = visibility[0].Length, h = visibility.Count;
		float Seen(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && visibility[y][x] == '2' ? 1f : 0f;
		// Only squares currently in sight can contribute opaque pixels. The sprite follows that small image
		// across the map, avoiding a full-map upload whenever the player turns or walks.
		var minX = float.PositiveInfinity;
		var minY = float.PositiveInfinity;
		var maxX = float.NegativeInfinity;
		var maxY = float.NegativeInfinity;
		for (var cy = 0; cy < h; cy++)
		for (var cx = 0; cx < w; cx++)
			if (visibility[cy][cx] == '2')
			{
				var projected = CellToScreen(new GridPos(cx, cy));
				minX = Math.Min(minX, projected.X);
				minY = Math.Min(minY, projected.Y);
				maxX = Math.Max(maxX, projected.X);
				maxY = Math.Max(maxY, projected.Y);
			}
		var left = float.IsPositiveInfinity(minX) ? 0 : Math.Clamp(Mathf.FloorToInt((minX - TileW - _visionOrigin.X) / _visionMaskPixel), 0, _visionMaskWidth);
		var right = float.IsPositiveInfinity(minX) ? 0 : Math.Clamp(Mathf.CeilToInt((maxX + TileW - _visionOrigin.X) / _visionMaskPixel), 0, _visionMaskWidth);
		var top = float.IsPositiveInfinity(minY) ? 0 : Math.Clamp(Mathf.FloorToInt((minY - TileH - _visionOrigin.Y) / _visionMaskPixel), 0, _visionMaskHeight);
		var bottom = float.IsPositiveInfinity(minY) ? 0 : Math.Clamp(Mathf.CeilToInt((maxY + TileH - _visionOrigin.Y) / _visionMaskPixel), 0, _visionMaskHeight);
		if (right <= left || bottom <= top)
		{
			_visionFeather.Visible = false;
			return;
		}
		var regionWidth = right - left;
		var regionHeight = bottom - top;
		var pixels = new byte[regionWidth * regionHeight * 4];
		for (var py = top; py < bottom; py++)
		for (var px = left; px < right; px++)
		{
			var point = _visionOrigin + new Vector2((px + 0.5f) * _visionMaskPixel,
				(py + 0.5f) * _visionMaskPixel);
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
			var i = ((py - top) * regionWidth + px - left) * 4;
			pixels[i] = 6;
			pixels[i + 1] = 6;
			pixels[i + 2] = 9;
			pixels[i + 3] = alpha;
		}
		var image = Image.CreateFromData(regionWidth, regionHeight, false, Image.Format.Rgba8, pixels);
		_visionFeather.Position = _visionOrigin + new Vector2(left, top) * _visionMaskPixel;
		if (_visionTexture is null || _visionTexture.GetWidth() != regionWidth || _visionTexture.GetHeight() != regionHeight)
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
