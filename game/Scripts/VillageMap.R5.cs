using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Survival, journey choices, and player-known hazards in the mapped client.</summary>
public partial class VillageMap
{
	private PanelContainer _travelChoicePanel = null!;
	private Label _travelChoiceTitle = null!;
	private VBoxContainer _travelDestinations = null!;
	private VBoxContainer _travelPaces = null!;
	private Button _travelBack = null!;
	private Button _travelCancel = null!;
	private readonly Dictionary<TravelPace, Button> _travelPaceButtons = new();
	private IReadOnlyList<RouteView> _travelExitRoutes = Array.Empty<RouteView>();
	private RouteView? _travelSelectedRoute;
	private LocationId? _travelDestination;
	private Node2D _trapLayer = null!;
	private readonly Dictionary<GridPos, Node2D> _trapMarks = new();
	private string _trapMarksKey = "";
	private PopupMenu _trapMenu = null!;
	private GridPos? _selectedTrap;
	private Button _quickSearch = null!;
	private Button _quickClimb = null!;
	private Button _quickCombatClimb = null!;

	private static string TravelPaceName(TravelPace pace) => pace switch
	{
		TravelPace.Fast => "veloce",
		TravelPace.Slow => "lenta",
		_ => "normale",
	};

	private void BuildR5Map()
	{
		_trapLayer = new Node2D { ZIndex = 4 };
		_mapRoot.AddChild(_trapLayer);
	}

