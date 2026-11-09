using Godot;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	private static bool _regionStartFreshOnReload;
	private Control _regionTitle = null!;
	private Label _regionTitleError = null!;
	private Button _regionContinue = null!;
	private Control _regionConfirm = null!;
	private Label _regionConfirmText = null!;
	private Func<bool>? _regionConfirmAction;
	private bool _regionWasPausedBeforeConfirm;
	private bool _regionTitleOpen;
	private bool _regionSessionStarted;

	private bool RegionSmokeWithoutTitle() => OS.GetCmdlineUserArgs().Any(arg =>
		arg.StartsWith("--region-", StringComparison.Ordinal) && arg.Contains("-smoke", StringComparison.Ordinal)
		&& !arg.StartsWith("--region-persistence-smoke=", StringComparison.Ordinal));

	private void BuildRegionMenu(CanvasLayer ui)
	{
		_regionTitle = RegionOverlay(ui);
		var titlePanel = RegionDialog(_regionTitle, 460, 320);
		var titleItems = RegionDialogItems(titlePanel);
		titleItems.AddChild(new Label { Text = _regionMapPilot
			? "BLACKMARSH · PROVA MAPPE 48×32" : "BLACKMARSH · L'ISOLA DELLA VEDETTA",
			HorizontalAlignment = HorizontalAlignment.Center });
		titleItems.AddChild(new Label { Text = _regionMapPilot
			? "Pianta esplorabile in sviluppo. Partita e salvataggio separati dalla regione principale."
			: "Una piccola regione che continua a vivere mentre la esplori.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center });
		MenuItem(titleItems, "Nuova partita", RequestNewRegionGame);
		_regionContinue = new Button { Text = "Continua" };
		_regionContinue.Pressed += ContinueRegionGame;
		titleItems.AddChild(_regionContinue);
		_regionTitleError = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center };
		titleItems.AddChild(_regionTitleError);
		MenuItem(titleItems, "Esci", () => GetTree().Quit());

		_regionConfirm = RegionOverlay(ui);
		var confirmPanel = RegionDialog(_regionConfirm, 460, 220);
		var confirmItems = RegionDialogItems(confirmPanel);
		_regionConfirmText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center };
		confirmItems.AddChild(_regionConfirmText);
		MenuItem(confirmItems, "Conferma", ConfirmRegionAction);
		MenuItem(confirmItems, "Annulla", CancelRegionConfirmation);
	}

	private static Control RegionOverlay(CanvasLayer ui)
	{
		var overlay = new Panel { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
		overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.025f, 0.035f, 0.05f, 0.92f),
		});
		ui.AddChild(overlay);
		return overlay;
	}

	private static PanelContainer RegionDialog(Control overlay, float width, float height)
	{
		var panel = new PanelContainer
		{
			AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
			OffsetLeft = -width / 2, OffsetRight = width / 2,
			OffsetTop = -height / 2, OffsetBottom = height / 2,
		};
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.07f, 0.08f, 0.11f),
			BorderColor = new Color(0.55f, 0.43f, 0.25f),
			BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
			ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 20, ContentMarginBottom = 20,
		});
		overlay.AddChild(panel);
		return panel;
	}

	private static VBoxContainer RegionDialogItems(PanelContainer panel)
	{
		var items = new VBoxContainer();
		items.AddThemeConstantOverride("separation", 14);
		panel.AddChild(items);
		return items;
	}

	private void ShowRegionTitle()
	{
		_regionTitleOpen = true;
		_regionTitle.Visible = true;
		_regionTitleError.Text = "";
		_regionContinue.Disabled = !File.Exists(SaveFilePath);
		_gameMenu.Hide();
		SetPaused(true);
	}

	private void HideRegionTitle()
	{
		_regionTitleOpen = false;
		_regionTitle.Hide();
		_regionSessionStarted = true;
		SetPaused(true);
	}

	private void ShowRegionTitleError(string message)
	{
		if (BlackmarshRegionMode && _regionTitleOpen)
			_regionTitleError.Text = message;
	}

	private void RequestNewRegionGame()
	{
		if (_regionSessionStarted)
		{
			RequestRegionConfirmation("Abbandonare la partita attuale e iniziarne una nuova? I progressi non salvati andranno persi.",
				() => { _regionStartFreshOnReload = true; GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene); return true; });
			return;
		}
		if (File.Exists(SaveFilePath))
		{
			RequestRegionConfirmation("Iniziare una nuova partita? Il salvataggio esistente resterà disponibile finché non salvi di nuovo.",
				() => { HideRegionTitle(); return true; });
			return;
		}
		HideRegionTitle();
	}

	private void ContinueRegionGame()
	{
		if (LoadGameFromPath(SaveFilePath))
			HideRegionTitle();
	}

	private void RequestRegionLoad() => RequestRegionConfirmation(
		"Caricare la partita salvata? I progressi non salvati andranno persi.",
		() => { var loaded = LoadGameFromPath(SaveFilePath); if (loaded) SetPaused(true); return loaded; });

	private void RequestRegionReturnToTitle() => RequestRegionConfirmation(
		"Tornare al titolo? I progressi non salvati andranno persi se inizi o carichi un'altra partita.",
		() => { ShowRegionTitle(); return true; });

	private void SaveRegionAndExit()
	{
		if (SaveGameToPath(SaveFilePath))
			GetTree().Quit();
		else
			_gameMenu.Hide();
	}

	private void RequestRegionConfirmation(string message, Func<bool> action)
	{
		_regionWasPausedBeforeConfirm = _paused;
		_regionConfirmAction = action;
		_regionConfirmText.Text = message;
		_regionConfirm.Show();
		_gameMenu.Hide();
		SetPaused(true);
	}

	private void ConfirmRegionAction()
	{
		var action = _regionConfirmAction;
		_regionConfirmAction = null;
		_regionConfirm.Hide();
		if (action?.Invoke() != true)
			SetPaused(_regionWasPausedBeforeConfirm);
	}

	private void CancelRegionConfirmation()
	{
		_regionConfirmAction = null;
		_regionConfirm.Hide();
		SetPaused(_regionWasPausedBeforeConfirm);
	}
}
