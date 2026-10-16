using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Survival, journey choices, and player-known hazards in the mapped client.</summary>
public partial class VillageMap
{
	private PanelContainer _travelChoicePanel = null!;
	private Label _travelChoiceTitle = null!;
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
		AddChild(_trapLayer);
	}

	private void BuildR5Ui(CanvasLayer ui)
	{
		_travelChoicePanel = HudPanel(440, 840, 170, 368);
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
		foreach (var (pace, label) in new[]
		{
			(TravelPace.Fast, "Veloce · ¾ del tempo"),
			(TravelPace.Normal, "Normale · durata prevista"),
			(TravelPace.Slow, "Lenta · ⅓ del tempo in più"),
		})
		{
			var selected = pace;
			var button = new Button { Text = label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			button.Pressed += () => ChooseTravelPace(selected);
			choices.AddChild(button);
		}
		var cancel = new Button { Text = "Annulla" };
		cancel.Pressed += () => _travelChoicePanel.Visible = false;
		choices.AddChild(cancel);

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

	private void OfferTravel(LocationId destination)
	{
		_travelDestination = destination;
		var name = _view.Locations.FirstOrDefault(l => l.Id == destination)?.Name ?? destination.Value;
		_travelChoiceTitle.Text = $"Parti per {name}. Scegli l'andatura del viaggio.";
		_travelChoicePanel.Visible = true;
	}

	private void ChooseTravelPace(TravelPace pace)
	{
		if (_travelDestination is not { } destination) return;
		_travelChoicePanel.Visible = false;
		_travelDestination = null;
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
