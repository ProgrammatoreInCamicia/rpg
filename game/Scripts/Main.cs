using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>
/// Checkpoint 0 client: isometric placeholder map, click a place to travel, clock and arrival visible.
/// All rules live in Sim; this node only sends commands, advances time while an action plays out,
/// and draws the current <see cref="WorldView"/>.
/// </summary>
public partial class Main : Node2D
{
    // Presentation-only placement of simulation places on the isometric grid.
    private static readonly Dictionary<LocationId, Vector2> LocationCells = new()
    {
        [SliceScenario.Ids.Inn] = new Vector2(3, 3),
        [SliceScenario.Ids.Granary] = new Vector2(8, 4),
    };

    private const int GridWidth = 12;
    private const int GridHeight = 9;

    /// <summary>Playback speed while an action is running: a 5-minute walk takes 2 real seconds.</summary>
    private const double GameSecondsPerRealSecond = 150;

    private static readonly Color GroundA = new(0.30f, 0.42f, 0.26f);
    private static readonly Color GroundB = new(0.27f, 0.38f, 0.23f);
    private static readonly Color Platform = new(0.55f, 0.48f, 0.36f);
    private static readonly Color PlatformHover = new(0.75f, 0.66f, 0.42f);
    private static readonly Color RouteColor = new(0.85f, 0.78f, 0.55f, 0.8f);

    private SimulationSession _sim = null!;
    private WorldView _view = null!;

    private Node2D _sorted = null!; // Y-sorted layer: buildings and characters
    private Node2D _playerToken = null!;
    private Camera2D _camera = null!;
    private readonly Dictionary<LocationId, Polygon2D> _platforms = new();

    private Label _clockLabel = null!;
    private Label _statusLabel = null!;
    private Label _factsLabel = null!;
    private Button _waitButton = null!;

    private ActionId? _runningAction;
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
        BuildPlayer();
        BuildCamera();
        BuildUi();
        Refresh();

        // Development aid: `godot --path game -- --smoke=<dir>` plays a scripted trip and saves screenshots.
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

        _pendingGameSeconds += delta * GameSecondsPerRealSecond;
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

    public bool IsBusy => _runningAction is not null;

    public void TravelTo(LocationId destination)
    {
        if (_runningAction is not null)
            return;

        var result = _sim.Execute(new TravelCommand { Actor = _sim.Player, Destination = destination });
        if (result.Success)
            _runningAction = result.Action;
        Refresh(result.Success ? null : result.Message);
    }

    private void Wait()
    {
        if (_runningAction is not null)
            return;
        _sim.Advance(Duration.FromHours(1));
        Refresh();
    }

    // ---------------------------------------------------------------- view

    private void Refresh(string? rejection = null)
    {
        _view = _sim.GetWorldView();
        var player = _view.Actors.Single(a => a.Id == _view.Player);

        _playerToken.Position = PlayerScreenPosition(player);

        var now = _view.Now;
        _clockLabel.Text = $"Giorno {now.Day + 1}   {now.Hour:00}:{now.Minute:00}";

        if (player.Travel is { } travel)
        {
            _statusLabel.Text = $"In viaggio verso {LocationName(travel.Destination)} — arrivo alle " +
                                $"{travel.ArrivesAt.Hour:00}:{travel.ArrivesAt.Minute:00}";
        }
        else
        {
            _statusLabel.Text = $"Ti trovi a: {LocationName(player.Location!.Value)}.\nClicca un luogo per viaggiare.";
        }
        if (rejection is not null)
            _statusLabel.Text += $"\n⚠ {rejection}";

        _waitButton.Disabled = _runningAction is not null;

        var facts = _view.RecentFacts.TakeLast(8)
            .Select(f => $"[{f.At.Hour:00}:{f.At.Minute:00}] {f.Description}");
        _factsLabel.Text = string.Join("\n", facts);
    }

    private Vector2 PlayerScreenPosition(ActorView player)
    {
        if (player.Location is { } here)
            return StandPoint(here);

        // Travelling: the simulation says the actor is at neither end; interpolate for display only.
        var travel = player.Travel!;
        var total = travel.ArrivesAt.Since(travel.DepartedAt).Seconds;
        var progress = total > 0 ? (float)_view.Now.Since(travel.DepartedAt).Seconds / total : 1f;
        return StandPoint(travel.Origin).Lerp(StandPoint(travel.Destination), Mathf.Clamp(progress, 0f, 1f));
    }

    /// <summary>Where characters stand at a place: in front of its building.</summary>
    private static Vector2 StandPoint(LocationId location) =>
        Iso.CellToScreen(LocationCells[location] + new Vector2(0.9f, 0.9f));

    private string LocationName(LocationId id) => _view.Locations.Single(l => l.Id == id).Name;

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
        for (var x = 0; x < GridWidth; x++)
        for (var y = 0; y < GridHeight; y++)
        {
            ground.AddChild(new Polygon2D
            {
                Polygon = Iso.Diamond(),
                Position = Iso.CellToScreen(new Vector2(x, y)),
                Color = (x + y) % 2 == 0 ? GroundA : GroundB,
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
            building.AddChild(MakeLabel(location.Name, new Vector2(-40, -130), 18));
            _sorted.AddChild(building);
        }
    }

    private void BuildPlayer()
    {
        _playerToken = new Node2D();
        // Placeholder figure: a shadow, a body and a head, with the origin at the feet.
        _playerToken.AddChild(new Polygon2D { Polygon = Iso.Diamond(0.3f), Color = new Color(0, 0, 0, 0.35f) });
        _playerToken.AddChild(new Polygon2D
        {
            Polygon = new[] { new Vector2(-9, 0), new Vector2(9, 0), new Vector2(7, -34), new Vector2(-7, -34) },
            Color = new Color(0.25f, 0.45f, 0.85f),
        });
        _playerToken.AddChild(new Polygon2D { Polygon = Circle(8, 12), Position = new Vector2(0, -42), Color = new Color(0.95f, 0.82f, 0.68f) });
        _sorted.AddChild(_playerToken);
    }

    private void BuildCamera()
    {
        var mapCenter = Iso.CellToScreen(new Vector2(GridWidth / 2f, GridHeight / 2f));
        _camera = new Camera2D { Position = mapCenter };
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

        var panel = new PanelContainer { Position = new Vector2(16, 16) };
        ui.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        _clockLabel = new Label();
        _clockLabel.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(_clockLabel);

        _statusLabel = new Label { CustomMinimumSize = new Vector2(360, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_statusLabel);

        _waitButton = new Button { Text = "Attendi 1 ora" };
        _waitButton.Pressed += Wait;
        box.AddChild(_waitButton);

        box.AddChild(new HSeparator());
        var title = new Label { Text = "Cronaca (debug)" };
        title.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(title);
        _factsLabel = new Label();
        _factsLabel.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_factsLabel);
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
