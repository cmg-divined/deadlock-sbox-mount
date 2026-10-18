using System;
using System.Runtime.InteropServices;
using Deadlock.Particles.Renderers;

namespace Deadlock;

/// <summary>
/// The lights Deadlock particle effects place, gathered every frame. While a map's Deadlock lighting lights the scene,
/// the Deadlock shaders ignore s&amp;box's lights, so they read these from a buffer of their own
/// (<c>deadlock_lighting.hlsl</c>); everything s&amp;box lights gets them as s&amp;box lights.
/// </summary>
public sealed class DeadlockParticleLights : GameObjectSystem<DeadlockParticleLights>
{
	/// <summary>
	/// The most particle lights lit at once; past it, the nearest to the camera.
	/// </summary>
	private const int MaxLights = 256;

	/// <summary>
	/// Buffers written in turn, so a frame never rewrites one a frame still being drawn reads.
	/// </summary>
	private const int BufferCount = 3;

	private readonly List<ParticleLight> _lights = [];
	private readonly List<DeadlockParticleLightGpu> _gpu = [];
	private readonly GpuBuffer<DeadlockParticleLightGpu>[] _buffers = new GpuBuffer<DeadlockParticleLightGpu>[BufferCount];
	private readonly List<ScenePointLight> _pointLights = [];
	private readonly List<SceneSpotLight> _spotLights = [];
	private int _nextBuffer;

