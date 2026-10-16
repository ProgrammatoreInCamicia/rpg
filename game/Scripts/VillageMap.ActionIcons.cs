using Godot;

namespace RpgSandbox.Game;

/// <summary>Small, code-native vector icons and a consistent visual language for the action bars.</summary>
public partial class VillageMap
{
	private static readonly Dictionary<string, Texture2D> ActionIcons = new();
	private static readonly Color MovementAccent = new("4baea2");
	private static readonly Color DefenseAccent = new("779dc7");
	private static readonly Color AttackAccent = new("cf7760");
	private static readonly Color MagicAccent = new("aa85d4");
	private static readonly Color UtilityAccent = new("bd9a61");

	private static Texture2D ActionIcon(string kind)
	{
		if (ActionIcons.TryGetValue(kind, out var cached)) return cached;
		var drawing = kind switch
		{
			"sword" => "<path d='M7 25 25 7l-1-3-3 1L5 23l2 2Zm-5-6 11 11M3 29l5-5' />",
			"dagger" => "<path d='M12 21 23 5l4-1-1 4-13 14M8 18l7 7M6 26l5-5M4 29l4-4' />",
			"spear" => "<path d='M4 28 23 9M21 6l7-3-3 7-4-4ZM18 13l-4-4' />",
			"mace" => "<path d='M7 27 21 13M16 8l8-3 4 4-3 8-8 1-5-5 4-5ZM5 25l3 3' />",
			"bow" => "<path d='M8 3c15 7 15 19 0 26M8 3v26M4 28 27 5m-7 0h7v7' />",
			"dash" => "<path d='M4 23h8l4-5 4 2 7-8M20 20l-2 7h-7M5 11h9M2 16h8' />",
			"escape" => "<path d='M6 7h14v18H6zM13 16h15m-5-5 5 5-5 5' />",
			"shield" => "<path d='M16 3 28 8v8c0 8-5 12-12 15C9 28 4 24 4 16V8L16 3Z' /><path d='m10 17 4 4 8-9' />",
			"more" => "<circle cx='6' cy='16' r='2'/><circle cx='16' cy='16' r='2'/><circle cx='26' cy='16' r='2' />",
			"heal" => "<path d='M13 4h6v9h9v6h-9v9h-6v-9H4v-6h9z' />",
			"magic" => "<path d='m6 27 17-17M22 4l1 5 5 1-5 1-1 5-1-5-5-1 5-1zM7 5l1 3 3 1-3 1-1 3-1-3-3-1 3-1zM25 22l1 3 3 1-3 1-1 3-1-3-3-1 3-1z' />",
			"eye" => "<path d='M2 16c5-7 9-10 14-10s9 3 14 10c-5 7-9 10-14 10S7 23 2 16Z' /><circle cx='16' cy='16' r='5' />",
			"turn" => "<path d='M4 8h15a7 7 0 0 1 0 14H7m5-5-5 5 5 5' />",
			"bless" => "<circle cx='16' cy='16' r='5' /><path d='M16 3v5M16 24v5M3 16h5M24 16h5M7 7l3 3M22 22l3 3M7 25l3-3M22 10l3-3' />",
			"favor" => "<path d='M6 26 20 12M17 9l6-6 2 2-6 6M4 28l3-3' /><path d='m24 17 1 3 3 1-3 1-1 3-1-3-3-1 3-1z' />",
			"smite" => "<path d='M18 3 8 18h7l-2 11 11-16h-7z' />",
			"aid" => "<path d='M16 27S4 19 4 11a6 6 0 0 1 12-2 6 6 0 0 1 12 2c0 8-12 16-12 16Z' /><path d='M16 12v8M12 16h8' />",
			"restore" => "<path d='M16 4c5 7 9 11 9 16a9 9 0 0 1-18 0c0-5 4-9 9-16Z' /><path d='M12 20a4 4 0 0 0 4 4' />",
			"ward" => "<circle cx='16' cy='16' r='12' /><path d='m16 8 6 3v5c0 4-3 7-6 8-3-1-6-4-6-8v-5z' />",
			"spellbook" => "<path d='M6 4h16a3 3 0 0 1 3 3v21H9a3 3 0 0 1-3-3Z' /><path d='M6 25a3 3 0 0 1 3-3h16M15 9l1 3 3 1-3 1-1 3-1-3-3-1 3-1z' />",
			"search" => "<circle cx='13' cy='13' r='8' /><path d='m19 19 9 9M10 11a4 4 0 0 1 4-3' />",
			"options" => "<path d='M5 9h22M5 16h22M5 23h22' /><circle cx='11' cy='9' r='2.6' /><circle cx='21' cy='16' r='2.6' /><circle cx='13' cy='23' r='2.6' />",
			"pause" => "<path d='M9 6v20M23 6v20' />",
			"stop" => "<path d='M7 7h18v18H7z' />",
			"sneak" => "<path d='M5 25c4-2 7-3 11-3s7 1 11 3M12 20l2-8 6-3M18 9l5 5M13 7a3 3 0 1 0 6 0 3 3 0 0 0-6 0Z' />",
			"torch" => "<path d='M16 15v14m-5 0h10M16 16c-7-4-4-9 0-13 0 4 7 7 0 13Z' />",
			"poison" => "<path d='M12 4h8M13 4v7l-6 8a6 6 0 0 0 5 9h8a6 6 0 0 0 5-9l-6-8V4M10 20h12M13 24h6' />",
			"rest" => "<path d='M5 22h22M7 22v5m18-5v5M7 19h18v3H7zM11 15h5v4h-5z' />",
			"bag" => "<path d='M8 11h16l3 18H5l3-18ZM12 11V7a4 4 0 0 1 8 0v4' />",
			"class" => "<path d='M16 3 27 8v8c0 7-5 12-11 14C10 28 5 23 5 16V8L16 3Z' /><path d='M16 9v14M10 16h12' />",
			"save" => "<path d='M5 4h19l4 4v20H5zM9 4v9h14V4M9 28V18h14v10M18 6v5' />",
			"load" => "<path d='M4 11V6h9l3 3h12v18H4V11Zm12 2v10m-4-4 4 4 4-4' />",
			"journal" => "<path d='M5 5h10c3 0 4 2 4 4v19c0-2-1-4-4-4H5zM19 9c0-2 1-4 4-4h5v19h-5c-3 0-4 2-4 4M8 11h7M8 16h7M22 11h4' />",
			_ => "<circle cx='16' cy='16' r='10' />",
		};
		var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 32 32'><g fill='none' stroke='#f5f3eb' stroke-width='2.4' stroke-linecap='round' stroke-linejoin='round'>{drawing}</g></svg>";
		var image = new Image();
		if (image.LoadSvgFromString(svg) != Error.Ok) throw new InvalidOperationException($"Cannot draw action icon {kind}");
		return ActionIcons[kind] = ImageTexture.CreateFromImage(image);
	}

