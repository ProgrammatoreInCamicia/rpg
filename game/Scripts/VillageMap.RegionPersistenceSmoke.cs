using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using Island = RpgSandbox.Sim.Scenarios.BlackmarshWatchIsland.Ids;

namespace RpgSandbox.Game;

/// <summary>Run seed and resume in separate Godot processes to check the actual regional Continue flow.</summary>
public partial class VillageMap
{
	private async void RunRegionPersistenceSmoke(string outputDir, bool resume)
	{
		try
		{
			Directory.CreateDirectory(outputDir);
			SliceRequire(_regionTitleOpen && _paused, "The regional main entry did not open at the title in pause.");
			await SliceFrames(2);
			if (!resume)
			{
				CaptureBlackmarshBuilding(outputDir, "region-title.png");
				HideRegionTitle();
				_gameMenu.Show();
				await SliceFrames(2);
				CaptureBlackmarshBuilding(outputDir, "region-game-menu.png");
				_gameMenu.Hide();
			}
			if (resume) await ResumeRegionSnapshots(outputDir);
			else SeedRegionSnapshots(outputDir);
			GD.Print($"REGION PERSISTENCE SMOKE OK ({(resume ? "resume" : "seed")}): three areas, travel, pending action, identity and title");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"REGION PERSISTENCE SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private void SeedRegionSnapshots(string directory)
	{
		var hamlet = NewRegionSmokeSession();
		RegionCommand(hamlet, new TorchCommand { Actor = hamlet.Player, Lit = true, StowShield = true });
		RegionWalk(hamlet, new GridPos(16, 14));
		RegionDoor(hamlet, Island.HouseDoor, true);
		RegionWalk(hamlet, new GridPos(13, 14));
		SaveRegionCase(directory, "hamlet", hamlet);

		var shed = NewRegionSmokeSession();
		RegionTravel(shed, Island.ShedFork);
		RegionWalk(shed, new GridPos(21, 16));
		RegionDoor(shed, Island.ShedDoor, true);
		RegionWalk(shed, new GridPos(24, 15));
		RegionWalk(shed, new GridPos(25, 16));
		RegionDoor(shed, Island.InnerDoor, true);
		RegionWalk(shed, new GridPos(27, 15));
		var glass = shed.GetPlayerView().Map!.Edges.Single(edge => edge.Kind == MapEdgeKind.Window);
		RegionWalk(shed, glass.A);
		RegionCommand(shed, new BreakGlassCommand { Actor = shed.Player, A = glass.A, B = glass.B });
		SaveRegionCase(directory, "shed", shed);

		var cove = NewRegionSmokeSession();
		RegionTravel(cove, Island.CoveFork);
		RegionWalk(cove, new GridPos(36, 18));
		SaveRegionCase(directory, "cove", cove);

		var transit = NewRegionSmokeSession();
		RegionWalk(transit, transit.GetPlayerView().Map!.Exits.Single().At);
		var journey = transit.Execute(new TravelCommand { Actor = transit.Player, Destination = Island.ShedFork });
		SliceRequire(journey.Success, journey.Message);
		transit.Advance(Duration.FromMinutes(2));
		SliceRequire(transit.GetPlayerView().Travel is not null && transit.GetPlayerView().Position is null,
			"The transit fixture arrived before saving.");
		SaveRegionCase(directory, "transit", transit);

		RegionWalk(shed, Island.StoreAccess);
		var take = shed.Execute(new TakeFoodCommand { Actor = shed.Player, Store = Island.ShedStore, Amount = 2 });
		SliceRequire(take.Success && shed.GetPlayerView().Action is not null, take.Message);
		SaveRegionCase(directory, "action", shed);
		SliceRequire(!_regionContinue.Disabled || !File.Exists(SaveFilePath),
			"The title Continue state is inconsistent with the existing slot.");
	}

	private async Task ResumeRegionSnapshots(string directory)
	{
		var slot = SaveFilePath;
		var previous = File.Exists(slot) ? File.ReadAllBytes(slot) : null;
		try
		{
			foreach (var name in new[] { "hamlet", "shed", "cove", "transit", "action" })
			{
				File.Copy(Path.Combine(directory, $"{name}.json"), slot, overwrite: true);
				ShowRegionTitle();
				SliceRequire(!_regionContinue.Disabled, "Continue is disabled for a valid regional slot.");
				_regionContinue.EmitSignal(BaseButton.SignalName.Pressed);
				File.WriteAllBytes(Path.Combine(directory, $"{name}.loaded.json"), RegionSnapshot(_sim));
				SliceRequire(!_regionTitleOpen && _paused && RegionSnapshot(_sim).SequenceEqual(
					File.ReadAllBytes(Path.Combine(directory, $"{name}.snapshot.json"))),
					$"Continue changed the {name} snapshot across process restart.");
				SliceRequire(_view.Area == _sim.GetPlayerView().Area && _view.Now == _sim.Now,
					$"The {name} map/time was not presented from the loaded session.");
				if (name == "hamlet")
					SliceRequire(_view.Doors.Single(d => d.At == Island.HouseDoor).Open == true
						&& _view.TorchLitUntil is not null && _view.MapVisibility!.Any(row => row.Contains('1')),
						"Hamlet door, torch or exploration memory changed on restart.");
				if (name == "shed")
					SliceRequire(_view.Map!.Edges.Single(edge => edge.Kind == MapEdgeKind.Window).Glass == WindowGlass.Broken
						&& _view.KnownBarriers.Count > 0 && _view.Doors.Any(d => d.At == Island.InnerDoor && d.Open == true),
						"Shed glass, door or opening knowledge changed on restart.");
				if (name == "transit")
					SliceRequire(_view.Travel is not null && _view.Position is null && !_mapRoot.Visible
						&& _running == _view.Travel.Action,
						"Mid-journey Continue exposed the origin or lost the journey action.");
				if (name == "action")
					SliceRequire(_view.Action?.Kind == "TakeFood" && _running == _view.Action.Id,
						"Pending food action was lost on restart.");
				if (name is "transit" or "action")
				{
					SetPaused(false);
					for (var frame = 0; frame < 1200 && _view.Action is not null; frame++)
						await SliceFrames(1);
					SliceRequire(_view.Action is null, $"The resumed {name} action did not finish.");
					if (name == "transit")
						SliceRequire(_view.Location == Island.ShedFork && _mapRoot.Visible,
							"The resumed journey did not reach the shed map.");
					else SliceRequire(_view.Food == 12 && _sim.GetWorldView().Stores.Single(s => s.Id == Island.ShedStore).Food == 2,
							"The resumed take did not transfer exactly two rations.");
				}
			}
			_gameMenu.Show();
			_gameMenu.GetChildren().OfType<VBoxContainer>().Single().GetChildren().OfType<Button>()
				.Single(button => button.Text == "Carica").EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_regionConfirm.Visible && _paused, "Carica replaced the session without confirmation.");
			var beforeCancel = _sim;
			CancelRegionConfirmation();
			SliceRequire(!_regionConfirm.Visible && ReferenceEquals(beforeCancel, _sim),
				"Cancelling the load confirmation replaced the session.");
			_gameMenu.Show();
			_gameMenu.GetChildren().OfType<VBoxContainer>().Single().GetChildren().OfType<Button>()
				.Single(button => button.Text == "Nuova partita").EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_regionConfirm.Visible && ReferenceEquals(beforeCancel, _sim),
				"Nuova partita replaced the session without confirmation.");
			CancelRegionConfirmation();
			_gameMenu.Show();
			_gameMenu.GetChildren().OfType<VBoxContainer>().Single().GetChildren().OfType<Button>()
				.Single(button => button.Text == "Torna al titolo").EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_regionConfirm.Visible && !_regionTitleOpen && ReferenceEquals(beforeCancel, _sim),
				"Returning to the title skipped confirmation.");
			CancelRegionConfirmation();

			ShowRegionTitle();
			var live = _sim;
			var root = _mapRoot;
			var valid = File.ReadAllBytes(slot);
			File.WriteAllText(slot, "{broken json");
			_regionContinue.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(ReferenceEquals(_sim, live) && ReferenceEquals(_mapRoot, root) && _regionTitleOpen
				&& _regionTitleError.Text.StartsWith("Caricamento non riuscito:", StringComparison.Ordinal),
				"A damaged save replaced the running region or hid the error.");
			using (var foreign = new MemoryStream())
			{
				PlayableSlice.CreateSession().Save(foreign);
				File.WriteAllBytes(slot, foreign.ToArray());
			}
			_regionContinue.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(ReferenceEquals(_sim, live) && _regionTitleOpen && File.Exists(slot),
				"A raw save from the old slice was accepted as the region.");
			File.WriteAllBytes(slot, valid);
			var invalid = Path.Combine(directory, "invalid-region.json");
			File.WriteAllBytes(invalid, RegionSaveBytes(PlayableSlice.CreateSession()));
			SliceRequire(!LoadGameFromPath(invalid) && ReferenceEquals(_sim, live)
				&& File.ReadAllBytes(slot).SequenceEqual(valid) && ReferenceEquals(_mapRoot, root),
				"A foreign Sim wrapped in the regional identity replaced the session or touched the valid slot.");
			var olderRevision = Path.Combine(directory, "wrong-revision.json");
			File.WriteAllText(olderRevision, File.ReadAllText(slot).Replace("\"Revision\":1", "\"Revision\":99"));
			SliceRequire(!LoadGameFromPath(olderRevision) && ReferenceEquals(_sim, live)
				&& File.ReadAllBytes(slot).SequenceEqual(valid),
				"An unsupported client save revision replaced the region or touched the valid slot.");
			_regionContinue.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(!_regionTitleOpen && !ReferenceEquals(_sim, live),
				"Restoring the valid regional slot did not resume it.");
			_gameMenu.Show();
			_gameMenu.GetChildren().OfType<VBoxContainer>().Single().GetChildren().OfType<Button>()
				.Single(button => button.Text == "Salva ed esci").EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(File.Exists(slot) && ReadRegionSave(slot).Success,
				"Salva ed esci did not write a valid regional slot before quitting.");
		}
		finally
		{
			if (previous is null) File.Delete(slot);
			else File.WriteAllBytes(slot, previous);
		}
	}

	private void SaveRegionCase(string directory, string name, SimulationSession session)
	{
		_sim = session;
		var path = Path.Combine(directory, $"{name}.json");
		SliceRequire(SaveGameToPath(path), $"Could not save {name} case.");
		File.WriteAllBytes(Path.Combine(directory, $"{name}.snapshot.json"), RegionSnapshot(session));
	}

	private static SimulationSession NewRegionSmokeSession()
	{
		var session = BlackmarshWatchIsland.CreateSession();
		session.FacePlayer(-1, -1);
		return session;
	}

	private static byte[] RegionSnapshot(SimulationSession session)
	{
		using var stream = new MemoryStream();
		session.Save(stream);
		return stream.ToArray();
	}

	private void RegionCommand(SimulationSession session, Command command)
	{
		var result = session.Execute(command);
		SliceRequire(result.Success, result.Message);
		if (result.Action is { } action)
			SliceRequire(session.AdvanceUntilCompleted(action, Duration.FromMinutes(30)).Outcome == AdvanceOutcome.Completed,
				$"Command did not complete: {command}");
	}

	private void RegionWalk(SimulationSession session, GridPos to)
	{
		if (session.GetPlayerView().Position != to)
			RegionCommand(session, new MoveToSpotCommand { Actor = session.Player, To = Spot.CenterOf(to) });
	}

	private void RegionDoor(SimulationSession session, GridPos at, bool open)
	{
		if (session.GetPlayerView().Doors.Single(d => d.At == at).Open != open)
			RegionCommand(session, new DoorCommand { Actor = session.Player, At = at, Open = open });
	}

	private void RegionTravel(SimulationSession session, LocationId destination)
	{
		RegionWalk(session, session.GetPlayerView().Map!.Exits.Single().At);
		RegionCommand(session, new TravelCommand { Actor = session.Player, Destination = destination });
	}
}
