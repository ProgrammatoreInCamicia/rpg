using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	private Node2D _spotOverlay = null!;
	private Line2D _spotRoute = null!;
	private Polygon2D _spotMarker = null!;
	private Node2D _spotThreatMarkers = null!;
	private Label _spotHint = null!;
	private Spot? _spotPreviewAt;
	private Spot? _spotPreviewFrom;
	private long _spotPreviewTime = -1;
	private bool _spotPreviewSneak;
	private IReadOnlyList<Spot>? _combatVisualWay;
	private double _combatVisualStarted;
	private double _combatVisualDuration;

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
		_spotThreatMarkers = new Node2D();
		_spotOverlay.AddChild(_spotThreatMarkers);
		_spotHint = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
		_spotHint.AddThemeColorOverride("font_color", Colors.White);
		_spotHint.AddThemeColorOverride("font_shadow_color", Colors.Black);
		_spotHint.AddThemeConstantOverride("shadow_offset_x", 1);
		_spotHint.AddThemeConstantOverride("shadow_offset_y", 1);
		_spotOverlay.AddChild(_spotHint);
	}

	private void UpdateSpotPreview() => UpdateSpotPreview(GetGlobalMousePosition());

	private void UpdateSpotPreview(Vector2 mouse)
	{
		if (!_freeMovementPrototype || _view.Combat is { IsYourTurn: false } || _view.Dead || _view.Map is not { } map
			|| _view.Spot is null)
		{
			_spotOverlay.Visible = false;
			return;
		}
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
			var preview = _sim.PreviewMoveToSpot(_sim.Player, at, _view.Combat is null && _sneak);
			// Do not let the route or its cost disclose walls behind unexplored squares.
			var known = preview.Way.Count > 1 && RouteKnown(preview.Way);
			var affordable = preview.Valid;
			_spotRoute.Points = known ? preview.Way.Select(SpotToScreen).ToArray() : Array.Empty<Vector2>();
			_spotRoute.Visible = known;
			_spotRoute.DefaultColor = affordable ? new Color(0.32f, 0.78f, 1f, 0.84f)
				: new Color(1f, 0.48f, 0.35f, 0.82f);
			_spotMarker.Color = known && affordable ? new Color(0.32f, 0.78f, 1f, 0.9f)
				: new Color(1f, 0.45f, 0.35f, 0.85f);
			_spotHint.Text = known && _view.Combat is not null
				? $"{preview.Feet:0.0} / {Math.Floor(preview.FeetLeft * 10 + 1e-9) / 10:0.0} piedi"
					+ (affordable ? "" : " · troppo lontano")
					+ (preview.Provokes.Count == 0 ? "" : $" · reazione: {string.Join(", ", preview.Provokes.Select(p => p.Name))}")
				: known ? $"{preview.Feet:0.0} piedi · {preview.Seconds} s"
				: preview.Way.Count > 1 ? "Percorso ancora inesplorato"
				: preview.Message;
			foreach (var child in _spotThreatMarkers.GetChildren())
			{
				_spotThreatMarkers.RemoveChild(child);
				child.QueueFree();
			}
			if (known)
				foreach (var attack in preview.Provokes)
					_spotThreatMarkers.AddChild(new Polygon2D { Polygon = Diamond(0.3f), Position = SpotToScreen(attack.At),
						Color = new Color(1f, 0.18f, 0.12f, 0.95f) });
			if (_view.Combat is { } combat)
				_combatHint.Text = known ? _spotHint.Text : BaseCombatHint(combat);
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

	/// <summary>The fight has already resolved in the Sim; slide the figure over the part of its validated way it reached.</summary>
	private void StartCombatSpotVisual(SpotMovePreview preview, Spot reached)
	{
		_combatVisualWay = null;
		if (preview.Way.Count < 2 || preview.Way[0] == reached)
			return;
		var way = new List<Spot> { preview.Way[0] };
		if (reached == preview.Way[^1])
			way.AddRange(preview.Way.Skip(1));
		else
		{
			// Opportunity attacks and unseen bodies may stop the authoritative walk on a leg.
			// Find that leg so the visual never continues to the originally requested destination.
			var closest = double.PositiveInfinity;
			var leg = 1;
			for (var i = 1; i < preview.Way.Count; i++)
			{
				var a = preview.Way[i - 1];
				var b = preview.Way[i];
				var dx = b.X - a.X;
				var dy = b.Y - a.Y;
				var lengthSquared = (double)dx * dx + (double)dy * dy;
				var t = lengthSquared == 0 ? 0 : Math.Clamp(((reached.X - a.X) * (double)dx + (reached.Y - a.Y) * (double)dy) / lengthSquared, 0, 1);
				var distance = Math.Sqrt(Math.Pow(a.X + t * dx - reached.X, 2) + Math.Pow(a.Y + t * dy - reached.Y, 2));
				if (distance >= closest)
					continue;
				closest = distance;
				leg = i;
			}
			if (closest <= 2)
				way.AddRange(preview.Way.Skip(1).Take(leg - 1));
			way.Add(reached);
		}
		var length = 0.0;
		for (var i = 1; i < way.Count; i++)
			length += way[i - 1].DistanceTo(way[i]);
		_combatVisualWay = way;
		_combatVisualStarted = Time.GetTicksMsec() / 1000.0;
		_combatVisualDuration = Math.Clamp(length / 360.0, 0.18, 0.9);
	}

	private Vector2? CombatVisualPosition()
	{
		if (_combatVisualWay is not { } way)
			return null;
		var fraction = (Time.GetTicksMsec() / 1000.0 - _combatVisualStarted) / _combatVisualDuration;
		if (fraction >= 1)
		{
			_combatVisualWay = null;
			return null;
		}
		var length = 0.0;
		for (var i = 1; i < way.Count; i++)
			length += way[i - 1].DistanceTo(way[i]);
		var remaining = length * Math.Clamp(fraction, 0, 1);
		for (var i = 1; i < way.Count; i++)
		{
			var leg = way[i - 1].DistanceTo(way[i]);
			if (remaining <= leg || i == way.Count - 1)
				return SpotToScreen(way[i - 1]).Lerp(SpotToScreen(way[i]), leg == 0 ? 1 : (float)(remaining / leg));
			remaining -= leg;
		}
		return SpotToScreen(way[^1]);
	}

	internal static Vector2 ScreenPointOf(Spot spot) => SpotToScreen(spot);
	internal static Spot SpotAtScreenForSmoke(Vector2 point) => ScreenToSpot(point);
	internal (bool Visible, int RoutePoints, int ThreatMarkers, string Hint) PreviewSpotForSmoke(Spot spot)
	{
		UpdateSpotPreview(SpotToScreen(spot));
		return (_spotOverlay.Visible, _spotRoute.Points.Length, _spotThreatMarkers.GetChildCount(), _spotHint.Text);
	}
	internal void ClickSpotForSmoke(Spot spot) => _UnhandledInput(new InputEventMouseButton
	{
		ButtonIndex = MouseButton.Left, Pressed = true,
		Position = GetGlobalTransformWithCanvas() * SpotToScreen(spot),
	});
}
