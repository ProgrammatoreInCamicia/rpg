using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Entry presentation for the playable granary slice.</summary>
public partial class VillageMap
{
	private PanelContainer? _sliceIntro;
	private Button? _sliceBegin;
	private Button? _sliceWaitButton;
	private Label? _sliceWaitStatus;
	private PanelContainer? _sliceGuidePanel;
	private Label? _sliceGuideText;
	private PanelContainer? _sliceDepositPanel;
	private Label? _sliceDepositStatus;
	private Button? _sliceDepositButton;
	private Node2D? _sliceAccessMarker;
	private bool _sliceWaiting;
	private GameTime _sliceWaitUntil;
	private HashSet<ObservationId> _sliceKnownObservations = new();

	private void BuildSliceIntro(CanvasLayer ui)
	{
		_sliceIntro = new PanelContainer
		{
			AnchorLeft = 0.5f,
			AnchorRight = 0.5f,
			OffsetLeft = -230,
			OffsetRight = 230,
			OffsetTop = 20,
			OffsetBottom = 210,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_sliceIntro.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.09f, 0.11f, 0.96f),
			BorderColor = new Color(0.67f, 0.52f, 0.30f),
			BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
			ContentMarginLeft = 16, ContentMarginTop = 12, ContentMarginRight = 16, ContentMarginBottom = 12,
		});
		ui.AddChild(_sliceIntro);

		var content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 8);
		_sliceIntro.AddChild(content);
		var title = new Label { Text = "Una sera al granaio" };
		title.AddThemeFontSizeOverride("font_size", 20);
		content.AddChild(title);
		content.AddChild(new Label
		{
			Text = "Le scorte del villaggio attirano i ladri. Raggiungi il granaio e osserva. Se scopri qualcosa, puoi raccontarlo alla guardia presso la locanda.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		});
		content.AddChild(new Label
		{
			Text = "Clic: cammina   ·   Spazio: pausa/riprendi   ·   X: fermati",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		});
		_sliceBegin = new Button { Text = "Comincia", FocusMode = Control.FocusModeEnum.None,
			CustomMinimumSize = new Vector2(0, 36) };
		_sliceBegin.AddThemeStyleboxOverride("normal", SliceButtonStyle(new Color(0.38f, 0.27f, 0.14f)));
		_sliceBegin.AddThemeStyleboxOverride("hover", SliceButtonStyle(new Color(0.51f, 0.36f, 0.17f)));
		_sliceBegin.AddThemeStyleboxOverride("pressed", SliceButtonStyle(new Color(0.25f, 0.18f, 0.10f)));
		_sliceBegin.Pressed += () => SetPaused(false);
		content.AddChild(_sliceBegin);
		BuildSliceWaitUi(ui);
		BuildSliceGuidanceUi(ui);
	}

	private void HideSliceIntro() => _sliceIntro?.Hide();

	private static StyleBoxFlat SliceButtonStyle(Color fill) => new()
	{
		BgColor = fill, BorderColor = new Color(0.75f, 0.60f, 0.35f),
		BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
		CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
		CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
	};

	private void BuildSliceGuidanceUi(CanvasLayer ui)
	{
		_sliceGuidePanel = new PanelContainer { OffsetLeft = 16, OffsetRight = 350,
			OffsetTop = 124, OffsetBottom = 226 };
		_sliceGuidePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.09f, 0.11f, 0.90f),
			ContentMarginLeft = 10, ContentMarginTop = 7, ContentMarginRight = 10, ContentMarginBottom = 7,
		});
		ui.AddChild(_sliceGuidePanel);
		var guideBox = new VBoxContainer();
		guideBox.AddThemeConstantOverride("separation", 3);
		_sliceGuidePanel.AddChild(guideBox);
		var title = new Label { Text = "Indicazioni" };
		title.AddThemeFontSizeOverride("font_size", 14);
		guideBox.AddChild(title);
		_sliceGuideText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_sliceGuideText.AddThemeFontSizeOverride("font_size", 11);
		guideBox.AddChild(_sliceGuideText);

		_sliceDepositPanel = new PanelContainer
		{
			AnchorLeft = 1, AnchorRight = 1,
			OffsetLeft = -308, OffsetRight = -18,
			OffsetTop = 165, OffsetBottom = 275,
			Visible = false,
		};
		_sliceDepositPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.09f, 0.11f, 0.93f),
			BorderColor = new Color(0.50f, 0.39f, 0.23f), BorderWidthBottom = 1,
			ContentMarginLeft = 9, ContentMarginTop = 7, ContentMarginRight = 9, ContentMarginBottom = 7,
		});
		ui.AddChild(_sliceDepositPanel);
		var depositBox = new VBoxContainer();
		depositBox.AddThemeConstantOverride("separation", 5);
		_sliceDepositPanel.AddChild(depositBox);
		_sliceDepositStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_sliceDepositStatus.AddThemeFontSizeOverride("font_size", 12);
		depositBox.AddChild(_sliceDepositStatus);
		_sliceDepositButton = new Button { Text = "Consegna 3 razioni" };
		_sliceDepositButton.Pressed += DeliverSliceRations;
		depositBox.AddChild(_sliceDepositButton);

		_sliceAccessMarker = new Node2D { Name = "SliceStoreAccess", ZIndex = 20, Visible = false };
		_sliceAccessMarker.AddChild(new Polygon2D
		{
			Polygon = Diamond(0.62f), Color = new Color(0.19f, 0.79f, 0.90f, 0.72f),
		});
		var accessLabel = new Label { Text = "Consegna qui", Position = new Vector2(-54, -40),
			MouseFilter = Control.MouseFilterEnum.Ignore };
		accessLabel.AddThemeFontSizeOverride("font_size", 12);
		accessLabel.AddThemeConstantOverride("outline_size", 4);
		accessLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
		_sliceAccessMarker.AddChild(accessLabel);
		AddChild(_sliceAccessMarker);
	}

	private void RefreshSliceGuidance()
	{
		if (!PlayableSliceMode || _sliceGuidePanel is null || _sliceGuideText is null || _sliceDepositPanel is null
			|| _sliceDepositStatus is null || _sliceDepositButton is null || _sliceAccessMarker is null) return;
		_sliceGuidePanel.Visible = _talkingTo is null;
		var store = _view.VisibleStores.FirstOrDefault(s => s.Id == MappedVillageScenario.Ids.GranaryStore);
		var nearby = store?.Position is { } crate && _view.Position is { } player
			&& Math.Abs(player.X - crate.X) + Math.Abs(player.Y - crate.Y) <= 4;
		var guardVisible = _view.VisibleActors.Any(a => a.Id == MappedVillageScenario.Ids.Guard);
		_sliceGuideText.Text = (!nearby
			? "Granaio: segui la strada verso sud-est."
			: "Granaio in vista: cerca il rombo davanti alla cassa.")
			+ "\n" + (guardVisible ? "Guardia in vista: clicca la sua figura." : "Guardia: parlale quando la vedi alla locanda.")
			+ "\nDiario: icona libro nella barra.";
		_sliceDepositPanel.Visible = nearby && _view.Combat is null && !_view.Dead;
		if (store is null)
		{
			_sliceAccessMarker.Visible = false;
			return;
		}
		var access = store.Access.FirstOrDefault();
		var accessKnown = store.Access.Count > 0 && _view.MapVisibility is { } visibility
			&& access.Y >= 0 && access.Y < visibility.Count
			&& access.X >= 0 && access.X < visibility[access.Y].Length
			&& visibility[access.Y][access.X] != '0';
		_sliceAccessMarker.Visible = nearby && accessKnown && _view.Combat is null && !_view.Dead;
		if (accessKnown) _sliceAccessMarker.Position = CellToScreen(access);
		_sliceDepositStatus.Text = _view.Food < 3 ? "Ti servono 3 razioni per la consegna."
			: accessKnown ? "Clicca il rombo presso la cassa, poi consegna."
			: "Avvicinati alla cassa per trovare il lato di consegna.";
		_sliceDepositButton.Disabled = _view.Food < 3 || _paused || _running is not null || _view.Action is not null;
	}

	private void DeliverSliceRations()
	{
		if (_view.VisibleStores.All(s => s.Id != MappedVillageScenario.Ids.GranaryStore)) return;
		StartActivity(new DepositFoodCommand { Actor = _sim.Player,
			Store = MappedVillageScenario.Ids.GranaryStore, Amount = 3 }, "consegna 3 razioni",
			result => result.Rejection == RejectionReason.OutOfReach
				? "Per consegnare, clicca il rombo davanti alla cassa e avvicinati prima di riprovare."
				: result.Message);
	}

	private void BuildSliceWaitUi(CanvasLayer ui)
	{
		var panel = new PanelContainer
		{
			AnchorLeft = 1, AnchorRight = 1,
			OffsetLeft = -308, OffsetRight = -18,
			OffsetTop = 70, OffsetBottom = 156,
		};
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.09f, 0.11f, 0.91f),
			BorderColor = new Color(0.50f, 0.39f, 0.23f), BorderWidthBottom = 1,
			ContentMarginLeft = 9, ContentMarginTop = 7, ContentMarginRight = 9, ContentMarginBottom = 7,
		});
		ui.AddChild(panel);
		var content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 5);
		panel.AddChild(content);
		_sliceWaitButton = new Button { Text = "Attendi fino a 5 minuti" };
		_sliceWaitButton.Pressed += ToggleSliceWait;
		content.AddChild(_sliceWaitButton);
		_sliceWaitStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_sliceWaitStatus.AddThemeFontSizeOverride("font_size", 11);
		content.AddChild(_sliceWaitStatus);
	}

	private void ToggleSliceWait()
	{
		if (_sliceWaiting)
		{
			CancelSliceWait("Attesa interrotta.");
			return;
		}
		if (_paused || _view.Combat is not null || _view.Dead || _view.Unconscious
			|| _view.Action is not null || _view.Move is not null || _running is not null)
		{
			ShowMessage("Riprendi e termina l'attività corrente prima di attendere.");
			return;
		}
		_sliceKnownObservations = _view.Observations.Select(o => o.Id).ToHashSet();
		_sliceWaitUntil = _view.Now.Plus(Duration.FromMinutes(5));
		_sliceWaiting = true;
		_pendingTicks = 0;
		ShowMessage("Attendi fino a cinque minuti. Una nuova osservazione interrompe l'attesa.");
		RefreshSliceWaitUi();
	}

	private bool ProcessSliceWaitFrame()
	{
		if (!PlayableSliceMode || !_sliceWaiting) return false;
		if (_paused || _view.Combat is not null || _view.Dead || _view.Unconscious
			|| _view.Action is not null || _view.Move is not null || _running is not null)
		{
			CancelSliceWait("Attesa interrotta.");
			return true;
		}
		var remaining = _sliceWaitUntil.Since(_view.Now).Ticks;
		if (remaining <= 0)
		{
			CancelSliceWait("Sono passati cinque minuti.");
			return true;
		}
		var before = _view.Now;
		_sim.Advance(Duration.FromTicks(Math.Min(remaining, GameTime.TicksPerSecond)));
		Refresh();
		if (_view.Observations.Any(o => !_sliceKnownObservations.Contains(o.Id)))
			CancelSliceWait("Hai notato qualcosa. Consulta il diario.");
		else if (_view.Combat is not null || _view.Dead || _view.Unconscious || _view.Now == before)
			CancelSliceWait("Attesa interrotta.");
		else if (_view.Now >= _sliceWaitUntil)
			CancelSliceWait("Sono passati cinque minuti.");
		return true; // The ordinary clock must not also run in this frame.
	}

	private void CancelSliceWait(string message)
	{
		if (!_sliceWaiting) return;
		_sliceWaiting = false;
		_sliceKnownObservations.Clear();
		_pendingTicks = 0;
		ShowMessage(message);
		RefreshSliceWaitUi();
	}

	private void RefreshSliceWaitUi()
	{
		if (_sliceWaitButton is null || _sliceWaitStatus is null) return;
		_sliceWaitButton.Text = _sliceWaiting ? "Interrompi attesa" : "Attendi fino a 5 minuti";
		_sliceWaitButton.Disabled = !_sliceWaiting && (_paused || _view.Combat is not null || _view.Dead
			|| _view.Unconscious || _view.Action is not null || _view.Move is not null || _running is not null);
		var remaining = Math.Max(0, _sliceWaitUntil.Since(_view.Now).Seconds);
		_sliceWaitStatus.Text = _sliceWaiting ? $"Rimangono {remaining / 60}:{remaining % 60:00} · si ferma se noti qualcosa."
			: "Attesa accelerata e interrompibile.";
	}

	private async void RunSliceEntrySmoke(string outputDir)
	{
		try
		{
			System.IO.Directory.CreateDirectory(outputDir);
			var start = _view.Now;
			if (!_paused || _sliceIntro?.Visible != true || start.Hour != 19 || start.Minute != 27)
				throw new InvalidOperationException("La slice non apre l'introduzione in pausa alle 19:27.");
			for (var frame = 0; frame < 30; frame++)
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (_view.Now != start)
				throw new InvalidOperationException("Il tempo avanza mentre si leggono le istruzioni.");
			var capture = System.IO.Path.Combine(outputDir, "entry-paused.png");
			if (GetViewport().GetTexture().GetImage().SavePng(capture) != Error.Ok)
				throw new InvalidOperationException("Impossibile salvare la cattura iniziale.");
			_sliceBegin!.EmitSignal(BaseButton.SignalName.Pressed);
			for (var frame = 0; frame < 30; frame++)
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (_view.Now <= start || _sliceIntro?.Visible == true)
				throw new InvalidOperationException("La slice non riprende dopo Comincia.");
			GD.Print("SLICE ENTRY SMOKE OK");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"SLICE ENTRY SMOKE FAIL: {error}");
			GetTree().Quit(1);
		}
	}
}
