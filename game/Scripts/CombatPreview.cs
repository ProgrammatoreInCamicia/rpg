using Godot;

namespace RpgSandbox.Game;

/// <summary>
/// C2 interface sketch with sample data. It has no SimulationSession and defines no combat API.
/// Open Scenes/CombatPreview.tscn explicitly to review reach, target selection, wounds and the dice log.
/// </summary>
public partial class CombatPreview : Node2D
{
	private const int Width = 9;
	private const int Height = 7;
	private const float TileW = 64f;
	private const float TileH = 32f;
	private static readonly Vector2 Origin = new(605, 145);
	private static readonly HashSet<Vector2I> Walls = new()
	{
		new(4, 0), new(4, 1), new(4, 2), new(4, 5), new(4, 6), new(7, 2),
	};
	private readonly Dictionary<Vector2I, Enemy> _enemies = new()
	{
		[new Vector2I(3, 3)] = new("Bandito", 11, 11),
		[new Vector2I(6, 4)] = new("Predone", 11, 11),
	};
	private readonly List<string> _dice = new()
	{
		"Round 1 · Turno del Paladino",
		"Seleziona una casella verde o un bersaglio rosso.",
	};
	private readonly int[] _sampleD20 = { 14, 7, 20, 12 };
	private Vector2I _player = new(2, 3);
	private Vector2I? _selected;
	private int _moveLeft = 6;
	private int _rollIndex;
	private int _round = 1;
	private bool _actionAvailable = true;
	private Label _roundLabel = null!;
	private Label _selectionLabel = null!;
	private Label _enemyLabel = null!;
	private RichTextLabel _log = null!;
	private Button _attack = null!;

	private sealed class Enemy(string name, int hp, int max)
	{
		public string Name { get; } = name;
		public int Hp { get; set; } = hp;
		public int Max { get; } = max;
		// The mock uses the same descriptive names and thresholds as C1 Vitality.Describe().
		public string Condition => Hp == 0 ? "morto"
			: Hp == Max ? "illeso"
			: Hp * 2 > Max ? "ferito" : "malconcio";
	}

	public override async void _Ready()
	{
		BuildUi();
		Refresh();
		var smoke = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--combat-preview-smoke="));
		if (smoke is null)
			return;
		try
		{
			await RunSmoke(smoke["--combat-preview-smoke=".Length..]);
			GD.Print("COMBAT PREVIEW SMOKE OK");
			GetTree().Quit(0);
		}
		catch (Exception e)
		{
			GD.PrintErr($"COMBAT PREVIEW SMOKE FAIL: {e}");
			GetTree().Quit(1);
		}
	}

