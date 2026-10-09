using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>
/// Slice client: isometric placeholder map, travel, food deliveries, NPCs acting on their own,
/// save/load and a debug panel explaining decisions. All rules live in Sim; this node only sends
/// commands, plays time out while the player's action runs, and draws the current <see cref="WorldView"/>.
/// </summary>
public partial class Main : Node2D
{
	// Presentation-only placement of simulation places on the isometric grid.
	private static readonly Dictionary<LocationId, Vector2> LocationCells = new()
	{
		[SliceScenario.Ids.Inn] = new Vector2(3, 3),
		[SliceScenario.Ids.Granary] = new Vector2(8, 4),
		[SliceScenario.Ids.BanditCamp] = new Vector2(9, 10),
	};

	private const int GridWidth = 13;
	private const int GridHeight = 13;
	private const string SavePath = "user://save.json";

	/// <summary>Minimum playback speed (game seconds per real second); long actions play faster.</summary>
	private const double MinGameSecondsPerRealSecond = 150;
	/// <summary>Longest real time an action takes to play out on screen.</summary>
	private const double MaxPlaybackRealSeconds = 3;

	private static readonly Color GroundA = new(0.30f, 0.42f, 0.26f);
	private static readonly Color GroundB = new(0.27f, 0.38f, 0.23f);
	private static readonly Color ForestA = new(0.18f, 0.30f, 0.17f);
	private static readonly Color ForestB = new(0.16f, 0.27f, 0.15f);
	private static readonly Color Platform = new(0.55f, 0.48f, 0.36f);
	private static readonly Color PlatformHover = new(0.75f, 0.66f, 0.42f);
	private static readonly Color RouteColor = new(0.85f, 0.78f, 0.55f, 0.8f);
	private static readonly Color PlayerColor = new(0.25f, 0.45f, 0.85f);
	private static readonly Color BanditColor = new(0.80f, 0.22f, 0.20f);
	private static readonly Color NeutralColor = new(0.60f, 0.60f, 0.60f);

	private SimulationSession _sim = null!;
	private WorldView _view = null!;

	private Node2D _sorted = null!; // Y-sorted layer: buildings and characters
	private Camera2D _camera = null!;
	private readonly Dictionary<LocationId, Polygon2D> _platforms = new();
	private readonly Dictionary<ActorId, Node2D> _tokens = new();
	private readonly Dictionary<StoreId, Label> _storeLabels = new();

	private Label _clockLabel = null!;
	private Label _statusLabel = null!;
	private Label _foodLabel = null!;
	private HBoxContainer _depositRow = null!;
	private SpinBox _depositAmount = null!;
	private Button _waitButton = null!;
	private Button _saveButton = null!;
	private Button _loadButton = null!;
	private Label _messageLabel = null!;
	private Label _factsLabel = null!;
	private Label _debugLabel = null!;

	private ActionId? _runningAction;
	private double _playbackSpeed = MinGameSecondsPerRealSecond;
	private double _pendingGameSeconds;
	private LocationId? _hovered;

	public override void _Ready()
	{
		_sim = SimulationSession.Create(SliceScenario.Create());
		_view = _sim.GetWorldView();

		BuildGround();
		BuildRoutes();
		_sorted = new Node2D { YSortEnabled = true };
		AddChild(_sorted);
		BuildLocations();
		BuildCamera();
		BuildUi();
		Refresh();

		// Development aid: `godot --path game -- --smoke=<dir>` plays a scripted session and saves screenshots.
		var smoke = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--smoke="));
		if (smoke is not null)
			AddChild(new SmokeRunner(this, smoke["--smoke=".Length..]));
	}

	public override void _Process(double delta)
	{
		PanCamera(delta);
		UpdateHover();

		if (_runningAction is not { } action)
			return;

		_pendingGameSeconds += delta * _playbackSpeed;
		var whole = (long)_pendingGameSeconds;
		if (whole == 0)
			return;
		_pendingGameSeconds -= whole;

		var result = _sim.AdvanceUntilCompleted(action, Duration.FromSeconds(whole));
		if (result.Outcome != AdvanceOutcome.TimeLimitReached)
		{
			_runningAction = null;
			_pendingGameSeconds = 0;
		}
		Refresh();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton { Pressed: true } mouse)
			return;

