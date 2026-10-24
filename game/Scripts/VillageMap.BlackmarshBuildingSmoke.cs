using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Checks the rendered shed and real movement/barrier handlers against the authored map.</summary>
public partial class VillageMap
{
	private async void RunBlackmarshBuildingSmoke(string outputDir)
	{
		try
		{
			Directory.CreateDirectory(outputDir);
			SliceRequire(GetViewport().GetVisibleRect().Size == new Vector2(1280, 720),
				"The building smoke needs the target 1280x720 viewport.");
			var table = BlackmarshWatchhouse.Ids.Table;
			var tableArt = _interiorProps.Single(p => p.Cell == table).Sprite;
			SliceRequire(!tableArt.Visible && RoofVisibleForSmoke(table)
				&& !_view.VisibleActors.Any(a => a.Id == BlackmarshWatchhouse.Ids.Raider),
				"The exterior revealed the unseen room or Vasan.");
			await SliceFrames(4);
			CaptureBlackmarshBuilding(outputDir, "outside.png");

			SetPaused(false);
			var torch = _sim.Execute(new TorchCommand { Actor = _sim.Player, Lit = true, StowShield = true });
			SliceRequire(torch.Success, torch.Message);
			Refresh();
			MoveTo(new GridPos(5, 6));
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Position == new GridPos(5, 6), "The player did not reach the shed facade.");
			SliceRequire(DoorAt(CellToScreen(BlackmarshWatchhouse.Ids.ShedDoor)) == BlackmarshWatchhouse.Ids.ShedDoor,
				"The door artwork does not pick the shed door.");
			ClickDoor(BlackmarshWatchhouse.Ids.ShedDoor);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Doors.Any(d => d.At == BlackmarshWatchhouse.Ids.ShedDoor && d.Open == true)
				&& PilotDoorOpenForSmoke(BlackmarshWatchhouse.Ids.ShedDoor),
				"The key did not open a visible door leaf.");
			CaptureBlackmarshBuilding(outputDir, "door-open.png");

			MoveTo(new GridPos(7, 6));
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Position == new GridPos(7, 6) && !RoofVisibleForSmoke(table),
				"The player did not enter or the whole shed roof stayed in place.");
			SliceRequire(!tableArt.Visible,
				"The closed inner room revealed its table before the player saw it.");
			ClickDoor(BlackmarshWatchhouse.Ids.InnerDoor);
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Doors.Any(d => d.At == BlackmarshWatchhouse.Ids.InnerDoor && d.Open == true),
				"The inner door was not opened through the interaction handler.");
			MoveTo(new GridPos(10, 5));
			await UntilBlackmarshStoreIdle();
			SliceRequire(_view.Position == new GridPos(10, 5) && tableArt.Visible
				&& _view.VisibleStores.Any(s => s.Id == BlackmarshWatchhouse.Ids.ShedStore),
				"The furnished store room was not visible after entering.");
			SliceRequire(_interiorProps.Count == 1 && _interiorProps[0].Cell == table,
				"Blackmarsh duplicated the store chest or borrowed pilot props.");
			MoveTo(table);
			await SliceFrames(3);
			SliceRequire(_view.Position == new GridPos(10, 5) && _running is null,
				"The rendered table does not occupy the Sim obstacle.");
			SetPaused(true);
			CaptureBlackmarshBuilding(outputDir, "inside.png");

			SetPaused(false);
			MoveTo(new GridPos(9, 5));
			await UntilBlackmarshStoreIdle();
			FaceForSmoke(1, 1);
			await SliceFrames(2);
			var window = _view.Map!.Edges.Single(e => e.Kind == MapEdgeKind.Window);
			var panel = _windowPanels[(window.A, window.B)];
			var glass = panel.GetNode<Polygon2D>("Glass").Polygon;
			var glassPoint = panel.Position + glass.Aggregate(Vector2.Zero, (sum, vertex) => sum + vertex) / glass.Length;
			SliceRequire(WindowAt(glassPoint) is not null,
				$"The window artwork is not pickable from {_view.Position}: A={_view.MapVisibility![window.A.Y][window.A.X]}, B={_view.MapVisibility[window.B.Y][window.B.X]}.");
			ClickWindowFrameForSmoke(window.A, window.B);
			SliceRequire(_pendingBarrier is not null && _walking, "Window click did not queue an approach.");
			Stop();
			await SliceFrames(3);
			SliceRequire(_pendingBarrier is null && !_walking && _view.Map!.Edges.Single(e => e.Kind == MapEdgeKind.Window).Open == false,
				"Stop left a window interaction queued.");
			MoveTo(new GridPos(5, 6));
			await UntilBlackmarshStoreIdle();
			SliceRequire(RoofVisibleForSmoke(table), "The roof did not return after leaving the shed.");
			SetPaused(true);
			CaptureBlackmarshBuilding(outputDir, "outside-after-visit.png");
			GD.Print("BLACKMARSH BUILDING SMOKE OK: roof, table, doors, window picking and Stop");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"BLACKMARSH BUILDING SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private void CaptureBlackmarshBuilding(string outputDir, string file) =>
		SliceRequire(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(outputDir, file)) == Error.Ok,
			$"Could not capture {file}.");
}
