using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>
/// Development aid, enabled by `--village-smoke=&lt;dir&gt;`: plays the village loop through the real UI paths
/// (click, walk, stop, reach the store, deposit, walk up to the guard) and fails with exit code 1 on any problem.
/// </summary>
public partial class VillageSmoke : Node
{
	private readonly VillageMap _map;
	private readonly string _outputDir;

	public VillageSmoke() : this(null!, "") { } // required by Godot for script instantiation

	public VillageSmoke(VillageMap map, string outputDir)
	{
		_map = map;
		_outputDir = outputDir;
	}

	public override async void _Ready()
	{
		try
		{
			await Run();
			GD.Print("VILLAGE SMOKE OK");
			GetTree().Quit(0);
		}
		catch (Exception e)
		{
			GD.PrintErr($"VILLAGE SMOKE FAIL: {e}");
			GetTree().Quit(1);
		}
	}

	private async Task Run()
	{
		Require(DirAccess.MakeDirRecursiveAbsolute(_outputDir) == Error.Ok, "Cannot create screenshot directory");
		await Frames(10);
		Require(_map.View.Position == MappedVillageScenario.Ids.PlayerStart, "Player does not start at the inn");
		Capture("1-start.png");

		// Real time with pause: the world goes on while the player stands still.
		var t0 = _map.View.Now;
		await Seconds(1.0);
		Require(_map.View.Now > t0 && _map.View.Position == MappedVillageScenario.Ids.PlayerStart,
			"The clock should run while the player stands still");

		// Paused, the clock stops; an order given while paused (a real click) starts when the game resumes.
		_map.SetPaused(true);
		var pausedAt = _map.View.Now;
		var target = new GridPos(12, 3);
		var screen = _map.GetViewport().GetCanvasTransform() * VillageMap.ScreenPointOf(target);
		Input.WarpMouse(screen);
		await Frames(3);
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen, GlobalPosition = screen });
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen, GlobalPosition = screen });
		await Frames(2);
		Require(!_map.IsWalking && _map.QueuedOrders == 1, "A click while paused should wait in the queue, not start walking");
		_map.ToggleTorch();
		Require(_map.View.TorchLitUntil is null && _map.View.Torches == 3 && _map.QueuedOrders == 2,
			"A torch ordered while paused must not light (nor be spent) before the resume");
		_map.Stop(); // X while paused takes back the queued walk
		Require(_map.QueuedOrders == 1, "X should take back the queued walk");
		_map.MoveTo(target);
		await Seconds(1.0);
		Require(_map.View.Now == pausedAt && _map.View.Position == MappedVillageScenario.Ids.PlayerStart, "Time or position moved while paused");
		_map.SetPaused(false);
		Require(_map.IsWalking && _map.View.TorchLitUntil is not null && _map.View.Torches == 2 && _map.QueuedOrders == 0,
			"On resume the queued orders should run in order");
		_map.ToggleTorch(); // put it out: torchlight would spoil the sneaking below
		Require(_map.View.TorchLitUntil is null, "The torch did not go out");
		Require(_map.EventText.Contains("G1 00:") && _map.EventText.Contains("accende una torcia")
			&& _map.EventText.Contains("spegne la torcia"),
			"The Events panel did not show the player's torch events with game time");

		// Walk a little at the exploration pace (several game seconds per real second, rules unchanged), save, stop.
		await Seconds(2.5);
		Capture("2-walking.png");
		var pace = _map.Pace;
		var walked = _map.View.Now.Since(pausedAt).Seconds;
		Require(walked >= (long)(2 * pace) && walked <= (long)(3 * pace),
			$"After 2.5 real seconds at {pace}× the walk should have taken {2 * pace}–{3 * pace} s, not {walked}");
		var savedAt = _map.View.Now;
		var savedSquare = _map.View.Position;
		var walkingSave = System.IO.Path.Combine(_outputDir, "walking.json");
		_map.SaveGameToPath(walkingSave);
		Require(File.Exists(walkingSave), "Saving during a walk did not create a snapshot");
		_map.Stop();
		var stoppedAt = _map.View.Position;
		var stoppedTime = _map.View.Now;
		await Seconds(1.5);
		Require(!_map.IsBusy && _map.View.Position == stoppedAt, "The position moved after stopping");
		Require(_map.View.Now > stoppedTime, "The world should go on after stopping");
		_map.LoadGameFromPath(walkingSave);
		Require(_map.IsWalking && _map.View.Now == savedAt && _map.View.Position == savedSquare,
			"Loading the mid-walk save did not resume the walk at the saved square");
		await Seconds(1.2);
		Require(_map.View.Now > savedAt, "The loaded walk did not advance");
		_map.Stop();

		// Reach the square next to the granary store and deposit.
		_map.MoveTo(new GridPos(16, 5));
		await UntilIdle(40);
		Require(_map.View.Position == new GridPos(16, 5), "Did not reach the store");
		Require(_map.View.Location == MappedVillageScenario.Ids.Granary, "Not in the granary yard");
		_map.Deposit(2);
		await UntilIdle(10);
		Require(_map.View.Food == 8, $"Deposit failed: {_map.LastMessage}");
		Capture("3-deposited.png");

		// At night the inn lamp spills out of the door; the granary yard stays dark.
		var light = _map.View.MapLight!;
		Require(light[3][8] == '1' && light[6][17] == '0', "Lamplight should reach the road by the door, not the granary yard");

		// From the road in front of the door the guard is in sight: a real click on her figure (not on her square)
		// walks up to her and opens the conversation.
		var guard = MappedVillageScenario.Ids.Guard;
		_map.MoveTo(new GridPos(8, 3));
		await UntilIdle(40);
		Require(_map.View.VisibleActors.Any(a => a.Id == guard), "The guard should be visible through the door");
		Require(_map.View.PeopleHere.All(p => p.Id != guard), "The guard should not be next to you yet");
		await Frames(3);
		var body = _map.GetViewport().GetCanvasTransform() * _map.BodyPointOf(guard)!.Value;
		Input.WarpMouse(body);
		await Frames(3);
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = body, GlobalPosition = body });
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = body, GlobalPosition = body });
		await Frames(2);
		Require(_map.IsWalking, $"Clicking on the guard did not walk up to her: {_map.LastMessage}");
		await UntilIdle(40);
		Require(_map.View.PeopleHere.Any(p => p.Id == guard), "The guard should be within talking distance");
		Require(_map.TalkingTo == guard, $"The conversation did not open: {_map.LastMessage}");
		Capture("4-talking-to-guard.png");

		// Next to the guard you cannot hide (watched: slow, not hidden). Out on the dark road, unseen, you can: slow pace,
		// one Stealth check.
		_map.SetSneak(true);
		_map.MoveTo(new GridPos(4, 3));
		Require(_map.IsWalking && _map.View.Sneaking is null, "Watched by the guard, the player should not be able to hide");
		await UntilIdle(20);
		_map.SetSneak(false);
		_map.MoveTo(new GridPos(20, 8));
		await UntilIdle(40);
		_map.SetSneak(true);
		var before = _map.View.Now;
		_map.MoveTo(new GridPos(14, 2));
		Require(_map.IsWalking && _map.View.Move!.Stealthy, $"Sneaking did not start: {_map.LastMessage}");
		var squares = _map.View.Move!.Path.Count;
		await UntilIdle(40);
		Require(_map.View.Sneaking is not null, "The player should still be sneaking after the walk");
		Require(_map.View.Now.Since(before).Seconds >= squares * 3 / 2, "Sneaking should go at the slow pace");
		Capture("5-sneaking.png");
		_map.SetSneak(false);

		// From the road, a real click on the inn door closes it: the guard and the lamplight disappear behind it.
		_map.MoveTo(new GridPos(8, 3));
		await UntilIdle(40);
		Require(_map.View.VisibleActors.Any(a => a.Id == guard), "The guard should be visible through the open door");
		await Frames(3);
		var door = _map.GetViewport().GetCanvasTransform() * VillageMap.ScreenPointOf(MappedVillageScenario.Ids.InnDoor);
		Input.WarpMouse(door);
		await Frames(3);
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = door, GlobalPosition = door });
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = door, GlobalPosition = door });
		await Frames(3);
		Require(_map.View.Doors.Single(d => d.At == MappedVillageScenario.Ids.InnDoor).Open == false,
			$"Clicking the door did not close it: {_map.LastMessage}");
		Require(_map.EventText.Contains("chiude la porta"), "The Events panel did not show the door action");
		Require(_map.View.VisibleActors.All(a => a.Id != guard), "The guard should be hidden by the closed door");
		Require(_map.View.MapLight![3][9] == '0', "The lamplight should not reach the road with the door closed");
		Require(_map.DisplayGlowAt(new GridPos(8, 3)) == 0f,
			"The road beyond a closed door should stay visually dark, not inherit glow through the door");

		// A torch lights the dark road around the paladin.
		_map.ToggleTorch();
		Require(_map.View.TorchLitUntil is not null && _map.View.Torches == 1, $"The torch did not light: {_map.LastMessage}");
		Require(_map.View.MapLight![3][9] == '2', "The torch should light the squares around");
		await Frames(3);
		Capture("6-door-closed-torch.png");

		// Shift-click uses a door square as a walking destination, even from next to it.
		var doorNow = _map.GetViewport().GetCanvasTransform() * VillageMap.ScreenPointOf(MappedVillageScenario.Ids.InnDoor);
		Input.WarpMouse(doorNow);
		await Frames(3);
		doorNow = _map.GetViewport().GetCanvasTransform() * VillageMap.ScreenPointOf(MappedVillageScenario.Ids.InnDoor);
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, ShiftPressed = true,
			Pressed = true, Position = doorNow, GlobalPosition = doorNow });
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, ShiftPressed = true,
			Pressed = false, Position = doorNow, GlobalPosition = doorNow });
		Require((_map.IsWalking && _map.View.Move?.Path[^1] == MappedVillageScenario.Ids.InnDoor)
			|| _map.View.Position == MappedVillageScenario.Ids.InnDoor,
			$"Shift-clicking the adjacent door should walk onto it: {_map.LastMessage}");
		await UntilIdle(10);
		Require(_map.View.Position == MappedVillageScenario.Ids.InnDoor, "The player did not enter the door square");

		// G5: the exit marker starts a real Route, and the off-map camp offers the return journey.
		_map.MoveTo(new GridPos(21, 12));
		await UntilIdle(25);
		Capture("7-forest-road.png");
		var forestExit = MappedVillageScenario.Ids.ForestExit;
		var exitPoint = _map.GetViewport().GetCanvasTransform() * VillageMap.ScreenPointOf(forestExit);
		Input.WarpMouse(exitPoint);
		await Frames(3);
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
			Position = exitPoint, GlobalPosition = exitPoint });
		Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false,
			Position = exitPoint, GlobalPosition = exitPoint });
		var travelDeadline = Time.GetTicksMsec() + 5000UL;
		while (_map.View.Travel is null)
		{
			Require(Time.GetTicksMsec() < travelDeadline, "Clicking the exit did not start the journey");
			await Frames(1);
		}
		await Frames(2);
		Require(_map.OffMapPanelVisible, "The travel panel did not replace the village map");
		Capture("8-travelling.png");
		await UntilIdle(12);
		Require(_map.View.Location == MappedVillageScenario.Ids.BanditCamp && _map.View.Map is null,
			$"The clickable forest exit did not lead to the camp: {_map.LastMessage}");
		Require(_map.OffMapPanelVisible, "The camp view is missing");
		Capture("9-forest-camp.png");
		_map.ReturnToVillage();
		await UntilIdle(12);
		Require(_map.View.Position == forestExit && _map.View.Map is not null,
			"The return journey did not put the player back on the village exit");
		Require(!_map.OffMapPanelVisible, "The village map did not return");
		Capture("10-village-return.png");
		await NightRaid();
		await DiscoveredTheft();
		await DoorMemory();
		await CombatHudPreview();
		await DeedJournalPreview();
		await NightDuel();
		await DaylightAssault();
	}

	/// <summary>G13: the village reacts to an assault through the real client and a saved world.</summary>
	private async Task DaylightAssault()
	{
		var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		session.Advance(new GameTime(13 * 3600 + 30 * 60).Since(session.Now));
		var farmer = MappedVillageScenario.Ids.Farmer;
		var guard = MappedVillageScenario.Ids.Guard;
		var approach = session.Execute(new MoveCommand { Actor = session.Player,
			To = MappedVillageScenario.Ids.FarmerWork, StopNextTo = true });
		Require(approach.Success, $"G13: cannot approach the farmer: {approach.Message}");
		session.AdvanceUntilCompleted(approach.Action!.Value, Duration.FromMinutes(5));
		Require(session.GetPlayerView().VisibleActors.Any(a => a.Id == farmer),
			"G13: the farmer is not visible from the assault square");
		var before = System.IO.Path.Combine(_outputDir, "g13-before-assault.json");
		using (var stream = File.Create(before)) session.Save(stream);
		_map.SetPaused(true);
		_map.LoadGameFromPath(before);
		await Frames(2);
		Capture("33-g13-before-assault.png");

		_map.OpenCombatForSmoke(farmer, "Colpo senz'armi");
		Require(_map.View.Combat is not null, $"G13: the assault did not start: {_map.LastMessage}");
		Capture("34-g13-assault.png");
		if (_map.View.Combat is { IsYourTurn: true })
			_map.EndCombatTurnForSmoke();
		await Frames(3);
		var fight = _map.View.Combat;
		Require(fight is not null && fight.Order.Any(c => c.Actor == guard),
			"G13: the guard did not join the fight after seeing the assault");
		Require(fight!.Log.Any(e => e.Kind == CombatLogKind.Joined && e.Actor == guard),
			"G13: the guard's arrival is missing from the combat log");
		Capture("35-g13-guard-joins.png");
		var joined = System.IO.Path.Combine(_outputDir, "g13-guard-joined.json");
		_map.SaveGameToPath(joined);

		// A second play-through closes the inn door. The farmer must carry the report to the guard,
		// demonstrating that a later confrontation follows knowledge rather than the first fight.
		var moveGuard = session.Execute(new MoveCommand { Actor = guard, To = new GridPos(6, 3) });
		Require(moveGuard.Success, $"G13: guard cannot reach the inn door: {moveGuard.Message}");
		session.AdvanceUntilCompleted(moveGuard.Action!.Value, Duration.FromMinutes(1));
		session.Execute(new StopCommand { Actor = guard });
		var close = session.Execute(new DoorCommand { Actor = guard, At = MappedVillageScenario.Ids.InnDoor, Open = false });
		Require(close.Success, $"G13: guard cannot close the inn door: {close.Message}");
		var hidden = System.IO.Path.Combine(_outputDir, "g13-before-unseen-assault.json");
		using (var stream = File.Create(hidden)) session.Save(stream);
		_map.LoadGameFromPath(hidden);
		_map.OpenCombatForSmoke(farmer, "Colpo senz'armi");
		Require(_map.View.Combat is not null, $"G13: hidden assault did not start: {_map.LastMessage}");
		Require(!_map.SmokeWorld.Actors.Single(a => a.Id == guard).Knowledge.Any(o => o.Kind == "Assault"),
			"G13: the guard learned an assault through the closed door");
		Require(_map.SmokeWorld.Actors.Single(a => a.Id == farmer).Knowledge.Any(o =>
			o.Kind == "Assault" && o.Culprit == _map.View.Id),
			"G13: the attacked farmer did not recognize his aggressor");
		var inFight = System.IO.Path.Combine(_outputDir, "g13-unseen-assault.json");
		_map.SaveGameToPath(inFight);
		using (var stream = File.OpenRead(inFight))
		{
			var loaded = SimulationSession.TryLoad(stream);
			Require(loaded.Success, $"G13: cannot reload assault: {loaded.Error}");
			session = loaded.Session!;
		}
		var woundedFarmer = false;
		for (var i = 0; i < 30 && session.GetPlayerView().Combat is not null; i++)
		{
			var turn = session.GetPlayerView().Combat;
			woundedFarmer |= turn!.Combatants.Any(c => c.Id == farmer && c.Condition is "ferito" or "malconcio");
			if (turn is { IsYourTurn: true, You.ActionAvailable: true } && !woundedFarmer
				&& turn.Targets.Any(t => t.Target == farmer && t.Attack == "Colpo senz'armi" && t.Valid))
				session.Execute(new AttackCommand { Actor = session.Player, Target = farmer, Attack = "Colpo senz'armi" });
			woundedFarmer |= session.GetPlayerView().Combat?.Combatants.Any(c => c.Id == farmer
				&& c.Condition is "ferito" or "malconcio") == true;
			if (woundedFarmer && session.GetPlayerView().Combat is { IsYourTurn: true } retreatTurn
				&& session.GetPlayerView().Position != new GridPos(4, 9))
			{
				var cover = new GridPos(4, 9);
				var step = retreatTurn.Reachable.OrderBy(r => r.At.StepsTo(cover)).ThenBy(r => r.CostFeet).FirstOrDefault();
				if (step is not null && step.At.StepsTo(cover) < session.GetPlayerView().Position!.Value.StepsTo(cover))
				{
					var retreat = session.Execute(new MoveCommand { Actor = session.Player, To = step.At });
					Require(retreat.Success, $"G13: cannot retreat to cover: {retreat.Message}");
				}
			}
			if (session.GetPlayerView().Combat is { IsYourTurn: true })
				session.Execute(new EndTurnCommand { Actor = session.Player });
			session.Advance(Duration.FromSeconds(6));
		}
		Require(woundedFarmer, "G13: the farmer was never wounded in the unseen assault");
		Require(session.GetPlayerView().Combat is null, "G13: the unseen assault did not finish");
		var village = MappedVillageScenario.Ids.VillageFaction;
		for (var i = 0; i < 1800 && !session.GetWorldView().Factions.Single(f => f.Id == village)
			.Wanted.Contains(session.Player); i++)
			session.Advance(Duration.FromSeconds(1));
		Require(session.GetWorldView().Factions.Single(f => f.Id == village).Wanted.Contains(session.Player),
			"G13: the farmer's report did not make the aggressor wanted");
		Require(session.GetWorldView().Actors.Single(a => a.Id == guard).Knowledge.Any(o =>
			o.Kind == "Assault" && o.Culprit == session.Player && o.Source == farmer),
			"G13: the guard did not learn who attacked the farmer from his report");
		var wantedSave = System.IO.Path.Combine(_outputDir, "g13-wanted.json");
		using (var stream = File.Create(wantedSave)) session.Save(stream);
		_map.LoadGameFromPath(wantedSave);
		await Frames(3);
		Capture("36-g13-wanted.png");

		var returnToGuard = session.Execute(new MoveCommand { Actor = session.Player,
			To = MappedVillageScenario.Ids.GuardStart, StopNextTo = true });
		Require(returnToGuard.Success, $"G13: wanted player cannot approach the guard: {returnToGuard.Message}");
		for (var i = 0; i < 900 && session.GetPlayerView().Combat is null; i++)
			session.Advance(Duration.FromSeconds(1));
		Require(session.GetPlayerView().Combat is { } confrontation
			&& confrontation.Order.Any(c => c.Actor == guard),
			"G13: the guard did not confront the wanted player after the report");
		Require(session.GetWorldView().RecentFacts.Any(f => f.Kind == "CombatStarted"
			&& f.Description.StartsWith("Guardia attacca Protagonista")),
			"G13: the new fight was not initiated by the guard");
		var confronted = System.IO.Path.Combine(_outputDir, "g13-confronted.json");
		using (var stream = File.Create(confronted)) session.Save(stream);
		_map.LoadGameFromPath(confronted);
		await Frames(3);
		Capture("37-g13-guard-confronts.png");
	}

	/// <summary>G12: the journal and conversation render each public C4 observation kind without hidden world data.</summary>
	private async Task DeedJournalPreview()
	{
		_map.SetPaused(true);
		_map.LoadGameFromPath(System.IO.Path.Combine(_outputDir, "raid-at-granary.json"));
		await Frames(2);
		var now = _map.View.Now;
		var granary = MappedVillageScenario.Ids.Granary;
		var raider = MappedVillageScenario.Ids.Raider;
		var farmer = MappedVillageScenario.Ids.Farmer;
		var player = _map.View.Id;
		ObservationView Deed(long id, string kind, string perceived) => new()
		{
			Id = new ObservationId(id), Origin = new ObservationId(id), Kind = kind, Perceived = perceived,
			ObservedAt = now, LearnedAt = now, ToldTo = Array.Empty<ActorId>(),
		};
		var observations = new[]
		{
			Deed(9001, "Theft", "Seen") with { Store = MappedVillageScenario.Ids.GranaryStore,
				StoreName = "Scorte del granaio", Amount = 4, Culprit = raider, CulpritName = "Razziatore",
				Location = granary, Square = MappedVillageScenario.Ids.GranaryAccess },
			Deed(9002, "Assault", "Seen") with { Culprit = player, CulpritName = "Protagonista",
				Victim = farmer, VictimName = "Contadino", Involved = new[] { player, farmer },
				InvolvedNames = new[] { "Protagonista", "Contadino" }, Location = granary,
				Square = MappedVillageScenario.Ids.GranaryAccess },
			Deed(9003, "Assault", "Heard") with { Square = new GridPos(17, 7) },
			Deed(9004, "Killing", "Seen") with { Culprit = farmer, CulpritName = "Contadino",
				Victim = raider, VictimName = "Razziatore", Location = granary,
				Square = MappedVillageScenario.Ids.GranaryAccess, Source = farmer, SourceName = "Contadino" },
		};
		var listener = new PersonView { Id = MappedVillageScenario.Ids.Guard, Name = "Guardia",
			Topics = observations.Select(o => new TopicView { Kind = "Tell", Observation = o.Id,
				Summary = $"Fatto {o.Id.Value}" }).ToArray() };
		_map.PreviewDeedsForSmoke(observations, listener);
		await Frames(3);
		var journal = _map.JournalTextForSmoke;
		Require(journal.Contains("Furto") && journal.Contains("Scontro") && journal.Contains("Rumori di lotta")
			&& journal.Contains("Uccisione"), "G12: the journal omitted an observation kind");
		var topics = string.Join("|", _map.TopicLabelsForSmoke);
		Require(topics.Contains("il furto") && topics.Contains("lo scontro")
			&& topics.Contains("i rumori di lotta") && topics.Contains("l'uccisione"),
			"G12: the conversation omitted a deed kind");
		Capture("31-g12-deeds-preview.png");
	}

	/// <summary>G11: the real C2 encounter through the village client, including a save in the player's turn.</summary>
	private async Task NightDuel()
	{
		_map.SetPaused(true);
		_map.LoadGameFromPath(System.IO.Path.Combine(_outputDir, "raid-at-granary.json"));
		await Frames(2);
		var raider = MappedVillageScenario.Ids.Raider;
		Require(_map.View.VisibleActors.Any(a => a.Id == raider), "G11: the raider is not visible at the granary");
		_map.OpenCombatForSmoke(raider, "Giavellotto (lanciato)");
		Require(_map.View.Combat is not null, $"G11: opening attack did not start a fight: {_map.LastMessage}");
		await Frames(2);
		Capture("27-duel-start.png");
		Require(_map.View.Combat!.Log.Any(e => e.Kind == CombatLogKind.Attack && e.Actor == _map.View.Id),
			"G11: the opening attack is missing from the combat log");
		Capture("28-duel-first-attack.png");

		var midTurn = System.IO.Path.Combine(_outputDir, "duel-mid-turn.json");
		var beforeLoad = _map.View;
		_map.SaveGameToPath(midTurn);
		_map.LoadGameFromPath(midTurn);
		Require(_map.View.Combat is { } loaded && loaded.Round == beforeLoad.Combat!.Round
			&& loaded.You.ActionAvailable == beforeLoad.Combat.You.ActionAvailable
			&& _map.View.Now == beforeLoad.Now, "G11: loading mid-turn changed combat resources or time");

		var woundedCaptured = false;
		var highestRound = _map.View.Combat!.Round;
		for (var i = 0; i < 12 && _map.View.Combat is not null && !_map.View.Dead; i++)
		{
			var fight = _map.View.Combat;
			highestRound = Math.Max(highestRound, fight.Round);
			if (!woundedCaptured && fight.Combatants.FirstOrDefault(c => c.Id == raider) is { Condition: "ferito" or "malconcio" })
			{
				_map.SelectCombatantForSmoke(raider);
				await Frames(2);
				Capture("29-duel-wounded.png");
				woundedCaptured = true;
			}
			if (fight.IsYourTurn && fight.You.ActionAvailable)
			{
				var attack = fight.Targets.FirstOrDefault(t => t.Target == raider && t.Valid);
				if (attack is not null)
					_map.AttackCombatTargetForSmoke(attack);
				else if (fight.Reachable.Count > 0)
					_map.MoveTo(fight.Reachable[0].At);
			}
			if (_map.View.Combat is { IsYourTurn: true })
				_map.EndCombatTurnForSmoke();
			await Frames(3); // the client advances NextRoundAt between rounds
		}
		Require(_map.View.Combat is null || _map.View.Dead, "G11: duel did not reach an ending");
		Require(highestRound >= 2 && _map.View.Now > beforeLoad.Now,
			"G11: the client did not advance time between combat rounds");
		Require(woundedCaptured, "G11: no screenshot of a wounded raider");
		Capture("30-duel-end.png");
	}

	/// <summary>G10: typed C2 data exercises the real village HUD before encounters are wired to the Sim.</summary>
	private async Task CombatHudPreview()
	{
		_map.SetPaused(true);
		_map.LoadGameFromPath(System.IO.Path.Combine(_outputDir, "raid-mid-walk.json"));
		await Frames(3);
		var raider = _map.View.VisibleActors.Single(a => a.Id == MappedVillageScenario.Ids.Raider);
		var player = _map.View.Id;
		_map.SelectOpeningAttackForSmoke(raider.Id);
		await Frames(2);
		Capture("25-opening-attack-preview.png");
		var preview = new CombatView
		{
			Round = 1, Turn = player, IsYourTurn = true,
			Order = new[]
			{
				new InitiativeView { Actor = player, Name = "Paladino", Total = 16, Current = true },
				new InitiativeView { Actor = raider.Id, Name = raider.Name, Total = 11, Current = false },
			},
			You = new CombatSelfView { HitPoints = 11, MaxHitPoints = 11, State = LifeState.Alive,
				KnockedOut = false, DeathSaveSuccesses = 0, DeathSaveFailures = 0, MovementLeftFeet = 30,
				SquaresLeft = 6, ActionAvailable = true, ReactionAvailable = true },
			Combatants = new[] { new CombatantView { Id = raider.Id, Name = raider.Name,
				Position = raider.Position!.Value, Side = CombatSide.Enemy, Condition = "ferito" } },
			Reachable = new[] { new ReachableSquareView { At = new GridPos(17, 8), CostFeet = 5 },
				new ReachableSquareView { At = new GridPos(18, 7), CostFeet = 5 } },
			Targets = new[]
			{
				new TargetView { Target = raider.Id, Attack = "Spada lunga", Valid = false,
					Reason = RejectionReason.TooFar, Why = "Fuori portata" },
				new TargetView { Target = raider.Id, Attack = "Giavellotto (lanciato)", Valid = true },
			},
			Log = new[] { new CombatLogEntry { Round = 1, Kind = CombatLogKind.Started,
				Text = "Il Paladino affronta il Razziatore al granaio." },
				new CombatLogEntry { Round = 1, Kind = CombatLogKind.Initiative,
				Text = "Iniziativa: Paladino 16, Razziatore 11." } },
		};
		_map.PreviewCombatForSmoke(preview);
		await Frames(3);
		Capture("25-combat-hud-preview.png");
		_map.SelectCombatantForSmoke(raider.Id);
		await Frames(2);
		Capture("26-combat-target-preview.png");
		var guard = _map.View.VisibleActors.FirstOrDefault(a => a.Id == MappedVillageScenario.Ids.Guard);
		_map.PreviewCombatForSmoke(preview with
		{
			Order = preview.Order.Append(new InitiativeView { Actor = MappedVillageScenario.Ids.Guard,
				Name = "Guardia", Total = 9, Current = false }).ToArray(),
			Combatants = preview.Combatants.Append(new CombatantView { Id = MappedVillageScenario.Ids.Guard,
				Name = "Guardia", Position = guard?.Position ?? new GridPos(10, 3),
				Side = CombatSide.Ally, Condition = "illeso" }).ToArray(),
			Log = preview.Log.Concat(new[]
			{
				new CombatLogEntry { Round = 1, Kind = CombatLogKind.Joined, Actor = MappedVillageScenario.Ids.Guard,
					Text = "La Guardia entra nello scontro e agirà dal prossimo round." },
				new CombatLogEntry { Round = 1, Kind = CombatLogKind.Fled, Actor = raider.Id,
					Text = "Il Razziatore cerca di fuggire." },
			}).ToArray(),
		});
		await Frames(2);
		Capture("32-g12-multiple-combatants-preview.png");
	}

	/// <summary>G9: frame an unknown farmhouse door, then the player's stale memory after it opens unseen.</summary>
	private async Task DoorMemory()
	{
		var houseDoor = MappedVillageScenario.Ids.HouseDoor;
		var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		var unknown = session.GetPlayerView().Doors.Single(d => d.At == houseDoor);
		Require(unknown.Known == DoorKnowledge.Unknown && unknown.Open is null,
			"The farmhouse door should be unknown from the inn at midnight");
		var unknownSave = System.IO.Path.Combine(_outputDir, "door-unknown.json");
		using (var stream = File.Create(unknownSave)) session.Save(stream);
		_map.SetPaused(true);
		_map.LoadGameFromPath(unknownSave);
		_map.FocusForSmoke(houseDoor);
		await Frames(3);
		Require(_map.View.Doors.Single(d => d.At == houseDoor).Known == DoorKnowledge.Unknown,
			"The UI load revealed the unknown door");
		Capture("22-door-unknown.png");

		var walk = session.Execute(new MoveCommand { Actor = session.Player, To = new GridPos(9, 13) });
		Require(walk.Success, "Cannot approach the farmhouse door for G9");
		session.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(5));
		Require(session.GetPlayerView().Doors.Single(d => d.At == houseDoor) is { Known: DoorKnowledge.Seen, Open: false },
			"The player did not see the closed farmhouse door");
		var seenSave = System.IO.Path.Combine(_outputDir, "door-seen-closed.json");
		using (var stream = File.Create(seenSave)) session.Save(stream);
		_map.LoadGameFromPath(seenSave);
		await Frames(3);
		Capture("23-door-seen-closed.png");

		walk = session.Execute(new MoveCommand { Actor = session.Player, To = MappedVillageScenario.Ids.PlayerStart });
		Require(walk.Success, "Cannot return to the inn for the remembered door");
		session.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(5));
		session.Advance(new GameTime(13 * 3600 + 30 * 60).Since(session.Now));
		Require(session.GetPlayerView().Doors.Single(d => d.At == houseDoor) is { Known: DoorKnowledge.Remembered, Open: false },
			"The player's memory of the closed door changed after the farmer opened it unseen");
		var rememberedSave = System.IO.Path.Combine(_outputDir, "door-remembered-closed.json");
		using (var stream = File.Create(rememberedSave)) session.Save(stream);
		_map.LoadGameFromPath(rememberedSave);
		await Frames(3);
		Require(_map.View.Doors.Single(d => d.At == houseDoor).Known == DoorKnowledge.Remembered,
			"The UI load lost the remembered door state");
		Capture("24-door-remembered-closed.png");
		_map.FocusForSmoke(null);
	}

	/// <summary>G6: show the raid through the actual village view, with a reproducible saved world at each long gap.</summary>
	private async Task NightRaid()
	{
		var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		var lookout = new GridPos(18, 8);
		var walk = session.Execute(new MoveCommand { Actor = session.Player, To = lookout });
		Require(walk.Success, "Cannot place the player at the raid lookout");
		session.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(5));
		var dusk = new GameTime(19 * 3600 + 30 * 60);
		session.Advance(dusk.Since(session.Now));
		var raider = MappedVillageScenario.Ids.Raider;
		var granary = MappedVillageScenario.Ids.GranaryStore;
		var camp = MappedVillageScenario.Ids.CampStore;
		for (var i = 0; i < 60 && session.GetWorldView().Actors.Single(a => a.Id == raider).Move is null; i++)
			session.Advance(Duration.FromSeconds(1));
		Require(session.GetWorldView().Actors.Single(a => a.Id == raider).Move?.Stealthy == true,
			"The raider did not enter the village sneaking at dusk");
		Require(session.GetWorldView().Stores.Single(s => s.Id == granary).Food == 40,
			"The granary was robbed before the raider arrived");

		_map.SetPaused(true);
		var midRaid = System.IO.Path.Combine(_outputDir, "raid-mid-walk.json");
		using (var stream = File.Create(midRaid)) session.Save(stream);
		_map.LoadGameFromPath(midRaid);
		await Frames(3);
		Require(_map.View.Position == lookout && _map.SmokeWorld.Actors.Single(a => a.Id == raider).Move?.Stealthy == true,
			"The UI did not restore the raid walk");
		Require(_map.View.VisibleActors.Any(a => a.Id == raider), "The raider should be visible at dusk from the lookout");
		Capture("11-raider-enters.png");

		_map.SetPace(6);
		_map.SetPaused(false);
		await Seconds(0.8);
		_map.SetPaused(true);
		Require(_map.SmokeWorld.Actors.Single(a => a.Id == raider).Position is not null,
			"The raider vanished from the map during his approach");
		Capture("12-raider-approaches.png");
		var resumedAt = _map.View.Now;
		_map.SaveGameToPath(midRaid);
		_map.LoadGameFromPath(midRaid);
		Require(_map.View.Now == resumedAt && _map.SmokeWorld.Actors.Single(a => a.Id == raider).Move?.Stealthy == true,
			"Saving and loading during the raider's walk lost the routine");
		_map.SetPaused(false);
		await Seconds(0.6);
		_map.SetPaused(true);
		Require(_map.View.Now > resumedAt && _map.SmokeWorld.Actors.Single(a => a.Id == raider).Position is not null,
			"The loaded raid walk did not resume");

		session.Advance(new GameTime(19 * 3600 + 31 * 60).Since(session.Now));
		Require(session.GetWorldView().Stores.Single(s => s.Id == granary).Food == 40,
			"The raid should still be in progress before the three-minute theft finishes");
		var atGranary = System.IO.Path.Combine(_outputDir, "raid-at-granary.json");
		using (var stream = File.Create(atGranary)) session.Save(stream);
		_map.LoadGameFromPath(atGranary);
		await Frames(3);
		Require(_map.SmokeWorld.Actors.Single(a => a.Id == raider).Position == MappedVillageScenario.Ids.GranaryAccess,
			"The raider did not reach the declared granary access");
		Require(_map.View.VisibleActors.Any(a => a.Id == raider),
			"The player cannot see the raider at the granary in the dusk fixture");
		Capture("13-raid-at-granary.png");

		session.Advance(new GameTime(19 * 3600 + 35 * 60).Since(session.Now));
		var afterTheft = System.IO.Path.Combine(_outputDir, "raid-after-theft.json");
		using (var stream = File.Create(afterTheft)) session.Save(stream);
		_map.LoadGameFromPath(afterTheft);
		await Frames(3);
		Require(_map.SmokeWorld.Stores.Single(s => s.Id == granary).Food == 32, "The raid did not take eight rations");
		Capture("14-after-theft.png");

		session.Advance(new GameTime(20 * 3600 + 10 * 60).Since(session.Now));
		var returned = System.IO.Path.Combine(_outputDir, "raid-returned.json");
		using (var stream = File.Create(returned)) session.Save(stream);
		_map.LoadGameFromPath(returned);
		await Frames(3);
		Require(_map.SmokeWorld.Actors.Single(a => a.Id == raider).Location == MappedVillageScenario.Ids.BanditCamp,
			"The raider did not return to the camp");
		Require(_map.SmokeWorld.Stores.Single(s => s.Id == camp).Food == 16,
			"The stolen rations were not delivered to the camp");
		Capture("15-raid-returned.png");
	}

	/// <summary>G8: the player is seen stealing, the guard catches up, and the farmer reports the theft.</summary>
	private async Task DiscoveredTheft()
	{
		var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		session.Advance(new GameTime(13 * 3600 + 30 * 60).Since(session.Now));
		var toAccess = session.Execute(new MoveCommand { Actor = session.Player, To = MappedVillageScenario.Ids.GranaryAccess });
		Require(toAccess.Success, "Cannot place the player at the granary access for G8");
		session.AdvanceUntilCompleted(toAccess.Action!.Value, Duration.FromMinutes(5));
		var guard = MappedVillageScenario.Ids.Guard;
		var farmer = MappedVillageScenario.Ids.Farmer;
		var granary = MappedVillageScenario.Ids.GranaryStore;
		Require(session.GetWorldView().Actors.Single(a => a.Id == farmer).Position == MappedVillageScenario.Ids.FarmerWork,
			"The farmer is not at work for the daylight theft");
		var beforeTheft = System.IO.Path.Combine(_outputDir, "discovered-before-theft.json");
		using (var stream = File.Create(beforeTheft)) session.Save(stream);
		_map.SetPaused(true);
		_map.LoadGameFromPath(beforeTheft);
		await Frames(3);
		Require(_map.View.Position == MappedVillageScenario.Ids.GranaryAccess,
			"The UI did not load the player at the granary entrance");
		Capture("16-daylight-granary.png");

		_map.SetPaused(false);
		_map.TakeViaUi(3);
		Require(_map.IsBusy, $"The Prendi handler did not start the theft: {_map.LastMessage}");
		await UntilIdle(8);
		Require(_map.View.Food == 13 && _map.SmokeWorld.Stores.Single(s => s.Id == granary).Food == 37,
			"The player's daylight theft did not take three rations");
		Require(_map.EventText.Contains("ruba 3 razioni") && _map.EventText.Contains("G1 13:"),
			"The Events panel did not show the daylight theft with its game time");
		Capture("17-theft-discovered.png");

		_map.MoveTo(new GridPos(12, 9));
		await UntilIdle(10);
		Require(_map.View.Position == new GridPos(12, 9), "The player did not retreat to the road");
		var midContact = System.IO.Path.Combine(_outputDir, "discovered-mid-contact.json");
		_map.SaveGameToPath(midContact);
		using (var stream = File.OpenRead(midContact))
		{
			var loaded = SimulationSession.TryLoad(stream);
			Require(loaded.Success, $"The UI's daylight theft save did not load: {loaded.Error}");
			session = loaded.Session!;
		}

		for (var i = 0; i < 600 && session.GetWorldView().Actors.Single(a => a.Id == guard).Action?.Kind != "Confiscate"; i++)
			session.Advance(Duration.FromSeconds(1));
		Require(session.GetWorldView().Actors.Single(a => a.Id == guard).Action?.Kind == "Confiscate",
			"The guard did not reach the player to confiscate the stolen food");
		var confronted = System.IO.Path.Combine(_outputDir, "discovered-confronted.json");
		using (var stream = File.Create(confronted)) session.Save(stream);
		_map.SetPaused(true);
		_map.LoadGameFromPath(confronted);
		await Frames(3);
		Require(_map.View.PeopleHere.Any(p => p.Id == guard), "The guard is not next to the player during confiscation");
		Capture("18-guard-confronts.png");

		for (var i = 0; i < 300 && session.GetPlayerView().Food != 10; i++)
			session.Advance(Duration.FromSeconds(1));
		Require(session.GetPlayerView().Food == 10, "The guard did not recover three stolen rations");
		var confiscated = System.IO.Path.Combine(_outputDir, "discovered-confiscated.json");
		using (var stream = File.Create(confiscated)) session.Save(stream);
		_map.LoadGameFromPath(confiscated);
		await Frames(3);
		Require(_map.EventText.Contains("si fa restituire") && _map.View.Food == 10,
			"The Events panel did not show the confiscation to the player");
		Capture("19-food-confiscated.png");

		for (var i = 0; i < 900 && session.GetWorldView().Stores.Single(s => s.Id == granary).Food != 40; i++)
			session.Advance(Duration.FromSeconds(1));
		Require(session.GetWorldView().Stores.Single(s => s.Id == granary).Food == 40,
			"The guard did not return the recovered food through the granary access");
		var restored = System.IO.Path.Combine(_outputDir, "discovered-restored.json");
		using (var stream = File.Create(restored)) session.Save(stream);
		_map.LoadGameFromPath(restored);
		await Frames(3);
		Capture("20-granary-restored.png");

		for (var i = 0; i < 900 && !session.GetWorldView().RecentFacts.Any(f => f.Kind == "InformationShared"
			&& f.Description.Contains("Contadino racconta a Guardia")); i++)
			session.Advance(Duration.FromSeconds(1));
		Require(session.GetWorldView().RecentFacts.Any(f => f.Kind == "ReportFailed" && f.Description.Contains("Contadino")),
			"The farmer's interrupted report was not recorded");
		Require(session.GetWorldView().RecentFacts.Any(f => f.Kind == "InformationShared"
			&& f.Description.Contains("Contadino racconta a Guardia")),
			"The farmer did not reach the guard to report the theft again");
		var reported = System.IO.Path.Combine(_outputDir, "discovered-reported.json");
		using (var stream = File.Create(reported)) session.Save(stream);
		_map.LoadGameFromPath(reported);
		await Frames(3);
		Capture("21-farmer-reports.png");
	}

	private static void Require(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private async Task UntilIdle(double maxSeconds)
	{
		var deadline = Time.GetTicksMsec() + (ulong)(maxSeconds * 1000);
		await Frames(1);
		while (_map.IsBusy)
		{
			Require(Time.GetTicksMsec() < deadline, "Timed out waiting for the player");
			await Frames(1);
		}
		await Frames(2);
	}

	private void Capture(string name)
	{
		var path = System.IO.Path.Combine(_outputDir, name);
		Require(GetViewport().GetTexture().GetImage().SavePng(path) == Error.Ok, $"Cannot save {path}");
		GD.Print($"VILLAGE SMOKE capture {path}");
	}

	private async Task Frames(int count)
	{
		for (var i = 0; i < count; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Task Seconds(double seconds) =>
		await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
