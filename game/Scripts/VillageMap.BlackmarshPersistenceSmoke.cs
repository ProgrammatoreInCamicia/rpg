using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Task 8: scene save/load, active movement and actions, openings, rejected files and real retries.</summary>
public partial class VillageMap
{
	private static int _blackmarshPersistencePhase;
	private static byte[]? _blackmarshPersistenceInitial;

	private async void RunBlackmarshPersistenceSmoke(string outputDir)
	{
		try
		{
			Directory.CreateDirectory(outputDir);
			using var snapshot = new MemoryStream();
			_sim.Save(snapshot);
			var initial = snapshot.ToArray();
			if (_blackmarshPersistencePhase == 0)
			{
				_blackmarshPersistenceInitial = initial;
				await CheckBlackmarshPersistence(outputDir);
				_blackmarshPersistencePhase = 1;
				_gameMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
				_gameMenu.GetChildren().OfType<VBoxContainer>().Single().GetChildren().OfType<Button>()
					.Single(button => button.Text == "Riprova").EmitSignal(BaseButton.SignalName.Pressed);
				return;
			}
			SliceRequire(_blackmarshPersistenceInitial is not null
				&& initial.SequenceEqual(_blackmarshPersistenceInitial) && _paused
				&& _sliceIntro is { Visible: true } && _sliceSummary is { Visible: false }
				&& _queue.Count == 0 && _running is null && !_walking && !_sliceWaiting
				&& _pendingBarrier is null && _view.Food == 10,
				$"Blackmarsh retry {_blackmarshPersistencePhase} did not restore the initial scene.");
			if (_blackmarshPersistencePhase == 1)
			{
				_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
				for (var i = 0; i < 2; i++)
				{
					_sliceWaitButton!.EmitSignal(BaseButton.SignalName.Pressed);
					await SliceWaitUntilStopped();
				}
				SliceRequire(_view.Observations.Count == 0 && _view.Food == 10
					&& _sim.GetWorldView().Stores.Single(s => s.Id == BlackmarshWatchhouse.Ids.VasanCache).Food == 4,
					"The retry did not reach the uninformed non-recovery outcome.");
				_sliceSummaryButton!.EmitSignal(BaseButton.SignalName.Pressed);
				_sliceSummaryEnd!.EmitSignal(BaseButton.SignalName.Pressed);
				SliceRequire(_sliceSummaryRetry is { Visible: true }, "The ending has no Retry action.");
				_blackmarshPersistencePhase = 2;
				_sliceSummaryRetry!.EmitSignal(BaseButton.SignalName.Pressed);
				return;
			}
			SliceRequire(_blackmarshPersistencePhase == 2, "Unexpected Blackmarsh retry phase.");
			GD.Print("BLACKMARSH PERSISTENCE SMOKE OK: mid-walk, queued intent, opening/glass, mid-take, outcome, rejected files, two retries");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"BLACKMARSH PERSISTENCE SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private async Task CheckBlackmarshPersistence(string outputDir)
	{
		var blackmarshPath = ProjectSettings.GlobalizePath(BlackmarshSavePath);
		var villagePath = ProjectSettings.GlobalizePath(VillageSavePath);
		var slicePath = ProjectSettings.GlobalizePath(SliceSavePath);
		var oldBlackmarsh = File.Exists(blackmarshPath) ? File.ReadAllBytes(blackmarshPath) : null;
		var oldVillage = File.Exists(villagePath) ? File.ReadAllBytes(villagePath) : null;
		var oldSlice = File.Exists(slicePath) ? File.ReadAllBytes(slicePath) : null;
		try
		{
			SliceRequire(SavePath == BlackmarshSavePath && SavePath != VillageSavePath && SavePath != SliceSavePath,
				"Blackmarsh uses another scene's save path.");
			_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
			MoveTo(new GridPos(5, 6));
			var departureSpot = _view.Spot;
			for (var frame = 0; frame < 90 && _view.Move is not null && _view.Spot == departureSpot; frame++)
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			SliceRequire(_view.Move is not null && _view.Spot != departureSpot,
				"The walk save was not made after a real step and before arrival.");
			SetPaused(true);
			SaveGame();
			var walkingAt = _view.Now;
			var walkingSpot = _view.Spot;
			var walkingBytes = File.ReadAllBytes(blackmarshPath);
			var walkingReference = LoadBlackmarshSmokeReference(walkingBytes);
			SetPaused(false);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Position == new GridPos(5, 6), "The continuous walk did not reach the door.");
			LoadGame();
			SliceRequire(_view.Now == walkingAt && _view.Spot == walkingSpot && _view.Move is not null
				&& _walking && _sliceIntro is { Visible: false } && _queue.Count == 0,
				"Loading mid-walk lost the move, added preroll or kept an old UI order.");
			SetPaused(false);
			await UntilBlackmarshStoreIdle();
			AdvanceBlackmarshReferenceTo(walkingReference, _view.Now);
			SliceRequire(_view.Position == new GridPos(5, 6)
				&& SaveBlackmarshSnapshot(_sim).SequenceEqual(SaveBlackmarshSnapshot(walkingReference)),
				"The loaded walk diverged from the uninterrupted Sim at the same checkpoint.");

			ClickDoor(BlackmarshWatchhouse.Ids.ShedDoor);
			await UntilBlackmarshStoreIdle();
			MoveTo(new GridPos(8, 6));
			await UntilBlackmarshStoreIdle();
			ClickDoor(BlackmarshWatchhouse.Ids.InnerDoor);
			await UntilBlackmarshStoreIdle();
			MoveTo(new GridPos(9, 5));
			await UntilBlackmarshStoreIdle();
			FaceForSmoke(1, 1);
			await SliceFrames(2);
			var window = _view.Map!.Edges.Single(edge => edge.Kind == MapEdgeKind.Window);
			ClickWindowFrameForSmoke(window.A, window.B);
			SliceRequire(_pendingBarrier is not null && _walking,
				"The distant window click did not start an approach.");
			var pendingSave = Path.Combine(outputDir, "pending-window.json");
			SaveGameToPath(pendingSave);
			LoadGameFromPath(pendingSave);
			SliceRequire(_pendingBarrier is null && _view.Move is not null && _walking,
				"Loading a walk kept the unsaved UI intent or lost the Sim movement.");
			SetPaused(false);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Map!.Edges.Single(edge => edge.Kind == MapEdgeKind.Window).Open == false,
				"An old window interaction fired after loading the walk.");

			OpenWindowMenuForSmoke(window.A, window.B);
			PressBarrierActionForSmoke("Rompi il vetro (6 secondi, rumoroso)");
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Map!.Edges.Single(edge => edge.Kind == MapEdgeKind.Window).Glass == WindowGlass.Broken,
				"Breaking the glass through its menu did not change the opening.");
			var knownOpenings = _view.KnownBarriers.ToArray();
			SliceRequire(knownOpenings.Length > 0, "The player did not learn any opening state before saving.");
			var openingSave = Path.Combine(outputDir, "opened-and-broken.json");
			SaveGameToPath(openingSave);
			MoveTo(new GridPos(8, 6));
			await UntilBlackmarshStoreIdle();
			LoadGameFromPath(openingSave);
			SliceRequire(_view.Doors.Any(door => door.At == BlackmarshWatchhouse.Ids.ShedDoor && door.Open == true)
				&& _view.Doors.Any(door => door.At == BlackmarshWatchhouse.Ids.InnerDoor && door.Open == true)
				&& _view.Map!.Edges.Single(edge => edge.Kind == MapEdgeKind.Window).Glass == WindowGlass.Broken
				&& _view.KnownBarriers.SequenceEqual(knownOpenings)
				&& _pendingBarrier is null,
				"Loading after the opening lost door/glass knowledge or kept UI intents.");

			SetPaused(false);
			MoveTo(BlackmarshWatchhouse.Ids.StoreAccess);
			await UntilBlackmarshStoreIdle();
			if (_selectedStore != BlackmarshWatchhouse.Ids.ShedStore)
				SelectStoreOption(_storeOptionIds.IndexOf(BlackmarshWatchhouse.Ids.ShedStore) + 1);
			SliceRequire(_selectedStore == BlackmarshWatchhouse.Ids.ShedStore && !_storeTake.Disabled,
				"The player could not target the shed after the opening load.");
			_storeTake.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_view.Action is not null && _running is not null,
				"The UI button did not start a Sim take action.");
			await SliceFrames(3);
			SliceRequire(_view.Action is not null && _view.Food == 10,
				"The action save was not made before the transfer completed.");
			SetPaused(true);
			var actionSave = Path.Combine(outputDir, "taking-rations.json");
			SaveGameToPath(actionSave);
			var actionAt = _view.Now;
			var actionReference = LoadBlackmarshSmokeReference(File.ReadAllBytes(actionSave));
			SetPaused(false);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Food == 14 && _sim.GetWorldView().Stores.Single(store => store.Id == BlackmarshWatchhouse.Ids.ShedStore).Food == 0,
				"The continuous take did not transfer exactly four rations.");
			LoadGameFromPath(actionSave);
			SliceRequire(_view.Now == actionAt && _view.Food == 10 && _view.Action is not null && _running is not null,
				"Loading during the take did not restore the active Sim action.");
			SetPaused(false);
			await UntilBlackmarshStoreIdle();
			AdvanceBlackmarshReferenceTo(actionReference, _view.Now);
			SliceRequire(SaveBlackmarshSnapshot(_sim).SequenceEqual(SaveBlackmarshSnapshot(actionReference))
				&& _view.Food == 14
				&& _sim.GetWorldView().RecentFacts.Count(f => f.Kind == "FoodTaken") == 1,
				"The resumed take diverged or duplicated food/facts.");

			_sliceSummaryButton!.EmitSignal(BaseButton.SignalName.Pressed);
			_sliceSummaryEnd!.EmitSignal(BaseButton.SignalName.Pressed);
			SaveGame();
			var outcomeBytes = File.ReadAllBytes(blackmarshPath);
			var outcomeAt = _view.Now;
			var currentSession = _sim;
			File.WriteAllText(blackmarshPath, "{broken json");
			LoadGame();
			SliceRequire(ReferenceEquals(_sim, currentSession) && _view.Now == outcomeAt
				&& _message.Text.StartsWith("Caricamento non riuscito:", StringComparison.Ordinal),
				"A corrupt save replaced the current session or hid the error.");
			File.WriteAllBytes(blackmarshPath, outcomeBytes);
			var foreignSave = Path.Combine(outputDir, "other-scene.json");
			using (var stream = File.Create(foreignSave)) PlayableSlice.CreateSession().Save(stream);
			LoadGameFromPath(foreignSave);
			SliceRequire(ReferenceEquals(_sim, currentSession) && _view.Now == outcomeAt
				&& _message.Text.Contains("mappa diversa", StringComparison.Ordinal),
				"A valid save from another scene was applied to the built Blackmarsh map.");
			LoadGame();
			SliceRequire(_view.Now == outcomeAt && _view.Food == 14
				&& _sliceSummary is { Visible: false } && !_sliceEnded && _pendingBarrier is null
				&& _queue.Count == 0 && !_sliceWaiting
				&& _sim.GetWorldView().RecentFacts.Count(f => f.Kind == "FoodTaken") == 1,
				"Loading the outcome changed food, replayed a command or kept transient UI.");
			SliceRequire(oldVillage is null ? !File.Exists(villagePath) : File.ReadAllBytes(villagePath).SequenceEqual(oldVillage),
				"Blackmarsh wrote the Village save.");
			SliceRequire(oldSlice is null ? !File.Exists(slicePath) : File.ReadAllBytes(slicePath).SequenceEqual(oldSlice),
				"Blackmarsh wrote the first slice save.");
		}
		finally
		{
			if (oldBlackmarsh is null) File.Delete(blackmarshPath);
			else File.WriteAllBytes(blackmarshPath, oldBlackmarsh);
		}
	}

	private static SimulationSession LoadBlackmarshSmokeReference(byte[] snapshot)
	{
		using var stream = new MemoryStream(snapshot);
		var loaded = SimulationSession.TryLoad(stream);
		if (!loaded.Success || loaded.Session is null)
			throw new InvalidOperationException($"Could not load smoke reference: {loaded.Error}");
		return loaded.Session;
	}

	private static void AdvanceBlackmarshReferenceTo(SimulationSession session, GameTime at) =>
		session.Advance(Duration.FromTicks(at.Since(session.Now).Ticks));

	private static byte[] SaveBlackmarshSnapshot(SimulationSession session)
	{
		using var stream = new MemoryStream();
		session.Save(stream);
		return stream.ToArray();
	}
}
