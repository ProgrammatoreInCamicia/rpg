using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Game;

/// <summary>The C3b inventory is a view of the Sim; only its commands change carried or ground items.</summary>
public partial class VillageMap
{
	private Button _inventoryButton = null!;
	private PanelContainer _inventoryPanel = null!;
	private Label _coinsLabel = null!;
	private VBoxContainer _equipmentRows = null!;
	private VBoxContainer _inventoryRows = null!;
	private VBoxContainer _groundRows = null!;
	private readonly Dictionary<GridPos, Node2D> _groundMarks = new();
	private string _inventoryKey = "";

	private void BuildInventoryUi(CanvasLayer ui, BoxContainer sidebar)
	{
		_inventoryButton = new Button { Text = "Inventario" };
		_inventoryButton.Pressed += () =>
		{
			_inventoryPanel.Visible = !_inventoryPanel.Visible;
			if (_inventoryPanel.Visible) _spellPanel.Visible = false;
		};
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
		contents.AddChild(new Label { Text = "Equipaggiamento" });
		_equipmentRows = new VBoxContainer();
		contents.AddChild(_equipmentRows);
		contents.AddChild(new HSeparator());
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
		_coinsLabel.Text = $"Denaro: {gold} mo · {silver} ma · {copper} mr";
		_inventoryButton.Text = $"Inventario ({_view.Inventory.Sum(i => i.Count)})";
		var key = $"{_view.Position}|{_view.Dead}|{_view.Unconscious}|{_view.Action?.Kind}|"
			+ $"{_view.Combat?.Round}:{_view.Combat?.Turn}:{_view.Combat?.IsYourTurn}:{_view.Combat?.You.ActionAvailable}:{_view.Combat?.You.InteractionAvailable}|{_view.Coins}|"
			+ string.Join(";", _view.Equipment.Select(e => $"{e.Slot}:{e.Item}:{e.Name}")) + "|"
			+ string.Join(";", _view.Inventory.Select(i => $"{i.Item}:{i.Name}:{i.Count}:{i.Equipped}")) + "|"
			+ string.Join(";", _view.GroundItems.Select(i => $"{i.At}:{i.Item}:{i.Name}:{i.Count}"));
		if (key == _inventoryKey)
			return;
		_inventoryKey = key;
		ClearRows(_equipmentRows);
		ClearRows(_inventoryRows);
		ClearRows(_groundRows);
		if (_view.Equipment.Count == 0)
			_equipmentRows.AddChild(new Label { Text = "Niente equipaggiato." });
		var canEquip = !_view.Dead && !_view.Unconscious && (_view.Combat is { } fight
			? fight.IsYourTurn && (fight.You.InteractionAvailable || fight.You.ActionAvailable)
			: _view.Action is null);
		foreach (var worn in _view.Equipment)
		{
			var row = new HBoxContainer();
			row.AddChild(new Label { Text = $"{EquipmentSlotName(worn.Slot)}: {worn.Name}",
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
			if (worn.Slot != EquipmentSlot.Armor)
			{
				var stow = new Button { Text = "Riponi", Disabled = !canEquip };
				stow.Pressed += () => EquipItem(null, worn.Slot);
				row.AddChild(stow);
			}
			_equipmentRows.AddChild(row);
		}
		if (_view.Inventory.Count == 0)
			_inventoryRows.AddChild(new Label { Text = "Nessun oggetto." });
		foreach (var stack in _view.Inventory)
		{
			var row = new HBoxContainer();
			var worn = stack.Equipped > 0 ? " (in uso)" : "";
			row.AddChild(new Label { Text = $"{stack.Name} ×{stack.Count}{worn}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				TooltipText = stack.Equipped > 0 ? $"{stack.Equipped} equipaggiato/i" : "" });
			var item = ContentPack.Default.Item(stack.Item.Value);
			if (item is { Weapon: not null } || item?.Armor is { Type: ArmorType.Shield })
			{
				var equip = new MenuButton { Text = "Impugna…", Disabled = !canEquip || stack.Count <= stack.Equipped };
				var menu = equip.GetPopup();
				if (item.Weapon is not null)
				{
					menu.AddItem("Mano principale", (int)EquipmentSlot.MainHand);
					menu.AddItem("Mano secondaria", (int)EquipmentSlot.OffHand);
				}
				if (item.Armor is { Type: ArmorType.Shield })
					menu.AddItem("Scudo", (int)EquipmentSlot.Shield);
				menu.IdPressed += id => EquipItem(stack.Item.Value, (EquipmentSlot)id);
				row.AddChild(equip);
			}
			var drop = new Button { Text = "Lascia 1", Disabled = _view.Dead || _view.Position is null
				|| stack.Count <= stack.Equipped,
				TooltipText = stack.Count <= stack.Equipped ? "È equipaggiato: non puoi lasciarlo finché non puoi cambiarlo." : "" };
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

	private static string EquipmentSlotName(EquipmentSlot slot) => slot switch
	{
		EquipmentSlot.Armor => "Armatura",
		EquipmentSlot.Shield => "Scudo",
		EquipmentSlot.MainHand => "Mano principale",
		EquipmentSlot.OffHand => "Mano secondaria",
		_ => slot.ToString(),
	};

	private void EquipItem(string? item, EquipmentSlot slot)
	{
		var result = _sim.Execute(new EquipCommand { Actor = _sim.Player, Item = item, Slot = slot });
		ShowMessage(result.Message);
		Refresh();
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

	internal void PickUpForSmoke(GroundItemView stack)
	{
		var index = _view.GroundItems.ToList().FindIndex(i => i.At == stack.At && i.Item == stack.Item);
		if (index < 0 || _groundRows.GetChild(index) is not HBoxContainer row
			|| row.GetChildren().OfType<Button>().SingleOrDefault() is not { Disabled: false } button)
			throw new InvalidOperationException("The ground item has no enabled pickup button");
		button.EmitSignal(Button.SignalName.Pressed);
	}
	internal bool InventoryPanelVisibleForSmoke => _inventoryPanel.Visible;
	internal void OpenInventoryForSmoke() => _inventoryPanel.Visible = true;
	internal void CloseInventoryForSmoke() => _inventoryPanel.Visible = false;
	internal IReadOnlyList<string> EquipmentLabelsForSmoke => _equipmentRows.GetChildren()
		.OfType<HBoxContainer>().SelectMany(row => row.GetChildren().OfType<Label>()).Select(label => label.Text).ToArray();
	internal void EquipForSmoke(ItemId item, EquipmentSlot slot)
	{
		var row = _inventoryRows.GetChildren().OfType<HBoxContainer>().FirstOrDefault(r =>
			r.GetChildren().OfType<Label>().Any(l => l.Text.StartsWith(_view.Inventory.Single(i => i.Item == item).Name + " ×", StringComparison.Ordinal)));
		var button = row?.GetChildren().OfType<MenuButton>().SingleOrDefault();
		if (button is null || button.Disabled || button.GetPopup().GetItemIndex((int)slot) < 0)
			throw new InvalidOperationException($"No enabled equip option for {item} in {slot}");
		button.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, (long)slot);
	}
	internal void StowForSmoke(EquipmentSlot slot)
	{
		var row = _equipmentRows.GetChildren().OfType<HBoxContainer>().FirstOrDefault(r =>
			r.GetChildren().OfType<Label>().Any(l => l.Text.StartsWith(EquipmentSlotName(slot) + ":", StringComparison.Ordinal)));
		var button = row?.GetChildren().OfType<Button>().SingleOrDefault();
		if (button is null || button.Disabled) throw new InvalidOperationException($"No enabled stow option for {slot}");
		button.EmitSignal(Button.SignalName.Pressed);
	}
	internal bool DropEnabledForSmoke(ItemId item)
	{
		var index = _view.Inventory.ToList().FindIndex(i => i.Item == item);
		return index >= 0 && _inventoryRows.GetChild(index) is HBoxContainer row
			&& row.GetChildren().OfType<Button>().FirstOrDefault(b => b.Text == "Lascia 1") is { Disabled: false };
	}

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
