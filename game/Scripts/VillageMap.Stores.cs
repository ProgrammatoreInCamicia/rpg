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
			_storeSelectionSuppressed = false;
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
