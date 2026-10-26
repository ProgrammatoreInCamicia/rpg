using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	// Movement already has route feedback; a downed actor cannot act. Every other timed player action
	// uses the same countdown, including actions resumed from a save or ended early by the Sim.
	private static bool ShowActionTimer(ActionView action) => action.Kind is not ("Move" or "Downed")
		&& action.CompletesAt > action.StartedAt;

	private static long RemainingActionSeconds(ActionView action, GameTime now) =>
		Math.Max(0L, (action.CompletesAt.Since(now).Ticks + GameTime.TicksPerSecond - 1) / GameTime.TicksPerSecond);

	private static string ActionTimeText(long seconds) => seconds >= 3600
		? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
		: $"{seconds / 60}:{seconds % 60:00}";

	private static string ActionMarkerName(ActionView action) => action.Kind switch
	{
		"TakeFood" => "Prendi",
		"DepositFood" => "Consegni",
		"Report" => "Riferisci",
		"Rest" => "Riposi",
		"Wait" => "Attendi",
		"Travel" => "Viaggi",
		"Guard" => "Sorvegli",
		"Confiscate" => "Confischi",
		_ => action.Description.Length > 26 ? action.Description[..25] + "…" : action.Description,
	};

	private string TimedActionStatus(ActionView action, GameTime now) =>
		$"{(_paused ? "IN PAUSA · " : "")}{action.Description} · {ActionTimeText(RemainingActionSeconds(action, now))} di gioco rimanenti.";

	private void UpdateTimedActionMarker(Node2D token)
	{
		var progress = token.GetNode<ProgressBar>("ActionProgress");
		var caption = token.GetNode<Label>("ActionProgressText");
		if (_view.Action is not { } action || !ShowActionTimer(action) || _view.Combat is not null)
		{
			progress.Visible = false;
			caption.Visible = false;
			return;
		}
		var total = Math.Max(1L, action.CompletesAt.Since(action.StartedAt).Ticks);
		var elapsed = Math.Clamp(_view.Now.Since(action.StartedAt).Ticks, 0L, total);
		progress.Value = 100.0 * elapsed / total;
		progress.Visible = true;
		caption.Text = $"{ActionMarkerName(action)} · {ActionTimeText(RemainingActionSeconds(action, _view.Now))}";
		caption.Visible = true;
	}
}
