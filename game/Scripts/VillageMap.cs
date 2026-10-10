using Godot;
using RpgSandbox.Sim;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>
/// T6a client: the village as an isometric grid drawn from <see cref="MapView"/>. Click a square to walk there, click
/// someone to walk up to them and talk; each square is shaded by its own light (T6b),
/// Space or "Fermati" to stop. Walking plays out at one game second per real second (Speed 30 = one square per
/// second), so stopping stops the clock. Positions, paths, reach and timing all come from the core.
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
	private static readonly Color Yard = new(0.66f, 0.58f, 0.38f);
	private static readonly Color Wall = new(0.42f, 0.38f, 0.40f);
	private static readonly Color Hover = new(1f, 1f, 1f, 0.25f);

	private SimulationSession _sim = null!;
	private PlayerView _view = null!;

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
	private HBoxContainer _storeRow = null!;
	private SpinBox _amount = null!;
	private VBoxContainer _people = null!;
	private Label _message = null!;

	private ActionId? _running;
	private ActorId? _approaching; // walking up to someone to talk to them
	private ActorId? _talkingTo;
	private bool _walking;
	private double _speed = 1;
	private double _pending;
	private string _peopleKey = "";

	public override void _Ready()
	{
		_sim = SimulationSession.Create(MappedVillageScenario.Create());
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

		if (_running is { } action)
		{
			_pending += delta * _speed;
			var whole = (long)_pending;
			if (whole > 0)
			{
				_pending -= whole;
				var result = _sim.AdvanceUntilCompleted(action, Duration.FromSeconds(whole));
				ActorId? arrived = null;
				if (result.Outcome != AdvanceOutcome.TimeLimitReached)
				{
					_running = null;
					_walking = false;
					_pending = 0;
					if (result.Outcome == AdvanceOutcome.Completed)
						arrived = _approaching;
					_approaching = null;
				}
				Refresh();
				if (arrived is { } who)
					OpenTalk(who);
			}
		}
		PlaceTokens();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space })
		{
			Stop();
			return;
		}
		if (@event is not InputEventMouseButton { Pressed: true } mouse)
			return;
		switch (mouse.ButtonIndex)
		{
			case MouseButton.Left:
				var point = GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position;
				// A figure stands above its square: clicking on its body means that person, not the square behind.
				if (PersonAt(point) is { } person)
					TalkTo(person);
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
	internal bool IsBusy => _running is not null;
	internal bool IsWalking => _walking;
	internal string LastMessage => _message.Text;
	internal ActorId? TalkingTo => _talkingTo;

	/// <summary>World position of a visible figure's body (used by the smoke runner to click on it).</summary>
	internal Vector2? BodyPointOf(ActorId id) =>
		_tokens.TryGetValue(id, out var token) && token.Visible ? token.Position + new Vector2(0, -18) : null;

	/// <summary>Walk to a square. A walk in progress is replaced (the core stops it on the square reached).</summary>
	public void MoveTo(GridPos cell)
	{
		if (Walk(new MoveCommand { Actor = _sim.Player, To = cell }))
			_approaching = null;
	}

	/// <summary>
	/// Click on someone: talk to them if they are next to you, otherwise walk up to them (the core picks the square
	/// before theirs on the way) and talk on arrival.
	/// </summary>
	public void TalkTo(ActorId person)
	{
		if (_view.PeopleHere.Any(p => p.Id == person))
		{
			OpenTalk(person);
			return;
		}
		if (_view.VisibleActors.FirstOrDefault(a => a.Id == person) is not { Position: { } there } seen)
			return;
		if (Walk(new MoveCommand { Actor = _sim.Player, To = there, StopNextTo = true }))
		{
			_approaching = person;
			ShowMessage($"Vai da {seen.Name}.");
		}
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
		_speed = 1; // one game second per real second while walking
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
		if (!_walking)
			return;
		var result = _sim.Execute(new StopCommand { Actor = _sim.Player });
		_running = null;
		_walking = false;
		_approaching = null;
		_pending = 0;
		_target.Visible = false;
		ShowMessage(result.Message);
		Refresh();
	}

	/// <summary>A fixed-length activity: plays out quickly, at most a few real seconds.</summary>
	private void StartActivity(Command command)
	{
		if (_running is not null)
			return;
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
	}

	internal void Deposit(int amount) =>
		StartActivity(new DepositFoodCommand { Actor = _sim.Player, Store = MappedVillageScenario.Ids.GranaryStore, Amount = amount });

	private void Take(int amount) =>
		StartActivity(new TakeFoodCommand { Actor = _sim.Player, Store = MappedVillageScenario.Ids.GranaryStore, Amount = amount });

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
		_talkingTo = null;
		// Resume a walk or activity that was in progress when saving.
		var view = _sim.GetPlayerView();
		_running = view.Action?.Id;
		_walking = view.Move is not null;
		_speed = _walking ? 1 : Math.Max(1, (view.Action?.CompletesAt.Since(view.Action.StartedAt).Seconds ?? 1) / MaxActivityRealSeconds);
		_pending = 0;
		Refresh();
		ShowMessage("Partita caricata.");
	}

	private void ShowMessage(string text) => _message.Text = text;

	// ---------------------------------------------------------------- view

	private void Refresh()
	{
		_view = _sim.GetPlayerView();
		var now = _view.Now;
		_clock.Text = $"Giorno {now.Day + 1}   {now.Hour:00}:{now.Minute:00}:{now.Second:00}   {DayName(_view.Daylight)}   ({HereName(_view.Light)})";
		var where = _view.Location is { } here ? _view.Locations.Single(l => l.Id == here).Name : "strada";
		_status.Text = _walking ? $"Cammini… ({where})" : $"Sei qui: {where}. Clicca dove vuoi andare.";
		_food.Text = $"Razioni con te: {_view.Food}";
		_stop.Disabled = !_walking;
		ShadeMap();
		if (!_walking)
			_target.Visible = false;

		// Store controls where a store is in sight; the core decides whether it is within reach.
		_storeRow.Visible = _view.VisibleStores.Count > 0;
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
		var figures = new List<(ActorId Id, string Name, bool Player, GridPos? Position, MoveView? Move)>
		{
			(me.Id, "Tu", true, me.Position, me.Move),
		};
		figures.AddRange(_view.VisibleActors.Select(a => (a.Id, a.Name, false, a.Position, a.Move)));

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
			shown.Add(figure.Id);
		}
		foreach (var (id, token) in _tokens)
			token.Visible = shown.Contains(id);
		if (_tokens.TryGetValue(_view.Id, out var mine))
			_camera.Position = _camera.Position.Lerp(mine.Position, 0.08f);
	}

	/// <summary>
	/// Presentation only: between two squares of the core's path, slide by the fraction of a second already played.
	/// The square itself always comes from the core.
	/// </summary>
	private Vector2 SmoothPosition(GridPos square, MoveView? move)
	{
		if (move is null || !_walking)
			return CellToScreen(square);
		var elapsed = _view.Now.Since(move.DepartedAt).Seconds + _pending;
		var steps = elapsed * move.Speed / 30.0;
		var reached = (int)Math.Floor(steps);
		if (reached >= move.Path.Count)
			return CellToScreen(move.Path[^1]);
		var from = reached == 0 ? move.From : move.Path[reached - 1];
		var to = move.Path[reached];
		return CellToScreen(from).Lerp(CellToScreen(to), (float)(steps - reached));
	}

	/// <summary>
	/// Presentation of the core's light levels (<see cref="PlayerView.MapLight"/>): each square is shaded by its own
	/// level, softened towards its neighbours so lamplight fades out instead of stopping at a hard edge.
	/// </summary>
	private void ShadeMap()
	{
		if (_view.MapLight is not { } rows)
			return;
		var key = string.Concat(rows);
		if (key == _lightKey)
			return;
		_lightKey = key;
		int h = rows.Count, w = rows[0].Length;
		float Level(int x, int y) => rows[y][x] switch { '2' => 1f, '1' => 0.55f, _ => 0f };
		_glow = new float[w, h];
		for (var y = 0; y < h; y++)
		for (var x = 0; x < w; x++)
		{
			float sum = 0;
			var count = 0;
			for (var dy = -1; dy <= 1; dy++)
			for (var dx = -1; dx <= 1; dx++)
				if (x + dx >= 0 && y + dy >= 0 && x + dx < w && y + dy < h && (dx != 0 || dy != 0))
				{
					sum += Level(x + dx, y + dy);
					count++;
				}
			_glow[x, y] = 0.6f * Level(x, y) + 0.4f * sum / count;
		}
		foreach (var (cell, item) in _lit)
			item.Modulate = Shade(cell);
	}

	private static readonly Color Night = new(0.22f, 0.25f, 0.42f);

	private Color Shade(GridPos cell) =>
		cell.X >= 0 && cell.Y >= 0 && cell.X < _glow.GetLength(0) && cell.Y < _glow.GetLength(1)
			? Night.Lerp(Colors.White, _glow[cell.X, cell.Y])
			: Colors.White;

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
		var map = _view.Map;
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
		var floor = new Node2D { ZIndex = -10 };
		AddChild(floor);
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
				_ => map.Zones.TryGetValue(c, out var zone) && zone == MappedVillageScenario.Ids.Inn ? InnFloor : Yard,
			};
			var shade = (x + y) % 2 == 0 ? 0f : 0.05f;
			var tile = new Polygon2D { Polygon = Diamond(), Position = CellToScreen(cell), Color = color.Darkened(shade) };
			floor.AddChild(tile);
			_lit[cell] = tile;
			if (c == '#')
			{
				var wall = MakeBlock(cell, Wall, WallHeight);
				_sorted.AddChild(wall);
				_lit[cell] = wall; // the floor under a wall is hidden anyway
			}
		}

		foreach (var store in _sim.GetWorldView().Stores.Where(s => s.Position is not null))
		{
			var block = MakeBlock(store.Position!.Value, new Color(0.65f, 0.45f, 0.30f), 34f);
			var label = new Label { Text = store.Name, Position = new Vector2(-50, -70) };
			label.AddThemeFontSizeOverride("font_size", 12);
			label.AddThemeConstantOverride("outline_size", 4);
			label.AddThemeColorOverride("font_outline_color", Colors.Black);
			block.AddChild(label);
			_storeLabels[store.Id] = label;
			_sorted.AddChild(block);
			_lit[store.Position.Value] = block;
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
		if (!player)
		{
			var label = new Label { Text = name, Position = new Vector2(-30, -52), Size = new Vector2(60, 0), HorizontalAlignment = HorizontalAlignment.Center };
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

		_stop = new Button { Text = "Fermati (Spazio)" };
		_stop.Pressed += Stop;
		box.AddChild(_stop);

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
	}
}
