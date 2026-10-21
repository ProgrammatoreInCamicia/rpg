using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	private sealed record PendingBarrierUse(GridPos? Door, GridPos A, GridPos B, bool Open);
	private PendingBarrierUse? _pendingBarrier;

	private void ApproachBarrier(PendingBarrierUse target)
	{
		if (_view.Combat is not null)
		{
			ShowMessage("Durante uno scontro scegli l'interazione dal menu dell'ostacolo.");
			return;
		}
		var cells = target.Door is null || _view.Map?.Edges.Any(e => e.DoorAt == target.Door) == true
			? new[] { target.A, target.B }
			: new[] { target.A, new GridPos(target.A.X - 1, target.A.Y),
				new GridPos(target.A.X + 1, target.A.Y), new GridPos(target.A.X, target.A.Y - 1),
				new GridPos(target.A.X, target.A.Y + 1) };
		var previews = cells.Where(cell => _view.Map is { } map && cell.X >= 0 && cell.Y >= 0
			&& cell.X < map.Width && cell.Y < map.Height
			&& (_view.MapVisibility is not { } visibility || visibility[cell.Y][cell.X] != '0'))
			.Select(Spot.CenterOf).Distinct()
			.Select(spot => (Spot: spot, Preview: _sim.PreviewMoveToSpot(_sim.Player, spot, _sneak)))
			.ToList();
		var candidates = previews
			.Where(choice => choice.Preview.Valid)
			.OrderBy(choice => choice.Preview.Feet)
			.ToList();
		if (candidates.Count == 0)
		{
			var unavailable = previews.FirstOrDefault(choice => choice.Preview.Reason is
				RejectionReason.ActorBusy or RejectionReason.NoMovementLeft or RejectionReason.InCombat);
			ShowMessage(unavailable.Preview?.Message ?? (target.Door is null
				? "Non riesci a raggiungere la finestra." : "Non riesci a raggiungere la porta."));
			return;
		}
		foreach (var choice in candidates)
		{
			if (!Walk(new MoveToSpotCommand { Actor = _sim.Player, To = choice.Spot, Stealthy = _sneak }))
				continue;
			_pendingBarrier = target;
			_approaching = null;
			_exitAfterWalking = null;
			_attackAfterWalking = null;
			ShowMessage(target.Door is null ? "Ti avvicini alla finestra." : "Ti avvicini alla porta.");
			return;
		}
	}

	private void FinishBarrierApproach(PendingBarrierUse target)
	{
		if (_view.Dead || _view.Combat is not null || _view.Map is null)
			return;
		if (target.Door is { } at)
		{
			if (_view.Doors.FirstOrDefault(d => d.At == at) is not { Known: DoorKnowledge.Seen, Open: { } open })
			{
				ShowMessage("La porta non è più visibile.");
				return;
			}
			if (open == target.Open)
			{
				ShowMessage(open ? "La porta è aperta." : "La porta è chiusa.");
				return;
			}
			var result = _sim.Execute(new DoorCommand { Actor = _sim.Player, At = at, Open = target.Open });
			ShowMessage(result.Message);
		}
		else
		{
			if (_view.Map.Edges.FirstOrDefault(e => e.Kind == MapEdgeKind.Window
				&& e.A == target.A && e.B == target.B) is not { Open: { } open } edge)
			{
				ShowMessage("La finestra non è più visibile.");
				return;
			}
			if (open == target.Open)
			{
				ShowMessage(open ? "La finestra è aperta." : "La finestra è chiusa.");
				return;
			}
			var result = _sim.Execute(new WindowCommand { Actor = _sim.Player,
				A = edge.A, B = edge.B, Open = target.Open });
			ShowMessage(result.Message);
		}
		Refresh();
	}
}
