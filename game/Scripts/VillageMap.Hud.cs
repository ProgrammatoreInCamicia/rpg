using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>The compact world HUD and a player-visible, scrolling account of what happened.</summary>
public partial class VillageMap
{
	private RichTextLabel _hudLog = null!;
	private PanelContainer _hudLogPanel = null!;
	private Button _hudLogExpand = null!;
	private bool _hudLogExpanded;
	private bool _hudLogHovered;
	private bool _hudWasInCombat;
	private Label _hudHitPoints = null!;
	private PanelContainer _exploreActionPanel = null!;
	private PanelContainer _restPanel = null!;
	private readonly List<string> _hudLines = new();
	private readonly HashSet<FactId> _hudFactIds = new();
	private int _hudCombatLines;
	private bool _hudShowingLastFight;
	private string _hudMessage = "";

	private static PanelContainer HudPanel(float left, float right, float top, float bottom,
		bool bottomAnchored = false, bool rightAnchored = false)
	{
		return new PanelContainer
		{
			AnchorLeft = rightAnchored ? 1 : 0, AnchorRight = rightAnchored ? 1 : 0,
			AnchorTop = bottomAnchored ? 1 : 0, AnchorBottom = bottomAnchored ? 1 : 0,
			OffsetLeft = left, OffsetRight = right, OffsetTop = top, OffsetBottom = bottom,
		};
	}

