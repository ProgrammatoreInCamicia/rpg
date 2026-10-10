using Godot;
using RpgSandbox.Sim;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>
/// Village client: the village as an isometric grid drawn from <see cref="MapView"/>. Click a square to walk there,
/// someone to walk up to them and talk, a door to open or close it; each square is shaded by its own light.
/// Real time with pause (BG1/BG2): the world runs at the chosen pace (1×/3×/6× game seconds per real second) whether
/// the player moves or not; Space pauses, X stops walking. Positions, paths, reach and timing all come from the core.
/// </summary>
public partial class VillageMap : Node2D
{
	private const float TileW = 64f;
	private const float TileH = 32f;
	private const float WallHeight = 26f;
	private const string SavePath = "user://village.json";

	/// <summary>Longest real time a fixed-length activity (deposit, conversation) takes to play out on screen.</summary>
	private const double MaxActivityRealSeconds = 3;

	private static readonly Color Road = new(0.45f, 0.47f, 0.38f);
	private static readonly Color InnFloor = new(0.55f, 0.42f, 0.30f);
	private static readonly Color HouseFloor = new(0.51f, 0.43f, 0.34f);
	private static readonly Color Yard = new(0.66f, 0.58f, 0.38f);
	private static readonly Color ForestRoad = new(0.38f, 0.48f, 0.34f);
	private static readonly Color Wall = new(0.42f, 0.38f, 0.40f);
	private static readonly Color Hover = new(1f, 1f, 1f, 0.25f);
	private static readonly Color DoorWood = new(0.50f, 0.32f, 0.18f);

	private SimulationSession _sim = null!;
	private PlayerView _view = null!;

	private Node2D _floor = null!;
	private Node2D _sorted = null!;
	private Camera2D _camera = null!;
	private Polygon2D _hover = null!;
	private Polygon2D _target = null!;
	private readonly Dictionary<GridPos, CanvasItem> _lit = new(); // floor, walls and stores, shaded by their square's light
	private float[,] _glow = new float[0, 0];
	private string _lightKey = "";
	private readonly Dictionary<ActorId, Node2D> _tokens = new();
	private readonly Dictionary<StoreId, Label> _storeLabels = new();

	private Label _clock = null!;
	private Label _status = null!;
	private Label _food = null!;
	private Button _stop = null!;
	private Button _pause = null!;
	private CheckButton _sneakToggle = null!;
	private Label _watched = null!;
	private Button _torch = null!;
	private readonly Dictionary<GridPos, Node2D> _doorPanels = new(); // shown while the door is closed
	private bool _sneak;
	private HBoxContainer _storeRow = null!;
	private SpinBox _amount = null!;
	private VBoxContainer _people = null!;
	private Label _message = null!;
	private Label _events = null!;
	private PanelContainer _offMapPanel = null!;
	private Label _offMapLabel = null!;
	private Button _returnToVillage = null!;

	private ActionId? _running;
	private ActorId? _approaching; // walking up to someone to talk to them
	private LocationId? _exitAfterWalking;
	private ActorId? _talkingTo;
	private bool _walking;
	private double _speed = 1;

	/// <summary>
	/// Exploration pace: game seconds played per real second while walking (Project Zomboid style). The rules are
	/// unchanged (one square takes one game second at Speed 30); only how long the player waits on screen changes.
	/// </summary>
	private double _pace = 3;
	private bool _paused;
	private readonly List<(string Label, bool Walk, Action Run)> _queue = new(); // orders given while paused
	private readonly Dictionary<double, Button> _paceButtons = new();
	private double _pending;
	private string _peopleKey = "";

	public override void _Ready()
	{
		_sim = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		_view = _sim.GetPlayerView();

		BuildMap();
		BuildUi();
		Refresh();

		var smoke = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--village-smoke="));
		if (smoke is not null)
			AddChild(new VillageSmoke(this, smoke["--village-smoke=".Length..]));
	}

