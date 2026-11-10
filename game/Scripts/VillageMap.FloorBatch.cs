using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	private const int FloorChunkSize = 8;
	private readonly Dictionary<(int X, int Y, Texture2D? Texture), FloorChunk> _floorChunks = new();

	private sealed class FloorChunk
	{
		public required MeshInstance2D Node;
		public readonly List<FloorCell> Cells = new();
	}

	private sealed class FloorCell
	{
		public required GridPos At;
		public required Color BaseColor;
		public required Vector2[] Uv;
		public Color Light = Colors.White;
	}

	private void AddBatchedFloor(GridPos cell, char symbol, FloorLook look, float variation)
	{
		var texture = FloorTexture(symbol, look);
		var key = (cell.X / FloorChunkSize, cell.Y / FloorChunkSize, texture);
		if (!_floorChunks.TryGetValue(key, out var chunk))
		{
			chunk = new FloorChunk
			{
				Node = new MeshInstance2D { Texture = texture },
			};
			_floorChunks.Add(key, chunk);
			_floor.AddChild(chunk.Node);
		}
		chunk.Cells.Add(new FloorCell
		{
			At = cell,
			BaseColor = FloorColor(symbol, look, variation, texture),
			Uv = texture is null ? new Vector2[4] : FloorUv(texture, cell).Select(uv => new Vector2(uv.X / texture.GetWidth(), uv.Y / texture.GetHeight())).ToArray(),
		});
		if (_presentation.FloorAccent == cell)
		{
			var accent = new Polygon2D { Polygon = Diamond(), Position = CellToScreen(cell), Color = Colors.Transparent };
			DressPilotFloor(accent);
			_floor.AddChild(accent);
		}
	}

	private void BuildBatchedFloors()
	{
		foreach (var chunk in _floorChunks.Values)
			RebuildFloorChunk(chunk);
	}

	private void ShadeBatchedFloors()
	{
		foreach (var chunk in _floorChunks.Values)
		{
			var changed = false;
			foreach (var cell in chunk.Cells)
			{
				var light = Shade(cell.At);
				if (cell.Light == light) continue;
				cell.Light = light;
				changed = true;
			}
			if (changed) RebuildFloorChunk(chunk);
		}
	}

	private static void RebuildFloorChunk(FloorChunk chunk)
	{
		using var tool = new SurfaceTool();
		tool.Begin(Mesh.PrimitiveType.Triangles);
		var diamond = Diamond();
		foreach (var cell in chunk.Cells)
		{
			var position = CellToScreen(cell.At);
			var color = cell.BaseColor * cell.Light;
			foreach (var corner in new[] { 0, 1, 2, 0, 2, 3 })
			{
				tool.SetColor(color);
				tool.SetUV(cell.Uv[corner]);
				var vertex = position + diamond[corner];
				tool.AddVertex(new Vector3(vertex.X, vertex.Y, 0));
			}
		}
		chunk.Node.Mesh = tool.Commit();
	}
}