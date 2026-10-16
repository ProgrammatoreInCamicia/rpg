using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;
using RpgSandbox.Sim.Scenarios;
using System.Text.Json.Nodes;

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
		if (OS.GetCmdlineUserArgs().Contains("--r1-only"))
		{
			await Frames(3);
			await RestAndHealing();
			return;
		}
		if (OS.GetCmdlineUserArgs().Contains("--r2r3a-only"))
		{
			await Frames(3);
			await ClassAndConditions();
			return;
		}
		if (OS.GetCmdlineUserArgs().Contains("--r3bc-attack-only"))
		{
			await Frames(3);
			await PaladinAttackFeatures();
			return;
		}
		if (OS.GetCmdlineUserArgs().Contains("--r4-ui-only"))
		{
			await Frames(3);
			await R4ClientControls();
			return;
		}
		if (OS.GetCmdlineUserArgs().Contains("--r5-ui-only"))
		{
			await Frames(3);
			await R5ClientControls();
			return;
		}
		if (OS.GetCmdlineUserArgs().Contains("--p5a-only"))
		{
			await Frames(3);
			await NightFarmhouseBreakIn();
			return;
		}
		await Frames(10);
		Require(_map.View.Position == MappedVillageScenario.Ids.PlayerStart, "Player does not start at the inn");
		Require(_map.View.Level == 1 && _map.View.Experience == 0 && _map.View.NextLevelAt == 300
			&& _map.LevelExperienceTextForSmoke.Contains("Livello 1 · Esperienza 0 / 300"),
			"C5b: the player level and experience are not shown from the typed view");
		_map._UnhandledInput(new InputEventKey { Keycode = Key.I, Pressed = true });
		Require(_map.InventoryOpenForSmoke, "UX: I did not open the inventory");
		_map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
		Require(!_map.InventoryOpenForSmoke, "UX: Escape did not close the inventory");
		_map.ArmAttackForSmoke("Spada lunga");
		Require(_map.AttackModeForSmoke, "UX: the attack mode did not arm");
		_map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
		Require(!_map.AttackModeForSmoke, "UX: Escape did not cancel the attack mode");
		Require(_map.HasFigureSpriteForSmoke(MappedVillageScenario.Ids.Player)
			&& _map.HasFigureSpriteForSmoke(MappedVillageScenario.Ids.Guard),
			"G15: directional character sprite did not load in the village");
		Require(_map.PaladinLive3dLoadedForSmoke, "G15: the live 3D paladin is not the village default");
		Require(_map.NpcLive3dLoadedForSmoke(MappedVillageScenario.Ids.Guard),
			"Visuals: the guard does not use the live 3D figure");
		_map.TogglePaladin3dPrototypeForSmoke();
		await Frames(2);
		Require(_map.Paladin3dLoadedForSmoke, "G15: the optional 3D paladin walk atlas did not load");
		Capture("1-paladin-3d-prototype.png");
		_map.TogglePaladin3dPrototypeForSmoke();
		Capture("1-start.png");
		// The main village, like the isolated house, accepts an exact floor point rather than snapping a click to a tile centre.
		var exactFloor = new Spot(165, 147);
		_map.ClickSpotForSmoke(exactFloor);
		_map.TogglePaladin3dPrototypeForSmoke();
		await Frames(8);
		Capture("1-paladin-3d-walking.png");
		_map.TogglePaladin3dPrototypeForSmoke();
		await UntilIdle(4);
		Require(_map.View.Spot == exactFloor
			&& _map.FootPointOf(_map.View.Id) is { } exactFoot
			&& exactFoot.DistanceTo(VillageMap.ScreenPointOf(exactFloor)) < 1f,
			"The village click did not walk to the exact floor point");
		Capture("1-free-walk.png");
		_map.MoveTo(MappedVillageScenario.Ids.PlayerStart);
		await UntilIdle(4);
		await Frames(3);
		Require(_map.PaladinLive3dLoadedForSmoke, "G15: the live 3D paladin did not supply the village sprite texture");
		Capture("1-paladin-live-3d.png");
		_map.ClickSpotForSmoke(new Spot(180, 145));
		await Frames(8);
		Require(_map.PaladinLive3dLoadedForSmoke, "G15: the live 3D paladin vanished while walking");
		Capture("1-paladin-live-3d-walking.png");
		await UntilIdle(4);
		_map.MoveTo(MappedVillageScenario.Ids.PlayerStart);
		await UntilIdle(4);
		_map.OpenInventoryForSmoke();
		Require(_map.EquipmentLabelsForSmoke.Any(x => x == "Armatura: Cotta di maglia")
			&& _map.EquipmentLabelsForSmoke.Any(x => x == "Scudo: Scudo")
			&& _map.EquipmentLabelsForSmoke.Any(x => x == "Mano principale: Spada lunga"),
			"D1a: equipment slots are not shown from the typed view");
		Require(!_map.DropEnabledForSmoke(new ItemId("chain-mail"))
			&& !_map.DropEnabledForSmoke(new ItemId("shield"))
			&& !_map.DropEnabledForSmoke(new ItemId("longsword"))
			&& _map.DropEnabledForSmoke(new ItemId("javelin")),
			"D1a: the inventory permits dropping equipped gear or blocks spare javelins");
		await Frames(2);
		Capture("45-d1a-equipment.png");
		_map.CloseInventoryForSmoke();

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
		_map.RequestTorchForSmoke();
		Require(_map.TorchShieldPromptVisibleForSmoke, "T1: the queued torch needs an offer to stow the shield");
		_map.ConfirmTorchShieldForSmoke();
		Require(_map.View.TorchLitUntil is null && _map.View.Torches == 3 && _map.QueuedOrders == 2,
			"A torch ordered while paused must not light (nor be spent) before the resume");
		_map.Stop(); // X while paused takes back the queued walk
		Require(_map.QueuedOrders == 1, "X should take back the queued walk");
		_map.MoveTo(target);
		await Seconds(1.0);
		Require(_map.View.Now == pausedAt && _map.View.Position == MappedVillageScenario.Ids.PlayerStart, "Time or position moved while paused");
		_map.SetPaused(false);
		Require(_map.IsWalking && _map.View.TorchLitUntil is not null && _map.View.Torches == 2
			&& _map.View.Equipment.Any(e => e.Slot == EquipmentSlot.OffHand && e.Item == new ItemId("torch"))
			&& !_map.View.Equipment.Any(e => e.Slot == EquipmentSlot.Shield) && _map.QueuedOrders == 0,
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
		Require(!_map.IsBusy && _map.View.Position == stoppedAt,
			$"The position moved after stopping: before={stoppedAt}, after={_map.View.Position}, busy={_map.IsBusy}, time={_map.View.Now.Seconds}, stoppedAtTime={stoppedTime.Seconds}");
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
		Require(light[6][17] == '0', "The granary yard should remain dark");

		// From the road in front of the door the guard is in sight: a real click on her figure (not on her square)
		// walks up to her and opens the conversation.
		var guard = MappedVillageScenario.Ids.Guard;
		_map.MoveTo(new GridPos(8, 3));
		await UntilIdle(40);
		Require(_map.View.MapLight![3][8] == '1', "Lamplight should reach the road by the inn door");
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
		await Frames(3);
		Require(_map.PaladinLive3dLoadedForSmoke, "G15: the live 3D paladin vanished by a closed door and torch");
		Capture("6-paladin-live-3d-torch-door.png");

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
		while (!_map.TravelChoiceOpenForSmoke)
		{
			Require(Time.GetTicksMsec() < travelDeadline, "Clicking the exit did not offer a journey pace");
			await Frames(1);
		}
		Capture("r5-travel-choice.png");
		_map.ChooseTravelPaceForSmoke(TravelPace.Fast);
		Require(_map.View.Travel is { Pace: TravelPace.Fast }, "R5b: fast pace was not sent to the journey");
		await Frames(2);
		Require(_map.OffMapPanelVisible, "The travel panel did not replace the village map");
		Capture("8-travelling.png");
		await UntilIdle(12);
		Require(_map.View.Location == MappedVillageScenario.Ids.BanditCamp && _map.View.Map is null,
			$"The clickable forest exit did not lead to the camp: {_map.LastMessage}");
		Require(_map.OffMapPanelVisible, "The camp view is missing");
		Capture("9-forest-camp.png");
		_map.ReturnToVillage();
		Require(_map.TravelChoiceOpenForSmoke, "R5b: the return journey did not offer a pace");
		_map.ChooseTravelPaceForSmoke(TravelPace.Normal);
		await UntilIdle(12);
		Require(_map.View.Position == forestExit && _map.View.Map is not null,
			"The return journey did not put the player back on the village exit");
		Require(!_map.OffMapPanelVisible, "The village map did not return");
		Capture("10-village-return.png");
		await NightRaid();
		await NightFarmhouseBreakIn();
		await RestAndHealing();
		await ClassAndConditions();
		await DiscoveredTheft();
		await DoorMemory();
		await WindowGlassAndBars();
		await CombatWindowAction();
		await CombatHudPreview();
		await DeedJournalPreview();
		await NightDuel();
		await DaylightAssault();
		await CombatActions();
		await PaladinAttackFeatures();
		await R4ClientControls();
		await InventoryPreview();
		await InventoryCombat();
		await R5ClientControls();
	}

	/// <summary>R5 client: only known hazards are drawn, and the controls send real Search, Disarm and Poison commands.</summary>
	private async Task R5ClientControls()
	{
		var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		using var stream = new MemoryStream();
		session.Save(stream);
		var save = JsonNode.Parse(stream.ToArray())!.AsObject();
		var actor = save["Actors"]!.AsArray().Single(a => a!["Id"]!.GetValue<string>() == MappedVillageScenario.Ids.Player.Value)!;
		actor["Inventory"]!.AsArray().Add(new JsonObject { ["Item"] = "iron-spike", ["Count"] = 1 });
		actor["Inventory"]!.AsArray().Add(new JsonObject { ["Item"] = "basic-poison", ["Count"] = 2 });
		var hazard = new GridPos(3, 2);
		Require(session.GetPlayerView().Map!.Rows[hazard.Y][hazard.X] != '#', "R5 fixture: trap square is a wall");
		var area = session.GetPlayerView().Map!.Area.Value;
		var mapped = save["Maps"]!.AsArray().Single(m => m!["Area"]!.GetValue<string>() == area)!;
		mapped["Traps"] = new JsonArray(new JsonObject
		{
			["X"] = hazard.X, ["Y"] = hazard.Y, ["Kind"] = "Pit", ["Found"] = true,
			["Sprung"] = false, ["Disarmed"] = false,
		});
		var fixture = System.IO.Path.Combine(_outputDir, "r5-client-hazard.json");
		File.WriteAllText(fixture, save.ToJsonString());
		_map.LoadGameFromPath(fixture);
		await Frames(2);
		Require(_map.NeedsTextForSmoke.Contains("acqua") && _map.View.Water is not null,
			"R5a: the HUD omitted water and supplies");
		Require(!_map.StatusCardExpandedForSmoke, "UX: the status card should start compact");
		_map.ToggleStatusCardForSmoke();
		Require(_map.StatusCardExpandedForSmoke, "UX: status details did not open");
		await Frames(1);
		Capture("ux-status-expanded.png");
		_map.ToggleStatusCardForSmoke();
		Require(_map.TrapMarkVisibleForSmoke(hazard), "R5c: a known pit has no map marker");
		_map.SetPaused(false);
		_map.SearchFromQuickbarForSmoke();
		Require(_map.IsBusy, "R5c: Search in the quickbar did not start its activity");
		await UntilIdle(5);
		_map.OpenTrapMenuForSmoke(hazard);
		Require(_map.TrapDisarmEnabledForSmoke, "R5c: the known nearby pit cannot be disarmed from its menu");
		_map.DisarmTrapFromMenuForSmoke();
		await UntilIdle(5);
		Require(_map.View.KnownTraps.Single(t => t.At == hazard).Disarmed,
			$"R5c: the client did not disarm the found pit: {_map.LastMessage}");
		_map.OpenInventoryForSmoke();
		_map.ApplyPoisonFromInventoryForSmoke();
		Require(_map.View.WeaponPoisonedUntil is not null,
			$"R5c: applying Basic Poison from the inventory failed: {_map.LastMessage}");
		await Frames(2);
		Capture("r5-hazard-ui.png");
		_map.CloseInventoryForSmoke();
		Require(_map.PoisonQuickButtonVisibleForSmoke, "UX: the available poison is missing from the items section");
		await Frames(1);
		Capture("ux-items-poison-ready.png");
		_map.ApplyPoisonFromQuickbarForSmoke();
		Require(_map.View.WeaponPoisonedUntil is not null
			&& !_map.View.Inventory.Any(i => i.Item.Value == "basic-poison"),
			"UX: the poison shortcut did not invoke the Sim command");
		await Frames(1);
		Capture("ux-items-poison.png");
		Capture("r5-hazard-map.png");

		var pitSave = JsonNode.Parse(stream.ToArray())!.AsObject();
		var fallen = pitSave["Actors"]!.AsArray().Single(a => a!["Id"]!.GetValue<string>() == MappedVillageScenario.Ids.Player.Value)!;
		fallen["InPit"] = true;
		var pitMap = pitSave["Maps"]!.AsArray().Single(m => m!["Area"]!.GetValue<string>() == area)!;
		pitMap["Traps"] = new JsonArray(new JsonObject
		{
			["X"] = MappedVillageScenario.Ids.PlayerStart.X,
			["Y"] = MappedVillageScenario.Ids.PlayerStart.Y,
			["Kind"] = "Pit", ["Found"] = true, ["Sprung"] = true, ["Disarmed"] = false,
		});
		var pitFixture = System.IO.Path.Combine(_outputDir, "r5-client-in-pit.json");
		File.WriteAllText(pitFixture, pitSave.ToJsonString());
		_map.LoadGameFromPath(pitFixture);
		await Frames(2);
		Require(_map.View.InPit && _map.ClimbQuickActionVisibleForSmoke,
			"R5c: the climb action must appear while the player is in a pit");
		_map.ClimbFromQuickbarForSmoke();
		Require(_map.IsBusy, "R5c: climb from the quickbar did not start its activity");
		await UntilIdle(5);
		Require(_map.View.RecentEvents.Any(e => e.Kind is "ClimbedOut" or "ClimbOutFailed"),
			"R5c: the climb attempt did not reach the Sim");
		Capture("r5-pit-ui.png");
	}

	/// <summary>C3b: spend the last thrown weapon, save/load, collect it through the client, and throw it again.</summary>
	private async Task InventoryCombat()
	{
		var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		var javelin = new ItemId("javelin");
		const string throwName = "Giavellotto (lanciato)";
		var spare = session.Execute(new DropCommand { Actor = session.Player, Item = javelin, Count = 5 });
		Require(spare.Success, $"C3b: cannot leave spare javelins: {spare.Message}");
		var before = System.IO.Path.Combine(_outputDir, "c3b-before-throw.json");
		using (var stream = File.Create(before)) session.Save(stream);
		_map.SetPaused(true);
		_map.LoadGameFromPath(before);
		_map.OpenInventoryForSmoke();
		Require(_map.View.Coins == 900 && _map.View.Inventory.Single(i => i.Item == javelin).Count == 1,
			"C3b: coins or carried javelin are missing from the client view");
		var guard = MappedVillageScenario.Ids.Guard;
		_map.OpenCombatForSmoke(guard, throwName);
		Require(_map.View.Combat is not null && !_map.View.Dead,
			$"C3b: the thrown weapon did not open a lasting fight: {_map.LastMessage}");
		Require(_map.View.Attacks.Single(a => a.Name == throwName).Remaining == 0,
			"C3b: the thrown javelin was not consumed in the client view");
		var fallen = _map.View.GroundItems.SingleOrDefault(i => i.Item == javelin && i.At != _map.View.Position)
			?? throw new InvalidOperationException("C3b: the thrown javelin is not visible on the target square");
		Require(_map.GroundMarkVisibleForSmoke(fallen.At),
			"C3b: the thrown javelin is not visible on the target square");
		var mid = System.IO.Path.Combine(_outputDir, "c3b-last-javelin-thrown.json");
		_map.SaveGameToPath(mid);
		_map.LoadGameFromPath(mid);
		Require(_map.View.Attacks.Single(a => a.Name == throwName).Remaining == 0
			&& _map.View.GroundItems.Any(i => i.Item == javelin && i.At == fallen.At),
			"C3b: loading restored the spent javelin or lost the ground item");
		if (_map.View.Combat is { IsYourTurn: true })
			_map.EndCombatTurnForSmoke();
		for (var i = 0; i < 60 && _map.View.Combat is not { IsYourTurn: true, You.ActionAvailable: true }; i++)
			await Frames(1);
		Require(_map.View.Combat is { IsYourTurn: true, You.ActionAvailable: true } ready
			&& ready.Targets.Any(t => t.Target == guard && t.Attack == throwName
				&& !t.Valid && t.Reason == RejectionReason.NoAmmo),
			"C3b: an empty quiver did not disable the attack with NoAmmo");
		_map.SelectCombatantForSmoke(guard);
		Require(_map.CombatAttackLabelsForSmoke.Any(text => text.Contains("Giavellotto") && text.Contains("finiti")),
			"C3b: the fight panel does not explain that the javelins are finished");
		await Frames(2);
		Capture("42-c3b-out-of-javelins.png");
		for (var turn = 0; turn < 8 && !_map.View.Dead; turn++)
		{
			if (_map.View.Combat is not { IsYourTurn: true } fight)
			{
				await Frames(2);
				continue;
			}
			if (_map.View.Position!.Value.StepsTo(fallen.At) > 1)
			{
				var approach = fight.Reachable.Where(r => r.At.StepsTo(fallen.At) <= 1)
					.OrderBy(r => r.CostFeet).FirstOrDefault();
				if (approach is not null)
					_map.MoveTo(approach.At);
			}
			if (_map.View.Position!.Value.StepsTo(fallen.At) <= 1)
			{
				var stack = _map.View.GroundItems.Single(i => i.Item == javelin && i.At == fallen.At);
				_map.PickUpForSmoke(stack);
				Require(_map.View.Attacks.Single(a => a.Name == throwName).Remaining == 1,
					"C3b: the client did not collect the thrown javelin");
				await Frames(2);
				Capture("43-c3b-javelin-recovered.png");
				break;
			}
			_map.EndCombatTurnForSmoke();
			await Frames(2);
		}
		Require(_map.View.Attacks.Single(a => a.Name == throwName).Remaining == 1,
			"C3b: the player did not reach and collect the javelin");
		for (var turn = 0; turn < 8 && _map.View.Combat is not null && !_map.View.Dead; turn++)
		{
			if (_map.View.Combat is { IsYourTurn: true, You.ActionAvailable: true } fight)
			{
				var throwAgain = fight.Targets.FirstOrDefault(t => t.Target == guard && t.Attack == throwName && t.Valid);
				if (throwAgain is not null)
				{
					_map.AttackCombatTargetForSmoke(throwAgain);
					break;
				}
			}
			if (_map.View.Combat is { IsYourTurn: true })
				_map.EndCombatTurnForSmoke();
			await Frames(2);
		}
		Require(_map.View.Attacks.Single(a => a.Name == throwName).Remaining == 0,
			"C3b: the recovered javelin was not thrown again");
		await Frames(2);
		Capture("44-c3b-javelin-rethrown.png");
	}

	/// <summary>Render the published C3b view contract before the inventory rules are wired into the Sim.</summary>
	private async Task InventoryPreview()
	{
		_map.SetPaused(true);
		var at = _map.View.Position!.Value;
		var ground = new GridPos(at.X + 1, at.Y);
		_map.PreviewInventoryForSmoke(
			new[] { new ItemStackView { Item = new ItemId("javelin"), Name = "Giavellotto", Count = 3 },
				new ItemStackView { Item = new ItemId("torch"), Name = "Torcia", Count = 2 } },
			new[] { new GroundItemView { At = ground, Item = new ItemId("javelin"),
				Name = "Giavellotto", Count = 1 } }, 930);
		Require(_map.GroundMarkVisibleForSmoke(ground), "C3b preview: the ground item is not marked");
		await Frames(2);
		Capture("41-c3b-inventory-preview.png");
	}

	/// <summary>R4: equip from the inventory, use maneuvers and the off-hand attack, then exercise the action menu.</summary>
	private async Task R4ClientControls()
	{
		var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		using var initial = new MemoryStream();
		session.Save(initial);
		var peaceful = JsonNode.Parse(initial.ToArray())!.AsObject();
		var player = peaceful["Actors"]!.AsArray().Single(a => a!["Id"]!.GetValue<string>() == MappedVillageScenario.Ids.Player.Value)!;
		player["Inventory"]!.AsArray().Add(new JsonObject { ["Item"] = "dagger", ["Count"] = 1 });
		player["Inventory"]!.AsArray().Add(new JsonObject { ["Item"] = "shortsword", ["Count"] = 1 });
		var peacefulSave = System.IO.Path.Combine(_outputDir, "r4-gear-outside.json");
		File.WriteAllText(peacefulSave, peaceful.ToJsonString());
		_map.LoadGameFromPath(peacefulSave);
		await Frames(2);
		_map.RequestTorchForSmoke();
		Require(_map.TorchShieldPromptVisibleForSmoke,
			"T1 client: lighting a torch with a shield should offer to stow it first");
		_map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
		Require(!_map.TorchShieldPromptVisibleForSmoke,
			"T1 client: Escape should dismiss the torch and shield choice");
		_map.SetPaused(false);
		_map.RequestTorchForSmoke();
		_map.ConfirmTorchShieldForSmoke();
		Require(_map.View.TorchLitUntil is not null
			&& !_map.View.Equipment.Any(e => e.Slot == EquipmentSlot.Shield)
			&& _map.View.Equipment.Any(e => e.Slot == EquipmentSlot.OffHand && e.Item == new ItemId("torch"))
			&& _map.View.Attacks.Any(a => a.Name == "Torcia"),
			$"T1 client: stowing the shield and lighting the torch failed: {_map.LastMessage}");
		Require(_map.SelectTorchFromOffHandForSmoke(),
			"T1 client: clicking the lit off-hand torch should arm the Torch attack");
		_map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
		_map.ToggleTorch();
		Require(_map.View.TorchLitUntil is null
			&& !_map.View.Equipment.Any(e => e.Slot == EquipmentSlot.OffHand && e.Item == new ItemId("torch")),
			"T1 client: putting out the torch should free the off hand");
		Require(_map.QuickbarVisibleForSmoke && _map.ExploreActionBarVisibleForSmoke,
			"UX: the compact exploration bar is missing");
		Require(_map.HudLogAlphaForSmoke < 0.5f, "UX: the log should be translucent at rest");
		_map.HoverHudLogForSmoke(true);
		Require(_map.HudLogAlphaForSmoke > 0.9f, "UX: hovering should reveal the log");
		_map.HoverHudLogForSmoke(false);
		_map.ExpandHudLogForSmoke();
		Require(_map.HudLogAlphaForSmoke == 1f, "UX: opening the log should make it opaque");
		_map.ExpandHudLogForSmoke();
		_map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
		Require(_map.GameMenuOpenForSmoke, "UX: Escape should open the game menu");
		_map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
		Require(!_map.GameMenuOpenForSmoke, "UX: Escape should close the game menu");
		_map.OpenInventoryForSmoke();
		_map.StowForSmoke(EquipmentSlot.MainHand);
		_map.EquipForSmoke(new ItemId("dagger"), EquipmentSlot.MainHand);
		_map.EquipForSmoke(new ItemId("shortsword"), EquipmentSlot.OffHand);
		Require(_map.View.Equipment.Any(e => e.Slot == EquipmentSlot.MainHand && e.Item == new ItemId("dagger"))
			&& _map.View.Equipment.Any(e => e.Slot == EquipmentSlot.OffHand && e.Item == new ItemId("shortsword"))
			&& _map.View.Attacks.Any(a => a.Name == "Spada corta (mano secondaria)"),
			$"R4b: inventory controls did not equip two weapons: {_map.LastMessage}");
		await Frames(2);
		Capture("r4-two-weapons-inventory.png");

		var source = System.IO.Path.Combine(_outputDir, "g13-guard-joined.json");
		var fight = JsonNode.Parse(File.ReadAllText(source))!.AsObject();
		var actors = fight["Actors"]!.AsArray();
		player = actors.Single(a => a!["Id"]!.GetValue<string>() == MappedVillageScenario.Ids.Player.Value)!;
		player["Inventory"]!.AsArray().Add(new JsonObject { ["Item"] = "dagger", ["Count"] = 1 });
		player["Inventory"]!.AsArray().Add(new JsonObject { ["Item"] = "shortsword", ["Count"] = 1 });
		player["Equipment"]!["Shield"] = null;
		player["Equipment"]!["MainHand"] = "dagger";
		player["Equipment"]!["OffHand"] = "shortsword";
		var twoWeapons = System.IO.Path.Combine(_outputDir, "r4-two-weapons-fight.json");
		File.WriteAllText(twoWeapons, fight.ToJsonString());
		_map.LoadGameFromPath(twoWeapons);
		await Frames(2);
		Require(_map.ActionQuickButtonVisibleForSmoke && _map.BonusQuickButtonVisibleForSmoke && _map.EndTurnFixedForSmoke,
			"UX: in combat every section is on the bar, with a fixed End Turn control");
		_map.ChooseBonusQuickFilterForSmoke();
		Require(_map.BonusQuickFilterSelectedForSmoke
			&& _map.BonusQuickButtonVisibleForSmoke
			&& !_map.ActionQuickButtonVisibleForSmoke,
			"UX: the Bonus filter should dim the commands that take the action");
		await Frames(1);
		Capture("ux-paladin-bonus.png");
		_map.ChooseAllQuickFilterForSmoke();
		Require(_map.BonusQuickButtonVisibleForSmoke, "UX: All should restore the class commands");
		Require(_map.ActionQuickButtonVisibleForSmoke,
			"UX: All should light the tactical actions again");
		await Frames(1);
		Capture("ux-combat-bar.png");
		var enemy = MappedVillageScenario.Ids.Farmer;
		Require(_map.View.Combat is { IsYourTurn: true, You.ActionAvailable: true }
			&& _map.View.Combat.Targets.Any(t => t.Target == enemy && t.Attack == "Pugnale" && t.Valid),
			$"R4b: no valid first light-weapon attack in the fixture: "
			+ $"attacks={string.Join(",", _map.View.Attacks.Select(a => a.Name))}; "
			+ $"options={string.Join(";", _map.View.Combat?.Targets.Where(t => t.Target == enemy).Select(t => $"{t.Attack}:{t.Valid}:{t.Why}") ?? Array.Empty<string>())}; "
			+ $"equipment={string.Join(",", _map.View.Equipment.Select(e => $"{e.Slot}:{e.Item}"))}; "
			+ $"combatants={string.Join(",", _map.View.Combat?.Combatants.Select(c => $"{c.Id}:{c.Condition}") ?? Array.Empty<string>())}; "
			+ $"visible={string.Join(",", _map.View.VisibleActors.Select(a => a.Id))}");
		_map.ArmAttackForSmoke("Pugnale");
		_map.ClickArmedTargetForSmoke(enemy);
		Require(_map.View.Combat is { You.OffHandAttackAvailable: true },
			$"R4b: first Light attack did not unlock the second hand: {_map.LastMessage}");
		_map.ArmAttackForSmoke("Spada corta (mano secondaria)");
		_map.ClickArmedTargetForSmoke(enemy);
		Require(_map.View.Combat is { You.OffHandAttackAvailable: false, You.BonusActionAvailable: false }
			&& _map.View.Combat.Log.Count(e => e.Kind == CombatLogKind.Attack && e.Actor == _map.View.Id) >= 2,
			$"R4b: the off-hand attack was not sent from the action bar: {_map.LastMessage}");
		// The swing lasts 0.42 s: watch for it frame by frame instead of sampling one instant.
		var swung = false;
		for (var frame = 0; frame < 60 && !swung; frame++)
		{
			await Frames(1);
			swung = _map.AttackSwingForSmoke(_map.View.Id);
		}
		Require(swung, "Visuals: the live figure did not swing after the attack");
		Capture("r4-offhand-attack.png");

		var original = System.IO.Path.Combine(_outputDir, "g13-guard-joined.json");
		var unarmed = JsonNode.Parse(File.ReadAllText(original))!.AsObject();
		var unarmedPlayer = unarmed["Actors"]!.AsArray().Single(a => a!["Id"]!.GetValue<string>() == MappedVillageScenario.Ids.Player.Value)!;
		unarmedPlayer["Equipment"]!["Shield"] = null;
		unarmedPlayer["Equipment"]!["MainHand"] = null;
		var unarmedSave = System.IO.Path.Combine(_outputDir, "r4-free-hands-fight.json");
		File.WriteAllText(unarmedSave, unarmed.ToJsonString());
		_map.LoadGameFromPath(unarmedSave);
		await Frames(2);
		_map.ArmAttackForSmoke("Colpo senz'armi");
		_map.SelectManeuverForSmoke(1);
		_map.ClickArmedTargetForSmoke(enemy);
		Require(_map.View.Combat is { You.ActionAvailable: false }
			&& _map.View.Combat.Log.Any(e => e.Kind == CombatLogKind.Condition && e.Text.Contains("afferrare")),
			$"R4a: Afferra was not sent by the unarmed attack menu: {_map.LastMessage}");
		await Frames(2);
		Capture("r4-grapple-attempt.png");

		_map.LoadGameFromPath(original);
		await Frames(2);
		_map.ArmAttackForSmoke("Colpo senz'armi");
		_map.SelectManeuverForSmoke(3);
		_map.ClickArmedTargetForSmoke(enemy);
		Require(_map.View.Combat is { You.ActionAvailable: false }
			&& _map.View.Combat.Log.Any(e => e.Kind == CombatLogKind.Condition && e.Text.Contains("buttare a terra")),
			$"R4a: Spingi a terra was not sent by the unarmed attack menu: {_map.LastMessage}");

		var held = JsonNode.Parse(File.ReadAllText(original))!.AsObject();
		var heldFarmer = held["Actors"]!.AsArray().Single(a => a!["Id"]!.GetValue<string>() == enemy.Value)!;
		heldFarmer["Conditions"] = new JsonArray(new JsonObject { ["Kind"] = "Grappled", ["Source"] = MappedVillageScenario.Ids.Player.Value });
		var heldSave = System.IO.Path.Combine(_outputDir, "r4-held-farmer.json");
		File.WriteAllText(heldSave, held.ToJsonString());
		_map.LoadGameFromPath(heldSave);
		await Frames(2);
		Require(_map.ReleaseEnabledForSmoke, "R4a: Lascia la presa is not enabled for the held creature");
		_map.PressReleaseForSmoke();
		Require(_map.View.VisibleActors.First(a => a.Id == enemy).Conditions.All(c => c.Kind != ConditionKind.Grappled),
			$"R4a: Lascia la presa did not free the held creature: {_map.LastMessage}");

		foreach (var (action, text) in new[] { (CombatAction.Search, "cerca"), (CombatAction.Ready, "prepara") })
		{
			_map.LoadGameFromPath(original);
			await Frames(2);
			_map.PressCombatActionForSmoke(action);
			Require(_map.View.Combat is { You.ActionAvailable: false }
				&& _map.View.Combat.Log.Any(e => e.Text.Contains(text, StringComparison.OrdinalIgnoreCase)),
				$"R4c: {action} did not run from the compact action menu: {_map.LastMessage}");
		}
		_map.LoadGameFromPath(original);
		await Frames(2);
		_map.PressCombatActionForSmoke(CombatAction.Hide);
		Require(_map.LastMessage.Contains("Ti vedono") || _map.View.Combat is { You.ActionAvailable: false },
			$"R4c: Nasconditi did not reach the Sim from the menu: {_map.LastMessage}");
	}

	/// <summary>R3b/R3c: the attack bar forwards both the Smite and Sacred Weapon choices to a real fight.</summary>
	private async Task PaladinAttackFeatures()
	{
		var source = System.IO.Path.Combine(_outputDir, "g13-guard-joined.json");
		var snapshot = JsonNode.Parse(File.ReadAllText(source))!.AsObject();
		var player = snapshot["Actors"]!.AsArray().Single(a => a!["Id"]!.GetValue<string>() == MappedVillageScenario.Ids.Player.Value)!;
		player["Experience"] = 900;
		player["Vitality"]!["Maximum"] = 25;
		player["Vitality"]!["Current"] = 25;
		player["Subclass"] = "devotion";
		var testSave = System.IO.Path.Combine(_outputDir, "r3bc-guard-fight.json");
		File.WriteAllText(testSave, snapshot.ToJsonString());
		_map.LoadGameFromPath(testSave);
		await Frames(2);
		var melee = _map.View.Combat?.Targets.FirstOrDefault(t => t.Attack == "Spada lunga" && t.Valid);
		Require(_map.View.Combat is { IsYourTurn: true, You.ActionAvailable: true } && melee is not null,
			$"R3bc: no melee target for the Paladin attack controls: "
			+ $"player={_map.View.Position}, options={string.Join("; ", _map.View.Combat?.Targets.Where(t => t.Attack == "Spada lunga").Select(t => $"{t.Target}:{t.Valid}:{t.Why}") ?? Array.Empty<string>())}");
		_map.SelectCombatantForSmoke(melee!.Target);
		_map.PressCombatActionForSmoke(CombatAction.Help);
		Require(_map.View.Combat is { You.ActionAvailable: false }
			&& _map.View.Combat.Log.Any(e => e.Text.Contains("distrae")),
			$"R4c: Aiuta did not receive the selected target from the action bar: {_map.LastMessage}");
		_map.LoadGameFromPath(testSave);
		await Frames(2);
		var before = _map.View.Combat!.Log.Count;
		_map.ArmAttackForSmoke("Spada lunga");
		_map.SelectAttackFeaturesForSmoke(freeSmite: true, sacredWeapon: true);
		Require(_map.ClickArmedTargetForSmoke(melee!.Target), "R3bc: the attack mode ignored the target");
		Require(_map.View.ChannelDivinity is { Remaining: 1, Max: 2 }
			&& _map.View.Effects.Any(e => e.Spell == "sacred-weapon"),
			$"R3c: Arma sacra was not applied from the attack bar: {_map.LastMessage}");
		var attack = _map.View.Combat?.Log.Skip(before).FirstOrDefault(e => e.Kind == CombatLogKind.Attack && e.Actor == _map.View.Id);
		Require(attack is not null && (attack.Hit != true || _map.View.Spells?.FreeSmite == false),
			$"R3b: Punizione gratuita was not carried by the attack bar: {_map.LastMessage}");
		await Frames(2);
		Capture("r3bc-sacred-smite.png");
	}

	/// <summary>G14: the C3a choices are usable in the village HUD, including a saved disengaged turn.</summary>
	private async Task CombatActions()
	{
		var joined = System.IO.Path.Combine(_outputDir, "g13-guard-joined.json");
		_map.SetPaused(true);
		_map.LoadGameFromPath(joined);
		await Frames(2);
		Require(_map.View.Combat is { IsYourTurn: true, You.ActionAvailable: true },
			"G14: the loaded fight is not at the player's action");
		Require(_map.View.Combat!.Actions.Where(a => a.Valid).All(a => _map.CombatActionEnabledForSmoke(a.Action)),
			"G14: a valid combat action button is disabled");
		var normalMove = _map.View.Combat!.You.MovementLeftFeet;
		_map.PressCombatActionForSmoke(CombatAction.Dash);
		Require(_map.View.Combat is { } dashed && dashed.You.MovementLeftFeet == normalMove + 30
			&& !dashed.You.ActionAvailable && dashed.Log.Any(e => e.Kind == CombatLogKind.Dash),
			"G14: Scatto did not update the real fight and HUD");
		await Frames(2);
		Capture("38-g14-dash.png");

		_map.LoadGameFromPath(joined);
		await Frames(2);
		var escapeSquare = new GridPos(13, 9);
		Require(_map.CombatMovePreviewForSmoke(escapeSquare).Provokes.Count > 0,
			"G14: the retreat does not expose an opportunity attack before disengaging");
		_map.PressCombatActionForSmoke(CombatAction.Disengage);
		Require(_map.View.Combat is { You.Disengaged: true, You.ActionAvailable: false },
			"G14: Disimpegno is missing from the player status");
		Require(Enum.GetValues<CombatAction>().All(a => !_map.CombatActionEnabledForSmoke(a)),
			"G14: actions remain enabled after spending the action");
		Require(_map.CombatMovePreviewForSmoke(escapeSquare).Provokes.Count == 0,
			"G14: Disimpegno did not remove known opportunity attacks");
		var midTurn = System.IO.Path.Combine(_outputDir, "g14-disengaged-mid-turn.json");
		_map.SaveGameToPath(midTurn);
		_map.LoadGameFromPath(midTurn);
		Require(_map.View.Combat is { You.Disengaged: true, You.ActionAvailable: false },
			"G14: reloading mid-turn lost Disimpegno or action use");
		await Frames(2);
		Capture("39-g14-disengaged.png");
		var opportunityAttacks = _map.View.Combat!.Log.Count(e => e.Kind == CombatLogKind.OpportunityAttack);
		_map.MoveTo(escapeSquare);
		Require(_map.View.Position == escapeSquare && _map.View.Combat is { } escaped
			&& escaped.Log.Count(e => e.Kind == CombatLogKind.OpportunityAttack) == opportunityAttacks,
			"G14: the player's disengaged retreat triggered an opportunity attack");
		_map.EndCombatTurnForSmoke();
		for (var i = 0; i < 60 && _map.View.Combat is not { IsYourTurn: true, You.ActionAvailable: true }; i++)
			await Frames(1);
		Require(_map.View.Combat is { IsYourTurn: true, You.ActionAvailable: true },
			"G14: the next player turn did not arrive after the retreat");
		_map.PressCombatActionForSmoke(CombatAction.Dodge);
		Require(_map.View.Combat is { You.Dodging: true, You.ActionAvailable: false } dodging
			&& dodging.Log.Any(e => e.Kind == CombatLogKind.Dodge),
			"G14: Schivata did not appear in the real fight and HUD");
		var dodgingSave = System.IO.Path.Combine(_outputDir, "g14-dodging-mid-turn.json");
		_map.SaveGameToPath(dodgingSave);
		_map.LoadGameFromPath(dodgingSave);
		Require(_map.View.Combat is { You.Dodging: true, You.ActionAvailable: false },
			"G14: reloading mid-turn lost Schivata or action use");
		await Frames(2);
		Require(_map.PlayerDodgeMarkVisibleForSmoke,
			"G14: Schivata is not marked on the player token");
		Capture("40-g14-dodge.png");
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
		Require(_map.SmokeWorld.RecentFacts.Any(f => f.Kind == "JoinedFight" && f.Description.StartsWith("Guardia")),
			"G13: the guard did not join the fight after witnessing the assault");
		var fight = _map.View.Combat;
		Require(fight is not null && fight.Order.Any(c => c.Actor == guard),
			"G13: the 360-degree combat view did not reveal the guard in the initiative order");
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
		Require(_map.AttackVisualActiveForSmoke(_map.View.Id),
			"G15: the opening attack did not trigger its visual animation");
		Require(_map.View.Combat!.Log.Any(e => e.Kind == CombatLogKind.Attack && e.Actor == _map.View.Id
			&& e.ShotFrom is not null && e.ShotTo is not null) && _map.ShotVisualActiveForSmoke,
			"P2b: the opening javelin did not use the resolved straight shot");
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
		var usedArmedAttack = false;
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
				{
					if (!usedArmedAttack)
					{
						_map.ArmAttackForSmoke(attack.Attack);
						Require(_map.ClickArmedTargetForSmoke(raider) && !_map.AttackModeForSmoke,
							"UX: armed attack did not strike the selected combat target");
						usedArmedAttack = true;
					}
					else _map.AttackCombatTargetForSmoke(attack);
				}
				else if (fight.Reachable.Count > 0)
					_map.MoveTo(fight.Reachable[0].At);
			}
			if (_map.View.Combat is { IsYourTurn: true })
				_map.EndCombatTurnForSmoke();
			await Frames(3); // the client advances NextRoundAt between rounds
		}
		Require(_map.View.Combat is null || _map.View.Dead, "G11: duel did not reach an ending");
		var lastFight = _map.View.LastFight;
		Require(lastFight is { Count: > 0 } && lastFight[^1].Kind == CombatLogKind.Ended,
			"P2b: the final public fight log is unavailable after combat ends");
		var finalAttack = lastFight!.LastOrDefault(e => e.Kind is CombatLogKind.Attack or CombatLogKind.OpportunityAttack);
		Require(finalAttack?.Actor is { } finalAttacker && _map.AttackVisualActiveForSmoke(finalAttacker),
			"P2b: the final attack did not animate from LastFight");
		Require(highestRound >= 2 && _map.View.Now > beforeLoad.Now,
			"G11: the client did not advance time between combat rounds");
		Require(woundedCaptured, "G11: no screenshot of a wounded raider");
		Require(usedArmedAttack, "UX: the duel never used the armed attack control");
		Require(_map.View.Experience == beforeLoad.Experience + 25
			&& _map.LevelExperienceTextForSmoke.Contains("Esperienza 25 / 300"),
			"C5b: defeating the raider did not update the experience shown in the HUD");
		Capture("30-duel-end.png");
		var endedSave = System.IO.Path.Combine(_outputDir, "duel-ended.json");
		_map.SaveGameToPath(endedSave);
		_map.LoadGameFromPath(endedSave);
		Require(_map.View.LastFight?.Count == lastFight!.Count && !_map.ShotVisualActiveForSmoke,
			"P2b: loading the ended fight lost its log or replayed an old shot");
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
				new TargetView { Target = raider.Id, Attack = "Giavellotto (lanciato)", Valid = true,
					Cover = CoverDegree.Half, Disadvantage = true, Reasons = new[] { "gittata lunga" } },
			},
			Log = new[] { new CombatLogEntry { Round = 1, Kind = CombatLogKind.Started,
				Text = "Il Paladino affronta il Razziatore al granaio." },
				new CombatLogEntry { Round = 1, Kind = CombatLogKind.Initiative,
				Text = "Iniziativa: Paladino 16, Razziatore 11." },
				new CombatLogEntry { Round = 1, Kind = CombatLogKind.Attack, Cover = CoverDegree.Half,
					Text = "Il Paladino tira (mezza copertura)." },
				new CombatLogEntry { Round = 1, Kind = CombatLogKind.Save,
					Text = "Il Razziatore supera il tiro salvezza su Costituzione." },
				new CombatLogEntry { Round = 1, Kind = CombatLogKind.Mastery,
					Text = "La spinta lo allontana di 10 piedi." } },
		};
		_map.PreviewCombatForSmoke(preview);
		Require(!_map.ExploreActionBarVisibleForSmoke && _map.CombatActionBarVisibleForSmoke,
			"UX: exploration controls stayed over the combat action bar");
		await Frames(3);
		Capture("25-combat-hud-preview.png");
		_map.SelectCombatantForSmoke(raider.Id);
		await Frames(2);
		Require(_map.CombatAttackLabelsForSmoke.Any(text => text.Contains("Giavellotto") && text.Contains("½ copertura"))
			&& _map.CombatLogTextForSmoke.Contains("mezza copertura"),
			"P3: the attack panel or combat log omitted the public cover degree");
		Require(_map.CombatAttackLabelsForSmoke.Any(text => text.Contains("gittata lunga"))
			&& _map.CombatLogTextForSmoke.Contains("tiro salvezza")
			&& _map.CombatLogTextForSmoke.Contains("spinta")
			&& _map.HudLogForSmoke.Contains("tiro salvezza"),
			"R2: attack reasons, saving throws or mastery lines are missing from combat HUD");
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
		var windowA = new GridPos(3, 0);
		var windowB = new GridPos(3, 1);
		_map.PreviewCombatForSmoke(preview with { You = preview.You with { InteractionAvailable = true } });
		_map.OpenWindowMenuForSmoke(windowA, windowB);
		Require(_map.BarrierActionsForSmoke.Contains("Apri (gratis)")
			&& _map.BarrierActionsForSmoke.Any(x => x == "Rompi il vetro (usa l'azione, rumoroso)"),
			"P4c: the combat window menu did not show free interaction and the Utilize action");
		_map.PreviewCombatForSmoke(preview with { You = preview.You with { InteractionAvailable = false, ActionAvailable = false } });
		_map.OpenWindowMenuForSmoke(windowA, windowB);
		Require(_map.BarrierActionsForSmoke.Contains("Apri (usa l'azione)")
			&& !_map.BarrierActionEnabledForSmoke("Apri (usa l'azione)")
			&& !_map.BarrierActionEnabledForSmoke("Rompi il vetro (usa l'azione, rumoroso)"),
			"P4c: spent combat resources did not disable window actions");
		_map.CloseBarrierMenuForSmoke();
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
		_map.OpenDoorMenuForSmoke(houseDoor);
		Require(_map.BarrierMenuVisibleForSmoke && !_map.BarrierActionsForSmoke.Any(a => a.StartsWith("Forza") || a.StartsWith("Scassina")),
			"P4a: an untried door exposed hidden lock actions");
		var locked = session.Execute(new DoorCommand { Actor = session.Player, At = houseDoor, Open = true });
		Require(!locked.Success && locked.Rejection == RejectionReason.Locked,
			"P4a: the farmhouse door did not reveal its lock when tried");
		var discoveredSave = System.IO.Path.Combine(_outputDir, "door-lock-discovered.json");
		using (var stream = File.Create(discoveredSave)) session.Save(stream);
		_map.LoadGameFromPath(discoveredSave);
		_map.OpenDoorMenuForSmoke(houseDoor);
		Require(_map.BarrierActionsForSmoke.Any(a => a.StartsWith("Forza"))
			&& !_map.BarrierActionsForSmoke.Any(a => a.StartsWith("Scassina"))
			&& _map.BarrierMarkForSmoke(houseDoor) == "CHIAVE",
			"P4a: discovered lock did not show the proper actions and marker");
		await Frames(2);
		Capture("23b-p4a-discovered-lock.png");

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

	/// <summary>P4b: the visible window state and available actions follow a real Sim save.</summary>
	private async Task WindowGlassAndBars()
	{
		var a = new GridPos(3, 0);
		var b = new GridPos(3, 1);
		var barsA = new GridPos(4, 11);
		var barsB = new GridPos(4, 12);
		var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		var walk = session.Execute(new MoveCommand { Actor = session.Player, To = b });
		Require(walk.Success, "P4b: cannot approach the inn window");
		session.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(5));
		var intactSave = System.IO.Path.Combine(_outputDir, "window-intact.json");
		using (var stream = File.Create(intactSave)) session.Save(stream);
		_map.LoadGameFromPath(intactSave);
		_map.OpenWindowMenuForSmoke(a, b);
		Require(_map.BarrierActionsForSmoke.Any(x => x.StartsWith("Rompi il vetro"))
			&& !_map.WindowShardsForSmoke(a, b) && _map.WindowBarsForSmoke(barsA, barsB),
			"P4b: intact glass or farmhouse bars are not represented");
		var broken = session.Execute(new BreakGlassCommand { Actor = session.Player, A = a, B = b });
		Require(broken.Success, "P4b: cannot break the inn window glass");
		session.AdvanceUntilCompleted(broken.Action!.Value, Duration.FromMinutes(5));
		var brokenSave = System.IO.Path.Combine(_outputDir, "window-broken.json");
		using (var stream = File.Create(brokenSave)) session.Save(stream);
		_map.LoadGameFromPath(brokenSave);
		_map.FocusForSmoke(b);
		_map.OpenWindowMenuForSmoke(a, b);
		Require(_map.WindowShardsForSmoke(a, b) && !_map.WindowPanelOpenForSmoke(a, b)
			&& _map.BarrierActionsForSmoke.Any(x => x.StartsWith("Togli i frammenti"))
			&& _map.BarrierActionsForSmoke.Contains("Scavalca")
			&& !_map.BarrierActionsForSmoke.Contains("Chiudi"),
			"P4b: broken glass did not update the artwork or actions");
		await Frames(2);
		Capture("23c-p4b-broken-window.png");
		var cleared = session.Execute(new ClearGlassCommand { Actor = session.Player, A = a, B = b });
		Require(cleared.Success, "P4b: cannot clear the broken glass");
		session.AdvanceUntilCompleted(cleared.Action!.Value, Duration.FromMinutes(5));
		var clearedSave = System.IO.Path.Combine(_outputDir, "window-cleared.json");
		using (var stream = File.Create(clearedSave)) session.Save(stream);
		_map.LoadGameFromPath(clearedSave);
		_map.OpenWindowMenuForSmoke(barsA, barsB);
		Require(!_map.WindowShardsForSmoke(a, b)
			&& _map.BarrierActionsForSmoke.Any(x => x.StartsWith("Forza le sbarre"))
			&& !_map.BarrierActionsForSmoke.Contains("Scavalca"),
			"P4b: cleared glass or visible bars did not update the artwork or actions");
		_map.FocusForSmoke(null);
	}

	/// <summary>P4c: the combat window button reaches the actual Sim command and spends the action.</summary>
	private async Task CombatWindowAction()
	{
		var a = new GridPos(3, 0);
		var b = new GridPos(3, 1);
		var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		var move = session.Execute(new MoveCommand { Actor = session.Player, To = b });
		Require(move.Success, "P4c: cannot approach the inn window");
		session.AdvanceUntilCompleted(move.Action!.Value, Duration.FromMinutes(5));
		move = session.Execute(new MoveCommand { Actor = MappedVillageScenario.Ids.Guard, To = new GridPos(4, 1) });
		Require(move.Success, "P4c: cannot position the guard by the window");
		session.AdvanceUntilCompleted(move.Action!.Value, Duration.FromMinutes(5));
		var attack = session.Execute(new AttackCommand { Actor = session.Player, Target = MappedVillageScenario.Ids.Guard,
			Attack = "Colpo senz'armi", NonLethal = true });
		Require(attack.Success && session.GetPlayerView().Combat is not null,
			$"P4c: cannot start a fight by the window: {attack.Message}");
		if (session.GetPlayerView().Combat is { IsYourTurn: true })
			session.Execute(new EndTurnCommand { Actor = session.Player });
		for (var i = 0; i < 12 && session.GetPlayerView().Combat is { IsYourTurn: false }; i++)
			session.Advance(Duration.FromSeconds(1));
		Require(session.GetPlayerView().Combat is { IsYourTurn: true, You.ActionAvailable: true },
			"P4c: no playable turn by the window");
		var combatSave = System.IO.Path.Combine(_outputDir, "window-combat.json");
		using (var stream = File.Create(combatSave)) session.Save(stream);
		_map.LoadGameFromPath(combatSave);
		_map.RightClickWindowForSmoke(a, b);
		var label = "Rompi il vetro (usa l'azione, rumoroso)";
		Require(_map.BarrierMenuVisibleForSmoke && _map.BarrierActionEnabledForSmoke(label),
			"P4c: right-click did not open the combat window menu");
		await Frames(2);
		Capture("23d-p4c-combat-window-menu.png");
		_map.PressBarrierActionForSmoke(label);
		Require(_map.View.Map!.Edges.Single(e => e.A == a && e.B == b).Glass == WindowGlass.Broken
			&& _map.View.Combat is { You.ActionAvailable: false },
			$"P4c: the combat menu did not break glass and spend the action: {_map.LastMessage}");
		await Frames(2);
		Capture("23d-p4c-combat-window.png");
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
		Require(_map.NpcLive3dLoadedForSmoke(raider), "Visuals: the raider does not use the live 3D figure");
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
		var access = MappedVillageScenario.Ids.GranaryAccess;
		session.FacePlayer(access.X - lookout.X, access.Y - lookout.Y);
		var atGranary = System.IO.Path.Combine(_outputDir, "raid-at-granary.json");
		using (var stream = File.Create(atGranary)) session.Save(stream);
		_map.LoadGameFromPath(atGranary);
		await Frames(3);
		Require(_map.SmokeWorld.Actors.Single(a => a.Id == raider).Position == MappedVillageScenario.Ids.GranaryAccess,
			"The raider did not reach the declared granary access");
		var raiderAtGranary = _map.SmokeWorld.Actors.Single(a => a.Id == raider).Position!.Value;
		_map.FaceForSmoke(raiderAtGranary.X - lookout.X, raiderAtGranary.Y - lookout.Y);
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

	/// <summary>P5a: a hungry band chooses the pantry when it avoids the granary; the client shows the raider at the lock.</summary>
	private async Task NightFarmhouseBreakIn()
	{
		var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		var lookout = new GridPos(10, 14);
		var walk = session.Execute(new MoveCommand { Actor = session.Player, To = lookout });
		Require(walk.Success, $"P5a: cannot reach the farmhouse lookout: {walk.Message}");
		session.AdvanceUntilCompleted(walk.Action!.Value, Duration.FromMinutes(5));
		Require(session.GetPlayerView().Position == lookout, "P5a: player did not reach the farmhouse lookout");

		// Seed the prior report that the granary is guarded, without replaying a whole first raid in this client smoke.
		using var initial = new MemoryStream();
		session.Save(initial);
		var snapshot = JsonNode.Parse(initial.ToArray())!.AsObject();
		var bandits = snapshot["Factions"]!.AsArray().Single(f => f!["Id"]!.GetValue<string>() == MappedVillageScenario.Ids.Bandits.Value)!.AsObject();
		bandits["AvoidUntil"]!.AsArray().Add(new JsonObject
		{
			["Store"] = MappedVillageScenario.Ids.GranaryStore.Value,
			["Until"] = new GameTime(9 * 86400).Ticks,
		});
		var camp = snapshot["Stores"]!.AsArray().Single(s => s!["Id"]!.GetValue<string>() == MappedVillageScenario.Ids.CampStore.Value)!.AsObject();
		camp["Contents"]!.AsArray().Single(i => i!["Item"]!.GetValue<string>() == "ration")!["Count"] = 4;
		snapshot["RngState"] = 1UL;
		using var seeded = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(snapshot.ToJsonString()));
		var loaded = SimulationSession.TryLoad(seeded);
		Require(loaded.Session is not null, $"P5a: cannot load the seeded raid: {loaded.Error}");
		session = loaded.Session!;
		session.FacePlayer(MappedVillageScenario.Ids.HouseDoor.X - lookout.X,
			MappedVillageScenario.Ids.HouseDoor.Y - lookout.Y);

		var raider = MappedVillageScenario.Ids.Raider;
		var houseDoor = MappedVillageScenario.Ids.HouseDoor;
		VisibleActorView? working = null;
		var deadline = new GameTime(8 * 3600);
		while (session.Now < deadline)
		{
			var actor = session.GetWorldView().Actors.Single(a => a.Id == raider);
			var nearHouse = actor.Assignment?.Target == MappedVillageScenario.Ids.Pantry
				&& actor.Position is { } at && Math.Abs(at.X - houseDoor.X) <= 5 && Math.Abs(at.Y - houseDoor.Y) <= 5;
			if (nearHouse && session.GetPlayerView().TorchLitUntil is null)
			{
				var torch = session.Execute(new TorchCommand { Actor = session.Player, Lit = true,
					StowShield = true });
				Require(torch.Success, $"P5a: cannot light the lookout at {session.Now}: {torch.Message}");
			}
			working = session.GetPlayerView().VisibleActors.FirstOrDefault(a => a.Id == raider && a.AtBarrier is not null);
			if (working is not null) break;
			session.Advance(Duration.FromSeconds(nearHouse ? 10 : 60));
		}
		var finalRaider = session.GetWorldView().Actors.Single(a => a.Id == raider);
		Require(working?.AtBarrier == new BarrierRefView { A = houseDoor, B = houseDoor },
			$"P5a: no visible raider at the farmhouse lock by 08:00; time {session.Now}, raider {finalRaider.Location}/{finalRaider.Position}, assignment {finalRaider.Assignment?.Target}, decision {finalRaider.LastDecision?.Rule}, recent {string.Join(',', session.GetWorldView().RecentFacts.TakeLast(8).Select(f => f.Kind))}");
		Require(working!.Doing is { Length: > 0 }, "P5a: the raider's lock work has no visible gesture");
		var atDoor = System.IO.Path.Combine(_outputDir, "p5a-raider-at-farmhouse-door.json");
		using (var stream = File.Create(atDoor)) session.Save(stream);
		_map.SetPaused(true);
		_map.LoadGameFromPath(atDoor);
		_map.FocusForSmoke(houseDoor);
		await Frames(3);
		Require(_map.View.VisibleActors.Any(a => a.Id == raider && a.AtBarrier == working.AtBarrier),
			"P5a: the loaded client did not see the raider working at the lock");
		Require(_map.BarrierActivityForSmoke(working.AtBarrier!),
			"P5a: the client did not highlight the door being picked");
		Capture("p5a-raider-picks-farmhouse-door.png");

		for (var i = 0; i < 120 && session.GetPlayerView().Doors.Single(d => d.At == houseDoor).Open != true; i++)
			session.Advance(Duration.FromSeconds(10));
		Require(session.GetPlayerView().Doors.Single(d => d.At == houseDoor).Open == true,
			"P5a: the raider never opened the farmhouse door after working on it");
		Require(session.GetWorldView().RecentFacts.Any(f => f.Kind is "LockPicked" or "Forced"),
			"P5a: the lock did not yield to picking or forcing");
		var opened = System.IO.Path.Combine(_outputDir, "p5a-farmhouse-door-open.json");
		using (var stream = File.Create(opened)) session.Save(stream);
		_map.LoadGameFromPath(opened);
		await Frames(3);
		Capture("p5a-farmhouse-door-open.png");
	}

	/// <summary>R1: a wounded paladin spends one Hit Die, sleeps, and can stop a further rest through the UI.</summary>
	private async Task RestAndHealing()
	{
		var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		using var initial = new MemoryStream();
		session.Save(initial);
		var snapshot = JsonNode.Parse(initial.ToArray())!.AsObject();
		var player = snapshot["Actors"]!.AsArray().Single(a => a!["Id"]!.GetValue<string>() == MappedVillageScenario.Ids.Player.Value)!;
		player["Vitality"]!["Current"] = 4;
		var wounded = System.IO.Path.Combine(_outputDir, "r1-wounded.json");
		File.WriteAllText(wounded, snapshot.ToJsonString());
		_map.SetPaused(false);
		_map.LoadGameFromPath(wounded);
		await Frames(3);
		Require(_map.View.HitDice is { Remaining: 1, Max: 1, Die: 10 }
			&& _map.RestResourcesForSmoke.Contains("1/1 (d10)")
			&& _map.RestResourcesForSmoke.Contains("PF 4/11"),
			"R1: the client did not show the paladin's Hit Points and Hit Dice");
		Require(_map.RestButtonsEnabledForSmoke, "R1: rest controls unavailable while the paladin is standing idle");
		Capture("r1-wounded-before-rest.png");

		_map.StartRestForSmoke(longRest: false, dice: 1);
		Require(_map.View.Action is { Kind: "Rest", Description: "Riposa" },
			$"R1: short rest did not start: {_map.LastMessage}");
		await Frames(2);
		Capture("r1-short-rest.png");
		await UntilIdle(7);
		Require(_map.View.Action is null && _map.View.HitDice is { Remaining: 0 }
			&& _map.EventText.Contains("finisce di riposare"),
			$"R1: short rest did not finish and spend its Hit Die: {_map.LastMessage}");
		var afterShort = System.IO.Path.Combine(_outputDir, "r1-after-short.json");
		_map.SaveGameToPath(afterShort);
		var shortHp = JsonNode.Parse(File.ReadAllText(afterShort))!["Actors"]!.AsArray()
			.Single(a => a!["Id"]!.GetValue<string>() == MappedVillageScenario.Ids.Player.Value)!["Vitality"]!["Current"]!.GetValue<int>();
		Require(shortHp > 4, $"R1: spending a Hit Die did not heal the paladin ({shortHp} PF)");
		Require(_map.RestResourcesForSmoke.Contains($"PF {shortHp}/11"),
			"R1: the client did not update Hit Points after the short rest");

		_map.StartRestForSmoke(longRest: true, dice: 0);
		Require(_map.View.Action is { Kind: "Rest", Description: "Dorme" },
			$"R1: long rest did not start: {_map.LastMessage}");
		await Frames(2);
		Capture("r1-long-rest-asleep.png");
		await UntilIdle(7);
		Require(_map.View.Action is null && _map.View.HitDice is { Remaining: 1 }
			&& _map.View.LongRestFrom is not null && _map.EventText.Contains("si sveglia riposato"),
			$"R1: long rest did not restore the Hit Die and apply its cooldown: {_map.LastMessage}");
		var afterLong = System.IO.Path.Combine(_outputDir, "r1-after-long.json");
		_map.SaveGameToPath(afterLong);
		var longHp = JsonNode.Parse(File.ReadAllText(afterLong))!["Actors"]!.AsArray()
			.Single(a => a!["Id"]!.GetValue<string>() == MappedVillageScenario.Ids.Player.Value)!["Vitality"]!["Current"]!.GetValue<int>();
		Require(longHp == 11, $"R1: long rest did not restore all PF ({longHp}/11)");
		Require(_map.RestResourcesForSmoke.Contains("PF 11/11"),
			"R1: the client did not update Hit Points after the long rest");
		Capture("r1-after-long-rest.png");

		_map.StartRestForSmoke(longRest: false, dice: 0);
		Require(_map.View.Action is { Kind: "Rest" }, "R1: cannot start a rest to test interruption");
		_map.Stop();
		Require(_map.View.Action is null && _map.EventText.Contains("interrompe il riposo"),
			$"R1: Stop did not interrupt the rest: {_map.LastMessage}");
	}

	/// <summary>R2/R3a: visible status, Lay On Hands and an actual level choice through the HUD.</summary>
	private async Task ClassAndConditions()
	{
		var session = SimulationSession.Create(MappedVillageScenario.CreateLivingVillage());
		using var initial = new MemoryStream();
		session.Save(initial);
		var snapshot = JsonNode.Parse(initial.ToArray())!.AsObject();
		var player = snapshot["Actors"]!.AsArray().Single(a => a!["Id"]!.GetValue<string>() == MappedVillageScenario.Ids.Player.Value)!;
		player["Vitality"]!["Current"] = 4;
		var wounded = System.IO.Path.Combine(_outputDir, "r3a-wounded.json");
		File.WriteAllText(wounded, snapshot.ToJsonString());
		_map.LoadGameFromPath(wounded);
		await Frames(3);
		_map.SetPaused(true);
		_map.PreviewConditionsForSmoke(new[] { new ConditionView { Kind = ConditionKind.Poisoned } }, 2,
			MappedVillageScenario.Ids.Guard, new[] { new ConditionView { Kind = ConditionKind.Prone } });
		Require(_map.ConditionsTextForSmoke.Contains("Avvelenato")
			&& _map.ConditionsTextForSmoke.Contains("Sfinimento: 2/6")
			&& _map.FigureLabelForSmoke(MappedVillageScenario.Ids.Guard).Contains("Prono"),
			"R2: conditions or exhaustion are missing from the player and figure HUD");
		await Frames(2);
		Capture("r2-conditions.png");
		_map.SetPaused(false);
		_map.LayOnHandsSelfForSmoke(2);
		Require(_map.View.HitPoints == 6 && _map.View.LayOnHands is { Remaining: 3, Max: 5 }
			&& _map.ClassSummaryForSmoke.Contains("3/5"),
			$"R3a: Lay On Hands did not heal from the panel: {_map.LastMessage}");
		Capture("r3a-lay-on-hands.png");
		Require(_map.View.Spells?.Prepared.Any(s => s.Id == "cure-wounds") == true,
			"R3b: Cura ferite is missing from the prepared spellbook");
		_map.CastSpellForSmoke("cure-wounds", 1, _map.View.Id);
		Require(_map.View.HitPoints > 6 && _map.View.Spells?.Slots.Single(s => s.Level == 1).Remaining == 1
			&& _map.SpellSummaryForSmoke.Contains("1° 1/2"),
			$"R3b: Cura ferite did not use a slot and heal through the book: {_map.LastMessage}");
		await Frames(2);
		Capture("r3b-cure-wounds.png");

		player["Experience"] = 300;
		player["Vitality"]!["Maximum"] = 18; // level 2 Paladin: 11 + fixed 6 + Constitution 1
		var levelTwo = System.IO.Path.Combine(_outputDir, "r3a-level-two.json");
		File.WriteAllText(levelTwo, snapshot.ToJsonString());
		_map.LoadGameFromPath(levelTwo);
		await Frames(3);
		Require(_map.View.PendingChoices.Any(c => c.Kind == "FightingStyle"),
			$"R3a: Fighting Style choice missing on level two (level {_map.View.Level}; {_map.LastMessage})");
		_map.ChooseFirstForSmoke("Stile di combattimento");
		Require(_map.View.FightingStyle == "Archery"
			&& _map.View.PendingChoices.All(c => c.Kind != "FightingStyle"),
			$"R3a: the level choice was not applied through the UI: {_map.LastMessage}");
		await Frames(2);
		Capture("r3a-fighting-style.png");

		player["Experience"] = 900;
		player["Vitality"]!["Maximum"] = 25;
		var levelThree = System.IO.Path.Combine(_outputDir, "r3c-level-three.json");
		File.WriteAllText(levelThree, snapshot.ToJsonString());
		_map.LoadGameFromPath(levelThree);
		await Frames(3);
		var oath = _map.View.PendingChoices.Single(c => c.Kind == "Subclass");
		_map.ChooseFirstForSmoke(oath.Title);
		Require(_map.View.Subclass == "devotion" && _map.View.ChannelDivinity is { Remaining: 2, Max: 2 },
			$"R3c: oath choice or Channel Divinity missing: {_map.LastMessage}");
		var preparation = _map.View.PendingChoices.Single(c => c.Kind == "PreparedSpells");
		var preparedBefore = _map.View.Spells!.Prepared.Count;
		_map.ChooseFirstForSmoke(preparation.Title);
		Require(_map.View.PendingChoices.All(c => c.Kind != "PreparedSpells")
			&& _map.View.Spells!.Prepared.Count == preparedBefore + preparation.Picks,
			$"R3b: spell preparation did not apply through the choice panel: {_map.LastMessage}");
		_map.UseDivineSenseForSmoke();
		Require(_map.View.ChannelDivinity is { Remaining: 1, Max: 2 }
			&& _map.SpellSummaryForSmoke.Contains("Incanalare: 1/2"),
			$"R3c: Percezione divina did not use Channel Divinity from the UI: {_map.LastMessage}");
		_map.CastSpellForSmoke("bless", 1, _map.View.Id);
		Require(_map.View.Spells?.Concentrating == "bless" && _map.MagicStatusForSmoke.Contains("Concentrazione: Benedizione"),
			$"R3b: concentration is not visible after casting Benedizione: {_map.LastMessage}");
		await Frames(2);
		Capture("r3bc-divine-concentration.png");
		Require(_map.SpellOnHotbarForSmoke("bless") && _map.SpellOnHotbarForSmoke("cure-wounds"),
			"Spells: the prepared spells are not slots on the hotbar");
		_map.OpenSpellbookForSmoke("bless");
		await Frames(2);
		Require(_map.SpellbookRowsForSmoke == _map.View.Spells!.ClassSpells.Count && _map.SpellbookDetailForSmoke.Contains("1d4"),
			"Spells: the spellbook should list every class spell and describe the one chosen");
		Capture("spellbook.png");
		_map.OpenSpellbookForSmoke("bless");
		_map.ClickSpellForSmoke("cure-wounds");
		Require(_map.CastPromptVisibleForSmoke, "Spells: a spell with a target should wait for it to be clicked");
		await Frames(2);
		Capture("spell-targeting.png");
		_map.CancelCastingForSmoke();
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
		Require(_map.HasFigureSpriteForSmoke(MappedVillageScenario.Ids.Farmer),
			"G15: the farmer sprite did not load in daylight");
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
