using System.Text.Json;
using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;
using Island = RpgSandbox.Sim.Scenarios.BlackmarshWatchIsland.Ids;

namespace RpgSandbox.Game;

/// <summary>A playable layout pilot while the regional scenario is being integrated by the Sim owner.</summary>
public partial class VillageMap
{
	private async void RunRegionMapPilotSmoke(string outputDir)
	{
		try
		{
			Directory.CreateDirectory(outputDir);
			SetPaused(true);
			await SliceFrames(8);
			SliceRequire(_view.Map is { Width: 48, Height: 32 } && _view.Area == Island.Hamlet,
				"The large hamlet did not open.");
			CaptureBlackmarshBuilding(outputDir, "hamlet.png");
			PresentationWalk(new GridPos(16, 14));
			PresentationDoor(new GridPos(15, 14), true);
			PresentationWalk(new GridPos(13, 14));
			await SliceFrames(8);
			CaptureBlackmarshBuilding(outputDir, "hamlet-house.png");
			await PresentationJourney(Island.ShedFork);
			SliceRequire(_view.Map is { Width: 48, Height: 32 } && _view.Area == Island.ShedArea,
				"The large shed did not open.");
			CaptureBlackmarshBuilding(outputDir, "shed.png");
			PresentationWalk(new GridPos(21, 16));
			PresentationDoor(new GridPos(22, 16), true);
			PresentationWalk(new GridPos(24, 15));
			await SliceFrames(8);
			CaptureBlackmarshBuilding(outputDir, "shed-inside.png");
			PresentationWalk(new GridPos(25, 16));
			PresentationDoor(new GridPos(26, 16), true);
			PresentationWalk(new GridPos(27, 13));
			await SliceFrames(8);
			CaptureBlackmarshBuilding(outputDir, "shed-store.png");
			await PresentationJourney(Island.CoveFork);
			SliceRequire(_view.Map is { Width: 48, Height: 32 } && _view.Area == Island.Cove,
				"The large cove did not open.");
			CaptureBlackmarshBuilding(outputDir, "cove.png");
			PresentationWalk(new GridPos(36, 18));
			await SliceFrames(8);
			CaptureBlackmarshBuilding(outputDir, "cove-overlook.png");
			PresentationWalk(new GridPos(37, 20));
			await SliceFrames(8);
			CaptureBlackmarshBuilding(outputDir, "cove-landing.png");
			SliceRequire(_sim.GetWorldView().RecentFacts.Any(f => f.Kind is "DefenseWarning" or "DefenseAttack" or "DefenseWithdrawal"),
				"The Vasan raider did not answer entry to its enlarged landing.");
			SliceRequire(_regionDefenseBalloon is { Visible: true },
				"The defender's answer was not legible in the game world.");
			await PresentationJourney(Island.HamletFork);
			SliceRequire(_view.Area == Island.Hamlet, "The pilot could not complete the travel loop.");
			GD.Print("REGION MAP PILOT SMOKE OK: three large areas, travel and render lifecycle");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"REGION MAP PILOT SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private static SimulationSession CreateRegionMapPilot()
	{
		var path = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"),
			"..", "docs", "BLACKMARSH_REGION_LAYOUT_48x32.json"));
		using var document = JsonDocument.Parse(File.ReadAllText(path));
		var layouts = document.RootElement.GetProperty("maps").EnumerateArray()
			.ToDictionary(m => m.GetProperty("area").GetString()!, m => m.GetProperty("rows")
				.EnumerateArray().Select(r => r.GetString()!).ToArray());
		var b = new ScenarioBuilder().WithSeed(7);
		b.AddArea(Island.Hamlet.Value, "Nucleo dei pescatori")
			.AddArea(Island.ShedArea.Value, "Rimessa della vedetta")
			.AddArea(Island.Cove.Value, "Caletta")
			.AddLocation(Island.HamletFork.Value, "Bivio del nucleo", Island.Hamlet.Value)
			.AddLocation(Island.CommonHouse.Value, "Casa comune", Island.Hamlet.Value)
			.AddLocation(Island.Well.Value, "Piazzale del pozzo", Island.Hamlet.Value)
			.AddLocation(Island.ShedFork.Value, "Bivio della rimessa", Island.ShedArea.Value)
			.AddLocation(Island.Shed.Value, "Rimessa della vedetta", Island.ShedArea.Value)
			.AddLocation(Island.CoveFork.Value, "Bivio della caletta", Island.Cove.Value)
			.AddLocation(Island.Landing.Value, "Approdo dei Vasan", Island.Cove.Value)
			.AddWater(Island.Well.Value)
			.AddRoute(Island.HamletFork.Value, Island.ShedFork.Value, Duration.FromMinutes(6))
			.AddRoute(Island.ShedFork.Value, Island.CoveFork.Value, Duration.FromMinutes(8))
			.AddRoute(Island.HamletFork.Value, Island.CoveFork.Value, Duration.FromMinutes(12));
		b.AddMap(Island.Hamlet.Value, layouts[Island.Hamlet.Value], new Dictionary<char, string>
			{ ['E'] = Island.HamletFork.Value, ['C'] = Island.CommonHouse.Value, ['W'] = Island.Well.Value })
			.AddExit(Island.Hamlet.Value, Island.HamletFork.Value, (47, 16))
			.AddInterior(Island.Hamlet.Value, Island.CommonHouse.Value)
			.AddPost(Island.Hamlet.Value, Island.CommonHouse.Value, PostKind.Home, (12, 15))
			.AddPost(Island.Hamlet.Value, Island.CommonHouse.Value, PostKind.Guard, (13, 16))
			.AddPost(Island.Hamlet.Value, Island.Well.Value, PostKind.Home, (24, 15))
			.AddLight(Island.Hamlet.Value, (25, 17), 15, 30)
			.AddObstacle(Island.Hamlet.Value, (25, 17))
			.AddGroundItem(Island.Hamlet.Value, (13, 25), "iron-spike");
		AddPilotBuilding(b, Island.Hamlet.Value, 9, 11, 15, 17, (15, 14), null, false);
		b.AddMap(Island.ShedArea.Value, layouts[Island.ShedArea.Value], new Dictionary<char, string>
			{ ['E'] = Island.ShedFork.Value, ['S'] = Island.Shed.Value })
			.AddExit(Island.ShedArea.Value, Island.ShedFork.Value, (0, 16))
			.AddInterior(Island.ShedArea.Value, Island.Shed.Value)
			.AddPost(Island.ShedArea.Value, Island.Shed.Value, PostKind.Work, (24, 14))
			.AddPost(Island.ShedArea.Value, Island.Shed.Value, PostKind.Guard, (26, 18))
			.AddLight(Island.ShedArea.Value, (24, 17), 15, 30)
			.AddObstacle(Island.ShedArea.Value, (24, 17))
			.AddGroundItem(Island.ShedArea.Value, (35, 9), "thieves-tools");
		AddPilotBuilding(b, Island.ShedArea.Value, 22, 12, 29, 18, (22, 16), (29, 15), true);
		for (var y = 12; y <= 18; y++)
			b.AddEdge(Island.ShedArea.Value, (25, y), (26, y),
				y == 16 ? MapEdgeKind.Door : MapEdgeKind.Wall,
				doorAt: y == 16 ? (26, y) : null);
		b.AddMap(Island.Cove.Value, layouts[Island.Cove.Value], new Dictionary<char, string>
			{ ['E'] = Island.CoveFork.Value, ['A'] = Island.Landing.Value })
			.AddExit(Island.Cove.Value, Island.CoveFork.Value, (0, 16))
			.AddPost(Island.Cove.Value, Island.Landing.Value, PostKind.Home, (39, 23))
			.AddLight(Island.Cove.Value, (40, 27), 15, 30)
			.AddObstacle(Island.Cove.Value, (40, 27))
			.AddGroundItem(Island.Cove.Value, (38, 28), "torch");
		b.AddStore(Island.Pantry.Value, "Dispensa della casa comune", Island.CommonHouse.Value, 12,
			Island.Islanders.Value, (10, 12), new[] { (11, 12) })
			.AddStore(Island.ShedStore.Value, "Cassa delle razioni d'emergenza", Island.Shed.Value, 4,
				Island.Islanders.Value, (28, 13), new[] { (27, 13) })
			.AddStore(Island.VasanCache.Value, "Provviste dei Vasan", Island.Landing.Value, 4,
				Island.Vasan.Value, (43, 26), new[] { (42, 26), (43, 25) })
			.AddFaction(Island.Islanders.Value, "Pescatori della vedetta", Island.Pantry.Value, authorityId: Island.Keeper.Value)
			.AddRaidingFaction(Island.Vasan.Value, "Vasan", Island.VasanCache.Value, 4,
				Duration.FromMinutes(19 * 60 + 59), 1, 4, Duration.FromHours(1))
			.AddEnmity(Island.Islanders.Value, Island.Vasan.Value)
			.AddDefense(Island.Vasan.Value, Island.Landing.Value)
			.AddActor(Island.Player.Value, "Protagonista", Island.Well.Value, isPlayer: true,
				food: 10, factionId: Island.Islanders.Value, at: (24, 16), torches: 3, build: "paladin",
				items: new[] { ("chain-mail", 1), ("shield", 1), ("longsword", 1), ("javelin", 6),
					("waterskin", 1), (Island.ShedKey, 1) })
			.AddActor(Island.Keeper.Value, "Custode della vedetta", Island.Well.Value,
				factionId: Island.Islanders.Value, at: (24, 15), build: "village-guard")
			.AddActor(Island.Fisher.Value, "Pescatore", Island.CommonHouse.Value,
				factionId: Island.Islanders.Value, workLocationId: Island.Shed.Value,
				shiftStart: Duration.FromHours(16), shiftEnd: Duration.FromMinutes(19 * 60 + 45),
				at: (12, 15), build: "farmer", items: new[] { (Island.ShedKey, 1) })
			.AddActor(Island.Raider.Value, "Predone vasan", Island.Landing.Value,
				factionId: Island.Vasan.Value, at: (39, 23), build: "raider");
		var session = SimulationSession.Create(b.Build());
		session.Advance(BlackmarshWatchIsland.Opening);
		return session;
	}

	private static void AddPilotBuilding(ScenarioBuilder b, string area, int left, int top, int right, int bottom,
		(int X, int Y) door, (int X, int Y)? window, bool locked)
	{
		for (var x = left; x <= right; x++)
		{
			b.AddEdge(area, (x, top - 1), (x, top), MapEdgeKind.Wall);
			b.AddEdge(area, (x, bottom), (x, bottom + 1), MapEdgeKind.Wall);
		}
		for (var y = top; y <= bottom; y++)
		{
			b.AddEdge(area, (left - 1, y), (left, y),
				(left, y) == door ? MapEdgeKind.Door : MapEdgeKind.Wall,
				doorAt: (left, y) == door ? door : null,
				lockSpec: locked && (left, y) == door
					? new LockSpec(Key: Island.ShedKey, Locked: true, ForceDc: 15) : null);
			b.AddEdge(area, (right, y), (right + 1, y),
				(right, y) == door ? MapEdgeKind.Door : (right, y) == window ? MapEdgeKind.Window : MapEdgeKind.Wall,
				doorAt: (right, y) == door ? door : null,
				lockSpec: (right, y) == window ? new LockSpec(Stuck: true, ForceDc: 10) : null);
		}
	}
}
