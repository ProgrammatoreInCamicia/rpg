using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	private sealed record ShotVisual(Line2D Trail, Polygon2D Tip, Vector2 From, Vector2 To,
		double Started, double Duration);
	private readonly List<ShotVisual> _shotVisuals = new();
	private Node2D _shotLayer = null!;

	private void BuildShotLayer()
	{
		_shotLayer = new Node2D { ZIndex = 20 };
		AddChild(_shotLayer);
	}

	/// <summary>The Sim supplies the resolved straight segment, including a miss's actual end point.</summary>
	private void StartShotVisual(Spot from, Spot to)
	{
		if (from == to)
			return;
		var start = SpotToScreen(from) + new Vector2(0, -21);
		var end = SpotToScreen(to) + new Vector2(0, -21);
		var trail = new Line2D { Width = 3f, Antialiased = true,
			DefaultColor = new Color(1f, 0.81f, 0.4f, 0.9f) };
		var tip = new Polygon2D { Polygon = new[] { new Vector2(9, 0), new Vector2(-4, -3), new Vector2(-4, 3) },
			Color = new Color(1f, 0.93f, 0.68f), Rotation = (end - start).Angle() };
		_shotLayer.AddChild(trail);
		_shotLayer.AddChild(tip);
		_shotVisuals.Add(new ShotVisual(trail, tip, start, end, Time.GetTicksMsec() / 1000.0,
			Math.Clamp(start.DistanceTo(end) / 760.0, 0.16, 0.65)));
	}

	private void UpdateShotVisuals()
	{
		var now = Time.GetTicksMsec() / 1000.0;
		for (var i = _shotVisuals.Count - 1; i >= 0; i--)
		{
			var shot = _shotVisuals[i];
			var fraction = (now - shot.Started) / shot.Duration;
			if (fraction >= 1)
			{
				shot.Trail.QueueFree();
				shot.Tip.QueueFree();
				_shotVisuals.RemoveAt(i);
				continue;
			}
			var head = shot.From.Lerp(shot.To, (float)Math.Clamp(fraction, 0, 1));
			var tail = shot.From.Lerp(shot.To, (float)Math.Clamp(fraction - 0.065, 0, 1));
			shot.Trail.Points = new[] { tail, head };
			shot.Tip.Position = head;
		}
	}

	private void ClearShotVisuals()
	{
		foreach (var shot in _shotVisuals)
		{
			shot.Trail.QueueFree();
			shot.Tip.QueueFree();
		}
		_shotVisuals.Clear();
	}

	internal bool ShotVisualActiveForSmoke => _shotVisuals.Count > 0;
}
