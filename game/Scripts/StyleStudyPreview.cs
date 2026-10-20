using Godot;

namespace RpgSandbox.Game;

/// <summary>A separate, non-playable viewer for judging art direction at the game's 1280×720 resolution.</summary>
public partial class StyleStudyPreview : Control
{
	private TextureRect _scene = null!;
	private Control _figures = null!;
	private int _mode = 1;

	private static readonly string[] Names = ["Paladino", "Guardia", "Contadino", "Razziatore"];
	private static readonly Rect2[] Crops =
	[
		new(70, 105, 405, 740),
		new(530, 0, 360, 840),
		new(970, 115, 320, 730),
		new(1350, 115, 390, 730),
	];

	public override async void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_scene = new TextureRect
		{
			Texture = GD.Load<Texture2D>("res://Assets/StyleStudy/interior-concept.png"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
		};
		AddChild(_scene);
		_scene.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		BuildFigureSheet();
		ShowMode(1);

		var smoke = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--style-study-smoke="));
		if (smoke is not null)
		{
			var directory = smoke["--style-study-smoke=".Length..];
			DirAccess.MakeDirRecursiveAbsolute(directory);
			foreach (var (mode, file) in new[]
			{
				(0, "current.png"), (1, "interior-proposal.png"), (2, "figures-at-scale.png"),
			})
			{
				ShowMode(mode);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				var image = GetViewport().GetTexture().GetImage();
				if (image.GetWidth() != 1280 || image.GetHeight() != 720)
					throw new InvalidOperationException($"Style preview viewport is {image.GetWidth()}×{image.GetHeight()}, expected 1280×720.");
				var path = System.IO.Path.Combine(directory, file);
				if (image.SavePng(path) != Error.Ok)
					throw new InvalidOperationException($"Could not save {path}.");
				GD.Print($"STYLE STUDY capture {path}");
			}
			GD.Print("STYLE STUDY SMOKE OK");
			GetTree().Quit(0);
		}
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
		if (key.Keycode == Key.Tab) ShowMode((_mode + 1) % 3);
		else if (key.Keycode == Key.Escape) GetTree().Quit();
	}

	private void ShowMode(int mode)
	{
		_mode = mode;
		_figures.Visible = mode == 2;
		_scene.Visible = mode != 2;
		if (mode != 2)
			_scene.Texture = GD.Load<Texture2D>(mode == 0
				? "res://Assets/StyleStudy/interior-current.png"
				: "res://Assets/StyleStudy/interior-concept.png");
	}

	private void BuildFigureSheet()
	{
		_figures = new Control();
		AddChild(_figures);
		_figures.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		var backdrop = new ColorRect { Color = new Color(0.15f, 0.16f, 0.17f) };
		_figures.AddChild(backdrop);
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddLabel(_figures, "PROVA SILHOUETTE · scala di gioco", new Vector2(42, 25), 25);
		AddLabel(_figures, "Riga superiore: circa 70 px, come nel mondo. Sotto: dettaglio ingrandito per esaminare il linguaggio visivo.",
			new Vector2(42, 62), 15);
		var source = GD.Load<Texture2D>("res://Assets/StyleStudy/character-lineup.png");
		for (var i = 0; i < Names.Length; i++)
		{
			var x = 35 + i * 310;
			var card = new ColorRect
			{
				Position = new Vector2(x, 104), Size = new Vector2(285, 565),
				Color = new Color(0.10f, 0.11f, 0.12f),
			};
			_figures.AddChild(card);
			var atlas = new AtlasTexture { Atlas = source, Region = Crops[i] };
			AddLabel(_figures, Names[i], new Vector2(x + 17, 119), 18);
			AddFigure(atlas, new Vector2(x + 142, 199), 70);
			var line = new ColorRect
			{
				Position = new Vector2(x + 17, 259), Size = new Vector2(251, 1),
				Color = new Color(0.52f, 0.40f, 0.24f),
			};
			_figures.AddChild(line);
			AddFigure(atlas, new Vector2(x + 142, 462), 330);
		}
		AddLabel(_figures, "TAB: scena attuale → proposta → figure     ESC: esci", new Vector2(42, 686), 13);
	}

	private void AddFigure(Texture2D texture, Vector2 center, float height) =>
		_figures.AddChild(new Sprite2D
		{
			Texture = texture,
			Position = center,
			Scale = Vector2.One * (height / texture.GetHeight()),
		});

	private static void AddLabel(Node parent, string value, Vector2 position, int size)
	{
		var label = new Label { Text = value, Position = position };
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", new Color(0.90f, 0.85f, 0.75f));
		parent.AddChild(label);
	}
}
