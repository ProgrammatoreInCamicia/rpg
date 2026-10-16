using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Spell and Channel Divinity controls. The Sim remains the authority on slots, targets and turn costs.</summary>
public partial class VillageMap
{
	private Button _spellButton = null!;
	private Button _combatSpellButton = null!;
	private VBoxContainer _combatSpellTile = null!;
	private Button _divineSenseButton = null!;
	private VBoxContainer _divineSenseTile = null!;
	private Button _spellDivineSenseButton = null!;
	private PanelContainer _spellPanel = null!;
	private Label _spellSummary = null!;
	private Label _magicStatus = null!;
	private Label _spellDescription = null!;
	private OptionButton _spellPicker = null!;
	private OptionButton _spellSlot = null!;
	private OptionButton _spellCondition = null!;
	private VBoxContainer _spellTargets = null!;
	private Button _castSpellButton = null!;
	private Button _prepareSpellsButton = null!;
	private readonly HashSet<ActorId> _chosenSpellTargets = new();
	private string? _chosenSpell;
	private int _chosenSlot;
	private string _spellOptionsKey = "";
	private string _spellTargetsKey = "";

	private void BuildSpellUi(CanvasLayer ui, BoxContainer explore)
	{
		_spellButton = new Button { Text = "Magia", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_spellButton.Pressed += ToggleSpellPanel;
		explore.AddChild(_spellButton);
		_spellPanel = HudPanel(-430, -16, 16, 540, rightAnchored: true);
		_spellPanel.Visible = false;
		_spellPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.10f, 0.11f, 0.13f, 0.97f) });
		ui.AddChild(_spellPanel);
		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		_spellPanel.AddChild(scroll);
		var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		box.AddThemeConstantOverride("separation", 7);
		scroll.AddChild(box);
		var header = new HBoxContainer();
		header.AddChild(new Label { Text = "Libro degli incantesimi", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		var close = new Button { Text = "Chiudi" };
		close.Pressed += () => _spellPanel.Visible = false;
		header.AddChild(close);
		box.AddChild(header);
		_spellSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_spellSummary);
		_prepareSpellsButton = new Button { Text = "Prepara o cambia incantesimi" };
		_prepareSpellsButton.Pressed += () => { _spellPanel.Visible = false; _classPanel.Visible = true; RebuildClassPanel(); };
		box.AddChild(_prepareSpellsButton);
		_spellDivineSenseButton = new Button { Text = "Percezione divina" };
		_spellDivineSenseButton.Pressed += UseDivineSense;
		box.AddChild(_spellDivineSenseButton);
		_spellPicker = new OptionButton();
		_spellPicker.ItemSelected += _ =>
		{
			_chosenSpell = _view.Spells?.Prepared.ElementAtOrDefault(_spellPicker.Selected)?.Id;
			_chosenSpellTargets.Clear();
			_spellTargetsKey = "";
			RefreshSpellUi();
		};
		box.AddChild(_spellPicker);
		_spellDescription = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_spellDescription);
		box.AddChild(new Label { Text = "Slot da usare" });
		_spellSlot = new OptionButton();
		_spellSlot.ItemSelected += _ =>
		{
			_chosenSlot = _spellSlot.GetItemId(_spellSlot.Selected);
			RefreshSpellCastState();
		};
		box.AddChild(_spellSlot);
		box.AddChild(new Label { Text = "Bersagli: seleziona chi riceve l'incantesimo" });
		_spellTargets = new VBoxContainer();
		box.AddChild(_spellTargets);
		_spellCondition = new OptionButton();
		_spellCondition.AddItem("Prima condizione rimovibile", 0);
		foreach (var (kind, name) in new[] {
			(ConditionKind.Blinded, "Accecato"), (ConditionKind.Deafened, "Assordato"),
			(ConditionKind.Paralyzed, "Paralizzato"), (ConditionKind.Poisoned, "Avvelenato") })
			_spellCondition.AddItem(name, (int)kind + 1);
		box.AddChild(_spellCondition);
		_castSpellButton = new Button { Text = "Lancia incantesimo" };
		_castSpellButton.Pressed += CastChosenSpell;
		box.AddChild(_castSpellButton);
	}

	private void BuildCombatSpellButtons(HBoxContainer row)
	{
		_combatSpellButton = new Button { TooltipText = "Apri il libro: scegli incantesimo, slot e bersagli. Il costo dipende dall'incantesimo." };
		_combatSpellButton.Pressed += ToggleSpellPanel;
		_combatSpellTile = AddActionTile(row, _combatSpellButton, "Magia", "Azione/bonus", "magic", MagicAccent);
		_divineSenseButton = new Button();
		_divineSenseButton.Pressed += UseDivineSense;
		_divineSenseTile = AddActionTile(row, _divineSenseButton, "Percezione", "Bonus", "eye", MagicAccent);
	}

	private void ToggleSpellPanel()
	{
		_spellPanel.Visible = !_spellPanel.Visible;
		if (_spellPanel.Visible)
		{
			_classPanel.Visible = false;
			_inventoryPanel.Visible = false;
			RefreshSpellUi();
		}
	}

	private void BuildMagicStatusUi(VBoxContainer box)
	{
		_magicStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_magicStatus);
	}

	private void RefreshMagicStatusUi()
	{
		var focus = _view.Spells?.Concentrating;
		var focusName = focus is null ? null : _view.Spells!.Prepared.FirstOrDefault(s => s.Id == focus)?.Name ?? focus;
		_magicStatus.Text = (focusName is null ? "" : $"Concentrazione: {focusName}")
			+ (_view.Effects.Count == 0 ? "" : $"{(focusName is null ? "" : "\n")}Effetti: {string.Join(", ", _view.Effects.Select(EffectName))}");
		_magicStatus.Visible = _magicStatus.Text.Length > 0;
	}

	private void RefreshSpellUi()
	{
		var book = _view.Spells;
		_spellButton.Visible = book is not null;
		_combatSpellButton.Visible = book is not null;
		_combatSpellTile.Visible = book is not null;
		_divineSenseButton.Visible = _view.ChannelDivinity is not null;
		_divineSenseTile.Visible = _view.ChannelDivinity is not null;
		_spellDivineSenseButton.Visible = _view.ChannelDivinity is not null;
		_divineSenseButton.Disabled = !CanUseDivineSense();
		_spellDivineSenseButton.Disabled = !CanUseDivineSense();
		SetActionTileEnabled(_divineSenseButton, !_divineSenseButton.Disabled);
		_divineSenseButton.TooltipText = $"Incanalare divinità: {_view.ChannelDivinity?.Remaining ?? 0}/{_view.ChannelDivinity?.Max ?? 0}. Usa l'azione bonus.";
		if (book is null) { _spellPanel.Visible = false; return; }
		_prepareSpellsButton.Visible = _view.PendingChoices.Any(c => c.Kind is "PreparedSpells" or "SwapSpell");
		_spellSummary.Text = $"Slot: {string.Join(" · ", book.Slots.Select(s => $"{s.Level}° {s.Remaining}/{s.Max}"))}"
			+ $"\nCD {book.SaveDc} · attacco +{book.AttackBonus}"
			+ $"\nConcentrazione: {(book.Concentrating is { } focus ? book.Prepared.FirstOrDefault(s => s.Id == focus)?.Name ?? focus : "nessuna")}"
			+ (_view.Effects.Count > 0 ? $"\nEffetti: {string.Join(", ", _view.Effects.Select(EffectName))}" : "")
			+ (book.Prepared.Any(s => s.Id == "divine-smite")
				? $"\nPunizione gratuita: {(book.FreeSmite ? "pronta" : "usata")}" : "")
			+ (_view.ChannelDivinity is { } channel ? $" · Incanalare: {channel.Remaining}/{channel.Max}" : "");
		var key = string.Join("|", book.Prepared.Select(s => s.Id));
		if (_spellOptionsKey != key)
		{
			_spellOptionsKey = key;
			_spellPicker.Clear();
			foreach (var spell in book.Prepared)
				_spellPicker.AddItem($"{spell.Name} · {spell.Level}°" + (spell.AlwaysPrepared ? " (sempre)" : ""));
			if (book.Prepared.All(s => s.Id != _chosenSpell)) _chosenSpell = book.Prepared.FirstOrDefault()?.Id;
			var index = book.Prepared.ToList().FindIndex(s => s.Id == _chosenSpell);
			if (index >= 0) _spellPicker.Select(index);
			_spellTargetsKey = "";
		}
		var chosen = book.Prepared.FirstOrDefault(s => s.Id == _chosenSpell);
		if (chosen is null) { _castSpellButton.Disabled = true; return; }
		var chosenIndex = book.Prepared.ToList().FindIndex(s => s.Id == _chosenSpell);
		if (chosenIndex >= 0 && _spellPicker.Selected != chosenIndex) _spellPicker.Select(chosenIndex);
		_spellDescription.Text = $"{chosen.Name}: {(chosen.Casting == "BonusAction" ? "azione bonus" : chosen.Casting == "Action" ? "azione" : "dopo un colpo in mischia")}, "
			+ (chosen.RangeFeet == 0 ? "su di te" : $"gittata {chosen.RangeFeet} piedi")
			+ $", fino a {chosen.Targets} bersagli" + (chosen.Concentration ? ", concentrazione" : "");
		_spellCondition.Visible = chosen.Id == "lesser-restoration";
		_spellSlot.Clear();
		foreach (var slot in book.Slots.Where(s => s.Level >= chosen.Level))
		{
			_spellSlot.AddItem($"{slot.Level}° · {slot.Remaining}/{slot.Max}", slot.Level);
			_spellSlot.SetItemDisabled(_spellSlot.ItemCount - 1, slot.Remaining == 0);
		}
		if (_spellSlot.ItemCount > 0)
		{
			var selected = Enumerable.Range(0, _spellSlot.ItemCount).FirstOrDefault(i => _spellSlot.GetItemId(i) == _chosenSlot && !_spellSlot.IsItemDisabled(i), -1);
			if (selected < 0) selected = Enumerable.Range(0, _spellSlot.ItemCount).FirstOrDefault(i => !_spellSlot.IsItemDisabled(i), -1);
			if (selected >= 0) { _spellSlot.Select(selected); _chosenSlot = _spellSlot.GetItemId(selected); }
		}
		var targetsKey = $"{chosen.Id}:{chosen.Targets}:{_view.Spot}:" + string.Join("|", _view.VisibleActors.Select(a => $"{a.Id}:{a.Spot}"));
		if (_spellTargetsKey != targetsKey)
		{
			_spellTargetsKey = targetsKey;
			RebuildSpellTargets(chosen);
		}
		RefreshSpellCastState();
	}

	private void RebuildSpellTargets(SpellView chosen)
	{
		foreach (var child in _spellTargets.GetChildren()) { _spellTargets.RemoveChild(child); child.QueueFree(); }
		if (chosen.Targets == 0 || chosen.Casting == "AfterMeleeHit")
		{
			_spellTargets.AddChild(new Label { Text = chosen.Targets == 0 ? "Su di te." : "Si usa con l'attacco, non da questo libro." });
			return;
		}
		var candidates = new[] { (_view.Id, "Te stesso") }
			.Concat(_view.VisibleActors.Select(a => (a.Id, a.Name))).ToArray();
		var available = candidates.Select(c => c.Id).ToHashSet();
		_chosenSpellTargets.RemoveWhere(id => !available.Contains(id));
		foreach (var (id, name) in candidates)
		{
			var check = new CheckBox { Text = name, ButtonPressed = _chosenSpellTargets.Contains(id) };
			check.Toggled += on => { if (on) _chosenSpellTargets.Add(id); else _chosenSpellTargets.Remove(id); RefreshSpellCastState(); };
			_spellTargets.AddChild(check);
		}
	}

	private void RefreshSpellCastState()
	{
		if (_view.Spells?.Prepared.FirstOrDefault(s => s.Id == _chosenSpell) is not { } chosen) return;
		var combat = _view.Combat;
		var canAct = combat is null ? _view.Action is null : combat.IsYourTurn
			&& (chosen.Casting == "Action" ? combat.You.ActionAvailable : combat.You.BonusActionAvailable)
			&& !combat.You.SpellSlotSpent;
		var limit = chosen.Targets + (chosen.Id == "bless" ? _chosenSlot - chosen.Level : 0);
		_castSpellButton.Disabled = _view.Dead || _view.Unconscious || chosen.Casting == "AfterMeleeHit"
			|| _spellSlot.Selected < 0 || _spellSlot.IsItemDisabled(_spellSlot.Selected)
			|| !canAct || chosen.Targets > 0 && (_chosenSpellTargets.Count == 0 || _chosenSpellTargets.Count > limit);
		_castSpellButton.TooltipText = chosen.Targets > 0 && _chosenSpellTargets.Count > limit
			? $"Scegli al massimo {limit} bersagli." : "La Sim verifica portata, vista e copertura al lancio.";
	}

	private void CastChosenSpell()
	{
		if (_castSpellButton.Disabled || _chosenSpell is null) return;
		var conditionId = _spellCondition.GetItemId(_spellCondition.Selected);
		var result = _sim.Execute(new CastCommand { Actor = _sim.Player, Spell = _chosenSpell,
			SlotLevel = _chosenSlot, Targets = _chosenSpellTargets.ToArray(),
			Condition = conditionId == 0 ? null : (ConditionKind)(conditionId - 1) });
		ShowMessage(result.Message);
		Refresh();
	}

	private void UseDivineSense()
	{
		if (!CanUseDivineSense()) return;
		var result = _sim.Execute(new ChannelDivinityCommand { Actor = _sim.Player, Effect = "DivineSense" });
		ShowMessage(result.Message);
		Refresh();
	}

	private bool CanUseDivineSense() => _view.ChannelDivinity is { Remaining: > 0 }
		&& !_view.Dead && !_view.Unconscious && (_view.Combat is { } combat
			? combat.IsYourTurn && combat.You.BonusActionAvailable : _view.Action is null);

	private static string EffectName(EffectView effect) => effect.Spell switch
	{
		"divine-sense" => "Percezione divina",
		"sacred-weapon" => "Arma sacra",
		_ => effect.Name,
	};

	internal string SpellSummaryForSmoke => _spellSummary.Text;
	internal string MagicStatusForSmoke => _magicStatus.Text;
	internal void UseDivineSenseForSmoke()
	{
		_spellPanel.Visible = true;
		_classPanel.Visible = false;
		RefreshSpellUi();
		if (_spellDivineSenseButton.Disabled) throw new InvalidOperationException("Percezione divina is disabled in the UI");
		_spellDivineSenseButton.EmitSignal(Button.SignalName.Pressed);
	}
	internal void CastSpellForSmoke(string spell, int slot, params ActorId[] targets)
	{
		_spellPanel.Visible = true;
		_classPanel.Visible = false;
		_chosenSpell = spell;
		_chosenSlot = slot;
		_chosenSpellTargets.Clear();
		foreach (var target in targets) _chosenSpellTargets.Add(target);
		_spellTargetsKey = "";
		RefreshSpellUi();
		CastChosenSpell();
	}
}