	private static StyleBoxFlat ActionStyle(Color accent, Color background) => new()
	{
		BgColor = background,
		BorderColor = accent,
		BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
		CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
		CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
		ContentMarginLeft = 6, ContentMarginRight = 6,
		ContentMarginTop = 4, ContentMarginBottom = 4,
	};

	private static void StyleActionButton(Button button, string icon, Color accent, bool iconOnly = false)
	{
		button.Icon = ActionIcon(icon);
		button.IconAlignment = iconOnly ? HorizontalAlignment.Center : HorizontalAlignment.Left;
		button.AddThemeStyleboxOverride("normal", ActionStyle(accent.Darkened(0.42f), new Color(0.13f, 0.15f, 0.18f, 0.96f)));
		button.AddThemeStyleboxOverride("hover", ActionStyle(accent, new Color(0.21f, 0.24f, 0.29f, 0.98f)));
		button.AddThemeStyleboxOverride("pressed", ActionStyle(accent.Lightened(0.2f), accent.Darkened(0.66f)));
		button.AddThemeStyleboxOverride("disabled", ActionStyle(accent.Darkened(0.52f), new Color(0.15f, 0.16f, 0.18f, 0.92f)));
	}

	private static VBoxContainer AddActionTile(HBoxContainer row, Button button, string title, string cost,
		string icon, Color accent)
	{
		var tile = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		tile.AddThemeConstantOverride("separation", 0);
		button.Text = "";
		button.CustomMinimumSize = new Vector2(54, 38);
		button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		StyleActionButton(button, icon, accent, iconOnly: true);
		tile.AddChild(button);
		var caption = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
		caption.AddThemeFontSizeOverride("font_size", 11);
		tile.AddChild(caption);
		var costLabel = new Label { Text = cost, HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(0.69f, 0.74f, 0.80f) };
		costLabel.AddThemeFontSizeOverride("font_size", 10);
		tile.AddChild(costLabel);
		row.AddChild(tile);
		return tile;
	}

	private static void SetActionTileEnabled(Button button, bool enabled)
	{
		if (button.GetParent() is CanvasItem tile)
			tile.Modulate = enabled ? Colors.White : new Color(0.75f, 0.76f, 0.79f);
	}
}
