using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	private ActorId? _loreTarget;
	private PanelContainer _personMenu = null!;
	private Label _personMenuTitle = null!;
	private Button _personAttack = null!;
	private PanelContainer _creaturePanel = null!;
	private Label _creatureText = null!;
	private Button _observe = null!;

	private void BuildLoreUi(CanvasLayer ui)
	{
		_personMenu = new PanelContainer { OffsetLeft = 900, OffsetRight = 1264, OffsetTop = 100,
			OffsetBottom = 230, Visible = false };
		_personMenu.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.10f, 0.11f, 0.13f, 0.96f) });
		ui.AddChild(_personMenu);
		var menu = new VBoxContainer();
		_personMenu.AddChild(menu);
		_personMenuTitle = new Label();
		menu.AddChild(_personMenuTitle);
		var info = new Button { Text = "Informazioni" };
		info.Pressed += OpenCreatureCard;
		menu.AddChild(info);
		_personAttack = new Button { Text = "Attacca" };
		_personAttack.Pressed += () =>
		{
			_personMenu.Visible = false;
			if (_loreTarget is { } target) SelectOpeningAttack(target);
		};
		menu.AddChild(_personAttack);
		var cancel = new Button { Text = "Chiudi" };
		cancel.Pressed += () => _personMenu.Visible = false;
		menu.AddChild(cancel);

		_creaturePanel = new PanelContainer { OffsetLeft = 440, OffsetRight = 840, OffsetTop = 100,
			OffsetBottom = 510, Visible = false };
		_creaturePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.10f, 0.11f, 0.13f, 0.96f) });
		ui.AddChild(_creaturePanel);
		var content = new VBoxContainer();
		_creaturePanel.AddChild(content);
		var title = new Label { Text = "Scheda della creatura" };
		title.AddThemeFontSizeOverride("font_size", 20);
		content.AddChild(title);
		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		content.AddChild(scroll);
		_creatureText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		scroll.AddChild(_creatureText);
		_observe = new Button { Text = "Osserva" };
		_observe.Pressed += ObserveCreature;
		content.AddChild(_observe);
		var close = new Button { Text = "Chiudi" };
		close.Pressed += () => _creaturePanel.Visible = false;
		content.AddChild(close);
	}

	private void OpenPersonMenu(ActorId target)
	{
		if (_view.VisibleActors.FirstOrDefault(a => a.Id == target) is not { } actor)
			return;
		_loreTarget = target;
		_personMenuTitle.Text = actor.Name;
		_personAttack.Disabled = _view.Combat is not null || _view.Dead;
		_creaturePanel.Visible = false;
		_personMenu.Visible = true;
	}

	private void OpenCreatureCard()
	{
		_personMenu.Visible = false;
		_creaturePanel.Visible = true;
		RefreshCreatureCard();
	}

	private void RefreshCreatureCard()
	{
		if (!_creaturePanel.Visible)
			return;
		if (_loreTarget is not { } target || _view.VisibleActors.FirstOrDefault(a => a.Id == target) is not { } actor)
		{
			_creaturePanel.Visible = false;
			return;
		}
		var card = actor.Card;
		if (card is null)
		{
			_creatureText.Text = actor.Name;
			_observe.Disabled = true;
			return;
		}
		var lines = new List<string> { actor.Name };
		if (card.Kind is { } kind) lines.Add($"Tipo: {kind}");
		if (actor.Condition is { } condition) lines.Add($"Condizione: {condition}");
		lines.Add(ArmorKnowledge(card));
		if (card.Tells.Count > 0) lines.Add("Osservato: " + string.Join(" ", card.Tells));
		if (card.KindLore?.Tells.Count > 0) lines.Add("Del tipo: " + string.Join(" ", card.KindLore.Tells));
		var attacks = card.Attacks.Concat(card.KindLore?.Attacks ?? Array.Empty<string>()).Distinct().ToArray();
		if (attacks.Length > 0) lines.Add("Attacchi visti: " + string.Join(", ", attacks));
		if (card.Tells.Count == 0 && card.KindLore?.Tells.Count is null or 0 && attacks.Length == 0)
			lines.Add("Non sai ancora altro di questa creatura.");
		_creatureText.Text = string.Join("\n\n", lines);
		_observe.Disabled = _view.Dead || _view.Combat is { } fight && (!fight.IsYourTurn || !fight.You.ActionAvailable) || !card.CanObserve;
	}

	private static string ArmorKnowledge(CreatureCardView card) => card.ArmorClass is { } exact ? $"CA dedotta: {exact}"
		: card.ArmorClassAbove is { } above && card.ArmorClassAtMost is { } ceiling
			? $"CA dedotta: più di {above}, al massimo {ceiling}"
		: card.ArmorClassAbove is { } lower ? $"CA dedotta: più di {lower}"
		: card.ArmorClassAtMost is { } upper ? $"CA dedotta: al massimo {upper}"
		: "CA: non conosciuta";

	private void ObserveCreature()
	{
		if (_loreTarget is not { } target) return;
		var result = _sim.Execute(new ObserveCommand { Actor = _sim.Player, Target = target });
		ShowMessage(result.Message);
		Refresh();
	}

	internal void OpenCreatureCardForSmoke(ActorId target) { OpenPersonMenu(target); OpenCreatureCard(); }
	internal void ObserveCreatureForSmoke() => ObserveCreature();
	internal string CreatureCardTextForSmoke => _creatureText.Text;
}
