using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using System.Diagnostics;

namespace RpgSandbox.Game;

/// <summary>Task 8: two full UI journeys from separate PlayableSlice scene loads.</summary>
public partial class VillageMap
{
	private static int _playableSliceSmokePhase;
	private static byte[]? _playableSliceSmokeInitial;
	private Stopwatch? _playableSliceClock;

	private async void RunPlayableSliceSmoke(string outputDir)
	{
		var clock = Stopwatch.StartNew();
		_playableSliceClock = clock;
		try
		{
			Directory.CreateDirectory(outputDir);
			SliceRequire(DisplayServer.GetName() != "headless", "The composed smoke needs a renderer for screenshots.");
			using var initial = new MemoryStream();
			_sim.Save(initial);
			if (_playableSliceSmokePhase == 0)
			{
				_playableSliceSmokeInitial = initial.ToArray();
				await CapturePlayableSlice(outputDir, "entry.png");
				await RunPlayableSliceIgnore(outputDir);
				GD.Print($"SLICE SMOKE ignore OK: {_view.Now}, {clock.Elapsed.TotalSeconds:F1}s real, theft known but not reported");
				_playableSliceSmokePhase = 1;
				_sliceSummaryRetry!.EmitSignal(BaseButton.SignalName.Pressed);
				return;
			}
			SliceRequire(_playableSliceSmokePhase == 1 && _playableSliceSmokeInitial is not null
				&& initial.ToArray().SequenceEqual(_playableSliceSmokeInitial),
				"The report branch did not restart from the same factory state.");
			CheckPlayableSliceEntry();
			await RunPlayableSliceReport(outputDir);
			GD.Print($"SLICE SMOKE report OK: {_view.Now}, {clock.Elapsed.TotalSeconds:F1}s real, guard visibly at post");
			GD.Print("SLICE SMOKE OK: two clean factory starts, observation, ignore/report, guard post and save resume");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"SLICE SMOKE FAIL (branch {_playableSliceSmokePhase}, {_view.Now}, {clock.Elapsed.TotalSeconds:F1}s): {error}");
			GetTree().Quit(1);
		}
	}

	private void CheckPlayableSliceEntry()
	{
		SliceRequire(_paused && _sliceIntro is { Visible: true } && _view.Now.Hour == 19 && _view.Now.Minute == 27
			&& _view.Position == MappedVillageScenario.Ids.PlayerStart && _view.Observations.Count == 0
			&& _sim.GetWorldView().Stores.Single(s => s.Id == MappedVillageScenario.Ids.GranaryStore).Food == 40
			&& _sim.GetWorldView().Actors.Single(a => a.Id == MappedVillageScenario.Ids.Guard).GuardDuty is null,
			"The playable entry is not the unobserved pre-theft state.");
	}

	private async Task RunPlayableSliceIgnore(string outputDir)
	{
		CheckPlayableSliceEntry();
		await ReachPlayableSliceLookout(torch: false);
		await ObservePlayableSliceTheft(identify: false);
		var guard = _sim.GetWorldView().Actors.Single(a => a.Id == MappedVillageScenario.Ids.Guard);
		SliceRequire(_view.Observations.All(o => o.ToldTo.Count == 0)
			&& guard.GuardDuty is null && guard.Knowledge.Count == 0,
			"The ignored branch gave the guard knowledge or a post without a report.");
		_sliceSummaryButton!.EmitSignal(BaseButton.SignalName.Pressed);
		SliceRequire(SliceSummaryText().Contains("non l'hai riferito") && _paused,
			"The ignored branch's summary falsely claims a report.");
		_sliceSummaryEnd!.EmitSignal(BaseButton.SignalName.Pressed);
		SliceRequire(_sliceSummaryTitle!.Text == "Prova conclusa" && _sliceSummaryRetry is { Visible: true },
			"The ignored branch cannot end or retry.");
		await CapturePlayableSlice(outputDir, "ignored-ending.png");
	}

	private async Task RunPlayableSliceReport(string outputDir)
	{
		await ReachPlayableSliceLookout(torch: true);
		await ObservePlayableSliceTheft(identify: true);
		ToggleJournal();
		SliceRequire(_journalText.Text.Contains("Furto visto"), "The observed theft is missing from the journal.");
		await CapturePlayableSlice(outputDir, "observation.png");
		ToggleJournal();
		var guard = MappedVillageScenario.Ids.Guard;
		var observation = _view.Observations.Single(o => o.Kind == "Theft"
			&& o.Store == MappedVillageScenario.Ids.GranaryStore);
		MoveTo(new GridPos(8, 3));
		await UntilSliceGuideIdle();
		ClickDoor(MappedVillageScenario.Ids.InnDoor);
		await UntilSliceGuideIdle();
		MoveTo(new GridPos(6, 2));
		await UntilSliceGuideIdle();
		TalkTo(guard);
		await UntilSliceGuideIdle();
		SliceRequire(_talkingTo == guard && SliceReportButtonAvailable()
			&& !PlayableSliceReported(observation), "The report option was unavailable before telling the guard.");
		_people.GetChildren().OfType<Button>()
			.Single(button => button.Text.StartsWith("Riferisci il furto", StringComparison.Ordinal))
			.EmitSignal(BaseButton.SignalName.Pressed);
		SliceRequire(_view.Action?.Kind == "Report" && !PlayableSliceReported(observation),
			"The report was marked complete when it only started.");
		await UntilSliceGuideIdle();
		SliceRequire(_view.Observations.Count(o => o.ToldTo.Contains(guard)) == 1
			&& _sim.GetWorldView().Actors.Single(a => a.Id == guard).Knowledge.Count(o => o.Origin == observation.Origin) == 1
			&& !SliceReportButtonAvailable(), "The report did not finish exactly once.");
		_sliceSummaryButton!.EmitSignal(BaseButton.SignalName.Pressed);
		SliceRequire(SliceSummaryText().Contains("hai riferito il furto alla guardia") && _paused,
			"The completed report is absent from the player summary.");
		await CapturePlayableSlice(outputDir, "report.png");
		_sliceSummaryContinue!.EmitSignal(BaseButton.SignalName.Pressed);
		CloseTalk();

		var path = ProjectSettings.GlobalizePath(SliceSavePath);
		var previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
		try
		{
			SetPaused(true);
			SaveGame();
			SliceRequire(File.Exists(path), "The report checkpoint was not saved.");
			var savedAt = _view.Now;
			SetPaused(false);
			await SliceFrames(5);
			SliceRequire(_view.Now > savedAt, "The world did not continue after saving.");
			LoadGame();
			SliceRequire(_view.Now == savedAt && _view.Observations.Count(o => o.ToldTo.Contains(guard)) == 1
				&& _queue.Count == 0 && !_sliceWaiting, "Loading did not resume the single completed report.");
			_sliceSummaryButton!.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(SliceSummaryText().Contains("hai riferito il furto alla guardia"),
				"The reloaded summary lost the completed report.");
			await CapturePlayableSlice(outputDir, "resumed-from-save.png");
			_sliceSummaryContinue!.EmitSignal(BaseButton.SignalName.Pressed);
			MoveTo(MappedVillageScenario.Ids.GranaryAccess);
			await UntilSliceGuideIdle();
			for (var wait = 0; wait < 5 && !_view.VisibleActors.Any(a => a.GuardingStore == MappedVillageScenario.Ids.GranaryStore); wait++)
			{
				SliceRequire(_sliceWaitButton is { Disabled: false }, "Waiting for the guard became unavailable.");
				await SliceWaitOnce();
			}
			SliceRequire(_view.VisibleActors.Any(a => a.Id == guard && a.GuardingStore == MappedVillageScenario.Ids.GranaryStore)
				&& _view.Observations.Count(o => o.ToldTo.Contains(guard)) == 1,
				"The guard never became visibly stationed after the report.");
			GD.Print($"SLICE SMOKE guard visible at {_view.Now}, {_playableSliceClock?.Elapsed.TotalSeconds:F1}s real from Comincia");
			_sliceSummaryButton!.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(SliceSummaryText().Contains("sorvegliare il granaio")
				&& SliceSummaryText().Contains("hai riferito il furto alla guardia"),
				"The player summary did not connect report and visible guard post.");
			await CapturePlayableSlice(outputDir, "guard-at-post.png");
		}
		finally
		{
			if (previous is null) File.Delete(path);
			else File.WriteAllBytes(path, previous);
		}
	}

	private async Task ReachPlayableSliceLookout(bool torch)
	{
		_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
		_playableSliceClock?.Restart();
		MoveTo(new GridPos(8, 3));
		await UntilSliceGuideIdle();
		SliceRequire(_view.Now.Hour == 19 && _view.Now.Minute < 32, "The player reached the inn door after the theft window.");
		ClickDoor(MappedVillageScenario.Ids.InnDoor);
		await UntilSliceGuideIdle();
		SliceRequire(_view.Doors.Any(d => d.At == MappedVillageScenario.Ids.InnDoor && d.Open == false),
			"The inn door was not closed through its UI command.");
		MoveTo(new GridPos(16, 6));
		await UntilSliceGuideIdle();
		SliceRequire(_view.Position == new GridPos(16, 6), "The player did not reach the granary lookout.");
		FaceToward(CellToScreen(new GridPos(16, 5)));
		SliceRequire(_view.Facing is { Y: < 0 }, "The player is not facing the granary.");
		if (torch)
		{
			RequestTorchToggle();
			if (_torchShieldConfirm.Visible)
				_torchShieldConfirm.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
			SliceRequire(_view.TorchLitUntil is not null, "The UI did not light the lookout's torch.");
		}
	}

	private async Task ObservePlayableSliceTheft(bool identify)
	{
		SliceRequire(_view.Observations.Count == 0, "The player learned of the theft before observing it.");
		for (var wait = 0; wait < 3 && _view.Observations.Count == 0; wait++)
		{
			SliceRequire(_sliceWaitButton is { Disabled: false }, "The waiting command is unavailable at the lookout.");
			await SliceWaitOnce();
		}
		var theft = _view.Observations.SingleOrDefault(o => o.Kind == "Theft"
			&& o.Store == MappedVillageScenario.Ids.GranaryStore);
		GD.Print($"SLICE SMOKE observation diagnostic: count={_view.Observations.Count}, perceived={theft?.Perceived}, "
			+ $"culprit={theft?.Culprit}, amount={theft?.Amount}, facing={_view.Facing}, "
			+ $"light={_view.MapLight?[5][16]}, food={_sim.GetWorldView().Stores.Single(s => s.Id == MappedVillageScenario.Ids.GranaryStore).Food}");
		SliceRequire(theft is { Perceived: "Seen" } && (!identify || theft.Culprit is not null) && theft.Amount == 8
			&& _sim.GetWorldView().Stores.Single(s => s.Id == MappedVillageScenario.Ids.GranaryStore).Food == 32,
			"The autonomous eight-ration theft was not seen and identified.");
		GD.Print($"SLICE SMOKE theft observed at {_view.Now} ({theft!.ObservedAt}, {theft.Amount} rations), "
			+ $"{_playableSliceClock?.Elapsed.TotalSeconds:F1}s real from Comincia");
	}

	/// <summary>Read from the current view: the observation captured earlier is a snapshot and never changes.</summary>
	private bool PlayableSliceReported(ObservationView observation) => _view.Observations
		.Any(o => o.Id == observation.Id && o.ToldTo.Contains(MappedVillageScenario.Ids.Guard));

	private async Task CapturePlayableSlice(string outputDir, string name)
	{
		await SliceFrames(3);
		var image = GetViewport().GetTexture().GetImage();
		SliceRequire(image.SavePng(Path.Combine(outputDir, name)) == Error.Ok, $"Could not capture {name}.");
	}
}
