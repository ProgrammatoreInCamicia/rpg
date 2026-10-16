using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>The visible hotbar: equipment beside the portrait, resources above icon-only actions.</summary>
public partial class VillageMap
{
	private enum QuickFilter { All, Action, Bonus }
	private PanelContainer _quickbarPanel = null!;
	private TextureRect _quickPortrait = null!;
	private Label _quickHitPoints = null!;
	private Button _quickMainHand = null!;
	private Button _quickOffHand = null!;
	private Button _quickTorch = null!;
	private HBoxContainer _quickExploreActions = null!;
	private Button _quickExploreAttack = null!;
	private HBoxContainer _quickCombatActions = null!;
	private Button _quickAttack = null!;
	private Button _quickDash = null!;
	private Button _quickDisengage = null!;
	private Button _quickDodge = null!;
	private Button _quickHeal = null!;
	private Button _quickMagic = null!;
	private Button _quickSense = null!;
	private Button _quickEndTurn = null!;
	private MenuButton _quickMore = null!;
	private Button _quickAllFilter = null!;
	private Button _quickActionFilter = null!;
	private Button _quickBonusFilter = null!;
	private Label _quickMovement = null!;
	private QuickFilter _quickFilter;
	private readonly Dictionary<Button, QuickFilter> _quickCombatCategories = new();
	private PanelContainer _attackOptionsPanel = null!;
	private PanelContainer _gameMenu = null!;
	private Button _gameMenuButton = null!;

