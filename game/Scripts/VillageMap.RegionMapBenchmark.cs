using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using Island = RpgSandbox.Sim.Scenarios.BlackmarshWatchIsland.Ids;

namespace RpgSandbox.Game;

/// <summary>Temporary regional size probe. It uses the normal client renderer and an isolated Sim fixture.</summary>
public partial class VillageMap
{
	private double _pilotShadeMs;
	private double _pilotBuildingsMs;
	private double _pilotVisionMs;
	private bool _pilotMeasureRefresh;
	private static SimulationSession CreateRegionMapBenchmarkFixture(string dimensions)
	{
		var (width, height) = ParseRegionMapBenchmarkSize(dimensions);
		var rows = Enumerable.Range(0, height).Select(y => new string(Enumerable.Range(0, width)
			.Select(x => x >= width / 2 - 3 && x <= width / 2 + 3
				&& y >= height / 2 - 3 && y <= height / 2 + 3 ? 'W' : '.').ToArray())).ToArray();
		var b = new ScenarioBuilder().WithSeed(7)
			.AddArea(Island.Hamlet.Value, "Benchmark")
			.AddLocation(Island.Well.Value, "Piazzale", Island.Hamlet.Value)
			.AddMap(Island.Hamlet.Value, rows, new Dictionary<char, string> { ['W'] = Island.Well.Value })
			.AddActor(Island.Player.Value, "Protagonista", Island.Well.Value, isPlayer: true,
				at: (width / 2, height / 2), build: "paladin");
		return SimulationSession.Create(b.Build());
	}

	private static (int Width, int Height) ParseRegionMapBenchmarkSize(string dimensions)
	{
		var parts = dimensions.Split('x');
		if (parts.Length != 2 || !int.TryParse(parts[0], out var width)
			|| !int.TryParse(parts[1], out var height)
			|| (width, height) is not ((20, 14) or (48, 32) or (80, 60)))
			throw new ArgumentException("Benchmark dimensions must be 20x14, 48x32 or 80x60.");
		return (width, height);
	}

	private async void RunRegionMapBenchmark(string dimensions, double sessionMs, double mapMs, double refreshMs)
	{
		try
		{
			SetPaused(true);
			await SliceFrames(8);
			var beforeBytes = GC.GetTotalMemory(false);
			var measure = System.Diagnostics.Stopwatch.StartNew();
			double simFacingMs = 0, clientFacingMs = 0;
			_pilotShadeMs = _pilotBuildingsMs = _pilotVisionMs = 0;
			_pilotMeasureRefresh = _regionMapPilot;
			foreach (var facing in new[] { new GridPos(1, 0), new GridPos(0, 1),
				new GridPos(-1, 0), new GridPos(0, -1) })
			{
				var before = measure.Elapsed.TotalMilliseconds;
				_sim.FacePlayer(facing.X, facing.Y);
				SyncFigureFacing(facing);
				simFacingMs += measure.Elapsed.TotalMilliseconds - before;
				before = measure.Elapsed.TotalMilliseconds;
				Refresh();
				clientFacingMs += measure.Elapsed.TotalMilliseconds - before;
			}
			var facingMs = measure.Elapsed.TotalMilliseconds;
			_pilotMeasureRefresh = false;
			var from = _view.Position!.Value;
			var move = _sim.Execute(new MoveToSpotCommand
				{ Actor = _sim.Player, To = Spot.CenterOf(new GridPos(from.X + 2, from.Y)) });
			SliceRequire(move.Success && move.Action is not null, $"Benchmark move failed: {move.Message}");
			var actionId = move.Action ?? throw new InvalidOperationException("Benchmark move has no action.");
			SliceRequire(_sim.AdvanceUntilCompleted(actionId, Duration.FromMinutes(1)).Outcome
				== AdvanceOutcome.Completed, "Benchmark move did not complete.");
			Refresh();
			var moveMs = measure.Elapsed.TotalMilliseconds - facingMs;
			await SliceFrames(60);
			var frameClock = System.Diagnostics.Stopwatch.StartNew();
			await SliceFrames(60);
			var framesMs = frameClock.Elapsed.TotalMilliseconds;
			var allocatedBytes = GC.GetTotalMemory(false) - beforeBytes;
			var currentSight = _view.MapVisibility?.Sum(row => row.Count(c => c == '2')) ?? 0;
			GD.Print($"REGION MAP BENCHMARK {dimensions}: session={sessionMs:F1}ms, map={mapMs - sessionMs:F1}ms, "
				+ $"initial-ui-refresh={refreshMs - mapMs:F1}ms, four-facings={facingMs:F1}ms "
				+ $"(sim={simFacingMs:F1}ms, refresh={clientFacingMs:F1}ms), "
				+ $"refresh-parts=(shade={_pilotShadeMs:F1}ms, buildings={_pilotBuildingsMs:F1}ms, vision={_pilotVisionMs:F1}ms), "
				+ $"two-square-move-and-refresh={moveMs:F1}ms, nodes={CountBenchmarkNodes(_mapRoot)}, "
				+ $"managed-delta={allocatedBytes}B, sight-cells={currentSight}, "
				+ $"mask={_visionTexture?.GetWidth()}x{_visionTexture?.GetHeight()}, "
				+ $"60-frames={framesMs:F1}ms, measured-fps={60000 / framesMs:F1}");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"REGION MAP BENCHMARK FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private static int CountBenchmarkNodes(Node root) =>
		1 + root.GetChildren().Sum(CountBenchmarkNodes);
}
