using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Reproduces ordinary-time arrival, attack and movement at the occupied granary access.</summary>
public partial class VillageMap
{
	private async void RunSliceCombatSmoke(string outputDir)
	{
		try
		{
			System.IO.Directory.CreateDirectory(outputDir);
			_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
			CompleteSlicePreparation(new MoveToSpotCommand { Actor = _sim.Player,
				To = Spot.CenterOf(new GridPos(8, 3)) });
			var closed = _sim.Execute(new DoorCommand { Actor = _sim.Player,
				At = MappedVillageScenario.Ids.InnDoor, Open = false });
			SliceRequire(closed.Success, closed.Message);
			CompleteSlicePreparation(new MoveToSpotCommand { Actor = _sim.Player,
				To = Spot.CenterOf(MappedVillageScenario.Ids.GranaryAccess) });
			_sim.FacePlayer(0, -1);
			var torch = _sim.Execute(new TorchCommand { Actor = _sim.Player, Lit = true, StowShield = true });
			SliceRequire(torch.Success, torch.Message);
			Refresh();
			_pace = 600; // Speeds up the ordinary Process clock; does not use the slice wait action.
			var raider = MappedVillageScenario.Ids.Raider;
			var reachedStore = false;
			for (var frame = 0; frame < 1000; frame++)
			{
				await SliceFrames(1);
				var raiderState = _sim.GetWorldView().Actors.Single(a => a.Id == raider);
				if (raiderState.Position != MappedVillageScenario.Ids.GranaryAccess
					|| raiderState.Action?.Kind != "TakeFood") continue;
				reachedStore = true;
				break;
			}
			SliceRequire(reachedStore && !_sliceWaiting && !_paused && _view.Combat is null
				&& _view.Position == MappedVillageScenario.Ids.GranaryAccess,
				"Ordinary time did not reach the occupied granary access.");
			var attack = _view.Attacks.FirstOrDefault(option =>
				_sim.PreviewAttack(_sim.Player, raider, option.Name).Valid);
			SliceRequire(attack is not null && _view.VisibleActors.Any(a => a.Id == raider),
				"The raider is not visible or attackable at the store.");
			SelectOpeningAttack(raider);
			_openingAttack.Select(Enumerable.Range(0, _openingAttack.ItemCount)
				.Single(i => _openingAttack.GetItemText(i) == attack!.Name));
			AskOpeningAttack();
			ConfirmOpeningAttack();
			_openingConfirm.Hide(); // The real confirmation button dismisses the dialog automatically.
			await SliceFrames(5);
			var combat = _view.Combat;
			SliceRequire(combat is { IsYourTurn: true }
				&& combat.You.MovementLeftInches > 0,
				"The opening attack did not leave a turn with movement available.");
			var destination = new GridPos(16, 6);
			SliceRequire(_sim.PreviewMoveToSpot(_sim.Player, Spot.CenterOf(destination)).Valid,
				"The overlapping raider traps the player despite available movement.");
			var beforeMove = _view.Spot;
			_armedAttack = attack!.Name;
			_attackArmed = true;
			var screen = GetGlobalTransformWithCanvas() * CellToScreen(destination);
			HandleCombatInput(new InputEventMouseButton
			{
				Pressed = true, ButtonIndex = MouseButton.Left, Position = screen,
			});
			SliceRequire(!_attackArmed && _view.Spot != beforeMove,
				"A floor click while targeting did not cancel targeting and move.");
			SliceRequire(_sliceWaitStatus!.Text.Contains("Fine turno"),
				"The slice does not explain why time is turn based.");
			if (_view.Combat?.You.MovementLeftInches <= 0)
				SliceRequire(_sliceWaitStatus.Text.Contains("Movimento esaurito"),
					"The slice does not explain an exhausted movement budget.");
			await SliceFrames(3);
			if (DisplayServer.GetName() != "headless")
				CaptureSliceGuide(outputDir, "combat-movement.png");
			var beforeTurn = _view.Now;
			EndCombatTurn();
			await SliceFrames(5);
			SliceRequire(_view.Now > beforeTurn, "End turn did not advance combat time.");
			GD.Print("SLICE COMBAT SMOKE OK: ordinary-time raider arrival, overlapping bodies, attack, floor movement and end turn");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"SLICE COMBAT SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}
}
