using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Actions and markings for barriers the player has actually learned about.</summary>
public partial class VillageMap
{
	private PanelContainer _barrierMenu = null!;
	private VBoxContainer _barrierActions = null!;
	private Label _barrierTitle = null!;
	private Label _barrierClue = null!;
	private readonly Dictionary<Node2D, Label> _barrierMarks = new();
	private readonly Dictionary<Node2D, Color> _activeBarrierBase = new();
	private readonly List<Line2D> _barrierActivityOutlines = new();

	private void ClearBarrierActivity()
	{
		foreach (var (panel, color) in _activeBarrierBase)
			panel.Modulate = color;
		_activeBarrierBase.Clear();
		foreach (var outline in _barrierActivityOutlines)
		{
			RemoveChild(outline);
			outline.QueueFree();
		}
		_barrierActivityOutlines.Clear();
	}

	private void RefreshBarrierActivity()
	{
		foreach (var actor in _view.VisibleActors)
		{
			if (actor.AtBarrier is not { } work) continue;
			Node2D? panel = work.IsDoor
				? _doorPanels.GetValueOrDefault(work.A)
				: _windowPanels.GetValueOrDefault((work.A, work.B));
			if (panel is null || !panel.Visible || _activeBarrierBase.ContainsKey(panel)) continue;
			_activeBarrierBase[panel] = panel.Modulate;
			var color = panel.Modulate;
			panel.Modulate = color.Lerp(new Color(1f, 0.78f, 0.30f, color.A), 0.55f);
			var diamond = Diamond(0.58f);
			var outline = new Line2D
			{
				Position = work.IsDoor ? CellToScreen(work.A) : CellToScreen(work.A).Lerp(CellToScreen(work.B), 0.5f),
				Points = diamond.Append(diamond[0]).ToArray(), Width = 3f, ZIndex = 14,
				DefaultColor = new Color(1f, 0.75f, 0.25f, 0.95f),
			};
			AddChild(outline);
			_barrierActivityOutlines.Add(outline);
		}
	}

	private void BuildBarrierUi(CanvasLayer ui)
	{
		_barrierMenu = new PanelContainer { OffsetLeft = 900, OffsetRight = 1264, OffsetTop = 100,
			OffsetBottom = 560, Visible = false };
		_barrierMenu.AddThemeStyleboxOverride("panel", new StyleBoxFlat
			{ BgColor = new Color(0.10f, 0.11f, 0.13f, 0.96f) });
		ui.AddChild(_barrierMenu);
		_barrierActions = new VBoxContainer();
		_barrierActions.AddThemeConstantOverride("separation", 6);
		_barrierMenu.AddChild(_barrierActions);
		_barrierTitle = new Label();
		_barrierTitle.AddThemeFontSizeOverride("font_size", 18);
		_barrierActions.AddChild(_barrierTitle);
		_barrierClue = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_barrierActions.AddChild(_barrierClue);
	}

