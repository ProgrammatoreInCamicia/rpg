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
			SliceRequire(_view.VisibleStores.Any(s => s.Id == shed) && _storePanel.Visible
				&& _selectedStore == shed && _storeChoice.GetItemText(1).Contains("razioni")
				&& _storeTake.Text == "Prendi 4 razioni",
				"The shed is visible, but its store action does not target four rations.");
			SelectStoreOption(0);
			SliceRequire(_selectedStore is null && _storeTake.Disabled,
				"Clearing the selection left an active store command.");
			TakeViaUi(4); // A stale hotkey/helper must not undo an explicit placeholder choice.
			SliceRequire(_selectedStore is null && _storeTake.Disabled && _running is null,
				"An unselected command reactivated the only visible store.");
			SelectStoreOption(1);
			SliceRequire(_selectedStore == shed && !_storeTake.Disabled,
				"The visible shed store could not be selected again.");
			_storeTake.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_running is null && _view.Food == 10
				&& _sim.GetWorldView().Stores.Single(s => s.Id == shed).Food == 4
				&& _sim.GetWorldView().Stores.Single(s => s.Id == cache).Food == 4
				&& _message.Text.Contains("avvicinarsi", StringComparison.OrdinalIgnoreCase),
				"A distant UI click transferred food or silently started approaching.");
			CaptureBlackmarshStore(outputDir, "too-far.png");
			MoveTo(new GridPos(5, 6));
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.VisibleStores.All(s => s.Id != shed) && _selectedStore is null && !_storePanel.Visible,
				"Losing sight of the shed kept its store selection.");
			MoveTo(new GridPos(10, 5));
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.VisibleStores.Any(s => s.Id == shed) && _selectedStore == shed
				&& !_storeTake.Disabled, "The sole visible store did not become usable when sight returned.");
			MoveTo(BlackmarshWatchhouse.Ids.StoreAccess);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Position == BlackmarshWatchhouse.Ids.StoreAccess && _selectedStore == shed,
				"The access square did not retain the selected visible shed store.");
			var accessSave = Path.Combine(outputDir, "access-session.json");
			SaveGameToPath(accessSave);
			_storeTake.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_running is not null, "The UI button did not start taking the four rations.");
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
				&& _selectedStore is null && _storePanel.Visible && _storeTake.Disabled
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
			SliceRequire(_selectedStore == shed && !_storeTake.Disabled,
				"The player could not select the shed instead of the visible Vasan cache.");
			LoadGameFromPath(initialSave);
			SliceRequire(_view.Position == BlackmarshWatchhouse.Ids.PlayerStart && _view.Food == 10
				&& _selectedStore is null && !_storePanel.Visible,
				"Loading an unseen position kept the old store selection.");
			GD.Print("BLACKMARSH STORE SMOKE OK: distant refusal, sight loss, four rations, load cleared selection");
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
