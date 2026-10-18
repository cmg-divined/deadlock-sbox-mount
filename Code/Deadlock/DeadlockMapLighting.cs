using System;
using System.Runtime.InteropServices;

namespace Deadlock;

/// <summary>
/// A mounted Deadlock map's lighting, as its shaders light it: the baked lightmaps, the cubemap and light probe
/// volumes, the sun, and the stationary and dynamic lights. The mount builds it from the map's compiled lighting;
/// on enable it is handed to every Deadlock shader in the scene as scene wide render attributes, which those
/// shaders light with the way the game's deferred lighting pass does.
/// </summary>
/// <remarks>
/// Everything is in the map's own space. This GameObject's transform places the map, so the map can be moved:
/// the shaders bring what they light into the map's space first.
/// </remarks>
[Title( "Deadlock Map Lighting" ), Category( "Deadlock" ), Icon( "wb_sunny" )]
public sealed class DeadlockMapLighting : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// Every texture the lighting reads, by mounted path. The other properties refer to them by index in this list.
	/// </summary>
	[Property, Group( "Textures" )]
	public List<string> Textures { get; set; } = [];

	/// <summary>
	/// The constant band of the lightmaps' second order spherical harmonics (directional_irradiance_sh2_dc).
	/// </summary>
	[Property, Group( "Lightmaps" )] public int LightmapIrradiance { get; set; } = -1;
	[Property, Group( "Lightmaps" )] public int LightmapIrradianceRed { get; set; } = -1;
	[Property, Group( "Lightmaps" )] public int LightmapIrradianceGreen { get; set; } = -1;
	[Property, Group( "Lightmaps" )] public int LightmapIrradianceBlue { get; set; } = -1;

	/// <summary>
	/// How much of each stationary light's direct light each lightmap texel doesn't see, one light per channel.
	/// </summary>
	[Property, Group( "Lightmaps" )] public int LightmapShadows { get; set; } = -1;

	[Property, Group( "Lightmaps" )] public Vector2 LightmapUvScale { get; set; } = Vector2.One;

	/// <summary>
	/// The cube array every cubemap volume reflects from.
	/// </summary>
	[Property, Group( "Volumes" )] public int EnvironmentMaps { get; set; } = -1;
	[Property, Group( "Volumes" )] public int EnvironmentMapMips { get; set; } = 1;

	/// <summary>
	/// Cubemap and light probe volumes, in the order they claim a point: earlier volumes first.
	/// </summary>
	[Property, Group( "Volumes" )] public List<DeadlockEnvironmentVolume> Volumes { get; set; } = [];

	[Property, Group( "Volumes" )] public List<DeadlockProbeVolume> ProbeVolumes { get; set; } = [];

	/// <summary>
	/// What lights whatever no volume covers, per colour channel: a first order harmonic, (normal, 1) dotted with it.
	/// </summary>
	[Property, Group( "Ambient" )] public Vector4 AmbientRed { get; set; }
	[Property, Group( "Ambient" )] public Vector4 AmbientGreen { get; set; }
	[Property, Group( "Ambient" )] public Vector4 AmbientBlue { get; set; }

	/// <summary>
	/// The sun's direction from the surface to it. Zero when the map has no realtime sun.
	/// </summary>
	[Property, Group( "Sun" )] public Vector3 SunDirection { get; set; }

	/// <summary>
	/// Linear colour times brightness.
	/// </summary>
	[Property, Group( "Sun" )] public Vector3 SunColor { get; set; }

	/// <summary>
	/// Which channel of the lightmap shadows holds the sun.
	/// </summary>
	[Property, Group( "Sun" )] public Vector4 SunShadowMask { get; set; }

	/// <summary>
	/// The stationary and dynamic lights, as the game hands them to its shaders. Omni lights are several faces.
	/// </summary>
	[Property, Group( "Lights" )] public List<DeadlockBarnLight> Lights { get; set; } = [];

	/// <summary>
	/// The shadow maps the map compiler rendered for lights with precomputed shadows.
	/// </summary>
	[Property, Group( "Lights" )] public int ShadowAtlas { get; set; } = -1;

	/// <summary>
	/// The split sum lookup the shaders take reflection energy from.
	/// </summary>
	[Property, Group( "Shading" )] public int BrdfLookup { get; set; } = -1;

	/// <summary>
	/// How far a cubemap reflection may be brightened to match the baked light around it: a ceiling of
	/// max( roughness * x + y, 1 ). Darkening isn't capped.
	/// </summary>
	[Property, Group( "Shading" )] public Vector2 CubemapNormalization { get; set; } = new( 2, 1 );

	/// <summary>
	/// How much rough specular on lightmapped surfaces comes from the lightmap rather than the cubemaps:
	/// saturate( x + y * roughness ), none on metals.
	/// </summary>
	[Property, Group( "Shading" )] public Vector2 LightmapSpecular { get; set; } = new( -1, 2 );

	private readonly List<Texture> _textures = [];
	private GpuBuffer<DeadlockEnvironmentVolume> _volumes;
	private GpuBuffer<DeadlockProbeVolume.Gpu> _probes;
	private GpuBuffer<DeadlockBarnLight> _lights;
	private GpuBuffer<uint> _grid;

	/// <summary>
	/// The most cells the volume and light grid has; its cells grow until it fits.
	/// </summary>
	private const int MaxGridCells = 196608;

	private bool _applied;
	private Transform? _mapTransform;

	// Applied on the first update rather than on enable: the mount builds map scenes in a scene that is never
	// updated, and loading the lightmaps there would be wasted.
	protected override void OnEnabled() => _applied = false;

	protected override void OnUpdate()
	{
		if ( !_applied )
		{
			_applied = true;
			Apply();
		}

		if ( _mapTransform != WorldTransform )
			SetMapTransform();
	}

	protected override void OnDisabled()
	{
		_applied = false;
		Clear();
	}

	protected override void OnValidate() => _applied = false;

	/// <summary>
	/// Loads the textures, uploads the volumes and lights, and sets the scene's render attributes.
	/// </summary>
	public void Apply()
	{
		Clear();

		if ( Scene?.RenderAttributes is not { } attributes )
			return;

		foreach ( var path in Textures )
			_textures.Add( string.IsNullOrEmpty( path ) ? null : Texture.Load( path, false ) );

		attributes.Set( "DeadlockLighting", 1 );
		SetMapTransform();

		attributes.Set( "DeadlockLightmapIrradiance", TextureIndex( LightmapIrradiance ) );
		attributes.Set( "DeadlockLightmapIrradianceR", TextureIndex( LightmapIrradianceRed ) );
		attributes.Set( "DeadlockLightmapIrradianceG", TextureIndex( LightmapIrradianceGreen ) );
		attributes.Set( "DeadlockLightmapIrradianceB", TextureIndex( LightmapIrradianceBlue ) );
		attributes.Set( "DeadlockLightmapShadows", TextureIndex( LightmapShadows ) );
		attributes.Set( "DeadlockLightmapUvScale", LightmapUvScale );

		attributes.Set( "DeadlockEnvironmentMaps", TextureIndex( EnvironmentMaps ) );
		attributes.Set( "DeadlockEnvironmentMapMips", (float)Math.Max( EnvironmentMapMips - 1, 0 ) );

		_volumes = Upload( "DeadlockEnvironmentVolumes", Volumes );
		attributes.Set( "DeadlockEnvironmentVolumeCount", Volumes.Count );

		_probes = Upload( "DeadlockProbeVolumes", ProbeVolumes.Select( x => x.ToGpu( TextureIndex ) ).ToList() );
		attributes.Set( "DeadlockProbeVolumeCount", ProbeVolumes.Count );

		attributes.Set( "DeadlockAmbientR", AmbientRed );
		attributes.Set( "DeadlockAmbientG", AmbientGreen );
		attributes.Set( "DeadlockAmbientB", AmbientBlue );

		attributes.Set( "DeadlockSunDirection", SunDirection );
		attributes.Set( "DeadlockSunColor", SunColor );
		attributes.Set( "DeadlockSunShadowMask", SunShadowMask );

		_lights = Upload( "DeadlockBarnLights", Lights.Select( WithCookie ).ToList() );
		attributes.Set( "DeadlockBarnLightCount", Lights.Count );
		attributes.Set( "DeadlockShadowAtlas", TextureIndex( ShadowAtlas ) );

		BuildGrid();

		attributes.Set( "DeadlockBrdfLookup", TextureIndex( BrdfLookup ) );
		attributes.Set( "DeadlockCubemapNormalization", CubemapNormalization );
		attributes.Set( "DeadlockLightmapSpecular", LightmapSpecular );
	}

	/// <summary>
	/// The transform from world space into the map's, as three rows.
	/// </summary>
	private void SetMapTransform()
	{
		var transform = WorldTransform;
		_mapTransform = transform;

		Vector3[] axes = [transform.Rotation.Forward, transform.Rotation.Left, transform.Rotation.Up];
		var scale = transform.Scale;

		for ( var i = 0; i < 3; i++ )
		{
			var row = axes[i] / scale[i];
			Scene.RenderAttributes.Set( $"DeadlockWorldToMap{i}", new Vector4( row, -Vector3.Dot( row, transform.Position ) ) );
		}
	}

	/// <summary>
	/// Takes the lighting back out of the scene, leaving Deadlock shaders lit by s&amp;box's own lights.
	/// </summary>
	public void Clear()
	{
		Scene?.RenderAttributes?.Set( "DeadlockLighting", 0 );
		_mapTransform = null;

		Scene?.RenderAttributes?.Set( "DeadlockGridSize", Vector4.Zero );

		_volumes?.Dispose();
		_probes?.Dispose();
		_lights?.Dispose();
		_grid?.Dispose();
		_volumes = null;
		_probes = null;
		_lights = null;
		_grid = null;
		_textures.Clear();
	}

	/// <summary>
	/// The grid the shaders find the volumes and lights around a point with (deadlock_lighting.hlsl), rather than
	/// visiting every one of them per pixel: midtown has 166 volumes and 33 lights, few of which reach any one point.
	/// A uniform grid over the map, each cell listing the volumes, then the lights, whose reach overlaps it, in their
	/// own order, so the shading is the same.
	/// </summary>
	private void BuildGrid()
	{
		var attributes = Scene.RenderAttributes;
		attributes.Set( "DeadlockGridSize", Vector4.Zero );

		List<BBox?>[] lists = [Volumes.Select( VolumeReach ).ToList(), Lights.Select( LightReach ).ToList()];

		BBox? union = null;
		foreach ( var reach in lists.SelectMany( x => x ) )
		{
			if ( reach is { } box )
				union = union?.AddBBox( box ) ?? box;
		}

		// Nothing with a limited reach: visiting everything is as good.
		if ( union is not { } grid )
			return;

		var size = grid.Size;
		var cellSize = MathF.Max( 256f, MathF.Max( size.x, MathF.Max( size.y, size.z ) ) / 128f );
		int nx, ny, nz;

		while ( true )
		{
			nx = Math.Max( 1, (int)MathF.Ceiling( size.x / cellSize ) );
			ny = Math.Max( 1, (int)MathF.Ceiling( size.y / cellSize ) );
			nz = Math.Max( 1, (int)MathF.Ceiling( size.z / cellSize ) );
			if ( (long)nx * ny * nz <= MaxGridCells )
				break;

			cellSize *= 1.25f;
		}

		var cells = nx * ny * nz;

		void ForEachCell( BBox? reach, Action<int> action )
		{
			if ( reach is not { } box )
			{
				for ( var cell = 0; cell < cells; cell++ )
					action( cell );

				return;
			}

			int Clamp( float value, int count ) => Math.Clamp( (int)MathF.Floor( value / cellSize ), 0, count - 1 );

			var min = box.Mins - grid.Mins;
			var max = box.Maxs - grid.Mins;

			for ( var z = Clamp( min.z, nz ); z <= Clamp( max.z, nz ); z++ )
			for ( var y = Clamp( min.y, ny ); y <= Clamp( max.y, ny ); y++ )
			for ( var x = Clamp( min.x, nx ); x <= Clamp( max.x, nx ); x++ )
				action( (z * ny + y) * nx + x );
		}

		// Count, then lay the lists out after the cells' headers (first and count of each list), and fill them.
		var counts = new int[cells * 2];
		for ( var list = 0; list < 2; list++ )
		{
			var index = list;
			foreach ( var reach in lists[list] )
				ForEachCell( reach, cell => counts[cell * 2 + index]++ );
		}

		var data = new uint[cells * 4 + counts.Sum()];
		var cursors = new int[cells * 2];
		var next = cells * 4;

		for ( var cell = 0; cell < cells; cell++ )
		{
			for ( var list = 0; list < 2; list++ )
			{
				data[cell * 4 + list * 2] = (uint)next;
				data[cell * 4 + list * 2 + 1] = (uint)counts[cell * 2 + list];
				cursors[cell * 2 + list] = next;
				next += counts[cell * 2 + list];
			}
		}

		for ( var list = 0; list < 2; list++ )
		{
			for ( var item = 0; item < lists[list].Count; item++ )
			{
				var index = list;
				var value = (uint)item;
				ForEachCell( lists[list][item], cell => data[cursors[cell * 2 + index]++] = value );
			}
		}

		_grid = new GpuBuffer<uint>( data.Length, debugName: "DeadlockGrid" );
		_grid.SetData( data.AsSpan() );

		attributes.Set( "DeadlockGrid", _grid );
		attributes.Set( "DeadlockGridOrigin", new Vector4( grid.Mins, 1f / cellSize ) );
		attributes.Set( "DeadlockGridSize", new Vector4( nx, ny, nz, 1 ) );
	}

	/// <summary>
	/// Where a volume can light, in the map's space: its sphere out to where the fade reaches zero, or its box.
	/// Null for one that reaches everywhere.
	/// </summary>
	private static BBox? VolumeReach( DeadlockEnvironmentVolume volume )
	{
		if ( volume.BoxMax.w != 0 )
		{
			// The shaders fade a sphere by 1 - saturate( distance * x + y ).
			var fade = volume.Fade;
			if ( fade.x <= 0 )
				return fade.y < 1 ? null : new BBox( volume.Center, volume.Center );

			var radius = (1 - fade.y) / fade.x;
			return BBox.FromPositionAndSize( (Vector3)volume.Center, radius * 2 + 1 );
		}

		return ReachOfBox( volume.WorldToLocal0, volume.WorldToLocal1, volume.WorldToLocal2, (Vector3)volume.BoxMin, (Vector3)volume.BoxMax );
	}

	/// <summary>
	/// Where a light can light: the box it's limited to, -1 to 1 through its illumination transform.
	/// </summary>
	private static BBox? LightReach( DeadlockBarnLight light )
		=> ReachOfBox( light.Illumination0, light.Illumination1, light.Illumination2, new Vector3( -1 ), new Vector3( 1 ) );

	/// <summary>
	/// The map space box around a box given in a local space by the rows of the map to local transform, or null
	/// when the transform can't be inverted.
	/// </summary>
	private static BBox? ReachOfBox( Vector4 row0, Vector4 row1, Vector4 row2, Vector3 min, Vector3 max )
	{
		var matrix = new System.Numerics.Matrix4x4(
			row0.x, row1.x, row2.x, 0,
			row0.y, row1.y, row2.y, 0,
			row0.z, row1.z, row2.z, 0,
			row0.w, row1.w, row2.w, 1 );

		if ( !System.Numerics.Matrix4x4.Invert( matrix, out var inverse ) )
			return null;

		var reachMin = new Vector3( float.MaxValue );
		var reachMax = new Vector3( float.MinValue );

		for ( var corner = 0; corner < 8; corner++ )
		{
			var local = new System.Numerics.Vector3(
				(corner & 1) != 0 ? max.x : min.x,
				(corner & 2) != 0 ? max.y : min.y,
				(corner & 4) != 0 ? max.z : min.z );

			Vector3 point = System.Numerics.Vector3.Transform( local, inverse );
			reachMin = Vector3.Min( reachMin, point );
			reachMax = Vector3.Max( reachMax, point );
		}

		if ( !float.IsFinite( reachMin.x + reachMin.y + reachMin.z + reachMax.x + reachMax.y + reachMax.z ) )
			return null;

		// A little margin for rounding.
		return new BBox( reachMin - 1, reachMax + 1 );
	}

	/// <summary>
	/// A light's cookie is a texture slot in its colour's w, plus one, negative for a spherical cookie. The shaders
	/// take the texture's bindless index there instead.
	/// </summary>
	private DeadlockBarnLight WithCookie( DeadlockBarnLight light )
	{
		var color = light.Color;
		if ( color.w == 0 )
			return light;

		var index = TextureIndex( (int)MathF.Abs( color.w ) - 1 );
		light.Color = new Vector4( color.x, color.y, color.z, index == 0 ? 0 : MathF.Sign( color.w ) * index );

		// Without its texture the light keeps a plain barn shape.
		if ( index == 0 )
			light.Shape = new Vector4( 1, 1, 0, 0 );

		return light;
	}

	private int TextureIndex( int slot )
	{
		return slot >= 0 && slot < _textures.Count && _textures[slot] is { IsValid: true } texture ? texture.Index : 0;
	}

	private GpuBuffer<T> Upload<T>( string name, List<T> items ) where T : unmanaged
	{
		// An empty buffer still has to exist for the shaders to bind.
		var buffer = new GpuBuffer<T>( Math.Max( items.Count, 1 ), debugName: name );
		if ( items.Count > 0 )
			buffer.SetData( items );

		Scene.RenderAttributes.Set( name, buffer );
		return buffer;
	}
}

