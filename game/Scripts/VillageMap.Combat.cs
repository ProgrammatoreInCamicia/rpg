using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Game;

/// <summary>Presentation and input for C2. The Sim owns turns, movement, attacks, dice and wounds.</summary>
public partial class VillageMap
{
	private Node2D _combatOverlay = null!;
	private PanelContainer _combatPanel = null!;
	private Label _combatHeader = null!;
	private Label _combatOrder = null!;
	private Label _combatRoster = null!;
	private Label _combatChange = null!;
	private Label _combatSelf = null!;
	private Label _combatHint = null!;
	private Label _combatTarget = null!;
	private readonly Dictionary<CombatAction, Button> _combatActionButtons = new();
	private VBoxContainer _combatAttacks = null!;
	private CheckBox _combatNonLethal = null!;
	private Button _combatEndTurn = null!;
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

		_combatPanel = new PanelContainer { OffsetLeft = 900, OffsetRight = 1264, OffsetTop = 16,
			OffsetBottom = 690, Visible = false };
		_combatPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.10f, 0.11f, 0.13f, 0.94f) });
		ui.AddChild(_combatPanel);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 8);
		_combatPanel.AddChild(box);
		_combatHeader = new Label();
		_combatHeader.AddThemeFontSizeOverride("font_size", 20);
		box.AddChild(_combatHeader);
		_combatOrder = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_combatOrder);
		_combatRoster = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_combatRoster.AddThemeFontSizeOverride("font_size", 12);
		box.AddChild(_combatRoster);
		_combatChange = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_combatChange.AddThemeFontSizeOverride("font_size", 12);
		box.AddChild(_combatChange);
		_combatSelf = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_combatSelf);
		_combatHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_combatHint);
		_combatTarget = new Label { Text = "Clicca su un avversario per scegliere un attacco." };
		box.AddChild(_combatTarget);
		_combatNonLethal = new CheckBox { Text = "Tramortisci (solo mischia)" };
		box.AddChild(_combatNonLethal);
		var actionRow = new HBoxContainer();
		box.AddChild(actionRow);
		foreach (var action in Enum.GetValues<CombatAction>())
		{
			var choice = action;
			var button = new Button { Text = CombatActionName(choice), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			button.Pressed += () => ExecuteCombatAction(choice);
			actionRow.AddChild(button);
			_combatActionButtons.Add(choice, button);
		}
		_combatAttacks = new VBoxContainer();
		box.AddChild(_combatAttacks);
		_combatEndTurn = new Button { Text = "Fine turno" };
		_combatEndTurn.Pressed += EndCombatTurn;
		box.AddChild(_combatEndTurn);
		box.AddChild(new Label { Text = "Registro dello scontro" });
		_combatLog = new RichTextLabel { BbcodeEnabled = false, ScrollFollowing = true,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 135) };
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

	internal void AttackCombatTargetForSmoke(TargetView attack) => ExecuteCombatAttack(attack);
	internal void PressCombatActionForSmoke(CombatAction action)
	{
		var button = _combatActionButtons[action];
		if (button.Disabled) throw new InvalidOperationException($"{action} is disabled in the combat UI");
		button.EmitSignal(Button.SignalName.Pressed);
	}
	internal bool CombatActionEnabledForSmoke(CombatAction action) => !_combatActionButtons[action].Disabled;
	internal MovePreview CombatMovePreviewForSmoke(GridPos cell) => _sim.PreviewMove(_sim.Player, cell);
	internal bool PlayerDodgeMarkVisibleForSmoke => _tokens.TryGetValue(_view.Id, out var token)
		&& token.GetNode<Line2D>("DodgeMark").Visible;

	private void RefreshCombatUi()
	{
		if (_combatPanel is null)
			return;
		var combat = _view.Combat;
		_combatPanel.Visible = combat is not null && !_view.Dead;
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
					+ (c.Dodging ? ", in schivata" : "") + ")"));
		_combatChange.Text = combat.Log.LastOrDefault(e => e.Kind is CombatLogKind.Joined or CombatLogKind.Fled) is { } change
			? $"Ingresso/fuga: {change.Text}" : "";
		_combatSelf.Text = $"Tu: {combat.You.HitPoints}/{combat.You.MaxHitPoints} PF · {(combat.You.KnockedOut ? "tramortito" : LifeName(combat.You.State))}"
			+ $"\nMovimento: {combat.You.MovementLeftFeet} piedi ({combat.You.SquaresLeft} caselle)"
			+ $" · Azione: {(combat.You.ActionAvailable ? "pronta" : "usata")}"
			+ $" · Reazione: {(combat.You.ReactionAvailable ? "pronta" : "usata")}"
			+ (combat.You.Disengaged ? "\nDisimpegnato: il movimento non provoca attacchi di opportunit\u00e0." : "")
			+ (combat.You.Dodging ? "\nIn schivata: gli attacchi che vedi arrivare hanno svantaggio." : "")
			+ (combat.You.State == LifeState.Dying
				? $"\nTiri salvezza contro la morte: {combat.You.DeathSaveSuccesses} successi, {combat.You.DeathSaveFailures} fallimenti" : "");
		_combatHint.Text = BaseCombatHint(combat);
		_combatEndTurn.Disabled = !combat.IsYourTurn;
		foreach (var (action, button) in _combatActionButtons)
		{
			var option = combat.Actions.FirstOrDefault(a => a.Action == action);
			button.Disabled = option?.Valid != true;
			button.TooltipText = option is { Valid: false } ? option.Why ?? "Azione non disponibile." : CombatActionTip(action);
		}
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
		_combatTarget.Text = $"{enemy.Name} · {enemy.Condition}";
		_combatNonLethal.Disabled = !combat.Targets.Any(t => t.Target == target && t.Valid && IsMeleeAttack(t.Attack));
		if (_combatNonLethal.Disabled)
			_combatNonLethal.ButtonPressed = false;
		foreach (var option in combat.Targets.Where(t => t.Target == target))
		{
			var choice = option;
			var attack = _view.Attacks.FirstOrDefault(a => a.Name == choice.Attack);
			var stock = attack is { Uses: not null } ? $" ({attack.Remaining ?? 0} rimasti)" : "";
			var button = new Button { Text = choice.Valid ? choice.Attack + stock : $"{choice.Attack}{stock} · {ShortCombatReason(choice)}",
				Disabled = !choice.Valid || !combat.IsYourTurn,
				TooltipText = choice.Why ?? "" };
			button.Pressed += () => ExecuteCombatAttack(choice);
			_combatAttacks.AddChild(button);
		}
	}

	private static string ShortCombatReason(TargetView choice) => choice.Reason switch
	{
		RejectionReason.TooFar => "fuori portata",
		RejectionReason.NotYourTurn => "non è il tuo turno",
		RejectionReason.NoActionLeft => "azione usata",
		RejectionReason.NotVisible => "non visibile",
		RejectionReason.NoAmmo => "finiti",
		_ => choice.Why is { Length: <= 22 } why ? why : choice.Reason.ToString(),
	};

	private static string CombatActionName(CombatAction action) => action switch
	{
		CombatAction.Dash => "Scatto",
		CombatAction.Disengage => "Disimpegno",
		CombatAction.Dodge => "Schivata",
		_ => action.ToString(),
	};

	private static string CombatActionTip(CombatAction action) => action switch
	{
		CombatAction.Dash => "Consuma l'azione: aggiunge la Velocit\u00e0 al movimento di questo turno.",
		CombatAction.Disengage => "Consuma l'azione: muoversi non provoca attacchi di opportunit\u00e0 in questo turno.",
		CombatAction.Dodge => "Consuma l'azione: chi attacca mentre lo vedi ha svantaggio fino al tuo prossimo turno.",
		_ => "",
	};

	private void DrawCombatOverlay()
	{
		ClearCombatOverlay();
		if (_view.Combat is not { } combat)
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
		if (_view.Combat is not { IsYourTurn: true } combat || _view.Position is null)
			return;
		var cell = ScreenToCell(GetGlobalMousePosition());
		if (_hoveredCombatCell == cell)
			return;
		_hoveredCombatCell = cell;
		_hoveredMove = combat.Reachable.Any(r => r.At == cell) && cell != _view.Position
			? _sim.PreviewMove(_sim.Player, cell) : null;
		if (_hoveredMove is { Valid: true } preview)
			_combatHint.Text = $"Percorso: {preview.TotalFeet} piedi"
				+ (preview.Provokes.Count == 0 ? " · nessun attacco di opportunità noto."
					: $" · Attacco di opportunità da {string.Join(", ", preview.Provokes.Select(p => p.Name))}!");
		else
			_combatHint.Text = BaseCombatHint(combat);
		DrawCombatOverlay();
	}

	private static string BaseCombatHint(CombatView combat)
	{
		var pending = combat.PendingOpeningAttack is { } opening
			? $"\nAttacco iniziale in attesa: {opening.Attack}." : "";
		return (combat.IsYourTurn
			? "Passa il mouse su una casella verde per vedere il percorso; clicca per muoverti."
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
		if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse)
			return @event is InputEventKey { Keycode: Key.Space or Key.X or Key.F or Key.T };
		var point = GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position;
		if (PersonAt(point) is { } person)
		{
			SelectCombatant(person);
			return true;
		}
		if (GroundItemAt(ScreenToCell(point)) is { } item)
		{
			PickUp(item);
			return true;
		}
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

	private void ExecuteCombatAttack(TargetView target)
	{
		var result = _sim.Execute(new AttackCommand { Actor = _sim.Player, Target = target.Target,
			Attack = target.Attack, NonLethal = _combatNonLethal.ButtonPressed && IsMeleeAttack(target.Attack) });
		ShowMessage(result.Message);
		Refresh();
	}

	private void ExecuteCombatAction(CombatAction action)
	{
		var result = _sim.Execute(new CombatActionCommand { Actor = _sim.Player, Action = action });
		ShowMessage(result.Message);
		Refresh();
	}

	private void EndCombatTurn()
	{
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
