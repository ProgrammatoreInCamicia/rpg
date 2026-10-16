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
		_hudLogPanel = HudPanel(-330, -16, -154, -16, bottomAnchored: true, rightAnchored: true);
		_hudLogPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.11f, 0.91f) });
		ui.AddChild(_hudLogPanel);
		var box = new VBoxContainer();
		_hudLogPanel.AddChild(box);
		var header = new HBoxContainer();
		box.AddChild(header);
		header.AddChild(new Label { Text = "Registro", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		_hudLogExpand = new Button { Text = "Espandi" };
		_hudLogExpand.Pressed += () =>
		{
			_hudLogExpanded = !_hudLogExpanded;
			RefreshHudLogSize();
		};
		header.AddChild(_hudLogExpand);
		_hudLog = new RichTextLabel { BbcodeEnabled = false, ScrollFollowing = true,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Stop };
		_hudLog.AddThemeFontSizeOverride("normal_font_size", 13);
		box.AddChild(_hudLog);
	}

	private void RefreshHudLogSize()
	{
		_hudLogPanel.OffsetTop = _hudLogExpanded ? -360 : _view.Combat is null ? -154 : -236;
		_hudLogExpand.Text = _hudLogExpanded ? "Riduci" : "Espandi";
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
		_exploreActionPanel.Visible = !inCombat && !_view.Dead;
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

	private void CloseHudOverlay()
	{
		if (_openingConfirm.Visible) { _openingConfirm.Hide(); return; }
		if (_openingPanel.Visible) { _openingPanel.Visible = false; return; }
		if (_classPanel.Visible) { _classPanel.Visible = false; return; }
		if (_spellPanel.Visible) { _spellPanel.Visible = false; return; }
		if (_inventoryPanel.Visible) { _inventoryPanel.Visible = false; return; }
		if (_journalPanel.Visible) { ToggleJournal(); return; }
		if (_creaturePanel.Visible) { _creaturePanel.Visible = false; return; }
		if (_barrierMenu.Visible) { _barrierMenu.Visible = false; return; }
		if (_personMenu.Visible) { _personMenu.Visible = false; return; }
		_restPanel.Visible = false;
	}

	internal string HudLogForSmoke => _hudLog.Text;
	internal bool InventoryOpenForSmoke => _inventoryPanel.Visible;
	internal bool ExploreActionBarVisibleForSmoke => _exploreActionPanel.Visible;
	internal bool CombatActionBarVisibleForSmoke => _combatPanel.Visible;
	internal void ToggleInventoryForSmoke() => _inventoryPanel.Visible = !_inventoryPanel.Visible;
}
