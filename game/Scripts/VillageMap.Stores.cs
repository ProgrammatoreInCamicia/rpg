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
	private bool _storeOpen;
	private Label _storeTitle = null!;
	private Label _storeContents = null!;
	private Button _storeApproach = null!;
	private HBoxContainer _storeActions = null!;

	private void RefreshStoreControls(bool onMap)
	{
		var stores = onMap && _view.Combat is null && !_view.Dead
			? _view.VisibleStores.Where(s => s.Position is not null).OrderBy(s => s.Name, StringComparer.Ordinal).ToArray()
			: Array.Empty<StoreView>();
		_storePanel.Visible = _storeOpen && stores.Length > 0;
		if (_selectedStore is { } previous && stores.All(s => s.Id != previous))
		{
			_selectedStore = null;
			_storeSelectionSuppressed = false;
			_storeOpen = false;
			_storePanel.Visible = false;
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
		_storeChoice.Visible = stores.Length > 1;
		var selectedStore = selectedIndex > 0 ? stores[selectedIndex - 1] : null;
		var atAccess = selectedStore is not null && _view.Position is { } at && selectedStore.Access.Contains(at);
		_storeTitle.Text = selectedStore?.Name ?? "Contenitore";
		_storeContents.Text = selectedStore is null ? "Seleziona un contenitore."
			: atAccess ? $"Contenuto: {(selectedStore.Contents.Count == 0 ? $"{selectedStore.Food} razioni" : string.Join(", ", selectedStore.Contents.Select(item => $"{item.Count} {item.Name}")))}\nNello zaino: {_view.Food} razioni"
			: "Avvicinati per vedere il contenuto.";
		_storeApproach.Visible = selectedStore is not null && !atAccess;
		_storeActions.Visible = atAccess;
		_storeDeposit.Disabled = !atAccess || _running is not null;
		_storeTake.Disabled = !atAccess || _running is not null;
		_storePanelStyle.BorderColor = atAccess
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

	internal void ClickStore(StoreId id)
	{
		if (_view.VisibleStores.FirstOrDefault(store => store.Id == id) is not { Position: { } } store)
		{
			ShowMessage("La cassa non è più in vista.");
			return;
		}
		_selectedStore = id;
		_storeSelectionSuppressed = false;
		_storeOpen = true;
		RefreshStoreControls(onMap: true);
		if (_view.Position is { } here && store.Access.Contains(here))
		{
			return;
		}
		ShowMessage($"{store.Name}: avvicinati per vedere il contenuto.");
	}

	private void ApproachSelectedStore() => Order("vai alla cassa", walk: true, () =>
	{
		if (_selectedStore is not { } id || _view.VisibleStores.FirstOrDefault(s => s.Id == id) is not { } store)
			return;
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
			ShowMessage($"Vai a {store.Name}. Il contenuto apparirà quando sarai vicino.");
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
		_storeOpen = true;
		RefreshStoreControls(onMap: true);
		ShowMessage($"Sei vicino a {store.Name}. Puoi prendere o consegnare razioni.");
	}

	private void SelectStoreOption(long option)
	{
		_selectedStore = option > 0 && option <= _storeOptionIds.Count
			? _storeOptionIds[(int)option - 1] : null;
		_storeSelectionSuppressed = _selectedStore is null;
		_storeOpen = _selectedStore is not null;
		RefreshStoreControls(_view.Map is not null && _view.Position is not null);
	}

	private void RefreshStoreButtonText()
	{
		var amount = (int)_amount.Value;
		_storeDeposit.Text = $"Consegna {amount} razioni";
		_storeTake.Text = $"Prendi {amount} razioni";
	}

	private static string StoreActivityStatus(ActionView action, GameTime now)
	{
		var remaining = Math.Max(0L, (action.CompletesAt.Since(now).Ticks + GameTime.TicksPerSecond - 1) / GameTime.TicksPerSecond);
		var verb = action.Kind == "TakeFood" ? "Prendi le razioni" : "Consegni le razioni";
		return $"{verb} · {remaining / 60}:{remaining % 60:00} di gioco rimanenti.";
	}

	private void UpdateStoreActivityMarker(Node2D token)
	{
		var progress = token.GetNode<ProgressBar>("StoreProgress");
		var caption = token.GetNode<Label>("StoreProgressText");
		if (_view.Action is not { Kind: "TakeFood" or "DepositFood" } action)
		{
			progress.Visible = false;
			caption.Visible = false;
			return;
		}
		var total = Math.Max(1L, action.CompletesAt.Since(action.StartedAt).Ticks);
		var elapsed = Math.Clamp(_view.Now.Since(action.StartedAt).Ticks, 0L, total);
		progress.Value = 100.0 * elapsed / total;
		progress.Visible = true;
		var remaining = Math.Max(0L, (action.CompletesAt.Since(_view.Now).Ticks + GameTime.TicksPerSecond - 1) / GameTime.TicksPerSecond);
		caption.Text = $"{(action.Kind == "TakeFood" ? "Prendi" : "Consegni")} · {remaining / 60}:{remaining % 60:00}";
		caption.Visible = true;
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
		if (current.Position is not { } at || !current.VisibleStores.Single(s => s.Id == selected).Access.Contains(at))
		{
			ShowMessage("Avvicinati alla cassa prima di usarla.");
			return;
		}
		if (take)
			StartActivity(new TakeFoodCommand { Actor = _sim.Player, Store = selected, Amount = amount });
		else
			StartActivity(new DepositFoodCommand { Actor = _sim.Player, Store = selected, Amount = amount });
	}
}
