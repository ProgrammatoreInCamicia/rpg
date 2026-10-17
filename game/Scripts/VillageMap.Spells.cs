using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>
/// Spells: each prepared spell is a slot on the hotbar (click to cast, with its targets clicked on the map); the
/// spellbook lists every spell of the class with its description and is where spells are prepared. Channel Divinity
/// controls. The Sim remains the authority on slots, targets and turn costs.
/// </summary>
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
	private VBoxContainer _bookPreparation = null!;
	private VBoxContainer _bookList = null!;
	private Label _bookDetail = null!;
	private string _bookKey = "";
	private string? _bookSelected;
	private readonly HashSet<string> _bookPicks = new();
	private string? _bookSwapOut;
	private string? _bookSwapIn;

	// Casting from the hotbar: the spell chosen, the slot level, the targets clicked so far.
	private SpellView? _castingSpell;
	private int _castingSlot;
	private readonly List<ActorId> _castingTargets = new();
	private PanelContainer _castPrompt = null!;
	private Label _castPromptText = null!;
	private Button _castPromptGo = null!;
	private PopupMenu _castSlotMenu = null!;
	private string? _castSlotMenuSpell;

	// The spell slots on the hotbar, in the Paladin section, rebuilt when the prepared spells change.
	private readonly List<(Button Button, string Spell, bool Combat)> _quickSpells = new();
	private string _quickSpellsKey = "";

	private static string SpellIcon(string spell) => spell switch
	{
		"bless" => "bless",
		"cure-wounds" => "heal",
		"divine-favor" => "favor",
		"divine-smite" => "smite",
		"shield-of-faith" => "shield",
		"aid" => "aid",
		"lesser-restoration" => "restore",
		"protection-from-evil-and-good" => "ward",
		_ => "magic",
	};

	private static string CastingName(string casting) => casting switch
	{
		"BonusAction" => "azione bonus",
		"Action" => "azione",
		_ => "azione bonus, dopo un colpo in mischia",
	};

	private void BuildSpellUi(CanvasLayer ui, BoxContainer explore)
	{
		_spellButton = new Button { Text = "Magia", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_spellButton.Pressed += ToggleSpellPanel;
		explore.AddChild(_spellButton);

		_spellPanel = HudPanel(-500, -70, 16, 548, rightAnchored: true);
		_spellPanel.Visible = false;
		_spellPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.07f, 0.064f, 0.058f, 0.98f), BorderColor = BarBronze,
			BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
			CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
			ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10,
		});
		ui.AddChild(_spellPanel);
		var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		box.AddThemeConstantOverride("separation", 8);
		_spellPanel.AddChild(box);
		var header = new HBoxContainer();
		var title = new Label { Text = "Libro degli incantesimi", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		title.AddThemeFontSizeOverride("font_size", 17);
		title.AddThemeColorOverride("font_color", BarGold);
		header.AddChild(title);
		var close = new Button { Text = "Chiudi" };
		close.Pressed += () => _spellPanel.Visible = false;
		header.AddChild(close);
		box.AddChild(header);
		_spellSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_spellSummary.AddThemeFontSizeOverride("font_size", 12);
		_spellSummary.AddThemeColorOverride("font_color", BarText);
		box.AddChild(_spellSummary);
		_spellDivineSenseButton = new Button { Text = "Percezione divina" };
		_spellDivineSenseButton.Pressed += UseDivineSense;
		box.AddChild(_spellDivineSenseButton);
		_bookPreparation = new VBoxContainer();
		_bookPreparation.AddThemeConstantOverride("separation", 4);
		box.AddChild(_bookPreparation);
		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		box.AddChild(scroll);
		_bookList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_bookList.AddThemeConstantOverride("separation", 3);
		scroll.AddChild(_bookList);
		var detailFrame = new PanelContainer { CustomMinimumSize = new Vector2(0, 150) };
		detailFrame.AddThemeStyleboxOverride("panel", SlotStyle(BarBronze.Darkened(0.3f), new Color(0.1f, 0.09f, 0.08f), 1, 4));
		box.AddChild(detailFrame);
		_bookDetail = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Text = "Scegli un incantesimo per leggerlo." };
		_bookDetail.AddThemeFontSizeOverride("font_size", 13);
		detailFrame.AddChild(_bookDetail);

		_castPrompt = HudPanel(330, 900, -230, -166, bottomAnchored: true);
		_castPrompt.Visible = false;
		_castPrompt.AddThemeStyleboxOverride("panel", SlotStyle(MagicAccent, new Color(0.08f, 0.07f, 0.1f, 0.96f), 1, 6));
		ui.AddChild(_castPrompt);
		var prompt = new HBoxContainer();
		prompt.AddThemeConstantOverride("separation", 8);
		_castPrompt.AddChild(prompt);
		_castPromptText = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		prompt.AddChild(_castPromptText);
		_castPromptGo = new Button { Text = "Lancia" };
		_castPromptGo.Pressed += CastNow;
		prompt.AddChild(_castPromptGo);
		var cancel = new Button { Text = "Annulla" };
		cancel.Pressed += () => { CancelCasting(); RefreshQuickbar(); };
		prompt.AddChild(cancel);

		_castSlotMenu = new PopupMenu();
		_castSlotMenu.IdPressed += id =>
		{
			if (_view.Spells?.Prepared.FirstOrDefault(s => s.Id == _castSlotMenuSpell) is { } spell)
				BeginCasting(spell, (int)id);
		};
		ui.AddChild(_castSlotMenu);
	}

	private void BuildCombatSpellButtons(HBoxContainer row)
	{
		_combatSpellButton = new Button { TooltipText = "Apri il libro degli incantesimi." };
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
			_bookKey = "";
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

	// ---------------------------------------------------------------- the spellbook

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
		if (book is null) { _spellPanel.Visible = false; CancelCasting(); return; }
		if (_castingSpell is not null && book.Prepared.All(s => s.Id != _castingSpell.Id)) CancelCasting();
		_spellSummary.Text = $"Slot: {string.Join(" · ", book.Slots.Select(s => $"{s.Level}° {s.Remaining}/{s.Max}"))}"
			+ $" · CD {book.SaveDc} · attacco +{book.AttackBonus}"
			+ (book.Prepared.Any(s => s.Id == "divine-smite")
				? $"\nPunizione gratuita: {(book.FreeSmite ? "pronta" : "usata")}" : "")
			+ (_view.ChannelDivinity is { } channel ? $" · Incanalare: {channel.Remaining}/{channel.Max}" : "");
		if (!_spellPanel.Visible) return;
		var key = string.Join("|", book.ClassSpells.Select(s => $"{s.Id}:{s.Prepared}:{s.WithinReach}")) + "#"
			+ string.Join("|", _view.PendingChoices.Select(c => $"{c.Id}:{c.Picks}:{string.Join(",", c.Options)}"))
			+ $"#{_bookSelected}#{string.Join(",", _bookPicks)}#{_bookSwapOut}#{_bookSwapIn}#{_view.Combat is null}";
		if (key == _bookKey) return;
		_bookKey = key;
		RebuildBook(book);
	}

	private void RebuildBook(SpellbookView book)
	{
		ClearChildren(_bookPreparation);
		ClearChildren(_bookList);
		var prepare = _view.PendingChoices.FirstOrDefault(c => c.Kind == "PreparedSpells");
		var swap = _view.PendingChoices.FirstOrDefault(c => c.Kind == "SwapSpell");
		_bookPicks.RemoveWhere(id => prepare is null || !prepare.Options.Contains(id));
		if (swap is null) { _bookSwapOut = null; _bookSwapIn = null; }
		if (prepare is not null)
		{
			var note = BookNote($"Puoi preparare ancora {prepare.Picks} incantesimi: spuntali nella lista ({_bookPicks.Count}/{prepare.Picks}).");
			_bookPreparation.AddChild(note);
			var confirm = new Button { Text = "Prepara gli incantesimi scelti",
				Disabled = _bookPicks.Count != Math.Min(prepare.Picks, prepare.Options.Count) || _view.Combat is not null };
			confirm.Pressed += () => Choose(prepare.Id, _bookPicks.ToArray());
			_bookPreparation.AddChild(confirm);
		}
		else if (swap is not null)
		{
			_bookPreparation.AddChild(BookNote("Dopo il riposo lungo puoi cambiare un incantesimo preparato: "
				+ "scegli quale togliere (−) e quale prendere (+)."));
			var confirm = new Button { Text = "Cambia l'incantesimo",
				Disabled = _bookSwapOut is null || _bookSwapIn is null || _view.Combat is not null };
			confirm.Pressed += () => Choose(swap.Id, new[] { _bookSwapOut!, _bookSwapIn! });
			_bookPreparation.AddChild(confirm);
		}
		foreach (var level in book.ClassSpells.GroupBy(s => s.Level))
		{
			var heading = new Label { Text = $"{level.Key}° livello" };
			heading.AddThemeFontSizeOverride("font_size", 12);
			heading.AddThemeColorOverride("font_color", BarBronze.Lightened(0.3f));
			_bookList.AddChild(heading);
			foreach (var spell in level)
				_bookList.AddChild(BookRow(spell, prepare, swap));
		}
		var selected = book.ClassSpells.FirstOrDefault(s => s.Id == _bookSelected);
		_bookDetail.Text = selected is null ? "Scegli un incantesimo per leggerlo."
			: $"{selected.Name} · {selected.Level}° livello\n"
			  + $"Lancio: {CastingName(selected.Casting)} · {(selected.RangeFeet == 0 ? "su di te" : selected.RangeFeet == 5 ? "a contatto" : $"gittata {selected.RangeFeet} piedi")}"
			  + (selected.Targets > 0 ? $" · fino a {selected.Targets} bersagli" : "") + (selected.Concentration ? " · concentrazione" : "")
			  + $"\n\n{selected.Description}";
	}

	private static Label BookNote(string text)
	{
		var note = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		note.AddThemeFontSizeOverride("font_size", 12);
		note.AddThemeColorOverride("font_color", BarGold);
		return note;
	}

	private Control BookRow(SpellView spell, ChoiceView? prepare, ChoiceView? swap)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		var icon = new TextureRect { Texture = ActionIcon(SpellIcon(spell.Id)), CustomMinimumSize = new Vector2(22, 22),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Modulate = spell.Prepared ? MagicAccent.Lightened(0.3f) : new Color(0.5f, 0.48f, 0.45f) };
		row.AddChild(icon);
		var name = new Button { Text = spell.Name, Flat = true, ToggleMode = true, ButtonPressed = spell.Id == _bookSelected,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left,
			TooltipText = spell.Description };
		name.AddThemeColorOverride("font_color", spell.Prepared ? new Color(0.93f, 0.89f, 0.8f) : new Color(0.62f, 0.59f, 0.54f));
		name.Pressed += () => { _bookSelected = spell.Id; RefreshSpellUi(); };
		row.AddChild(name);
		var tag = spell.AlwaysPrepared ? "sempre preparato" : spell.Prepared ? "preparato" : !spell.WithinReach ? "oltre i tuoi slot" : "";
		var tagLabel = new Label { Text = tag, VerticalAlignment = VerticalAlignment.Center };
		tagLabel.AddThemeFontSizeOverride("font_size", 11);
		tagLabel.AddThemeColorOverride("font_color", spell.Prepared ? MagicAccent.Lightened(0.35f) : BarText.Darkened(0.35f));
		row.AddChild(tagLabel);
		if (prepare is not null && prepare.Options.Contains(spell.Id))
		{
			var pick = new CheckBox { ButtonPressed = _bookPicks.Contains(spell.Id), TooltipText = "Prepara questo incantesimo" };
			pick.Toggled += on =>
			{
				if (on && _bookPicks.Count < prepare.Picks) _bookPicks.Add(spell.Id); else _bookPicks.Remove(spell.Id);
				RefreshSpellUi();
			};
			row.AddChild(pick);
		}
		else if (swap is not null && swap.Options.Contains(spell.Id))
		{
			var outgoing = spell.Prepared;
			var chosen = outgoing ? _bookSwapOut == spell.Id : _bookSwapIn == spell.Id;
			var mark = new Button { Text = outgoing ? "−" : "+", ToggleMode = true, ButtonPressed = chosen,
				TooltipText = outgoing ? "Togli questo incantesimo" : "Prepara questo al suo posto" };
			mark.Pressed += () =>
			{
				if (outgoing) _bookSwapOut = chosen ? null : spell.Id; else _bookSwapIn = chosen ? null : spell.Id;
				RefreshSpellUi();
			};
			row.AddChild(mark);
		}
		return row;
	}

	private void Choose(string choice, IReadOnlyList<string> options)
	{
		var result = _sim.Execute(new ChooseCommand { Actor = _sim.Player, Choice = choice, Options = options });
		ShowMessage(result.Message);
		if (result.Success) { _bookPicks.Clear(); _bookSwapOut = null; _bookSwapIn = null; }
		_bookKey = "";
		Refresh();
	}

	private static void ClearChildren(Node parent)
	{
		foreach (var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
	}

	// ---------------------------------------------------------------- spells on the hotbar

	/// <summary>Rebuilds the spell slots of the Paladin section when the prepared spells change, then their state.</summary>
	private void RefreshQuickSpells()
	{
		var prepared = _view.Spells?.Prepared ?? Array.Empty<SpellView>();
		var key = string.Join("|", prepared.Select(s => s.Id));
		if (key != _quickSpellsKey)
		{
			_quickSpellsKey = key;
			foreach (var (button, _, _) in _quickSpells)
			{
				_quickActions.Remove(button);
				button.GetParent()?.RemoveChild(button);
				button.QueueFree();
			}
			_quickSpells.Clear();
			foreach (var combat in new[] { false, true })
			foreach (var spell in prepared)
				_quickSpells.Add((QuickSpellSlot(spell, combat), spell.Id, combat));
		}
		foreach (var (button, id, _) in _quickSpells)
		{
			if (prepared.FirstOrDefault(s => s.Id == id) is not { } spell) continue;
			var why = SpellRefusal(spell);
			button.Disabled = why is not null;
			button.TooltipText = SpellTooltip(spell) + (why is null ? "" : $"\n{why}");
			button.SetPressedNoSignal(spell.Id == "divine-smite" ? _smiteChoice != 0 : _castingSpell?.Id == spell.Id);
		}
		foreach (var grids in new[] { _quickExploreGrids, _quickCombatGrids })
		{
			var paladin = grids[QuickSection.Paladin];
			var shown = paladin.GetChildren().OfType<Button>().Count(b => b.Visible);
			paladin.Columns = Math.Max(2, (shown + SlotRows - 1) / SlotRows);
		}
	}

	private Button QuickSpellSlot(SpellView spell, bool combat)
	{
		var button = new Button { ToggleMode = true };
		var cost = spell.Casting == "Action" ? QuickFilter.Action : QuickFilter.Bonus;
		QuickAction(QuickSection.Paladin, combat, SpellIcon(spell.Id), SpellTooltip(spell), MagicAccent, () => { }, cost, button);
		button.Pressed += () => ClickSpell(spell.Id);
		button.GuiInput += input =>
		{
			if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } click)
				OpenSlotMenu(spell.Id, click.GlobalPosition);
		};
		var level = new Label { Text = spell.Level.ToString(), Position = new Vector2(SlotSize - 11, SlotSize - 16),
			MouseFilter = Control.MouseFilterEnum.Ignore };
		level.AddThemeFontSizeOverride("font_size", 10);
		level.AddThemeColorOverride("font_color", MagicAccent.Lightened(0.45f));
		level.AddThemeColorOverride("font_outline_color", Colors.Black);
		level.AddThemeConstantOverride("outline_size", 3);
		button.AddChild(level);
		// Keep the empty slots after the spells.
		var grid = (combat ? _quickCombatGrids : _quickExploreGrids)[QuickSection.Paladin];
		grid.MoveChild(button, grid.GetChildren().Count(c => c is Button && c != button));
		return button;
	}

	private static string SpellTooltip(SpellView spell) =>
		$"{spell.Name} · {spell.Level}° · {CastingName(spell.Casting)}"
		+ (spell.RangeFeet == 0 ? " · su di te" : spell.RangeFeet == 5 ? " · a contatto" : $" · {spell.RangeFeet} piedi")
		+ (spell.Concentration ? " · concentrazione" : "")
		+ $"\n{spell.Description}"
		+ (spell.Casting == "AfterMeleeHit" ? "\nClic: pronta per il prossimo colpo in mischia." : "\nClic: lancia con lo slot più basso · clic destro: scegli lo slot.");

	/// <summary>Why the spell cannot be cast now, or null. The Sim checks again on casting.</summary>
	private string? SpellRefusal(SpellView spell)
	{
		if (_view.Dead || _view.Unconscious) return "Non puoi lanciare incantesimi ora.";
		var book = _view.Spells!;
		if (spell.Id == "divine-smite")
			return _view.Combat is not { IsYourTurn: true } ? "Si lancia colpendo in mischia, nel tuo turno di uno scontro."
				: !book.FreeSmite && !book.Slots.Any(s => s.Remaining > 0) ? "Nessuno slot rimasto." : null;
		if (!book.Slots.Any(s => s.Level >= spell.Level && s.Remaining > 0)) return "Nessuno slot rimasto di quel livello.";
		if (_view.Combat is { } combat)
		{
			if (!combat.IsYourTurn) return "Non è il tuo turno.";
			if (combat.You.SpellSlotSpent) return "Hai già speso uno slot in questo turno.";
			if (spell.Casting == "Action" ? !combat.You.ActionAvailable : !combat.You.BonusActionAvailable)
				return spell.Casting == "Action" ? "Hai già usato l'azione." : "Hai già usato l'azione bonus.";
		}
		else if (_view.Action is not null) return "Sei impegnato in un'altra attività.";
		return null;
	}

	private void ClickSpell(string id)
	{
		if (_view.Spells?.Prepared.FirstOrDefault(s => s.Id == id) is not { } spell) return;
		if (SpellRefusal(spell) is { } why) { ShowMessage(why); RefreshQuickbar(); return; }
		if (spell.Id == "divine-smite") { ToggleSmite(); return; }
		if (_castingSpell?.Id == spell.Id) { CancelCasting(); RefreshQuickbar(); return; }
		var lowest = _view.Spells.Slots.First(s => s.Level >= spell.Level && s.Remaining > 0).Level;
		BeginCasting(spell, lowest);
	}

	private void OpenSlotMenu(string id, Vector2 at)
	{
		if (_view.Spells?.Prepared.FirstOrDefault(s => s.Id == id) is not { } spell || spell.Casting == "AfterMeleeHit") return;
		_castSlotMenuSpell = id;
		_castSlotMenu.Clear();
		foreach (var slot in _view.Spells.Slots.Where(s => s.Level >= spell.Level))
		{
			_castSlotMenu.AddItem($"Lancia con uno slot di {slot.Level}° ({slot.Remaining}/{slot.Max})", slot.Level);
			_castSlotMenu.SetItemDisabled(_castSlotMenu.ItemCount - 1, slot.Remaining == 0 || SpellRefusal(spell) is not null);
		}
		_castSlotMenu.Position = new Vector2I((int)at.X, (int)at.Y - 30 * _castSlotMenu.ItemCount);
		_castSlotMenu.Popup();
	}

	/// <summary>A spell on oneself is cast at once; one with targets waits for them to be clicked on the map.</summary>
	private void BeginCasting(SpellView spell, int slot)
	{
		_attackArmed = false;
		_castingSpell = spell;
		_castingSlot = slot;
		_castingTargets.Clear();
		if (spell.Targets == 0) { CastNow(); return; }
		RefreshCastPrompt();
		RefreshAttackBars();
		RefreshQuickbar();
	}

	private int CastingLimit => _castingSpell is null ? 0
		: _castingSpell.Targets + (_castingSpell.Id == "bless" ? _castingSlot - _castingSpell.Level : 0);

	private void RefreshCastPrompt()
	{
		_castPrompt.Visible = _castingSpell is not null;
		if (_castingSpell is not { } spell) return;
		_castPromptText.Text = $"{spell.Name} · slot {_castingSlot}° · "
			+ $"{_castingTargets.Count}/{CastingLimit} bersagli (anche te). Clicca sulla mappa; Esc annulla.";
		_castPromptGo.Disabled = _castingTargets.Count == 0;
	}

	/// <summary>A click on the map while choosing targets: a creature, or oneself. Returns whether it was taken.</summary>
	private bool HandleSpellClick(Vector2 point)
	{
		if (_castingSpell is null) return false;
		ActorId? target = PersonAt(point);
		if (target is null && _tokens.TryGetValue(_view.Id, out var mine)
			&& new Rect2(mine.Position + new Vector2(-14, -40), new Vector2(28, 48)).HasPoint(point))
			target = _view.Id;
		if (target is not { } chosen)
		{
			ShowMessage($"Clicca una creatura per {_castingSpell.Name}. Esc annulla.");
			return true;
		}
		if (!_castingTargets.Remove(chosen)) _castingTargets.Add(chosen);
		if (_castingTargets.Count >= CastingLimit) CastNow();
		else RefreshCastPrompt();
		return true;
	}

	private void CastNow()
	{
		if (_castingSpell is not { } spell) return;
		var result = _sim.Execute(new CastCommand { Actor = _sim.Player, Spell = spell.Id, SlotLevel = _castingSlot,
			Targets = _castingTargets.ToArray() });
		ShowMessage(result.Message);
		CancelCasting();
		Refresh();
	}

	private void CancelCasting()
	{
		_castingSpell = null;
		_castingTargets.Clear();
		if (_castPrompt is not null) _castPrompt.Visible = false;
	}

	/// <summary>Divine Smite: readied for the next melee hit (the free use first, else the lowest slot), with the weapon in hand.</summary>
	private void ToggleSmite()
	{
		if (_smiteChoice != 0)
		{
			_smiteChoice = 0;
			RefreshAttackBars();
			RefreshQuickbar();
			return;
		}
		SelectEquippedAttack(EquipmentSlot.MainHand);
		var book = _view.Spells!;
		_smiteChoice = book.FreeSmite ? -1 : book.Slots.FirstOrDefault(s => s.Remaining > 0)?.Level ?? 0;
		RefreshAttackBars();
		ShowMessage(_smiteChoice != 0 ? "Punizione divina pronta: colpisci un nemico in mischia."
			: "Punizione divina non è disponibile con l'attacco scelto.");
		RefreshQuickbar();
	}

	// ---------------------------------------------------------------- Channel Divinity

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
	internal bool SpellOnHotbarForSmoke(string spell) => _quickSpells.Any(q => q.Spell == spell && q.Button.Visible
		&& q.Button.GetParent()?.GetParent()?.GetParent() is CanvasItem { Visible: true });
	internal void OpenSpellbookForSmoke(string spell)
	{
		_bookSelected = spell;
		if (!_spellPanel.Visible) ToggleSpellPanel(); else { _bookKey = ""; RefreshSpellUi(); }
	}
	internal int SpellbookRowsForSmoke => _bookList.GetChildren().OfType<HBoxContainer>().Count();
	internal string SpellbookDetailForSmoke => _bookDetail.Text;
	internal void ClickSpellForSmoke(string spell) => ClickSpell(spell);
	internal bool CastPromptVisibleForSmoke => _castPrompt.Visible;
	internal void CancelCastingForSmoke() { CancelCasting(); RefreshQuickbar(); }
	internal void UseDivineSenseForSmoke()
	{
		_spellPanel.Visible = true;
		_classPanel.Visible = false;
		RefreshSpellUi();
		if (_spellDivineSenseButton.Disabled) throw new InvalidOperationException("Percezione divina is disabled in the UI");
		_spellDivineSenseButton.EmitSignal(Button.SignalName.Pressed);
	}

	/// <summary>Casts as the player does: the spell's slot on the hotbar, then the targets clicked.</summary>
	internal void CastSpellForSmoke(string spell, int slot, params ActorId[] targets)
	{
		var shown = _quickSpells.FirstOrDefault(q => q.Spell == spell && q.Combat == (_view.Combat is not null));
		if (shown.Button is null || shown.Button.Disabled)
			throw new InvalidOperationException($"{spell} is not castable from the hotbar: {shown.Button?.TooltipText}");
		var view = _view.Spells!.Prepared.Single(s => s.Id == spell);
		BeginCasting(view, slot);
		if (view.Targets == 0) return;
		foreach (var target in targets)
			if (!_castingTargets.Contains(target)) _castingTargets.Add(target);
		CastNow();
	}
}
