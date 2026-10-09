using Godot;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>
/// Development aid, enabled only by the `--smoke=&lt;dir&gt;` user argument: drives a trip through the
/// real UI path and saves screenshots before, during and after it, then quits.
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
		Capture("1-start.png");

		// Go through the real input path: move the mouse over the granary and click.
		var target = _main.GetViewport().GetCanvasTransform() * _main.ScreenPointOf(SliceScenario.Ids.Granary);
		Input.WarpMouse(target);
		await Frames(3);
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = target, GlobalPosition = target });
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = target, GlobalPosition = target });
		await Seconds(1.0);
		Capture("2-travelling.png");

		while (_main.IsBusy)
			await Frames(1);
		await Frames(2);
		Capture("3-arrived.png");

		_main.DepositViaUi(4);
		await Frames(5);
		Capture("4-depositing.png");
		while (_main.IsBusy)
			await Frames(1);
		await Frames(2);
		Capture("5-deposited.png");

		GD.Print("SMOKE OK");
		GetTree().Quit();
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

	private async Task Seconds(double seconds) =>
		await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
