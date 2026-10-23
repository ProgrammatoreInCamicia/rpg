using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Checks the Blackmarsh scene's real entry, pause, save isolation and two scene reloads.</summary>
public partial class VillageMap
{
	private static int _blackmarshEntryPhase;
	private static byte[]? _blackmarshInitial;

	private async void RunBlackmarshEntrySmoke(string outputDir)
	{
		try
		{
			Directory.CreateDirectory(outputDir);
			using var snapshot = new MemoryStream();
			_sim.Save(snapshot);
			var bytes = snapshot.ToArray();
			SliceRequire(BlackmarshWatchhouseMode && _paused && _view.Now.Hour == 19
				&& _view.Now.Minute == 57 && _view.Position == BlackmarshWatchhouse.Ids.PlayerStart
				&& _view.Food == 10 && _view.Observations.Count == 0
				&& _sim.Player == BlackmarshWatchhouse.Ids.Player
				&& _sim.GetWorldView().Stores.Single(s => s.Id == BlackmarshWatchhouse.Ids.ShedStore).Food == 4
				&& _sliceIntro is null && _queue.Count == 0 && _running is null,
				"Blackmarsh did not open at the Task 1 state, paused and without slice UI.");
			SliceRequire(SavePath == BlackmarshSavePath && SavePath != VillageSavePath
				&& SavePath != SliceSavePath && CanRetryScene,
				"Blackmarsh save or retry is not separated from existing entries.");
			if (_blackmarshEntryPhase == 0)
			{
				_blackmarshInitial = bytes;
				await SliceFrames(8);
				SliceRequire(GetViewport().GetTexture().GetImage()
					.SavePng(Path.Combine(outputDir, "entry-paused.png")) == Error.Ok,
					"Could not capture the Blackmarsh opening scene.");
				using var still = new MemoryStream();
				_sim.Save(still);
				SliceRequire(still.ToArray().SequenceEqual(bytes), "Blackmarsh changed while paused.");
				var blackmarshPath = ProjectSettings.GlobalizePath(BlackmarshSavePath);
				var villagePath = ProjectSettings.GlobalizePath(VillageSavePath);
				var slicePath = ProjectSettings.GlobalizePath(SliceSavePath);
				var oldBlackmarsh = File.Exists(blackmarshPath) ? File.ReadAllBytes(blackmarshPath) : null;
				var oldVillage = File.Exists(villagePath) ? File.ReadAllBytes(villagePath) : null;
				var oldSlice = File.Exists(slicePath) ? File.ReadAllBytes(slicePath) : null;
				try
				{
					SaveGame();
					SliceRequire(File.ReadAllBytes(blackmarshPath).SequenceEqual(bytes),
						"Blackmarsh save did not contain the opening session.");
					SliceRequire((oldVillage is null ? !File.Exists(villagePath)
						: File.ReadAllBytes(villagePath).SequenceEqual(oldVillage))
						&& (oldSlice is null ? !File.Exists(slicePath)
							: File.ReadAllBytes(slicePath).SequenceEqual(oldSlice)),
						"Blackmarsh save modified an existing entry's file.");
				}
				finally
				{
					if (oldBlackmarsh is null) File.Delete(blackmarshPath);
					else File.WriteAllBytes(blackmarshPath, oldBlackmarsh);
				}
				SetPaused(false);
				var resumedAt = _view.Now;
				for (var frame = 0; frame < 90 && _view.Now == resumedAt; frame++)
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				SliceRequire(_view.Now > resumedAt,
					"Blackmarsh clock did not resume.");
				SetPaused(true);
			}
			else
				SliceRequire(_blackmarshInitial is not null && bytes.SequenceEqual(_blackmarshInitial),
					$"Blackmarsh retry {_blackmarshEntryPhase} did not reproduce the original session.");
			if (_blackmarshEntryPhase < 2)
			{
				_blackmarshEntryPhase++;
				_gameMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
				_gameMenu.GetChildren().OfType<VBoxContainer>().Single().GetChildren().OfType<Button>()
					.Single(button => button.Text == "Riprova").EmitSignal(BaseButton.SignalName.Pressed);
				return;
			}
			GD.Print("BLACKMARSH ENTRY SMOKE OK: paused entry, clock, isolated save, two scene retries");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"BLACKMARSH ENTRY SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}
}
