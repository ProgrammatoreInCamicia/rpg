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
	[Export] public bool InteriorPrototype { get; set; }
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
	private GridPos? _smokeFocus;
	private Polygon2D _hover = null!;
	private readonly Dictionary<GridPos, CanvasItem> _lit = new(); // floor, walls and stores, shaded by their square's light
	private readonly List<(GridPos Cell, CanvasItem Item)> _edgeLit = new();
	private readonly Dictionary<(GridPos A, GridPos B), Node2D> _windowPanels = new();
	private float[,] _glow = new float[0, 0];
	private string _lightKey = "";
	private readonly Dictionary<ActorId, Node2D> _tokens = new();
	private readonly Dictionary<StoreId, Label> _storeLabels = new();

	private Label _clock = null!;
	private Label _status = null!;
	private Label _food = null!;
	private Label _levelExperience = null!;
	private Button _stop = null!;
	private Button _pause = null!;
	private CheckButton _sneakToggle = null!;
	private Label _watched = null!;
	private Button _torch = null!;
	private readonly Dictionary<GridPos, Node2D> _doorPanels = new(); // shown while the door is closed
	private readonly Dictionary<GridPos, Polygon2D> _doorFloors = new();
	private readonly Dictionary<GridPos, Line2D> _doorOutlines = new();
	private bool _sneak;
	private HBoxContainer _storeRow = null!;
	private SpinBox _amount = null!;
	private VBoxContainer _people = null!;
	private ScrollContainer _peopleScroll = null!;
	private Label _message = null!;
	private Label _events = null!;
	private PanelContainer _eventsPanel = null!;
	private PanelContainer _journalPanel = null!;
	private RichTextLabel _journalText = null!;
	private Button _journalButton = null!;
	private bool _journalOpen;
	private PanelContainer _offMapPanel = null!;
	private Label _offMapLabel = null!;
	private Button _returnToVillage = null!;

	private ActionId? _running;
	private ActorId? _approaching; // walking up to someone to talk to them
	private (ActorId Target, string Attack, bool NonLethal)? _attackAfterWalking;
	private LocationId? _exitAfterWalking;
	private ActorId? _talkingTo;
	private bool _walking;
	private bool _freeMovementEnabled;
	private double _speed = 1;

	/// <summary>
	/// Exploration pace: game seconds played per real second while walking (Project Zomboid style). The rules are
	/// unchanged (one square takes one game second at Speed 30); only how long the player waits on screen changes.
	/// </summary>
	private double _pace = 3;
	private bool _paused;
	private readonly List<(string Label, bool Walk, Action Run)> _queue = new(); // orders given while paused
	private readonly Dictionary<double, Button> _paceButtons = new();
	private double _pendingTicks;
	private string _peopleKey = "";

	public override void _Ready()
	{
		var interior = InteriorPrototype || OS.GetCmdlineUserArgs().Contains("--interior-prototype");
		_freeMovementEnabled = true;
		_sim = SimulationSession.Create(interior ? InteriorPrototypeScenario.Create() : ScenarioFile.Load("living-village"));
		if (interior)
			_sim.Advance(Duration.FromHours(13)); // the playable scene opens in daylight; the smoke also checks night
		_sim.FacePlayer(1, 1); // starts looking into the village, towards the guard and the road
		_view = _sim.GetPlayerView();
		SyncFigureFacing(_view.Facing);

		BuildMap();
		BuildUi();
		Refresh();

		var smoke = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--village-smoke="));
		if (smoke is not null)
			AddChild(new VillageSmoke(this, smoke["--village-smoke=".Length..]));
		var interiorSmoke = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--interior-smoke="));
		if (interiorSmoke is not null)
			AddChild(new InteriorSmoke(this, interiorSmoke["--interior-smoke=".Length..]));
	}

	public override void _Process(double delta)
	{
		Pan(delta);
		UpdateHover();
		UpdateSpotPreview();
		if (_view.Combat?.NextRoundAt is { } nextRound && !_view.Dead)
		{
			// NPC turns are already resolved by EndTurn. This closes the six-second round and starts the next
			// player turn; Advance deliberately stops at that turn instead of consuming any later world time.
			_sim.Advance(Duration.FromTicks(Math.Max(0, nextRound.Since(_view.Now).Ticks)));
			Refresh();
		}

		// The world goes on whether the player moves or not (real time with pause, as in BG1/BG2): the exploration pace
		// while walking or standing, a fast-forward during fixed-length activities. Only the pause stops the clock.
		if (!_paused && _view.Combat is null && !_view.Dead && _view.Unconscious)
		{
			// No one is there to tend the player: the hours it lies senseless pass at once, the world going on meanwhile.
			_sim.AdvanceWhileUnconscious(Duration.FromHours(24));
			_running = null;
			_walking = false;
			_approaching = null;
			_exitAfterWalking = null;
			_attackAfterWalking = null;
			_pendingTicks = 0;
			Refresh();
		}
		else if (!_paused && _view.Combat is null && !_view.Dead)
		{
			_pendingTicks += delta * (_running is not null && !_walking ? _speed : _pace) * GameTime.TicksPerSecond;
			var ticks = (long)_pendingTicks;
			if (ticks > 0)
			{
				_pendingTicks -= ticks;
				ActorId? arrived = null;
				LocationId? exitReached = null;
				if (_running is { } action)
				{
					var result = _sim.AdvanceUntilCompleted(action, Duration.FromTicks(ticks));
					if (result.Outcome != AdvanceOutcome.TimeLimitReached)
					{
						_running = null;
						_walking = false;
						_pendingTicks = 0;
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
					_sim.Advance(Duration.FromTicks(ticks));
				Refresh();
				if (arrived is { } who)
					OpenTalk(who);
				if (_attackAfterWalking is { } pending && !_walking)
				{
					_attackAfterWalking = null;
					FinishApproachAttack(pending);
				}
				if (exitReached is { } exit)
					BeginExitJourney(exit);
			}
		}
		UpdateShotVisuals();
		PlaceTokens();
		UpdateBuildingOcclusion((float)delta);
		UpdateCombatHover();
		UpdateTargetingHover();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.I })
		{
			_inventoryPanel.Visible = !_inventoryPanel.Visible;
			return;
		}
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
		{
			if (_attackArmed) { _attackArmed = false; RefreshAttackBars(); }
			else CloseHudOverlay();
			return;
		}
		if (_view.Combat is not null && HandleCombatInput(@event))
			return;
		if (_view.Dead)
			return;
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
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.P })
		{
			TogglePaladin3dPrototype();
			return;
		}
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.O })
		{
			TogglePaladinLive3dPrototype();
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
		if (mouse.ButtonIndex == MouseButton.Right)
		{
			var point = GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position;
			if (PersonAt(point) is { } target)
				OpenPersonMenu(target);
			else if (DoorAt(point) is { } door)
				OpenDoorMenu(door);
			else if (WindowAt(point) is { } window)
				OpenWindowMenu(window);
			else
				FaceToward(point);
			return;
		}
		switch (mouse.ButtonIndex)
		{
			case MouseButton.Left:
				var point = GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position;
				// A figure stands above its square: clicking on its body means that person, not the square behind.
				if (PersonAt(point) is { } person)
				{
					if (!UseArmedAttack(person)) TalkTo(person);
				}
				else if (_attackArmed)
					ShowMessage("Scegli un bersaglio per l'attacco. Esc annulla.");
				else if (_view.Map.Exits.FirstOrDefault(e => e.At == ScreenToCell(point)) is { } exit)
					GoToExit(exit);
				else if (DoorAt(point) is { } door)
				{
					if (mouse.ShiftPressed)
						MoveTo(door); // walk onto an adjacent door instead of operating it
					else
						ClickDoor(door);
				}
				else if (WindowAt(point) is { } window)
					ClickWindow(window);
				else if (GroundItemAt(ScreenToCell(point)) is { } item)
					PickUp(item);
				else if (_freeMovementEnabled)
					MoveToSpot(ScreenToSpot(point));
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
	internal void AdvanceForSmoke(GameTime to) { _sim.Advance(to.Since(_view.Now)); Refresh(); }
	internal void TorchForSmoke() { _sim.Execute(new TorchCommand { Actor = _sim.Player, Lit = true }); Refresh(); }
	internal WorldView SmokeWorld => _sim.GetWorldView();
	internal bool IsBusy => _running is not null;
	internal bool IsWalking => _walking;
	internal string LastMessage => _message.Text;
	internal string EventText => _events.Text;
	internal string LevelExperienceTextForSmoke => _levelExperience.Text;
	internal ActorId? TalkingTo => _talkingTo;
	internal bool OffMapPanelVisible => _offMapPanel.Visible;

	/// <summary>World position of a visible figure's body (used by the smoke runner to click on it).</summary>
	internal Vector2? BodyPointOf(ActorId id) =>
		_tokens.TryGetValue(id, out var token) && token.Visible ? token.Position + new Vector2(0, -18) : null;
	internal Vector2? FootPointOf(ActorId id) =>
		_tokens.TryGetValue(id, out var token) && token.Visible ? token.Position : null;

	/// <summary>Walk to a square. A walk in progress is replaced (the core stops it on the square reached).</summary>
	public void MoveTo(GridPos cell) => Order($"vai in {cell}", walk: true, () =>
	{
		if (_view.Combat is not null)
		{
			ExecuteCombatMove(cell);
			return;
		}
		if (Walk(new MoveCommand { Actor = _sim.Player, To = cell, Stealthy = _sneak }))
		{
			_attackAfterWalking = null;
			_exitAfterWalking = null;
			_approaching = null;
		}
	});

	/// <summary>Walk to the exact floor point in the isolated interior prototype.</summary>
	internal void MoveToSpot(Spot spot) => Order($"vai a {spot}", walk: true, () =>
	{
		if (Walk(new MoveToSpotCommand { Actor = _sim.Player, To = spot, Stealthy = _sneak }))
		{
			_attackAfterWalking = null;
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
		if (_view.Position is { } here && (here == door && _view.Map?.Edges.Any(e => e.DoorAt == door) == true
			|| here != door && Math.Max(Math.Abs(here.X - door.X), Math.Abs(here.Y - door.Y)) == 1))
		{
			var knowledge = _view.Doors.FirstOrDefault(d => d.At == door);
			if (knowledge is not { Known: DoorKnowledge.Seen, Open: { } open })
			{
				MoveTo(door); // walking to the door follows the usual movement rules; do not toggle stale knowledge
				return;
			}
			var result = _sim.Execute(new DoorCommand { Actor = _sim.Player, At = door, Open = !open });
			ShowMessage(result.Message);
			Refresh();
			return;
		}
		MoveTo(door);
	});

	internal void ClickWindow(MapEdgeView window) => Order("usa la finestra", walk: false, () =>
	{
		if (_view.Map?.Edges.FirstOrDefault(e => e.Kind == MapEdgeKind.Window
			&& e.A == window.A && e.B == window.B) is not { Open: { } open } current)
			return;
		var result = _sim.Execute(new WindowCommand { Actor = _sim.Player,
			A = current.A, B = current.B, Open = !open });
		ShowMessage(result.Message);
		Refresh();
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
			if (door.Open == false && _doorPanels.TryGetValue(door.At, out var panel)
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
		if (!_paused || _view.Combat is not null)
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
				var resumed = move.Spots is { } spots
					? Walk(new MoveToSpotCommand { Actor = _sim.Player, To = spots[^1], Stealthy = on })
					: Walk(new MoveCommand { Actor = _sim.Player, To = move.Path[^1], Stealthy = on });
				if (resumed)
					_approaching = approaching;
			});
	}

	private bool Walk(Command command)
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
		if (!_walking && _view.Action?.Kind != "Rest")
			return;
		var result = _sim.Execute(new StopCommand { Actor = _sim.Player });
		if (!result.Success)
		{
			ShowMessage(result.Message);
			Refresh();
			return;
		}
		_running = null;
		_walking = false;
		_approaching = null;
		_attackAfterWalking = null;
		_exitAfterWalking = null;
		_pendingTicks = 0;
		ShowMessage(result.Message);
		Refresh();
	}

	/// <summary>A fixed-length activity: plays out quickly, at most a few real seconds.</summary>
	private void StartActivity(Command command, string label = "attività") => Order(label, walk: false, () =>
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
		ResetHudLog();
		_attackArmed = false;
		_observedRestStarted = null;
		_restDiceInitialized = false;
		if (_sim.GetPlayerView().Facing is null)
			_sim.FacePlayer(1, 1); // saves made before directional vision
		SyncFigureFacing(_sim.GetPlayerView().Facing);
		_peopleKey = "";
		_lightKey = "";
		_journalOpen = false;
		_approaching = null;
		_attackAfterWalking = null;
		_exitAfterWalking = null;
		_talkingTo = null;
		_combatVisualTracks.Clear();
		ClearShotVisuals();
		_queue.Clear();
		_sneak = _sim.GetPlayerView().Sneaking is not null;
		_sneakToggle.SetPressedNoSignal(_sneak);
		// Resume a walk or activity that was in progress when saving.
		var view = _sim.GetPlayerView();
		_seenCombatVisuals = view.Combat?.Log.Count ?? view.LastFight?.Count ?? 0;
		_observingLastFight = view.Combat is null && view.LastFight is not null;
		_running = view.Action?.Id;
		_walking = view.Move is not null;
		_speed = Math.Max(1, (view.Action?.CompletesAt.Since(view.Action.StartedAt).Seconds ?? 1) / MaxActivityRealSeconds);
		_pendingTicks = 0;
		Refresh();
		ShowMessage("Partita caricata.");
	}

	private void ShowMessage(string text)
	{
		_message.Text = text;
		if (_hudLog is not null && _view is not null) RefreshHud();
	}

	// ---------------------------------------------------------------- view

	private void Refresh()
	{
		ClearBarrierActivity();
		_view = _sim.GetPlayerView();
		_spotPreviewAt = null; // doors, light and newly explored squares can change a route within one second
		if (_view.Combat is null && _view.LastFight is null)
			_combatVisualTracks.Clear();
		if (_view.Combat is not null)
		{
			_journalOpen = false;
			_talkingTo = null;
			_creaturePanel.Visible = false;
			_personMenu.Visible = false;
			_running = null;
			_walking = false;
			_approaching = null;
			_attackAfterWalking = null;
			_exitAfterWalking = null;
			_queue.Clear();
		}
		var onMap = _view.Map is not null && _view.Position is not null;
		_floor.Visible = onMap;
		_sorted.Visible = onMap;
		_roofs.Visible = onMap;
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
		_status.Text = _view.Dead ? "Sei morto. Carica un salvataggio per riprovare."
			: _view.Combat is { } combat ? $"Scontro · round {combat.Round}. " + (combat.IsYourTurn ? "È il tuo turno." : "Agiscono gli altri.")
			: _view.Action is { Kind: "Rest" } rest ? (_paused ? "IN PAUSA · " : "")
				+ $"{rest.Description} fino alle {rest.CompletesAt.Hour:00}:{rest.CompletesAt.Minute:00}."
			: (_paused ? $"IN PAUSA — ordini in coda: {_queue.Count}. " : "")
			+ (_walking ? $"Cammini… ({where})" : _view.Travel is not null ? "Sei in viaggio."
				: onMap ? $"Sei qui: {where}. Clicca dove vuoi andare." : $"Sei qui: {where}.");
		_food.Text = $"Razioni con te: {_view.Food}";
		_levelExperience.Visible = _view.Level is not null;
		if (_view.Level is { } level)
			_levelExperience.Text = _view.NextLevelAt is { } next
				? $"Livello {level} · Esperienza {_view.Experience} / {next}"
				: $"Livello {level} · Esperienza {_view.Experience} (massimo)";
		var eventsText = _view.RecentEvents.Count == 0 ? "Nessun evento recente."
			: string.Join("\n\n", _view.RecentEvents.Reverse().Select(e =>
				$"G{e.At.Day + 1} {e.At.Hour:00}:{e.At.Minute:00} · {e.Description}"));
		if (_events.Text != eventsText)
			_events.Text = eventsText;
		_torch.Text = _view.TorchLitUntil is not null ? "Spegni torcia (T)" : "Torcia (T)";
		_torch.TooltipText = _view.TorchLitUntil is { } burnsUntil
			? $"Ancora {Math.Max(1, burnsUntil.Since(_view.Now).Seconds / 60)} minuti."
			: $"Torcie rimaste: {_view.Torches}.";
		_torch.Disabled = _view.Combat is not null || _view.Dead || _view.TorchLitUntil is null && _view.Torches == 0;
		_pause.Disabled = _view.Combat is not null || _view.Dead;
		_sneakToggle.Disabled = _view.Combat is not null || _view.Dead;
		foreach (var door in _view.Doors)
		{
			if (!_doorPanels.TryGetValue(door.At, out var panel))
				continue;
			panel.Visible = door.Known != DoorKnowledge.Unknown && door.Open == false;
			if (_doorFloors.TryGetValue(door.At, out var floor) && _doorOutlines.TryGetValue(door.At, out var outline))
			{
				floor.Color = door.Known == DoorKnowledge.Unknown ? Road
					: door.Known == DoorKnowledge.Remembered ? DoorWood.Lerp(Colors.Gray, 0.5f).Darkened(0.25f)
					: DoorWood.Darkened(0.25f);
				outline.Visible = door.Known != DoorKnowledge.Seen;
				outline.DefaultColor = door.Known == DoorKnowledge.Unknown
					? new Color(0.78f, 0.78f, 0.74f, 0.8f) : new Color(0.72f, 0.58f, 0.40f, 0.7f);
			}
		}
		if (_view.Map is { } mapWithWindows)
			foreach (var window in mapWithWindows.Edges.Where(e => e.Kind == MapEdgeKind.Window && e.Open is not null))
				if (_windowPanels.TryGetValue((window.A, window.B), out var panel))
					SetWindowPanel(panel, window);
		RefreshBarrierMarks();
		var watchers = _view.VisibleActors.Where(a => a.SeesYou == true).Select(a => a.Name).ToList();
		var hidden = _view.Sneaking is { } total ? $"Di soppiatto (Furtività {total}). "
			: _view.Move is { Stealthy: true } ? "Vai piano, ma ti vedono: non sei nascosto. " : "";
		_watched.Text = hidden + (watchers.Count > 0 ? $"Ti vede: {string.Join(", ", watchers)}." : _view.VisibleActors.Count > 0 ? "Nessuno di quelli che vedi ti vede." : "");
		_stop.Disabled = !_walking && _view.Action?.Kind != "Rest";
		_stop.Text = _view.Action?.Kind == "Rest" ? "Interrompi riposo (X)" : "Fermati (X)";
		RefreshRestUi();
		RefreshConditionsUi();
		RefreshClassUi();
		ShadeMap();
		RefreshBuildingVisibility();
		RefreshVisionFeather();
		foreach (var door in _view.Doors)
		{
			var panel = _doorPanels[door.At];
			var lit = Shade(_view.Map?.Edges.FirstOrDefault(e => e.DoorAt == door.At) is { } edge
				? FacadeShadeCell(edge) : door.At);
			panel.Modulate = door.Known == DoorKnowledge.Remembered
				? new Color(lit.R * 0.72f, lit.G * 0.72f, lit.B * 0.72f, 0.42f) : lit;
		}
		RefreshBarrierActivity();
		// Store controls where a store is in sight; the core decides whether it is within reach.
		_storeRow.Visible = onMap && _view.Combat is null && !_view.Dead
			&& _view.VisibleStores.Any(s => s.Id == MappedVillageScenario.Ids.GranaryStore);
		foreach (var store in _view.VisibleStores)
			if (_storeLabels.TryGetValue(store.Id, out var label))
				label.Text = $"{store.Name}: {store.Food}";

		RefreshPeople();
		_people.Visible = _view.Combat is null && !_view.Dead;
		_peopleScroll.Visible = _people.Visible && _view.PeopleHere.Count > 0;
		RefreshJournal();
		RefreshCreatureCard();
		RefreshCombatUi();
		RefreshAttackBars();
		RefreshHud();
		ObserveCombatVisuals();
		RefreshInventoryUi();
		PlaceGroundItems();
		PlaceTokens();
	}

	private void RefreshPeople()
	{
		// The conversation ends by itself when you walk away (or the other does).
		if (_talkingTo is { } talking && _view.PeopleHere.All(p => p.Id != talking))
			_talkingTo = null;
		_peopleScroll.CustomMinimumSize = new Vector2(0, _talkingTo is null ? 56 : 150);
		_eventsPanel.Visible = false;
		var key = string.Join("|", _view.PeopleHere.Select(p =>
			$"{p.Id}:{p.Doing}:{string.Join(',', p.Topics.Select(t => $"{t.Kind}:{t.Observation}:{t.Summary}"))}"))
			+ "#" + _view.UnseenNearby + "#" + _talkingTo;
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
				var tell = new Button { Text = $"Racconta {TopicKind(topic)}: {topic.Summary}",
					AutowrapMode = TextServer.AutowrapMode.WordSmart };
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
		var me = (_view.Id, _view.Position, _view.Spot, _view.Move);
		var figures = new List<(ActorId Id, string Name, string? Doing, string? Condition,
			IReadOnlyList<ConditionView> Conditions, bool Hostile, bool Player, GridPos? Position, Spot? Spot, MoveView? Move)>
		{
			(me.Id, "Tu", _view.Action is { Kind: "Rest" } rest
				? rest.Description.Contains("Dorm", StringComparison.OrdinalIgnoreCase) ? "Zzz · dorme" : "riposa"
				: null, _view.Dead ? "morto" : null, _view.Conditions, false, true, me.Position, me.Spot, me.Move),
		};
		figures.AddRange(_view.VisibleActors.Select(a => (a.Id,
			a.SeesYou == true ? $"{a.Name} · ti vede" : a.Name,
			a.Doing, a.Condition, a.Conditions, a.Hostile, false, a.Position, a.Spot, a.Move)));

		var shown = new HashSet<ActorId>();
		foreach (var figure in figures)
		{
			if (figure.Position is null)
				continue;
			if (!_tokens.TryGetValue(figure.Id, out var token))
			{
				token = MakeToken(figure.Id, figure.Name, figure.Player);
				_tokens[figure.Id] = token;
				_sorted.AddChild(token);
			}
			var previousPosition = token.Position;
			token.Position = CombatVisualPosition(figure.Id) is { } visual
				? visual : AuthoritativePosition(figure.Position.Value, figure.Spot);
			token.Modulate = Shade(ScreenToCell(token.Position));
			var prone = figure.Condition is "morto" or "tramortito"
				|| figure.Condition?.StartsWith("a terra", StringComparison.Ordinal) == true
				|| figure.Conditions.Any(c => c.Kind == ConditionKind.Prone);
			token.GetNode<Polygon2D>("Body").Visible = !prone;
			token.GetNode<Polygon2D>("Head").Visible = !prone;
			token.GetNode<Polygon2D>("Prone").Visible = prone;
			UpdateFigureSprite(token, figure.Id, previousPosition,
				figure.Move is not null || _combatVisualTracks.ContainsKey(figure.Id), prone, figure.Move);
			token.GetNode<Line2D>("DodgeMark").Visible = !prone && _view.Combat is { } combat
				&& (figure.Player ? combat.You.Dodging
					: combat.Combatants.Any(c => c.Id == figure.Id && c.Dodging));
			var tokenColor = figure.Player ? new Color(0.25f, 0.45f, 0.85f)
				: figure.Hostile ? new Color(0.84f, 0.25f, 0.22f) : new Color(0.35f, 0.65f, 0.35f);
			token.GetNode<Polygon2D>("Body").Color = tokenColor;
			token.GetNode<Polygon2D>("Prone").Color = tokenColor;
			if (figure.Condition == "morto")
				token.Modulate = token.Modulate.Darkened(0.45f);
			if (figure.Player && _view.Sneaking is not null)
				token.Modulate = token.Modulate with { A = 0.55f }; // sneaking
			else if (figure.Move is { Stealthy: true })
				token.Modulate = token.Modulate with { A = 0.68f }; // the visible slow, cautious gait
			if (token.GetNodeOrNull<Label>("Name") is { } nameLabel)
			{
				var condition = (figure.Condition is null or "illeso" ? "" : $" · {figure.Condition}")
					+ ConditionsText(figure.Conditions);
				var hostility = figure.Hostile ? " · OSTILE" : "";
				nameLabel.Text = figure.Doing is { Length: > 0 } doing
					? $"{figure.Name}{hostility}{condition}\n{doing}" : figure.Name + hostility + condition;
				nameLabel.AddThemeColorOverride("font_color", figure.Hostile ? new Color(1f, 0.63f, 0.59f) : Colors.White);
			}
			token.GetNode<Polygon2D>("Flame").Visible = figure.Player
				? _view.TorchLitUntil is not null
				: _view.VisibleActors.Any(a => a.Id == figure.Id && a.Torch);
			shown.Add(figure.Id);
		}
		// A mover that vanished after a visible stretch still crosses that stretch on screen, then disappears.
		foreach (var actor in _combatVisualTracks.Keys.ToArray())
		{
			if (shown.Contains(actor) || CombatVisualPosition(actor) is not { } position)
				continue;
			if (!_tokens.TryGetValue(actor, out var token))
			{
				token = MakeToken(actor, "Figura", false);
				_tokens[actor] = token;
				_sorted.AddChild(token);
			}
			var previousPosition = token.Position;
			token.Position = position;
			token.Modulate = Shade(ScreenToCell(position));
			UpdateFigureSprite(token, actor, previousPosition, true, false);
			shown.Add(actor);
		}
		foreach (var (id, token) in _tokens)
			token.Visible = shown.Contains(id);
		// Nearby people can share almost the same projected Y: place their captions on separate lines.
		var occupiedCaptions = new List<Rect2>();
		foreach (var token in _tokens.Values.Where(t => t.Visible && t.GetNodeOrNull<Label>("Name") is not null)
			.OrderBy(t => t.Position.Y).ThenBy(t => t.Position.X))
		{
			var label = token.GetNode<Label>("Name");
			var height = label.Text.Contains('\n') ? 36f : 20f;
			var caption = new Rect2(token.Position + new Vector2(-70, -58), new Vector2(140, height));
			while (occupiedCaptions.Any(used => used.Grow(3).Intersects(caption)))
				caption.Position -= new Vector2(0, height + 6);
			label.Position = caption.Position - token.Position;
			occupiedCaptions.Add(caption);
		}
		if (_tokens.TryGetValue(_view.Id, out var mine))
			_camera.Position = _camera.Position.Lerp(CameraTarget(_smokeFocus is { } focus ? CellToScreen(focus) : mine.Position), 0.15f);
	}

	/// <summary>
	/// The Sim supplies the point at the current tick, including between movement checkpoints.
	/// </summary>
	private Vector2 AuthoritativePosition(GridPos square, Spot? spot) =>
		spot is { } exact ? SpotToScreen(exact) : CellToScreen(square);

	/// <summary>
	/// Presentation of the core's light levels (<see cref="PlayerView.MapLight"/>): each square is shaded by its own
	/// level, softened towards its neighbours so lamplight fades out instead of stopping at a hard edge.
	/// </summary>
	private void ShadeMap()
	{
		if (_view.MapLight is not { } rows)
			return;
		var key = string.Concat(rows) + "|" + string.Concat(_view.MapVisibility ?? Array.Empty<string>())
			+ "|" + string.Join(",", _view.Doors.Select(d => $"{d.At}:{d.Open}"));
		if (key == _lightKey)
			return;
		_lightKey = key;
		int h = rows.Count, w = rows[0].Length;
		var map = _view.Map!;
		var closedDoors = _view.Doors.Where(d => d.Open == false).Select(d => d.At).ToHashSet();
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
			_glow[x, y] = _view.MapVisibility is not null ? Level(x, y)
				: count == 0 ? Level(x, y) : 0.6f * Level(x, y) + 0.4f * sum / count;
		}
		foreach (var (cell, item) in _lit)
			item.Modulate = Shade(cell);
		foreach (var (cell, item) in _edgeLit)
			item.Modulate = Shade(cell);
	}

	private static readonly Color Night = new(0.22f, 0.25f, 0.42f);

	private Color Shade(GridPos cell)
	{
		if (_view.MapVisibility is { } visibility)
		{
			var state = visibility[cell.Y][cell.X];
			if (state == '0') return new Color(0.025f, 0.025f, 0.035f);
			if (state == '1') return new Color(0.14f, 0.15f, 0.18f);
		}
		return cell.X >= 0 && cell.Y >= 0 && cell.X < _glow.GetLength(0) && cell.Y < _glow.GetLength(1)
			? Night.Lerp(Colors.White, _glow[cell.X, cell.Y]) : Colors.White;
	}

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

	private static Vector2 SpotToScreen(Spot s) => new(
		((s.X - s.Y) / (float)Spot.PerSquare) * TileW / 2f,
		((s.X + s.Y) / (float)Spot.PerSquare - 1f) * TileH / 2f);

	private static Spot ScreenToSpot(Vector2 p)
	{
		var a = p.X / (TileW / 2f);
		var b = p.Y / (TileH / 2f);
		return new Spot((int)Math.Round(((a + b) / 2f + 0.5f) * Spot.PerSquare),
			(int)Math.Round(((b - a) / 2f + 0.5f) * Spot.PerSquare));
	}

	// Keep the compact interior in the viewport area beside the fixed left HUD.
	private Vector2 CameraTarget(Vector2 worldPoint) => _view.MapVisibility is not null
		? worldPoint + new Vector2(_view.Combat is null ? -200f : -25f, 0f) : worldPoint;

	/// <summary>World position of a square (used by the smoke runner to click on it).</summary>
	internal static Vector2 ScreenPointOf(GridPos c) => CellToScreen(c);

	/// <summary>Keep a fixture's door in frame while the paused village smoke captures it.</summary>
	internal void FocusForSmoke(GridPos? cell)
	{
		_smokeFocus = cell;
		if (cell is { } at)
			_camera.Position = CameraTarget(CellToScreen(at));
	}

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
		var canUseBoundary = (_view.Combat is null || _view.Combat.IsYourTurn)
			&& (DoorAt(mouse) is not null || WindowAt(mouse) is not null);
		Input.SetDefaultCursorShape(person is not null || canUseBoundary
			? Input.CursorShape.PointingHand : Input.CursorShape.Arrow);
		var cell = person is { } id && _tokens.TryGetValue(id, out var token) ? ScreenToCell(token.Position) : ScreenToCell(mouse);
		var map = _view.Position is null ? null : _view.Map;
		_hover.Visible = _view.Combat is not null && map is not null
			&& cell.X >= 0 && cell.Y >= 0 && cell.X < map.Width && cell.Y < map.Height
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
		BuildRoofLayer();
		BuildVisionFeather();
		LoadGroundTextures();

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
			ApplyGroundTexture(tile, c, map.Zones.TryGetValue(c, out var floorZone) ? floorZone : null, cell, shade);
			_floor.AddChild(tile);
			_lit[cell] = tile;
			if (c == '#')
			{
				var wall = MakeBlock(cell, Wall, WallHeight, _stoneWallTexture);
				_sorted.AddChild(wall);
				_lit[cell] = wall; // the floor under a wall is hidden anyway
				RegisterBlockOccluder(wall);
			}
		}

		foreach (var store in _sim.GetWorldView().Stores.Where(s => s.Position is not null
			&& _view.Locations.Any(l => l.Id == s.Location && l.Area == map.Area)))
		{
			var block = MakeBlock(store.Position!.Value, new Color(0.65f, 0.45f, 0.30f), 34f, _woodTexture);
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

		foreach (var edge in map.Edges)
		{
			var panel = MakeEdgePanel(edge);
			_sorted.AddChild(panel);
			if (edge.Kind is MapEdgeKind.Window or MapEdgeKind.Door)
				AddBarrierMark(panel, edge.Kind == MapEdgeKind.Window ? 17f : WallHeight);
			_edgeLit.Add((FacadeShadeCell(edge), panel));
			RegisterBuildingEdge(edge, panel);
			if (edge.Kind == MapEdgeKind.Window)
				_windowPanels[(edge.A, edge.B)] = panel;
			if (edge.DoorAt is { } at)
				_doorPanels[at] = panel;
		}

		// Legacy square doors keep their wooden floor and panel.
		foreach (var door in _view.Doors)
		{
			if (map.Edges.Any(e => e.DoorAt == door.At))
				continue;
			if (_lit.TryGetValue(door.At, out var tile) && tile is Polygon2D floorTile)
				_doorFloors[door.At] = floorTile;
			var diamond = Diamond(0.7f);
			var outline = new Line2D { Position = CellToScreen(door.At), Points = new[]
				{ diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] },
				Width = 2f, Visible = false };
			_floor.AddChild(outline);
			_doorOutlines[door.At] = outline;
			var panel = MakeBlock(door.At, DoorWood, WallHeight, _woodTexture);
			_sorted.AddChild(panel);
			AddBarrierMark(panel, WallHeight);
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

		_hover = new Polygon2D { Polygon = Diamond(), Color = Hover, ZIndex = -5, Visible = false };
		AddChild(_hover);
		BuildSpotOverlay();
		BuildShotLayer();

		var zoom = _view.MapVisibility is null ? 1.2f : 1.08f;
		_camera = new Camera2D { Position = CameraTarget(CellToScreen(_view.Position ?? new GridPos(0, 0))), Zoom = new Vector2(zoom, zoom) };
		AddChild(_camera);
		_camera.MakeCurrent();
	}

	/// <summary>A raised isometric block on a square (walls, the store), with its origin at the base for Y-sorting.</summary>
	private static Node2D MakeBlock(GridPos cell, Color color, float height, Texture2D? texture = null)
	{
		var node = new Node2D { Position = CellToScreen(cell) };
		var d = Diamond(0.98f);
		var up = new Vector2(0, -height);
		Polygon2D Face(Vector2[] points, Color tint, float textureShade, bool top)
		{
			var face = new Polygon2D { Polygon = points, Color = texture is null ? tint : top
				? Colors.White : Colors.White.Darkened(textureShade) };
			if (texture is not null)
			{
				var span = Math.Max(1, texture.GetWidth() * 160 / 1254);
				var margin = Math.Max(1, texture.GetWidth() * 120 / 1254);
				var sx = margin + (cell.X * 73 + cell.Y * 101) % (texture.GetWidth() - 2 * margin);
				var sy = margin + (cell.X * 89 + cell.Y * 59) % (texture.GetHeight() - 2 * margin);
				face.Texture = texture;
				face.UV = top
					? new[] { new Vector2(sx, sy - span / 2), new Vector2(sx + span / 2, sy),
						new Vector2(sx, sy + span / 2), new Vector2(sx - span / 2, sy) }
					: new[] { new Vector2(sx, sy), new Vector2(sx + span, sy),
						new Vector2(sx + span, sy + span), new Vector2(sx, sy + span) };
			}
			return face;
		}
		node.AddChild(Face(new[] { d[3], d[2], d[2] + up, d[3] + up }, color.Darkened(0.3f), 0.32f, false));
		node.AddChild(Face(new[] { d[2], d[1], d[1] + up, d[2] + up }, color.Darkened(0.12f), 0.15f, false));
		node.AddChild(Face(new[] { d[0] + up, d[1] + up, d[2] + up, d[3] + up }, color.Lightened(0.1f), 0f, true));
		return node;
	}

	private Node2D MakeToken(ActorId id, string name, bool player)
	{
		var token = new Node2D();
		var color = player ? new Color(0.25f, 0.45f, 0.85f) : new Color(0.35f, 0.65f, 0.35f);
		token.AddChild(new Polygon2D { Polygon = Diamond(0.4f), Color = new Color(0, 0, 0, 0.35f) });
		token.AddChild(new Polygon2D
		{
			Name = "Body",
			Polygon = new[] { new Vector2(-6, 0), new Vector2(6, 0), new Vector2(5, -24), new Vector2(-5, -24) },
			Color = color,
		});
		token.AddChild(new Polygon2D
		{
			Name = "Head",
			Polygon = Enumerable.Range(0, 10).Select(i => Vector2.FromAngle(Mathf.Tau * i / 10) * 5.5f).ToArray(),
			Position = new Vector2(0, -30),
			Color = new Color(0.95f, 0.82f, 0.68f),
		});
		token.AddChild(new Polygon2D
		{
			Name = "Prone",
			Polygon = new[] { new Vector2(-16, -4), new Vector2(-10, -10), new Vector2(15, -5), new Vector2(10, 1) },
			Color = color,
			Visible = false,
		});
		AddFigureSprite(token, id);
		token.AddChild(new Line2D
		{
			Name = "DodgeMark",
			Points = new[] { new Vector2(0, -40), new Vector2(13, -20), new Vector2(0, 1),
				new Vector2(-13, -20), new Vector2(0, -40) },
			Width = 2f,
			DefaultColor = new Color(0.45f, 0.9f, 1f),
			Visible = false,
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
		var panel = HudPanel(16, 300, 16, 222);
		// In the explored interior, unknown squares can fall behind the HUD; keep the controls readable.
		if (_view.MapVisibility is not null)
			panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.12f, 0.12f, 0.13f) });
		ui.AddChild(panel);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 3);
		panel.AddChild(box);

		_clock = new Label();
		_clock.AddThemeFontSizeOverride("font_size", 15);
		box.AddChild(_clock);
		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_status);
		_food = new Label();
		box.AddChild(_food);
		_levelExperience = new Label();
		box.AddChild(_levelExperience);
		_hudHitPoints = new Label();
		box.AddChild(_hudHitPoints);
		BuildConditionsUi(box);
		_watched = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_watched);

		_exploreActionPanel = HudPanel(208, 930, -145, -16, bottomAnchored: true);
		_exploreActionPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.11f, 0.94f) });
		ui.AddChild(_exploreActionPanel);
		var actions = new VBoxContainer();
		actions.AddThemeConstantOverride("separation", 4);
		_exploreActionPanel.AddChild(actions);
		var primary = new HBoxContainer();
		actions.AddChild(primary);

		_pause = new Button { Text = "Pausa (Spazio)", ToggleMode = true };
		_pause.Toggled += SetPaused;
		_pause.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		primary.AddChild(_pause);
		_stop = new Button { Text = "Fermati (X)" };
		_stop.Pressed += Stop;
		_stop.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		primary.AddChild(_stop);
		_sneakToggle = new CheckButton { Text = "Furtivo (F)", TooltipText = "Muoviti piano e cerca il buio." };
		_sneakToggle.Toggled += SetSneak;
		_sneakToggle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		primary.AddChild(_sneakToggle);
		_torch = new Button { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_torch.Pressed += ToggleTorch;
		primary.AddChild(_torch);
		var utility = new HBoxContainer();
		actions.AddChild(utility);
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
		utility.AddChild(paceRow);
		var restButton = new Button { Text = "Riposo", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		restButton.Pressed += () => _restPanel.Visible = !_restPanel.Visible;
		utility.AddChild(restButton);
		BuildClassUi(ui, utility);
		BuildInventoryUi(ui, utility);
		_restPanel = HudPanel(300, 720, -300, -165, bottomAnchored: true);
		_restPanel.Visible = false;
		_restPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.10f, 0.11f, 0.13f, 0.96f) });
		ui.AddChild(_restPanel);
		var restBox = new VBoxContainer();
		_restPanel.AddChild(restBox);
		BuildRestUi(restBox);

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
		actions.AddChild(_storeRow);

		_peopleScroll = new ScrollContainer { OffsetLeft = 16, OffsetRight = 350,
			OffsetTop = 234, OffsetBottom = 410, Visible = false };
		_people = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_peopleScroll.AddChild(_people);
		ui.AddChild(_peopleScroll);

		var saveRow = new HBoxContainer();
		var save = new Button { Text = "Salva", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		save.Pressed += SaveGame;
		saveRow.AddChild(save);
		var load = new Button { Text = "Carica", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		load.Pressed += LoadGame;
		saveRow.AddChild(load);
		utility.AddChild(saveRow);
		_journalButton = new Button { Text = "Diario" };
		_journalButton.Pressed += ToggleJournal;
		utility.AddChild(_journalButton);
		BuildAttackBar(actions);

		_message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
		_message.AddThemeFontSizeOverride("font_size", 13);
		ui.AddChild(_message);

		_eventsPanel = new PanelContainer { Visible = false };
		var eventsBox = new VBoxContainer();
		_eventsPanel.AddChild(eventsBox);
		var eventsTitle = new Label { Text = "Eventi" };
		eventsTitle.AddThemeFontSizeOverride("font_size", 16);
		eventsBox.AddChild(eventsTitle);
		var eventScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 112) };
		_events = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_events.AddThemeFontSizeOverride("font_size", 12);
		eventScroll.AddChild(_events);
		eventsBox.AddChild(eventScroll);
		ui.AddChild(_eventsPanel);
		BuildCombatUi(ui);
		BuildLoreUi(ui);
		BuildBarrierUi(ui);
		BuildJournalUi(ui);
		BuildHudLog(ui);

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