/// <summary>
/// A cubemap volume (env_cubemap_box, env_cubemap, env_combined_light_probe_volume), laid out for the shaders.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct DeadlockEnvironmentVolume
{
	/// <summary>
	/// Rows of the volume's world to local transform.
	/// </summary>
	public Vector4 WorldToLocal0 { get; set; }
	public Vector4 WorldToLocal1 { get; set; }
	public Vector4 WorldToLocal2 { get; set; }

	/// <summary>
	/// The box in local space; w is the cube's slice of the cube array.
	/// </summary>
	public Vector4 BoxMin { get; set; }

	/// <summary>
	/// w is 1 for a sphere, which projects and fades by its radius instead of the box.
	/// </summary>
	public Vector4 BoxMax { get; set; }

	/// <summary>
	/// A box's inverse edge fade widths, or for a sphere its fade scale (x), bias (y) and radius (z).
	/// </summary>
	public Vector4 Fade { get; set; }

	/// <summary>
	/// A sphere's centre; w is the light probe volume sharing this volume, -1 for none.
	/// </summary>
	public Vector4 Center { get; set; }

	public Vector4 Tint { get; set; }

	/// <summary>
	/// The luminance of the cube's radiance as a first order harmonic, which reflections are normalized against.
	/// </summary>
	public Vector4 LuminanceSH { get; set; }
}

