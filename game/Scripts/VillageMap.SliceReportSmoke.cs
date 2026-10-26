using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Exercises the report branch through the slice's existing conversation UI.</summary>
public partial class VillageMap
{
	private async void RunSliceReportSmoke(string outputDir)
	{
		try
		{
			System.IO.Directory.CreateDirectory(outputDir);
			var guard = MappedVillageScenario.Ids.Guard;
			SliceRequire(_view.Observations.Count == 0, "The initial slice has unexpected evidence.");
			_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
			MoveTo(new GridPos(6, 2));
			await UntilSliceGuideIdle();
			TalkTo(guard);
			await UntilSliceGuideIdle();
			SliceRequire(_talkingTo == guard && !SliceReportButtonAvailable()
				&& SliceReportDialogText().Contains("Non hai ancora"),
				"The guard offered a theft report before the player observed one.");
			CaptureSliceGuide(outputDir, "no-report.png");
			CloseTalk();

			ResetSliceSmokeSession();
			CompleteSlicePreparation(new MoveToSpotCommand { Actor = _sim.Player,
				To = Spot.CenterOf(new GridPos(8, 3)) });
			var closed = _sim.Execute(new DoorCommand { Actor = _sim.Player,
				At = MappedVillageScenario.Ids.InnDoor, Open = false });
			SliceRequire(closed.Success, closed.Message);
			CompleteSlicePreparation(new MoveToSpotCommand { Actor = _sim.Player,
				To = Spot.CenterOf(new GridPos(16, 7)) });
			_sim.FacePlayer(0, -1);
			var torch = _sim.Execute(new TorchCommand { Actor = _sim.Player, Lit = true, StowShield = true });
			SliceRequire(torch.Success, torch.Message);
			Refresh();
			await SliceWaitOnce();
			_sliceWaitButton!.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceWaitUntilStopped();
			var observed = _view.Observations.Single(o => o.Kind == "Theft"
				&& o.Store == MappedVillageScenario.Ids.GranaryStore);
			SliceRequire(observed.Source is null && observed.Perceived == "Seen"
				&& !observed.ToldTo.Contains(guard), "Theft evidence was not first-hand and untold.");
			MoveTo(new GridPos(8, 3));
			await UntilSliceGuideIdle();
			ClickDoor(MappedVillageScenario.Ids.InnDoor);
			await UntilSliceGuideIdle();
			MoveTo(new GridPos(6, 2));
			await UntilSliceGuideIdle();
			TalkTo(guard);
			await UntilSliceGuideIdle();
			SliceRequire(_talkingTo == guard && SliceReportButtonAvailable()
				&& SliceReportDialogText().Contains("Hai notato il furto"),
				"The observed theft is not offered in the guard conversation.");
			var button = _people.GetChildren().OfType<Button>()
				.Single(b => b.Text.StartsWith("Riferisci il furto", StringComparison.Ordinal));
			button.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_view.Action?.Kind == "Report" && _running is not null
				&& _message.Text.Contains("Rapporto avviato")
				&& _status.Text.Contains("rimanenti")
				&& _tokens[_view.Id].GetNode<ProgressBar>("ActionProgress").Visible
				&& !_view.Observations.Single(o => o.Id == observed.Id).ToldTo.Contains(guard),
				"Starting the report did not show a timer or was confused with completion.");
			await SliceFrames(3);
			SliceRequire(SliceReportDialogText().Contains("Rapporto in corso"),
				"The conversation does not show the report in progress.");
			CaptureSliceGuide(outputDir, "report-started.png");
			await UntilSliceGuideIdle();
			SliceRequire(!_tokens[_view.Id].GetNode<ProgressBar>("ActionProgress").Visible,
				"The report timer remained over the player after completion.");
			var reported = _view.Observations.Single(o => o.Id == observed.Id);
			SliceRequire(reported.ToldTo.Contains(guard) && reported.Source == observed.Source
				&& reported.Origin == observed.Origin && reported.Store == observed.Store
				&& reported.Amount == observed.Amount && reported.Culprit == observed.Culprit
				&& _message.Text.Contains("Rapporto concluso"),
				"The completed report lost the observed content or completion feedback.");
			SliceRequire(!SliceReportButtonAvailable()
				&& SliceReportDialogText().Contains("Osserva come reagisce"),
				"The already-reported theft is still offered as a new report.");
			SliceRequire(_sim.GetWorldView().Actors.Single(a => a.Id == guard).Knowledge
				.Count(o => o.Origin == observed.Origin) == 1,
				"The guard has duplicate evidence for the theft.");
			ToggleJournal();
			SliceRequire(_journalText.Text.Contains("Rapporto alla guardia: completato")
				&& _journalText.Text.Contains("Furto visto")
				&& _journalText.Text.Contains($"{observed.Amount} razioni"),
				"The diary lost the distinction between observation and completed report.");
			await SliceFrames(3);
			CaptureSliceGuide(outputDir, "report-complete.png");
			GD.Print("SLICE REPORT SMOKE OK: no premature topic, report started/completed, evidence preserved and not duplicated");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"SLICE REPORT SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private bool SliceReportButtonAvailable() => _people.GetChildren().OfType<Button>()
		.Any(b => b.Text.StartsWith("Riferisci il furto", StringComparison.Ordinal) && !b.Disabled);

	private string SliceReportDialogText() => string.Join(" ", _people.GetChildren().OfType<Label>()
		.Select(label => label.Text));
}
