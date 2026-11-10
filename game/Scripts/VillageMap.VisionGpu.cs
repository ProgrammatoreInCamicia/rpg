using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	private ShaderMaterial? _visionGpuMaterial;
	private ImageTexture? _visionGpuWhiteTexture;
	private string? _visionGpuVisibilityKey;

	private void BuildGpuVision()
	{
		var white = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		white.Fill(Colors.White);
		_visionGpuWhiteTexture = ImageTexture.CreateFromImage(white);
		_visionGpuMaterial = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://Shaders/region_vision_feather.gdshader"),
		};
		_visionFeather.Texture = _visionGpuWhiteTexture;
		_visionFeather.Material = _visionGpuMaterial;
	}

	private void RefreshGpuVision(IReadOnlyList<string> visibility, string visibilityKey)
	{
		var width = visibility[0].Length;
		var height = visibility.Count;
		if (_visionGpuVisibilityKey != visibilityKey)
		{
			_visionGpuVisibilityKey = visibilityKey;
			var pixels = new byte[width * height];
			for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
				pixels[y * width + x] = visibility[y][x] == '2' ? (byte)255 : (byte)0;
			var image = Image.CreateFromData(width, height, false, Image.Format.R8, pixels);
			if (_visionTexture is null || _visionTexture.GetWidth() != width || _visionTexture.GetHeight() != height)
				_visionTexture = ImageTexture.CreateFromImage(image);
			else
				_visionTexture.Update(image);
			_visionGpuMaterial!.SetShaderParameter("seen_map", _visionTexture);
			_visionGpuMaterial.SetShaderParameter("map_size", new Vector2(width, height));
		}

		var minX = float.PositiveInfinity;
		var minY = float.PositiveInfinity;
		var maxX = float.NegativeInfinity;
		var maxY = float.NegativeInfinity;
		for (var y = 0; y < height; y++)
		for (var x = 0; x < width; x++)
			if (visibility[y][x] == '2')
			{
				var projected = CellToScreen(new GridPos(x, y));
				minX = Math.Min(minX, projected.X);
				minY = Math.Min(minY, projected.Y);
				maxX = Math.Max(maxX, projected.X);
				maxY = Math.Max(maxY, projected.Y);
			}
		if (float.IsPositiveInfinity(minX))
		{
			_visionFeather.Visible = false;
			return;
		}
		var left = Math.Clamp(Mathf.FloorToInt((minX - TileW - _visionOrigin.X) / _visionMaskPixel), 0, _visionMaskWidth);
		var right = Math.Clamp(Mathf.CeilToInt((maxX + TileW - _visionOrigin.X) / _visionMaskPixel), 0, _visionMaskWidth);
		var top = Math.Clamp(Mathf.FloorToInt((minY - TileH - _visionOrigin.Y) / _visionMaskPixel), 0, _visionMaskHeight);
		var bottom = Math.Clamp(Mathf.CeilToInt((maxY + TileH - _visionOrigin.Y) / _visionMaskPixel), 0, _visionMaskHeight);
		if (right <= left || bottom <= top)
		{
			_visionFeather.Visible = false;
			return;
		}
		var origin = _visionOrigin + new Vector2(left, top) * _visionMaskPixel;
		var size = new Vector2(right - left, bottom - top) * _visionMaskPixel;
		_visionFeather.Visible = true;
		_visionFeather.Position = origin;
		_visionFeather.Scale = size;
		var eye = _view.Spot ?? Spot.CenterOf(_view.Position!.Value);
		var face = _view.Facing is { } facing ? new Vector2(facing.X, facing.Y).Normalized() : Vector2.Zero;
		_visionGpuMaterial!.SetShaderParameter("crop_origin", origin);
		_visionGpuMaterial.SetShaderParameter("crop_size", size);
		_visionGpuMaterial.SetShaderParameter("tile_half", new Vector2(TileW / 2f, TileH / 2f));
		_visionGpuMaterial.SetShaderParameter("eye_cell", new Vector2(
			eye.X / (float)Spot.PerSquare - 0.5f, eye.Y / (float)Spot.PerSquare - 0.5f));
		_visionGpuMaterial.SetShaderParameter("facing", face);
		_visionGpuMaterial.SetShaderParameter("directional", face != Vector2.Zero && _view.Combat is null);
	}
}