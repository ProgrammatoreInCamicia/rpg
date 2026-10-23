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
			_sliceSummaryButton!.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_sliceSummary is { Visible: true } && _paused,
				"The summary did not open and pause during combat.");
			await SliceFrames(2);
			if (DisplayServer.GetName() != "headless")
				CaptureSliceGuide(outputDir, "combat-summary-open.png");
			var frozenCombat = SliceCombatWorldSnapshot();
			var floorClick = new InputEventMouseButton
			{
				Pressed = true, ButtonIndex = MouseButton.Left,
				Position = GetGlobalTransformWithCanvas() * CellToScreen(destination),
			};
			_quickEndTurn.EmitSignal(BaseButton.SignalName.Pressed);
			_combatEndTurn.EmitSignal(BaseButton.SignalName.Pressed);
			Input.ParseInputEvent(floorClick); // the real viewport route must not reach the map behind the panel
			HandleCombatInput(floorClick);
			MoveTo(destination); // other UI actions use the common order route
			await SliceFrames(3);
			SliceRequire(SliceCombatWorldSnapshot() == frozenCombat && QueuedOrders == 0,
				"Combat commands changed the world behind the open summary.");
			_sliceSummaryEnd!.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(_sliceEnded, "The combat summary did not reach the explicit ending.");
			await SliceFrames(2);
			if (DisplayServer.GetName() != "headless")
				CaptureSliceGuide(outputDir, "combat-summary-ended.png");
			_quickEndTurn.EmitSignal(BaseButton.SignalName.Pressed);
			HandleCombatInput(floorClick);
			MoveTo(destination);
			await SliceFrames(3);
			SliceRequire(SliceCombatWorldSnapshot() == frozenCombat && QueuedOrders == 0,
				"Combat commands changed the world behind the concluded summary.");
			_sliceSummaryContinue!.EmitSignal(BaseButton.SignalName.Pressed);
			SliceRequire(!_paused && _sliceSummary is { Visible: false },
				"Continuing after the combat summary did not restore control.");
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
			_personMenu.Visible = false;
			var raiderFeet = _tokens[raider].Position + new Vector2(0, -16);
			HandleCombatInput(new InputEventMouseButton
			{
				Pressed = true, ButtonIndex = MouseButton.Right, Position = GetGlobalTransformWithCanvas() * raiderFeet,
			});
			SliceRequire(_personMenu.Visible && _loreTarget == raider && _personAttack.Disabled,
				"A right click on a combatant did not open what the player knows of it.");
			_personMenu.Visible = false;

			_gameMenu.Visible = false;
			_gameMenuButton.GrabFocus();
			Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = true });
			Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = false });
			await SliceFrames(3);
			SliceRequire(!_gameMenu.Visible, "Space pressed the focused menu button.");
			_gameMenuButton.ReleaseFocus();

			var beforeTurn = _view.Now;
			EndCombatTurn();
			var nextRound = _view.Combat?.NextRoundAt;
			SliceRequire(nextRound is not null,
				"Ending the turn did not leave a round waiting to advance.");
			_sliceSummaryButton.EmitSignal(BaseButton.SignalName.Pressed);
			var frozenRound = SliceCombatWorldSnapshot();
			await SliceFrames(5);
			SliceRequire(SliceCombatWorldSnapshot() == frozenRound,
				"The combat round advanced behind the open summary.");
			_sliceSummaryContinue.EmitSignal(BaseButton.SignalName.Pressed);
			await SliceFrames(5);
			SliceRequire(_view.Now > beforeTurn && _view.Now >= nextRound!.Value,
				"Closing the summary did not let the combat round advance.");
			GD.Print("SLICE COMBAT SMOKE OK: ordinary-time raider arrival, overlapping bodies, attack, floor movement and end turn");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"SLICE COMBAT SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private string SliceCombatWorldSnapshot()
	{
		using var stream = new System.IO.MemoryStream();
		_sim.Save(stream);
		return Convert.ToBase64String(stream.ToArray());
	}
}