	private void BuildQuickbarUi(CanvasLayer ui)
	{
		_quickbarPanel = HudPanel(208, 930, -130, -16, bottomAnchored: true);
		_quickbarPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.09f, 0.11f, 0.94f),
		});
		ui.AddChild(_quickbarPanel);
		var bar = new HBoxContainer();
		bar.AddThemeConstantOverride("separation", 7);
		_quickbarPanel.AddChild(bar);
		var portraitBlock = new VBoxContainer { CustomMinimumSize = new Vector2(72, 0) };
		portraitBlock.AddThemeConstantOverride("separation", 0);
		bar.AddChild(portraitBlock);
		var portraitFrame = new PanelContainer { CustomMinimumSize = new Vector2(70, 70), ClipContents = true };
		portraitFrame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.23f, 0.21f, 0.30f), BorderColor = UtilityAccent,
			BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
			CornerRadiusTopLeft = 35, CornerRadiusTopRight = 35,
			CornerRadiusBottomLeft = 35, CornerRadiusBottomRight = 35,
		});
		portraitBlock.AddChild(portraitFrame);
		_quickPortrait = new TextureRect { CustomMinimumSize = new Vector2(68, 68),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = Control.MouseFilterEnum.Ignore };
		portraitFrame.AddChild(_quickPortrait);
		_quickHitPoints = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_quickHitPoints.AddThemeFontSizeOverride("font_size", 11);
		portraitBlock.AddChild(_quickHitPoints);

		var equipment = new VBoxContainer();
		equipment.AddThemeConstantOverride("separation", 3);
		bar.AddChild(equipment);
		_quickMainHand = EquipmentButton(equipment, "sword", "Mano principale");
		_quickMainHand.Pressed += () => SelectEquippedAttack(EquipmentSlot.MainHand);
		_quickOffHand = EquipmentButton(equipment, "shield", "Mano secondaria");
		_quickOffHand.Pressed += () => SelectEquippedAttack(EquipmentSlot.OffHand);
		_quickTorch = EquipmentButton(equipment, "torch", "Torcia (T)");
		_quickTorch.Pressed += ToggleTorch;

		var actionArea = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		actionArea.AddThemeConstantOverride("separation", 5);
		bar.AddChild(actionArea);
		var resources = new HBoxContainer();
		resources.AddThemeConstantOverride("separation", 4);
		actionArea.AddChild(resources);
		_quickAllFilter = ResourceButton(resources, "Tutte", QuickFilter.All);
		_quickActionFilter = ResourceButton(resources, "Azione", QuickFilter.Action);
		_quickBonusFilter = ResourceButton(resources, "Bonus", QuickFilter.Bonus);
		_quickMovement = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Right };
		_quickMovement.AddThemeFontSizeOverride("font_size", 11);
		resources.AddChild(_quickMovement);
		_quickExploreActions = new HBoxContainer();
		_quickExploreActions.AddThemeConstantOverride("separation", 4);
		actionArea.AddChild(_quickExploreActions);
		BuildExploreQuickActions();
		_quickCombatActions = new HBoxContainer { Visible = false };
		_quickCombatActions.AddThemeConstantOverride("separation", 4);
		actionArea.AddChild(_quickCombatActions);
		BuildCombatQuickActions();

		_attackOptionsPanel = HudPanel(410, 930, -280, -140, bottomAnchored: true);
		_attackOptionsPanel.Visible = false;
		_attackOptionsPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.09f, 0.10f, 0.13f, 0.97f) });
		ui.AddChild(_attackOptionsPanel);
		var options = new VBoxContainer();
		_attackOptionsPanel.AddChild(options);
		var heading = new HBoxContainer();
		heading.AddChild(new Label { Text = "Attacco: arma e opzioni", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		var closeOptions = new Button { Text = "Chiudi" };
		closeOptions.Pressed += () => _attackOptionsPanel.Visible = false;
		heading.AddChild(closeOptions);
		options.AddChild(heading);
		BuildAttackBar(options);
		BuildGameMenu(ui);
	}

	private Button EquipmentButton(BoxContainer parent, string icon, string tooltip)
	{
		var button = new Button { CustomMinimumSize = new Vector2(38, 28), TooltipText = tooltip };
		StyleActionButton(button, icon, UtilityAccent, iconOnly: true);
		parent.AddChild(button);
		return button;
	}

	private Button ResourceButton(HBoxContainer parent, string label, QuickFilter filter)
	{
		var button = new Button { Text = label, ToggleMode = true, CustomMinimumSize = new Vector2(82, 26) };
		button.Pressed += () => { _quickFilter = filter; RefreshQuickbar(); };
		parent.AddChild(button);
		return button;
	}

	private Button QuickAction(HBoxContainer row, string icon, string tooltip, Color accent, Action action,
		QuickFilter category = QuickFilter.All)
	{
		var button = new Button { CustomMinimumSize = new Vector2(42, 42), TooltipText = tooltip };
		StyleActionButton(button, icon, accent, iconOnly: true);
		button.Pressed += action;
		row.AddChild(button);
		if (row == _quickCombatActions) _quickCombatCategories[button] = category;
		return button;
	}

	private void BuildExploreQuickActions()
	{
		_quickExploreAttack = QuickAction(_quickExploreActions, "sword", "Attacco: clicca un bersaglio. Esc annulla.", AttackAccent,
			() => { _attackArmed = !_attackArmed; RefreshAttackBars(); RefreshQuickbar(); });
		QuickAction(_quickExploreActions, "pause", "Pausa o riprendi (Spazio)", MovementAccent, () => SetPaused(!_paused));
		QuickAction(_quickExploreActions, "stop", "Ferma il movimento (X)", MovementAccent, Stop);
		QuickAction(_quickExploreActions, "sneak", "Muoviti furtivamente (F)", DefenseAccent, () => SetSneak(!_sneak));
		QuickAction(_quickExploreActions, "rest", "Riposo", UtilityAccent, () => _restPanel.Visible = !_restPanel.Visible);
		QuickAction(_quickExploreActions, "class", "Scheda del paladino", UtilityAccent, () => { _classPanel.Visible = true; RebuildClassPanel(); });
		QuickAction(_quickExploreActions, "bag", "Inventario (I)", UtilityAccent, () => _inventoryPanel.Visible = !_inventoryPanel.Visible);
		QuickAction(_quickExploreActions, "magic", "Libro degli incantesimi", MagicAccent, ToggleSpellPanel);
		QuickAction(_quickExploreActions, "journal", "Diario", UtilityAccent, ToggleJournal);
	}

	private void BuildCombatQuickActions()
	{
		_quickAttack = QuickAction(_quickCombatActions, "sword", "Attacco: scegli l'arma e clicca un bersaglio. Esc annulla.",
			AttackAccent, () => { _attackArmed = !_attackArmed; RefreshAttackBars(); RefreshQuickbar(); }, QuickFilter.Action);
		_quickDash = QuickAction(_quickCombatActions, "dash", CombatActionTip(CombatAction.Dash), MovementAccent,
			() => ExecuteCombatAction(CombatAction.Dash), QuickFilter.Action);
		_quickDisengage = QuickAction(_quickCombatActions, "escape", CombatActionTip(CombatAction.Disengage), DefenseAccent,
			() => ExecuteCombatAction(CombatAction.Disengage), QuickFilter.Action);
		_quickDodge = QuickAction(_quickCombatActions, "shield", CombatActionTip(CombatAction.Dodge), DefenseAccent,
			() => ExecuteCombatAction(CombatAction.Dodge), QuickFilter.Action);
		_quickMore = new MenuButton { CustomMinimumSize = new Vector2(42, 42), TooltipText = "Altre azioni" };
		StyleActionButton(_quickMore, "more", UtilityAccent, iconOnly: true);
		_quickCombatActions.AddChild(_quickMore);
		_quickCombatCategories[_quickMore] = QuickFilter.Action;
		var menu = _quickMore.GetPopup();
		foreach (var choice in Enum.GetValues<CombatAction>().Where(c => c is not (CombatAction.Dash or CombatAction.Disengage or CombatAction.Dodge)))
			menu.AddItem($"{CombatActionName(choice)} · Azione", (int)choice);
		menu.AddSeparator();
		menu.AddItem("Lascia la presa · gratuita", ReleaseMenuId);
		menu.IdPressed += id => { if (id == ReleaseMenuId) ReleaseGrapple(); else ExecuteCombatAction((CombatAction)id); };
		_quickHeal = QuickAction(_quickCombatActions, "heal", "Imposizione delle mani · azione bonus", UtilityAccent,
			() => { _classPanel.Visible = true; RebuildClassPanel(); }, QuickFilter.Bonus);
		_quickMagic = QuickAction(_quickCombatActions, "magic", "Incantesimi · azione o azione bonus", MagicAccent,
			ToggleSpellPanel);
		_quickSense = QuickAction(_quickCombatActions, "eye", "Percezione divina · azione bonus", MagicAccent,
			UseDivineSense, QuickFilter.Bonus);
		QuickAction(_quickCombatActions, "more", "Opzioni dell'attacco: arma, Punizione, manovre e danni non letali", AttackAccent,
			() => _attackOptionsPanel.Visible = !_attackOptionsPanel.Visible);
		_quickEndTurn = QuickAction(_quickCombatActions, "turn", "Fine turno", MovementAccent, EndCombatTurn);
	}

	private void SelectEquippedAttack(EquipmentSlot slot)
	{
		var equipped = _view.Equipment.FirstOrDefault(e => e.Slot == slot);
		if (equipped is null) { _inventoryPanel.Visible = true; return; }
		var attack = _view.Attacks.FirstOrDefault(a => a.Name.StartsWith(equipped.Name, StringComparison.Ordinal)
			&& IsOffHandAttack(a.Name) == (slot == EquipmentSlot.OffHand));
		if (attack is null) { _inventoryPanel.Visible = true; return; }
		_armedAttack = attack.Name;
		_attackArmed = true;
		RefreshAttackBars();
		RefreshQuickbar();
	}

	private static string EquipmentIcon(string? item)
	{
		if (item is null) return "sword";
		if (item.Contains("dagger", StringComparison.Ordinal)) return "dagger";
		if (item.Contains("javelin", StringComparison.Ordinal) || item.Contains("spear", StringComparison.Ordinal)) return "spear";
		if (item.Contains("mace", StringComparison.Ordinal) || item.Contains("club", StringComparison.Ordinal)) return "mace";
		if (item.Contains("bow", StringComparison.Ordinal) || item.Contains("crossbow", StringComparison.Ordinal)) return "bow";
		return "sword";
	}

	private void RefreshQuickbar()
	{
		if (_quickbarPanel is null) return;
		_quickbarPanel.Visible = !_view.Dead;
		_exploreActionPanel.Visible = false;
		_combatPanel.Visible = false;
		var combat = _view.Combat;
		_quickCombatActions.Visible = combat is not null;
		_quickExploreActions.Visible = combat is null;
		_quickExploreAttack.Disabled = !_view.Attacks.Any() || _view.Dead || _view.Unconscious;
		_quickExploreAttack.SetPressedNoSignal(_attackArmed);
		_quickActionFilter.Visible = combat is not null;
		_quickBonusFilter.Visible = combat is not null;
		_quickAllFilter.SetPressedNoSignal(_quickFilter == QuickFilter.All);
		_quickActionFilter.SetPressedNoSignal(_quickFilter == QuickFilter.Action);
		_quickBonusFilter.SetPressedNoSignal(_quickFilter == QuickFilter.Bonus);
		_quickActionFilter.Text = combat is { } fight ? $"Azione {(fight.You.ActionAvailable ? "●" : "○")}" : "Azione";
		_quickBonusFilter.Text = combat is { } turn ? $"Bonus {(turn.You.BonusActionAvailable ? "●" : "○")}" : "Bonus";
		_quickMovement.Text = combat is { } battle ? $"Mov. {battle.You.MovementLeftFeet} piedi" : $"PF {_view.HitPoints}/{_view.MaxHitPoints}";
		_quickHitPoints.Text = $"{_view.HitPoints}/{_view.MaxHitPoints} PF";
		_quickPortrait.Texture = EnsurePaladinLiveModel().LiveTexture;
		var main = _view.Equipment.FirstOrDefault(e => e.Slot == EquipmentSlot.MainHand);
		var off = _view.Equipment.FirstOrDefault(e => e.Slot is EquipmentSlot.OffHand or EquipmentSlot.Shield);
		_quickMainHand.TooltipText = main is null ? "Mano principale libera · apri inventario" : $"Mano principale: {main.Name} · clicca per attaccare";
		_quickOffHand.TooltipText = off is null ? "Mano secondaria libera · apri inventario" : $"{(off.Slot == EquipmentSlot.Shield ? "Scudo" : "Mano secondaria")}: {off.Name}";
		_quickMainHand.Icon = ActionIcon(EquipmentIcon(main?.Item.Value));
		_quickOffHand.Icon = ActionIcon(off?.Slot == EquipmentSlot.Shield ? "shield" : EquipmentIcon(off?.Item.Value));
		_quickTorch.Visible = _view.Torches > 0 || _view.TorchLitUntil is not null;
		_quickTorch.Disabled = combat is not null || _view.TorchLitUntil is null && _view.Torches == 0;
		_quickTorch.TooltipText = _view.TorchLitUntil is null ? "Accendi una torcia dall'inventario (T)" : "Spegni la torcia (T)";
		if (combat is null) { _attackOptionsPanel.Visible = false; return; }
		_quickAttack.Disabled = !combat.IsYourTurn || !_view.Attacks.Any();
		_quickAttack.SetPressedNoSignal(_attackArmed);
		_quickEndTurn.Disabled = !combat.IsYourTurn;
		_quickHeal.Disabled = !combat.IsYourTurn || !combat.You.BonusActionAvailable || _view.LayOnHands is not { Remaining: > 0 };
		_quickMagic.Visible = _view.Spells is not null;
		_quickSense.Visible = _view.ChannelDivinity is not null;
		_quickSense.Disabled = !CanUseDivineSense();
		foreach (var (action, button) in new[] { (CombatAction.Dash, _quickDash), (CombatAction.Disengage, _quickDisengage),
			(CombatAction.Dodge, _quickDodge) })
		{
			var option = combat.Actions.FirstOrDefault(a => a.Action == action);
			button.Disabled = option?.Valid != true;
			button.TooltipText = option is { Valid: false } ? $"{CombatActionName(action)}: {option.Why}" : CombatActionTip(action);
		}
		var popup = _quickMore.GetPopup();
		foreach (var action in Enum.GetValues<CombatAction>().Where(a => a is not (CombatAction.Dash or CombatAction.Disengage or CombatAction.Dodge)))
		{
			var option = combat.Actions.FirstOrDefault(a => a.Action == action);
			var index = popup.GetItemIndex((int)action);
			popup.SetItemDisabled(index, option?.Valid != true);
			popup.SetItemTooltip(index, option is { Valid: false } ? option.Why ?? "Non disponibile" : CombatActionTip(action));
		}
		var release = _view.VisibleActors.Any(a => a.Conditions.Any(c => c.Kind == ConditionKind.Grappled && c.Source == _view.Id));
		popup.SetItemDisabled(popup.GetItemIndex(ReleaseMenuId), !release);
		_quickMore.Disabled = !release && !combat.Actions.Any(a => a.Valid
			&& a.Action is not (CombatAction.Dash or CombatAction.Disengage or CombatAction.Dodge));
		foreach (var (button, category) in _quickCombatCategories)
			button.Visible = (button != _quickMagic || _view.Spells is not null)
				&& (button != _quickSense || _view.ChannelDivinity is not null)
				&& (_quickFilter == QuickFilter.All || category == QuickFilter.All || category == _quickFilter);
	}

	private void BuildGameMenu(CanvasLayer ui)
	{
		_gameMenuButton = new Button { Position = new Vector2(1218, 16), Size = new Vector2(46, 40),
			TooltipText = "Menu di gioco (Esc)" };
		StyleActionButton(_gameMenuButton, "more", UtilityAccent, iconOnly: true);
		_gameMenuButton.Pressed += ToggleGameMenu;
		ui.AddChild(_gameMenuButton);
		_gameMenu = HudPanel(480, 800, 130, 440);
		_gameMenu.Visible = false;
		_gameMenu.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.07f, 0.08f, 0.11f, 0.98f) });
		ui.AddChild(_gameMenu);
		var items = new VBoxContainer();
		items.AddThemeConstantOverride("separation", 8);
		_gameMenu.AddChild(items);
		items.AddChild(new Label { Text = "Menu di gioco" });
		MenuItem(items, "Riprendi", () => _gameMenu.Visible = false);
		MenuItem(items, "Salva", () => { SaveGame(); _gameMenu.Visible = false; });
		MenuItem(items, "Carica", () => { LoadGame(); _gameMenu.Visible = false; });
		MenuItem(items, "Riposo", () => { _restPanel.Visible = true; _gameMenu.Visible = false; });
		items.AddChild(new Label { Text = "Ritmo di esplorazione" });
		var paces = new HBoxContainer();
		items.AddChild(paces);
		foreach (var pace in new[] { 1.0, 3.0, 6.0 })
		{
			var speed = pace;
			MenuItem(paces, $"{pace}×", () => SetPace(speed));
		}
	}

	private static void MenuItem(BoxContainer parent, string title, Action action)
	{
		var button = new Button { Text = title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		button.Pressed += action;
		parent.AddChild(button);
	}

	private void ToggleGameMenu() => _gameMenu.Visible = !_gameMenu.Visible;
	internal bool GameMenuOpenForSmoke => _gameMenu.Visible;
	internal bool QuickbarVisibleForSmoke => _quickbarPanel.Visible;
	internal void ChooseBonusQuickFilterForSmoke() => _quickBonusFilter.EmitSignal(BaseButton.SignalName.Pressed);
	internal void ChooseAllQuickFilterForSmoke() => _quickAllFilter.EmitSignal(BaseButton.SignalName.Pressed);
	internal bool BonusQuickFilterSelectedForSmoke => _quickFilter == QuickFilter.Bonus && _quickBonusFilter.ButtonPressed;
	internal bool ActionQuickButtonVisibleForSmoke => _quickDash.Visible;
	internal bool BonusQuickButtonVisibleForSmoke => _quickHeal.Visible;
}
