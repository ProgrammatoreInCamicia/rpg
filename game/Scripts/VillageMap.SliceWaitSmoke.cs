using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using System.Diagnostics;

namespace RpgSandbox.Game;

/// <summary>Focused integration checks for the slice's accelerated, interruptible clock.</summary>
public partial class VillageMap
{
	private async void RunSliceWaitSmoke(string outputDir)
	{
		try
		{
			System.IO.Directory.CreateDirectory(outputDir);
			SliceRequire(_paused && _sliceWaitButton is { Disabled: true }, "Wait was available during the introduction.");
			_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
			_sliceWaitButton!.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_sliceWaiting, "The wait button did not start waiting.");
			var started = _view.Now;
			await SliceFrames(4);
			SliceRequire(_view.Now > started && _view.Now.Since(started).Seconds <= 4,
				"One frame advanced the world twice during waiting.");
			SetPaused(true);
			SliceRequire(!_sliceWaiting, "Pause did not cancel the wait.");
			var pausedAt = _view.Now;
			await SliceFrames(8);
			SliceRequire(_view.Now == pausedAt, "Time advanced while paused after the wait.");
			SetPaused(false);
			_sliceWaitButton.EmitSignal(BaseButton.SignalName.Pressed);
			MoveTo(new GridPos(3, 2));
			SliceRequire(!_sliceWaiting && _view.Move is not null, "A new walking order did not interrupt waiting.");
			Stop();

			ResetSliceSmokeSession();
			using var initial = new MemoryStream();
			_sim.Save(initial);
			initial.Position = 0;
			var loaded = SimulationSession.TryLoad(initial);
			SliceRequire(loaded.Success, $"Could not copy the baseline: {loaded.Error}");
			var ordinary = loaded.Session!;
			var beforeHidden = _view.Now;
			await SliceWaitOnce();
			await SliceWaitOnce();
			SliceRequire(_view.Observations.Count == 0, "Waiting revealed a theft outside the player's view.");
			SliceRequire(_sim.GetWorldView().Stores.Single(s => s.Id == MappedVillageScenario.Ids.GranaryStore).Food == 32,
				"The autonomous theft did not occur while the player waited out of sight.");
			ordinary.Advance(_view.Now.Since(beforeHidden));
			using var acceleratedSave = new MemoryStream();
			using var ordinarySave = new MemoryStream();
			_sim.Save(acceleratedSave);
			ordinary.Save(ordinarySave);
			SliceRequire(acceleratedSave.ToArray().SequenceEqual(ordinarySave.ToArray()),
				"Accelerated waiting diverged from ordinary simulation at the same checkpoint.");

			ResetSliceSmokeSession();
			CompleteSlicePreparation(new MoveToSpotCommand { Actor = _sim.Player, To = Spot.CenterOf(new GridPos(8, 3)) });
			var closed = _sim.Execute(new DoorCommand { Actor = _sim.Player, At = MappedVillageScenario.Ids.InnDoor, Open = false });
			SliceRequire(closed.Success, closed.Message);
			CompleteSlicePreparation(new MoveToSpotCommand { Actor = _sim.Player, To = Spot.CenterOf(new GridPos(16, 7)) });
			_sim.FacePlayer(0, -1);
			var torch = _sim.Execute(new TorchCommand { Actor = _sim.Player, Lit = true, StowShield = true });
			SliceRequire(torch.Success, torch.Message);
			Refresh();
			var clock = Stopwatch.StartNew();
			await SliceWaitOnce();
			SliceRequire(_view.Observations.Count == 0, "The first five-minute wait ended on an unexpected observation.");
			_sliceWaitButton.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceWaitUntilStopped();
			clock.Stop();
			var theft = _view.Observations.SingleOrDefault(o => o.Kind == "Theft");
			SliceRequire(theft is { Perceived: "Seen", Culprit: { } }, "The lookout did not see and identify the theft.");
			SliceRequire(_view.Now.Since(theft!.ObservedAt).Seconds <= 1 && _view.Now < _sliceWaitUntil,
				"The wait continued past the first player observation.");
			ToggleJournal();
			SliceRequire(_journalText.Text.Contains("razioni"), "The journal did not show the observed theft.");
			var capture = System.IO.Path.Combine(outputDir, "theft-observed.png");
			SliceRequire(GetViewport().GetTexture().GetImage().SavePng(capture) == Error.Ok,
				"Could not capture the observed theft.");
			GD.Print($"SLICE WAIT SMOKE OK: first observation in {clock.Elapsed.TotalSeconds:F1}s real after lookout; stopped at {_view.Now}");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"SLICE WAIT SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private void ResetSliceSmokeSession()
	{
		CancelSliceWait("Attesa interrotta.");
		_sim = PlayableSlice.CreateSession();
		_running = null;
		_walking = false;
		_pendingTicks = 0;
		_queue.Clear();
		_lightKey = "";
		_peopleKey = "";
		Refresh();
	}

	private void CompleteSlicePreparation(Command command)
	{
		var started = _sim.Execute(command);
		SliceRequire(started.Success && started.Action is not null, started.Message);
		var result = _sim.AdvanceUntilCompleted(started.Action!.Value, Duration.FromMinutes(10));
		SliceRequire(result.Outcome == AdvanceOutcome.Completed, $"Preparation did not finish: {result.Outcome}");
	}

	private async Task SliceWaitOnce()
	{
		_sliceWaitButton!.EmitSignal(BaseButton.SignalName.Pressed);
		await SliceWaitUntilStopped();
	}

	private async Task SliceWaitUntilStopped()
	{
		for (var frame = 0; frame < 310 && _sliceWaiting; frame++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		SliceRequire(!_sliceWaiting, "The five-minute wait exceeded its frame budget.");
	}

	private async Task SliceFrames(int count)
	{
		for (var frame = 0; frame < count; frame++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private static void SliceRequire(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
}
