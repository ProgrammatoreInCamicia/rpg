using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>A deliberate attack tool: select a weapon, inspect a target, then click to use it.</summary>
public partial class VillageMap
{
	private readonly List<(OptionButton Picker, Button Arm, CheckBox NonLethal)> _attackBars = new();
	private readonly List<Label> _attackHints = new();
	private string? _armedAttack;
	private bool _attackArmed;
	private bool _nonLethalEnabled;
	private string _attackOptionsKey = "";
	private readonly List<(HBoxContainer Row, OptionButton Smite, CheckBox Sacred)> _attackFeatureBars = new();
	private int _smiteChoice;
	private bool _sacredWeaponChosen;
	private const int FreeSmiteMenuId = 100;

	private void BuildAttackBar(BoxContainer parent)
	{
		var row = new HBoxContainer();
		parent.AddChild(row);
		var picker = new OptionButton { CustomMinimumSize = new Vector2(220, 0),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		picker.ItemSelected += index =>
		{
			_armedAttack = picker.GetItemText((int)index);
			RefreshAttackBars();
		};
		row.AddChild(picker);
		var arm = new Button { Text = "Attacca", ToggleMode = true,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		arm.Pressed += () =>
		{
			_attackArmed = !_attackArmed;
			RefreshAttackBars();
		};
		row.AddChild(arm);
		var nonLethal = new CheckBox { Text = "Non letale", TooltipText = "Vale solo per gli attacchi in mischia." };
		nonLethal.Toggled += on =>
		{
			_nonLethalEnabled = on;
			RefreshAttackBars();
		};
		row.AddChild(nonLethal);
		_attackBars.Add((picker, arm, nonLethal));
		var features = new HBoxContainer { Visible = false };
		parent.AddChild(features);
		var smite = new OptionButton { CustomMinimumSize = new Vector2(220, 0),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		smite.ItemSelected += _ =>
		{
			var selected = smite.GetItemId(smite.Selected);
			_smiteChoice = selected == FreeSmiteMenuId ? -1 : selected;
			RefreshAttackBars();
		};
		features.AddChild(smite);
		var sacred = new CheckBox { Text = "Arma sacra", TooltipText = "Consuma un uso di Incanalare divinità con l'attacco." };
		sacred.Toggled += on => { _sacredWeaponChosen = on; RefreshAttackBars(); };
		features.AddChild(sacred);
		_attackFeatureBars.Add((features, smite, sacred));
		var hint = new Label { Text = "Clicca un bersaglio. Esc annulla.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
		parent.AddChild(hint);
		_attackHints.Add(hint);
	}

	private void RefreshAttackBars()
	{
		if (_attackBars.Count == 0) return;
		var key = string.Join("|", _view.Attacks.Select(a => $"{a.Name}:{a.Remaining}"));
		if (key != _attackOptionsKey)
		{
			_attackOptionsKey = key;
			foreach (var (picker, _, _) in _attackBars)
			{
				picker.Clear();
				foreach (var attack in _view.Attacks)
				{
					picker.AddItem(attack.Name);
					if (attack.Uses is not null && attack.Remaining <= 0)
						picker.SetItemDisabled(picker.ItemCount - 1, true);
				}
			}
			if (_armedAttack is null || _view.Attacks.All(a => a.Name != _armedAttack || a.Uses is not null && a.Remaining <= 0))
				_armedAttack = _view.Attacks.FirstOrDefault(a => a.Uses is null || a.Remaining > 0)?.Name;
		}
		foreach (var (picker, arm, nonLethal) in _attackBars)
		{
			var index = _view.Attacks.ToList().FindIndex(a => a.Name == _armedAttack);
			if (index >= 0) picker.Select(index);
			arm.SetPressedNoSignal(_attackArmed);
			arm.Text = _attackArmed ? "Bersaglio… (Esc)" : "Attacca";
			arm.Disabled = _armedAttack is null || _view.Dead || _view.Unconscious
				|| _view.Combat is { IsYourTurn: false };
			nonLethal.SetPressedNoSignal(_nonLethalEnabled);
		}
		foreach (var hint in _attackHints) hint.Visible = _attackArmed;
		var combat = _view.Combat;
		var melee = _armedAttack is { } chosen && IsMeleeAttack(chosen);
		var smiteReady = combat is { IsYourTurn: true, You.BonusActionAvailable: true }
			&& melee && _view.Spells?.Prepared.Any(s => s.Id == "divine-smite") == true;
		var sacredReady = combat is { IsYourTurn: true, You.ActionAvailable: true }
			&& melee && _view.ChannelDivinity is { Remaining: > 0 } && _view.Subclass == "devotion";
		if (!smiteReady || _smiteChoice > 0 && combat!.You.SpellSlotSpent
			|| _smiteChoice == -1 && _view.Spells?.FreeSmite != true) _smiteChoice = 0;
		if (!sacredReady) _sacredWeaponChosen = false;
		foreach (var (row, smite, sacred) in _attackFeatureBars)
		{
			row.Visible = combat is not null && (_view.Spells?.Prepared.Any(s => s.Id == "divine-smite") == true || _view.ChannelDivinity is not null);
			smite.Clear();
			smite.AddItem("Senza Punizione", 0);
			if (_view.Spells?.FreeSmite == true) smite.AddItem("Punizione gratuita", FreeSmiteMenuId);
			foreach (var slot in _view.Spells?.Slots ?? Array.Empty<SlotView>())
			{
				smite.AddItem($"Punizione · slot {slot.Level}° ({slot.Remaining})", slot.Level);
				smite.SetItemDisabled(smite.ItemCount - 1, slot.Remaining == 0 || combat?.You.SpellSlotSpent == true);
			}
			var selectedId = _smiteChoice == -1 ? FreeSmiteMenuId : _smiteChoice;
			var index = Enumerable.Range(0, smite.ItemCount).FirstOrDefault(i => smite.GetItemId(i) == selectedId, 0);
			smite.Select(index);
			smite.Disabled = !smiteReady;
			sacred.Visible = _view.ChannelDivinity is not null;
			sacred.Disabled = !sacredReady;
			sacred.SetPressedNoSignal(_sacredWeaponChosen);
		}
	}

	private bool UseArmedAttack(ActorId target)
	{
		if (!_attackArmed || _armedAttack is not { } attack) return false;
		if (_view.Combat is { } combat)
		{
			var option = combat.Targets.FirstOrDefault(t => t.Target == target && t.Attack == attack);
			if (option is null || !option.Valid)
			{
				ShowMessage(option?.Why ?? "Non puoi colpire questo bersaglio.");
				return true;
			}
			_attackArmed = false;
			ExecuteCombatAttack(option);
			return true;
		}
		if (_view.VisibleActors.All(a => a.Id != target)) return true;
		_attackArmed = false;
		RefreshAttackBars();
		_openingTarget = target;
		_openingConfirmedAttack = attack;
		_openingConfirmedNonLethal = _nonLethalEnabled && IsMeleeAttack(attack);
		var name = _view.VisibleActors.Single(a => a.Id == target).Name;
		var preview = _sim.PreviewAttack(_sim.Player, target, attack, _openingConfirmedNonLethal);
		_openingConfirm.DialogText = $"Attaccare {name} con {attack}?"
			+ (preview.Reasons.Count > 0 ? $"\n{string.Join(", ", preview.Reasons)}" : "")
			+ (preview.Valid ? "" : $"\n{preview.Message}");
		_openingConfirm.PopupCentered();
		return true;
	}

	private void UpdateTargetingHover()
	{
		if (!_attackArmed || _armedAttack is not { } attack || _view.Map is null) return;
		var point = GetGlobalTransformWithCanvas().AffineInverse() * GetViewport().GetMousePosition();
		if (PersonAt(point) is not { } target)
		{
			foreach (var hint in _attackHints) hint.Text = "Clicca un bersaglio. Esc annulla.";
			return;
		}
		var name = _view.VisibleActors.FirstOrDefault(a => a.Id == target)?.Name ?? "Bersaglio";
		if (_view.Combat is { } combat)
		{
			var option = combat.Targets.FirstOrDefault(t => t.Target == target && t.Attack == attack);
			var message = option is null ? $"{name}: bersaglio non valido."
				: $"{name}: {(option.Valid ? "attaccabile" : option.Why ?? "non attaccabile") }"
					+ (option.Reasons.Count > 0 ? $" · {string.Join(", ", option.Reasons)}" : "");
			foreach (var hint in _attackHints) hint.Text = message;
		}
		else
		{
			var preview = _sim.PreviewAttack(_sim.Player, target, attack, _nonLethalEnabled && IsMeleeAttack(attack));
			var message = $"{name}: {(preview.Valid ? "attaccabile" : preview.Message)}"
				+ (preview.Reasons.Count > 0 ? $" · {string.Join(", ", preview.Reasons)}" : "");
			foreach (var hint in _attackHints) hint.Text = message;
		}
	}

	internal bool AttackModeForSmoke => _attackArmed;
	internal bool ClickArmedTargetForSmoke(ActorId target) => UseArmedAttack(target);
	internal void ArmAttackForSmoke(string attack)
	{
		_armedAttack = attack;
		_attackArmed = true;
		RefreshAttackBars();
	}
	internal void SelectAttackFeaturesForSmoke(bool freeSmite, bool sacredWeapon)
	{
		if (_view.Combat is null) throw new InvalidOperationException("Attack features need a fight");
		var (_, smite, sacred) = _attackFeatureBars[^1];
		if (freeSmite)
		{
			var free = Enumerable.Range(0, smite.ItemCount).FirstOrDefault(i => smite.GetItemId(i) == FreeSmiteMenuId, -1);
			if (free < 0 || smite.Disabled) throw new InvalidOperationException("Free Divine Smite is unavailable in the UI");
			smite.Select(free);
			_smiteChoice = -1;
		}
		if (sacredWeapon)
		{
			if (sacred.Disabled) throw new InvalidOperationException("Sacred Weapon is unavailable in the UI");
			sacred.SetPressedNoSignal(true);
			_sacredWeaponChosen = true;
		}
		RefreshAttackBars();
	}
}
