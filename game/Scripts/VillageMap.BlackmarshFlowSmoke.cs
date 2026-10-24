using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Task 7: the Blackmarsh introduction, wait, store button and knowledge-limited conclusion.</summary>
public partial class VillageMap
{
	private async void RunBlackmarshFlowSmoke(string outputDir)
	{
		try
		{
			Directory.CreateDirectory(outputDir);
			var initialSave = Path.Combine(outputDir, "initial-session.json");
			SliceRequire(_paused && _sliceIntro is { Visible: true } && _sliceBegin is not null
				&& _sliceWaitButton is not null && _sliceSummaryButton is not null
				&& _blackmarshGuideText is not null && _blackmarshGuideText.Text.Contains("chiave")
				&& !_blackmarshGuideText.Text.Contains("Predone"),
				"The Blackmarsh entry did not explain the known situation without revealing the raider.");
			await SliceFrames(3);
			CaptureBlackmarshFlow(outputDir, "entry.png");
			SaveGameToPath(initialSave);
			var pausedAt = _view.Now;
			await SliceFrames(6);
			SliceRequire(_view.Now == pausedAt, "The intro let time advance before Comincia.");
			_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceFrames(3);
			SliceRequire(!_paused && !_sliceIntro!.Visible, "Comincia did not start the Blackmarsh clock.");

			// Give the player a cancellable wait, then pass enough time for the unseen raid.
			_sliceWaitButton!.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_sliceWaiting, "The shared wait button did not start Blackmarsh's wait.");
			_sliceWaitButton.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(!_sliceWaiting, "The shared wait button did not stop the wait.");
			for (var i = 0; i < 2; i++)
			{
				var from = _view.Now;
				_sliceWaitButton.EmitSignal(BaseButton.SignalName.Pressed);
				await SliceWaitUntilStopped();
				SliceRequire(_view.Now > from && _view.Now <= from.Plus(Duration.FromMinutes(5)),
					"Waiting advanced the clock twice or beyond five minutes.");
			}
			SliceRequire(_sim.GetWorldView().Stores.Single(s => s.Id == BlackmarshWatchhouse.Ids.VasanCache).Food == 4
				&& _view.Observations.Count == 0 && _view.Food == 10,
				"The unseen branch did not reach the raid checkpoint while the player stayed uninformed.");
			_sliceSummaryButton!.EmitSignal(BaseButton.SignalName.Pressed);
			var ignored = _sliceSummaryText!.Text;
			SliceRequire(_paused && ignored.Contains("Le tue razioni: 10")
				&& ignored.Contains("non hai osservato") && !ignored.Contains("Vasan")
				&& !ignored.Contains("4 razioni") && !ignored.Contains("0 razioni"),
				$"The unseen conclusion leaked hidden supplies or a theft: {ignored}");
			var frozenAt = _view.Now;
			_sliceSummaryEnd!.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceFrames(5);
			SliceRequire(_sliceSummaryRetry is { Visible: true } && _sliceSummaryContinue is { Visible: true }
				&& _view.Now == frozenAt && _paused,
				"Ending the unseen branch changed time or omitted the conclusion actions.");
			CaptureBlackmarshFlow(outputDir, "unseen-conclusion.png");
			_sliceSummaryContinue!.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(!_paused && _sliceSummary is { Visible: false },
				"Continua did not resume Blackmarsh after the conclusion.");

			// Start the recovery branch from the same saved session, without changing world internals.
			LoadGameFromPath(initialSave);
			SetPaused(false);
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
				&& _selectedStore == BlackmarshWatchhouse.Ids.ShedStore && !_storeTake.Disabled,
				"The player could not reach and select the shed store through the UI path.");
			_storeTake.EmitSignal(BaseButton.SignalName.Pressed);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Food == 14 && _sim.GetWorldView().Stores.Single(s => s.Id == BlackmarshWatchhouse.Ids.ShedStore).Food == 0,
				"The recovery button did not transfer the four rations.");
			_sliceSummaryButton.EmitSignal(BaseButton.SignalName.Pressed);
			var recovered = _sliceSummaryText!.Text;
			SliceRequire(_paused && recovered.Contains("Le tue razioni: 14")
				&& recovered.Contains("Scorte nella rimessa: 0")
				&& !recovered.Contains("Vasan") && !recovered.Contains("Provviste dei Vasan"),
				$"The recovery conclusion disclosed hidden supplies or missed the player's food: {recovered}");
			var recoveredAt = _view.Now;
			await SliceFrames(5);
			SliceRequire(_view.Now == recoveredAt, "The open recovery summary advanced the clock.");
			_sliceSummaryEnd.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceFrames(3);
			SliceRequire(_sliceSummaryTitle!.Text == "Prova conclusa" && _sliceSummaryRetry!.Visible
				&& _view.Now == recoveredAt && _paused,
				"The recovery conclusion did not freeze or show Riprova.");
			CaptureBlackmarshFlow(outputDir, "recovered-conclusion.png");
			GD.Print("BLACKMARSH FLOW SMOKE OK: shared wait, unseen branch, four-ration recovery, frozen conclusions");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"BLACKMARSH FLOW SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private void CaptureBlackmarshFlow(string outputDir, string file) =>
		SliceRequire(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(outputDir, file)) == Error.Ok,
			$"Could not capture {file}.");
}
