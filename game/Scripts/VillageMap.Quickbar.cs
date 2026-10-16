using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>The visible hotbar: equipment beside the portrait, resources above icon-only actions.</summary>
public partial class VillageMap
{
	private enum QuickFilter { All, Action, Bonus }
	private enum QuickDeck { Common, Paladin, Items }
	private PanelContainer _quickbarPanel = null!;
	private TextureRect _quickPortrait = null!;
	private Label _quickHitPoints = null!;
	private Button _quickMainHand = null!;
	private Button _quickOffHand = null!;
	private Button _quickTorch = null!;
	private GridContainer _quickExploreActions = null!;
	private Button _quickExploreAttack = null!;
	private GridContainer _quickCombatActions = null!;
	private Button _quickAttack = null!;
	private Button _quickDash = null!;
	private Button _quickDisengage = null!;
	private Button _quickDodge = null!;
	private Button _quickHeal = null!;
	private Button _quickMagic = null!;
	private Button _quickExploreMagic = null!;
	private Button _quickSense = null!;
	private Button _quickExploreSense = null!;
	private Button _quickPoison = null!;
	private Button _quickCombatPoison = null!;
	private Button _quickItemTorch = null!;
	private Button _quickCombatItemTorch = null!;
	private Button _quickEndTurn = null!;
	private MenuButton _quickMore = null!;
	private Button _quickAllFilter = null!;
	private Button _quickActionFilter = null!;
	private Button _quickBonusFilter = null!;
	private Label _quickMovement = null!;
	private Label _quickConcentration = null!;
	private Label _quickExploreEmpty = null!;
	private Label _quickCombatEmpty = null!;
	private Button _quickCommonDeck = null!;
	private Button _quickPaladinDeck = null!;
	private Button _quickItemsDeck = null!;
	private QuickFilter _quickFilter;
	private QuickDeck _quickDeck;
	private readonly Dictionary<Button, (QuickDeck Deck, QuickFilter Cost)> _quickActions = new();
	private PanelContainer _attackOptionsPanel = null!;
	private PanelContainer _gameMenu = null!;
	private Button _gameMenuButton = null!;
	private ConfirmationDialog _torchShieldConfirm = null!;