	private void BuildHudLog(CanvasLayer ui)
	{
		_hudLogPanel = HudPanel(-330, -66, -116, -16, bottomAnchored: true, rightAnchored: true);
		_hudLogPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.11f, 0.84f) });
		_hudLogPanel.MouseEntered += () => { _hudLogHovered = true; RefreshHudLogAppearance(); };
		_hudLogPanel.MouseExited += () => { _hudLogHovered = false; RefreshHudLogAppearance(); };
		ui.AddChild(_hudLogPanel);
		var box = new VBoxContainer();
		_hudLogPanel.AddChild(box);
		_hudLogExpand = new Button { TooltipText = "Apri o chiudi il registro", AnchorLeft = 1,
			AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -56,
			OffsetRight = -16, OffsetTop = -56, OffsetBottom = -16 };
		StyleActionButton(_hudLogExpand, "journal", UtilityAccent, iconOnly: true);
		_hudLogExpand.Pressed += () =>
		{
			_hudLogExpanded = !_hudLogExpanded;
			RefreshHudLogSize();
		};
		ui.AddChild(_hudLogExpand);
		_hudLog = new RichTextLabel { BbcodeEnabled = false, ScrollFollowing = true,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore };
		_hudLog.AddThemeFontSizeOverride("normal_font_size", 13);
		box.AddChild(_hudLog);
		RefreshHudLogAppearance();
	}

	private void RefreshHudLogSize()
	{
		_hudLogPanel.OffsetTop = _hudLogExpanded ? -360 : _view.Combat is null ? -116 : -150;
		_hudLogPanel.OffsetRight = _hudLogExpanded ? -16 : -66;
		_hudLogExpand.TooltipText = _hudLogExpanded ? "Riduci il registro" : "Apri il registro";
		RefreshHudLogAppearance();
	}

	private void RefreshHudLogAppearance()
	{
		_hudLogPanel.Modulate = new Color(1, 1, 1, _hudLogExpanded ? 1 : _hudLogHovered ? 0.94f : 0.42f);
		_hudLog.MouseFilter = _hudLogExpanded ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
	}

	private void ResetHudLog()
	{
		_hudLines.Clear();
		_hudFactIds.Clear();
		_hudCombatLines = 0;
		_hudShowingLastFight = false;
		_hudWasInCombat = false;
		_hudMessage = "";
		if (_message is not null) _message.Text = "";
	}

	private void RefreshHud()
	{
		var inCombat = _view.Combat is not null;
		if (inCombat && !_hudWasInCombat)
		{
			_classPanel.Visible = false;
			_inventoryPanel.Visible = false;
			_spellPanel.Visible = false;
		}
		_hudWasInCombat = inCombat;
		RefreshQuickbar();
		RefreshHudLogSize();
		if (_view.Combat is not null) _restPanel.Visible = false;
		_hudHitPoints.Text = $"PF {_view.HitPoints}/{_view.MaxHitPoints}";
		foreach (var fact in _view.RecentEvents)
		{
			if (!_hudFactIds.Add(fact.Id)) continue;
			// The combat register already carries these with the player's visible dice and damage.
			if ((_view.Combat is not null || _view.LastFight is not null)
				&& fact.Kind is "Attack" or "CombatStarted" or "CombatEnded" or "DeathSave"
					or "SavingThrow" or "ConditionStarted" or "ConditionEnded") continue;
			AddHudLine($"G{fact.At.Day + 1} {fact.At.Hour:00}:{fact.At.Minute:00} · {fact.Description}");
		}
		var lastFight = _view.Combat is null && _view.LastFight is not null;
		var combatLog = _view.Combat?.Log ?? _view.LastFight;
		if (combatLog is null)
			_hudCombatLines = 0;
		else
		{
			if (_hudShowingLastFight && !lastFight || combatLog.Count < _hudCombatLines)
				_hudCombatLines = 0;
			foreach (var entry in combatLog.Skip(_hudCombatLines))
				AddHudLine($"R{entry.Round} · {entry.Text}");
			_hudCombatLines = combatLog.Count;
		}
		_hudShowingLastFight = lastFight;
		if (_message.Text is { Length: > 0 } message && message != _hudMessage)
		{
			_hudMessage = message;
			if (_hudLines.Count == 0 || !_hudLines[^1].EndsWith(message, StringComparison.Ordinal))
				AddHudLine(message);
		}
		_hudLog.Text = string.Join("\n", _hudLines);
	}

	private void AddHudLine(string line)
	{
		_hudLines.Add(line);
		if (_hudLines.Count > 150) _hudLines.RemoveAt(0);
	}

	private bool CloseHudOverlay()
	{
		if (_gameMenu.Visible) { _gameMenu.Visible = false; return true; }
		if (_attackOptionsPanel.Visible) { _attackOptionsPanel.Visible = false; return true; }
		if (_openingConfirm.Visible) { _openingConfirm.Hide(); return true; }
		if (_openingPanel.Visible) { _openingPanel.Visible = false; return true; }
		if (_classPanel.Visible) { _classPanel.Visible = false; return true; }
		if (_spellPanel.Visible) { _spellPanel.Visible = false; return true; }
		if (_inventoryPanel.Visible) { _inventoryPanel.Visible = false; return true; }
		if (_journalPanel.Visible) { ToggleJournal(); return true; }
		if (_creaturePanel.Visible) { _creaturePanel.Visible = false; return true; }
		if (_barrierMenu.Visible) { _barrierMenu.Visible = false; return true; }
		if (_personMenu.Visible) { _personMenu.Visible = false; return true; }
		if (_restPanel.Visible) { _restPanel.Visible = false; return true; }
		return false;
	}

	internal string HudLogForSmoke => _hudLog.Text;
	internal float HudLogAlphaForSmoke => _hudLogPanel.Modulate.A;
	internal void HoverHudLogForSmoke(bool hover) { _hudLogHovered = hover; RefreshHudLogAppearance(); }
	internal void ExpandHudLogForSmoke() => _hudLogExpand.EmitSignal(BaseButton.SignalName.Pressed);
	internal bool InventoryOpenForSmoke => _inventoryPanel.Visible;
	internal bool ExploreActionBarVisibleForSmoke => _quickbarPanel.Visible && _view.Combat is null;
	internal bool CombatActionBarVisibleForSmoke => _quickbarPanel.Visible && _view.Combat is not null;
	internal void ToggleInventoryForSmoke() => _inventoryPanel.Visible = !_inventoryPanel.Visible;
}
