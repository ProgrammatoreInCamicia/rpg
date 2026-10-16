using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Presentation and input for C2. The Sim owns turns, movement, attacks, dice and wounds.</summary>
public partial class VillageMap
{
	private Node2D _combatOverlay = null!;
	private PanelContainer _combatTopPanel = null!;
	private PanelContainer _combatPanel = null!;
	private Label _combatHeader = null!;
	private Label _combatOrder = null!;
	private Label _combatRoster = null!;
	private Label _combatChange = null!;
	private Label _combatSelf = null!;
	private Label _combatHint = null!;
	private Label _combatTarget = null!;
	private readonly Dictionary<CombatAction, Button> _combatActionButtons = new();
	private MenuButton _combatMoreActions = null!;
	private const int ReleaseMenuId = 100;
	private VBoxContainer _combatAttacks = null!;
	private CheckBox _combatNonLethal = null!;
	private Button _combatEndTurn = null!;
	private Button _combatLayOnHands = null!;
	private RichTextLabel _combatLog = null!;
	private PanelContainer _openingPanel = null!;
	private Label _openingTargetLabel = null!;
	private OptionButton _openingAttack = null!;
	private CheckBox _openingNonLethal = null!;
	private ConfirmationDialog _openingConfirm = null!;
	private PanelContainer _deathPanel = null!;
	private ActorId? _selectedCombatTarget;
	private ActorId? _openingTarget;
	private GridPos? _hoveredCombatCell;
	private MovePreview? _hoveredMove;
	private string? _openingConfirmedAttack;
	private bool _openingConfirmedNonLethal;