		switch (mouse.ButtonIndex)
		{
			case MouseButton.Left when _hovered is { } target:
				TravelTo(target);
				break;
			case MouseButton.WheelUp:
				_camera.Zoom = (_camera.Zoom * 1.1f).Clamp(new Vector2(0.4f, 0.4f), new Vector2(2.5f, 2.5f));
				break;
			case MouseButton.WheelDown:
				_camera.Zoom = (_camera.Zoom / 1.1f).Clamp(new Vector2(0.4f, 0.4f), new Vector2(2.5f, 2.5f));
				break;
		}
	}

	// ---------------------------------------------------------------- player actions

	public bool IsBusy => _runningAction is not null;

	public void TravelTo(LocationId destination) =>
		StartAction(new TravelCommand { Actor = _sim.Player, Destination = destination });

	/// <summary>Used by the smoke runner: same path as the UI controls.</summary>
	public void DepositViaUi(int amount)
	{
		_depositAmount.Value = amount;
		DepositFood();
	}

	public void WaitOneHour() => StartAction(new WaitCommand { Actor = _sim.Player, Duration = Duration.FromHours(1) });

	private void DepositFood()
	{
		var player = PlayerView();
		var store = _view.Stores.FirstOrDefault(s => s.Location == player.Location);
		if (store is null)
			return;
		StartAction(new DepositFoodCommand { Actor = _sim.Player, Store = store.Id, Amount = (int)_depositAmount.Value });
	}

	/// <summary>Sends a command; if it starts an action, time plays out in _Process until it completes.</summary>
	private void StartAction(Command command)
	{
		if (_runningAction is not null)
			return;

		var result = _sim.Execute(command);
		if (result.Success)
		{
			_runningAction = result.Action;
			var duration = result.CompletesAt!.Value.Since(_sim.Now).Seconds;
			_playbackSpeed = Math.Max(MinGameSecondsPerRealSecond, duration / MaxPlaybackRealSeconds);
		}
		Refresh(result.Success ? null : result.Message);
	}

	// ---------------------------------------------------------------- save / load

	public void SaveGame()
	{
		if (_runningAction is not null)
			return;

		// Write to a temporary file, then replace: a crash mid-write never corrupts the previous save.
		var path = ProjectSettings.GlobalizePath(SavePath);
		var temp = path + ".tmp";
		try
		{
			using (var stream = File.Create(temp))
				_sim.Save(stream);
			File.Move(temp, path, overwrite: true);
			ShowMessage($"Partita salvata ({_view.Now.Hour:00}:{_view.Now.Minute:00}).");
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			ShowMessage($"Salvataggio non riuscito: {e.Message}");
		}
	}

	public void LoadGame()
	{
		if (_runningAction is not null)
			return;

		var path = ProjectSettings.GlobalizePath(SavePath);
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

		// The current game is replaced only if the load succeeded.
		if (!result.Success)
		{
			ShowMessage($"Caricamento non riuscito: {result.Error}");
			return;
		}
		_sim = result.Session!;
		Refresh();
		ShowMessage($"Partita caricata ({_view.Now.Hour:00}:{_view.Now.Minute:00}).");
	}

	private void ShowMessage(string text) => _messageLabel.Text = text;

	// ---------------------------------------------------------------- view

	private ActorView PlayerView() => _view.Actors.Single(a => a.Id == _view.Player);

	private void Refresh(string? rejection = null)
	{
		_view = _sim.GetWorldView();
		var player = PlayerView();

		PlaceTokens();

		var now = _view.Now;
		_clockLabel.Text = $"Giorno {now.Day + 1}   {Clock(now)}";

		if (player.Travel is { } travel)
			_statusLabel.Text = $"In viaggio verso {LocationName(travel.Destination)} — arrivo alle {Clock(travel.ArrivesAt)}";
		else if (player.Action is { } action)
			_statusLabel.Text = $"{action.Description} — fino alle {Clock(action.CompletesAt)}";
		else
			_statusLabel.Text = $"Ti trovi a: {LocationName(player.Location!.Value)}.\nClicca un luogo per viaggiare.";
		if (rejection is not null)
			_statusLabel.Text += $"\n⚠ {rejection}";

		var busy = _runningAction is not null;
		_waitButton.Disabled = busy;
		_saveButton.Disabled = busy;
		_loadButton.Disabled = busy;
		_foodLabel.Text = $"Razioni con te: {player.Food}";

		// Deposit controls only where there is a store; the core still validates every request.
		var storeHere = _view.Stores.FirstOrDefault(s => s.Location == player.Location);
		_depositRow.Visible = storeHere is not null && player.Food > 0;
		_depositAmount.MaxValue = Math.Max(1, player.Food);
		_depositAmount.Editable = !busy;

		foreach (var store in _view.Stores)
			_storeLabels[store.Id].Text = $"{store.Name}: {store.Food}";

		_factsLabel.Text = string.Join("\n", _view.RecentFacts.TakeLast(10).Select(f => $"[{Clock(f.At)}] {f.Description}"));
		_debugLabel.Text = DebugText();
	}

	/// <summary>The omniscient debug panel: what each NPC and faction is doing and why.</summary>
	private string DebugText()
	{
		var lines = new List<string>();
		foreach (var npc in _view.Actors.Where(a => !a.IsPlayer))
		{
			lines.Add($"■ {npc.Name}  ({npc.Food} razioni)");
			lines.Add($"  Azione: {npc.Action?.Description ?? "nessuna"}" +
					  (npc.Action is { } a ? $" fino alle {Clock(a.CompletesAt)}" : ""));
			if (npc.Assignment is { } job)
				lines.Add($"  Incarico: razzia di {StoreName(job.Target)} ({job.Amount} razioni)");
			if (npc.LastDecision is { } d)
			{
				lines.Add($"  Decisione [{Clock(d.At)}] {d.Rule}: {d.Reason}");
				lines.AddRange(d.Inputs.Select(i => $"    · {i}"));
			}
			lines.Add("");
		}
		foreach (var faction in _view.Factions)
		{
			var home = faction.HomeStore is { } h ? $" — {StoreName(h)}: {_view.Stores.Single(s => s.Id == h).Food}" : "";
			lines.Add($"◆ {faction.Name}{home}" + (faction.DailyUpkeep > 0 ? $", consumo {faction.DailyUpkeep}/giorno" : ""));
			if (faction.NextEvaluation is { } next)
				lines.Add($"  Prossima valutazione: {Clock(next)}");
			if (faction.LastDecision is { } d)
			{
				lines.Add($"  Decisione [{Clock(d.At)}] {d.Rule}: {d.Reason}");
				lines.AddRange(d.Inputs.Select(i => $"    · {i}"));
			}
			lines.Add("");
		}
		return string.Join("\n", lines).TrimEnd();
	}

	private void PlaceTokens()
	{
		foreach (var actor in _view.Actors)
		{
			if (!_tokens.TryGetValue(actor.Id, out var token))
			{
				token = MakeToken(actor);
				_tokens[actor.Id] = token;
				_sorted.AddChild(token);
			}
			token.Position = ActorScreenPosition(actor);
		}
	}

	private Vector2 ActorScreenPosition(ActorView actor)
	{
		if (actor.Location is { } here)
		{
			// Several actors at the same place stand side by side, in id order.
			var present = _view.Actors.Where(a => a.Location == here).ToList();
			var index = present.FindIndex(a => a.Id == actor.Id);
			return StandPoint(here) + new Vector2((index - (present.Count - 1) / 2f) * 30f, 0);
		}

		// Travelling: the simulation says the actor is at neither end; interpolate for display only.
		var travel = actor.Travel!;
		var total = travel.ArrivesAt.Since(travel.DepartedAt).Seconds;
		var progress = total > 0 ? (float)_view.Now.Since(travel.DepartedAt).Seconds / total : 1f;
		return StandPoint(travel.Origin).Lerp(StandPoint(travel.Destination), Mathf.Clamp(progress, 0f, 1f));
	}

	/// <summary>Where characters stand at a place: in front of its building.</summary>
	private static Vector2 StandPoint(LocationId location) =>
		Iso.CellToScreen(LocationCells[location] + new Vector2(0.9f, 0.9f));

	/// <summary>World position of a place (used by the smoke runner to aim the mouse).</summary>
	public Vector2 ScreenPointOf(LocationId location) => Iso.CellToScreen(LocationCells[location]);

	private string LocationName(LocationId id) => _view.Locations.Single(l => l.Id == id).Name;
	private string StoreName(StoreId id) => _view.Stores.Single(s => s.Id == id).Name;
	private static string Clock(GameTime t) => $"{t.Hour:00}:{t.Minute:00}";

	private void UpdateHover()
	{
		var mouse = GetGlobalMousePosition();
		LocationId? hovered = null;
		foreach (var (id, platform) in _platforms)
		{
			var local = mouse - platform.GlobalPosition;
			if (Geometry2D.IsPointInPolygon(local, platform.Polygon))
				hovered = id;
		}

		if (hovered == _hovered)
			return;
		_hovered = hovered;
		foreach (var (id, platform) in _platforms)
			platform.Color = id == hovered ? PlatformHover : Platform;
	}

	// ---------------------------------------------------------------- scene construction

	private void BuildGround()
	{
		var ground = new Node2D { ZIndex = -10 };
		AddChild(ground);
		var camp = LocationCells[SliceScenario.Ids.BanditCamp];
		for (var x = 0; x < GridWidth; x++)
		for (var y = 0; y < GridHeight; y++)
		{
			// Darker tiles around the camp suggest the forest (presentation only).
			var forest = new Vector2(x, y).DistanceTo(camp) < 3.5f;
			var even = (x + y) % 2 == 0;
			ground.AddChild(new Polygon2D
			{
				Polygon = Iso.Diamond(),
				Position = Iso.CellToScreen(new Vector2(x, y)),
				Color = forest ? (even ? ForestA : ForestB) : (even ? GroundA : GroundB),
			});
		}
	}

	private void BuildRoutes()
	{
		var routes = new Node2D { ZIndex = -5 };
		AddChild(routes);
		foreach (var route in _view.Routes.Where(r => r.From.CompareTo(r.To) < 0))
		{
			var a = StandPoint(route.From);
			var b = StandPoint(route.To);
			routes.AddChild(new Line2D { Points = new[] { a, b }, Width = 6, DefaultColor = RouteColor });
			routes.AddChild(MakeLabel($"{route.TravelTime.Seconds / 60} min", (a + b) / 2 + new Vector2(16, -44), 14));
		}
	}

	private void BuildLocations()
	{
		foreach (var location in _view.Locations)
		{
			var center = Iso.CellToScreen(LocationCells[location.Id]);

			var platform = new Polygon2D { Polygon = Iso.Diamond(2.6f), Color = Platform, Position = center, ZIndex = -4 };
			AddChild(platform);
			_platforms[location.Id] = platform;

			// Placeholder building: an isometric box whose origin is its base, so Y-sort works.
			var building = new Node2D { Position = center };
			var (top, left, right) = Iso.Box(1.2f, 70f);
			var hue = location.Id.Value.Sum(c => c) % 256 / 255f; // stable across runs, unlike string.GetHashCode
			var baseColor = Color.FromHsv(hue, 0.35f, 0.75f);
			building.AddChild(new Polygon2D { Polygon = left, Color = baseColor.Darkened(0.35f) });
			building.AddChild(new Polygon2D { Polygon = right, Color = baseColor.Darkened(0.15f) });
			building.AddChild(new Polygon2D { Polygon = top, Color = baseColor.Lightened(0.15f) });
			building.AddChild(MakeLabel(location.Name, new Vector2(-60, -130), 18));
			var storeOffset = 0f;
			foreach (var store in _view.Stores.Where(s => s.Location == location.Id))
			{
				var label = MakeLabel("", new Vector2(88, -16 + storeOffset), 15);
				label.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.6f));
				building.AddChild(label);
				_storeLabels[store.Id] = label;
				storeOffset += 20;
			}
			_sorted.AddChild(building);
		}
	}

	private static Node2D MakeToken(ActorView actor)
	{
		var color = actor.IsPlayer ? PlayerColor : actor.Faction == SliceScenario.Ids.Bandits ? BanditColor : NeutralColor;
		var token = new Node2D();
		// Placeholder figure: a shadow, a body and a head, with the origin at the feet.
		token.AddChild(new Polygon2D { Polygon = Iso.Diamond(0.3f), Color = new Color(0, 0, 0, 0.35f) });
		token.AddChild(new Polygon2D
		{
			Polygon = new[] { new Vector2(-9, 0), new Vector2(9, 0), new Vector2(7, -34), new Vector2(-7, -34) },
			Color = color,
		});
		token.AddChild(new Polygon2D { Polygon = Circle(8, 12), Position = new Vector2(0, -42), Color = new Color(0.95f, 0.82f, 0.68f) });
		if (!actor.IsPlayer)
			token.AddChild(MakeLabel(actor.Name, new Vector2(-36, -74), 12));
		return token;
	}

	private void BuildCamera()
	{
		var mapCenter = Iso.CellToScreen(new Vector2(7.5f, 6.5f));
		_camera = new Camera2D { Position = mapCenter, Zoom = new Vector2(0.85f, 0.85f) };
		AddChild(_camera);
		_camera.MakeCurrent();
	}

	private void PanCamera(double delta)
	{
		var dir = Vector2.Zero;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
		if (dir != Vector2.Zero)
			_camera.Position += dir.Normalized() * (float)(600 * delta) / _camera.Zoom.X;
	}

	private void BuildUi()
	{
		var ui = new CanvasLayer();
		AddChild(ui);

		// Player panel (left).
		var panel = new PanelContainer { Position = new Vector2(16, 16) };
		ui.AddChild(panel);
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
		box.AddThemeConstantOverride("separation", 8);
		panel.AddChild(box);

		_clockLabel = new Label();
		_clockLabel.AddThemeFontSizeOverride("font_size", 22);
		box.AddChild(_clockLabel);

		_statusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_statusLabel);

		_foodLabel = new Label();
		box.AddChild(_foodLabel);

		_depositRow = new HBoxContainer();
		_depositRow.AddThemeConstantOverride("separation", 8);
		_depositAmount = new SpinBox { MinValue = 1, MaxValue = 1, Value = 1, Step = 1, CustomMinimumSize = new Vector2(90, 0) };
		_depositRow.AddChild(_depositAmount);
		var depositButton = new Button { Text = "Consegna razioni al deposito", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		depositButton.Pressed += DepositFood;
		_depositRow.AddChild(depositButton);
		box.AddChild(_depositRow);

		_waitButton = new Button { Text = "Attendi 1 ora" };
		_waitButton.Pressed += WaitOneHour;
		box.AddChild(_waitButton);

		var saveRow = new HBoxContainer();
		saveRow.AddThemeConstantOverride("separation", 8);
		_saveButton = new Button { Text = "Salva", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_saveButton.Pressed += SaveGame;
		saveRow.AddChild(_saveButton);
		_loadButton = new Button { Text = "Carica", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_loadButton.Pressed += LoadGame;
		saveRow.AddChild(_loadButton);
		box.AddChild(saveRow);

		_messageLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_messageLabel.AddThemeFontSizeOverride("font_size", 13);
		box.AddChild(_messageLabel);

		box.AddChild(new HSeparator());
		box.AddChild(SmallLabel("Cronaca (debug)"));
		_factsLabel = SmallLabel("");
		_factsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		box.AddChild(_factsLabel);

		// Debug panel (right): omniscient, separate from what the player knows.
		var debugPanel = new PanelContainer
		{
			AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -436, OffsetRight = -16, OffsetTop = 16,
		};
		ui.AddChild(debugPanel);
		var debugBox = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
		debugPanel.AddChild(debugBox);
		debugBox.AddChild(SmallLabel("Debug — perché succede"));
		_debugLabel = SmallLabel("");
		_debugLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		debugBox.AddChild(_debugLabel);
	}

	private static Label SmallLabel(string text)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 13);
		return label;
	}

	private static Label MakeLabel(string text, Vector2 position, int size)
	{
		var label = new Label { Text = text, Position = position };
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_outline_color", Colors.Black);
		label.AddThemeConstantOverride("outline_size", 4);
		return label;
	}

	private static Vector2[] Circle(float radius, int segments) =>
		Enumerable.Range(0, segments)
			.Select(i => Vector2.FromAngle(Mathf.Tau * i / segments) * radius)
			.ToArray();
}
