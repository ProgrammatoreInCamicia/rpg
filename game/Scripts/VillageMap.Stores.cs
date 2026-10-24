using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Store controls select an ID from the current player view; only the Sim decides reach and transfer.</summary>
public partial class VillageMap
{
	private StoreId? _selectedStore;
	private bool _storeSelectionSuppressed;
	private readonly List<StoreId> _storeOptionIds = new();
	private string _storeOptionsKey = "";
	private PanelContainer _storePanel = null!;
	private OptionButton _storeChoice = null!;
	private Button _storeDeposit = null!;
	private Button _storeTake = null!;
	private StoreId? _pendingStore;
	private StyleBoxFlat _storePanelStyle = null!;

	private void RefreshStoreControls(bool onMap)
	{
		var stores = onMap && _view.Combat is null && !_view.Dead
			? _view.VisibleStores.Where(s => s.Position is not null).OrderBy(s => s.Name, StringComparer.Ordinal).ToArray()
			: Array.Empty<StoreView>();
		_storePanel.Visible = stores.Length > 0;
		if (_selectedStore is { } previous && stores.All(s => s.Id != previous))
		{
			_selectedStore = null;
			_storeSelectionSuppressed = false;
		}
		if (_selectedStore is null && !_storeSelectionSuppressed && stores.Length == 1)
			_selectedStore = stores[0].Id;
		var key = string.Join("|", stores.Select(s => $"{s.Id.Value}:{s.Name}"));
		if (key != _storeOptionsKey || _storeChoice.ItemCount == 0)
		{
			_storeOptionsKey = key;
			_storeOptionIds.Clear();
			_storeChoice.Clear();
			_storeChoice.AddItem("Seleziona deposito");
			foreach (var store in stores)
			{
				_storeOptionIds.Add(store.Id);
				_storeChoice.AddItem(store.Name);
			}
		}
		var selectedIndex = _selectedStore is { } selected ? _storeOptionIds.IndexOf(selected) + 1 : 0;
		_storeChoice.Select(selectedIndex);
		_storeDeposit.Disabled = selectedIndex == 0;
		_storeTake.Disabled = selectedIndex == 0;
		_storePanelStyle.BorderColor = selectedIndex > 0 && _view.Position is { } at
			&& stores[selectedIndex - 1].Access.Contains(at)
			? new Color(0.93f, 0.71f, 0.32f) : new Color(0.45f, 0.35f, 0.22f);
	}

	/// <summary>Pick only a store exposed by the player's view, on its block or visible name.</summary>
	private StoreId? StoreAt(Vector2 point) => _view.VisibleStores
		.Where(store => store.Position is not null && _storeLabels.TryGetValue(store.Id, out var label)
			&& label.Visible && StoreShapeContains(store.Position.Value, label, point))
		.OrderByDescending(store => store.Position!.Value.Y)
		.Select(store => (StoreId?)store.Id)
		.FirstOrDefault();

	private static bool StoreShapeContains(GridPos cell, Label label, Vector2 point)
	{
		var local = point - CellToScreen(cell);
		return new Rect2(new Vector2(-TileW / 2f, -34f - TileH / 2f),
			new Vector2(TileW, 34f + TileH)).HasPoint(local)
			|| label.GetRect().HasPoint(local);
	}

	internal void ClickStore(StoreId id) => Order("vai alla cassa", walk: true, () =>
	{
		if (_view.VisibleStores.FirstOrDefault(store => store.Id == id) is not { Position: { } } store)
		{
			ShowMessage("La cassa non è più in vista.");
			return;
		}
		_selectedStore = id;
		_storeSelectionSuppressed = false;
		RefreshStoreControls(onMap: true);
		if (_view.Position is { } here && store.Access.Contains(here))
		{
			ShowMessage($"{store.Name}: usa Prendi o Consegna nel pannello evidenziato in basso a destra.");
			return;
		}
		var access = store.Access
			.Select(cell => (Cell: cell, Route: _sim.PreviewMoveToSpot(_sim.Player, Spot.CenterOf(cell), _sneak)))
			.Where(option => option.Route.Valid)
			.OrderBy(option => option.Route.Feet)
			.Select(option => (GridPos?)option.Cell)
			.FirstOrDefault();
		if (access is not { } target)
		{
			ShowMessage($"Non riesci a raggiungere {store.Name}. Controlla porte e ostacoli.");
			return;
		}
		if (Walk(new MoveToSpotCommand { Actor = _sim.Player, To = Spot.CenterOf(target), Stealthy = _sneak }))
		{
			_approaching = null;
			_exitAfterWalking = null;
			_pendingStore = id;
			ShowMessage($"Vai alla cassa. All'arrivo usa Prendi o Consegna nel pannello in basso a destra.");
		}
	});

	private void FinishStoreApproach(StoreId id)
	{
		var store = _view.VisibleStores.FirstOrDefault(visible => visible.Id == id);
		if (store is null || _view.Position is not { } at || !store.Access.Contains(at))
		{
			ShowMessage("Non riesci a raggiungere la cassa.");
			return;
		}
		_selectedStore = id;
		_storeSelectionSuppressed = false;
		RefreshStoreControls(onMap: true);
		ShowMessage($"{store.Name}: usa Prendi o Consegna nel pannello evidenziato in basso a destra.");
	}

	private void SelectStoreOption(long option)
	{
		_selectedStore = option > 0 && option <= _storeOptionIds.Count
			? _storeOptionIds[(int)option - 1] : null;
		_storeSelectionSuppressed = _selectedStore is null;
		RefreshStoreControls(_view.Map is not null && _view.Position is not null);
	}

	private void RefreshStoreButtonText()
	{
		var amount = (int)_amount.Value;
		_storeDeposit.Text = $"Consegna {amount} razioni";
		_storeTake.Text = $"Prendi {amount} razioni";
	}

	private void UseSelectedStore(int amount, bool take)
	{
		// Revalidate at the moment of the command, including calls from old smoke helpers.
		var current = _sim.GetPlayerView();
		if (_selectedStore is not { } selected || current.VisibleStores.All(s => s.Id != selected))
		{
			_selectedStore = null;
			RefreshStoreControls(current.Map is not null && current.Position is not null);
			ShowMessage("Seleziona un deposito visibile.");
			return;
		}
		if (take)
			StartActivity(new TakeFoodCommand { Actor = _sim.Player, Store = selected, Amount = amount });
		else
			StartActivity(new DepositFoodCommand { Actor = _sim.Player, Store = selected, Amount = amount });
	}
}
