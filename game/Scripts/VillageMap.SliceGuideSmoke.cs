using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Exercises the Task 4 controls from the prepared scene without changing Sim state by hand.</summary>
public partial class VillageMap
{
	private async void RunSliceGuideSmoke(string outputDir)
	{
		try
		{
			System.IO.Directory.CreateDirectory(outputDir);
			var granary = MappedVillageScenario.Ids.GranaryStore;
			SliceRequire(_storeLabels.TryGetValue(granary, out var label)
				&& !label.Text.Contains(": 40"),
				"The granary count is visible before the player reaches its access point.");
			SliceRequire(_sliceDepositPanel is { Visible: false }, "The deposit controls appear from too far away.");
			SliceRequire(_sliceGuideText is not null &&
				(_view.VisibleActors.Any(a => a.Id == MappedVillageScenario.Ids.Guard)
					== _sliceGuideText.Text.Contains("Guardia in vista")),
				"The guidance reveals a guard who is not currently visible.");
			await SliceFrames(3);
			CaptureSliceGuide(outputDir, "entry-guide.png");
			_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
			MoveTo(new GridPos(18, 5));
			await UntilSliceGuideIdle();
			SliceRequire(_view.Position == new GridPos(18, 5), "Could not reach the far side of the granary.");
			SliceRequire(_view.VisibleStores.Any(s => s.Id == granary) && _sliceDepositPanel!.Visible
				&& _storeLabels[granary].Visible, "The visible granary has no label or delivery command.");
			var foodBefore = _view.Food;
			var storeBefore = _sim.GetWorldView().Stores.Single(s => s.Id == granary).Food;
			_sliceDepositButton!.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_running is null && _view.Food == foodBefore
				&& _sim.GetWorldView().Stores.Single(s => s.Id == granary).Food == storeBefore
				&& _message.Text.Contains("rombo"),
				"A distant delivery changed supplies or failed to explain the access point.");
			CaptureSliceGuide(outputDir, "too-far.png");

			MoveTo(MappedVillageScenario.Ids.GranaryAccess);
			await UntilSliceGuideIdle();
			SliceRequire(_view.Position == MappedVillageScenario.Ids.GranaryAccess
				&& _sliceAccessMarker is { Visible: true }, "The accessible side of the store was not marked.");
			_sliceDepositButton.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_running is not null, "The three-ration delivery did not start through its button.");
			await UntilSliceGuideIdle();
			SliceRequire(_view.Food == foodBefore - 3
				&& _sim.GetWorldView().Stores.Single(s => s.Id == granary).Food == storeBefore + 3,
				"The delivery did not transfer exactly three rations.");
			CaptureSliceGuide(outputDir, "delivered.png");

			MoveTo(new GridPos(8, 3));
			await UntilSliceGuideIdle();
			var guard = MappedVillageScenario.Ids.Guard;
			SliceRequire(_view.VisibleActors.Any(a => a.Id == guard)
				&& _sliceGuideText!.Text.Contains("Guardia in vista"),
				"The guard is not visible or the guidance does not identify the visible figure.");
			TalkTo(guard);
			await UntilSliceGuideIdle();
			SliceRequire(_talkingTo == guard && _sliceGuidePanel is { Visible: false },
				"The guard dialog did not open cleanly.");
			CaptureSliceGuide(outputDir, "guard-talk.png");
			GD.Print("SLICE GUIDE SMOKE OK: distant refusal, three-ration delivery and guard approach");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"SLICE GUIDE SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private async Task UntilSliceGuideIdle()
	{
		for (var frame = 0; frame < 1200 && _running is not null; frame++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		SliceRequire(_running is null, "A slice movement or delivery exceeded the frame budget.");
		await SliceFrames(3);
	}

	private void CaptureSliceGuide(string outputDir, string name)
	{
		var path = System.IO.Path.Combine(outputDir, name);
		SliceRequire(GetViewport().GetTexture().GetImage().SavePng(path) == Error.Ok,
			$"Could not capture {name}.");
	}
}