/// <summary>
/// A light probe volume: an ambient cube per voxel, in a part of the map's probe atlas.
/// </summary>
public struct DeadlockProbeVolume
{
	/// <summary>
	/// Rows of the transform from world space to 0-1 across the volume.
	/// </summary>
	public Vector4 WorldToLocal0 { get; set; }
	public Vector4 WorldToLocal1 { get; set; }
	public Vector4 WorldToLocal2 { get; set; }

	/// <summary>
	/// Where samples are clamped to, so they don't blend with the neighbouring volumes in the atlas.
	/// </summary>
	public Vector3 BorderMin { get; set; }
	public Vector3 BorderMax { get; set; }

	public Vector3 AtlasScale { get; set; }
	public Vector3 AtlasOffset { get; set; }

	/// <summary>
	/// Indices in <see cref="DeadlockMapLighting.Textures"/>: the ambient cubes, and the direct light shadows.
	/// </summary>
	public int Irradiance { get; set; }
	public int Shadows { get; set; }

	[StructLayout( LayoutKind.Sequential )]
	internal struct Gpu
	{
		public Vector4 WorldToLocal0;
		public Vector4 WorldToLocal1;
		public Vector4 WorldToLocal2;
		public Vector4 BorderMin;
		public Vector4 BorderMax;
		public Vector4 AtlasScale;
		public Vector4 AtlasOffset;
	}

