using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Task 9: complete player-handler routes, from fresh scenes, with world-only causal oracles.</summary>
public partial class VillageMap
{
	private static int _blackmarshComposedPhase;
	private static byte[]? _blackmarshComposedInitial;
	private static readonly List<string> BlackmarshComposedTimes = new();

	private async void RunBlackmarshComposedSmoke(string outputDir)
	{
		try
		{
			Directory.CreateDirectory(outputDir);
			var size = GetViewport().GetVisibleRect().Size;
			SliceRequire(size == new Vector2(1280, 720) || size == new Vector2(1920, 1080),
				$"Unexpected composed viewport {size}.");
			var initial = SaveBlackmarshSnapshot(_sim);
			SliceRequire(BlackmarshWatchhouseMode && _paused && _sliceIntro is { Visible: true }
				&& _view.Position == BlackmarshWatchhouse.Ids.PlayerStart && _view.Food == 10
				&& _view.Observations.Count == 0 && _sim.Player == BlackmarshWatchhouse.Ids.Player
				&& _sim.GetWorldView().Areas.All(area => area.Id.Value.StartsWith("bm_watch_", StringComparison.Ordinal))
				&& _sim.GetWorldView().Locations.All(location => location.Id.Value.StartsWith("bm_watch_", StringComparison.Ordinal))
				&& _sim.GetWorldView().Actors.All(actor => actor.Id.Value.StartsWith("bm_watch_", StringComparison.Ordinal))
				&& _sim.GetWorldView().Stores.All(store => store.Id.Value.StartsWith("bm_watch_", StringComparison.Ordinal))
				&& !_blackmarshGuideText!.Text.Contains("granaio", StringComparison.OrdinalIgnoreCase)
				&& _sliceIntro.GetChildren().OfType<VBoxContainer>().Single().GetChildren()
					.OfType<Label>().All(label => !label.Text.Contains("granaio", StringComparison.OrdinalIgnoreCase)),
				"The fresh Blackmarsh scene contains stale village content or is not paused at entry.");
			if (_blackmarshComposedPhase == 0)
			{
				_blackmarshComposedInitial = initial;
				await SliceFrames(3);
				CaptureBlackmarshComposed(outputDir, "entry.png");
				await RunBlackmarshComposedRecovery(outputDir);
			}
			else
			{
				SliceRequire(_blackmarshComposedInitial is not null && initial.SequenceEqual(_blackmarshComposedInitial)
					&& _queue.Count == 0 && _running is null && !_sliceWaiting && _pendingBarrier is null,
					$"Fresh scene {_blackmarshComposedPhase} did not reset after Retry.");
				if (_blackmarshComposedPhase == 1)
					await RunBlackmarshComposedUnseen(outputDir);
				else if (_blackmarshComposedPhase == 2)
					await RunBlackmarshComposedWitness(outputDir);
				else
					throw new InvalidOperationException("Unexpected composed phase.");
			}
			if (_blackmarshComposedPhase < 2)
			{
				_blackmarshComposedPhase++;
				_sliceSummaryRetry!.EmitSignal(BaseButton.SignalName.Pressed);
				return;
			}
			File.WriteAllLines(Path.Combine(outputDir, "times.txt"), BlackmarshComposedTimes);
			GD.Print("BLACKMARSH COMPOSED SMOKE OK: recovery, unseen raid, witnessed break-in, load and two real retries");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"BLACKMARSH COMPOSED SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private async Task RunBlackmarshComposedRecovery(string outputDir)
	{
		_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
		UseTorch(stowShield: true);
		MoveTo(new GridPos(5, 6));
		await UntilBlackmarshStoreIdle();
		ClickDoor(BlackmarshWatchhouse.Ids.ShedDoor);
		await UntilBlackmarshStoreIdle();
		MoveTo(new GridPos(8, 6));
		await UntilBlackmarshStoreIdle();
		ClickDoor(BlackmarshWatchhouse.Ids.InnerDoor);
		await UntilBlackmarshStoreIdle();
		MoveTo(BlackmarshWatchhouse.Ids.StoreAccess);
		await UntilBlackmarshStoreIdle();
		SliceRequire(_view.Position == BlackmarshWatchhouse.Ids.StoreAccess
			&& _view.Doors.Any(d => d.At == BlackmarshWatchhouse.Ids.ShedDoor && d.Open == true)
			&& _view.Doors.Any(d => d.At == BlackmarshWatchhouse.Ids.InnerDoor && d.Open == true),
			"The composed route did not open both doors and reach the store.");
		CaptureBlackmarshComposed(outputDir, "interior.png");
		var save = Path.Combine(outputDir, "recovery-before-take.json");
		SaveGameToPath(save);
		MoveTo(new GridPos(10, 5));
		await UntilBlackmarshStoreIdle();
		LoadGameFromPath(save);
		SliceRequire(_view.Position == BlackmarshWatchhouse.Ids.StoreAccess && _view.Food == 10
			&& _queue.Count == 0 && _running is null, "The composed load did not restore the pre-take position.");
		SetPaused(false);
		if (_selectedStore != BlackmarshWatchhouse.Ids.ShedStore)
			SelectStoreOption(_storeOptionIds.IndexOf(BlackmarshWatchhouse.Ids.ShedStore) + 1);
		SliceRequire(_selectedStore == BlackmarshWatchhouse.Ids.ShedStore && !_storeTake.Disabled,
			"The shed store was unavailable after loading the composed route.");
		_storeTake.EmitSignal(BaseButton.SignalName.Pressed);
		await UntilBlackmarshStoreIdle();
		SliceRequire(_view.Food == 14 && _sim.GetWorldView().Stores.Single(s => s.Id == BlackmarshWatchhouse.Ids.ShedStore).Food == 0,
			"The player did not recover exactly four rations.");
		SliceRequire(_blackmarshGuideText!.Text.Contains("La cassa è vuota", StringComparison.Ordinal),
			"The player at the access did not learn that the recovered shed is empty.");
		BlackmarshComposedTimes.Add($"recovered: {_view.Now}");
		CaptureBlackmarshComposed(outputDir, "recovery.png");
		MoveTo(BlackmarshWatchhouse.Ids.PlayerStart);
		await UntilBlackmarshStoreIdle();
		await WaitBlackmarshComposedTwice();
		var world = _sim.GetWorldView();
		SliceRequire(_view.Food == 14 && world.Stores.Single(s => s.Id == BlackmarshWatchhouse.Ids.VasanCache).Food == 0
			&& world.RecentFacts.Any(f => f.Kind == "RaidOrdered")
			&& world.RecentFacts.Count(f => f.Kind == "FoodTaken") == 1
			&& !world.RecentFacts.Any(f => f.Kind is "FoodStolen" or "FoodDeposited"),
			"The recovered branch did not change the raider's outcome.");
		AssertBlackmarshComposedFood();
		ConcludeBlackmarshComposed(14, observed: false);
		await SliceFrames(3);
		CaptureBlackmarshComposed(outputDir, "recovered-conclusion.png");
	}

	private async Task RunBlackmarshComposedUnseen(string outputDir)
	{
		_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
		await WaitBlackmarshComposedTwice();
		var world = _sim.GetWorldView();
		SliceRequire(_view.Food == 10 && _view.Observations.Count == 0
			&& world.Stores.Single(s => s.Id == BlackmarshWatchhouse.Ids.ShedStore).Food == 0
			&& world.Stores.Single(s => s.Id == BlackmarshWatchhouse.Ids.VasanCache).Food == 4
			&& world.RecentFacts.Any(f => f.Kind == "GlassBroken")
			&& world.RecentFacts.Any(f => f.Kind == "Climbed")
			&& world.RecentFacts.Any(f => f.Kind == "FoodStolen")
			&& world.RecentFacts.Any(f => f.Kind == "FoodDeposited"),
			"The unattended raider did not physically break in, take food and carry it home.");
		BlackmarshComposedTimes.Add($"unseen raid deposited: {world.RecentFacts.Single(f => f.Kind == "FoodDeposited").At}");
		AssertBlackmarshComposedFood();
		ConcludeBlackmarshComposed(10, observed: false);
		await SliceFrames(3);
		CaptureBlackmarshComposed(outputDir, "unseen-conclusion.png");
	}

	private async Task RunBlackmarshComposedWitness(string outputDir)
	{
		_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
		UseTorch(stowShield: true);
		MoveTo(new GridPos(12, 7));
		await UntilBlackmarshStoreIdle();
		SliceRequire(_view.Position == new GridPos(12, 7), "Could not reach the window lookout.");
		FaceForSmoke(0, -1);
		await SliceFrames(3);
		for (var i = 0; i < 2; i++)
		{
			_sliceWaitButton!.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceWaitUntilStopped();
			if (_sim.GetWorldView().RecentFacts.Any(f => f.Kind == "GlassBroken")) break;
		}
		SliceRequire(_sim.GetWorldView().RecentFacts.Any(f => f.Kind == "GlassBroken")
			&& _view.Map!.Edges.Single(e => e.Kind == MapEdgeKind.Window).Glass == WindowGlass.Broken
			&& _view.VisibleActors.Any(actor => actor.Id == BlackmarshWatchhouse.Ids.Raider),
			"The expected window breach did not occur from a fresh observed route.");
		BlackmarshComposedTimes.Add($"window broken: {_sim.GetWorldView().RecentFacts.First(f => f.Kind == "GlassBroken").At}");
		CaptureBlackmarshComposed(outputDir, "witnessed-break-in.png");
		for (var i = 0; i < 3 && !_view.Observations.Any(o => o.Kind == "Theft" && o.Store == BlackmarshWatchhouse.Ids.ShedStore); i++)
		{
			_sliceWaitButton!.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceWaitUntilStopped();
		}
		SliceRequire(_view.Observations.Any(o => o.Kind == "Theft" && o.Store == BlackmarshWatchhouse.Ids.ShedStore),
			"The nearby player did not observe the raider's completed theft.");
		SliceRequire(_view.VisibleStores.Any(s => s.Id == BlackmarshWatchhouse.Ids.ShedStore && s.Food == 0)
			&& _view.Position is { } witness && !_view.VisibleStores.Single(s => s.Id == BlackmarshWatchhouse.Ids.ShedStore).Access.Contains(witness)
			&& !_blackmarshGuideText!.Text.Contains("La cassa è vuota", StringComparison.Ordinal)
			&& _blackmarshGuideText.Text.Contains("usa Avvicinati", StringComparison.Ordinal),
			"The guide disclosed the shed count through the window without an access or player take.");
		AssertBlackmarshComposedFood();
		BlackmarshComposedTimes.Add($"witnessed theft: {_view.Observations.First(o => o.Kind == "Theft" && o.Store == BlackmarshWatchhouse.Ids.ShedStore).ObservedAt}");
		ConcludeBlackmarshComposed(10, observed: true);
		SliceRequire(_sliceSummaryText!.Text.Contains("Sottrazione alle"),
			"The observed theft did not appear in the player's conclusion.");
		await SliceFrames(3);
		CaptureBlackmarshComposed(outputDir, "witnessed-conclusion.png");
	}

	private async Task WaitBlackmarshComposedTwice()
	{
		for (var i = 0; i < 2; i++)
		{
			var start = _view.Now;
			_sliceWaitButton!.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceWaitUntilStopped();
			SliceRequire(_view.Now > start && _view.Now <= start.Plus(Duration.FromMinutes(5)),
				"The composed wait advanced by more than one five-minute interval.");
		}
	}

	private void ConcludeBlackmarshComposed(int food, bool observed)
	{
		_sliceSummaryButton!.EmitSignal(BaseButton.SignalName.Pressed);
		var summary = _sliceSummaryText!.Text;
		SliceRequire(_paused && summary.Contains($"Le tue razioni: {food}")
			&& (observed || !summary.Contains("Vasan", StringComparison.OrdinalIgnoreCase))
			&& !summary.Contains("granaio", StringComparison.OrdinalIgnoreCase)
			&& (observed || summary.Contains("non hai osservato"))
			&& (observed || !summary.Contains("Sottrazione alle")),
			$"The conclusion leaked unavailable information: {summary}");
		_sliceSummaryEnd!.EmitSignal(BaseButton.SignalName.Pressed);
		SliceRequire(_sliceSummaryRetry is { Visible: true } && _sliceSummaryTitle!.Text == "Prova conclusa",
			"The branch did not reach its playable conclusion.");
	}

	private void AssertBlackmarshComposedFood()
	{
		var world = _sim.GetWorldView();
		var consumed = world.RecentFacts.Count(f => f.Kind == "FoodConsumed") * 4;
		SliceRequire(consumed + world.Actors.Sum(a => a.Food) + world.Stores.Sum(s => s.Food) == 18,
			"The composed routes created or lost food.");
	}

	private void CaptureBlackmarshComposed(string outputDir, string file) =>
		SliceRequire(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(outputDir, file)) == Error.Ok,
			$"Could not capture {file}.");
}
