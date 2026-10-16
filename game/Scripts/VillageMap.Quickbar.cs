using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>
/// The hotbar, after the reference in docs/hotbar.webp: one dark frame with a bronze rim; the portrait with its hit
/// point bar and the hands as slots; the Common, Paladin and Items sections side by side in a fixed grid of slots (empty
/// slots show); Action and Bonus as markers that dim the commands not using them; a round fixed button on the right.
/// </summary>
public partial class VillageMap
{
	private enum QuickFilter { All, Action, Bonus }
	private enum QuickSection { Common, Paladin, Items }

	private static readonly Color BarBronze = new("7a5f3a");
	private static readonly Color BarGold = new("d9b26a");
	private static readonly Color BarText = new("cdbf9f");
	private static readonly Color SlotFill = new(0.115f, 0.105f, 0.095f);
	private static readonly Color ActionMark = new("5fbf6a");
	private static readonly Color BonusMark = new("e08a3c");
	private const int SlotSize = 42;
	private const int SlotRows = 2;

	/// <summary>The sections of the grid, left to right: columns of slots and the caption under them.</summary>
	private static readonly (QuickSection Section, int Columns, string Title)[] QuickSections =
	{
		(QuickSection.Common, 4, "COMUNI"), (QuickSection.Paladin, 2, "PALADINO"), (QuickSection.Items, 2, "OGGETTI"),
	};

	private PanelContainer _quickbarPanel = null!;
	private TextureRect _quickPortrait = null!;
	private ProgressBar _quickHitPointsBar = null!;
	private Label _quickHitPoints = null!;
	private Button _quickMainHand = null!;
	private Button _quickOffHand = null!;
	private Button _quickTorch = null!;
	private HBoxContainer _quickExploreActions = null!;
	private HBoxContainer _quickCombatActions = null!;
	private readonly Dictionary<QuickSection, GridContainer> _quickExploreGrids = new();
	private readonly Dictionary<QuickSection, GridContainer> _quickCombatGrids = new();
	private readonly Dictionary<GridContainer, List<Panel>> _quickEmptySlots = new();
	private Button _quickExploreAttack = null!;
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
	private Button _quickPause = null!;
	private MenuButton _quickMore = null!;
	private Button _quickAllFilter = null!;
	private Button _quickActionFilter = null!;
	private Button _quickBonusFilter = null!;
	private Label _quickMode = null!;
	private Label _quickMovement = null!;
	private Label _quickConcentration = null!;
	private QuickFilter _quickFilter;
	private readonly Dictionary<Button, (QuickSection Section, QuickFilter Cost)> _quickActions = new();
	private readonly HashSet<Button> _quickDimmed = new();
	private PanelContainer _attackOptionsPanel = null!;
	private PanelContainer _gameMenu = null!;
	private Button _gameMenuButton = null!;
	private ConfirmationDialog _torchShieldConfirm = null!;

	private static StyleBoxFlat SlotStyle(Color border, Color fill, int width = 1, int radius = 4) => new()
	{
		BgColor = fill, BorderColor = border,
		BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width,
		CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
		CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
		ContentMarginLeft = 7, ContentMarginRight = 7, ContentMarginTop = 7, ContentMarginBottom = 7,
	};

	/// <summary>A square slot with its icon: a dark inset, the rim in the command's colour, gold when held or armed.</summary>
	private static void StyleSlot(Button button, string icon, Color accent, int size = SlotSize, bool round = false)
	{
		var radius = round ? size / 2 : 4;
		button.Text = "";
		button.Icon = ActionIcon(icon);
		button.IconAlignment = HorizontalAlignment.Center;
		button.ExpandIcon = true;
		button.CustomMinimumSize = new Vector2(size, size);
		button.FocusMode = Control.FocusModeEnum.None;
		var rim = accent.Lerp(BarBronze, 0.55f).Darkened(0.2f);
		button.AddThemeStyleboxOverride("normal", SlotStyle(rim, SlotFill, 1, radius));
		button.AddThemeStyleboxOverride("hover", SlotStyle(accent, SlotFill.Lightened(0.09f), 1, radius));
		button.AddThemeStyleboxOverride("pressed", SlotStyle(BarGold, accent.Darkened(0.72f), 2, radius));
		button.AddThemeStyleboxOverride("hover_pressed", SlotStyle(BarGold, accent.Darkened(0.6f), 2, radius));
		button.AddThemeStyleboxOverride("disabled", SlotStyle(new Color(0.19f, 0.17f, 0.15f), new Color(0.075f, 0.07f, 0.065f), 1, radius));
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		button.AddThemeColorOverride("icon_normal_color", new Color(0.90f, 0.86f, 0.78f));
		button.AddThemeColorOverride("icon_hover_color", Colors.White);
		button.AddThemeColorOverride("icon_pressed_color", BarGold.Lightened(0.3f));
		button.AddThemeColorOverride("icon_hover_pressed_color", BarGold.Lightened(0.3f));
		button.AddThemeColorOverride("icon_disabled_color", new Color(0.36f, 0.34f, 0.31f));
	}

