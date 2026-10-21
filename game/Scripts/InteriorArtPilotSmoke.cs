using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Captures the playable art pilot at the actual viewport size and checks its key transitions.</summary>
public partial class InteriorArtPilotSmoke : Node
{
	private readonly VillageMap _map;
	private readonly string _directory;

	public InteriorArtPilotSmoke() : this(null!, "") { }
	public InteriorArtPilotSmoke(VillageMap map, string directory) { _map = map; _directory = directory; }

	public override async void _Ready()
	{
		try
		{
			DirAccess.MakeDirRecursiveAbsolute(_directory);
			_map.SetPaused(true);
			_map.FocusForSmoke(new GridPos(9, 7));
			await Frames(3);
			Require(_map.RoofRaisedForSmoke(new GridPos(11, 7)), "the slate roof is not raised above the walls");
			Capture("outside.png");
			_map.SetPaused(false);
			_map.MoveTo(new GridPos(11, 7));
			await UntilIdle();
			_map.SetPaused(true);
			Require(_map.View.Position == new GridPos(11, 7), "player did not enter the first room");
			Require(_map.PilotDoorOpenForSmoke(InteriorArtPilotScenario.Entrance),
				"the entrance leaf vanished instead of opening");
			Require(!_map.RoofVisibleForSmoke(new GridPos(6, 5))
				&& !_map.RoofVisibleForSmoke(new GridPos(11, 7)), "roof did not open across the house");
			Capture("inside-lamplit.png");
			_map.SetPaused(false);
			_map.MoveTo(new GridPos(10, 6));
			await UntilIdle();
			_map.SetPaused(true);
			Capture("inside-bedroom.png");
			_map.SetPaused(false);
			_map.MoveTo(new GridPos(11, 5)); // the bed occupies this floor square
			await Frames(3);
			Require(_map.View.Position == new GridPos(10, 6) && !_map.IsBusy,
				"the player could walk through the bed");
			_map.MoveTo(new GridPos(7, 7));
			await UntilIdle();
			_map.SetPaused(true);
			Require(_map.View.Position == new GridPos(7, 7), "player did not reach the second room");
			Require(_map.View.Doors.Single(d => d.At == InteriorArtPilotScenario.RoomDoor).Open == true,
				"the inner door did not open during movement");
			Require(_map.PilotDoorOpenForSmoke(InteriorArtPilotScenario.RoomDoor),
				"the inner door leaf vanished instead of opening");
			Require(_map.View.MapVisibility![5][11] == '1'
				&& _map.DisplayShadeAtForSmoke(new GridPos(11, 5)).R > 0.5f,
				"the explored bedroom became dark after turning away");
			Capture("inside-stone-room.png");
			_map.SetPaused(false);
			_map.MoveTo(new GridPos(13, 7));
			await UntilIdle();
			_map.ClickDoor(InteriorArtPilotScenario.Entrance);
			await Frames(2);
			_map.SetPaused(true);
			Require(_map.View.Doors.Single(d => d.At == InteriorArtPilotScenario.Entrance).Open == false,
				"the entrance did not close after leaving");
			Require(_map.RoofVisibleForSmoke(new GridPos(6, 5)),
				"the explored house stayed open after leaving");
			Capture("outside-after-visit.png");
			_map.SetPaused(false);
			_map.MoveTo(new GridPos(11, 7));
			await UntilIdle();
			_map.SetPaused(true);
			_map.AdvanceForSmoke(new GameTime(26 * 3600));
			_map.TorchForSmoke();
			await Frames(3);
			Capture("night-torch.png");
			GD.Print("INTERIOR ART PILOT SMOKE OK");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PushError($"INTERIOR ART PILOT SMOKE FAILED: {error}");
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

	private async Task UntilIdle()
	{
		for (var i = 0; i < 360 && _map.IsBusy; i++)
			await Frames(1);
		Require(!_map.IsBusy, "walk did not complete");
		await Frames(3);
	}

	private void Capture(string name)
	{
		var image = GetViewport().GetTexture().GetImage();
		Require(image.GetWidth() == 1280 && image.GetHeight() == 720, "viewport size changed");
		var path = System.IO.Path.Combine(_directory, name);
		Require(image.SavePng(path) == Error.Ok, $"cannot save {path}");
		GD.Print($"INTERIOR ART PILOT capture {path}");
	}
}
