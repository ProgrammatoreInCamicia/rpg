using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Player controls and feedback for Short and Long Rests; the Sim owns their rules and timing.</summary>
public partial class VillageMap
{
	private Label _restResources = null!;
	private SpinBox _restDice = null!;
	private Button _shortRest = null!;
	private Button _longRest = null!;
	private GameTime? _observedRestStarted;
	private bool _restDiceInitialized;

	private void BuildRestUi(VBoxContainer box)
	{
		_restResources = new Label();
		box.AddChild(_restResources);
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 5);
		box.AddChild(row);
		_restDice = new SpinBox { MinValue = 0, MaxValue = 0, Value = 0, Step = 1,
			CustomMinimumSize = new Vector2(54, 0), TooltipText = "Dadi Vita da spendere al termine del riposo breve." };
		row.AddChild(_restDice);
		_shortRest = new Button { Text = "Riposo breve", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			TooltipText = "Un'ora; spende i Dadi Vita scelti e recupera punti ferita." };
		_shortRest.Pressed += () => StartRest(longRest: false);
		row.AddChild(_shortRest);
		_longRest = new Button { Text = "Riposo lungo", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			TooltipText = "Otto ore di sonno; recupera tutti i punti ferita e i Dadi Vita." };
		_longRest.Pressed += () => StartRest(longRest: true);
		row.AddChild(_longRest);
	}

	private void StartRest(bool longRest)
	{
		StartActivity(new RestCommand { Actor = _sim.Player, Long = longRest,
			HitDice = longRest ? 0 : (int)_restDice.Value }, longRest ? "riposo lungo" : "riposo breve");
	}

	private void RefreshRestUi()
	{
		if (_view.Action is { Kind: "Rest" } rest)
			_observedRestStarted = rest.StartedAt;
		else if (_observedRestStarted is { } started)
		{
			var outcome = _view.RecentEvents.LastOrDefault(e => e.At >= started
				&& e.Kind is "RestCompleted" or "RestInterrupted");
			if (outcome is not null) ShowMessage(outcome.Description);
			_observedRestStarted = null;
		}

		var dice = _view.HitDice;
		_restResources.Text = $"PF {_view.HitPoints}/{_view.MaxHitPoints}";
		_restDice.Visible = dice is not null;
		_shortRest.Visible = dice is not null;
		_longRest.Visible = dice is not null;
		if (dice is null) return;
		if (!_restDiceInitialized)
		{
			_restDice.MaxValue = dice.Remaining;
			_restDice.Value = Math.Min(1, dice.Remaining);
			_restDiceInitialized = true;
		}
		_restResources.Text += $" · Dadi Vita: {dice.Remaining}/{dice.Max} (d{dice.Die})"
			+ (_view.LongRestFrom is { } next && next > _view.Now
				? $"\nProssimo riposo lungo: G{next.Day + 1} {next.Hour:00}:{next.Minute:00}" : "");
		_restDice.MaxValue = dice.Remaining;
		if (_restDice.Value > dice.Remaining) _restDice.Value = dice.Remaining;
		var busy = _view.Action is not null || _view.Combat is not null || _view.Dead || _view.Unconscious;
		_shortRest.Disabled = busy;
		_longRest.Disabled = busy || _view.LongRestFrom is { } available && available > _view.Now;
		_restDice.Editable = !busy;
	}

	internal void StartRestForSmoke(bool longRest, int dice)
	{
		_restDice.Value = dice;
		StartRest(longRest);
	}
	internal string RestResourcesForSmoke => _restResources.Text;
	internal bool RestButtonsEnabledForSmoke => !_shortRest.Disabled && !_longRest.Disabled;
}