	private void BuildQuickbarUi(CanvasLayer ui)
	{
		_quickbarPanel = HudPanel(208, 930, -132, -16, bottomAnchored: true);
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
		_quickTorch.Pressed += RequestTorchToggle;

		var actionArea = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		actionArea.AddThemeConstantOverride("separation", 5);
		bar.AddChild(actionArea);
		var resources = new HBoxContainer();
		resources.AddThemeConstantOverride("separation", 4);
		actionArea.AddChild(resources);
		_quickAllFilter = ResourceButton(resources, "Tutte", QuickFilter.All);
		_quickActionFilter = ResourceButton(resources, "Azione", QuickFilter.Action);
		_quickBonusFilter = ResourceButton(resources, "Bonus", QuickFilter.Bonus);
		_quickConcentration = new Label { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_quickConcentration.AddThemeFontSizeOverride("font_size", 11);
		resources.AddChild(_quickConcentration);
		_quickMovement = new Label {
			HorizontalAlignment = HorizontalAlignment.Right };
		_quickMovement.AddThemeFontSizeOverride("font_size", 11);
		resources.AddChild(_quickMovement);
		_quickExploreActions = new GridContainer { Columns = 10, CustomMinimumSize = new Vector2(0, 40) };
		_quickExploreActions.AddThemeConstantOverride("h_separation", 4);
		_quickExploreActions.AddThemeConstantOverride("v_separation", 4);
		actionArea.AddChild(_quickExploreActions);
		BuildExploreQuickActions();
		_quickExploreEmpty = new Label { Text = "Nessun comando in questa scheda", Visible = false };
		_quickExploreActions.AddChild(_quickExploreEmpty);
		_quickCombatActions = new GridContainer { Columns = 10, Visible = false, CustomMinimumSize = new Vector2(0, 40) };
		_quickCombatActions.AddThemeConstantOverride("h_separation", 4);
		_quickCombatActions.AddThemeConstantOverride("v_separation", 4);
		actionArea.AddChild(_quickCombatActions);
		BuildCombatQuickActions();
		_quickCombatEmpty = new Label { Text = "Nessun comando in questa scheda", Visible = false };
		_quickCombatActions.AddChild(_quickCombatEmpty);
		var decks = new HBoxContainer();
		decks.AddThemeConstantOverride("separation", 4);
		actionArea.AddChild(decks);
		_quickCommonDeck = DeckButton(decks, "Comuni", QuickDeck.Common);
		_quickPaladinDeck = DeckButton(decks, "Paladino", QuickDeck.Paladin);
		_quickItemsDeck = DeckButton(decks, "Oggetti", QuickDeck.Items);
		var fixedArea = new VBoxContainer { CustomMinimumSize = new Vector2(48, 0) };
		bar.AddChild(fixedArea);
		_quickEndTurn = new Button { CustomMinimumSize = new Vector2(46, 46), TooltipText = "Fine turno" };
		StyleActionButton(_quickEndTurn, "turn", MovementAccent, iconOnly: true);
		_quickEndTurn.Pressed += EndCombatTurn;
		fixedArea.AddChild(_quickEndTurn);

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
		_torchShieldConfirm = new ConfirmationDialog { Title = "Torcia e scudo",
			DialogText = "Per accendere la torcia devi liberare la mano secondaria. Riponi lo scudo e accendi la torcia?" };
		_torchShieldConfirm.Confirmed += () => { _torchShieldConfirm.Hide(); UseTorch(stowShield: true); };
		ui.AddChild(_torchShieldConfirm);
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

	private Button DeckButton(HBoxContainer parent, string label, QuickDeck deck)
	{
		var button = new Button { Text = label, ToggleMode = true, CustomMinimumSize = new Vector2(84, 24) };
		button.AddThemeFontSizeOverride("font_size", 12);
		button.Pressed += () => { _quickDeck = deck; RefreshQuickbarDeck(); };
		parent.AddChild(button);
		return button;
	}

	private Button QuickAction(GridContainer row, string icon, string tooltip, Color accent, Action action,
		QuickDeck deck = QuickDeck.Common, QuickFilter cost = QuickFilter.All)
	{
		var button = new Button { CustomMinimumSize = new Vector2(39, 39), TooltipText = tooltip };
		StyleActionButton(button, icon, accent, iconOnly: true);
		button.Pressed += action;
		row.AddChild(button);
		_quickActions[button] = (deck, cost);
		return button;
	}

	private void BuildExploreQuickActions()
	{
		_quickExploreAttack = QuickAction(_quickExploreActions, "sword", "Attacco: clicca un bersaglio. Esc annulla.", AttackAccent,
			() => { _attackArmed = !_attackArmed; RefreshAttackBars(); RefreshQuickbar(); });
		QuickAction(_quickExploreActions, "pause", "Pausa o riprendi (Spazio)", MovementAccent, () => SetPaused(!_paused));
		QuickAction(_quickExploreActions, "stop", "Ferma il movimento (X)", MovementAccent, Stop);
		QuickAction(_quickExploreActions, "sneak", "Muoviti furtivamente (F)", DefenseAccent, () => SetSneak(!_sneak));
		_quickSearch = QuickAction(_quickExploreActions, "eye", "Cerca trappole entro 10 piedi: Indagare per fosse, Percezione per aghi · 1 minuto", UtilityAccent, SearchForTraps);
		_quickClimb = QuickAction(_quickExploreActions, "escape", "Risali dalla fossa · 1 minuto", MovementAccent, ClimbOutOfPit);
		QuickAction(_quickExploreActions, "rest", "Riposo", UtilityAccent, () => _restPanel.Visible = !_restPanel.Visible);
		QuickAction(_quickExploreActions, "journal", "Diario", UtilityAccent, ToggleJournal);
		QuickAction(_quickExploreActions, "class", "Scheda del paladino", UtilityAccent,
			() => { _classPanel.Visible = true; RebuildClassPanel(); }, QuickDeck.Paladin);
		_quickExploreMagic = QuickAction(_quickExploreActions, "magic", "Libro degli incantesimi", MagicAccent,
			ToggleSpellPanel, QuickDeck.Paladin);
		_quickExploreSense = QuickAction(_quickExploreActions, "eye", "Percezione divina", MagicAccent,
			UseDivineSense, QuickDeck.Paladin);
		QuickAction(_quickExploreActions, "bag", "Inventario (I)", UtilityAccent,
			() => _inventoryPanel.Visible = !_inventoryPanel.Visible, QuickDeck.Items);
		_quickItemTorch = QuickAction(_quickExploreActions, "torch", "Accendi o spegni la torcia (T)", UtilityAccent,
			RequestTorchToggle, QuickDeck.Items);
		_quickPoison = QuickAction(_quickExploreActions, "poison", "Applica veleno base all'arma principale", UtilityAccent,
			ApplyWeaponPoison, QuickDeck.Items);
	}

	private void BuildCombatQuickActions()
	{
		_quickAttack = QuickAction(_quickCombatActions, "sword", "Attacco: scegli l'arma e clicca un bersaglio. Esc annulla.",
			AttackAccent, () => { _attackArmed = !_attackArmed; RefreshAttackBars(); RefreshQuickbar(); }, cost: QuickFilter.Action);
		_quickDash = QuickAction(_quickCombatActions, "dash", CombatActionTip(CombatAction.Dash), MovementAccent,
			() => ExecuteCombatAction(CombatAction.Dash), cost: QuickFilter.Action);
		_quickDisengage = QuickAction(_quickCombatActions, "escape", CombatActionTip(CombatAction.Disengage), DefenseAccent,
			() => ExecuteCombatAction(CombatAction.Disengage), cost: QuickFilter.Action);
		_quickDodge = QuickAction(_quickCombatActions, "shield", CombatActionTip(CombatAction.Dodge), DefenseAccent,
			() => ExecuteCombatAction(CombatAction.Dodge), cost: QuickFilter.Action);
		_quickMore = new MenuButton { CustomMinimumSize = new Vector2(42, 42), TooltipText = "Altre azioni" };
		StyleActionButton(_quickMore, "more", UtilityAccent, iconOnly: true);
		_quickCombatActions.AddChild(_quickMore);
		_quickActions[_quickMore] = (QuickDeck.Common, QuickFilter.Action);
		var menu = _quickMore.GetPopup();
		foreach (var choice in Enum.GetValues<CombatAction>().Where(c => c is not (CombatAction.Dash or CombatAction.Disengage or CombatAction.Dodge)))
			menu.AddItem($"{CombatActionName(choice)} · Azione", (int)choice);
		menu.AddSeparator();
		menu.AddItem("Lascia la presa · gratuita", ReleaseMenuId);
		menu.IdPressed += id => { if (id == ReleaseMenuId) ReleaseGrapple(); else ExecuteCombatAction((CombatAction)id); };
		_quickHeal = QuickAction(_quickCombatActions, "heal", "Imposizione delle mani · azione bonus", UtilityAccent,
			() => { _classPanel.Visible = true; RebuildClassPanel(); }, QuickDeck.Paladin, QuickFilter.Bonus);
		_quickMagic = QuickAction(_quickCombatActions, "magic", "Incantesimi · azione o azione bonus", MagicAccent,
			ToggleSpellPanel, QuickDeck.Paladin);
		_quickSense = QuickAction(_quickCombatActions, "eye", "Percezione divina · azione bonus", MagicAccent,
			UseDivineSense, QuickDeck.Paladin, QuickFilter.Bonus);
		QuickAction(_quickCombatActions, "more", "Opzioni dell'attacco: arma, Punizione, manovre e danni non letali", AttackAccent,
			() => _attackOptionsPanel.Visible = !_attackOptionsPanel.Visible, cost: QuickFilter.Action);
		_quickCombatClimb = QuickAction(_quickCombatActions, "escape", "Risali dalla fossa · azione", MovementAccent,
			ClimbOutOfPit, cost: QuickFilter.Action);
		QuickAction(_quickCombatActions, "bag", "Inventario (I)", UtilityAccent,
			() => _inventoryPanel.Visible = !_inventoryPanel.Visible, QuickDeck.Items);
		_quickCombatItemTorch = QuickAction(_quickCombatActions, "torch", "Accendi o spegni la torcia (T)", UtilityAccent,
			RequestTorchToggle, QuickDeck.Items);
		_quickCombatPoison = QuickAction(_quickCombatActions, "poison", "Applica veleno base · azione bonus", UtilityAccent,
			ApplyWeaponPoison, QuickDeck.Items, QuickFilter.Bonus);
	}

	private void SelectEquippedAttack(EquipmentSlot slot)
	{
		var equipped = _view.Equipment.FirstOrDefault(e => e.Slot == slot);
		if (equipped is null) { _inventoryPanel.Visible = true; return; }
		var attack = _view.Attacks.FirstOrDefault(a => a.Name.StartsWith(equipped.Name, StringComparison.Ordinal)
			&& (equipped.Item.Value == "torch" || IsOffHandAttack(a.Name) == (slot == EquipmentSlot.OffHand)));
		if (attack is null) { _inventoryPanel.Visible = true; return; }
		_armedAttack = attack.Name;
		_attackArmed = true;
		RefreshAttackBars();
		RefreshQuickbar();
	}

	private static string EquipmentIcon(string? item)
	{
		if (item is null) return "sword";
		if (item == "torch") return "torch";
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
		if (combat is null) _quickFilter = QuickFilter.All;
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
		_quickMovement.Text = combat is { } battle ? $"Mov. {battle.You.MovementLeftFeet} ft" : "";
		var concentration = _view.Spells?.Concentrating;
		var concentrationName = concentration is null ? null
			: _view.Spells!.Prepared.FirstOrDefault(s => s.Id == concentration)?.Name ?? concentration;
		_quickConcentration.Visible = concentrationName is not null;
		_quickConcentration.Text = concentrationName is null ? ""
			: $"Concentrazione: {(concentrationName.Length > 14 ? concentrationName[..13] + "…" : concentrationName)}";
		_quickConcentration.TooltipText = concentrationName is null ? "" : $"Concentrazione: {concentrationName}";
		_quickHitPoints.Text = $"{_view.HitPoints}/{_view.MaxHitPoints} PF";
		_quickPortrait.Texture = EnsurePaladinLiveModel().LiveTexture;
		var main = _view.Equipment.FirstOrDefault(e => e.Slot == EquipmentSlot.MainHand);
		var off = _view.Equipment.FirstOrDefault(e => e.Slot is EquipmentSlot.OffHand or EquipmentSlot.Shield);
		_quickMainHand.TooltipText = main is null ? "Mano principale libera · apri inventario" : $"Mano principale: {main.Name} · clicca per attaccare";
		_quickOffHand.TooltipText = off is null ? "Mano secondaria libera · apri inventario" : $"{(off.Slot == EquipmentSlot.Shield ? "Scudo" : "Mano secondaria")}: {off.Name}";
		_quickMainHand.Icon = main is null ? null : ActionIcon(EquipmentIcon(main.Item.Value));
		_quickOffHand.Icon = off is null ? null : ActionIcon(off.Slot == EquipmentSlot.Shield ? "shield" : EquipmentIcon(off.Item.Value));
		_quickTorch.Visible = _view.Torches > 0 || _view.TorchLitUntil is not null;
		_quickTorch.Disabled = _view.Dead || _view.TorchLitUntil is null && _view.Torches == 0;
		_quickItemTorch.Disabled = _quickTorch.Disabled;
		_quickCombatItemTorch.Disabled = _quickTorch.Disabled;
		_quickPoison.Disabled = !CanApplyWeaponPoison();
		_quickCombatPoison.Disabled = !CanApplyWeaponPoison();
		_quickExploreSense.Disabled = !CanUseDivineSense();
		_quickEndTurn.Visible = combat is not null;
		_quickTorch.TooltipText = _view.TorchLitUntil is not null ? "Spegni la torcia (T)"
			: off?.Slot == EquipmentSlot.Shield ? "Riponi lo scudo e accendi la torcia (T)"
			: off?.Slot == EquipmentSlot.OffHand ? "Mano secondaria occupata: riponi l'arma prima di accendere la torcia"
			: "Accendi una torcia (T)";
		if (combat is not null)
			_quickTorch.TooltipText += off?.Slot == EquipmentSlot.Shield && _view.TorchLitUntil is null
				? " - costa l'azione Utilizzare e l'interazione del turno"
				: " - usa l'interazione del turno; la seconda costa l'azione";
		_quickItemTorch.TooltipText = _quickTorch.TooltipText;
		_quickCombatItemTorch.TooltipText = _quickTorch.TooltipText;
		if (combat is null) { _attackOptionsPanel.Visible = false; RefreshQuickbarDeck(); return; }
		_quickAttack.Disabled = !combat.IsYourTurn || !_view.Attacks.Any();
		_quickAttack.SetPressedNoSignal(_attackArmed);
		_quickEndTurn.Disabled = !combat.IsYourTurn;
		_quickHeal.Disabled = !combat.IsYourTurn || !combat.You.BonusActionAvailable || _view.LayOnHands is not { Remaining: > 0 };
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
		RefreshQuickbarDeck();
	}

	private void RefreshQuickbarDeck()
	{
		_quickCommonDeck.SetPressedNoSignal(_quickDeck == QuickDeck.Common);
		_quickPaladinDeck.SetPressedNoSignal(_quickDeck == QuickDeck.Paladin);
		_quickItemsDeck.SetPressedNoSignal(_quickDeck == QuickDeck.Items);
		foreach (var (button, setting) in _quickActions)
		{
			var available = (button != _quickMagic && button != _quickExploreMagic || _view.Spells is not null)
				&& (button != _quickSense && button != _quickExploreSense || _view.ChannelDivinity is not null)
				&& (button != _quickClimb && button != _quickCombatClimb || _view.InPit)
				&& (button != _quickItemTorch && button != _quickCombatItemTorch
					|| _view.Torches > 0 || _view.TorchLitUntil is not null)
				&& (button != _quickPoison && button != _quickCombatPoison
					|| _view.Inventory.Any(i => i.Item.Value == "basic-poison" && i.Count > 0));
			button.Visible = available && setting.Deck == _quickDeck
				&& (_view.Combat is null || _quickFilter == QuickFilter.All
					|| setting.Cost == QuickFilter.All || setting.Cost == _quickFilter);
		}
		_quickExploreEmpty.Visible = _view.Combat is null
			&& !_quickActions.Keys.Any(b => b.GetParent() == _quickExploreActions && b.Visible);
		_quickCombatEmpty.Visible = _view.Combat is not null
			&& !_quickActions.Keys.Any(b => b.GetParent() == _quickCombatActions && b.Visible);
		var active = _view.Combat is null ? _quickExploreActions : _quickCombatActions;
		var visibleCount = _quickActions.Keys.Count(b => b.GetParent() == active && b.Visible);
		var twoRows = visibleCount > active.Columns;
		active.CustomMinimumSize = new Vector2(0, twoRows ? 84 : 40);
		_quickbarPanel.OffsetTop = twoRows ? -174 : -132;
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
	internal void ChooseCommonQuickDeckForSmoke() => _quickCommonDeck.EmitSignal(BaseButton.SignalName.Pressed);
	internal void ChoosePaladinQuickDeckForSmoke() => _quickPaladinDeck.EmitSignal(BaseButton.SignalName.Pressed);
	internal void ChooseItemsQuickDeckForSmoke() => _quickItemsDeck.EmitSignal(BaseButton.SignalName.Pressed);
	internal bool QuickDeckSelectedForSmoke(string name) => _quickDeck.ToString() == name;
	internal bool EndTurnFixedForSmoke => _quickEndTurn.Visible && _quickEndTurn.GetParent() != _quickCombatActions;
	internal void ChooseBonusQuickFilterForSmoke() => _quickBonusFilter.EmitSignal(BaseButton.SignalName.Pressed);
	internal void ChooseAllQuickFilterForSmoke() => _quickAllFilter.EmitSignal(BaseButton.SignalName.Pressed);
	internal bool BonusQuickFilterSelectedForSmoke => _quickFilter == QuickFilter.Bonus && _quickBonusFilter.ButtonPressed;
	internal bool ActionQuickButtonVisibleForSmoke => _quickDash.Visible;
	internal bool BonusQuickButtonVisibleForSmoke => _quickHeal.Visible;
	internal bool PoisonQuickButtonVisibleForSmoke => _quickPoison.Visible && !_quickPoison.Disabled;
	internal void ApplyPoisonFromQuickbarForSmoke() => _quickPoison.EmitSignal(BaseButton.SignalName.Pressed);
	internal void RequestTorchForSmoke() => RequestTorchToggle();
	internal bool TorchShieldPromptVisibleForSmoke => _torchShieldConfirm.Visible;
	internal void ConfirmTorchShieldForSmoke() => _torchShieldConfirm.EmitSignal("confirmed");
	internal bool SelectTorchFromOffHandForSmoke()
	{
		SelectEquippedAttack(EquipmentSlot.OffHand);
		return _attackArmed && _armedAttack == "Torcia";
	}
}
