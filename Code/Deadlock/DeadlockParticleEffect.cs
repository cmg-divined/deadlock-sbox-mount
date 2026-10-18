using System;
using Deadlock.Particles;
using Deadlock.Particles.Renderers;

namespace Deadlock;

/// <summary>
/// Plays a Deadlock particle system: simulates it the way the game does (a port of ValveResourceFormat's particle
/// simulation) and draws its sprites, trails and ropes with <c>deadlock_particle.shader</c>, lit by the map's
/// Deadlock lighting.
/// </summary>
/// <remarks>
/// Control point 0 is this GameObject unless <see cref="ControlPoints"/> names another. Control points follow the
/// GameObjects they're bound to, as an info_particle_system's cpoint keys make them.
/// </remarks>
[Title( "Deadlock Particle Effect" ), Category( "Deadlock" ), Icon( "auto_awesome" )]
public sealed class DeadlockParticleEffect : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// The longest step one frame simulates, so a hitch doesn't throw particles across the map.
	/// </summary>
	private const float MaxFrameTime = 0.1f;

	/// <summary>
	/// The particle system to play, e.g. <c>mount://deadlock/particles/foo.vpcf</c>.
	/// </summary>
	[Property]
	public DeadlockParticleSystem Particle
	{
		get => particle;
		set
		{
			if ( particle == value )
				return;

			particle = value;
			Restart();
		}
	}

	private DeadlockParticleSystem particle;

	/// <summary>
	/// Whether the effect plays as soon as it's enabled.
	/// </summary>
	[Property] public bool StartActive { get; set; } = true;

	/// <summary>
	/// Starts the effect over once everything it emitted has died.
	/// </summary>
	[Property] public bool Loop { get; set; }

	/// <summary>
	/// The GameObjects control points follow, by control point: element 0 is control point 0, which places the whole
	/// effect, and defaults to this GameObject. Unset ones stay where the system puts them.
	/// </summary>
	[Property] public List<GameObject> ControlPoints { get; set; } = [];

	/// <summary>
	/// Fixed control point values, set when the effect starts after the bound ones: an info_particle_system's data
	/// vector, tint colour (0 to 255) and literal positions.
	/// </summary>
	[Property] public List<DeadlockControlPointValue> ControlPointValues { get; set; } = [];

	/// <summary>
	/// Whether the effect plays in the editor's scene view too, not only in play mode.
	/// </summary>
	[Property] public bool PlayInEditor { get; set; } = true;

	private ParticleSystemSimulation simulation;
	private ParticleSystemRenderer renderer;
	private SceneCustomObject sceneObject;
	private bool playing;
	private bool started;
	private System.Numerics.Vector3 viewPosition;
	private bool hasView;

	// Set when a view draws the effect (on the render thread); like the game, an effect left undrawn for its
	// system's sleep time stops simulating until it's drawn again.
	private bool drawn;
	private float undrawnTime;

	/// <summary>
	/// Whether the effect is playing and still has something to show or emit.
	/// </summary>
	public bool IsPlaying => playing && simulation is not null && !simulation.IsFinished();

	protected override void OnEnabled()
	{
		playing = StartActive;
		started = false;

		sceneObject = new SceneCustomObject( Scene.SceneWorld )
		{
			RenderOverride = Render,
		};

		sceneObject.Flags.CastShadows = false;
		sceneObject.Flags.IsOpaque = true;
		sceneObject.Flags.IsTranslucent = true;
	}

	protected override void OnDisabled()
	{
		sceneObject?.Delete();
		sceneObject = null;

		Release();
	}

	/// <summary>
	/// Starts the effect from the beginning.
	/// </summary>
	public void Play()
	{
		playing = true;
		Restart();
	}

	/// <summary>
	/// Stops emitting; what's alive plays out, and the system's endcap runs.
	/// </summary>
	public void StopEmission()
	{
		if ( simulation is null )
			return;

		simulation.Stop();
		simulation.PlayEndCap();
	}

	/// <summary>
	/// Stops the effect and removes its particles.
	/// </summary>
	public void Stop()
	{
		playing = false;
		Release();
	}

	private void Restart()
	{
		Release();
		started = false;
	}

	private void Release()
	{
		renderer?.Dispose();
		renderer = null;
		simulation = null;
	}

	private bool ShouldRun => playing && (Game.IsPlaying || PlayInEditor);

	protected override void OnUpdate()
	{
		// Things drawing can't do itself, as it happens on the render thread.
		ParticleQuadBuffer.ReleaseOutgrown();
		ParticleFunctionRenderer.PrepareMaterial();

		if ( !ShouldRun )
		{
			if ( simulation is not null )
				Release();

			return;
		}

		// Built on the first update rather than on enable, so a scene that only loads and saves this (like the
		// mount building a map) doesn't load the system.
		if ( simulation is null && !started )
			Create();

		if ( simulation is null )
			return;

		PlaceControlPoints();

		var cameraPosition = hasView ? viewPosition : (System.Numerics.Vector3)(Scene.Camera?.WorldPosition ?? WorldPosition);
		var frameTime = MathF.Min( Time.Delta, MaxFrameTime );

		undrawnTime = drawn ? 0f : undrawnTime + Time.Delta;
		drawn = false;
		var asleep = undrawnTime > simulation.NoDrawTimeToGoToSleep;

		if ( frameTime > 0f && !asleep )
		{
			simulation.SetCameraPosition( cameraPosition );
			simulation.Update( frameTime, Time.Now );
			renderer.Act( cameraPosition, true );
		}

		UpdateBounds();

		if ( simulation.IsFinished() )
		{
			if ( Loop )
				simulation.Replay();
			else
				playing = false;
		}
	}

	private void Create()
	{
		started = true;

		var resource = Particle;
		if ( resource?.Definition is null )
			return;

		try
		{
			var files = new ParticleResourceFiles();
			simulation = new ParticleSystemSimulation( resource.Definition, files, ParticleLog.Instance );
			simulation.RenderState.Collision = new ParticleSceneCollision( Scene );

			// The first update starts it, with the control points placed.
			renderer = new ParticleSystemRenderer( simulation, resource, files, ParticleLog.Instance );

			PlaceControlPoints();

			foreach ( var value in ControlPointValues )
			{
				if ( value.ControlPoint is >= 0 and < 64 )
					simulation.GetControlPoint( value.ControlPoint ).Position = value.Value;
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"{GameObject.Name}: particle system '{resource.ResourcePath}' failed to start: {e.Message}" );
			Release();
		}
	}

	private void PlaceControlPoints()
	{
		var origin = ControlPoints.Count > 0 && ControlPoints[0] is { IsValid: true } placed ? placed : GameObject;
		SetControlPoint( 0, origin.WorldTransform );

		for ( var i = 1; i < ControlPoints.Count && i < 64; i++ )
		{
			if ( ControlPoints[i] is { IsValid: true } target )
				SetControlPoint( i, target.WorldTransform );
		}
	}

	private void SetControlPoint( int index, Transform transform )
	{
		var controlPoint = simulation.GetControlPoint( index );
		controlPoint.Position = transform.Position;

		var rotation = transform.Rotation;
		controlPoint.Orientation = rotation.Forward;
		controlPoint.Rotation = System.Numerics.Quaternion.Normalize( new System.Numerics.Quaternion( rotation.x, rotation.y, rotation.z, rotation.w ) );
	}

	private void UpdateBounds()
	{
		if ( sceneObject is null )
			return;

		var local = simulation.LocalBoundingBox;
		var origin = simulation.MainControlPoint.Position;

		// Systems without bounds of their own draw from anywhere.
		if ( local.Min.X <= -1e30f || local.Max.X >= 1e30f )
		{
			sceneObject.Bounds = BBox.FromPositionAndSize( WorldPosition, 1e7f );
			return;
		}

		sceneObject.Bounds = new BBox( local.Min + origin, local.Max + origin );
	}

	private void Render( SceneObject _ )
	{
		var pass = Graphics.LayerType switch
		{
			SceneLayerType.Translucent => ParticleRenderPass.Translucent,
			SceneLayerType.Opaque => ParticleRenderPass.Opaque,
			_ => (ParticleRenderPass?)null,
		};

		if ( pass is null || renderer is null )
			return;

		var view = ParticleView.FromCamera( Graphics.CameraTransform, Graphics.FieldOfView );
		viewPosition = view.Location;
		hasView = true;

		if ( renderer.Simulation.IsWithinDrawDistance( view.Location ) )
			drawn = true;

		renderer.Draw( view, pass.Value );
	}
}

/// <summary>
/// A control point set to a fixed value when an effect starts.
/// </summary>
public struct DeadlockControlPointValue
{
	[KeyProperty] public int ControlPoint { get; set; }
	public Vector3 Value { get; set; }
}
