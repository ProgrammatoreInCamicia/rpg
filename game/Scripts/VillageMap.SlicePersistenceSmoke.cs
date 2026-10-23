using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Task 7: save/load in one slice scene and two real scene reloads.</summary>
public partial class VillageMap
{
	private static int _slicePersistencePhase;
	private static byte[]? _slicePersistenceInitial;

	private async void RunSlicePersistenceSmoke(string outputDir)
	{
		try
		{
			System.IO.Directory.CreateDirectory(outputDir);
			using var baseline = new MemoryStream();
			_sim.Save(baseline);
			if (_slicePersistencePhase == 0)
			{
				_slicePersistenceInitial = baseline.ToArray();
				await CheckSlicePersistence(outputDir);
				_slicePersistencePhase = 1;
				_gameMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
				_gameMenu.GetChildren().OfType<VBoxContainer>().Single().GetChildren().OfType<Button>()
					.Single(button => button.Text == "Riprova").EmitSignal(BaseButton.SignalName.Pressed);
				return;
			}
			SliceRequire(_slicePersistenceInitial is not null && baseline.ToArray().SequenceEqual(_slicePersistenceInitial)
				&& _paused && _sliceIntro is { Visible: true } && _view.Observations.Count == 0
				&& _queue.Count == 0 && _running is null && !_walking && !_sliceWaiting
				&& _sliceSummary is { Visible: false } && !_gameMenu.Visible,
				$"Retry {_slicePersistencePhase} did not recreate the initial scene and session.");
			if (_slicePersistencePhase == 1)
			{
				_slicePersistencePhase = 2;
				_sliceSummaryButton!.EmitSignal(BaseButton.SignalName.Pressed);
				_sliceSummaryEnd!.EmitSignal(BaseButton.SignalName.Pressed);
				SliceRequire(_sliceSummaryRetry is { Visible: true }, "Retry is absent from the ending.");
				_sliceSummaryRetry!.EmitSignal(BaseButton.SignalName.Pressed);
				return;
			}
			SliceRequire(_slicePersistencePhase == 2, "Unexpected retry phase.");
			GD.Print("SLICE PERSISTENCE SMOKE OK: mid-walk, report, corrupted file, separate save, two scene retries");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"SLICE PERSISTENCE SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private async Task CheckSlicePersistence(string outputDir)
	{
		var slicePath = ProjectSettings.GlobalizePath(SliceSavePath);
		var villagePath = ProjectSettings.GlobalizePath(VillageSavePath);
		SliceRequire(SavePath == SliceSavePath && slicePath != villagePath, "The slice shares Village's save path.");
		var oldSlice = System.IO.File.Exists(slicePath) ? System.IO.File.ReadAllBytes(slicePath) : null;
		var oldVillage = System.IO.File.Exists(villagePath) ? System.IO.File.ReadAllBytes(villagePath) : null;
		try
		{
			_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
			MoveTo(new GridPos(6, 2));
			SliceRequire(_view.Move is not null, "The player did not begin walking.");
			var departure = _view.Now;
			var departureSpot = _view.Spot;
			for (var frame = 0; frame < 90 && _view.Move is not null && _view.Spot == departureSpot; frame++)
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			SliceRequire(_view.Move is not null && _view.Now > departure && _view.Spot != departureSpot,
				"The save was not taken after a real step and before arrival.");
			SetPaused(true);
			SaveGame();
			var walkingAt = _view.Now;
			var walkingSave = System.IO.File.ReadAllBytes(slicePath);
			SetPaused(false);
			await SliceFrames(5);
			SliceRequire(_view.Now > walkingAt, "The world did not advance after the walking save.");
			SetPaused(true);
			MoveTo(new GridPos(4, 2));
			SliceRequire(_queue.Count == 1, "The smoke did not queue a later walking order.");
			LoadGame();
			SliceRequire(_view.Now == walkingAt && _view.Move is not null && _walking
				&& _sliceIntro is { Visible: false } && _queue.Count == 0,
				"Mid-walk load did not restore the active move without a new preroll.");
			SetPaused(false);
			await UntilSliceGuideIdle();
			SliceRequire(_view.Position == new GridPos(6, 2) && _queue.Count == 0,
				"The loaded walk failed to finish or replayed an old queued order.");

			ResetSliceSmokeSession();
			CompleteSlicePreparation(new MoveToSpotCommand { Actor = _sim.Player, To = Spot.CenterOf(new GridPos(8, 3)) });
			var closed = _sim.Execute(new DoorCommand { Actor = _sim.Player,
				At = MappedVillageScenario.Ids.InnDoor, Open = false });
			SliceRequire(closed.Success, closed.Message);
			CompleteSlicePreparation(new MoveToSpotCommand { Actor = _sim.Player, To = Spot.CenterOf(new GridPos(16, 7)) });
			_sim.FacePlayer(0, -1);
			var torch = _sim.Execute(new TorchCommand { Actor = _sim.Player, Lit = true, StowShield = true });
			SliceRequire(torch.Success, torch.Message);
			Refresh();
			await SliceWaitOnce();
			_sliceWaitButton!.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceWaitUntilStopped();
			SliceRequire(_view.Observations.Any(o => o.Kind == "Theft"), "The player did not see the theft.");
			MoveTo(new GridPos(8, 3));
			await UntilSliceGuideIdle();
			ClickDoor(MappedVillageScenario.Ids.InnDoor);
			await UntilSliceGuideIdle();
			MoveTo(new GridPos(6, 2));
			await UntilSliceGuideIdle();
			TalkTo(MappedVillageScenario.Ids.Guard);
			await UntilSliceGuideIdle();
			_people.GetChildren().OfType<Button>().Single(button => button.Text.StartsWith("Riferisci il furto", StringComparison.Ordinal))
				.EmitSignal(BaseButton.SignalName.Pressed);
			await UntilSliceGuideIdle();
			var guard = MappedVillageScenario.Ids.Guard;
			SliceRequire(_view.Observations.Count(o => o.ToldTo.Contains(guard)) == 1, "The report was not completed once.");
			SetPaused(true);
			SaveGame();
			var reportedAt = _view.Now;
			var reportedSave = System.IO.File.ReadAllBytes(slicePath);
			SliceRequire(!reportedSave.SequenceEqual(walkingSave), "The later save did not replace the walking save.");
			var sessionBeforeCorruption = _sim;
			System.IO.File.WriteAllText(slicePath, "{broken json");
			LoadGame();
			SliceRequire(ReferenceEquals(_sim, sessionBeforeCorruption) && _view.Now == reportedAt
				&& _message.Text.StartsWith("Caricamento non riuscito:", StringComparison.Ordinal),
				"A corrupted save replaced the current session or hid the error.");
			System.IO.File.WriteAllBytes(slicePath, reportedSave);
			LoadGame();
			SliceRequire(_view.Now == reportedAt && _view.Observations.Count(o => o.ToldTo.Contains(guard)) == 1
				&& _sim.GetWorldView().Actors.Single(a => a.Id == guard).Knowledge
					.Count(o => _view.Observations.Any(v => v.Origin == o.Origin && v.ToldTo.Contains(guard))) == 1
				&& _queue.Count == 0 && _talkingTo is null && !_sliceWaiting,
				"Loading after the report duplicated evidence or left a transient command.");
			SetPaused(false);
			await SliceFrames(5);
			SliceRequire(_view.Observations.Count(o => o.ToldTo.Contains(guard)) == 1,
				"The report replayed after loading and resuming.");
			SliceRequire(oldVillage is null ? !System.IO.File.Exists(villagePath)
				: System.IO.File.ReadAllBytes(villagePath).SequenceEqual(oldVillage),
				"The slice modified Village's save.");
			GD.Print($"SLICE PERSISTENCE: saved mid-walk at {walkingAt}; report at {reportedAt}; corruption preserved session");
		}
		finally
		{
			if (oldSlice is null) System.IO.File.Delete(slicePath);
			else System.IO.File.WriteAllBytes(slicePath, oldSlice);
		}
	}
}
