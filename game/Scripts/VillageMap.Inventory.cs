using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>The C3b inventory is a view of the Sim; only its commands change carried or ground items.</summary>
public partial class VillageMap
{
	private Button _inventoryButton = null!;
	private PanelContainer _inventoryPanel = null!;
	private Label _coinsLabel = null!;
	private VBoxContainer _inventoryRows = null!;
	private VBoxContainer _groundRows = null!;
	private readonly Dictionary<GridPos, Node2D> _groundMarks = new();
	private string _inventoryKey = "";

	private void BuildInventoryUi(CanvasLayer ui, VBoxContainer sidebar)
	{
		_inventoryButton = new Button { Text = "Inventario" };
		_inventoryButton.Pressed += () => _inventoryPanel.Visible = !_inventoryPanel.Visible;
		sidebar.AddChild(_inventoryButton);

		_inventoryPanel = new PanelContainer { OffsetLeft = 450, OffsetRight = 800, OffsetTop = 16,
			OffsetBottom = 440, Visible = false };
		ui.AddChild(_inventoryPanel);
		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 6);
		_inventoryPanel.AddChild(column);
		var header = new HBoxContainer();
		header.AddChild(new Label { Text = "Inventario", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		var close = new Button { Text = "Chiudi" };
		close.Pressed += () => _inventoryPanel.Visible = false;
		header.AddChild(close);
		column.AddChild(header);
		_coinsLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		column.AddChild(_coinsLabel);
		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 325),
			SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		column.AddChild(scroll);
		var contents = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		scroll.AddChild(contents);
		contents.AddChild(new Label { Text = "Con te" });
		_inventoryRows = new VBoxContainer();
		contents.AddChild(_inventoryRows);
		contents.AddChild(new HSeparator());
		contents.AddChild(new Label { Text = "Oggetti a terra che vedi" });
		_groundRows = new VBoxContainer();
		contents.AddChild(_groundRows);
	}

	private void RefreshInventoryUi()
	{
		var gold = _view.Coins / 100;
		var silver = _view.Coins % 100 / 10;
		var copper = _view.Coins % 10;
		_coinsLabel.Text = $"Denaro: {gold} mo · {silver} ma · {copper} mr\nA Blackmarsh, 1 penny d'argento = 1 ma.";
		_inventoryButton.Text = $"Inventario ({_view.Inventory.Sum(i => i.Count)})";
		var key = $"{_view.Position}|{_view.Dead}|{_view.Combat?.Round}:{_view.Combat?.Turn}|{_view.Coins}|"
			+ string.Join(";", _view.Inventory.Select(i => $"{i.Item}:{i.Name}:{i.Count}")) + "|"
			+ string.Join(";", _view.GroundItems.Select(i => $"{i.At}:{i.Item}:{i.Name}:{i.Count}"));
		if (key == _inventoryKey)
			return;
		_inventoryKey = key;
		ClearRows(_inventoryRows);
		ClearRows(_groundRows);
		if (_view.Inventory.Count == 0)
			_inventoryRows.AddChild(new Label { Text = "Nessun oggetto." });
		foreach (var stack in _view.Inventory)
		{
			var row = new HBoxContainer();
			row.AddChild(new Label { Text = $"{stack.Name} ×{stack.Count}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
			var drop = new Button { Text = "Lascia 1", Disabled = _view.Dead || _view.Position is null };
			drop.Pressed += () => Drop(stack.Item, 1);
			row.AddChild(drop);
			_inventoryRows.AddChild(row);
		}
		if (_view.GroundItems.Count == 0)
			_groundRows.AddChild(new Label { Text = "Nessun oggetto visibile." });
		foreach (var stack in _view.GroundItems)
		{
			var row = new HBoxContainer();
			row.AddChild(new Label { Text = $"{stack.Name} ×{stack.Count} · {stack.At}",
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
			var pick = new Button { Text = "Raccogli 1", Disabled = _view.Dead || !GroundInReach(stack.At) };
			pick.Pressed += () => PickUp(stack);
			row.AddChild(pick);
			_groundRows.AddChild(row);
		}
	}

	private static void ClearRows(VBoxContainer rows)
	{
		foreach (var child in rows.GetChildren())
		{
			rows.RemoveChild(child);
			child.QueueFree();
		}
	}

	private bool GroundInReach(GridPos at) => _view.Position is { } here
		&& Math.Max(Math.Abs(here.X - at.X), Math.Abs(here.Y - at.Y)) <= 1;

	private GroundItemView? GroundItemAt(GridPos at) => GroundInReach(at)
		? _view.GroundItems.FirstOrDefault(item => item.At == at) : null;

	private void PickUp(GroundItemView stack) => Order($"raccogli {stack.Name}", walk: false, () =>
	{
		var result = _sim.Execute(new PickUpCommand { Actor = _sim.Player, At = stack.At, Item = stack.Item, Count = 1 });
		ShowMessage(result.Message);
		Refresh();
	});

	private void Drop(ItemId item, int count) => Order("lascia un oggetto", walk: false, () =>
	{
		var result = _sim.Execute(new DropCommand { Actor = _sim.Player, Item = item, Count = count });
		ShowMessage(result.Message);
		Refresh();
	});

	private void PlaceGroundItems()
	{
		var stacks = _view.GroundItems.GroupBy(i => i.At).ToDictionary(g => g.Key, g => g.ToArray());
		foreach (var (cell, items) in stacks)
		{
			if (!_groundMarks.TryGetValue(cell, out var mark))
			{
				mark = new Node2D();
				mark.AddChild(new Polygon2D { Polygon = new[] { new Vector2(0, -6), new Vector2(9, 0),
					new Vector2(0, 6), new Vector2(-9, 0) }, Color = new Color(1f, 0.73f, 0.25f) });
				var label = new Label { Name = "Count", Position = new Vector2(9, -14) };
				label.AddThemeFontSizeOverride("font_size", 11);
				label.AddThemeConstantOverride("outline_size", 3);
				label.AddThemeColorOverride("font_outline_color", Colors.Black);
				mark.AddChild(label);
				_groundMarks[cell] = mark;
				_sorted.AddChild(mark);
			}
			mark.Position = CellToScreen(cell) + new Vector2(13, -5);
			mark.Modulate = Shade(cell);
			mark.GetNode<Label>("Count").Text = items.Sum(i => i.Count).ToString();
			mark.Visible = true;
		}
		foreach (var (cell, mark) in _groundMarks)
			if (!stacks.ContainsKey(cell))
				mark.Visible = false;
	}

	internal void PreviewInventoryForSmoke(IReadOnlyList<ItemStackView> inventory,
		IReadOnlyList<GroundItemView> ground, long coins)
	{
		_view = _view with { Inventory = inventory, GroundItems = ground, Coins = coins };
		RefreshInventoryUi();
		PlaceGroundItems();
		_inventoryPanel.Visible = true;
	}

	internal bool GroundMarkVisibleForSmoke(GridPos at) =>
		_groundMarks.TryGetValue(at, out var mark) && mark.Visible;
}