	public DeadlockParticleLights( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 10, Update, "DeadlockParticleLights" );
	}

	private void Update()
	{
		_lights.Clear();

		foreach ( var effect in Scene.GetAllComponents<DeadlockParticleEffect>() )
			effect.CollectLights( _lights );

		if ( _lights.Count > MaxLights && Scene.Camera is { } camera )
		{
			var eye = (System.Numerics.Vector3)camera.WorldPosition;
			_lights.Sort( ( a, b ) => System.Numerics.Vector3.DistanceSquared( a.Position, eye ).CompareTo( System.Numerics.Vector3.DistanceSquared( b.Position, eye ) ) );
		}

		var count = Math.Min( _lights.Count, MaxLights );

		Upload( count );
		UpdateSceneLights( count );
	}

	/// <summary>
	/// The lights in map space, for the Deadlock shaders.
	/// </summary>
	private void Upload( int count )
	{
		if ( Scene.RenderAttributes is not { } attributes )
			return;

		// The map's lighting works in the map's space, which moves with its Deadlock Map Lighting.
		var map = Scene.GetAllComponents<DeadlockMapLighting>().FirstOrDefault( x => x.Active )?.WorldTransform ?? global::Transform.Zero;
		var inverseRotation = map.Rotation.Inverse;
		var scale = map.UniformScale != 0f ? 1f / map.UniformScale : 1f;

		_gpu.Clear();

		for ( var i = 0; i < count; i++ )
		{
			var light = _lights[i];
			var position = map.PointToLocal( (Vector3)light.Position );
			var direction = inverseRotation * (Vector3)light.Direction;
			var segment = inverseRotation * (Vector3)light.Segment * scale;

			_gpu.Add( new DeadlockParticleLightGpu
			{
				Position = new Vector4( position, light.Range * scale ),
				Color = new Vector4( light.Color.X, light.Color.Y, light.Color.Z, light.Specular ),
				Falloff = new Vector4( light.Falloff.X / (scale * scale), light.Falloff.Y / scale, light.Falloff.Z, light.Falloff.W ),
				Fade = new Vector4( light.DistanceFade.X, light.DistanceFade.Y / scale, light.ConeFade.X, light.ConeFade.Y ),
				Direction = new Vector4( direction, light.CapsuleDistance ? 1f : 0f ),
				Segment = new Vector4( segment, 0f ),
			} );
		}

		var index = _nextBuffer;
		_nextBuffer = (_nextBuffer + 1) % BufferCount;

		var buffer = _buffers[index];
		if ( buffer is null || buffer.ElementCount < Math.Max( count, 1 ) )
		{
			buffer?.Dispose();
			buffer = _buffers[index] = new GpuBuffer<DeadlockParticleLightGpu>( Math.Max( count, 16 ), debugName: "DeadlockParticleLights" );
		}

		if ( count > 0 )
			buffer.SetData( _gpu );

		attributes.Set( "DeadlockParticleLights", buffer );
		attributes.Set( "DeadlockParticleLightCount", count );
	}

	/// <summary>
	/// The lights as s&amp;box lights, which s&amp;box shades with inverse square falloff alone: a light that falls off
	/// linearly gets the inverse square falloff matching it at 100 units.
	/// </summary>
	private void UpdateSceneLights( int count )
	{
		var points = 0;
		var spots = 0;

		for ( var i = 0; i < count; i++ )
		{
			var light = _lights[i];
			SceneLight sceneLight;

			if ( light.ConeOuter > 0f )
			{
				if ( spots == _spotLights.Count )
					_spotLights.Add( new SceneSpotLight( Scene.SceneWorld, (Vector3)light.Position, global::Color.White ) );

				var spot = _spotLights[spots++];
				spot.ConeInner = light.ConeInner;
				spot.ConeOuter = light.ConeOuter;
				spot.Rotation = Rotation.LookAt( (Vector3)light.Direction );
				sceneLight = spot;
			}
			else
			{
				if ( points == _pointLights.Count )
					_pointLights.Add( new ScenePointLight( Scene.SceneWorld, (Vector3)light.Position, light.Range, global::Color.White ) );

				sceneLight = _pointLights[points++];
			}

			var quadratic = light.Falloff.X > 0f ? light.Falloff.X : light.Falloff.Y / 100f;

			sceneLight.Position = (Vector3)light.Position;
			sceneLight.LightColor = new Color( light.Color.X, light.Color.Y, light.Color.Z );
			sceneLight.Radius = light.Range;
			sceneLight.QuadraticAttenuation = quadratic * 10000f;
			sceneLight.RenderSpecular = light.Specular > 0f;
			sceneLight.ShadowsEnabled = false;
			sceneLight.FogLighting = SceneLight.FogLightingMode.None;
			sceneLight.RenderingEnabled = true;
		}

		for ( var i = points; i < _pointLights.Count; i++ )
			_pointLights[i].RenderingEnabled = false;

		for ( var i = spots; i < _spotLights.Count; i++ )
			_spotLights[i].RenderingEnabled = false;
	}

	public override void Dispose()
	{
		foreach ( var light in _pointLights )
			light.Delete();

		foreach ( var light in _spotLights )
			light.Delete();

		_pointLights.Clear();
		_spotLights.Clear();

		for ( var i = 0; i < _buffers.Length; i++ )
		{
			_buffers[i]?.Dispose();
			_buffers[i] = null;
		}

		base.Dispose();
	}
}

/// <summary>
/// A particle light as <c>deadlock_lighting.hlsl</c> reads it, in map space.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct DeadlockParticleLightGpu
{
	/// <summary>
	/// Where the light is, and its reach in w.
	/// </summary>
	public Vector4 Position;

	/// <summary>
	/// Linear colour, and how much specular it gives in w.
	/// </summary>
	public Vector4 Color;

	/// <summary>
	/// saturate( 1 / ( x d² + y d + z ) - w ), the distance at least one unit.
	/// </summary>
	public Vector4 Falloff;

	/// <summary>
	/// smoothstep( 0, 1, x + y d ) over distance, and saturate( z + w cos ) over the angle from the cone's axis.
	/// </summary>
	public Vector4 Fade;

	/// <summary>
	/// The cone's axis, and 1 in w where distances are to the luminaire's nearest point.
	/// </summary>
	public Vector4 Direction;

	/// <summary>
	/// Half the luminaire, along its axis.
	/// </summary>
	public Vector4 Segment;
}