	private static Panel EmptySlot(int size = SlotSize)
	{
		var slot = new Panel { CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore };
		slot.AddThemeStyleboxOverride("panel", SlotStyle(new Color(0.16f, 0.14f, 0.12f), new Color(0.07f, 0.065f, 0.06f)));
		return slot;
	}

	private static Control Divider()
	{
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 7);
		margin.AddThemeConstantOverride("margin_right", 7);
		margin.AddThemeConstantOverride("margin_top", 4);
		margin.AddThemeConstantOverride("margin_bottom", 4);
		margin.AddChild(new ColorRect { Color = BarBronze.Darkened(0.35f), CustomMinimumSize = new Vector2(1, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore });
		return margin;
	}

	private void BuildQuickbarUi(CanvasLayer ui)
	{
		_quickbarPanel = HudPanel(224, 930, -158, -10, bottomAnchored: true);
		_quickbarPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.062f, 0.056f, 0.05f, 0.96f), BorderColor = BarBronze,
			BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
			CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7, CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7,
			ShadowColor = new Color(0, 0, 0, 0.55f), ShadowSize = 10,
			ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
		});
		ui.AddChild(_quickbarPanel);
		var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		bar.AddThemeConstantOverride("separation", 10);
		_quickbarPanel.AddChild(bar);

		// The portrait, ringed in bronze, with the hit points as a bar under it.
		var portraitBlock = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
		portraitBlock.AddThemeConstantOverride("separation", 5);
		bar.AddChild(portraitBlock);
		var portraitFrame = new PanelContainer { CustomMinimumSize = new Vector2(78, 78), ClipContents = true };
		portraitFrame.AddThemeStyleboxOverride("panel", SlotStyle(BarBronze.Lightened(0.15f), new Color(0.16f, 0.14f, 0.13f), 3, 39));
		portraitBlock.AddChild(portraitFrame);
		_quickPortrait = new TextureRect { CustomMinimumSize = new Vector2(64, 64),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = Control.MouseFilterEnum.Ignore };
		portraitFrame.AddChild(_quickPortrait);
		_quickHitPointsBar = new ProgressBar { ShowPercentage = false, MinValue = 0, MaxValue = 1, Value = 1,
			CustomMinimumSize = new Vector2(78, 16), MouseFilter = Control.MouseFilterEnum.Ignore };
		_quickHitPointsBar.AddThemeStyleboxOverride("background", SlotStyle(new Color(0, 0, 0, 0.8f), new Color(0.17f, 0.06f, 0.055f), 1, 3));
		var fill = SlotStyle(new Color(0, 0, 0, 0), new Color(0.66f, 0.15f, 0.13f), 0, 3);
		_quickHitPointsBar.AddThemeStyleboxOverride("fill", fill);
		portraitBlock.AddChild(_quickHitPointsBar);
		_quickHitPoints = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore };
		_quickHitPoints.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_quickHitPoints.AddThemeFontSizeOverride("font_size", 11);
		_quickHitPoints.AddThemeColorOverride("font_color", Colors.White);
		_quickHitPoints.AddThemeColorOverride("font_outline_color", Colors.Black);
		_quickHitPoints.AddThemeConstantOverride("outline_size", 4);
		_quickHitPointsBar.AddChild(_quickHitPoints);

		// The hands: what is held, as slots; the torch under them.
		var hands = new GridContainer { Columns = 2, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
		hands.AddThemeConstantOverride("h_separation", 4);
		hands.AddThemeConstantOverride("v_separation", 4);
		bar.AddChild(hands);
		_quickMainHand = HandSlot(hands, "sword", "Mano principale");
		_quickMainHand.Pressed += () => SelectEquippedAttack(EquipmentSlot.MainHand);
		_quickOffHand = HandSlot(hands, "shield", "Mano secondaria");
		_quickOffHand.Pressed += () => SelectEquippedAttack(EquipmentSlot.OffHand);
		_quickTorch = HandSlot(hands, "torch", "Torcia (T)");
		_quickTorch.Pressed += RequestTorchToggle;
		hands.AddChild(EmptySlot());
		bar.AddChild(Divider());

		// The middle: resources over the grid of sections.
		var middle = new VBoxContainer();
		middle.AddThemeConstantOverride("separation", 5);
		bar.AddChild(middle);
		var resources = new HBoxContainer { CustomMinimumSize = new Vector2(0, 22) };
		resources.AddThemeConstantOverride("separation", 6);
		middle.AddChild(resources);
		_quickActionFilter = ResourcePip(resources, "Azione", ActionMark, QuickFilter.Action);
		_quickBonusFilter = ResourcePip(resources, "Bonus", BonusMark, QuickFilter.Bonus);
		_quickAllFilter = new Button { Visible = false };
		_quickAllFilter.Pressed += () => { _quickFilter = QuickFilter.All; RefreshQuickbar(); };
		resources.AddChild(_quickAllFilter);
		_quickMode = SmallLabel(resources, BarText);
		_quickMovement = SmallLabel(resources, MovementAccent.Lightened(0.25f));
		resources.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		_quickConcentration = SmallLabel(resources, MagicAccent.Lightened(0.25f));
		_quickExploreActions = SectionSet(middle, _quickExploreGrids);
		_quickCombatActions = SectionSet(middle, _quickCombatGrids);
		_quickCombatActions.Visible = false;
		BuildExploreQuickActions();
		BuildCombatQuickActions();
		bar.AddChild(Divider());

		// The fixed round button: End Turn in a fight, pause and resume outside.
		var fixedArea = new CenterContainer { CustomMinimumSize = new Vector2(62, 0) };
		bar.AddChild(fixedArea);
		_quickEndTurn = new Button { TooltipText = "Fine turno" };
		StyleSlot(_quickEndTurn, "turn", BarGold, 58, round: true);
		_quickEndTurn.Pressed += EndCombatTurn;
		fixedArea.AddChild(_quickEndTurn);
		_quickPause = new Button { TooltipText = "Pausa o riprendi (Spazio)", ToggleMode = true };
		StyleSlot(_quickPause, "pause", BarGold, 58, round: true);
		_quickPause.Pressed += () => SetPaused(!_paused);
		fixedArea.AddChild(_quickPause);

		_attackOptionsPanel = HudPanel(410, 930, -310, -166, bottomAnchored: true);
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

	private static Label SmallLabel(HBoxContainer parent, Color color)
	{
		var label = new Label { VerticalAlignment = VerticalAlignment.Center };
		label.AddThemeFontSizeOverride("font_size", 12);
		label.AddThemeColorOverride("font_color", color);
		parent.AddChild(label);
		return label;
	}

	/// <summary>One set of sections (exploration or fight): a grid per section, its caption under it, dividers between.</summary>
	private HBoxContainer SectionSet(VBoxContainer parent, Dictionary<QuickSection, GridContainer> grids)
	{
		var set = new HBoxContainer();
		set.AddThemeConstantOverride("separation", 0);
		parent.AddChild(set);
		foreach (var (section, columns, title) in QuickSections)
		{
			if (set.GetChildCount() > 0)
				set.AddChild(Divider());
			var column = new VBoxContainer();
			column.AddThemeConstantOverride("separation", 3);
			set.AddChild(column);
			var grid = new GridContainer { Columns = columns };
			grid.AddThemeConstantOverride("h_separation", 4);
			grid.AddThemeConstantOverride("v_separation", 4);
			column.AddChild(grid);
			var caption = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
			caption.AddThemeFontSizeOverride("font_size", 9);
			caption.AddThemeColorOverride("font_color", BarBronze.Lightened(0.2f));
			column.AddChild(caption);
			grids[section] = grid;
			_quickEmptySlots[grid] = new List<Panel>();
		}
		return set;
	}

	private Button HandSlot(GridContainer parent, string icon, string tooltip)
	{
		var button = new Button { TooltipText = tooltip };
		StyleSlot(button, icon, UtilityAccent);
		parent.AddChild(button);
		return button;
	}

	/// <summary>Action or Bonus: a marker lit while it is unspent; pressed, it dims the commands that do not use it.</summary>
	private Button ResourcePip(HBoxContainer parent, string label, Color mark, QuickFilter filter)
	{
		var button = new Button { Text = label, ToggleMode = true, FocusMode = Control.FocusModeEnum.None,
			CustomMinimumSize = new Vector2(0, 22), TooltipText = $"Mostra in evidenza i comandi che usano: {label.ToLowerInvariant()}" };
		button.AddThemeFontSizeOverride("font_size", 12);
		var normal = SlotStyle(mark.Darkened(0.5f), SlotFill, 1, 11);
		normal.ContentMarginLeft = normal.ContentMarginRight = 9;
		normal.ContentMarginTop = normal.ContentMarginBottom = 1;
		var pressed = (StyleBoxFlat)normal.Duplicate();
		pressed.BorderColor = BarGold;
		pressed.BgColor = mark.Darkened(0.72f);
		var hover = (StyleBoxFlat)normal.Duplicate();
		hover.BorderColor = mark;
		button.AddThemeStyleboxOverride("normal", normal);
		button.AddThemeStyleboxOverride("hover", hover);
		button.AddThemeStyleboxOverride("pressed", pressed);
		button.AddThemeStyleboxOverride("hover_pressed", pressed);
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		button.Pressed += () => { _quickFilter = _quickFilter == filter ? QuickFilter.All : filter; RefreshQuickbar(); };
		parent.AddChild(button);
		return button;
	}

	private Button QuickAction(QuickSection section, bool combat, string icon, string tooltip, Color accent, Action action,
		QuickFilter cost = QuickFilter.All, Button? button = null)
	{
		button ??= new Button();
		button.TooltipText = tooltip;
		StyleSlot(button, icon, accent);
		if (button is not MenuButton)
			button.Pressed += action;
		if (cost != QuickFilter.All)
		{
			// The cost in the corner: a green dot uses the action, an orange triangle the bonus action.
			var mark = new Label { Text = cost == QuickFilter.Action ? "●" : "▲", Position = new Vector2(3, -1),
				MouseFilter = Control.MouseFilterEnum.Ignore };
			mark.AddThemeFontSizeOverride("font_size", 9);
			mark.AddThemeColorOverride("font_color", cost == QuickFilter.Action ? ActionMark : BonusMark);
			button.AddChild(mark);
		}
		(combat ? _quickCombatGrids : _quickExploreGrids)[section].AddChild(button);
		_quickActions[button] = (section, cost);
		return button;
	}

	private void BuildExploreQuickActions()
	{
		_quickExploreAttack = QuickAction(QuickSection.Common, false, "sword", "Attacco: clicca un bersaglio. Esc annulla.", AttackAccent,
			() => { _attackArmed = !_attackArmed; RefreshAttackBars(); RefreshQuickbar(); });
		_quickExploreAttack.ToggleMode = true;
		QuickAction(QuickSection.Common, false, "stop", "Ferma il movimento (X)", MovementAccent, Stop);
		QuickAction(QuickSection.Common, false, "sneak", "Muoviti furtivamente (F)", DefenseAccent, () => SetSneak(!_sneak));
		_quickSearch = QuickAction(QuickSection.Common, false, "search",
			"Cerca trappole entro 10 piedi: Indagare per fosse, Percezione per aghi · 1 minuto", UtilityAccent, SearchForTraps);
		_quickClimb = QuickAction(QuickSection.Common, false, "escape", "Risali dalla fossa · 1 minuto", MovementAccent, ClimbOutOfPit);
		QuickAction(QuickSection.Common, false, "rest", "Riposo", UtilityAccent, () => _restPanel.Visible = !_restPanel.Visible);
		QuickAction(QuickSection.Common, false, "journal", "Diario", UtilityAccent, ToggleJournal);
		QuickAction(QuickSection.Paladin, false, "class", "Scheda del paladino", UtilityAccent,
			() => { _classPanel.Visible = true; RebuildClassPanel(); });
		_quickExploreMagic = QuickAction(QuickSection.Paladin, false, "spellbook",
			"Libro degli incantesimi: tutta la lista, le descrizioni, la preparazione", MagicAccent, ToggleSpellPanel);
		_quickExploreSense = QuickAction(QuickSection.Paladin, false, "eye", "Percezione divina", MagicAccent, UseDivineSense);
		QuickAction(QuickSection.Items, false, "bag", "Inventario (I)", UtilityAccent,
			() => _inventoryPanel.Visible = !_inventoryPanel.Visible);
		_quickItemTorch = QuickAction(QuickSection.Items, false, "torch", "Accendi o spegni la torcia (T)", UtilityAccent, RequestTorchToggle);
		_quickPoison = QuickAction(QuickSection.Items, false, "poison", "Applica veleno base all'arma principale", UtilityAccent,
			ApplyWeaponPoison);
	}

	private void BuildCombatQuickActions()
	{
		_quickAttack = QuickAction(QuickSection.Common, true, "sword", "Attacco: scegli l'arma e clicca un bersaglio. Esc annulla.",
			AttackAccent, () => { _attackArmed = !_attackArmed; RefreshAttackBars(); RefreshQuickbar(); }, QuickFilter.Action);
		_quickAttack.ToggleMode = true;
		_quickDash = QuickAction(QuickSection.Common, true, "dash", CombatActionTip(CombatAction.Dash), MovementAccent,
			() => ExecuteCombatAction(CombatAction.Dash), QuickFilter.Action);
		_quickDisengage = QuickAction(QuickSection.Common, true, "escape", CombatActionTip(CombatAction.Disengage), DefenseAccent,
			() => ExecuteCombatAction(CombatAction.Disengage), QuickFilter.Action);
		_quickDodge = QuickAction(QuickSection.Common, true, "shield", CombatActionTip(CombatAction.Dodge), DefenseAccent,
			() => ExecuteCombatAction(CombatAction.Dodge), QuickFilter.Action);
		_quickMore = (MenuButton)QuickAction(QuickSection.Common, true, "more", "Altre azioni", UtilityAccent, () => { },
			QuickFilter.Action, new MenuButton());
		var menu = _quickMore.GetPopup();
		foreach (var choice in Enum.GetValues<CombatAction>().Where(c => c is not (CombatAction.Dash or CombatAction.Disengage or CombatAction.Dodge)))
			menu.AddItem($"{CombatActionName(choice)} · Azione", (int)choice);
		menu.AddSeparator();
		menu.AddItem("Lascia la presa · gratuita", ReleaseMenuId);
		menu.IdPressed += id => { if (id == ReleaseMenuId) ReleaseGrapple(); else ExecuteCombatAction((CombatAction)id); };
		QuickAction(QuickSection.Common, true, "options", "Opzioni dell'attacco: arma, Punizione, manovre e danni non letali", AttackAccent,
			() => _attackOptionsPanel.Visible = !_attackOptionsPanel.Visible, QuickFilter.Action);
		_quickCombatClimb = QuickAction(QuickSection.Common, true, "escape", "Risali dalla fossa · azione", MovementAccent,
			ClimbOutOfPit, QuickFilter.Action);
		_quickHeal = QuickAction(QuickSection.Paladin, true, "heal", "Imposizione delle mani · azione bonus", UtilityAccent,
			() => { _classPanel.Visible = true; RebuildClassPanel(); }, QuickFilter.Bonus);
		_quickMagic = QuickAction(QuickSection.Paladin, true, "spellbook",
			"Libro degli incantesimi: tutta la lista, le descrizioni, la preparazione", MagicAccent, ToggleSpellPanel);
		_quickSense = QuickAction(QuickSection.Paladin, true, "eye", "Percezione divina · azione bonus", MagicAccent,
			UseDivineSense, QuickFilter.Bonus);
		QuickAction(QuickSection.Items, true, "bag", "Inventario (I)", UtilityAccent,
			() => _inventoryPanel.Visible = !_inventoryPanel.Visible);
		_quickCombatItemTorch = QuickAction(QuickSection.Items, true, "torch", "Accendi o spegni la torcia (T)", UtilityAccent,
			RequestTorchToggle);
		_quickCombatPoison = QuickAction(QuickSection.Items, true, "poison", "Applica veleno base · azione bonus", UtilityAccent,
			ApplyWeaponPoison, QuickFilter.Bonus);
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
		_quickActionFilter.SetPressedNoSignal(_quickFilter == QuickFilter.Action);
		_quickBonusFilter.SetPressedNoSignal(_quickFilter == QuickFilter.Bonus);
		_quickActionFilter.Text = combat is { } fight ? $"{(fight.You.ActionAvailable ? "●" : "○")} Azione" : "Azione";
		_quickBonusFilter.Text = combat is { } turn ? $"{(turn.You.BonusActionAvailable ? "▲" : "△")} Bonus" : "Bonus";
		_quickActionFilter.AddThemeColorOverride("font_color", combat is { You.ActionAvailable: true } ? ActionMark.Lightened(0.3f) : BarText.Darkened(0.35f));
		_quickBonusFilter.AddThemeColorOverride("font_color", combat is { You.BonusActionAvailable: true } ? BonusMark.Lightened(0.3f) : BarText.Darkened(0.35f));
		_quickMode.Visible = combat is null;
		_quickMode.Text = "Esplorazione" + (_paused ? " · in pausa" : "") + (_sneak ? " · furtivo" : "");
		_quickMovement.Visible = combat is not null;
		_quickMovement.Text = combat is { } battle ? $"Movimento {battle.You.MovementLeftFeet} ft" : "";
		var concentration = _view.Spells?.Concentrating;
		var concentrationName = concentration is null ? null
			: _view.Spells!.Prepared.FirstOrDefault(s => s.Id == concentration)?.Name ?? concentration;
		_quickConcentration.Visible = concentrationName is not null;
		_quickConcentration.Text = concentrationName is null ? ""
			: $"◆ {(concentrationName.Length > 18 ? concentrationName[..17] + "…" : concentrationName)}";
		_quickConcentration.TooltipText = concentrationName is null ? "" : $"Concentrazione: {concentrationName}";
		_quickConcentration.MouseFilter = Control.MouseFilterEnum.Pass;
		_quickHitPoints.Text = $"{_view.HitPoints}/{_view.MaxHitPoints}";
		_quickHitPointsBar.TooltipText = $"Punti ferita: {_view.HitPoints} su {_view.MaxHitPoints}";
		_quickHitPointsBar.Value = _view.MaxHitPoints > 0 ? Math.Clamp((double)_view.HitPoints / _view.MaxHitPoints, 0, 1) : 0;
		_quickPortrait.Texture = EnsurePaladinLiveModel().LiveTexture;
		var main = _view.Equipment.FirstOrDefault(e => e.Slot == EquipmentSlot.MainHand);
		var off = _view.Equipment.FirstOrDefault(e => e.Slot is EquipmentSlot.OffHand or EquipmentSlot.Shield);
		_quickMainHand.TooltipText = main is null ? "Mano principale libera · apri inventario" : $"Mano principale: {main.Name} · clicca per attaccare";
		_quickOffHand.TooltipText = off is null ? "Mano secondaria libera · apri inventario" : $"{(off.Slot == EquipmentSlot.Shield ? "Scudo" : "Mano secondaria")}: {off.Name}";
		_quickMainHand.Icon = main is null ? null : ActionIcon(EquipmentIcon(main.Item.Value));
		_quickOffHand.Icon = off is null ? null : ActionIcon(off.Slot == EquipmentSlot.Shield ? "shield" : EquipmentIcon(off.Item.Value));
		_quickTorch.Disabled = _view.Dead || _view.TorchLitUntil is null && _view.Torches == 0;
		_quickTorch.Modulate = _view.Torches > 0 || _view.TorchLitUntil is not null ? Colors.White : new Color(1, 1, 1, 0.35f);
		_quickItemTorch.Disabled = _quickTorch.Disabled;
		_quickCombatItemTorch.Disabled = _quickTorch.Disabled;
		_quickPoison.Disabled = !CanApplyWeaponPoison();
		_quickCombatPoison.Disabled = !CanApplyWeaponPoison();
		_quickExploreSense.Disabled = !CanUseDivineSense();
		_quickEndTurn.Visible = combat is not null;
		_quickPause.Visible = combat is null;
		_quickPause.SetPressedNoSignal(_paused);
		_quickPause.Icon = ActionIcon(_paused ? "turn" : "pause");
		_quickPause.TooltipText = _paused ? "Riprendi (Spazio)" : "Pausa (Spazio)";
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

	/// <summary>Which commands are on the bar now, which the filter dims, and the empty slots that keep the grid whole.</summary>
	private void RefreshQuickbarDeck()
	{
		_quickDimmed.Clear();
		foreach (var (button, setting) in _quickActions)
		{
			button.Visible = (button != _quickMagic && button != _quickExploreMagic || _view.Spells is not null)
				&& (button != _quickSense && button != _quickExploreSense || _view.ChannelDivinity is not null)
				&& (button != _quickClimb && button != _quickCombatClimb || _view.InPit)
				&& (button != _quickItemTorch && button != _quickCombatItemTorch
					|| _view.Torches > 0 || _view.TorchLitUntil is not null)
				&& (button != _quickPoison && button != _quickCombatPoison
					|| _view.Inventory.Any(i => i.Item.Value == "basic-poison" && i.Count > 0));
			var dimmed = _view.Combat is not null && _quickFilter != QuickFilter.All && setting.Cost != _quickFilter;
			if (dimmed) _quickDimmed.Add(button);
			button.Modulate = dimmed ? new Color(1, 1, 1, 0.22f) : Colors.White;
		}
		RefreshQuickSpells();
		foreach (var (grid, empties) in _quickEmptySlots)
		{
			var used = grid.GetChildren().OfType<Button>().Count(b => b.Visible);
			var capacity = Math.Max(grid.Columns * SlotRows, (used + grid.Columns - 1) / grid.Columns * grid.Columns);
			while (empties.Count < capacity - used)
			{
				var slot = EmptySlot();
				empties.Add(slot);
				grid.AddChild(slot);
			}
			for (var i = 0; i < empties.Count; i++)
				empties[i].Visible = i < capacity - used;
		}
		// Wider than its place (more spells): it grows to the left, short of the log on the right.
		var width = _quickbarPanel.GetCombinedMinimumSize().X;
		_quickbarPanel.OffsetLeft = Math.Max(16, Math.Min(224, 940 - width));
		_quickbarPanel.OffsetRight = _quickbarPanel.OffsetLeft + Math.Max(width, 930 - 224);
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

	/// <summary>On the bar and not dimmed by the Action or Bonus filter.</summary>
	private bool QuickLit(Button button) => button.Visible && button.GetParent()?.GetParent()?.GetParent() is CanvasItem { Visible: true }
		&& !_quickDimmed.Contains(button);

	internal bool GameMenuOpenForSmoke => _gameMenu.Visible;
	internal bool QuickbarVisibleForSmoke => _quickbarPanel.Visible;
	internal bool EndTurnFixedForSmoke => _quickEndTurn.Visible && _quickEndTurn.GetParent() is not GridContainer;
	internal void ChooseBonusQuickFilterForSmoke() => _quickBonusFilter.EmitSignal(BaseButton.SignalName.Pressed);
	internal void ChooseAllQuickFilterForSmoke() => _quickAllFilter.EmitSignal(BaseButton.SignalName.Pressed);
	internal bool BonusQuickFilterSelectedForSmoke => _quickFilter == QuickFilter.Bonus && _quickBonusFilter.ButtonPressed;
	internal bool ActionQuickButtonVisibleForSmoke => QuickLit(_quickDash);
	internal bool BonusQuickButtonVisibleForSmoke => QuickLit(_quickHeal);
	internal bool PoisonQuickButtonVisibleForSmoke => QuickLit(_quickPoison) && !_quickPoison.Disabled;
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
