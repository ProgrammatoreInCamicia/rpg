using Godot;

namespace RpgSandbox.Game;

/// <summary>
/// Isolated art experiment: one articulated 3D character rendered from eight angles.
/// Run the scene with -- --paladin-prototype=ABSOLUTE_DIRECTORY to export its frames.
/// No simulation state or village rendering depends on this prototype.
/// </summary>
public partial class Paladin3DPrototype : Node3D
{
	private readonly List<Node3D> _hips = new();
	private readonly List<Node3D> _knees = new();
	private readonly List<Node3D> _shoulders = new();
	private Node3D _figure = null!;
	private Node3D _body = null!;
	private Node3D _cape = null!;
	private SubViewport _viewport = null!;

	public override async void _Ready()
	{
		var destination = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--paladin-prototype="));
		if (destination is null)
		{
			GD.PrintErr("Pass --paladin-prototype=ABSOLUTE_DIRECTORY after -- to export frames.");
			GetTree().Quit(1);
			return;
		}
		try
		{
			BuildScene();
			await Export(destination["--paladin-prototype=".Length..]);
			GD.Print("PALADIN 3D PROTOTYPE OK");
			GetTree().Quit(0);
		}
		catch (Exception error)
		{
			GD.PrintErr($"PALADIN 3D PROTOTYPE FAIL: {error}");
			GetTree().Quit(1);
		}
	}