	private void BuildR5Ui(CanvasLayer ui)
	{
		_travelChoicePanel = HudPanel(440, 840, 150, 405);
		_travelChoicePanel.Visible = false;
		_travelChoicePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.09f, 0.12f, 0.98f),
		});
		ui.AddChild(_travelChoicePanel);
		var choices = new VBoxContainer();
		choices.AddThemeConstantOverride("separation", 7);
		_travelChoicePanel.AddChild(choices);
		_travelChoiceTitle = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		choices.AddChild(_travelChoiceTitle);
		_travelDestinations = new VBoxContainer();
		_travelDestinations.AddThemeConstantOverride("separation", 7);
		choices.AddChild(_travelDestinations);
		_travelPaces = new VBoxContainer();
		_travelPaces.AddThemeConstantOverride("separation", 7);
		choices.AddChild(_travelPaces);
		foreach (var (pace, label) in new[]
		{
			(TravelPace.Fast, "Veloce"),
			(TravelPace.Normal, "Normale"),
			(TravelPace.Slow, "Lenta"),
		})
		{
			var selected = pace;
			var button = new Button { Text = label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			button.Pressed += () => ChooseTravelPace(selected);
			_travelPaces.AddChild(button);
			_travelPaceButtons[pace] = button;
		}
		_travelBack = new Button { Text = "Altre destinazioni", Visible = false };
		_travelBack.Pressed += ShowTravelDestinations;
		choices.AddChild(_travelBack);
		_travelCancel = new Button { Text = "Annulla" };
		_travelCancel.Pressed += CancelTravelChoice;
		choices.AddChild(_travelCancel);

		_trapMenu = new PopupMenu();
		_trapMenu.IdPressed += id =>
		{
			_trapMenu.Hide();
			if (id == 1 && _selectedTrap is { } at) DisarmKnownTrap(at);
			else if (id == 2) SearchForTraps();
			else if (id == 3) ClimbOutOfPit();
		};
		ui.AddChild(_trapMenu);
	}

	private static Duration TravelDuration(RouteView route, TravelPace pace) => pace switch
	{
		TravelPace.Fast => Duration.FromTicks(route.TravelTime.Ticks * 3 / 4),
		TravelPace.Slow => Duration.FromTicks(route.TravelTime.Ticks * 3 / 2),
		_ => route.TravelTime,
	};

	private static string TravelDurationText(Duration duration)
	{
		var seconds = (duration.Ticks + GameTime.TicksPerSecond - 1) / GameTime.TicksPerSecond;
		return seconds >= 3600 ? $"{seconds / 3600} h {seconds / 60 % 60:00} min"
			: seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds / 60}:{seconds % 60:00} min";
	}

	private void OfferExitRoutes(ExitView exit)
	{
		_travelExitRoutes = exit.Routes;
		if (_travelExitRoutes.Count == 0)
		{
			ShowMessage("Da qui non parte alcun sentiero.");
			return;
		}
		if (_travelExitRoutes.Count == 1)
		{
			OfferTravel(_travelExitRoutes[0].To);
			return;
		}
		ShowTravelDestinations();
	}

	private void ShowTravelDestinations()
	{
		_travelDestination = null;
		_travelSelectedRoute = null;
		_travelChoiceTitle.Text = "Dove vuoi andare? Scegli un sentiero.";
		foreach (var child in _travelDestinations.GetChildren())
		{
			_travelDestinations.RemoveChild(child);
			child.QueueFree();
		}
		foreach (var route in _travelExitRoutes)
		{
			var destination = route.To;
			var name = _view.Locations.First(l => l.Id == destination).Name;
			var button = new Button { Text = $"{name} · {TravelDurationText(route.TravelTime)} a passo normale",
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			button.Pressed += () => OfferTravel(destination);
			_travelDestinations.AddChild(button);
		}
		_travelDestinations.Visible = true;
		_travelPaces.Visible = false;
		_travelBack.Visible = false;
		_travelChoicePanel.OffsetBottom = 325;
		_travelChoicePanel.Visible = true;
	}

	private void OfferTravel(LocationId destination)
	{
		var route = _view.Routes.FirstOrDefault(r => r.From == _view.Location && r.To == destination);
		if (route is null)
		{
			CancelTravelChoice();
			ShowMessage("Questo sentiero non è più disponibile.");
			return;
		}
		_travelDestination = destination;
		_travelSelectedRoute = route;
		var name = _view.Locations.FirstOrDefault(l => l.Id == destination)?.Name ?? destination.Value;
		_travelChoiceTitle.Text = $"Parti per {name}. Scegli l'andatura:";
		foreach (var (pace, button) in _travelPaceButtons)
			button.Text = $"{TravelPaceName(pace)} · {TravelDurationText(TravelDuration(route, pace))}";
		_travelDestinations.Visible = false;
		_travelPaces.Visible = true;
		_travelBack.Visible = _travelExitRoutes.Count > 1;
		_travelChoicePanel.OffsetBottom = _travelBack.Visible ? 390 : 355;
		_travelChoicePanel.Visible = true;
	}

	private void CancelTravelChoice()
	{
		_travelChoicePanel.Visible = false;
		_travelDestination = null;
		_travelSelectedRoute = null;
		_travelExitRoutes = Array.Empty<RouteView>();
	}

	private void ChooseTravelPace(TravelPace pace)
	{
		if (_travelDestination is not { } destination || _travelSelectedRoute?.To != destination) return;
		CancelTravelChoice();
		StartActivity(new TravelCommand { Actor = _sim.Player, Destination = destination, Pace = pace }, "viaggio");
	}

	private void SearchForTraps() => StartActivity(new SearchCommand { Actor = _sim.Player }, "cerca trappole");
	private void ClimbOutOfPit() => StartActivity(new ClimbOutCommand { Actor = _sim.Player }, "risali dalla fossa");
	private void DisarmKnownTrap(GridPos at) => StartActivity(new DisarmCommand { Actor = _sim.Player, At = at }, "disinnesca");

	private void RefreshR5Controls()
	{
		_quickSearch.Disabled = _view.Dead || _view.Unconscious || _view.Map is null
			|| _view.Combat is not null || _view.Action is not null || _view.InPit;
		_quickClimb.Disabled = _view.Dead || _view.Unconscious || _view.Action is not null;
		_quickCombatClimb.Disabled = _view.Combat is not { IsYourTurn: true, You.ActionAvailable: true };
		RefreshQuickbarDeck();
		RefreshKnownTraps();
	}

	private void RefreshKnownTraps()
	{
		_trapLayer.Visible = _view.Map is not null && !_view.Dead;
		var key = _view.Map?.Area + "|" + string.Join(";", _view.KnownTraps.Select(t =>
			$"{t.At}:{t.Kind}:{t.Sprung}:{t.Disarmed}"));
		if (key == _trapMarksKey) return;
		_trapMarksKey = key;
		foreach (var mark in _trapMarks.Values) mark.QueueFree();
		_trapMarks.Clear();
		if (_view.Map is null) return;
		foreach (var trap in _view.KnownTraps)
		{
			var mark = new Node2D { Position = CellToScreen(trap.At) };
			var tint = trap.Disarmed ? new Color(0.53f, 0.67f, 0.60f)
				: trap.Kind == TrapKind.Pit ? new Color(0.92f, 0.58f, 0.24f) : new Color(0.94f, 0.35f, 0.32f);
			var shape = new Line2D { Points = new[] { new Vector2(0, -12), new Vector2(18, 0),
				new Vector2(0, 12), new Vector2(-18, 0), new Vector2(0, -12) },
				DefaultColor = tint, Width = 2.5f };
			mark.AddChild(shape);
			var symbol = new Label { Text = trap.Disarmed ? "✓" : trap.Sprung ? "!" : trap.Kind == TrapKind.Pit ? "○" : "!",
				Position = new Vector2(-7, -13), MouseFilter = Control.MouseFilterEnum.Ignore };
			symbol.AddThemeColorOverride("font_color", tint);
			symbol.AddThemeConstantOverride("outline_size", 3);
			symbol.AddThemeColorOverride("font_outline_color", Colors.Black);
			mark.AddChild(symbol);
			_trapLayer.AddChild(mark);
			_trapMarks[trap.At] = mark;
		}
	}

	private TrapView? KnownTrapAt(Vector2 point) => _view.KnownTraps.FirstOrDefault(t => t.At == ScreenToCell(point));

	private void OpenTrapMenu(TrapView trap, Vector2 screen)
	{
		_selectedTrap = trap.At;
		_trapMenu.Clear();
		_trapMenu.AddItem(trap.Kind == TrapKind.Pit ? "Fossa conosciuta" : "Ago nella serratura");
		_trapMenu.SetItemDisabled(0, true);
		_trapMenu.AddItem("Disinnesca", 1);
		_trapMenu.SetItemDisabled(1, trap.Disarmed || trap.Sprung || _view.Combat is not null || _view.InPit);
		_trapMenu.AddItem("Cerca altre trappole", 2);
		_trapMenu.SetItemDisabled(2, _view.Combat is not null || _view.InPit);
		if (_view.InPit) _trapMenu.AddItem("Risali dalla fossa", 3);
		_trapMenu.Position = new Vector2I((int)screen.X, (int)screen.Y);
		_trapMenu.Popup();
	}

	internal bool TravelChoiceOpenForSmoke => _travelChoicePanel.Visible;
	internal void ChooseTravelPaceForSmoke(TravelPace pace) => ChooseTravelPace(pace);
	internal bool TrapMarkVisibleForSmoke(GridPos at) => _trapMarks.TryGetValue(at, out var mark) && mark.Visible;
	internal void OpenTrapMenuForSmoke(GridPos at)
	{
		var trap = _view.KnownTraps.Single(t => t.At == at);
		OpenTrapMenu(trap, new Vector2(640, 360));
	}
	internal bool TrapDisarmEnabledForSmoke => !_trapMenu.IsItemDisabled(_trapMenu.GetItemIndex(1));
	internal void DisarmTrapFromMenuForSmoke() => _trapMenu.EmitSignal(PopupMenu.SignalName.IdPressed, 1L);
	internal void SearchFromQuickbarForSmoke() => _quickSearch.EmitSignal(BaseButton.SignalName.Pressed);
	internal bool ClimbQuickActionVisibleForSmoke => _quickClimb.Visible;
	internal void ClimbFromQuickbarForSmoke() => _quickClimb.EmitSignal(BaseButton.SignalName.Pressed);
}
