using Godot;
using RpgSandbox.Sim.Api;
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
		try
		{
			await Run();
			GD.Print("SMOKE OK");
			GetTree().Quit(0);
		}
		catch (Exception e)
		{
			GD.PrintErr($"SMOKE FAIL: {e}");
			GetTree().Quit(1);
		}
	}

	private async Task Run()
	{
		Require(DirAccess.MakeDirRecursiveAbsolute(_outputDir) == Error.Ok, "Cannot create screenshot directory");

		await Frames(10);
		Require(!_main.DebugEnabled, "Debug must be off at startup");
		// Used to verify that a failed assertion produces exit 1 and never SMOKE OK.
		Require(!OS.GetCmdlineUserArgs().Contains("--smoke-force-failure"), "Injected smoke failure");
		await Frames(2);
		Capture("1-start-player-view.png");

		// Go through the real input path: move the mouse over the granary and click.
		var target = _main.GetViewport().GetCanvasTransform() * _main.ScreenPointOf(SliceScenario.Ids.Granary);
		Input.WarpMouse(target);
		await Frames(3);
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = target, GlobalPosition = target }, true);
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = target, GlobalPosition = target }, true);
		Require(_main.IsBusy, "Map click did not start travel");
		await UntilIdle();
		Require(_main.PlayerState.Location == SliceScenario.Ids.Granary, "Player did not arrive at granary");

		// Watch the granary through the morning: the raid happens at 09:30–09:33.
		for (var hour = 0; hour < 10; hour++)
		{
			_main.WaitOneHour();
			Require(_main.IsBusy, "Wait command was rejected");
			await UntilIdle();
		}
		Capture("2-witnessed-player-view.png");
		Require(_main.PlayerState.Observations.Any(o => o.Thief == SliceScenario.Ids.Raider), "Theft was not witnessed and identified");

		_main.TravelTo(SliceScenario.Ids.Inn);
		await UntilIdle();
		Require(_main.PlayerState.Location == SliceScenario.Ids.Inn, "Player did not return to inn");
		Capture("3-at-inn-report-options.png");

		Require(_main.ReportViaUi(SliceScenario.Ids.Guard) && _main.IsBusy, "No accepted report to guard");
		await UntilIdle();
		Require(_main.WorldState.Actors.Single(a => a.Id == SliceScenario.Ids.Guard).GuardDuty is not null, "Guard did not react to report");
		var protectedFood = _main.WorldState.Stores.Single(s => s.Id == SliceScenario.Ids.GranaryStore).Food;
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
		Require(_main.WorldState.Stores.Single(s => s.Id == SliceScenario.Ids.GranaryStore).Food == protectedFood, "Guarded granary lost food");
		Require(_main.WorldState.Factions.Single(f => f.Id == SliceScenario.Ids.Bandits).AvoidedTargets.Count > 0, "Raid was not deterred");

		// Load through the same path as the button, using a separate fixture: never touch user://save.json.
		var session = SimulationSession.Create(SliceScenario.Create());
		var trip = session.Execute(new TravelCommand { Actor = session.Player, Destination = SliceScenario.Ids.Granary });
		Require(trip.Success, "Cannot create travel fixture");
		session.Advance(Duration.FromSeconds(120));
		LoadFixture(session, "mid-travel.json");
		Require(_main.IsBusy && _main.PlayerState.Travel is not null, "Loaded travel was not resumed");
		await UntilIdle();
		Require(_main.PlayerState.Location == SliceScenario.Ids.Granary && _main.PlayerState.Now == trip.CompletesAt,
			"Loaded travel did not stop at its arrival time");
		_main.WaitOneHour();
		Require(_main.IsBusy, "Commands remain blocked after loaded travel");
		await UntilIdle();
		Capture("6-loaded-travel-completed.png");

		// Accumulate real observations, then return when both recipients are at the inn.
		session.Advance(Duration.FromSeconds((9 * 24 + 19) * 3600 - session.Now.Seconds));
		var back = session.Execute(new TravelCommand { Actor = session.Player, Destination = SliceScenario.Ids.Inn });
		Require(back.Success, "Cannot create long-session fixture");
		session.AdvanceUntilCompleted(back.Action!.Value, Duration.FromHours(1));
		LoadFixture(session, "many-reports.json");
		_main.SetDebug(false);
		await Frames(5);
		Require(_main.PlayerState.ReportOptions.Count >= 10, "Long-session fixture has too few report options");
		Require(_main.EssentialControlsVisible, "Reports pushed essential controls off screen");
		Capture("7-many-reports.png");
		_main.SetDebug(true);
		GetWindow().Size = new Vector2I(1024, 600);
		await Frames(5);
		Require(_main.EssentialControlsVisible, "Essential controls are clipped after resize with debug on");
		Capture("8-resized-debug.png");
	}

	private void LoadFixture(SimulationSession session, string name)
	{
		var path = System.IO.Path.Combine(_outputDir, name);
		using (var stream = File.Create(path))
			session.Save(stream);
		_main.LoadGameFromPath(path);
		Require(_main.PlayerState.Now == session.Now, "Fixture load failed");
	}

	private static void Require(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private async Task UntilIdle()
	{
		await Frames(1);
		var deadline = Time.GetTicksMsec() + 15000;
		while (_main.IsBusy)
		{
			Require(Time.GetTicksMsec() < deadline, "Timed out waiting for player action");
			await Frames(1);
		}
		await Frames(2);
	}

	private void Capture(string name)
	{
		var path = System.IO.Path.Combine(_outputDir, name);
		var error = GetViewport().GetTexture().GetImage().SavePng(path);
		Require(error == Error.Ok, $"Cannot save screenshot {path}: {error}");
		GD.Print($"SMOKE capture {path}: {error}");
	}

	private async Task Frames(int count)
	{
		for (var i = 0; i < count; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}