	internal Gpu ToGpu( Func<int, int> textureIndex ) => new()
	{
		WorldToLocal0 = WorldToLocal0,
		WorldToLocal1 = WorldToLocal1,
		WorldToLocal2 = WorldToLocal2,
		BorderMin = new Vector4( BorderMin, textureIndex( Irradiance ) ),
		BorderMax = new Vector4( BorderMax, textureIndex( Shadows ) ),
		AtlasScale = new Vector4( AtlasScale, 0 ),
		AtlasOffset = new Vector4( AtlasOffset, 0 ),
	};
}

/// <summary>
/// One face of a light2 light (light_barn, light_omni2, light_rect), laid out the way Deadlock's shaders take it.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct DeadlockBarnLight
{
	/// <summary>
	/// Rows of the world to light clip space projection: inside the light is -1 to 1 across and 0 to 1 deep.
	/// </summary>
	public Vector4 Frustum0 { get; set; }
	public Vector4 Frustum1 { get; set; }
	public Vector4 Frustum2 { get; set; }
	public Vector4 Frustum3 { get; set; }

	/// <summary>
	/// Rows of the transform into the box the light is limited to, where inside is -1 to 1.
	/// </summary>
	public Vector4 Illumination0 { get; set; }
	public Vector4 Illumination1 { get; set; }
	public Vector4 Illumination2 { get; set; }

	/// <summary>
	/// The point the light falls off from, with the squared distance it is at full brightness in w. A w of zero
	/// makes xyz the direction of a light with parallel rays.
	/// </summary>
	public Vector4 Position { get; set; }

	/// <summary>
	/// Distance fade scale and bias, and the near and far skirts.
	/// </summary>
	public Vector4 DistanceFade { get; set; }

	/// <summary>
	/// Linear colour times intensity; w selects a cookie, 0 for none.
	/// </summary>
	public Vector4 Color { get; set; }

	public Vector4 Orientation { get; set; }

	/// <summary>
	/// Cone fade scale and bias, and in z the half length of the luminaire.
	/// </summary>
	public Vector4 AngleFade { get; set; }

	/// <summary>
	/// The light's rectangle in the precomputed shadow atlas: offset, then scale, which is zero without one.
	/// </summary>
	public Vector4 ShadowRect { get; set; }

	/// <summary>
	/// The softness and roundness of a barn light's shape, or the cookie's offset and scale.
	/// </summary>
	public Vector4 Shape { get; set; }

	/// <summary>
	/// Which channel of the baked shadows is this light's.
	/// </summary>
	public Vector4 BakedShadowMask { get; set; }

	/// <summary>
	/// Minimum roughness, shadow strength, shadow filter radius and flags.
	/// </summary>
	public Vector4 Parameters { get; set; }
}
