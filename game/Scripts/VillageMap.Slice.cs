using Godot;

namespace RpgSandbox.Game;

/// <summary>Entry presentation for the playable granary slice.</summary>
public partial class VillageMap
{
	private PanelContainer? _sliceIntro;
	private Button? _sliceBegin;

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
		_sliceBegin = new Button { Text = "Comincia", FocusMode = Control.FocusModeEnum.None };
		_sliceBegin.Pressed += () => SetPaused(false);
		content.AddChild(_sliceBegin);
	}

	private void HideSliceIntro() => _sliceIntro?.Hide();

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
