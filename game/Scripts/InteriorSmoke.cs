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
			_map.SetPaused(false);
			_map.MoveTo(InteriorPrototypeScenario.WindowOutside);
			await UntilIdle();
			_map.SetPaused(true);
			Require(_map.View.MapVisibility![3][6] == '2', "window does not reveal the first room");
			Capture("day-window.png");
			_map.SetPaused(false);
			_map.MoveTo(new GridPos(6, 4));
			await UntilIdle();
			_map.SetPaused(true);
			Require(_map.View.Doors.Single(d => d.At == InteriorPrototypeScenario.Door).Open == true,
				"walking through the edge door did not open it");
			Capture("day-inside.png");
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
