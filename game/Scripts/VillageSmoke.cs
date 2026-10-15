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
		Require(_map.View.Level == 1 && _map.View.Experience == 0 && _map.View.NextLevelAt == 300
			&& _map.LevelExperienceTextForSmoke.Contains("Livello 1 · Esperienza 0 / 300"),
			"C5b: the player level and experience are not shown from the typed view");
		Require(_map.HasFigureSpriteForSmoke(MappedVillageScenario.Ids.Player)
			&& _map.HasFigureSpriteForSmoke(MappedVillageScenario.Ids.Guard),
			"G15: directional character sprite did not load in the village");
		Require(_map.PaladinLive3dLoadedForSmoke, "G15: the live 3D paladin is not the village default");
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
		await WindowGlassAndBars();
		await CombatHudPreview();
		await DeedJournalPreview();
		await NightDuel();
		await DaylightAssault();
		await CombatActions();
		await InventoryPreview();
		await InventoryCombat();
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

	/// <summary>G14: the C3a choices are usable in the village HUD, including a saved disengaged turn.</summary>
	private async Task CombatActions()
	{
		var joined = System.IO.Path.Combine(_outputDir, "g13-guard-joined.json");
		_map.SetPaused(true);
		_map.LoadGameFromPath(joined);
		await Frames(2);
		Require(_map.View.Combat is { IsYourTurn: true, You.ActionAvailable: true },
			"G14: the loaded fight is not at the player's action");
		Require(Enum.GetValues<CombatAction>().All(_map.CombatActionEnabledForSmoke),
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
		var lastFight = _map.View.LastFight;
		Require(lastFight is { Count: > 0 } && lastFight[^1].Kind == CombatLogKind.Ended,
			"P2b: the final public fight log is unavailable after combat ends");
		var finalAttack = lastFight!.LastOrDefault(e => e.Kind is CombatLogKind.Attack or CombatLogKind.OpportunityAttack);
		Require(finalAttack?.Actor is { } finalAttacker && _map.AttackVisualActiveForSmoke(finalAttacker),
			"P2b: the final attack did not animate from LastFight");
		Require(highestRound >= 2 && _map.View.Now > beforeLoad.Now,
			"G11: the client did not advance time between combat rounds");
		Require(woundedCaptured, "G11: no screenshot of a wounded raider");
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
					Cover = CoverDegree.Half },
			},
			Log = new[] { new CombatLogEntry { Round = 1, Kind = CombatLogKind.Started,
				Text = "Il Paladino affronta il Razziatore al granaio." },
				new CombatLogEntry { Round = 1, Kind = CombatLogKind.Initiative,
				Text = "Iniziativa: Paladino 16, Razziatore 11." },
				new CombatLogEntry { Round = 1, Kind = CombatLogKind.Attack, Cover = CoverDegree.Half,
				Text = "Il Paladino tira (mezza copertura)." } },
		};
		_map.PreviewCombatForSmoke(preview);
		await Frames(3);
		Capture("25-combat-hud-preview.png");
		_map.SelectCombatantForSmoke(raider.Id);
		await Frames(2);
		Require(_map.CombatAttackLabelsForSmoke.Any(text => text.Contains("Giavellotto") && text.Contains("½ copertura"))
			&& _map.CombatLogTextForSmoke.Contains("mezza copertura"),
			"P3: the attack panel or combat log omitted the public cover degree");
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