	private void BuildScene()
	{
		_viewport = new SubViewport
		{
			Size = new Vector2I(192, 192),
			TransparentBg = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			OwnWorld3D = true,
		};
		AddChild(_viewport);
		var stage = new Node3D();
		_viewport.AddChild(stage);
		var camera = new Camera3D
		{
			Projection = Camera3D.ProjectionType.Orthogonal,
			Size = 3.4f,
			Position = new Vector3(3.6f, 2.9f, 4.2f),
			Current = true,
		};
		stage.AddChild(camera);
		camera.LookAt(new Vector3(0, 1.05f, 0));
		stage.AddChild(new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = Colors.Transparent,
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color("b9c5d2"),
				AmbientLightEnergy = 0.8f,
			},
		});
		stage.AddChild(new DirectionalLight3D
		{
			RotationDegrees = new Vector3(-55, -35, 0),
			LightEnergy = 1.5f,
			ShadowEnabled = false,
		});

		_figure = new Node3D();
		stage.AddChild(_figure);
		_body = new Node3D();
		_figure.AddChild(_body);
		var mail = Paint("70818c", 0.48f);
		var steel = Paint("a8b6bd", 0.58f);
		var darkSteel = Paint("46525d", 0.5f);
		var blue = Paint("234679", 0.08f);
		var gold = Paint("c69c51", 0.48f);
		var leather = Paint("4d3328", 0.02f);
		var skin = Paint("b48265", 0.0f);
		var hair = Paint("735039", 0.0f);

		Part(_body, new CylinderMesh { TopRadius = 0.24f, BottomRadius = 0.31f, Height = 0.72f },
			mail, new Vector3(0, 1.28f, 0));
		Part(_body, new CylinderMesh { TopRadius = 0.26f, BottomRadius = 0.30f, Height = 0.25f },
			blue, new Vector3(0, 1.02f, 0));
		Part(_body, new BoxMesh { Size = new Vector3(0.52f, 0.09f, 0.40f) },
			leather, new Vector3(0, 1.11f, 0));
		Part(_body, new SphereMesh { Radius = 0.19f, Height = 0.38f },
			skin, new Vector3(0, 1.84f, 0));
		Part(_body, new SphereMesh { Radius = 0.205f, Height = 0.28f },
			hair, new Vector3(0, 1.98f, -0.025f));
		Part(_body, new SphereMesh { Radius = 0.23f, Height = 0.30f },
			steel, new Vector3(0, 2.03f, 0));
		Part(_body, new BoxMesh { Size = new Vector3(0.47f, 0.055f, 0.38f) },
			gold, new Vector3(0, 1.91f, 0));
		Part(_body, new BoxMesh { Size = new Vector3(0.075f, 0.20f, 0.10f) },
			steel, new Vector3(0, 1.82f, 0.20f));
		Part(_body, new SphereMesh { Radius = 0.075f, Height = 0.13f },
			gold, new Vector3(0, 1.45f, 0.275f));

		_cape = new Node3D { Position = new Vector3(0, 1.63f, -0.30f) };
		_body.AddChild(_cape);
		Part(_cape, new BoxMesh { Size = new Vector3(0.57f, 0.91f, 0.035f) },
			blue, new Vector3(0, -0.40f, -0.025f));
		Part(_cape, new BoxMesh { Size = new Vector3(0.57f, 0.05f, 0.05f) },
			gold, new Vector3(0, -0.85f, -0.025f));

		for (var side = -1; side <= 1; side += 2)
		{
			var hip = new Node3D { Position = new Vector3(side * 0.16f, 0.90f, 0) };
			_body.AddChild(hip);
			_hips.Add(hip);
			Part(hip, new CapsuleMesh { Radius = 0.12f, Height = 0.52f },
				mail, new Vector3(0, -0.25f, 0));
			var knee = new Node3D { Position = new Vector3(0, -0.49f, 0) };
			hip.AddChild(knee);
			_knees.Add(knee);
			Part(knee, new CapsuleMesh { Radius = 0.105f, Height = 0.48f },
				darkSteel, new Vector3(0, -0.22f, 0));
			Part(knee, new BoxMesh { Size = new Vector3(0.23f, 0.18f, 0.36f) },
				leather, new Vector3(0, -0.43f, 0.105f));

			var shoulder = new Node3D { Position = new Vector3(side * 0.32f, 1.60f, 0) };
			_body.AddChild(shoulder);
			_shoulders.Add(shoulder);
			Part(shoulder, new SphereMesh { Radius = 0.17f, Height = 0.28f },
				steel, new Vector3(0, -0.035f, 0));
			Part(shoulder, new CapsuleMesh { Radius = 0.10f, Height = 0.55f },
				mail, new Vector3(side * 0.07f, -0.32f, 0));
			Part(shoulder, new SphereMesh { Radius = 0.11f, Height = 0.20f },
				leather, new Vector3(side * 0.08f, -0.61f, 0));
			if (side < 0)
			{
				Part(shoulder, new CylinderMesh { TopRadius = 0.26f, BottomRadius = 0.22f, Height = 0.055f },
					blue, new Vector3(-0.09f, -0.61f, 0.19f), new Vector3(90, 0, 0));
				Part(shoulder, new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 0.063f },
					gold, new Vector3(-0.09f, -0.61f, 0.225f), new Vector3(90, 0, 0));
			}
			else
			{
				Part(shoulder, new BoxMesh { Size = new Vector3(0.045f, 0.72f, 0.075f) },
					steel, new Vector3(0.08f, -0.91f, 0.14f), new Vector3(16, 0, 11));
				Part(shoulder, new BoxMesh { Size = new Vector3(0.28f, 0.045f, 0.08f) },
					gold, new Vector3(0.08f, -0.63f, 0.14f));
			}
		}
	}

	private static StandardMaterial3D Paint(string color, float metallic) => new()
	{
		AlbedoColor = new Color(color),
		Metallic = metallic,
		Roughness = 0.72f,
		ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
	};

	private static void Part(Node3D parent, Mesh mesh, Material material, Vector3 position, Vector3? rotation = null)
	{
		var part = new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = material,
			Position = position,
			RotationDegrees = rotation ?? Vector3.Zero,
		};
		parent.AddChild(part);
	}

	private void Pose(int direction, float cycle)
	{
		_figure.Rotation = new Vector3(0, direction * Mathf.Pi / 4f, 0);
		var stride = Mathf.Sin(cycle * Mathf.Tau);
		_body.Position = new Vector3(0, 0.024f * (1f - Mathf.Cos(cycle * Mathf.Tau * 2f)), 0);
		_body.Rotation = new Vector3(0, 0.018f * stride, 0);
		_cape.Rotation = new Vector3(-0.07f * stride, 0, 0.04f * stride);
		for (var i = 0; i < 2; i++)
		{
			var signed = i == 0 ? stride : -stride;
			_hips[i].Rotation = new Vector3(signed * 0.37f, 0, 0);
			_knees[i].Rotation = new Vector3(Mathf.Max(0, -signed) * 0.42f, 0, 0);
			_shoulders[i].Rotation = new Vector3(-signed * 0.28f, 0, 0);
		}
	}

	private async Task Export(string folder)
	{
		if (DirAccess.MakeDirRecursiveAbsolute(folder) != Error.Ok)
			throw new IOException($"Cannot create {folder}");
		const int size = 192;
		const int phases = 8;
		var atlas = Image.CreateEmpty(size * phases, size * 8, false, Image.Format.Rgba8);
		for (var direction = 0; direction < 8; direction++)
		for (var phase = 0; phase < phases; phase++)
		{
			Pose(direction, phase / (float)phases);
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			var frame = _viewport.GetTexture().GetImage();
			if (frame.IsEmpty())
				throw new IOException("Godot did not render a frame");
			frame.Convert(Image.Format.Rgba8);
			atlas.BlitRect(frame, new Rect2I(0, 0, size, size), new Vector2I(phase * size, direction * size));
		}
		var atlasPath = System.IO.Path.Combine(folder, "paladin-3d-walk-8x8.png");
		if (atlas.SavePng(atlasPath) != Error.Ok)
			throw new IOException($"Cannot write {atlasPath}");
		GD.Print($"PALADIN 3D PROTOTYPE atlas {atlasPath}");
	}
}