	private void AddBarrierMark(Node2D panel, float height)
	{
		var mark = new Label { Position = panel.Position + new Vector2(-29, -height - 21), ZIndex = 12,
			CustomMinimumSize = new Vector2(58, 0), HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		mark.AddThemeFontSizeOverride("font_size", 11);
		mark.AddThemeConstantOverride("outline_size", 4);
		mark.AddThemeColorOverride("font_outline_color", Colors.Black);
		AddChild(mark);
		_barrierMarks[panel] = mark;
	}

	private KnownBarrierView? KnownDoor(GridPos at) => _view.KnownBarriers.FirstOrDefault(k => k.Door == at);

	private KnownBarrierView? KnownWindow(GridPos a, GridPos b) =>
		_view.KnownBarriers.FirstOrDefault(k => k.Door is null && k.A == a && k.B == b);

	private void SetBarrierMark(Node2D panel, KnownBarrierView? known)
	{
		if (!_barrierMarks.TryGetValue(panel, out var mark))
			return;
		mark.Visible = panel.Visible && known is { Locked: true } or { Stuck: true } or { Broken: true };
		if (!mark.Visible)
			return;
		mark.Text = known!.Broken ? "ROTTA" : known.Locked ? "CHIAVE" : "BLOCC.";
		mark.Modulate = known.Broken ? new Color("f3b19c") : known.Locked
			? new Color("ffe3a2") : new Color("f5c8a0");
		mark.TooltipText = known.Clue;
	}

	private void RefreshBarrierMarks()
	{
		foreach (var (at, panel) in _doorPanels)
			SetBarrierMark(panel, KnownDoor(at));
		foreach (var ((a, b), panel) in _windowPanels)
			SetBarrierMark(panel, KnownWindow(a, b));
		foreach (var mark in _barrierMarks.Values)
			mark.Visible &= _view.Map is not null && _view.Position is not null;
		if (_view.Dead || _view.Map is null || _view.Combat is { IsYourTurn: false })
			_barrierMenu.Visible = false;
	}

	private void OpenDoorMenu(GridPos at)
	{
		if (_view.Doors.FirstOrDefault(d => d.At == at) is not { Known: DoorKnowledge.Seen, Open: { } open })
			return;
		OpenBarrierMenu(KnownDoor(at), door: at, a: null, b: null, open);
	}

	private void OpenWindowMenu(MapEdgeView edge)
	{
		if (edge.Open is not { } open)
			return;
		OpenBarrierMenu(KnownWindow(edge.A, edge.B), door: null, edge.A, edge.B, open, edge);
	}

	private void OpenBarrierMenu(KnownBarrierView? known, GridPos? door, GridPos? a, GridPos? b, bool open,
		MapEdgeView? window = null)
	{
		if (_view.Dead || _view.Combat is { IsYourTurn: false })
			return;
		var combat = _view.Combat;
		var interaction = combat is null ? "" : combat.You.InteractionAvailable ? " (gratis)" : " (usa l'azione)";
		var action = combat is null ? "1 minuto" : "usa l'azione";
		var canInteract = combat is null || combat.You.InteractionAvailable || combat.You.ActionAvailable;
		var canAct = combat is null || combat.You.ActionAvailable;
		_personMenu.Visible = false;
		_barrierMenu.OffsetLeft = _view.Combat is null ? 900 : 525;
		_barrierMenu.OffsetRight = _view.Combat is null ? 1264 : 890;
		_barrierMenu.Visible = true;
		foreach (var child in _barrierActions.GetChildren().Skip(2).ToArray())
		{
			_barrierActions.RemoveChild(child);
			child.QueueFree();
		}
		_barrierTitle.Text = door is not null ? "Porta" : "Finestra";
		_barrierClue.Text = known is null ? "Non sai se è chiusa a chiave o bloccata."
			: known.Broken ? $"Serratura rotta: {known.Clue}"
			: known.Locked ? $"Chiusa a chiave: {known.Clue}"
			: known.Stuck ? $"Bloccata: {known.Clue}"
			: known.Clue;
		if (window is not null)
		{
			var visible = window.Glass switch
			{
				WindowGlass.Broken => "Vetro rotto; restano frammenti.",
				WindowGlass.Cleared => "Vetro ripulito.",
				_ => "",
			};
			if (window.Barred == true)
				visible += (visible.Length > 0 ? " " : "") + "Sbarre alla finestra.";
			if (visible.Length > 0)
				_barrierClue.Text = visible + (known is null ? "" : $" {known.Clue}");
		}
		if (window is null || window.Glass == WindowGlass.Intact)
			AddBarrierButton((open ? "Chiudi" : "Apri") + interaction, () =>
			{
				if (combat is not null)
					RunCombatBarrier(door is { } at
						? new DoorCommand { Actor = _sim.Player, At = at, Open = !open }
						: new WindowCommand { Actor = _sim.Player, A = a!.Value, B = b!.Value, Open = !open });
				else if (door is { } at) ClickDoor(at);
				else if (_view.Map?.Edges.FirstOrDefault(e => e.Kind == MapEdgeKind.Window && e.A == a && e.B == b) is { } edge)
					ClickWindow(edge);
			}, canInteract);
		if (window is { Glass: WindowGlass.Intact })
			AddBarrierButton(combat is null ? "Rompi il vetro (6 secondi, rumoroso)" : "Rompi il vetro (usa l'azione, rumoroso)", () => RunBarrierWork(new BreakGlassCommand
				{ Actor = _sim.Player, A = a, B = b }), canAct);
		if (window is { Glass: WindowGlass.Broken } && combat is null)
			AddBarrierButton("Togli i frammenti (1 minuto)", () => StartActivity(new ClearGlassCommand
				{ Actor = _sim.Player, A = a, B = b }));
		if (window is { Barred: false } && (open || window.Glass is WindowGlass.Broken or WindowGlass.Cleared))
			AddBarrierButton(combat is null ? "Scavalca" : $"Scavalca ({ClimbCostInches(a!.Value, b!.Value) / (double)Spot.PerFoot:0.#} piedi)",
				() => RunBarrierWork(new ClimbCommand
				{ Actor = _sim.Player, A = a, B = b }),
				combat is null || ClimbCostInches(a!.Value, b!.Value) <= combat.You.MovementLeftInches);
		if (window is { Barred: true })
			AddBarrierButton($"Forza le sbarre ({action}, rumoroso)", () => RunBarrierWork(new ForceCommand
				{ Actor = _sim.Player, A = a, B = b }), canAct);
		if (window is not { Glass: WindowGlass.Broken or WindowGlass.Cleared }
			&& known is { Locked: true } && _view.Inventory.Any(i => i.Item.Value == "thieves-tools" && i.Count > 0))
			AddBarrierButton($"Scassina ({action})", () => RunBarrierWork(new PickLockCommand
				{ Actor = _sim.Player, Door = door, A = a, B = b }), canAct);
		if (window is not { Barred: true } && window is not { Glass: WindowGlass.Broken or WindowGlass.Cleared }
			&& known is ({ Locked: true } or { Stuck: true }))
			AddBarrierButton($"Forza ({action}, rumoroso)", () => RunBarrierWork(new ForceCommand
				{ Actor = _sim.Player, Door = door, A = a, B = b }), canAct);
		if (window is not { Glass: WindowGlass.Broken or WindowGlass.Cleared } && known is { Locked: true })
			AddBarrierButton("Apri con la chiave" + interaction, () => UseBarrierLock(door, a, b, locked: false), canInteract);
		else if (window is not { Glass: WindowGlass.Broken or WindowGlass.Cleared }
			&& known is { HasLock: true, Locked: false, Stuck: false, Broken: false } && !open)
			AddBarrierButton("Chiudi a chiave" + interaction, () => UseBarrierLock(door, a, b, locked: true), canInteract);
		AddBarrierButton("Chiudi menu", () => { });
		var buttons = _barrierActions.GetChildren().OfType<Button>().Count();
		_barrierMenu.OffsetBottom = _barrierMenu.OffsetTop + Math.Min(460,
			85 + 40 * buttons + (_barrierClue.Text.Length > 55 ? 24 : 0));
	}

	private void AddBarrierButton(string label, Action action, bool enabled = true)
	{
		var button = new Button { Text = label, Disabled = !enabled };
		button.Pressed += () =>
		{
			_barrierMenu.Visible = false;
			action();
		};
		_barrierActions.AddChild(button);
	}

	private int ClimbCostInches(GridPos a, GridPos b)
	{
		if (_view.Spot is not { } from)
			return 10 * Spot.PerFoot;
		var beyond = from.Cell == a ? b : a;
		return (int)Math.Ceiling(from.DistanceTo(Spot.CenterOf(beyond)) - 1e-9) + 5 * Spot.PerFoot;
	}

	private void RunCombatBarrier(Command command)
	{
		var from = _view.Spot;
		var result = _sim.Execute(command);
		var next = _sim.GetPlayerView();
		if (command is ClimbCommand && result.Success && from is { } start && next is { Spot: { } end, Dead: false }
			&& start != end)
			StartCombatVisual(_view.Id, new[] { start, end });
		ShowMessage(result.Message);
		Refresh();
	}

	private void RunBarrierWork(Command command)
	{
		if (_view.Combat is not null) RunCombatBarrier(command);
		else StartActivity(command);
	}

	private void UseBarrierLock(GridPos? door, GridPos? a, GridPos? b, bool locked)
	{
		var command = new LockCommand { Actor = _sim.Player, Door = door, A = a, B = b, Locked = locked };
		if (_view.Combat is not null)
		{
			RunCombatBarrier(command);
			return;
		}
		Order(locked ? "chiudi a chiave" : "apri con la chiave", walk: false, () =>
		{
			var result = _sim.Execute(command);
			ShowMessage(result.Message);
			Refresh();
		});
	}

	internal bool BarrierMenuVisibleForSmoke => _barrierMenu.Visible;
	internal void OpenDoorMenuForSmoke(GridPos at) => OpenDoorMenu(at);
	internal void OpenWindowMenuForSmoke(GridPos a, GridPos b) =>
		OpenWindowMenu(_view.Map!.Edges.Single(e => e.Kind == MapEdgeKind.Window && e.A == a && e.B == b));
	internal IReadOnlyList<string> BarrierActionsForSmoke => _barrierActions.GetChildren().OfType<Button>()
		.Select(button => button.Text).ToArray();
	internal bool BarrierActionEnabledForSmoke(string label) => _barrierActions.GetChildren().OfType<Button>()
		.Single(button => button.Text == label).Disabled == false;
	internal void PressBarrierActionForSmoke(string label) => _barrierActions.GetChildren().OfType<Button>()
		.Single(button => button.Text == label).EmitSignal(Button.SignalName.Pressed);
	internal void CloseBarrierMenuForSmoke() => _barrierMenu.Visible = false;
	internal string? BarrierMarkForSmoke(GridPos at) => _doorPanels.TryGetValue(at, out var panel)
		? _barrierMarks.TryGetValue(panel, out var mark) && mark.Visible ? mark.Text : null : null;
	internal bool BarrierActivityForSmoke(BarrierRefView barrier)
	{
		var panel = barrier.IsDoor ? _doorPanels.GetValueOrDefault(barrier.A)
			: _windowPanels.GetValueOrDefault((barrier.A, barrier.B));
		return panel is not null && _activeBarrierBase.ContainsKey(panel) && _barrierActivityOutlines.Count > 0;
	}
}
