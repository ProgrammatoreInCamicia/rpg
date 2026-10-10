using Godot;
using RpgSandbox.Sim;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
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

		// At night, nobody around: the paladin pockets one ration. He sees his own Stealth roll (with Disadvantage
		// from chain mail), never who might have noticed.
		Require(_main.PlayerState.Light == Light.Dark, "It should be night at 00:05");
		_main.TakeViaUi(1);
		Require(_main.IsBusy, "Take command was rejected");
		Require(_main.LastMessage.Contains("Furtività") && _main.LastMessage.Contains("svantaggio"), "Own stealth roll not shown");
		await UntilIdle();
		Require(_main.PlayerState.Food == 11, "The ration was not taken");
		_main.ShowSheet(true);
		await Frames(2);
		Capture("1b-night-theft-and-sheet.png");
		_main.ShowSheet(false);

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
		// Talk through the real input path: click on the farmer standing at the inn.
		var farmerPoint = _main.ScreenPointOfActor(SliceScenario.Ids.Farmer);
		Require(farmerPoint is not null, "Farmer is not drawn at the inn");
		var farmerScreen = _main.GetViewport().GetCanvasTransform() * farmerPoint!.Value;
		Input.WarpMouse(farmerScreen);
		await Frames(3);
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = farmerScreen, GlobalPosition = farmerScreen });
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = farmerScreen, GlobalPosition = farmerScreen });
		await Frames(3);
		Require(_main.TalkingTo == SliceScenario.Ids.Farmer, "Clicking the farmer did not open the conversation");
		Require(_main.PlayerState.PeopleHere.Single(p => p.Id == SliceScenario.Ids.Farmer).Topics.Count == 1, "Farmer has no topic to tell");
		Require(!_main.IsBusy, "Opening a conversation must not take time");
		Capture("3-at-inn-talk-to-farmer.png");

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
		Require(_main.OpenTalk(SliceScenario.Ids.Farmer), "Cannot talk to the farmer in the long session");
		Require(_main.PlayerState.PeopleHere.Single(p => p.Id == SliceScenario.Ids.Farmer).Topics.Count >= 5, "Too few topics for the farmer");
		await Frames(3);
		Require(_main.EssentialControlsVisible, "Reports pushed essential controls off screen");
		Capture("7-many-reports.png");
		_main.SetDebug(true);
		GetWindow().Size = new Vector2I(1024, 600);
		await Frames(5);
		Require(_main.EssentialControlsVisible, "Essential controls are clipped after resize with debug on");
		Capture("8-resized-debug.png");

		// Two sessions can reuse the same observation IDs for different facts. Loading one while a conversation is
		// open must discard its old controls and their captured topics.
		var first = ConversationFixture(0);
		var second = ConversationFixture(37);
		LoadFixture(first, "conversation-eight.json");
		Require(_main.OpenTalk(SliceScenario.Ids.Farmer), "Cannot open first conversation");
		Require(_main.TalkTopicTexts.Any(t => t.Contains("8 razioni")), "First conversation lacks the eight-ration topic");
		LoadFixture(second, "conversation-three.json");
		Require(_main.TalkingTo is null, "Loading a game kept the previous conversation open");
		Require(_main.OpenTalk(SliceScenario.Ids.Farmer), "Cannot open the loaded conversation");
		_main.SetDebug(false);
		await Frames(2); // queued controls from the previous conversation are freed at frame end
		Require(_main.TalkTopicTexts.Any(t => t.Contains("3 razioni")) &&
			!_main.TalkTopicTexts.Any(t => t.Contains("8 razioni")), "Loaded conversation shows stale topics");
		Capture("9-conversation-after-load.png");

		// A first-hand observation made through hearing must say so in the journal. Nobody in the dark
		// appears as an identified talk target, although the player can sense a nearby presence.
		SimulationSession? night = null;
		for (ulong seed = 0; seed < 40; seed++)
		{
			var candidate = NightRaid(seed);
			if (candidate.GetPlayerView().Observations.Any(o => o.Perceived == "Heard"))
			{
				night = candidate;
				break;
			}
		}
		Require(night is not null, "No night raid was heard in the fixed seed set");
		LoadFixture(night!, "heard-at-night.json");
		_main.SetDebug(false);
		Require(_main.JournalText.Contains("l'hai sentito tu") && !_main.JournalText.Contains("l'hai visto tu"),
			"The journal presents a heard theft as something seen");
		await Frames(2);
		Capture("10-heard-at-night.png");

		var unseen = NightRaid(0, minute: 5); // farmer is still at the unlit granary
		LoadFixture(unseen, "unseen-nearby.json");
		Require(_main.PlayerState.UnseenNearby > 0 && _main.UnseenText.Contains("troppo buio"),
			"An unseen nearby person has no explanation in the client");
		await Frames(2);
		Capture("11-unseen-nearby.png");
	}

	private static SimulationSession ConversationFixture(int takenBeforeRaid)
	{
		var session = SimulationSession.Create(SliceScenario.Create());
		var trip = session.Execute(new TravelCommand { Actor = session.Player, Destination = SliceScenario.Ids.Granary });
		Require(trip.Success, "Cannot reach granary for conversation fixture");
		session.AdvanceUntilCompleted(trip.Action!.Value, Duration.FromHours(1));
		if (takenBeforeRaid > 0)
		{
			var take = session.Execute(new TakeFoodCommand
				{ Actor = session.Player, Store = SliceScenario.Ids.GranaryStore, Amount = takenBeforeRaid });
			Require(take.Success, "Cannot empty granary for conversation fixture");
			session.AdvanceUntilCompleted(take.Action!.Value, Duration.FromHours(1));
		}
		session.Advance(Duration.FromSeconds(9 * 3600 + 34 * 60 - session.Now.Seconds));
		var back = session.Execute(new TravelCommand { Actor = session.Player, Destination = SliceScenario.Ids.Inn });
		Require(back.Success, "Cannot return to inn for conversation fixture");
		session.AdvanceUntilCompleted(back.Action!.Value, Duration.FromHours(1));
		Require(session.GetPlayerView().Observations.Any(o => o.Amount == 8 - Math.Min(takenBeforeRaid, 5)),
			"Conversation fixture did not witness the expected raid");
		return session;
	}

	private static SimulationSession NightRaid(ulong seed, int minute = 94)
	{
		var scenario = new ScenarioBuilder()
			.WithSeed(seed)
			.AddArea(SliceScenario.Ids.Village.Value, "Villaggio").AddArea(SliceScenario.Ids.Forest.Value, "Bosco")
			.AddLocation(SliceScenario.Ids.Inn.Value, "Locanda", SliceScenario.Ids.Village.Value, lit: true)
			.AddLocation(SliceScenario.Ids.Granary.Value, "Granaio", SliceScenario.Ids.Village.Value)
			.AddLocation(SliceScenario.Ids.BanditCamp.Value, "Campo dei banditi", SliceScenario.Ids.Forest.Value)
			.AddRoute(SliceScenario.Ids.Inn.Value, SliceScenario.Ids.Granary.Value, Duration.FromMinutes(5))
			.AddRoute(SliceScenario.Ids.Granary.Value, SliceScenario.Ids.BanditCamp.Value, Duration.FromMinutes(30))
			.AddFaction(SliceScenario.Ids.VillageFaction.Value, "Villaggio", homeStoreId: SliceScenario.Ids.GranaryStore.Value, authorityId: SliceScenario.Ids.Guard.Value)
			.AddRaidingFaction(SliceScenario.Ids.Bandits.Value, "Banditi", SliceScenario.Ids.CampStore.Value, 6, Duration.FromMinutes(30), 10, 8, Duration.FromHours(1))
			.AddStore(SliceScenario.Ids.GranaryStore.Value, "Scorte del granaio", SliceScenario.Ids.Granary.Value, 40, SliceScenario.Ids.VillageFaction.Value)
			.AddStore(SliceScenario.Ids.CampStore.Value, "Scorte del campo", SliceScenario.Ids.BanditCamp.Value, 14, SliceScenario.Ids.Bandits.Value)
			.AddActor(SliceScenario.Ids.Player.Value, "Protagonista", SliceScenario.Ids.Granary.Value, isPlayer: true, sheet: Sheets.Paladin())
			.AddActor(SliceScenario.Ids.Raider.Value, "Razziatore", SliceScenario.Ids.BanditCamp.Value, factionId: SliceScenario.Ids.Bandits.Value, sheet: Sheets.Raider())
			.AddActor(SliceScenario.Ids.Farmer.Value, "Contadino", SliceScenario.Ids.Granary.Value, factionId: SliceScenario.Ids.VillageFaction.Value, sheet: Sheets.Farmer(),
				workLocationId: SliceScenario.Ids.Granary.Value, shiftStart: Duration.Zero, shiftEnd: Duration.FromHours(6))
			.AddActor(SliceScenario.Ids.Guard.Value, "Guardia", SliceScenario.Ids.Inn.Value, factionId: SliceScenario.Ids.VillageFaction.Value, sheet: Sheets.VillageGuard())
			.Build();
		var session = SimulationSession.Create(scenario);
		session.Advance(Duration.FromMinutes(minute));
		return session;
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
