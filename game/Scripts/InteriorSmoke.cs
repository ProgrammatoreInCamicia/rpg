using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Visual smoke for the isolated two-room interior prototype.</summary>
public partial class InteriorSmoke : Node
{
	private readonly VillageMap _map;
	private readonly string _directory;

	public InteriorSmoke() : this(null!, "") { }
	public InteriorSmoke(VillageMap map, string directory) { _map = map; _directory = directory; }

	public override async void _Ready()
	{
		try
		{
			DirAccess.MakeDirRecursiveAbsolute(_directory);
			_map.SetPaused(true);
			_map.FocusForSmoke(new GridPos(6, 4));
			_map.AdvanceForSmoke(new GameTime(13 * 3600));
			await Frames(3);
			Capture("day-outside.png");
			Require(_map.RoofRaisedForSmoke(new GridPos(6, 4)),
				"the roof is still drawn at floor height");
			Require(!_map.PreviewSpotForSmoke(Spot.CenterOf(new GridPos(4, 4))).Visible,
				"an unexplored room showed a free-walk preview");
			_map.SetPaused(false);
			_map.MoveTo(InteriorPrototypeScenario.WindowOutside);
			await UntilIdle();
			_map.SetPaused(true);
			Require(_map.View.MapVisibility![3][6] == '2', "window does not reveal the first room");
			Require(!_map.RoofVisibleForSmoke(new GridPos(6, 3))
				&& _map.RoofVisibleForSmoke(new GridPos(4, 4)),
				"the roof did not cut away only the part seen through the window");
			Capture("day-window.png");
			var window = _map.View.Map!.Edges.Single(e => e.Kind == MapEdgeKind.Window);
			Require(window.Open == false && !_map.WindowPanelOpenForSmoke(window.A, window.B),
				"the prototype window did not begin closed");
			_map.SetPaused(false);
			_map.ClickWindowForSmoke(window.A, window.B);
			await Frames(2);
			_map.SetPaused(true);
			Require(_map.View.Map!.Edges.Single(e => e.Kind == MapEdgeKind.Window).Open == true
				&& _map.WindowPanelOpenForSmoke(window.A, window.B),
				$"clicking the window did not open it or its visual panel: {_map.LastMessage}");
			Capture("day-window-open.png");
			_map.SetPaused(false);
			_map.ClickWindowForSmoke(window.A, window.B);
			await Frames(2);
			_map.SetPaused(true);
			Require(_map.View.Map!.Edges.Single(e => e.Kind == MapEdgeKind.Window).Open == false,
				"clicking the open window did not close it");
			_map.SetPaused(false);
			_map.MoveTo(new GridPos(6, 4));
			await UntilIdle();
			_map.SetPaused(true);
			Require(_map.View.Doors.Single(d => d.At == InteriorPrototypeScenario.Door).Open == true,
				"walking through the edge door did not open it");
			Capture("day-inside.png");
			Require(_map.NpcLive3dLoadedForSmoke(MappedVillageScenario.Ids.Farmer),
				"the visible inhabitant does not use the live 3D figure");
			// Once the room has been explored, stepping outside and shutting the door must
			// restore the facade. Remembered floor and internal partitions stay concealed.
			_map.SetPaused(false);
			_map.MoveTo(new GridPos(8, 4));
			await UntilIdle();
			_map.ClickDoor(InteriorPrototypeScenario.Door);
			await Frames(2);
			_map.SetPaused(true);
			Require(_map.View.Doors.Single(d => d.At == InteriorPrototypeScenario.Door).Open == false,
				"the outside click did not shut the house door");
			Require(_map.View.MapVisibility![4][6] == '1' && _map.RoofVisibleForSmoke(new GridPos(6, 4)),
				"the explored room remained visible from outside behind a closed door");
			Require(!_map.InteriorEdgeVisibleForSmoke(new GridPos(5, 3), new GridPos(6, 3)),
				"an internal partition leaked through the roof");
			Capture("day-outside-after-visit.png");
			_map.SetPaused(false);
			_map.MoveTo(new GridPos(6, 4));
			await UntilIdle();
			_map.SetPaused(true);
			var exact = new Spot(396, 276); // six inches off the centre of the room's (6,4) square
			Require(VillageMap.SpotAtScreenForSmoke(VillageMap.ScreenPointOf(exact)) == exact,
				"the isometric spot projection did not round-trip");
			_map.SetPaused(false);
			_map.ClickSpotForSmoke(exact);
			await UntilIdle();
			Require(_map.View.Spot == exact, "free movement did not end at the chosen floor point");
			Require(_map.FootPointOf(_map.View.Id) is { } foot
				&& foot.DistanceTo(VillageMap.ScreenPointOf(exact)) < 1f,
				"the displayed figure did not end at its Sim spot");
			_map.SetPaused(true);
			var preview = _map.PreviewSpotForSmoke(new Spot(402, 276));
			Require(!preview.Visible, "exploration still shows a combat-style route preview");
			Capture("day-free-spot.png");
			// Drive half a game second while paused so Stop is checked between two authoritative checkpoints.
			var midGoal = Spot.CenterOf(new GridPos(7, 4));
			var startedAt = _map.View.Now;
			_map.SetPaused(false);
			_map.MoveToSpot(midGoal);
			_map.SetPaused(true);
			_map.AdvanceForSmoke(startedAt.Plus(Duration.FromTicks(30)));
			var midSpot = _map.View.Spot;
			Require(midSpot is { } && midSpot != exact && midSpot != midGoal && _map.IsWalking,
				"the walker did not reach an intermediate Spot after 30 ticks");
			Require(_map.FootPointOf(_map.View.Id) is { } midFoot
				&& midFoot.DistanceTo(VillageMap.ScreenPointOf(midSpot.GetValueOrDefault())) < 1f,
				"the figure did not match the Sim Spot between checkpoints");
			var midSave = System.IO.Path.Combine(_directory, "mid-tick-walk.json");
			_map.SaveGameToPath(midSave);
			_map.SetPaused(false);
			_map.Stop();
			_map.SetPaused(true);
			Require(!_map.IsWalking && _map.View.Spot == midSpot,
				"Stop moved the walker away from the visible mid-tick Spot");
			_map.LoadGameFromPath(midSave);
			Require(_map.IsWalking && _map.View.Spot == midSpot && _map.View.Now == startedAt.Plus(Duration.FromTicks(30)),
				$"the mid-tick save did not restore the same walk and Spot: {_map.LastMessage}; walking={_map.IsWalking}, spot={_map.View.Spot}, expected={midSpot}, now={_map.View.Now.Ticks}, expectedNow={startedAt.Ticks + 30}");
			_map.SetPaused(false);
			_map.Stop();
			Require(_map.View.Spot == midSpot, "Stop after reloading changed the mid-tick Spot");
			_map.MoveToSpot(exact);
			Require(_map.IsWalking && _map.View.Spot == midSpot,
				"a new order did not start from the stopped mid-tick Spot");
			await UntilIdle();
			_map.SetPaused(false);
			_map.ClickDoor(InteriorPrototypeScenario.Door);
			await Frames(2);
			_map.SetPaused(true);
			Require(_map.View.Doors.Single(d => d.At == InteriorPrototypeScenario.Door).Open == false,
				"the inside click did not close the edge door");
			Capture("day-door-closed.png");
			_map.AdvanceForSmoke(new GameTime(26 * 3600));
			_map.TorchForSmoke();
			await Frames(3);
			Capture("night-torch.png");
			_map.OpenCreatureCardForSmoke(MappedVillageScenario.Ids.Farmer);
			Require(_map.CreatureCardTextForSmoke.Contains("Tipo:")
				&& _map.CreatureCardTextForSmoke.Contains("CA: non conosciuta"),
				"the information panel revealed an unknown combat number or did not show the creature");
			_map.ObserveCreatureForSmoke();
			Require(_map.EventText.Contains("Intuizione:"), "Observe did not reach the player's event log");
			await Frames(2);
			Capture("creature-card.png");
			_map.SetPaused(false);
			_map.MoveToSpot(Spot.CenterOf(new GridPos(5, 4)));
			await UntilIdle();
			Require(_map.FarmerAttacksForSmoke() && _map.View.Combat is { IsYourTurn: true },
				"the interior fight did not reach the player's turn");
			var risky = _map.PreviewSpotForSmoke(Spot.CenterOf(new GridPos(7, 5)));
			Require(risky.Visible && risky.ThreatMarkers > 0 && risky.Hint.Contains("reazione"),
				$"an opportunity attack is not marked on the free combat route: {risky}");
			var combatGoal = new Spot(340, 278);
			var before = _map.View.Combat!.You.MovementLeftInches;
			var combatPreview = _map.PreviewSpotForSmoke(combatGoal);
			Require(combatPreview.Visible && combatPreview.RoutePoints >= 2 && combatPreview.Hint.Contains("/"),
				"the fight does not preview the free route and remaining movement");
			_map.ClickSpotForSmoke(combatGoal);
			Require(_map.View.Spot == combatGoal && _map.View.Combat!.You.MovementLeftInches < before,
				"the fight click did not spend movement to the precise point");
			await Seconds(0.4);
			Require(_map.FootPointOf(_map.View.Id) is { } combatFoot
				&& combatFoot.DistanceTo(VillageMap.ScreenPointOf(combatGoal)) < 1f,
				"the fight figure did not finish its visual path at the Sim point");
			Capture("combat-free-spot.png");
			GD.Print("INTERIOR SMOKE OK");
			GetTree().Quit(0);
		}
		catch (Exception e)
		{
			GD.PushError($"INTERIOR SMOKE FAILED: {e}");
			GetTree().Quit(1);
		}
	}

	private static void Require(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private async Task Frames(int count)
	{
		for (var i = 0; i < count; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Task Seconds(double seconds)
	{
		var until = Time.GetTicksMsec() + (ulong)(seconds * 1000);
		while (Time.GetTicksMsec() < until)
			await Frames(1);
	}

	private async Task UntilIdle()
	{
		for (var i = 0; i < 240 && _map.IsBusy; i++)
			await Frames(1);
		Require(!_map.IsBusy, "walk did not complete");
		await Frames(2);
	}

	private void Capture(string name)
	{
		var path = System.IO.Path.Combine(_directory, name);
		Require(GetViewport().GetTexture().GetImage().SavePng(path) == Error.Ok, $"cannot save {path}");
		GD.Print($"INTERIOR SMOKE capture {path}");
	}
}
