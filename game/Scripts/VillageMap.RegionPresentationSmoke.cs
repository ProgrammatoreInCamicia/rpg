using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using Island = RpgSandbox.Sim.Scenarios.BlackmarshWatchIsland.Ids;

namespace RpgSandbox.Game;

/// <summary>Task 3: drive public commands; inspect the resulting render tree, never edit a running Sim.</summary>
public partial class VillageMap
{
	private async void RunRegionPresentationSmoke(string outputDir, bool fixture)
	{
		try
		{
			Directory.CreateDirectory(outputDir);
			SetPaused(true); // Deterministic command-driven smoke; travel UI is Task 4.
			var session = _sim;
			var camera = _camera;
			var hud = GetChildren().OfType<CanvasLayer>().Single();
			await SliceFrames(4);
			if (fixture) await ExercisePresentationFixture(outputDir);
			else
			{
				SliceRequire(_view.Area == Island.Hamlet && _view.Now == GameTime.Start.Plus(BlackmarshWatchIsland.Opening),
					"Region did not open at the authored place/time.");
				await PresentationCapture(outputDir, "hamlet-entry");
				PresentationCommand(new TorchCommand { Actor = _sim.Player, Lit = true, StowShield = true });
				PresentationWalk(new GridPos(8, 6));
				PresentationDoor(Island.HouseDoor, true);
				PresentationWalk(new GridPos(6, 6));
				SliceRequire(!RoofVisibleForSmoke(new GridPos(3, 5)), "Common house did not use the indoor cutaway.");
				await PresentationCapture(outputDir, "common-house");
				PresentationWalk(new GridPos(8, 6));
				PresentationDoor(Island.HouseDoor, false);
				await PresentationJourney(Island.ShedFork);
				PresentationWalk(new GridPos(5, 6));
				PresentationDoor(Island.ShedDoor, true);
				PresentationWalk(new GridPos(8, 6));
				FaceForSmoke(-1, -1);
				SliceRequire(_npcLiveModels.TryGetValue(Island.Fisher, out var workingFisher)
					&& workingFisher.Style == Paladin3DPrototype.CharacterStyle.Farmer,
					"The working fisher did not render with his regional appearance.");
				PresentationDoor(Island.InnerDoor, true);
				PresentationWalk(new GridPos(10, 5));
				FaceForSmoke(1, 1);
				SliceRequire(_interiorProps.Count == 1 && _interiorProps[0].Cell == BlackmarshWatchhouse.Ids.Table
					&& _interiorProps[0].Sprite.Visible, "Shed did not load its own table profile.");
				SliceRequire(ActorStyle(Island.Fisher) == Paladin3DPrototype.CharacterStyle.Farmer,
					"The fisher changed appearance when visiting the shed.");
				await PresentationCapture(outputDir, "shed-interior");
				PresentationWalk(new GridPos(8, 6));
				PresentationDoor(Island.InnerDoor, false);
				PresentationWalk(new GridPos(5, 6));
				PresentationDoor(Island.ShedDoor, false);
				await PresentationJourney(Island.CoveFork);
				SliceRequire(_roofTiles.Count == 0 && _interiorProps.Count == 0 && _doorPanels.Count == 0,
					"Cove inherited buildings, furniture or door picking from the shed.");
				PresentationWalk(new GridPos(14, 10));
				await PresentationCapture(outputDir, "cove");
				await PresentationJourney(Island.HamletFork);
				for (var lap = 0; lap < 2; lap++)
					foreach (var destination in lap == 0
						? new[] { Island.CoveFork, Island.ShedFork, Island.HamletFork }
						: new[] { Island.ShedFork, Island.CoveFork, Island.HamletFork })
						await PresentationJourney(destination);
				PresentationWalk(new GridPos(8, 6));
				FaceForSmoke(-1, 0);
				SliceRequire(_view.Doors.Single(d => d.At == Island.HouseDoor).Open is { } houseOpen
					&& PilotDoorOpenForSmoke(Island.HouseDoor) == houseOpen,
					"The common-house door does not reflect its observed state after NPC routines.");
				SliceRequire(_view.MapVisibility!.Any(row => row.Contains('1')), "Exploration memory was lost on return.");
				PresentationDoor(Island.HouseDoor, true);
				PresentationWalk(new GridPos(6, 6));
				FaceForSmoke(-1, 1);
				SliceRequire(_npcLiveModels.TryGetValue(Island.Fisher, out var fisher)
					&& fisher.Style == Paladin3DPrototype.CharacterStyle.Farmer,
					"The fisher's rendered model changed style at home.");
				await PresentationCapture(outputDir, "hamlet-return");
			}
			SliceRequire((fixture || ReferenceEquals(session, _sim)) && ReferenceEquals(camera, _camera)
				&& ReferenceEquals(hud, GetChildren().OfType<CanvasLayer>().Single()),
				"Travelling replaced the world, camera or HUD.");
			GD.Print($"REGION PRESENTATION SMOKE OK ({(fixture ? "fixture" : "island")}): area lifecycle, views and retained world");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"REGION PRESENTATION SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private void PresentationCommand(Command command)
	{
		var result = _sim.Execute(command);
		SliceRequire(result.Success, result.Message);
		if (result.Action is { } action)
			SliceRequire(_sim.AdvanceUntilCompleted(action, Duration.FromMinutes(20)).Outcome == AdvanceOutcome.Completed,
				$"Command did not complete: {command}");
		Refresh();
	}

	private void PresentationWalk(GridPos to)
	{
		if (_view.Position != to)
			PresentationCommand(new MoveToSpotCommand { Actor = _sim.Player, To = Spot.CenterOf(to) });
	}

	private void PresentationDoor(GridPos at, bool open)
	{
		// NPC routines can already have opened a door; consult the observed view, not an assumed initial state.
		if (_view.Doors.Single(d => d.At == at).Open != open)
			PresentationCommand(new DoorCommand { Actor = _sim.Player, At = at, Open = open });
	}

	private async Task PresentationJourney(LocationId destination)
	{
		var session = _sim;
		PresentationWalk(_view.Map!.Exits.Single().At);
		if (_view.KnownTraps.FirstOrDefault() is { } knownTrap)
		{
			OpenTrapMenu(knownTrap, new Vector2(100, 150));
			SliceRequire(_selectedTrap == knownTrap.At && _trapMenu.Visible,
				"Could not establish a local selection before departure.");
		}
		var oldRoot = _mapRoot;
		var before = _view.Now;
		var result = _sim.Execute(new TravelCommand { Actor = _sim.Player, Destination = destination });
		SliceRequire(result.Success, result.Message);
		Refresh();
		SliceRequire(!_mapRoot.Visible && _offMapPanel.Visible && _view.Position is null,
			"The origin map/overlays remained visible in transit.");
		SliceRequire(_selectedStore is null && _pendingBarrier is null && _selectedTrap is null
			&& !_trapMenu.Visible && _queue.Count == 0,
			"An old area's interaction survived departure.");
		await SliceFrames(2);
		SliceRequire(_sim.AdvanceUntilCompleted(result.Action!.Value, Duration.FromMinutes(20)).Outcome == AdvanceOutcome.Completed,
			"Travel did not arrive.");
		Refresh();
		await SliceFrames(3);
		SliceRequire(ReferenceEquals(session, _sim) && _view.Now > before && _view.Location == destination && _builtMap.Area == _view.Area && _mapRoot.Visible,
			"The renderer did not attach to the arrival map.");
		SliceRequire(!GodotObject.IsInstanceValid(oldRoot), "The old map's render tree was not freed.");
		SliceRequire(GetChildren().Count(n => n.Name == "MapPresentation") == 1
			&& GetChildren().OfType<Camera2D>().Count() == 1, "Map roots or cameras accumulate after travelling.");
		SliceRequire(_lit.Keys.All(p => p.X < _builtMap.Width && p.Y < _builtMap.Height)
			&& _doorPanels.Keys.ToHashSet().SetEquals(_view.Doors.Select(d => d.At)),
			"A geometry/picking cache contains cells from the previous map.");
	}

	private async Task PresentationCapture(string directory, string name)
	{
		await SliceFrames(5);
		CaptureBlackmarshBuilding(directory, name + ".png");
	}

	// A test-only initial world: identical door/window/trap coordinates in different areas, different states and light.
	// It uses the region profiles but never modifies the shipped content or a running world.
	private static SimulationSession CreateMapPresentationFixture()
	{
		var b = new ScenarioBuilder().WithSeed(7);
		foreach (var (area, room, fork, width, open) in new[]
		{
			(Island.Hamlet, Island.CommonHouse, Island.HamletFork, 20, false),
			(Island.ShedArea, Island.Shed, Island.ShedFork, 18, true),
		})
		{
			b.AddArea(area.Value, area.Value).AddLocation(room.Value, room.Value, area.Value)
				.AddLocation(fork.Value, fork.Value, area.Value)
				.AddMap(area.Value, Enumerable.Range(0, 14).Select(y => new string(Enumerable.Range(0, width)
					.Select(x => x == 0 ? 'E' : x is >= 2 and <= 7 && y is >= 3 and <= 8 ? 'H' : '.').ToArray())).ToArray(),
					new Dictionary<char, string> { ['H'] = room.Value, ['E'] = fork.Value })
				.AddExit(area.Value, fork.Value, (0, 6)).AddInterior(area.Value, room.Value)
				.AddLight(area.Value, (5, 5), open ? 5 : 30, open ? 10 : 40);
			for (var x = 2; x <= 7; x++)
			{
				b.AddEdge(area.Value, (x, 2), (x, 3), MapEdgeKind.Wall);
				b.AddEdge(area.Value, (x, 8), (x, 9), MapEdgeKind.Wall);
			}
			for (var y = 3; y <= 8; y++)
			{
				b.AddEdge(area.Value, (1, y), (2, y), MapEdgeKind.Wall);
				b.AddEdge(area.Value, (7, y), (8, y), y == 6 ? MapEdgeKind.Door : y == 5 ? MapEdgeKind.Window : MapEdgeKind.Wall,
					doorAt: y == 6 ? (7, 6) : null, open: open);
			}
		}
		b.AddRoute(Island.HamletFork.Value, Island.ShedFork.Value, Duration.FromMinutes(2))
			.AddTrap(Island.Hamlet.Value, (5, 7), "pit")
			.AddGroundItem(Island.Hamlet.Value, (5, 6), "javelin", 1)
			.AddActor(Island.Player.Value, "Player", Island.CommonHouse.Value, isPlayer: true, at: (5, 6), build: "paladin");
		return SimulationSession.Create(b.Build());
	}

	private async Task ExercisePresentationFixture(string directory)
	{
		var door = new GridPos(7, 6);
		var pit = new GridPos(5, 7);
		for (var attempt = 0; attempt < 20 && _view.KnownTraps.Count == 0; attempt++)
			PresentationCommand(new SearchCommand { Actor = _sim.Player });
		SliceRequire(_trapMarks.ContainsKey(pit) && GroundMarkVisibleForSmoke(new GridPos(5, 6)),
			"The fixture did not establish actual known trap and ground-item markers.");
		FaceForSmoke(1, 0);
		var glow = DisplayGlowAt(new GridPos(6, 6));
		SliceRequire(DoorAt(CellToScreen(door)) == door && !PilotDoorOpenForSmoke(door),
			"The first area's closed door was not drawn/pickable.");
		SliceRequire(!WindowPanelOpenForSmoke(new GridPos(7, 5), new GridPos(8, 5)),
			"The first area's window did not render closed.");
		await PresentationCapture(directory, "fixture-first");
		PresentationWalk(new GridPos(6, 6));
		PresentationCommand(new DoorCommand { Actor = _sim.Player, At = door, Open = true });
		PresentationWalk(new GridPos(8, 6));
		PresentationCommand(new DoorCommand { Actor = _sim.Player, At = door, Open = false });
		await PresentationJourney(Island.ShedFork);
		PresentationWalk(new GridPos(5, 6));
		FaceForSmoke(1, 0);
		SliceRequire(_view.Doors.Any(d => d.At == door && d.Open == true) && PilotDoorOpenForSmoke(door),
			"The second area's open door inherited the first area's closed state.");
		SliceRequire(WindowPanelOpenForSmoke(new GridPos(7, 5), new GridPos(8, 5)),
			"The second area's window inherited the first area's closed state.");
		SliceRequire(_trapMarks.Count == 0 && _groundMarks.Count == 0 && DisplayGlowAt(new GridPos(6, 6)) < glow,
			"Trap, ground items or lighting leaked through equal coordinates.");
		await PresentationCapture(directory, "fixture-second");
		PresentationWalk(new GridPos(8, 6));
		await PresentationJourney(Island.HamletFork);
		PresentationWalk(new GridPos(8, 6));
		FaceForSmoke(-1, 0);
		SliceRequire(_view.Doors.Single(d => d.At == door).Open == false && !PilotDoorOpenForSmoke(door),
			"The closed door did not persist while its area was away.");
		PresentationCommand(new DoorCommand { Actor = _sim.Player, At = door, Open = true });
		PresentationWalk(new GridPos(5, 6));
		SliceRequire(_trapMarks.ContainsKey(pit) && GroundMarkVisibleForSmoke(new GridPos(5, 6)),
			"Known local markers did not return with their own area.");
		// Exercise the existing accepted same-map load; region-wide save selection/validation is Task 5.
		var save = Path.Combine(directory, "same-area.json");
		SaveGameToPath(save);
		var oldRoot = _mapRoot;
		LoadGameFromPath(save);
		await SliceFrames(3);
		SliceRequire(!GodotObject.IsInstanceValid(oldRoot) && _trapMarks.ContainsKey(pit)
			&& GroundMarkVisibleForSmoke(new GridPos(5, 6)), "An accepted same-area load did not rebuild local presentation.");
	}
}
