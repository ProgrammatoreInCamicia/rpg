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

	private void BuildBarrierUi(CanvasLayer ui)
	{
		_barrierMenu = new PanelContainer { OffsetLeft = 900, OffsetRight = 1264, OffsetTop = 100,
			OffsetBottom = 360, Visible = false };
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
		if (_view.Combat is not null || _view.Dead || _view.Map is null)
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
		OpenBarrierMenu(KnownWindow(edge.A, edge.B), door: null, edge.A, edge.B, open);
	}

	private void OpenBarrierMenu(KnownBarrierView? known, GridPos? door, GridPos? a, GridPos? b, bool open)
	{
		if (_view.Combat is not null || _view.Dead)
			return;
		_personMenu.Visible = false;
		_barrierMenu.Visible = true;
		foreach (var child in _barrierActions.GetChildren().Skip(2).ToArray())
		{
			_barrierActions.RemoveChild(child);
			child.QueueFree();
		}
		_barrierTitle.Text = door is not null ? "Porta" : "Finestra";
		_barrierClue.Text = known is null ? "Non sai se è chiusa a chiave o bloccata."
			: known.Broken ? $"Serratura rotta. {known.Clue}"
			: known.Locked ? $"Chiusa a chiave. {known.Clue}"
			: known.Stuck ? $"Bloccata. {known.Clue}"
			: known.Clue;
		AddBarrierButton(open ? "Chiudi" : "Apri", () =>
		{
			if (door is { } at) ClickDoor(at);
			else if (_view.Map?.Edges.FirstOrDefault(e => e.Kind == MapEdgeKind.Window && e.A == a && e.B == b) is { } edge)
				ClickWindow(edge);
		});
		if (known is { Locked: true } && _view.Inventory.Any(i => i.Item.Value == "thieves-tools" && i.Count > 0))
			AddBarrierButton("Scassina (1 minuto)", () => StartActivity(new PickLockCommand
				{ Actor = _sim.Player, Door = door, A = a, B = b }));
		if (known is { Locked: true } or { Stuck: true })
			AddBarrierButton("Forza (1 minuto, rumoroso)", () => StartActivity(new ForceCommand
				{ Actor = _sim.Player, Door = door, A = a, B = b }));
		if (known is { Locked: true })
			AddBarrierButton("Apri con la chiave", () => UseBarrierLock(door, a, b, locked: false));
		else if (known is { Locked: false, Stuck: false, Broken: false } && !open)
			AddBarrierButton("Chiudi a chiave", () => UseBarrierLock(door, a, b, locked: true));
		AddBarrierButton("Chiudi menu", () => { });
	}

	private void AddBarrierButton(string label, Action action)
	{
		var button = new Button { Text = label };
		button.Pressed += () =>
		{
			_barrierMenu.Visible = false;
			action();
		};
		_barrierActions.AddChild(button);
	}

	private void UseBarrierLock(GridPos? door, GridPos? a, GridPos? b, bool locked) =>
		Order(locked ? "chiudi a chiave" : "apri con la chiave", walk: false, () =>
		{
			var result = _sim.Execute(new LockCommand
				{ Actor = _sim.Player, Door = door, A = a, B = b, Locked = locked });
			ShowMessage(result.Message);
			Refresh();
		});

	internal bool BarrierMenuVisibleForSmoke => _barrierMenu.Visible;
	internal void OpenDoorMenuForSmoke(GridPos at) => OpenDoorMenu(at);
	internal IReadOnlyList<string> BarrierActionsForSmoke => _barrierActions.GetChildren().OfType<Button>()
		.Select(button => button.Text).ToArray();
	internal string? BarrierMarkForSmoke(GridPos at) => _doorPanels.TryGetValue(at, out var panel)
		? _barrierMarks.TryGetValue(panel, out var mark) && mark.Visible ? mark.Text : null : null;
}