	private void BuildCombatUi(CanvasLayer ui)
	{
		_combatOverlay = new Node2D { ZIndex = -4, Visible = false };
		AddChild(_combatOverlay);

		_combatTopPanel = HudPanel(370, 930, 16, 112);
		_combatTopPanel.Visible = false;
		_combatTopPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.10f, 0.11f, 0.13f, 0.91f) });
		ui.AddChild(_combatTopPanel);
		var top = new VBoxContainer();
		top.AddThemeConstantOverride("separation", 2);
		_combatTopPanel.AddChild(top);
		_combatPanel = HudPanel(208, 930, -323, -16, bottomAnchored: true);
		_combatPanel.Visible = false;
		_combatPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.10f, 0.11f, 0.13f, 0.94f) });
		ui.AddChild(_combatPanel);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 3);
		_combatPanel.AddChild(box);
		_combatHeader = new Label();
		_combatHeader.AddThemeFontSizeOverride("font_size", 17);
		top.AddChild(_combatHeader);
		_combatOrder = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		top.AddChild(_combatOrder);
		_combatRoster = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_combatRoster.AddThemeFontSizeOverride("font_size", 12);
		top.AddChild(_combatRoster);
		_combatChange = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_combatChange.AddThemeFontSizeOverride("font_size", 12);
		top.AddChild(_combatChange);
		_combatSelf = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_combatSelf);
		_combatHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_combatHint);
		_combatTarget = new Label { Text = "Clicca su un avversario per scegliere un attacco.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_combatTarget);
		_combatNonLethal = new CheckBox { Text = "Tramortisci (solo mischia)", Visible = false };
		box.AddChild(_combatNonLethal);
		BuildAttackBar(box);
		var actionRow = new HBoxContainer();
		actionRow.AddThemeConstantOverride("separation", 5);
		box.AddChild(actionRow);
		foreach (var action in new[] { CombatAction.Dash, CombatAction.Disengage, CombatAction.Dodge })
		{
			var choice = action;
			var button = new Button();
			button.Pressed += () => ExecuteCombatAction(choice);
			AddActionTile(actionRow, button, CombatActionName(choice), "Azione",
				choice == CombatAction.Dash ? "dash" : "shield",
				choice == CombatAction.Dash ? MovementAccent : DefenseAccent);
			_combatActionButtons.Add(choice, button);
		}
		_combatMoreActions = new MenuButton { TooltipText = "Liberarsi, aiutare, nascondersi, cercare, preparare e lasciare una presa." };
		var moreMenu = _combatMoreActions.GetPopup();
		foreach (var action in Enum.GetValues<CombatAction>().Where(a => a is not (CombatAction.Dash or CombatAction.Disengage or CombatAction.Dodge)))
		{
			moreMenu.AddItem($"{CombatActionName(action)} · Azione", (int)action);
			moreMenu.SetItemIcon(moreMenu.ItemCount - 1, ActionIcon(action switch
			{
				CombatAction.Escape => "escape", CombatAction.Help => "heal",
				CombatAction.Hide => "sneak", CombatAction.Search => "eye",
				_ => "sword",
			}));
		}
		moreMenu.AddSeparator();
		moreMenu.AddItem("Lascia la presa · gratuita", ReleaseMenuId);
		moreMenu.SetItemIcon(moreMenu.ItemCount - 1, ActionIcon("escape"));
		moreMenu.IdPressed += id =>
		{
			if (id == ReleaseMenuId) ReleaseGrapple();
			else ExecuteCombatAction((CombatAction)id);
		};
		AddActionTile(actionRow, _combatMoreActions, "Altre", "Menu", "more", UtilityAccent);
		_combatLayOnHands = new Button { TooltipText = "Imposizione delle mani: cura un bersaglio usando l'azione bonus." };
		_combatLayOnHands.Pressed += () =>
		{
			_classPanel.Visible = true;
			RebuildClassPanel();
		};
		AddActionTile(actionRow, _combatLayOnHands, "Cura", "Bonus", "heal", UtilityAccent);
		BuildCombatSpellButtons(actionRow);
		var attackScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 50), Visible = false,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		box.AddChild(attackScroll);
		_combatAttacks = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		attackScroll.AddChild(_combatAttacks);
		_combatEndTurn = new Button { Text = "Fine turno", TooltipText = "Concludi il turno e lascia agire gli altri combattenti." };
		StyleActionButton(_combatEndTurn, "turn", MovementAccent);
		_combatEndTurn.Pressed += EndCombatTurn;
		box.AddChild(_combatEndTurn);
		_combatLog = new RichTextLabel { BbcodeEnabled = false, Visible = false };
		box.AddChild(_combatLog);

		_openingPanel = new PanelContainer { OffsetLeft = 900, OffsetRight = 1264, OffsetTop = 100,
			OffsetBottom = 285, Visible = false };
		_openingPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.10f, 0.11f, 0.13f, 0.94f) });
		ui.AddChild(_openingPanel);
		var openingBox = new VBoxContainer();
		_openingPanel.AddChild(openingBox);
		_openingTargetLabel = new Label();
		openingBox.AddChild(_openingTargetLabel);
		_openingAttack = new OptionButton();
		_openingAttack.ItemSelected += _ => RefreshOpeningNonLethal();
		openingBox.AddChild(_openingAttack);
		_openingNonLethal = new CheckBox { Text = "Tramortisci se il colpo va a segno" };
		openingBox.AddChild(_openingNonLethal);
		var openingRow = new HBoxContainer();
		var openingCancel = new Button { Text = "Annulla", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		openingCancel.Pressed += () => _openingPanel.Visible = false;
		openingRow.AddChild(openingCancel);
		var openingGo = new Button { Text = "Attacca…", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		openingGo.Pressed += AskOpeningAttack;
		openingRow.AddChild(openingGo);
		openingBox.AddChild(openingRow);
		_openingConfirm = new ConfirmationDialog { Title = "Conferma l'attacco" };
		_openingConfirm.Confirmed += ConfirmOpeningAttack;
		ui.AddChild(_openingConfirm);

		_deathPanel = new PanelContainer { OffsetLeft = 430, OffsetRight = 850, OffsetTop = 230,
			OffsetBottom = 420, Visible = false };
		ui.AddChild(_deathPanel);
		var deathBox = new VBoxContainer();
		_deathPanel.AddChild(deathBox);
		var deathTitle = new Label { Text = "Sei morto", HorizontalAlignment = HorizontalAlignment.Center };
		deathTitle.AddThemeFontSizeOverride("font_size", 28);
		deathBox.AddChild(deathTitle);
		deathBox.AddChild(new Label { Text = "Carica una partita salvata per riprovare.",
			HorizontalAlignment = HorizontalAlignment.Center });
		var reload = new Button { Text = "Carica" };
		reload.Pressed += LoadGame;
		deathBox.AddChild(reload);
	}

	/// <summary>Show a typed C2 fixture while the Sim's encounter rules are being implemented.</summary>
	internal void PreviewCombatForSmoke(CombatView combat)
	{
		_view = _view with { Combat = combat };
		_status.Text = $"Scontro · round {combat.Round}. È il tuo turno.";
		_storeRow.Visible = false;
		_people.Visible = false;
		_pause.Disabled = true;
		_sneakToggle.Disabled = true;
		_torch.Disabled = true;
		RefreshCombatUi();
		RefreshAttackBars();
		RefreshHud();
	}

	internal void SelectCombatantForSmoke(ActorId actor) => SelectCombatant(actor);

	internal void SelectOpeningAttackForSmoke(ActorId actor) => SelectOpeningAttack(actor);

	internal void OpenCombatForSmoke(ActorId target, string attack)
	{
		_openingTarget = target;
		_openingConfirmedAttack = attack;
		_openingConfirmedNonLethal = false;
		ConfirmOpeningAttack();
	}

	internal void EndCombatTurnForSmoke() => EndCombatTurn();
	internal bool FarmerAttacksForSmoke()
	{
		var result = _sim.Execute(new AttackCommand { Actor = MappedVillageScenario.Ids.Farmer,
			Target = _sim.Player, Attack = "Bastone" });
		ShowMessage(result.Message);
		Refresh();
		return result.Success;
	}

	internal void AttackCombatTargetForSmoke(TargetView attack) => ExecuteCombatAttack(attack);
	internal void PressCombatActionForSmoke(CombatAction action)
	{
		if (!CombatActionEnabledForSmoke(action)) throw new InvalidOperationException($"{action} is disabled in the combat UI");
		if (_combatActionButtons.TryGetValue(action, out var button)) button.EmitSignal(Button.SignalName.Pressed);
		else _combatMoreActions.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, (long)action);
	}
	internal bool CombatActionEnabledForSmoke(CombatAction action) => _combatActionButtons.TryGetValue(action, out var button)
		? !button.Disabled : !_combatMoreActions.GetPopup().IsItemDisabled(_combatMoreActions.GetPopup().GetItemIndex((int)action));
	internal bool ReleaseEnabledForSmoke => !_combatMoreActions.GetPopup().IsItemDisabled(_combatMoreActions.GetPopup().GetItemIndex(ReleaseMenuId));
	internal void PressReleaseForSmoke()
	{
		if (!ReleaseEnabledForSmoke) throw new InvalidOperationException("Release is disabled in the combat UI");
		_combatMoreActions.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, (long)ReleaseMenuId);
	}
	internal MovePreview CombatMovePreviewForSmoke(GridPos cell) => _sim.PreviewMove(_sim.Player, cell);
	internal IReadOnlyList<string> CombatAttackLabelsForSmoke => _combatAttacks.GetChildren()
		.OfType<Button>().Select(button => button.Text).ToArray();
	internal string CombatLogTextForSmoke => _combatLog.Text;
	internal bool PlayerDodgeMarkVisibleForSmoke => _tokens.TryGetValue(_view.Id, out var token)
		&& token.GetNode<Line2D>("DodgeMark").Visible;

	private void RefreshCombatUi()
	{
		if (_combatPanel is null)
			return;
		var combat = _view.Combat;
		_combatPanel.Visible = combat is not null && !_view.Dead;
		_combatTopPanel.Visible = combat is not null && !_view.Dead;
		_combatOverlay.Visible = combat is not null && !_view.Dead;
		_deathPanel.Visible = _view.Dead;
		if (combat is null)
		{
			_selectedCombatTarget = null;
			_hoveredCombatCell = null;
			_hoveredMove = null;
			ClearCombatOverlay();
			return;
		}
		_openingPanel.Visible = false;
		_combatHeader.Text = $"Scontro · round {combat.Round}";
		_combatOrder.Text = "Iniziativa: " + string.Join(" · ", combat.Order.Select(o =>
			(o.Current ? "▶ " : "") + o.Name + (o.Total is { } total ? $" {total}" : "")));
		_combatRoster.Text = combat.Combatants.Count == 0 ? "Nessun altro combattente in vista."
			: "In vista: " + string.Join(" · ", combat.Combatants.Select(c =>
				$"{c.Name} ({(c.Side == CombatSide.Ally ? "alleato" : "avversario")}, {c.Condition}"
					+ ConditionsText(_view.VisibleActors.FirstOrDefault(a => a.Id == c.Id)?.Conditions ?? Array.Empty<ConditionView>())
					+ (c.Dodging ? ", in schivata" : "") + ")"));
		_combatChange.Text = combat.Log.LastOrDefault(e => e.Kind is CombatLogKind.Joined or CombatLogKind.Fled) is { } change
			? $"Ingresso/fuga: {change.Text}" : "";
		_combatSelf.Text = $"Tu: {combat.You.HitPoints}/{combat.You.MaxHitPoints} PF · {(combat.You.KnockedOut ? "tramortito" : LifeName(combat.You.State))}"
			+ (_freeMovementEnabled
				? $"\nMovimento: {Math.Floor(combat.You.MovementLeftInches / (double)Spot.PerFoot * 10) / 10:0.0} piedi"
				: $"\nMovimento: {combat.You.MovementLeftFeet} piedi ({combat.You.SquaresLeft} caselle)")
			+ $" · Azione: {(combat.You.ActionAvailable ? "pronta" : "usata")}"
			+ $" · Bonus: {(combat.You.BonusActionAvailable ? "pronta" : "usata")}"
			+ (combat.You.AttacksLeft > 0 ? $" · Attacchi rimasti: {combat.You.AttacksLeft}" : "")
			+ (combat.You.OffHandAttackAvailable ? " · Mano secondaria pronta" : "")
			+ $" · Reazione: {(combat.You.ReactionAvailable ? "pronta" : "usata")}"
			+ (combat.You.Disengaged ? "\nDisimpegnato: il movimento non provoca attacchi di opportunit\u00e0." : "")
			+ (combat.You.Dodging ? "\nIn schivata: gli attacchi che vedi arrivare hanno svantaggio." : "")
			+ (combat.You.State == LifeState.Dying
				? $"\nTiri salvezza contro la morte: {combat.You.DeathSaveSuccesses} successi, {combat.You.DeathSaveFailures} fallimenti" : "");
		_combatHint.Text = BaseCombatHint(combat);
		_combatEndTurn.Disabled = !combat.IsYourTurn;
		_combatLayOnHands.Disabled = !combat.IsYourTurn || !combat.You.BonusActionAvailable
			|| _view.LayOnHands is not { Remaining: > 0 } || _view.Dead || _view.Unconscious;
		SetActionTileEnabled(_combatLayOnHands, !_combatLayOnHands.Disabled);
		foreach (var (action, button) in _combatActionButtons)
		{
			var option = combat.Actions.FirstOrDefault(a => a.Action == action);
			button.Disabled = option?.Valid != true;
			SetActionTileEnabled(button, !button.Disabled);
			button.TooltipText = option is { Valid: false } ? option.Why ?? "Azione non disponibile." : CombatActionTip(action);
		}
		var menu = _combatMoreActions.GetPopup();
		foreach (var action in Enum.GetValues<CombatAction>().Where(a => !_combatActionButtons.ContainsKey(a)))
		{
			var option = combat.Actions.FirstOrDefault(a => a.Action == action);
			var index = menu.GetItemIndex((int)action);
			menu.SetItemDisabled(index, option?.Valid != true);
			menu.SetItemTooltip(index, option is { Valid: false } ? option.Why ?? "Azione non disponibile." : CombatActionTip(action));
		}
		var canRelease = CanReleaseKnownGrapple();
		menu.SetItemDisabled(menu.GetItemIndex(ReleaseMenuId), !canRelease);
		_combatMoreActions.Disabled = _view.Dead || _view.Unconscious
			|| !combat.Actions.Any(a => !_combatActionButtons.ContainsKey(a.Action) && a.Valid) && !canRelease;
		SetActionTileEnabled(_combatMoreActions, !_combatMoreActions.Disabled);
		_combatLog.Text = string.Join("\n", combat.Log.TakeLast(14).Select(e => $"R{e.Round} · {e.Text}"));
		if (_selectedCombatTarget is { } selected && combat.Combatants.All(c => c.Id != selected))
			_selectedCombatTarget = null;
		RefreshCombatAttacks();
		_hoveredCombatCell = null;
		_hoveredMove = null;
		DrawCombatOverlay();
	}

	private void RefreshCombatAttacks()
	{
		foreach (var child in _combatAttacks.GetChildren())
		{
			_combatAttacks.RemoveChild(child);
			child.QueueFree();
		}
		var combat = _view.Combat;
		if (combat is null || _selectedCombatTarget is not { } target
			|| combat.Combatants.FirstOrDefault(c => c.Id == target) is not { } enemy)
		{
			_combatTarget.Text = "Clicca su un avversario per scegliere un attacco.";
			_combatNonLethal.Disabled = true;
			_combatNonLethal.ButtonPressed = false;
			return;
		}
		_combatTarget.Text = $"{enemy.Name} · {enemy.Condition}"
			+ ConditionsText(_view.VisibleActors.FirstOrDefault(a => a.Id == enemy.Id)?.Conditions ?? Array.Empty<ConditionView>());
		_combatNonLethal.Disabled = !combat.Targets.Any(t => t.Target == target && t.Valid && IsMeleeAttack(t.Attack));
		if (_combatNonLethal.Disabled)
			_combatNonLethal.ButtonPressed = false;
		foreach (var option in combat.Targets.Where(t => t.Target == target))
		{
			var choice = option;
			var attack = _view.Attacks.FirstOrDefault(a => a.Name == choice.Attack);
			var stock = attack is { Uses: not null } ? $" ×{attack.Remaining ?? 0}" : "";
			var cover = choice.Valid ? CoverLabel(choice.Cover) : "";
			var reasons = choice.Reasons.Count > 0 ? $" ({string.Join(", ", choice.Reasons)})" : "";
			var button = new Button { Text = choice.Valid ? choice.Attack + stock + cover + reasons : $"{choice.Attack}{stock} · {ShortCombatReason(choice)}",
				Disabled = !choice.Valid || !combat.IsYourTurn,
				TooltipText = string.Join("\n", new[] { choice.Why, CoverTooltip(choice.Cover),
					choice.Reasons.Count > 0 ? "Vantaggio/svantaggio: " + string.Join(", ", choice.Reasons) : null }
					.Where(s => !string.IsNullOrWhiteSpace(s))) };
			button.Pressed += () => ExecuteCombatAttack(choice);
			_combatAttacks.AddChild(button);
		}
	}

	private static string CoverLabel(CoverDegree cover) => cover switch
	{
		CoverDegree.Half => " · ½ copertura",
		CoverDegree.ThreeQuarters => " · ¾ copertura",
		_ => "",
	};

	private static string CoverTooltip(CoverDegree cover) => cover switch
	{
		CoverDegree.Half => "Il bersaglio ha +2 alla CA per la copertura.",
		CoverDegree.ThreeQuarters => "Il bersaglio ha +5 alla CA per la copertura.",
		_ => "",
	};

	private static string ShortCombatReason(TargetView choice) => choice.Reason switch
	{
		RejectionReason.TooFar => "fuori portata",
		RejectionReason.NotYourTurn => "non è il tuo turno",
		RejectionReason.NoActionLeft => "azione usata",
		RejectionReason.NotVisible => "non visibile",
		RejectionReason.NoLineOfFire => "tiro ostruito",
		RejectionReason.NoAmmo => "finiti",
		_ => choice.Why is { Length: <= 22 } why ? why : choice.Reason.ToString(),
	};

	private static string CombatActionName(CombatAction action) => action switch
	{
		CombatAction.Dash => "Scatto",
		CombatAction.Disengage => "Disimpegno",
		CombatAction.Dodge => "Schivata",
		CombatAction.Escape => "Liberarsi",
		CombatAction.Help => "Aiuta",
		CombatAction.Hide => "Nasconditi",
		CombatAction.Search => "Cerca",
		CombatAction.Ready => "Prepara",
		_ => action.ToString(),
	};

	private static string CombatActionTip(CombatAction action) => action switch
	{
		CombatAction.Dash => "Consuma l'azione: aggiunge la Velocit\u00e0 al movimento di questo turno.",
		CombatAction.Disengage => "Consuma l'azione: muoversi non provoca attacchi di opportunit\u00e0 in questo turno.",
		CombatAction.Dodge => "Consuma l'azione: chi attacca mentre lo vedi ha svantaggio fino al tuo prossimo turno.",
		CombatAction.Escape => "Consuma l'azione per tentare di liberarti da una presa.",
		CombatAction.Help => "Seleziona un nemico vicino, poi Aiuta: il prossimo attacco di un alleato contro di lui ha vantaggio.",
		CombatAction.Hide => "Consuma l'azione per nasconderti se non ti vedono.",
		CombatAction.Search => "Consuma l'azione per cercare creature nascoste.",
		CombatAction.Ready => "Consuma l'azione e prepara un colpo in mischia quando un nemico entra a portata.",
		_ => "",
	};

	private void DrawCombatOverlay()
	{
		ClearCombatOverlay();
		if (_freeMovementEnabled || _view.Combat is not { } combat)
			return;
		foreach (var square in combat.Reachable.Where(s => s.At != _view.Position))
			_combatOverlay.AddChild(new Polygon2D { Polygon = Diamond(0.72f), Position = CellToScreen(square.At),
				Color = new Color(0.18f, 0.72f, 0.42f, 0.36f) });
		if (_hoveredMove is not { Valid: true } preview || preview.Path.Count == 0)
			return;
		var from = _view.Position is { } at ? CellToScreen(at) : Vector2.Zero;
		var points = new[] { from }.Concat(preview.Path.Select(CellToScreen)).ToArray();
		_combatOverlay.AddChild(new Line2D { Points = points, Width = 4f,
			DefaultColor = preview.Provokes.Count > 0 ? new Color(1f, 0.40f, 0.22f) : new Color(1f, 0.85f, 0.22f) });
		if (preview.OpensDoor is { } door)
			_combatOverlay.AddChild(new Polygon2D { Polygon = Diamond(0.34f), Position = CellToScreen(door),
				Color = new Color(1f, 0.75f, 0.25f, 0.82f) });
	}

	private void ClearCombatOverlay()
	{
		if (_combatOverlay is null)
			return;
		foreach (var child in _combatOverlay.GetChildren())
		{
			_combatOverlay.RemoveChild(child);
			child.QueueFree();
		}
	}

	private void UpdateCombatHover()
	{
		if (_freeMovementEnabled || _view.Combat is not { IsYourTurn: true } combat || _view.Position is null)
			return;
		var cell = ScreenToCell(GetGlobalMousePosition());
		if (_hoveredCombatCell == cell)
			return;
		_hoveredCombatCell = cell;
		_hoveredMove = combat.Reachable.Any(r => r.At == cell) && cell != _view.Position
			? _sim.PreviewMove(_sim.Player, cell) : null;
		if (_hoveredMove is { Valid: true } preview)
			_combatHint.Text = $"Percorso: {preview.TotalFeet} piedi"
				+ (preview.OpensDoor is null ? "" : " · apre la porta")
				+ (preview.Provokes.Count == 0 ? " · nessun attacco di opportunità noto."
					: $" · Attacco di opportunità da {string.Join(", ", preview.Provokes.Select(p => p.Name))}!");
		else
			_combatHint.Text = BaseCombatHint(combat);
		DrawCombatOverlay();
	}

	private string BaseCombatHint(CombatView combat)
	{
		var pending = combat.PendingOpeningAttack is { } opening
			? $"\nAttacco iniziale in attesa: {opening.Attack}." : "";
		return (combat.IsYourTurn
			? _freeMovementEnabled
				? "Terreno: percorso e movimento · Clic destro: porte e finestre"
				: "Casella verde: percorso e movimento · Clic destro: porte e finestre"
			: "Attendi il tuo turno.") + pending;
	}

	private static string LifeName(LifeState state) => state switch
	{
		LifeState.Alive => "vivo",
		LifeState.Dying => "morente",
		LifeState.Stable => "stabile",
		LifeState.Dead => "morto",
		_ => state.ToString(),
	};

	private bool HandleCombatInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton { Pressed: true } mouse
			|| mouse.ButtonIndex is not (MouseButton.Left or MouseButton.Right))
			return @event is InputEventKey { Keycode: Key.Space or Key.X or Key.F or Key.T };
		var point = GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position;
		if (mouse.ButtonIndex == MouseButton.Left && PersonAt(point) is { } person)
		{
			if (!UseArmedAttack(person)) SelectCombatant(person);
			return true;
		}
		if (mouse.ButtonIndex == MouseButton.Left && _attackArmed)
		{
			ShowMessage("Scegli un bersaglio per l'attacco. Esc annulla.");
			return true;
		}
		if (DoorAt(point) is { } door && (mouse.ButtonIndex == MouseButton.Right
			|| _view.Doors.FirstOrDefault(d => d.At == door)?.Open == false))
		{
			OpenDoorMenu(door);
			return true;
		}
		if (WindowAt(point) is { } window)
		{
			OpenWindowMenu(window);
			return true;
		}
		if (mouse.ButtonIndex == MouseButton.Right)
		{
			FaceToward(point);
			return true;
		}
		if (GroundItemAt(ScreenToCell(point)) is { } item)
		{
			PickUp(item);
			return true;
		}
		if (_freeMovementEnabled)
			ExecuteCombatSpotMove(ScreenToSpot(point));
		else
			MoveTo(ScreenToCell(point));
		return true;
	}

	private void SelectCombatant(ActorId person)
	{
		var combat = _view.Combat;
		if (combat?.Combatants.FirstOrDefault(c => c.Id == person) is not { Side: CombatSide.Enemy })
			return;
		_selectedCombatTarget = person;
		RefreshCombatAttacks();
	}

	private void ExecuteCombatMove(GridPos cell)
	{
		var result = _sim.Execute(new MoveCommand { Actor = _sim.Player, To = cell });
		ShowMessage(result.Message);
		Refresh();
	}

	private void ExecuteCombatSpotMove(Spot spot)
	{
		var from = _view.Spot;
		var preview = from is null ? null : _sim.PreviewMoveToSpot(_sim.Player, spot);
		var result = _sim.Execute(new MoveToSpotCommand { Actor = _sim.Player, To = spot });
		if (result.Success && preview is { Way.Count: > 1 } && _sim.GetPlayerView() is { Spot: { } reached, Dead: false })
			StartCombatSpotVisual(preview, reached);
		ShowMessage(result.Message);
		Refresh();
	}

	private void ExecuteCombatAttack(TargetView target)
	{
		_combatVisualTracks.Clear();
		var result = _sim.Execute(new AttackCommand { Actor = _sim.Player, Target = target.Target,
			Attack = target.Attack, NonLethal = _nonLethalEnabled && IsMeleeAttack(target.Attack),
			SmiteFree = _smiteChoice == -1, SmiteSlot = _smiteChoice > 0 ? _smiteChoice : null,
			SacredWeapon = _sacredWeaponChosen, Maneuver = ChosenManeuver, ShoveProne = ChosenShoveProne,
			OffHand = IsOffHandAttack(target.Attack) });
		if (result.Success) { _smiteChoice = 0; _sacredWeaponChosen = false; }
		ShowMessage(result.Message);
		Refresh();
	}

	private void ExecuteCombatAction(CombatAction action)
	{
		if (action == CombatAction.Help && _selectedCombatTarget is null)
		{
			ShowMessage("Seleziona un nemico vicino, poi premi Aiuta.");
			return;
		}
		_combatVisualTracks.Clear();
		var result = _sim.Execute(new CombatActionCommand { Actor = _sim.Player, Action = action,
			Target = action == CombatAction.Help ? _selectedCombatTarget : null });
		ShowMessage(result.Message);
		Refresh();
	}

	private bool CanReleaseKnownGrapple() => _view.VisibleActors.Any(a =>
		a.Conditions.Any(c => c.Kind == ConditionKind.Grappled && c.Source == _view.Id));

	private void ReleaseGrapple()
	{
		var result = _sim.Execute(new ReleaseCommand { Actor = _sim.Player });
		ShowMessage(result.Message);
		Refresh();
	}

	private void EndCombatTurn()
	{
		_combatVisualTracks.Clear();
		var result = _sim.Execute(new EndTurnCommand { Actor = _sim.Player });
		ShowMessage(result.Message);
		Refresh();
	}

	private void SelectOpeningAttack(ActorId person)
	{
		if (_view.Combat is not null || _view.Dead || _view.VisibleActors.All(a => a.Id != person))
			return;
		_journalOpen = false;
		_journalPanel.Visible = false;
		_openingTarget = person;
		_openingTargetLabel.Text = $"Attacca {_view.VisibleActors.Single(a => a.Id == person).Name}?";
		_openingAttack.Clear();
		foreach (var attack in _view.Attacks)
		{
			_openingAttack.AddItem(attack.Name);
			var index = _openingAttack.ItemCount - 1;
			if (attack.Uses is not null)
			{
				_openingAttack.SetItemTooltip(index, $"{attack.Remaining ?? 0} rimasti");
				_openingAttack.SetItemDisabled(index, attack.Remaining <= 0);
			}
		}
		if (_openingAttack.ItemCount > 0)
			_openingAttack.Select(Enumerable.Range(0, _openingAttack.ItemCount)
				.FirstOrDefault(i => !_openingAttack.IsItemDisabled(i)));
		RefreshOpeningNonLethal();
		_openingPanel.Visible = true;
	}

	private bool IsMeleeAttack(string name) =>
		_view.Attacks.FirstOrDefault(a => a.Name == name)?.Melee == true;

	private void RefreshOpeningNonLethal()
	{
		var attack = _openingAttack.Selected >= 0 ? _openingAttack.GetItemText(_openingAttack.Selected) : "";
		_openingNonLethal.Disabled = !IsMeleeAttack(attack);
		if (_openingNonLethal.Disabled)
			_openingNonLethal.ButtonPressed = false;
		if (_openingTarget is { } target && attack.Length > 0)
		{
			var preview = _sim.PreviewAttack(_sim.Player, target, attack);
			var name = _view.VisibleActors.FirstOrDefault(a => a.Id == target)?.Name ?? "questa persona";
			_openingTargetLabel.Text = $"Attacca {name}?"
				+ (preview.Reasons.Count > 0 ? $"\nVantaggio/svantaggio: {string.Join(", ", preview.Reasons)}" : "")
				+ (preview.Valid ? "" : $"\n{preview.Message}");
		}
	}

	private void AskOpeningAttack()
	{
		if (_openingTarget is not { } target || _openingAttack.Selected < 0)
			return;
		_openingConfirmedAttack = _openingAttack.GetItemText(_openingAttack.Selected);
		_openingConfirmedNonLethal = _openingNonLethal.ButtonPressed;
		var name = _view.VisibleActors.FirstOrDefault(a => a.Id == target)?.Name ?? "questa persona";
		_openingConfirm.DialogText = $"Vuoi attaccare {name} con {_openingConfirmedAttack}?";
		_openingConfirm.PopupCentered();
	}

	private void ConfirmOpeningAttack()
	{
		if (_openingTarget is not { } target || _openingConfirmedAttack is not { } attack)
			return;
		_openingPanel.Visible = false;
		var result = _sim.Execute(new AttackCommand { Actor = _sim.Player, Target = target,
			Attack = attack, NonLethal = _openingConfirmedNonLethal });
		if (result.Rejection == RejectionReason.TooFar
			&& _view.VisibleActors.FirstOrDefault(a => a.Id == target) is { Position: { } there })
		{
			if (Walk(new MoveCommand { Actor = _sim.Player, To = there, StopNextTo = true, Stealthy = _sneak }))
			{
				_attackAfterWalking = (target, attack, _openingConfirmedNonLethal);
				ShowMessage("Ti avvicini prima di attaccare.");
			}
			return;
		}
		ShowMessage(result.Message);
		Refresh();
	}

	private void FinishApproachAttack((ActorId Target, string Attack, bool NonLethal) pending)
	{
		var result = _sim.Execute(new AttackCommand { Actor = _sim.Player, Target = pending.Target,
			Attack = pending.Attack, NonLethal = pending.NonLethal });
		ShowMessage(result.Message);
		Refresh();
	}
}
