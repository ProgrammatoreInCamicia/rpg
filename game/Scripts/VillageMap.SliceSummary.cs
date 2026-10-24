using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>
/// Astra Task 6: a summary of the granary slice that can be opened at any time. It is rebuilt from the player's view
/// each time it is shown, never from a quest state: what the player knows, not what happened.
/// </summary>
public partial class VillageMap
{
	private PanelContainer? _sliceSummary;
	private Label? _sliceSummaryTitle;
	private Label? _sliceSummaryText;
	private Button? _sliceSummaryButton;
	private Button? _sliceSummaryContinue;
	private Button? _sliceSummaryEnd;
	private Button? _sliceSummaryQuit;
	private Button? _sliceSummaryRetry;
	private bool _sliceEnded;
	private bool _sliceSummaryWasPaused;

	private void BuildSliceSummaryUi(CanvasLayer ui, VBoxContainer waitPanel)
	{
		_sliceSummaryButton = new Button { Text = "Riepilogo", FocusMode = Control.FocusModeEnum.None };
		_sliceSummaryButton.Pressed += OpenSliceSummary;
		waitPanel.AddChild(_sliceSummaryButton);

		_sliceSummary = new PanelContainer
		{
			AnchorLeft = 0.5f, AnchorRight = 0.5f,
			OffsetLeft = -250, OffsetRight = 250,
			OffsetTop = 90, OffsetBottom = 200, // grows with the text
			MouseFilter = Control.MouseFilterEnum.Stop,
			Visible = false,
		};
		_sliceSummary.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.09f, 0.11f, 0.97f),
			BorderColor = new Color(0.67f, 0.52f, 0.30f),
			BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
			ContentMarginLeft = 16, ContentMarginTop = 12, ContentMarginRight = 16, ContentMarginBottom = 12,
		});
		ui.AddChild(_sliceSummary);
		var content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 8);
		_sliceSummary.AddChild(content);
		_sliceSummaryTitle = new Label();
		_sliceSummaryTitle.AddThemeFontSizeOverride("font_size", 20);
		content.AddChild(_sliceSummaryTitle);
		_sliceSummaryText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_sliceSummaryText.AddThemeFontSizeOverride("font_size", 13);
		content.AddChild(_sliceSummaryText);
		var buttons = new HBoxContainer();
		buttons.AddThemeConstantOverride("separation", 8);
		content.AddChild(buttons);
		_sliceSummaryContinue = SliceSummaryAction(buttons, CloseSliceSummary);
		_sliceSummaryEnd = SliceSummaryAction(buttons, EndSlice);
		_sliceSummaryEnd.Text = "Termina la prova";
		_sliceSummaryQuit = SliceSummaryAction(buttons, () => GetTree().Quit());
		_sliceSummaryQuit.Text = "Esci";
		_sliceSummaryRetry = SliceSummaryAction(buttons, RetryScene);
		_sliceSummaryRetry.Text = "Riprova";
	}

	private static Button SliceSummaryAction(HBoxContainer row, Action pressed)
	{
		var button = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		button.AddThemeStyleboxOverride("normal", SliceButtonStyle(new Color(0.38f, 0.27f, 0.14f)));
		button.AddThemeStyleboxOverride("hover", SliceButtonStyle(new Color(0.51f, 0.36f, 0.17f)));
		button.AddThemeStyleboxOverride("pressed", SliceButtonStyle(new Color(0.25f, 0.18f, 0.10f)));
		button.Pressed += pressed;
		row.AddChild(button);
		return button;
	}

	/// <summary>Opening the summary stops time while it is read; closing it gives back the pause state found.</summary>
	private void OpenSliceSummary()
	{
		if (_sliceSummary is null || _sliceSummary.Visible) return;
		_sliceSummaryWasPaused = _paused;
		if (!_paused) SetPaused(true);
		_sliceSummary.Visible = true;
		RefreshSliceSummary();
	}

	private void CloseSliceSummary()
	{
		if (_sliceSummary is null || !_sliceSummary.Visible) return;
		_sliceSummary.Visible = false;
		if (!_sliceSummaryWasPaused || _sliceEnded) SetPaused(false);
		_sliceEnded = false;
	}

	/// <summary>Ending is the player's explicit choice: no hidden trigger decides that the evening is over.</summary>
	private void EndSlice()
	{
		_sliceEnded = true;
		RefreshSliceSummary();
	}

	private void RefreshSliceSummary()
	{
		if (_sliceSummary is not { Visible: true } || _sliceSummaryTitle is null || _sliceSummaryText is null
			|| _sliceSummaryContinue is null || _sliceSummaryEnd is null || _sliceSummaryQuit is null
			|| _sliceSummaryRetry is null) return;
		_sliceSummaryTitle.Text = _sliceEnded ? "Prova conclusa"
			: BlackmarshWatchhouseMode ? "La rimessa della vedetta" : "Una sera al granaio";
		_sliceSummaryText.Text = string.Join("\n", BlackmarshWatchhouseMode ? BlackmarshSummaryLines() : SliceSummaryLines());
		_sliceSummaryContinue.Text = _sliceEnded ? "Continua a esplorare" : "Continua";
		_sliceSummaryEnd.Visible = !_sliceEnded;
		_sliceSummaryQuit.Visible = _sliceEnded;
		_sliceSummaryRetry.Visible = _sliceEnded;
	}

	/// <summary>What the player knows of the evening, from the filtered view only.</summary>
	private List<string> SliceSummaryLines()
	{
		var granary = MappedVillageScenario.Ids.GranaryStore;
		var guard = MappedVillageScenario.Ids.Guard;
		var lines = new List<string>();
		var thefts = _view.Observations.Where(o => o.Kind == "Theft" && o.Store == granary)
			.OrderBy(o => o.ObservedAt.Ticks).ToList();
		if (thefts.Count == 0)
			lines.Add("Notizie: non sai di furti al granaio.");
		foreach (var theft in thefts)
		{
			var how = theft.Source is not null ? "te l'hanno raccontato"
				: theft.Perceived == "Seen" ? "l'hai visto" : "l'hai sentito";
			var amount = theft.Amount is { } count ? $"{count} razioni" : "delle razioni";
			var who = theft.CulpritName is { } name ? $"Colpevole: {name}." : "Non sai chi sia stato.";
			lines.Add($"Furto al granaio alle {theft.ObservedAt.Hour:00}:{theft.ObservedAt.Minute:00}: "
				+ $"{amount} portate via; {how}. {who}");
		}
		if (thefts.Count > 0)
			lines.Add(thefts.Any(o => o.ToldTo.Contains(guard))
				? "Rapporto: hai riferito il furto alla guardia."
				: "Rapporto: non l'hai riferito alla guardia. Altri potrebbero saperlo comunque.");
		lines.Add($"Le tue razioni: {_view.Food}.");
		var store = _view.VisibleStores.FirstOrDefault(s => s.Id == granary);
		lines.Add(store is not null && _view.Position is { } here && store.Access.Contains(here)
			? $"Scorte del granaio: {store.Food} razioni."
			: "Scorte del granaio: le conti solo dalla casella di consegna.");
		var keeper = _view.VisibleActors.FirstOrDefault(a => a.GuardingStore == granary);
		lines.Add(keeper is not null
			? $"Vedi {keeper.Name} sorvegliare il granaio."
			: "Da qui non vedi nessuno sorvegliare il granaio.");
		return lines;
	}
}
