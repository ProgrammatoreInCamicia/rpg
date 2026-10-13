using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	private Node2D _spotOverlay = null!;
	private Line2D _spotRoute = null!;
	private Polygon2D _spotMarker = null!;
	private Label _spotHint = null!;
	private Spot? _spotPreviewAt;
	private Spot? _spotPreviewFrom;
	private long _spotPreviewTime = -1;
	private bool _spotPreviewSneak;

	private void BuildSpotOverlay()
	{
		_spotOverlay = new Node2D { ZIndex = -4, Visible = false };
		AddChild(_spotOverlay);
		_spotRoute = new Line2D { Width = 3f, Antialiased = true,
			DefaultColor = new Color(0.32f, 0.78f, 1f, 0.84f) };
		_spotOverlay.AddChild(_spotRoute);
		_spotMarker = new Polygon2D { Polygon = Diamond(0.22f),
			Color = new Color(0.32f, 0.78f, 1f, 0.9f) };
		_spotOverlay.AddChild(_spotMarker);
		_spotHint = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
		_spotHint.AddThemeColorOverride("font_color", Colors.White);
		_spotHint.AddThemeColorOverride("font_shadow_color", Colors.Black);
		_spotHint.AddThemeConstantOverride("shadow_offset_x", 1);
		_spotHint.AddThemeConstantOverride("shadow_offset_y", 1);
		_spotOverlay.AddChild(_spotHint);
	}

	private void UpdateSpotPreview()
	{
		if (!_freeMovementPrototype || _view.Combat is not null || _view.Dead || _view.Map is not { } map
			|| _view.Spot is null)
		{
			_spotOverlay.Visible = false;
			return;
		}
		var mouse = GetGlobalMousePosition();
		var at = ScreenToSpot(mouse);
		var cell = at.Cell;
		if (cell.X < 0 || cell.Y < 0 || cell.X >= map.Width || cell.Y >= map.Height
			|| map.Rows[cell.Y][cell.X] == '#' || PersonAt(mouse) is not null
			|| map.Exits.Any(e => e.At == cell) || DoorAt(mouse) is not null
			|| GroundItemAt(ScreenToCell(mouse)) is not null
			|| _view.MapVisibility is { } visibility && visibility[cell.Y][cell.X] == '0')
		{
			_spotOverlay.Visible = false;
			return;
		}
		if (_spotPreviewAt != at || _spotPreviewFrom != _view.Spot || _spotPreviewTime != _view.Now.Seconds
			|| _spotPreviewSneak != _sneak)
		{
			_spotPreviewAt = at;
			_spotPreviewFrom = _view.Spot;
			_spotPreviewTime = _view.Now.Seconds;
			_spotPreviewSneak = _sneak;
			var preview = _sim.PreviewMoveToSpot(_sim.Player, at, _sneak);
			// Do not let the route or its cost disclose walls behind unexplored squares.
			var known = preview.Valid && RouteKnown(preview.Way);
			_spotRoute.Points = known ? preview.Way.Select(SpotToScreen).ToArray() : Array.Empty<Vector2>();
			_spotRoute.Visible = known;
			_spotMarker.Color = known ? new Color(0.32f, 0.78f, 1f, 0.9f)
				: new Color(1f, 0.45f, 0.35f, 0.85f);
			_spotHint.Text = known ? $"{preview.Feet:0.0} piedi · {preview.Seconds} s"
				: preview.Valid ? "Percorso ancora inesplorato"
				: preview.Message;
		}
		_spotMarker.Position = SpotToScreen(at);
		_spotHint.Position = _spotMarker.Position + new Vector2(10, -20);
		_spotOverlay.Visible = true;
	}

	private bool RouteKnown(IReadOnlyList<Spot> way)
	{
		if (_view.MapVisibility is not { } visibility)
			return true;
		for (var i = 1; i < way.Count; i++)
		{
			var steps = (int)Math.Ceiling(way[i - 1].DistanceTo(way[i]));
			for (var n = 0; n <= steps; n++)
			{
				var fraction = steps == 0 ? 0 : n / (double)steps;
				var point = new Spot((int)Math.Round(way[i - 1].X + (way[i].X - way[i - 1].X) * fraction),
					(int)Math.Round(way[i - 1].Y + (way[i].Y - way[i - 1].Y) * fraction));
				var cell = point.Cell;
				if (cell.X < 0 || cell.Y < 0 || cell.Y >= visibility.Count || cell.X >= visibility[cell.Y].Length
					|| visibility[cell.Y][cell.X] == '0')
					return false;
			}
		}
		return true;
	}

	internal static Vector2 ScreenPointOf(Spot spot) => SpotToScreen(spot);
	internal static Spot SpotAtScreenForSmoke(Vector2 point) => ScreenToSpot(point);
	internal void ClickSpotForSmoke(Spot spot) => _UnhandledInput(new InputEventMouseButton
	{
		ButtonIndex = MouseButton.Left, Pressed = true,
		Position = GetGlobalTransformWithCanvas() * SpotToScreen(spot),
	});
}