	public override void _Process(double delta)
	{
		Pan(delta);
		UpdateHover();

		// The world goes on whether the player moves or not (real time with pause, as in BG1/BG2): the exploration pace
		// while walking or standing, a fast-forward during fixed-length activities. Only the pause stops the clock.
		if (!_paused)
		{
			_pending += delta * (_running is not null && !_walking ? _speed : _pace);
			var whole = (long)_pending;
			if (whole > 0)
			{
				_pending -= whole;
				ActorId? arrived = null;
				LocationId? exitReached = null;
				if (_running is { } action)
				{
					var result = _sim.AdvanceUntilCompleted(action, Duration.FromSeconds(whole));
					if (result.Outcome != AdvanceOutcome.TimeLimitReached)
					{
						_running = null;
						_walking = false;
						_pending = 0;
						if (result.Outcome == AdvanceOutcome.Completed)
						{
							arrived = _approaching;
							exitReached = _exitAfterWalking;
						}
						_approaching = null;
						_exitAfterWalking = null;
					}
				}
				else
					_sim.Advance(Duration.FromSeconds(whole));
				Refresh();
				if (arrived is { } who)
					OpenTalk(who);
				if (exitReached is { } exit)
					BeginExitJourney(exit);
			}
		}
		PlaceTokens();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space })
		{
			SetPaused(!_paused);
			return;
		}
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.X })
		{
			Stop();
			return;
		}
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F })
		{
			SetSneak(!_sneak);
			return;
		}
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.T })
		{
			ToggleTorch();
			return;
		}
		if (@event is not InputEventMouseButton { Pressed: true } mouse)
			return;
		if (_view.Map is null || _view.Position is null)
			return;
		switch (mouse.ButtonIndex)
		{
			case MouseButton.Left:
				var point = GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position;
				// A figure stands above its square: clicking on its body means that person, not the square behind.
				if (PersonAt(point) is { } person)
					TalkTo(person);
				else if (_view.Map.Exits.FirstOrDefault(e => e.At == ScreenToCell(point)) is { } exit)
					GoToExit(exit);
				else if (DoorAt(point) is { } door)
				{
					if (mouse.ShiftPressed)
						MoveTo(door); // walk onto an adjacent door instead of operating it
					else
						ClickDoor(door);
				}
				else
					MoveTo(ScreenToCell(point));
				break;
			case MouseButton.WheelUp:
				_camera.Zoom = (_camera.Zoom * 1.1f).Clamp(new Vector2(0.5f, 0.5f), new Vector2(3f, 3f));
				break;
			case MouseButton.WheelDown:
				_camera.Zoom = (_camera.Zoom / 1.1f).Clamp(new Vector2(0.5f, 0.5f), new Vector2(3f, 3f));
				break;
		}
	}

	// ---------------------------------------------------------------- commands

	internal PlayerView View => _view;
	internal WorldView SmokeWorld => _sim.GetWorldView();
	internal bool IsBusy => _running is not null;
	internal bool IsWalking => _walking;
	internal string LastMessage => _message.Text;
	internal string EventText => _events.Text;
	internal ActorId? TalkingTo => _talkingTo;
	internal bool OffMapPanelVisible => _offMapPanel.Visible;

	/// <summary>World position of a visible figure's body (used by the smoke runner to click on it).</summary>
	internal Vector2? BodyPointOf(ActorId id) =>
		_tokens.TryGetValue(id, out var token) && token.Visible ? token.Position + new Vector2(0, -18) : null;

	/// <summary>Walk to a square. A walk in progress is replaced (the core stops it on the square reached).</summary>
	public void MoveTo(GridPos cell) => Order($"vai in {cell}", walk: true, () =>
	{
		if (Walk(new MoveCommand { Actor = _sim.Player, To = cell, Stealthy = _sneak }))
		{
			_exitAfterWalking = null;
			_approaching = null;
		}
	});

	/// <summary>Clicking a map exit walks there, then starts its single Route.</summary>
	private void GoToExit(ExitView exit) => Order($"vai a {exit.Name}", walk: true, () =>
	{
		if (_view.Position == exit.At)
		{
			BeginExitJourney(exit.Location);
			return;
		}
		if (Walk(new MoveCommand { Actor = _sim.Player, To = exit.At, Stealthy = _sneak }))
		{
			_approaching = null;
			_exitAfterWalking = exit.Location;
			ShowMessage($"Vai a {exit.Name}.");
		}
	});

	private void BeginExitJourney(LocationId location)
	{
		var exit = _view.Map?.Exits.FirstOrDefault(e => e.Location == location);
		if (exit is null || _view.Position != exit.At)
		{
			ShowMessage("Non sei più all'uscita.");
			return;
		}
		if (exit.Routes.Count != 1)
		{
			ShowMessage("Scegli una destinazione dalla strada.");
			return;
		}
		StartActivity(new TravelCommand { Actor = _sim.Player, Destination = exit.Routes[0].To });
	}

	internal void ReturnToVillage() => StartActivity(new TravelCommand
	{
		Actor = _sim.Player, Destination = MappedVillageScenario.Ids.ForestRoad,
	});

	/// <summary>
	/// Click on someone: talk to them if they are next to you, otherwise walk up to them (the core picks the square
	/// before theirs on the way) and talk on arrival.
	/// </summary>
	public void TalkTo(ActorId person) => Order("vai a parlare", walk: true, () =>
	{
		if (_view.PeopleHere.Any(p => p.Id == person))
		{
			OpenTalk(person);
			return;
		}
		if (_view.VisibleActors.FirstOrDefault(a => a.Id == person) is not { Position: { } there } seen)
			return;
		if (Walk(new MoveCommand { Actor = _sim.Player, To = there, StopNextTo = true, Stealthy = _sneak }))
		{
			_exitAfterWalking = null;
			_approaching = person;
			ShowMessage($"Vai da {seen.Name}.");
		}
	});

	/// <summary>Click on a door: next to it, open or close it; from afar, walk there (walking opens it anyway).</summary>
	internal void ClickDoor(GridPos door) => Order("usa la porta", walk: false, () =>
	{
		if (_view.Position is { } here && here != door && Math.Max(Math.Abs(here.X - door.X), Math.Abs(here.Y - door.Y)) == 1)
		{
			var open = _view.Doors.FirstOrDefault(d => d.At == door)?.Open ?? true;
			var result = _sim.Execute(new DoorCommand { Actor = _sim.Player, At = door, Open = !open });
			ShowMessage(result.Message);
			Refresh();
			return;
		}
		MoveTo(door);
	});

	internal void ToggleTorch() => Order("torcia", walk: false, () =>
	{
		var result = _sim.Execute(new TorchCommand { Actor = _sim.Player, Lit = _view.TorchLitUntil is null });
		ShowMessage(result.Message);
		_lightKey = "";
		Refresh();
	});

	/// <summary>The door under a world point: its square, or the panel standing on it while closed.</summary>
	private GridPos? DoorAt(Vector2 point)
	{
		var cell = ScreenToCell(point);
		foreach (var door in _view.Doors)
		{
			if (door.At == cell)
				return door.At;
			if (!door.Open && _doorPanels.TryGetValue(door.At, out var panel)
				&& new Rect2(panel.Position + new Vector2(-TileW / 4f, -WallHeight - TileH / 2f), new Vector2(TileW / 2f, WallHeight + TileH)).HasPoint(point))
				return door.At;
		}
		return null;
	}

	internal bool IsPaused => _paused;

	/// <summary>Pause or resume the world. Orders given while paused start when it resumes.</summary>
	internal void SetPaused(bool paused)
	{
		_paused = paused;
		_pause.SetPressedNoSignal(paused);
		if (!paused)
		{
			var orders = _queue.ToList();
			_queue.Clear();
			foreach (var order in orders)
				order.Run();
		}
		Refresh();
	}

	internal int QueuedOrders => _queue.Count;

	/// <summary>
	/// Every order of the player goes through here. Running, it is carried out at once; paused, it waits in the queue
	/// and nothing in the world changes (no dice, no torch, no door) until the game resumes, when the queue runs in
	/// order. A new walk replaces a walk already waiting.
	/// </summary>
	private void Order(string label, bool walk, Action run)
	{
		if (!_paused)
		{
			run();
			return;
		}
		if (walk)
			_queue.RemoveAll(o => o.Walk);
		_queue.Add((label, walk, run));
		ShowMessage($"In coda: {string.Join(", ", _queue.Select(o => o.Label))}. Riprendi (Spazio) per eseguire, X annulla il cammino.");
		Refresh();
	}

	internal double Pace => _pace;

	/// <summary>Change the exploration pace; a walk in progress speeds up or slows down at once.</summary>
	internal void SetPace(double pace)
	{
		_pace = pace;
		foreach (var (value, button) in _paceButtons)
			button.SetPressedNoSignal(value == pace);
	}

	/// <summary>Switch between walking and sneaking; a walk in progress carries on to the same square in the new way.</summary>
	internal void SetSneak(bool on)
	{
		_sneak = on;
		_sneakToggle.SetPressedNoSignal(on);
		if (_walking && _view.Move is { } move)
			Order("cambia andatura", walk: true, () =>
			{
				var approaching = _approaching;
				if (Walk(new MoveCommand { Actor = _sim.Player, To = move.Path[^1], Stealthy = on }))
					_approaching = approaching;
			});
	}

	private bool Walk(MoveCommand command)
	{
		if (_running is not null && !_walking)
			return false; // busy with a fixed-length activity
		var result = _sim.Execute(command);
		if (!result.Success)
		{
			ShowMessage(result.Message);
			return false;
		}
		_running = result.Action;
		_walking = true;
		_target.Position = CellToScreen(_sim.GetPlayerView().Move!.Path[^1]);
		_target.Visible = true;
		Refresh();
		return true;
	}

	private void OpenTalk(ActorId person)
	{
		if (_view.PeopleHere.FirstOrDefault(p => p.Id == person) is not { } there)
		{
			var name = _view.VisibleActors.FirstOrDefault(a => a.Id == person)?.Name ?? "Chi cercavi";
			ShowMessage($"{name} non è più lì.");
			return;
		}
		_talkingTo = person;
		ShowMessage($"Parli con {there.Name}.");
		Refresh();
	}

	private void CloseTalk()
	{
		_talkingTo = null;
		Refresh();
	}

	public void Stop()
	{
		// Paused: X takes back a walk still waiting in the queue; otherwise the stop itself waits for the resume.
		if (_paused && _queue.RemoveAll(o => o.Walk) > 0)
		{
			ShowMessage("Ordine di cammino annullato.");
			Refresh();
			return;
		}
		Order("fermati", walk: false, StopNow);
	}

	private void StopNow()
	{
		if (!_walking)
			return;
		var result = _sim.Execute(new StopCommand { Actor = _sim.Player });
		_running = null;
		_walking = false;
		_approaching = null;
		_exitAfterWalking = null;
		_pending = 0;
		_target.Visible = false;
		ShowMessage(result.Message);
		Refresh();
	}

	/// <summary>A fixed-length activity: plays out quickly, at most a few real seconds.</summary>
	private void StartActivity(Command command) => Order("attività", walk: false, () =>
	{
		if (_running is not null)
		{
			ShowMessage("Prima finisci quello che stai facendo.");
			return;
		}
		var result = _sim.Execute(command);
		ShowMessage(result.Message);
		if (result.Success && result.Action is { } action)
		{
			_running = action;
			_walking = false;
			var length = result.CompletesAt!.Value.Since(_sim.Now).Seconds;
			_speed = Math.Max(1, length / MaxActivityRealSeconds);
		}
		Refresh();
	});

	internal void Deposit(int amount) =>
		StartActivity(new DepositFoodCommand { Actor = _sim.Player, Store = MappedVillageScenario.Ids.GranaryStore, Amount = amount });

	private void Take(int amount) =>
		StartActivity(new TakeFoodCommand { Actor = _sim.Player, Store = MappedVillageScenario.Ids.GranaryStore, Amount = amount });

	internal void TakeViaUi(int amount) => Take(amount); // same handler as the Prendi button, used by the village smoke

	private void Tell(ActorId listener, TopicView topic) =>
		StartActivity(new ReportCommand { Actor = _sim.Player, Recipient = listener, Observation = topic.Observation });

	private void SaveGame() => SaveGameToPath(ProjectSettings.GlobalizePath(SavePath));

	internal void SaveGameToPath(string path)
	{
		try
		{
			using (var stream = File.Create(path + ".tmp"))
				_sim.Save(stream);
			File.Move(path + ".tmp", path, overwrite: true);
			ShowMessage("Partita salvata.");
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			ShowMessage($"Salvataggio non riuscito: {e.Message}");
		}
	}

	private void LoadGame() => LoadGameFromPath(ProjectSettings.GlobalizePath(SavePath));

	internal void LoadGameFromPath(string path)
	{
		if (!File.Exists(path))
		{
			ShowMessage("Nessun salvataggio da caricare.");
			return;
		}
		LoadResult result;
		try
		{
			using var stream = File.OpenRead(path);
			result = SimulationSession.TryLoad(stream);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			ShowMessage($"Caricamento non riuscito: {e.Message}");
			return;
		}
		if (!result.Success)
		{
			ShowMessage($"Caricamento non riuscito: {result.Error}");
			return;
		}
		_sim = result.Session!;
		_peopleKey = "";
		_lightKey = "";
		_approaching = null;
		_exitAfterWalking = null;
		_talkingTo = null;
		_queue.Clear();
		_sneak = _sim.GetPlayerView().Sneaking is not null;
		_sneakToggle.SetPressedNoSignal(_sneak);
		// Resume a walk or activity that was in progress when saving.
		var view = _sim.GetPlayerView();
		_running = view.Action?.Id;
		_walking = view.Move is not null;
		_speed = Math.Max(1, (view.Action?.CompletesAt.Since(view.Action.StartedAt).Seconds ?? 1) / MaxActivityRealSeconds);
		_pending = 0;
		Refresh();
		ShowMessage("Partita caricata.");
	}

	private void ShowMessage(string text) => _message.Text = text;

	// ---------------------------------------------------------------- view

	private void Refresh()
	{
		_view = _sim.GetPlayerView();
		var onMap = _view.Map is not null && _view.Position is not null;
		_floor.Visible = onMap;
		_sorted.Visible = onMap;
		_offMapPanel.Visible = !onMap;
		if (!onMap)
		{
			_offMapLabel.Text = _view.Travel is { } travel
				? $"In viaggio verso {_view.Locations.Single(l => l.Id == travel.Destination).Name}\n" +
				  $"Arrivo tra {Math.Max(1, travel.ArrivesAt.Since(_view.Now).Seconds / 60)} minuti di gioco"
				: _view.Location is { } place ? _view.Locations.Single(l => l.Id == place).Name : "In viaggio";
			_returnToVillage.Visible = _view.Travel is null && _view.Location == MappedVillageScenario.Ids.BanditCamp;
		}
		var now = _view.Now;
		_clock.Text = $"Giorno {now.Day + 1}   {now.Hour:00}:{now.Minute:00}:{now.Second:00}   {DayName(_view.Daylight)}   ({HereName(_view.Light)})";
		var where = _view.Location is { } here ? _view.Locations.Single(l => l.Id == here).Name
			: onMap ? "strada" : "in viaggio";
		_status.Text = (_paused ? $"IN PAUSA — ordini in coda: {_queue.Count}. " : "")
			+ (_walking ? $"Cammini… ({where})" : _view.Travel is not null ? "Sei in viaggio."
				: onMap ? $"Sei qui: {where}. Clicca dove vuoi andare." : $"Sei qui: {where}.");
		_food.Text = $"Razioni con te: {_view.Food}";
		var eventsText = _view.RecentEvents.Count == 0 ? "Nessun evento recente."
			: string.Join("\n\n", _view.RecentEvents.Reverse().Select(e =>
				$"G{e.At.Day + 1} {e.At.Hour:00}:{e.At.Minute:00} · {e.Description}"));
		if (_events.Text != eventsText)
			_events.Text = eventsText;
		_torch.Text = _view.TorchLitUntil is { } burnsUntil
			? $"Spegni la torcia (T) — ancora {Math.Max(1, burnsUntil.Since(_view.Now).Seconds / 60)} min"
			: $"Accendi una torcia (T) — te ne restano {_view.Torches}";
		_torch.Disabled = _view.TorchLitUntil is null && _view.Torches == 0;
		foreach (var door in _view.Doors)
			if (_doorPanels.TryGetValue(door.At, out var panel))
				panel.Visible = !door.Open;
		var watchers = _view.VisibleActors.Where(a => a.SeesYou == true).Select(a => a.Name).ToList();
		var hidden = _view.Sneaking is { } total ? $"Di soppiatto (Furtività {total}). "
			: _view.Move is { Stealthy: true } ? "Vai piano, ma ti vedono: non sei nascosto. " : "";
		_watched.Text = hidden + (watchers.Count > 0 ? $"Ti vede: {string.Join(", ", watchers)}." : _view.VisibleActors.Count > 0 ? "Nessuno di quelli che vedi ti vede." : "");
		_stop.Disabled = !_walking;
		ShadeMap();
		if (!_walking || !onMap)
			_target.Visible = false;

		// Store controls where a store is in sight; the core decides whether it is within reach.
		_storeRow.Visible = onMap && _view.VisibleStores.Any(s => s.Id == MappedVillageScenario.Ids.GranaryStore);
		foreach (var store in _view.VisibleStores)
			if (_storeLabels.TryGetValue(store.Id, out var label))
				label.Text = $"{store.Name}: {store.Food}";

		RefreshPeople();
		PlaceTokens();
	}

	private void RefreshPeople()
	{
		// The conversation ends by itself when you walk away (or the other does).
		if (_talkingTo is { } talking && _view.PeopleHere.All(p => p.Id != talking))
			_talkingTo = null;
		var key = string.Join("|", _view.PeopleHere.Select(p => $"{p.Id}:{p.Topics.Count}:{p.Doing}")) + "#" + _view.UnseenNearby + "#" + _talkingTo;
		if (key == _peopleKey)
			return;
		_peopleKey = key;
		foreach (var child in _people.GetChildren())
			child.QueueFree();
		foreach (var person in _view.PeopleHere)
		{
			var doing = person.Doing is { } d ? $" ({d})" : "";
			if (person.Id != _talkingTo)
			{
				_people.AddChild(new Label { Text = $"Accanto a te: {person.Name}{doing}. Clicca sul personaggio per parlare." });
				continue;
			}
			var title = new Label { Text = $"Parli con {person.Name}{doing}" };
			title.AddThemeFontSizeOverride("font_size", 16);
			_people.AddChild(title);
			if (person.Topics.Count == 0)
				_people.AddChild(new Label { Text = "  Non hai niente di nuovo da raccontare." });
			foreach (var topic in person.Topics)
			{
				var id = person.Id;
				var tell = new Button { Text = $"Racconta: {topic.Summary}", AutowrapMode = TextServer.AutowrapMode.WordSmart };
				tell.Pressed += () => Tell(id, topic);
				_people.AddChild(tell);
			}
			var bye = new Button { Text = "Congedati" };
			bye.Pressed += CloseTalk;
			_people.AddChild(bye);
		}
		if (_view.UnseenNearby > 0)
			_people.AddChild(new Label { Text = "Senti qualcuno vicino, ma è troppo buio per capire chi." });
	}

	private void PlaceTokens()
	{
		var me = (_view.Id, _view.Position, _view.Move);
		var figures = new List<(ActorId Id, string Name, string? Doing, bool Player, GridPos? Position, MoveView? Move)>
		{
			(me.Id, "Tu", null, true, me.Position, me.Move),
		};
		figures.AddRange(_view.VisibleActors.Select(a => (a.Id, a.SeesYou == true ? $"{a.Name} · ti vede" : a.Name,
			a.Doing, false, a.Position, a.Move)));

		var shown = new HashSet<ActorId>();
		foreach (var figure in figures)
		{
			if (figure.Position is null)
				continue;
			if (!_tokens.TryGetValue(figure.Id, out var token))
			{
				token = MakeToken(figure.Name, figure.Player);
				_tokens[figure.Id] = token;
				_sorted.AddChild(token);
			}
			token.Position = SmoothPosition(figure.Position.Value, figure.Move);
			token.Modulate = Shade(ScreenToCell(token.Position));
			if (figure.Player && _view.Sneaking is not null)
				token.Modulate = token.Modulate with { A = 0.55f }; // sneaking
			else if (figure.Move is { Stealthy: true })
				token.Modulate = token.Modulate with { A = 0.68f }; // the visible slow, cautious gait
			if (token.GetNodeOrNull<Label>("Name") is { } nameLabel)
				nameLabel.Text = figure.Doing is { Length: > 0 } doing ? $"{figure.Name}\n{doing}" : figure.Name;
			token.GetNode<Polygon2D>("Flame").Visible = figure.Player
				? _view.TorchLitUntil is not null
				: _view.VisibleActors.Any(a => a.Id == figure.Id && a.Torch);
			shown.Add(figure.Id);
		}
		foreach (var (id, token) in _tokens)
			token.Visible = shown.Contains(id);
		if (_tokens.TryGetValue(_view.Id, out var mine))
			_camera.Position = _camera.Position.Lerp(mine.Position, 0.15f);
	}

	/// <summary>
	/// Presentation only: between two squares of the core's path, slide by the fraction of a second already played.
	/// The square itself always comes from the core.
	/// </summary>
	private Vector2 SmoothPosition(GridPos square, MoveView? move)
	{
		if (move is null)
			return CellToScreen(square);
		var elapsed = _view.Now.Since(move.DepartedAt).Seconds + _pending;
		// The Sim reaches step i at ceil(i*30/Speed) whole game seconds. Interpolate between those
		// actual deadlines, including speeds where a square takes more than one second.
		double AtStep(int step) => Math.Ceiling(step * 30.0 / move.Speed);
		var reached = 0;
		while (reached < move.Path.Count && AtStep(reached + 1) <= elapsed)
			reached++;
		if (reached >= move.Path.Count)
			return CellToScreen(move.Path[^1]);
		var from = reached == 0 ? move.From : move.Path[reached - 1];
		var to = move.Path[reached];
		var start = AtStep(reached);
		var end = AtStep(reached + 1);
		return CellToScreen(from).Lerp(CellToScreen(to), (float)((elapsed - start) / (end - start)));
	}

	/// <summary>
	/// Presentation of the core's light levels (<see cref="PlayerView.MapLight"/>): each square is shaded by its own
	/// level, softened towards its neighbours so lamplight fades out instead of stopping at a hard edge.
	/// </summary>
	private void ShadeMap()
	{
		if (_view.MapLight is not { } rows)
			return;
		var key = string.Concat(rows) + "|" + string.Join(",", _view.Doors.Select(d => $"{d.At}:{d.Open}"));
		if (key == _lightKey)
			return;
		_lightKey = key;
		int h = rows.Count, w = rows[0].Length;
		var map = _view.Map!;
		var closedDoors = _view.Doors.Where(d => !d.Open).Select(d => d.At).ToHashSet();
		bool Blocked(int x, int y) => map.Rows[y][x] == '#' || closedDoors.Contains(new GridPos(x, y));
		bool Connected(int x, int y, int nx, int ny) =>
			!Blocked(x, y) && !Blocked(nx, ny) &&
			(x == nx || y == ny || !Blocked(nx, y) || !Blocked(x, ny));
		float Level(int x, int y) => rows[y][x] switch { '2' => 1f, '1' => 0.55f, _ => 0f };
		_glow = new float[w, h];
		for (var y = 0; y < h; y++)
		for (var x = 0; x < w; x++)
		{
			float sum = 0;
			var count = 0;
			for (var dy = -1; dy <= 1; dy++)
			for (var dx = -1; dx <= 1; dx++)
				if (x + dx >= 0 && y + dy >= 0 && x + dx < w && y + dy < h && (dx != 0 || dy != 0)
					&& Connected(x, y, x + dx, y + dy))
				{
					sum += Level(x + dx, y + dy);
					count++;
				}
			_glow[x, y] = count == 0 ? Level(x, y) : 0.6f * Level(x, y) + 0.4f * sum / count;
		}
		foreach (var (cell, item) in _lit)
			item.Modulate = Shade(cell);
		foreach (var (cell, panel) in _doorPanels)
			panel.Modulate = Shade(cell);
	}

	private static readonly Color Night = new(0.22f, 0.25f, 0.42f);

	private Color Shade(GridPos cell) =>
		cell.X >= 0 && cell.Y >= 0 && cell.X < _glow.GetLength(0) && cell.Y < _glow.GetLength(1)
			? Night.Lerp(Colors.White, _glow[cell.X, cell.Y])
			: Colors.White;

	/// <summary>Visual glow, exposed to the village smoke to check that walls do not leak light.</summary>
	internal float DisplayGlowAt(GridPos cell) => _glow[cell.X, cell.Y];

	private static string DayName(Light daylight) => daylight switch
	{
		Light.Bright => "giorno",
		Light.Dim => "crepuscolo",
		_ => "notte",
	};

	private static string HereName(Light light) => light switch
	{
		Light.Bright => "qui luce piena",
		Light.Dim => "qui luce fioca",
		_ => "qui buio",
	};

	// ---------------------------------------------------------------- iso grid

	private static Vector2 CellToScreen(GridPos c) => new((c.X - c.Y) * TileW / 2f, (c.X + c.Y) * TileH / 2f);

	/// <summary>World position of a square (used by the smoke runner to click on it).</summary>
	internal static Vector2 ScreenPointOf(GridPos c) => CellToScreen(c);

	private static GridPos ScreenToCell(Vector2 p)
	{
		var a = p.X / (TileW / 2f);
		var b = p.Y / (TileH / 2f);
		return new GridPos((int)Mathf.Round((a + b) / 2f), (int)Mathf.Round((b - a) / 2f));
	}

	private static Vector2[] Diamond(float scale = 1f) => new[]
	{
		new Vector2(0, -TileH / 2f * scale), new Vector2(TileW / 2f * scale, 0),
		new Vector2(0, TileH / 2f * scale), new Vector2(-TileW / 2f * scale, 0),
	};

	private void UpdateHover()
	{
		var mouse = GetGlobalMousePosition();
		var person = PersonAt(mouse);
		Input.SetDefaultCursorShape(person is null ? Input.CursorShape.Arrow : Input.CursorShape.PointingHand);
		var cell = person is { } id && _tokens.TryGetValue(id, out var token) ? ScreenToCell(token.Position) : ScreenToCell(mouse);
		var map = _view.Position is null ? null : _view.Map;
		_hover.Visible = map is not null && cell.X >= 0 && cell.Y >= 0 && cell.X < map.Width && cell.Y < map.Height
						 && map.Rows[cell.Y][cell.X] != '#';
		_hover.Position = CellToScreen(cell);
	}

	/// <summary>The visible person (not you) whose figure is under a world point, the nearest in front if several.</summary>
	private ActorId? PersonAt(Vector2 point) =>
		_view.VisibleActors
			.Where(a => _tokens.TryGetValue(a.Id, out var t) && t.Visible
						&& new Rect2(t.Position + new Vector2(-12, -38), new Vector2(24, 44)).HasPoint(point))
			.OrderByDescending(a => _tokens[a.Id].Position.Y)
			.Select(a => (ActorId?)a.Id)
			.FirstOrDefault();

	// ---------------------------------------------------------------- construction

	private void BuildMap()
	{
		var map = _view.Map!;
		_floor = new Node2D { ZIndex = -10 };
		AddChild(_floor);
		_sorted = new Node2D { YSortEnabled = true };
		AddChild(_sorted);

		for (var y = 0; y < map.Height; y++)
		for (var x = 0; x < map.Width; x++)
		{
			var c = map.Rows[y][x];
			var cell = new GridPos(x, y);
			var color = c switch
			{
				'#' => Wall.Darkened(0.2f),
				'.' => Road,
				'S' => Yard,
				_ => map.Zones.TryGetValue(c, out var zone) ? zone == MappedVillageScenario.Ids.Inn ? InnFloor
					: zone == MappedVillageScenario.Ids.FarmerHouse ? HouseFloor
					: zone == MappedVillageScenario.Ids.ForestRoad ? ForestRoad : Yard : Yard,
			};
			var shade = (x + y) % 2 == 0 ? 0f : 0.05f;
			var tile = new Polygon2D { Polygon = Diamond(), Position = CellToScreen(cell), Color = color.Darkened(shade) };
			_floor.AddChild(tile);
			_lit[cell] = tile;
			if (c == '#')
			{
				var wall = MakeBlock(cell, Wall, WallHeight);
				_sorted.AddChild(wall);
				_lit[cell] = wall; // the floor under a wall is hidden anyway
			}
		}

		foreach (var store in _sim.GetWorldView().Stores.Where(s => s.Position is not null
			&& _view.Locations.Any(l => l.Id == s.Location && l.Area == map.Area)))
		{
			var block = MakeBlock(store.Position!.Value, new Color(0.65f, 0.45f, 0.30f), 34f);
			// Keep the west access clear for the raider/guard labels during a raid.
			var label = new Label { Text = store.Name, Position = new Vector2(24, -70) };
			label.AddThemeFontSizeOverride("font_size", 12);
			label.AddThemeConstantOverride("outline_size", 4);
			label.AddThemeColorOverride("font_outline_color", Colors.Black);
			block.AddChild(label);
			_storeLabels[store.Id] = label;
			_sorted.AddChild(block);
			_lit[store.Position.Value] = block;
		}

		// Doors: a wooden floor square, and a wooden panel as tall as the walls while closed.
		foreach (var door in _view.Doors)
		{
			if (_lit.TryGetValue(door.At, out var tile) && tile is Polygon2D floorTile)
				floorTile.Color = DoorWood.Darkened(0.25f);
			var panel = MakeBlock(door.At, DoorWood, WallHeight);
			_sorted.AddChild(panel);
			_doorPanels[door.At] = panel;
		}

		foreach (var exit in map.Exits)
		{
			var marker = new Node2D { Position = CellToScreen(exit.At) };
			marker.AddChild(new Polygon2D { Polygon = Diamond(0.65f), Color = new Color(0.32f, 0.83f, 0.59f, 0.75f) });
			var sign = new Label { Text = exit.Name, Position = new Vector2(-72, -42), Size = new Vector2(144, 0),
				HorizontalAlignment = HorizontalAlignment.Center };
			sign.AddThemeFontSizeOverride("font_size", 11);
			sign.AddThemeConstantOverride("outline_size", 4);
			sign.AddThemeColorOverride("font_outline_color", Colors.Black);
			marker.AddChild(sign);
			_sorted.AddChild(marker);
		}

		_hover = new Polygon2D { Polygon = Diamond(), Color = Hover, ZIndex = -5 };
		AddChild(_hover);
		_target = new Polygon2D { Polygon = Diamond(0.6f), Color = new Color(0.3f, 0.6f, 1f, 0.6f), ZIndex = -5, Visible = false };
		AddChild(_target);

		_camera = new Camera2D { Position = CellToScreen(_view.Position ?? new GridPos(0, 0)), Zoom = new Vector2(1.2f, 1.2f) };
		AddChild(_camera);
		_camera.MakeCurrent();
	}

	/// <summary>A raised isometric block on a square (walls, the store), with its origin at the base for Y-sorting.</summary>
	private static Node2D MakeBlock(GridPos cell, Color color, float height)
	{
		var node = new Node2D { Position = CellToScreen(cell) };
		var d = Diamond(0.98f);
		var up = new Vector2(0, -height);
		node.AddChild(new Polygon2D { Polygon = new[] { d[3], d[2], d[2] + up, d[3] + up }, Color = color.Darkened(0.3f) });
		node.AddChild(new Polygon2D { Polygon = new[] { d[2], d[1], d[1] + up, d[2] + up }, Color = color.Darkened(0.12f) });
		node.AddChild(new Polygon2D { Polygon = new[] { d[0] + up, d[1] + up, d[2] + up, d[3] + up }, Color = color.Lightened(0.1f) });
		return node;
	}

	private static Node2D MakeToken(string name, bool player)
	{
		var token = new Node2D();
		var color = player ? new Color(0.25f, 0.45f, 0.85f) : new Color(0.35f, 0.65f, 0.35f);
		token.AddChild(new Polygon2D { Polygon = Diamond(0.4f), Color = new Color(0, 0, 0, 0.35f) });
		token.AddChild(new Polygon2D
		{
			Polygon = new[] { new Vector2(-6, 0), new Vector2(6, 0), new Vector2(5, -24), new Vector2(-5, -24) },
			Color = color,
		});
		token.AddChild(new Polygon2D
		{
			Polygon = Enumerable.Range(0, 10).Select(i => Vector2.FromAngle(Mathf.Tau * i / 10) * 5.5f).ToArray(),
			Position = new Vector2(0, -30),
			Color = new Color(0.95f, 0.82f, 0.68f),
		});
		// A torch in the hand, shown while it burns.
		token.AddChild(new Polygon2D
		{
			Name = "Flame",
			Polygon = new[] { new Vector2(0, -12), new Vector2(4, -4), new Vector2(0, 0), new Vector2(-4, -4) },
			Position = new Vector2(10, -18),
			Color = new Color(1f, 0.65f, 0.15f),
			Visible = false,
		});
		if (!player)
		{
			var label = new Label { Name = "Name", Text = name, Position = new Vector2(-70, -58), Size = new Vector2(140, 0), HorizontalAlignment = HorizontalAlignment.Center };
			label.AddThemeFontSizeOverride("font_size", 10);
			label.AddThemeConstantOverride("outline_size", 3);
			label.AddThemeColorOverride("font_outline_color", Colors.Black);
			token.AddChild(label);
		}
		return token;
	}

	private void Pan(double delta)
	{
		var dir = Vector2.Zero;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
		if (dir != Vector2.Zero)
			_camera.Position += dir.Normalized() * (float)(500 * delta) / _camera.Zoom.X;
	}

	private void BuildUi()
	{
		var ui = new CanvasLayer();
		AddChild(ui);
		var panel = new PanelContainer { OffsetLeft = 16, OffsetRight = 376, OffsetTop = 16 };
		ui.AddChild(panel);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 8);
		panel.AddChild(box);

		_clock = new Label();
		_clock.AddThemeFontSizeOverride("font_size", 20);
		box.AddChild(_clock);
		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_status);
		_food = new Label();
		box.AddChild(_food);

		_pause = new Button { Text = "Pausa (Spazio)", ToggleMode = true };
		_pause.Toggled += SetPaused;
		box.AddChild(_pause);
		_stop = new Button { Text = "Fermati (X)" };
		_stop.Pressed += Stop;
		box.AddChild(_stop);
		box.AddChild(new Label { Text = "Maiusc+clic su una porta: cammina sulla sua casella." });
		var paceRow = new HBoxContainer();
		paceRow.AddThemeConstantOverride("separation", 6);
		paceRow.AddChild(new Label { Text = "Ritmo:", TooltipText = "Quanto scorre veloce il tempo mentre cammini. Le regole non cambiano: cambia solo quanto aspetti." });
		var paceGroup = new ButtonGroup();
		foreach (var pace in new[] { 1.0, 3.0, 6.0 })
		{
			var button = new Button { Text = $"{pace}×", ToggleMode = true, ButtonGroup = paceGroup, ButtonPressed = pace == _pace, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			button.Pressed += () => SetPace(pace);
			paceRow.AddChild(button);
			_paceButtons[pace] = button;
		}
		box.AddChild(paceRow);
		_sneakToggle = new CheckButton { Text = "Muoviti di soppiatto (F)", TooltipText = "Andatura lenta: preferisci il buio. Parlare o fermarti a lavorare ti fa tornare visibile." };
		_sneakToggle.Toggled += SetSneak;
		box.AddChild(_sneakToggle);
		_watched = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_watched);
		_torch = new Button();
		_torch.Pressed += ToggleTorch;
		box.AddChild(_torch);

		_storeRow = new HBoxContainer();
		_storeRow.AddThemeConstantOverride("separation", 6);
		_amount = new SpinBox { MinValue = 1, MaxValue = 99, Value = 1, Step = 1, CustomMinimumSize = new Vector2(80, 0) };
		_storeRow.AddChild(_amount);
		var deposit = new Button { Text = "Consegna" };
		deposit.Pressed += () => Deposit((int)_amount.Value);
		_storeRow.AddChild(deposit);
		var take = new Button { Text = "Prendi" };
		take.Pressed += () => Take((int)_amount.Value);
		_storeRow.AddChild(take);
		box.AddChild(_storeRow);

		_people = new VBoxContainer();
		box.AddChild(_people);

		var saveRow = new HBoxContainer();
		var save = new Button { Text = "Salva", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		save.Pressed += SaveGame;
		saveRow.AddChild(save);
		var load = new Button { Text = "Carica", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		load.Pressed += LoadGame;
		saveRow.AddChild(load);
		box.AddChild(saveRow);

		_message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_message.AddThemeFontSizeOverride("font_size", 13);
		box.AddChild(_message);

		var eventsPanel = new PanelContainer();
		var eventsBox = new VBoxContainer();
		eventsPanel.AddChild(eventsBox);
		var eventsTitle = new Label { Text = "Eventi" };
		eventsTitle.AddThemeFontSizeOverride("font_size", 16);
		eventsBox.AddChild(eventsTitle);
		var eventScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 112) };
		_events = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_events.AddThemeFontSizeOverride("font_size", 12);
		eventScroll.AddChild(_events);
		eventsBox.AddChild(eventScroll);
		box.AddChild(eventsPanel);

		_offMapPanel = new PanelContainer { OffsetLeft = 440, OffsetRight = 840, OffsetTop = 100, OffsetBottom = 245,
			Visible = false };
		ui.AddChild(_offMapPanel);
		var journeyBox = new VBoxContainer();
		journeyBox.AddThemeConstantOverride("separation", 12);
		_offMapPanel.AddChild(journeyBox);
		_offMapLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_offMapLabel.AddThemeFontSizeOverride("font_size", 21);
		journeyBox.AddChild(_offMapLabel);
		_returnToVillage = new Button { Text = "Torna al villaggio", Visible = false };
		_returnToVillage.Pressed += ReturnToVillage;
		journeyBox.AddChild(_returnToVillage);
	}
}
