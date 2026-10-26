using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Task 5: the visible store selector and its real UI buttons, with the Sim in charge of reach.</summary>
public partial class VillageMap
{
	private async void RunBlackmarshStoreSmoke(string outputDir)
	{
		try
		{
			Directory.CreateDirectory(outputDir);
			var initialSave = Path.Combine(outputDir, "initial-session.json");
			SaveGameToPath(initialSave);
			SliceRequire(!_storePanel.Visible && _selectedStore is null,
				"A store was selected before the player saw one.");
			SetPaused(false);
			var torch = _sim.Execute(new TorchCommand { Actor = _sim.Player, Lit = true, StowShield = true });
			SliceRequire(torch.Success, torch.Message);
			Refresh();
			MoveTo(new GridPos(5, 6));
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Position == new GridPos(5, 6), "Could not reach the shed entrance.");
			ClickDoor(BlackmarshWatchhouse.Ids.ShedDoor);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Doors.Any(d => d.At == BlackmarshWatchhouse.Ids.ShedDoor && d.Open == true),
				"The player could not open the keyed shed door.");
			MoveTo(new GridPos(8, 6));
			await UntilBlackmarshStoreIdle();
			ClickDoor(BlackmarshWatchhouse.Ids.InnerDoor);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Doors.Any(d => d.At == BlackmarshWatchhouse.Ids.InnerDoor && d.Open == true),
				"The inner door did not open.");
			MoveTo(new GridPos(10, 5));
			await UntilBlackmarshStoreIdle();
			var shed = BlackmarshWatchhouse.Ids.ShedStore;
			var cache = BlackmarshWatchhouse.Ids.VasanCache;
			SliceRequire(_view.VisibleStores.Any(s => s.Id == shed) && !_storePanel.Visible
				&& _selectedStore == shed && _storeChoice.GetItemText(1).Contains("razioni")
				&& _storeTake.Text == "Prendi 4 razioni",
				"The shed is visible, but the contextual panel opened before a click.");
			SelectStoreOption(0);
			SliceRequire(_selectedStore is null && _storeTake.Disabled,
				"Clearing the selection left an active store command.");
			TakeViaUi(4); // A stale hotkey/helper must not undo an explicit placeholder choice.
			SliceRequire(_selectedStore is null && _storeTake.Disabled && _running is null,
				"An unselected command reactivated the only visible store.");
			SelectStoreOption(1);
			SliceRequire(_selectedStore == shed && _storeTake.Disabled,
				"The visible shed store could not be selected again.");
			_storeTake.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_running is null && _view.Food == 10
				&& _sim.GetWorldView().Stores.Single(s => s.Id == shed).Food == 4
				&& _sim.GetWorldView().Stores.Single(s => s.Id == cache).Food == 4
				&& _message.Text.Contains("Avvicinati", StringComparison.OrdinalIgnoreCase),
				"A distant UI click transferred food or silently started approaching.");
			CaptureBlackmarshStore(outputDir, "too-far.png");
			var storeCell = _view.VisibleStores.Single(s => s.Id == shed).Position!.Value;
			var blockClick = CellToScreen(storeCell) + new Vector2(0, -34);
			var nameClick = CellToScreen(storeCell) + _storeLabels[shed].Position + new Vector2(12, 5);
			SliceRequire(StoreAt(blockClick) == shed && StoreAt(nameClick) == shed,
				"The visible chest or its name does not pick the shed store.");
			_UnhandledInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left,
				Position = GetGlobalTransformWithCanvas() * nameClick });
			SliceRequire(_running is null && _storePanel.Visible && _storeApproach.Visible
				&& !_storeActions.Visible && !_storeContents.Text.Contains('4'),
				"Clicking the chest did not open a private, contextual panel.");
			await SliceFrames(2);
			CaptureBlackmarshStore(outputDir, "chest-from-afar.png");
			_storeApproach.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_running is not null && _pendingStore == shed,
				"Avvicinati did not begin an approach to the chest access.");
			Stop();
			SliceRequire(_running is null && !_walking && _pendingStore is null
				&& _view.Position != BlackmarshWatchhouse.Ids.StoreAccess,
				"Stop did not cancel the approach ordered by the chest click.");
			_UnhandledInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left,
				Position = GetGlobalTransformWithCanvas() * nameClick });
			_storeApproach.EmitSignal(BaseButton.SignalName.Pressed);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Position == BlackmarshWatchhouse.Ids.StoreAccess
				&& _selectedStore == shed && !_storeTake.Disabled && _storeActions.Visible
				&& _storeContents.Text.Contains("Contenuto: 4"),
				$"The chest click did not reach the access and reveal the store actions: pos={_view.Position}, selected={_selectedStore}, actions={_storeActions.Visible}, contents={_storeContents.Text}, message={_message.Text}.");
			CaptureBlackmarshStore(outputDir, "click-arrival.png");
			MoveTo(new GridPos(5, 6));
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.VisibleStores.All(s => s.Id != shed) && _selectedStore is null && !_storePanel.Visible,
				"Losing sight of the shed kept its store selection.");
			MoveTo(new GridPos(10, 5));
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.VisibleStores.Any(s => s.Id == shed) && _selectedStore == shed
				&& !_storePanel.Visible && _storeTake.Disabled, "The store reopened before a new click after losing sight.");
			MoveTo(BlackmarshWatchhouse.Ids.StoreAccess);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Position == BlackmarshWatchhouse.Ids.StoreAccess && _selectedStore == shed,
				"The access square did not retain the selected visible shed store.");
			var accessSave = Path.Combine(outputDir, "access-session.json");
			SaveGameToPath(accessSave);
			ClickStore(shed);
			_storeTake.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_running is not null && _view.Action?.Kind == "TakeFood"
				&& _status.Text.Contains("rimanenti")
				&& _tokens[_view.Id].GetNode<ProgressBar>("ActionProgress").Visible,
				"The UI button did not show progress while taking four rations.");
			await SliceFrames(2);
			CaptureBlackmarshStore(outputDir, "taking-progress.png");
			MoveTo(new GridPos(10, 5));
			SliceRequire(_message.Text.Contains("attendi la fine") && _running is not null,
				"A movement click during taking food looked frozen or replaced the action.");
			SetPaused(true);
			MoveTo(new GridPos(10, 5));
			SliceRequire(_queue.Count == 0 && _message.Text.Contains("attendi la fine"),
				"A paused movement click queued a hidden order during food taking.");
			SetPaused(false);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Food == 14 && _sim.GetWorldView().Stores.Single(s => s.Id == shed).Food == 0
				&& _sim.GetWorldView().Stores.Single(s => s.Id == cache).Food <= 4
				&& !_sim.GetWorldView().RecentFacts.Any(f => f.Kind == "FoodDeposited"),
				$"The UI transfer was wrong: player={_view.Food}, shed={_sim.GetWorldView().Stores.Single(s => s.Id == shed).Food}, "
				+ $"Vasan={_sim.GetWorldView().Stores.Single(s => s.Id == cache).Food}, message={_message.Text}.");
			CaptureBlackmarshStore(outputDir, "taken-four.png");
			LoadGameFromPath(accessSave);
			await SliceFrames(2);
			SliceRequire(_view.Position == BlackmarshWatchhouse.Ids.StoreAccess && _view.Food == 10
				&& _selectedStore is null && !_storePanel.Visible && _storeTake.Disabled
				&& _storeOptionIds.Contains(shed) && _storeOptionIds.Contains(cache),
				$"Loading with two stores visible did not require a new selection: position={_view.Position}, food={_view.Food}, "
				+ $"selected={_selectedStore}, panel={_storePanel.Visible}, disabled={_storeTake.Disabled}, "
				+ $"visible={string.Join(',', _view.VisibleStores.Select(s => s.Id.Value))}.");
			CaptureBlackmarshStore(outputDir, "choose-store-after-load.png");
			TakeViaUi(4);
			SliceRequire(_running is null && _view.Food == 10
				&& _sim.GetWorldView().Stores.Single(s => s.Id == cache).Food <= 4
				&& _message.Text.Contains("Seleziona un deposito", StringComparison.Ordinal),
				"An unselected command after load targeted one of the two visible stores.");
			SelectStoreOption(_storeOptionIds.IndexOf(shed) + 1);
			SliceRequire(_selectedStore == shed && _storePanel.Visible && !_storeTake.Disabled,
				"The player could not select the shed instead of the visible Vasan cache.");
			_amount.Value = 2;
			_storeTake.EmitSignal(BaseButton.SignalName.Pressed);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Food == 12 && _blackmarshKnownShedFood == 2,
				"A partial take did not update the player's last counted amount.");
			MoveTo(new GridPos(10, 5));
			await UntilBlackmarshStoreIdle();
			SliceRequire(!_blackmarshGuideText!.Text.Contains("cassa è vuota", StringComparison.Ordinal)
				&& _blackmarshKnownShedFood == 2,
				"A partial take caused the guide to claim that the shed is empty from afar.");
			LoadGameFromPath(accessSave);
			SliceRequire(_blackmarshKnownShedFood == 4,
				"Loading at the access kept a stale store count from the previous session.");
			LoadGameFromPath(initialSave);
			SliceRequire(_view.Position == BlackmarshWatchhouse.Ids.PlayerStart && _view.Food == 10
				&& _selectedStore is null && !_storePanel.Visible,
				"Loading an unseen position kept the old store selection.");
			UseTorch(stowShield: true);
			MoveTo(new GridPos(13, 10));
			await UntilBlackmarshStoreIdle();
			var strike = _view.Attacks.FirstOrDefault(option =>
				_sim.PreviewAttack(_sim.Player, BlackmarshWatchhouse.Ids.Raider, option.Name).Valid);
			SliceRequire(strike is not null, "The nearby raider cannot be attacked through the combat handler.");
			OpenCombatForSmoke(BlackmarshWatchhouse.Ids.Raider, strike!.Name);
			_pace = 600;
			for (var round = 0; round < 16 && _view.VisibleActors.FirstOrDefault(a => a.Id == BlackmarshWatchhouse.Ids.Raider)?.Condition != "morto"; round++)
			{
				if (_view.Combat is { IsYourTurn: true } combat)
				{
					var target = combat.Targets.FirstOrDefault(t => t.Target == BlackmarshWatchhouse.Ids.Raider && t.Valid && t.Attack == strike.Name);
					if (target is not null) AttackCombatTargetForSmoke(target);
					if (_view.Combat is { IsYourTurn: true }) EndCombatTurnForSmoke();
				}
				await SliceFrames(8);
			}
			var corpse = _view.VisibleActors.Single(a => a.Id == BlackmarshWatchhouse.Ids.Raider);
			SliceRequire(corpse.Condition == "morto" && _view.PeopleHere.All(p => p.Id != corpse.Id),
				"The dead raider is still offered as a listener.");
			var corpsePoint = _tokens[corpse.Id].Position + new Vector2(0, -16);
			SliceRequire(PersonAt(corpsePoint) == corpse.Id, "The visible fallen raider cannot be picked.");
			_UnhandledInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left,
				Position = GetGlobalTransformWithCanvas() * corpsePoint });
			SliceRequire(_running is null && !_walking && _talkingTo is null && _queue.Count == 0
				&& _message.Text.Contains("è morto", StringComparison.Ordinal),
				"Clicking the dead raider still started walking or talking.");
			GD.Print("BLACKMARSH STORE SMOKE OK: chest click/approach, four rations, load, dead actor click");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"BLACKMARSH STORE SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private async Task UntilBlackmarshStoreIdle()
	{
		for (var frame = 0; frame < 1200 && _running is not null; frame++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		SliceRequire(_running is null, "A Blackmarsh store preparation exceeded the frame budget.");
		await SliceFrames(3);
	}

	private void CaptureBlackmarshStore(string outputDir, string file) =>
		SliceRequire(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(outputDir, file)) == Error.Ok,
			$"Could not capture {file}.");
}