	public override void _Draw()
	{
		var reachable = ReachableDistances();
		for (var y = 0; y < Height; y++)
		for (var x = 0; x < Width; x++)
		{
			var cell = new Vector2I(x, y);
			var center = ScreenOf(cell);
			var color = Walls.Contains(cell) ? new Color("353940")
				: reachable.ContainsKey(cell) && cell != _player ? new Color("3f775b") : new Color("6c6755");
			DrawColoredPolygon(Diamond(center), color);
			DrawPolyline(new[] { center + new Vector2(0, -16), center + new Vector2(32, 0),
				center + new Vector2(0, 16), center + new Vector2(-32, 0), center + new Vector2(0, -16) },
				new Color("a9a08a"), 1f);
			if (Walls.Contains(cell))
				DrawRect(new Rect2(center + new Vector2(-14, -23), new Vector2(28, 22)), new Color("4a4c50"));
		}
		DrawCircle(ScreenOf(_player) + new Vector2(0, -14), 12, new Color("4c8df2"));
		foreach (var (cell, enemy) in _enemies)
			if (enemy.Hp > 0)
			{
				DrawCircle(ScreenOf(cell) + new Vector2(0, -14), 12,
					_selected == cell ? new Color("ffc857") : new Color("d36b61"));
				if (_selected == cell)
					DrawArc(ScreenOf(cell) + new Vector2(0, -14), 17, 0, Mathf.Tau, 32, new Color("ffe5a0"), 2);
			}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse)
			return;
		var point = GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position;
		for (var y = Height - 1; y >= 0; y--)
		for (var x = Width - 1; x >= 0; x--)
		{
			var cell = new Vector2I(x, y);
			var delta = point - ScreenOf(cell);
			if (Math.Abs(delta.X) / (TileW / 2) + Math.Abs(delta.Y) / (TileH / 2) > 1)
				continue;
			ClickCell(cell);
			return;
		}
	}

	private void ClickCell(Vector2I cell)
	{
		if (_enemies.TryGetValue(cell, out var enemy) && enemy.Hp > 0)
		{
			_selected = cell;
			AddLog($"Bersaglio: {enemy.Name} ({enemy.Condition.ToLowerInvariant()}).");
		}
		else if (ReachableDistances().TryGetValue(cell, out var distance) && cell != _player)
		{
			_moveLeft -= distance;
			_player = cell;
			_selected = null;
			AddLog($"Ti sposti. Movimento rimasto: {_moveLeft} caselle.");
		}
		Refresh();
	}

	private Dictionary<Vector2I, int> ReachableDistances()
	{
		var distance = new Dictionary<Vector2I, int> { [_player] = 0 };
		var frontier = new Queue<Vector2I>();
		frontier.Enqueue(_player);
		while (frontier.Count > 0)
		{
			var here = frontier.Dequeue();
			if (distance[here] >= _moveLeft)
				continue;
			for (var dy = -1; dy <= 1; dy++)
			for (var dx = -1; dx <= 1; dx++)
			{
				if (dx == 0 && dy == 0) continue;
				var next = here + new Vector2I(dx, dy);
				if (!Free(next) || distance.ContainsKey(next)) continue;
				if (dx != 0 && dy != 0 && (!Free(here + new Vector2I(dx, 0)) || !Free(here + new Vector2I(0, dy))))
					continue;
				distance[next] = distance[here] + 1;
				frontier.Enqueue(next);
			}
		}
		return distance;
	}

	private bool Free(Vector2I cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height
		&& !Walls.Contains(cell) && (!_enemies.TryGetValue(cell, out var enemy) || enemy.Hp == 0);

	private void AttackSelected()
	{
		if (_selected is not { } cell || !_enemies.TryGetValue(cell, out var enemy) || enemy.Hp == 0 || !_actionAvailable)
			return;
		if (Math.Max(Math.Abs(cell.X - _player.X), Math.Abs(cell.Y - _player.Y)) > 1)
		{
			AddLog($"{enemy.Name} è fuori portata della spada.");
			return;
		}
		var die = _sampleD20[_rollIndex++ % _sampleD20.Length];
		var hit = die == 20 || die != 1 && die + 4 >= 12;
		var damage = die == 20 ? 8 : 5;
		AddLog($"Spada lunga: d20 {die} + 4 = {die + 4} contro CA 12: {(hit ? "colpito" : "mancato")}.");
		if (hit)
		{
			enemy.Hp = Math.Max(0, enemy.Hp - damage);
			AddLog($"Danno dimostrativo: {damage}. {enemy.Name}: {enemy.Condition.ToLowerInvariant()}.");
		}
		_actionAvailable = false;
		Refresh();
	}

	private void EndTurn()
	{
		_round++;
		_moveLeft = 6;
		_actionAvailable = true;
		AddLog($"Round {_round} · Turno del Paladino (anteprima: nessuna IA). ");
		Refresh();
	}

	private void AddLog(string line)
	{
		_dice.Add(line);
		_log.Text = string.Join("\n", _dice.TakeLast(14));
	}

	private void Refresh()
	{
		_roundLabel.Text = $"Round {_round} · Paladino 11/11 PF\nMovimento: {_moveLeft} caselle · Azione: {(_actionAvailable ? "disponibile" : "usata")}";
		_selectionLabel.Text = _selected is { } cell && _enemies.TryGetValue(cell, out var enemy)
			? $"Selezionato: {enemy.Name}\nStato: {UpperFirst(enemy.Condition)}" : "Seleziona un bersaglio rosso.";
		_enemyLabel.Text = string.Join("\n", _enemies.Values.Select(e => $"{e.Name}: {UpperFirst(e.Condition)}"));
		_attack.Disabled = _selected is null || !_actionAvailable;
		QueueRedraw();
	}

	private void BuildUi()
	{
		var layer = new CanvasLayer();
		AddChild(layer);
		var left = new PanelContainer { OffsetLeft = 16, OffsetTop = 16, OffsetRight = 310, OffsetBottom = 320 };
		layer.AddChild(left);
		var controls = new VBoxContainer();
		controls.AddThemeConstantOverride("separation", 12);
		left.AddChild(controls);
		controls.AddChild(new Label { Text = "ANTEPRIMA C2 · DATI FINTI" });
		_roundLabel = new Label();
		controls.AddChild(_roundLabel);
		controls.AddChild(new Label { Text = "Verde: caselle raggiungibili\nRosso: bersagli cliccabili\nQuesta scena non usa la Sim." });
		_selectionLabel = new Label();
		controls.AddChild(_selectionLabel);
		_enemyLabel = new Label();
		controls.AddChild(_enemyLabel);
		_attack = new Button { Text = "Attacca con spada lunga" };
		_attack.Pressed += AttackSelected;
		controls.AddChild(_attack);
		var end = new Button { Text = "Termina turno" };
		end.Pressed += EndTurn;
		controls.AddChild(end);
		var right = new PanelContainer { OffsetLeft = 955, OffsetTop = 16, OffsetRight = 1264, OffsetBottom = 500 };
		layer.AddChild(right);
		var logBox = new VBoxContainer();
		right.AddChild(logBox);
		logBox.AddChild(new Label { Text = "REGISTRO DEI DADI · ESEMPIO" });
		_log = new RichTextLabel { CustomMinimumSize = new Vector2(285, 410), ScrollActive = true };
		logBox.AddChild(_log);
		_log.Text = string.Join("\n", _dice);
	}

	private static Vector2 ScreenOf(Vector2I cell) => Origin + new Vector2((cell.X - cell.Y) * TileW / 2,
		(cell.X + cell.Y) * TileH / 2);

	private static string UpperFirst(string value) => char.ToUpperInvariant(value[0]) + value[1..];

	private static Vector2[] Diamond(Vector2 c) =>
		new[] { c + new Vector2(0, -16), c + new Vector2(32, 0), c + new Vector2(0, 16), c + new Vector2(-32, 0) };

	private async Task RunSmoke(string directory)
	{
		if (DirAccess.MakeDirRecursiveAbsolute(directory) != Error.Ok)
			throw new IOException($"Cannot create {directory}");
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		var cell = new Vector2I(3, 3);
		var point = ScreenOf(cell);
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
			Position = point, GlobalPosition = point });
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (_selected != cell || _selectionLabel.Text.Contains("11/11"))
			throw new InvalidOperationException("The target was not selected or its exact HP leaked");
		AttackSelected();
		if (!_dice.Any(d => d.Contains("d20 14 + 4 = 18")) || _enemies[cell].Condition != "ferito" || _moveLeft != 6)
			throw new InvalidOperationException("Attack or descriptive condition did not update");
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		var file = Path.Combine(directory, "combat-preview.png");
		if (GetViewport().GetTexture().GetImage().SavePng(file) != Error.Ok)
			throw new IOException($"Cannot save {file}");
		GD.Print($"COMBAT PREVIEW capture {file}");
	}
}
