using Godot;
using RpgSandbox.Sim.Api;
using Island = RpgSandbox.Sim.Scenarios.BlackmarshWatchIsland.Ids;

namespace RpgSandbox.Game;

/// <summary>Task 4: choose destinations and pace through the same map and HUD handlers as a player.</summary>
public partial class VillageMap
{
	private async void RunRegionTravelSmoke(string outputDir)
	{
		try
		{
			Directory.CreateDirectory(outputDir);
			var world = _sim;
			SetPace(6);
			SetPaused(false);
			await TravelByUi(Island.CoveFork, TravelPace.Fast, cancelFirst: true, outputDir);
			await TravelByUi(Island.ShedFork, TravelPace.Slow, cancelFirst: false, outputDir);
			await TravelByUi(Island.HamletFork, TravelPace.Normal, cancelFirst: false, outputDir);
			await TravelByUi(Island.ShedFork, TravelPace.Fast, cancelFirst: false, outputDir);
			await TravelByUi(Island.CoveFork, TravelPace.Normal, cancelFirst: false, outputDir);
			await TravelByUi(Island.HamletFork, TravelPace.Slow, cancelFirst: false, outputDir);
			SliceRequire(ReferenceEquals(world, _sim), "The UI tour replaced the simulation session.");
			GD.Print("REGION TRAVEL SMOKE OK: two route orders, cancellation, pace, pause, transit and returns");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"REGION TRAVEL SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private async Task TravelByUi(LocationId destination, TravelPace pace, bool cancelFirst, string outputDir)
	{
		var exit = _view.Map!.Exits.Single();
		var route = exit.Routes.Single(r => r.To == destination);
		ClickRegionExit(exit);
		await UntilRegionTravelChoice();
		SliceRequire(_view.Position == exit.At && _travelDestinations.Visible && !_travelPaces.Visible
			&& _travelDestinations.GetChildCount() == 2 && _view.Travel is null,
			"Clicking a regional fork did not present its two destinations.");
		if (cancelFirst)
			CaptureBlackmarshBuilding(outputDir, "route-selection.png");
		if (cancelFirst)
		{
			_travelCancel.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(!_travelChoicePanel.Visible && _view.Travel is null && _running is null,
				"Cancelling the route selection started a journey.");
			ClickRegionExit(exit);
			await UntilRegionTravelChoice();
		}
		var name = _view.Locations.Single(l => l.Id == destination).Name;
		SliceRequire(_travelDestinations.GetChildren().OfType<Button>().Any(b => b.Text.StartsWith(name)
			&& b.Text.Contains(TravelDurationText(route.TravelTime))), "The route choice hides its normal travel time.");
		SelectRegionDestination(name);
		SliceRequire(_travelPaces.Visible && !_travelDestinations.Visible && _travelBack.Visible,
			"Choosing a destination did not offer the three paces.");
		if (cancelFirst)
		{
			await SliceFrames(2);
			CaptureBlackmarshBuilding(outputDir, "pace-selection.png");
		}
		foreach (var shownPace in new[] { TravelPace.Fast, TravelPace.Normal, TravelPace.Slow })
			SliceRequire(_travelPaceButtons[shownPace].Text.Contains(TravelDurationText(TravelDuration(route, shownPace))),
				"A pace button shows the wrong predicted duration.");
		if (cancelFirst)
		{
			_travelBack.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_travelDestinations.Visible && !_travelPaces.Visible, "Back did not restore destination choices.");
			SelectRegionDestination(name);
		}
		var oldArea = _view.Area;
		var before = _view.Now;
		_travelPaceButtons[pace].EmitSignal(BaseButton.SignalName.Pressed);
		SliceRequire(_view.Travel is { Pace: var chosenPace } && chosenPace == pace && _view.Position is null
			&& !_mapRoot.Visible && _offMapPanel.Visible && !_travelChoicePanel.Visible,
			"The selected route did not enter transit at the selected pace.");
		SliceRequire(_view.Travel!.ArrivesAt.Since(_view.Travel.DepartedAt) == TravelDuration(route, pace),
			"Actual journey duration differs from the pace preview.");
		SliceRequire(_view.VisibleActors.Count == 0 && _view.VisibleStores.Count == 0,
			"Transit exposed people or supplies on a remote map.");
		if (cancelFirst)
		{
			await SliceFrames(2);
			CaptureBlackmarshBuilding(outputDir, "in-transit.png");
		}
		var active = _running;
		_UnhandledInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left,
			Position = GetGlobalTransformWithCanvas() * CellToScreen(exit.At) });
		SliceRequire(_running == active && _view.Position is null && QueuedOrders == 0,
			"A click during transit acted on the hidden origin map.");
		SetPaused(true);
		var frozen = _view.Now;
		await SliceFrames(3);
		SliceRequire(_view.Now == frozen && _running == active, "Pause did not stop time during transit.");
		SetPaused(false);
		for (var frame = 0; frame < 1200 && _view.Travel is not null; frame++)
			await SliceFrames(1);
		SliceRequire(_view.Travel is null && _view.Location == destination && _view.Area != oldArea
			&& _view.Position is not null && _mapRoot.Visible && _view.Now > before,
			"The chosen journey did not arrive on the destination map.");
		if (destination == Island.CoveFork || destination == Island.HamletFork)
		{
			await SliceFrames(3);
			CaptureBlackmarshBuilding(outputDir, $"arrival-{destination.Value}-{pace}.png");
		}
	}

	private void ClickRegionExit(ExitView exit)
	{
		_UnhandledInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left,
			Position = GetGlobalTransformWithCanvas() * CellToScreen(exit.At) });
	}

	private void SelectRegionDestination(string name) =>
		_travelDestinations.GetChildren().OfType<Button>().Single(b => b.Text.StartsWith(name))
			.EmitSignal(BaseButton.SignalName.Pressed);

	private async Task UntilRegionTravelChoice()
	{
		for (var frame = 0; frame < 1200 && !_travelChoicePanel.Visible; frame++)
			await SliceFrames(1);
		SliceRequire(_travelChoicePanel.Visible, "The fork did not open the route panel within the frame budget.");
	}
}
