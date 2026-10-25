using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Blackmarsh guidance and conclusions, derived from the player's filtered view.</summary>
public partial class VillageMap
{
	private PanelContainer? _blackmarshGuidePanel;
	private Label? _blackmarshGuideText;
	private int? _blackmarshKnownShedFood;

	private void BuildBlackmarshGuidanceUi(CanvasLayer ui)
	{
		_blackmarshGuidePanel = new PanelContainer
		{
			OffsetLeft = 16, OffsetRight = 350, OffsetTop = 124, OffsetBottom = 209,
		};
		_blackmarshGuidePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.09f, 0.11f, 0.90f),
			ContentMarginLeft = 10, ContentMarginTop = 7, ContentMarginRight = 10, ContentMarginBottom = 7,
		});
		ui.AddChild(_blackmarshGuidePanel);
		var content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 3);
		_blackmarshGuidePanel.AddChild(content);
		var title = new Label { Text = "La rimessa" };
		title.AddThemeFontSizeOverride("font_size", 14);
		content.AddChild(title);
		_blackmarshGuideText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_blackmarshGuideText.AddThemeFontSizeOverride("font_size", 11);
		content.AddChild(_blackmarshGuideText);
	}

	private void RefreshBlackmarshGuidance()
	{
		if (!BlackmarshWatchhouseMode || _blackmarshGuidePanel is null || _blackmarshGuideText is null) return;
		_blackmarshGuidePanel.Visible = _talkingTo is null && _view.Combat is null && !_journalOpen;
		var key = _view.Inventory.Any(item => item.Item.Value == BlackmarshWatchhouse.Ids.ShedKey && item.Count > 0);
		var store = _view.VisibleStores.FirstOrDefault(s => s.Id == BlackmarshWatchhouse.Ids.ShedStore);
		var countedAtAccess = store is not null && _view.Position is { } here && store.Access.Contains(here);
		if (countedAtAccess) _blackmarshKnownShedFood = store!.Food;
		_blackmarshGuideText.Text = store is not null && countedAtAccess && _blackmarshKnownShedFood == 0
			? "La cassa è vuota. Torna sul sentiero o apri il riepilogo."
			: store is not null && _blackmarshKnownShedFood == 0
			? "All'ultimo controllo la cassa era vuota. Torna sul sentiero o apri il riepilogo."
			: store is not null
			? "Cassa in vista: cliccala, poi usa Avvicinati. All'arrivo puoi prendere o consegnare razioni."
			: key ? "Rimessa a est: hai la chiave; clicca la porta per entrare."
				: "Rimessa a est: controlla la chiave nell'inventario (I).";
		_blackmarshGuideText.Text += "\nDiario: icona libro · Attesa e riepilogo: a destra.";
	}

	private List<string> BlackmarshSummaryLines()
	{
		var lines = new List<string> { $"Le tue razioni: {_view.Food}." };
		var shed = BlackmarshWatchhouse.Ids.ShedStore;
		var store = _view.VisibleStores.FirstOrDefault(s => s.Id == shed);
		lines.Add(store is not null && _view.Position is { } here && store.Access.Contains(here)
			? $"Scorte nella rimessa: {store.Food} razioni."
			: "Scorte nella rimessa: le conti solo dalla casella d'accesso.");
		var thefts = _view.Observations.Where(o => o.Kind == "Theft" && o.Store == shed)
			.OrderBy(o => o.ObservedAt.Ticks).ToList();
		if (thefts.Count == 0)
			lines.Add("Notizie: non hai osservato sottrazioni dalla rimessa.");
		foreach (var theft in thefts)
		{
			var how = theft.Source is not null ? "te l'hanno raccontato"
				: theft.Perceived == "Seen" ? "l'hai visto" : "l'hai sentito";
			var amount = theft.Amount is { } count ? $"{count} razioni" : "delle razioni";
			var who = theft.CulpritName is { } name ? $" Colpevole: {name}." : "";
			lines.Add($"Sottrazione alle {theft.ObservedAt.Hour:00}:{theft.ObservedAt.Minute:00}: "
				+ $"{amount}; {how}.{who}");
		}
		return lines;
	}
}
