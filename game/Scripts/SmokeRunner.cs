using Godot;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>
/// Development aid, enabled only by the `--smoke=&lt;dir&gt;` user argument: plays a scripted session
/// through the real UI paths, saves screenshots along the way, then quits.
/// Story: the player watches the granary, sees the raider steal, tells the guard, and the next raid fails.
/// </summary>
public partial class SmokeRunner : Node
{
	private readonly Main _main;
	private readonly string _outputDir;

	public SmokeRunner() : this(null!, "") { } // required by Godot for script instantiation

	public SmokeRunner(Main main, string outputDir)
	{
		_main = main;
		_outputDir = outputDir;
	}

	public override async void _Ready()
	{
		DirAccess.MakeDirRecursiveAbsolute(_outputDir);

		await Frames(10);
		_main.SetDebug(false);
		await Frames(2);
		Capture("1-start-player-view.png");

		// Go through the real input path: move the mouse over the granary and click.
		var target = _main.GetViewport().GetCanvasTransform() * _main.ScreenPointOf(SliceScenario.Ids.Granary);
		Input.WarpMouse(target);
		await Frames(3);
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = target, GlobalPosition = target });
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = target, GlobalPosition = target });
		await UntilIdle();

		// Watch the granary through the morning: the raid happens at 09:30–09:33.
		for (var hour = 0; hour < 10; hour++)
		{
			_main.WaitOneHour();
			await UntilIdle();
		}
		Capture("2-witnessed-player-view.png");

		_main.TravelTo(SliceScenario.Ids.Inn);
		await UntilIdle();
		Capture("3-at-inn-report-options.png");

		if (!_main.ReportViaUi(SliceScenario.Ids.Guard))
			GD.PrintErr("SMOKE FAIL: no report option for the guard");
		await UntilIdle();
		_main.SetDebug(true);
		await Frames(2);
		Capture("4-guard-reacts-debug.png");

		// Day 3: the bandits are hungry again, but the granary is guarded.
		for (var i = 0; i < 8; i++)
		{
			_main.SkipHours(6);
			await Frames(1);
		}
		await Frames(2);
		Capture("5-day3-raid-deterred-debug.png");

		GD.Print("SMOKE OK");
		GetTree().Quit();
	}

	private async Task UntilIdle()
	{
		await Frames(1);
		while (_main.IsBusy)
			await Frames(1);
		await Frames(2);
	}

	private void Capture(string name)
	{
		var path = System.IO.Path.Combine(_outputDir, name);
		var error = GetViewport().GetTexture().GetImage().SavePng(path);
		GD.Print($"SMOKE capture {path}: {error}");
	}

	private async Task Frames(int count)
	{
		for (var i = 0; i < count; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}
