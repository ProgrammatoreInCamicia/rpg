using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Exercises the slice summary on its two branches: the theft kept to oneself, then reported.</summary>
public partial class VillageMap
{
	private async void RunSliceSummarySmoke(string outputDir)
	{
		try
		{
			System.IO.Directory.CreateDirectory(outputDir);
			var guard = MappedVillageScenario.Ids.Guard;
			_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceFrames(3);
			_sliceSummaryButton!.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_paused && SliceSummaryText().Contains("non sai di furti")
				&& SliceSummaryText().Contains("non vedi nessuno sorvegliare")
				&& !SliceSummaryText().Contains("Rapporto"),
				"The opening summary claims knowledge the player does not have.");
			var summaryAt = _view.Now;
			Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = true });
			Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = false });
			await SliceFrames(3);
			SliceRequire(_paused && _view.Now == summaryAt,
				"Space resumed world time behind the open summary.");
			_quickPause.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceFrames(3);
			SliceRequire(_paused && _view.Now == summaryAt && _quickPause.ButtonPressed,
				"The hotbar resumed world time behind the open summary.");
			_sliceSummaryContinue!.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(!_paused && _sliceSummary is { Visible: false }, "Continue did not give time back.");
			_gameMenuButton.GrabFocus(); // as after a click on it
			Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = true });
			Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = false });
			await SliceFrames(3);
			SliceRequire(_paused && !_gameMenu.Visible, "Space pressed the focused menu button instead of only pausing.");
			SetPaused(false);

			// Branch 1: the theft is seen and kept to oneself.
			ResetSliceSmokeSession();
			CompleteSlicePreparation(new MoveToSpotCommand { Actor = _sim.Player, To = Spot.CenterOf(new GridPos(8, 3)) });
			var closed = _sim.Execute(new DoorCommand { Actor = _sim.Player, At = MappedVillageScenario.Ids.InnDoor, Open = false });
			SliceRequire(closed.Success, closed.Message);
			CompleteSlicePreparation(new MoveToSpotCommand { Actor = _sim.Player, To = Spot.CenterOf(new GridPos(16, 7)) });
			_sim.FacePlayer(0, -1);
			var torch = _sim.Execute(new TorchCommand { Actor = _sim.Player, Lit = true, StowShield = true });
			SliceRequire(torch.Success, torch.Message);
			Refresh();
			await SliceWaitOnce();
			_sliceWaitButton!.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceWaitUntilStopped();
			SliceRequire(_view.Observations.Any(o => o.Kind == "Theft"), "The theft was not observed.");
			_sliceSummaryButton.EmitSignal(BaseButton.SignalName.Pressed);
			var ignored = SliceSummaryText();
			SliceRequire(ignored.Contains("l'hai visto") && ignored.Contains("Colpevole")
				&& ignored.Contains("non l'hai riferito") && ignored.Contains("Altri potrebbero saperlo")
				&& ignored.Contains("le conti solo dalla casella"),
				$"The kept-quiet summary is wrong:\n{ignored}");
			_sliceSummaryEnd!.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_sliceSummaryTitle!.Text == "Prova conclusa" && _sliceSummaryQuit!.Visible && _paused,
				"Ending the slice did not show the conclusion.");
			await SliceFrames(3);
			if (DisplayServer.GetName() != "headless")
				CaptureSliceGuide(outputDir, "summary-ignored.png");
			_sliceSummaryContinue.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(!_paused && !_sliceEnded && _sliceSummary!.Visible == false,
				"Continuing after the end did not give the game back.");

			// Branch 2: the same theft reported to the guard, who is then seen at her post.
			MoveTo(new GridPos(8, 3));
			await UntilSliceGuideIdle();
			ClickDoor(MappedVillageScenario.Ids.InnDoor);
			await UntilSliceGuideIdle();
			MoveTo(new GridPos(6, 2));
			await UntilSliceGuideIdle();
			TalkTo(guard);
			await UntilSliceGuideIdle();
			_people.GetChildren().OfType<Button>().Single(b => b.Text.StartsWith("Riferisci il furto", StringComparison.Ordinal))
				.EmitSignal(BaseButton.SignalName.Pressed);
			await UntilSliceGuideIdle();
			CloseTalk();
			SliceRequire(_view.Observations.Any(o => o.ToldTo.Contains(guard)), "The report was not completed.");
			MoveTo(MappedVillageScenario.Ids.GranaryAccess);
			await UntilSliceGuideIdle();
			for (var second = 0; second < 600 && _sim.GetWorldView().Actors.Single(a => a.Id == guard).Action?.Kind != "Guard"; second++)
				_sim.Advance(Duration.FromSeconds(1));
			Refresh();
			_sliceSummaryButton.EmitSignal(BaseButton.SignalName.Pressed);
			var reported = SliceSummaryText();
			SliceRequire(reported.Contains("hai riferito il furto alla guardia") && reported.Contains("sorvegliare il granaio")
				&& !reported.Contains("non vedi nessuno") && reported.Contains("Scorte del granaio: 32 razioni")
				&& !reported.Contains("restitu"),
				$"The reported summary is wrong:\n{reported}");
			await SliceFrames(3);
			if (DisplayServer.GetName() != "headless")
				CaptureSliceGuide(outputDir, "summary-reported.png");
			GD.Print("SLICE SUMMARY SMOKE OK: empty start, kept-quiet branch with explicit end, reported branch with the guard seen at her post");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"SLICE SUMMARY SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private string SliceSummaryText() => _sliceSummaryText?.Text ?? "";
}
