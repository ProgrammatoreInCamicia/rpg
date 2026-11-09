using Godot;
using RpgSandbox.Sim.Api;
using Island = RpgSandbox.Sim.Scenarios.BlackmarshWatchIsland.Ids;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	private FactId? _lastRegionDefenseFact;
	private PanelContainer? _regionDefenseBalloon;
	private ulong _regionDefenseBalloonUntil;

	private void RefreshRegionDefenseFeedback()
	{
		if (!BlackmarshRegionMode) return;
		var fact = _view.RecentEvents.LastOrDefault(e => e.Kind is
			"DefenseWarning" or "DefenseAttack" or "DefenseWithdrawal");
		if (fact is null || fact.Id == _lastRegionDefenseFact) return;
		_lastRegionDefenseFact = fact.Id;
		if (!_view.VisibleActors.Any(a => a.Id == Island.Raider)
			|| !_tokens.TryGetValue(Island.Raider, out var token) || !token.Visible)
			return;
		if (_regionDefenseBalloon is not null && GodotObject.IsInstanceValid(_regionDefenseBalloon))
			_regionDefenseBalloon.QueueFree();
		var balloon = new PanelContainer
		{
			Position = new Vector2(-107, -118), Size = new Vector2(214, 52),
			ZIndex = 15, MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		balloon.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.12f, 0.09f, 0.08f, 0.92f),
			BorderColor = new Color(0.85f, 0.53f, 0.32f),
			BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
			ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 5, ContentMarginBottom = 5,
		});
		var label = new Label
		{
			Text = fact.Kind switch
			{
				"DefenseWarning" => "Vattene dall'approdo!",
				"DefenseAttack" => "Hai avuto il tuo avvertimento!",
				_ => "Meglio tenersi a distanza.",
			},
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		label.AddThemeFontSizeOverride("font_size", 14);
		balloon.AddChild(label);
		token.AddChild(balloon);
		_regionDefenseBalloon = balloon;
		_regionDefenseBalloonUntil = Time.GetTicksMsec() + 4500;
	}

	private void UpdateRegionDefenseFeedback()
	{
		if (_regionDefenseBalloon is not null && GodotObject.IsInstanceValid(_regionDefenseBalloon)
			&& Time.GetTicksMsec() >= _regionDefenseBalloonUntil)
		{
			_regionDefenseBalloon.QueueFree();
			_regionDefenseBalloon = null;
		}
	}
}
