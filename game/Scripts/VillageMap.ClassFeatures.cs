using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Rules;

namespace RpgSandbox.Game;

/// <summary>R3a Paladin choices and Lay On Hands; the Sim validates every choice and target.</summary>
public partial class VillageMap
{
	private Button _classButton = null!;
	private PanelContainer _classPanel = null!;
	private VBoxContainer _classChoices = null!;
	private Label _classSummary = null!;
	private SpinBox _layAmount = null!;
	private OptionButton _layTarget = null!;
	private CheckBox _layCurePoison = null!;
	private Button _layButton = null!;
	private readonly List<ActorId> _layTargets = new();
	private string _classChoiceKey = "";

	private void BuildClassUi(CanvasLayer ui, BoxContainer main)
	{
		_classButton = new Button { Text = "Paladino" };
		_classButton.Pressed += () =>
		{
			_classPanel.Visible = !_classPanel.Visible;
			if (_classPanel.Visible) RebuildClassPanel();
		};
		main.AddChild(_classButton);

		_classPanel = new PanelContainer { OffsetLeft = 390, OffsetRight = 760,
			OffsetTop = 16, OffsetBottom = 500, Visible = false };
		_classPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.10f, 0.11f, 0.13f, 0.96f) });
		ui.AddChild(_classPanel);
		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		_classPanel.AddChild(scroll);
		var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		box.AddThemeConstantOverride("separation", 8);
		scroll.AddChild(box);
		var close = new Button { Text = "Chiudi" };
		close.Pressed += () => _classPanel.Visible = false;
		box.AddChild(close);
		_classSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_classSummary);
		box.AddChild(new Label { Text = "Imposizione delle mani" });
		_layTarget = new OptionButton();
		box.AddChild(_layTarget);
		_layAmount = new SpinBox { MinValue = 1, MaxValue = 1, Value = 1, Step = 1 };
		box.AddChild(_layAmount);
		_layCurePoison = new CheckBox { Text = "Togli Avvelenato (5 punti)" };
		_layCurePoison.Toggled += on => _layAmount.Editable = !on;
		box.AddChild(_layCurePoison);
		_layButton = new Button { Text = "Imponi le mani" };
		_layButton.Pressed += UseLayOnHands;
		box.AddChild(_layButton);
		_classChoices = new VBoxContainer();
		box.AddChild(_classChoices);
	}

	private void RefreshClassUi()
	{
		var pool = _view.LayOnHands;
		_classButton.Visible = pool is not null || _view.PendingChoices.Count > 0;
		_classButton.Text = _view.PendingChoices.Count > 0
			? $"Paladino · scelte ({_view.PendingChoices.Count})" : "Paladino";
		if (!_classPanel.Visible) return;
		if (_classChoiceKey != ChoiceKey()) { RebuildClassPanel(); return; }
		_classSummary.Text = $"Imposizione delle mani: {pool?.Remaining ?? 0}/{pool?.Max ?? 0} PF"
			+ (_view.FightingStyle is { } style ? $"\nStile: {style}" : "")
			+ (_view.MasteryWeapons.Count > 0 ? $"\nPadronanze: {string.Join(", ", _view.MasteryWeapons)}" : "");
		_layAmount.MaxValue = Math.Max(1, pool?.Remaining ?? 0);
		var canAct = _view.Combat is { } combat
			? combat.IsYourTurn && combat.You.BonusActionAvailable
			: _view.Action is null;
		_layButton.Disabled = pool is not { Remaining: > 0 } || !canAct || _view.Dead || _view.Unconscious;
		_layCurePoison.Disabled = pool is not { Remaining: >= 5 } || !canAct;
		if (_layCurePoison.Disabled) _layCurePoison.ButtonPressed = false;
		foreach (var choiceButton in _classChoices.GetChildren().OfType<Button>())
			choiceButton.Disabled = _view.Combat is not null || !choiceButton.HasMeta("validChoice")
				|| !choiceButton.GetMeta("validChoice").AsBool();
		foreach (var selector in _classChoices.GetChildren().OfType<OptionButton>())
			selector.Disabled = _view.Combat is not null || selector.ItemCount == 0;
	}

	private void RebuildClassPanel()
	{
		_classChoiceKey = ChoiceKey();
		_layTargets.Clear();
		_layTarget.Clear();
		_layTargets.Add(_view.Id);
		_layTarget.AddItem("Te stesso");
		foreach (var actor in _view.VisibleActors.Where(a => _view.Spot is { } here && a.Spot is { } there
			&& here.DistanceTo(there) <= 5 * Spot.PerFoot))
		{
			_layTargets.Add(actor.Id);
			_layTarget.AddItem(actor.Name);
		}
		_layTarget.Select(0);
		foreach (var child in _classChoices.GetChildren())
		{
			_classChoices.RemoveChild(child);
			child.QueueFree();
		}
		if (_view.PendingChoices.Count > 0)
			_classChoices.AddChild(new Label { Text = "Scelte di livello" });
		foreach (var choice in _view.PendingChoices)
		{
			_classChoices.AddChild(new Label { Text = choice.Title + (choice.Optional ? " (facoltativa)" : "") });
			var selectors = new List<OptionButton>();
			for (var i = 0; i < choice.Picks; i++)
			{
				var selector = new OptionButton();
				foreach (var option in choice.Options) selector.AddItem(option);
				if (selector.ItemCount > 0) selector.Select(Math.Min(i, selector.ItemCount - 1));
				selector.Disabled = _view.Combat is not null || choice.Options.Count == 0;
				_classChoices.AddChild(selector);
				selectors.Add(selector);
			}
			var submit = new Button { Text = "Conferma " + choice.Title,
				Disabled = _view.Combat is not null || choice.Options.Count < (choice.Kind == "AbilityScores" ? 1 : choice.Picks) };
			submit.SetMeta("validChoice", choice.Options.Count >= (choice.Kind == "AbilityScores" ? 1 : choice.Picks));
			submit.Pressed += () =>
			{
				var selected = selectors.Select(s => s.GetItemText(s.Selected)).ToArray();
				var result = _sim.Execute(new ChooseCommand { Actor = _sim.Player, Choice = choice.Id, Options = selected });
				ShowMessage(result.Message);
				Refresh();
				if (result.Success) RebuildClassPanel();
			};
			_classChoices.AddChild(submit);
		}
		RefreshClassUi();
	}

	private string ChoiceKey() => string.Join("|", _view.PendingChoices.Select(c =>
		$"{c.Id}:{c.Kind}:{c.Picks}:{string.Join(",", c.Options)}"));

	private void UseLayOnHands()
	{
		if (_layTarget.Selected < 0 || _layTarget.Selected >= _layTargets.Count) return;
		var result = _sim.Execute(new LayOnHandsCommand { Actor = _sim.Player,
			Target = _layTargets[_layTarget.Selected], Amount = (int)_layAmount.Value,
			CurePoison = _layCurePoison.ButtonPressed });
		ShowMessage(result.Message);
		Refresh();
	}

	internal string ClassSummaryForSmoke => _classSummary.Text;
	internal void OpenClassPanelForSmoke() { _classPanel.Visible = true; RebuildClassPanel(); }
	internal void LayOnHandsSelfForSmoke(int amount)
	{
		OpenClassPanelForSmoke();
		_layAmount.Value = amount;
		UseLayOnHands();
	}
	internal void ChooseFirstForSmoke(string title)
	{
		OpenClassPanelForSmoke();
		var button = _classChoices.GetChildren().OfType<Button>()
			.Single(b => b.Text == "Conferma " + title);
		if (button.Disabled) throw new InvalidOperationException($"Choice {title} is disabled in the UI");
		button.EmitSignal(Button.SignalName.Pressed);
	}
}
