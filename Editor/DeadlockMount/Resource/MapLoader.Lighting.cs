using Deadlock;
using System;
using Numerics = System.Numerics;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// The map's lighting, laid out for Deadlock's shaders the way the game hands it to its deferred lighting pass.
/// Light faces, volumes and their constants follow ValveResourceFormat's renderer, which matches the game's.
/// </summary>
internal sealed partial class MapLoader
{
	/// <summary>
	/// No light shines sharper than this roughness.
	/// </summary>
	private const float MinSpecularRoughness = 0.04f;

	/// <summary>
	/// How far in front of its luminaire a light2 light is as bright as its brightness says.
	/// </summary>
	private const float LinearBrightnessDistance = 100f;

	/// <summary>
	/// Cubemap volumes reach this much past their box, so faces lying on it are inside.
	/// </summary>
	private const float VolumeBoundsExtend = 0.02f;

	private static readonly string[] LightProbeTextures = ["lightprobetexture", "lightprobetexture_dlshd"];

	private void BuildLighting( CompiledWorld world, IReadOnlyList<MapEntity> entities )
	{
		var gameObject = new GameObject( true, "Lighting" );
		var lighting = gameObject.Components.Create<DeadlockMapLighting>();
		var textures = new List<string>();

		int AddTexture( string path )
		{
			if ( string.IsNullOrWhiteSpace( path ) )
				return -1;

			path = CompiledWorld.NormalizePath( path );
			if ( !Host.FileExists( path + "_c" ) )
				return -1;

			var mountPath = $"mount://{Host.Ident}/{path}";
			var index = textures.IndexOf( mountPath );
			if ( index >= 0 )
				return index;

			textures.Add( mountPath );
			return textures.Count - 1;
		}

		if ( world.LightingInfo is { } info && info.GetBool( "m_bHasLightmaps" ) )
		{
			foreach ( var path in info.GetStringArray( "m_lightMaps" ) )
			{
				switch ( System.IO.Path.GetFileNameWithoutExtension( path ) )
				{
					case "directional_irradiance_sh2_dc": lighting.LightmapIrradiance = AddTexture( path ); break;
					case "directional_irradiance_sh2_r": lighting.LightmapIrradianceRed = AddTexture( path ); break;
					case "directional_irradiance_sh2_g": lighting.LightmapIrradianceGreen = AddTexture( path ); break;
					case "directional_irradiance_sh2_b": lighting.LightmapIrradianceBlue = AddTexture( path ); break;
					case "direct_light_shadows": lighting.LightmapShadows = AddTexture( path ); break;
				}
			}

			if ( lighting.LightmapIrradiance < 0 )
				Log.Warning( $"{mapName}: the lightmaps aren't second order harmonics, which is all the Deadlock shaders read." );

			var uvScale = info.GetFloatArray( "m_vLightmapUvScale" );
			if ( uvScale.Length >= 2 )
				lighting.LightmapUvScale = new Vector2( uvScale[0], uvScale[1] );
		}

		AddVolumes( lighting, entities, AddTexture );
		AddSunLighting( lighting, entities );

		lighting.Lights = [];
		foreach ( var entity in entities )
		{
			if ( entity.Classname.ToLowerInvariant() is "light_barn" or "light_omni2" or "light_rect" && IsRealtime( entity ) && entity.GetBool( "enabled", true ) )
				lighting.Lights.AddRange( ComputeLightFaces( entity, AddTexture ) );
		}

		lighting.ShadowAtlas = AddTexture( $"maps/{mapName}/lightmaps/precomputed_shadowmap_atlas.vtex" );
		lighting.BrdfLookup = AddTexture( "textures/dev/brdf_lut.vtex" );
		lighting.Textures = textures;
	}

	/// <summary>
	/// Cubemap volumes and the light probe volumes combined with them, in the order they claim a point: the most
	/// indoor first, then by their place in the cube array.
	/// </summary>
	private void AddVolumes( DeadlockMapLighting lighting, IReadOnlyList<MapEntity> entities, Func<string, int> addTexture )
	{
		var volumes = new List<(int Indoor, int ArrayIndex, DeadlockEnvironmentVolume Volume)>();
		var probes = new List<DeadlockProbeVolume>();
		var cubemaps = new Dictionary<string, CompiledTexture>( StringComparer.OrdinalIgnoreCase );
		string environmentMaps = null;

		CompiledTexture ReadTexture( string path )
		{
			path = CompiledWorld.NormalizePath( path );
			if ( !cubemaps.TryGetValue( path, out var texture ) )
				cubemaps[path] = texture = Host.FileExists( path + "_c" ) ? new CompiledTexture( Host.ReadFile( path + "_c" ) ) : null;

			return texture;
		}

		foreach ( var entity in entities )
		{
			var classname = entity.Classname.ToLowerInvariant();
			if ( classname is not ("env_combined_light_probe_volume" or "env_cubemap_box" or "env_cubemap" or "env_light_probe_volume") )
				continue;

			if ( entity.GetBool( "StartDisabled" ) )
				continue;

			var transform = ReadTransform( entity );
			var origin = transform.Position;
			Vector3[] axes = [transform.Rotation.Forward, transform.Rotation.Left, transform.Rotation.Up];
			var isSphere = classname == "env_cubemap";

			Vector3 mins, maxs;
			if ( isSphere )
			{
				var radius = entity.GetFloat( "influenceradius", 512 );
				mins = -radius;
				maxs = radius;
			}
			else
			{
				mins = ReadVector( entity, "box_mins", -64 );
				maxs = ReadVector( entity, "box_maxs", 64 );
			}

			var probeIndex = -1;
			if ( classname is "env_combined_light_probe_volume" or "env_light_probe_volume" && entity.Has( "light_probe_atlas_x" ) )
			{
				var irradiance = addTexture( entity.GetString( LightProbeTextures[0] ) );
				var shadowsPath = entity.GetString( LightProbeTextures[1] );
				var shadows = addTexture( shadowsPath );

				// The atlas is laid out in the shadows texture's voxels; the irradiance stacks six of it in depth.
				if ( irradiance >= 0 && shadows >= 0 && ReadTexture( shadowsPath ) is { } atlas )
				{
					var dims = new Vector3( atlas.Width, atlas.Height, atlas.Depth );
					var size = ReadVector( entity, "light_probe_size", 1, "x", "y", "z" );
					var offset = ReadVector( entity, "light_probe_atlas", 0, "x", "y", "z" );
					var boxSize = maxs - mins;

					probes.Add( new DeadlockProbeVolume
					{
						// World space to 0-1 across the box.
						WorldToLocal0 = new Vector4( axes[0] / boxSize.x, (-Vector3.Dot( axes[0], origin ) - mins.x) / boxSize.x ),
						WorldToLocal1 = new Vector4( axes[1] / boxSize.y, (-Vector3.Dot( axes[1], origin ) - mins.y) / boxSize.y ),
						WorldToLocal2 = new Vector4( axes[2] / boxSize.z, (-Vector3.Dot( axes[2], origin ) - mins.z) / boxSize.z ),

						// Half a voxel in from the edges, depth in sixths like the ambient cube lookups.
						BorderMin = new Vector3( 0.5f / size.x, 0.5f / size.y, 0.5f / size.z / 6f ),
						BorderMax = new Vector3( 1 - 0.5f / size.x, 1 - 0.5f / size.y, (1 - 0.5f / size.z) / 6f ),
						AtlasScale = size / dims,
						AtlasOffset = offset / dims,
						Irradiance = irradiance,
						Shadows = shadows,
					} );

					probeIndex = probes.Count - 1;
				}
			}

			var slice = -1;
			var luminance = Vector4.Zero;
			var cubemapPath = entity.GetString( "cubemaptexture" );

			// A custom cubemap is a texture of the mapper's choosing, not part of the baked array.
			if ( !string.IsNullOrEmpty( cubemapPath ) && string.IsNullOrEmpty( entity.GetString( "customcubemaptexture" ) ) && ReadTexture( cubemapPath ) is { IsCube: true } cubemap )
			{
				environmentMaps ??= CompiledWorld.NormalizePath( cubemapPath );

				if ( environmentMaps.Equals( CompiledWorld.NormalizePath( cubemapPath ), StringComparison.OrdinalIgnoreCase ) )
				{
					lighting.EnvironmentMaps = addTexture( cubemapPath );
					lighting.EnvironmentMapMips = cubemap.MipCount;
					slice = entity.GetInt( "array_index" );
					luminance = NormalizationSH( cubemap.RadianceCoefficients, slice );
				}
				else
				{
					Log.Warning( $"{mapName}: a cubemap volume reflects '{cubemapPath}', but the map's cubemaps are in '{environmentMaps}'." );
				}
			}

			if ( slice < 0 && probeIndex < 0 )
				continue;

			var fade = ReadVector( entity, "edge_fade_dists", 0 );
			Vector4 fadeParameters;

			if ( isSphere )
			{
				// 1 - saturate( distance * x + y ): fades to nothing over the outer edge of the radius.
				var width = MathF.Max( entity.GetFloat( "edge_fade_dist", 0 ), VolumeBoundsExtend );
				fadeParameters = new Vector4( 1 / width, 1 - maxs.x / width, maxs.x, 0 );
			}
			else
			{
				fadeParameters = new Vector4( 1 / (fade.x + VolumeBoundsExtend), 1 / (fade.y + VolumeBoundsExtend), 1 / (fade.z + VolumeBoundsExtend), 0 );
			}

			volumes.Add( (entity.GetInt( "indoor_outdoor_level" ), slice, new DeadlockEnvironmentVolume
			{
				WorldToLocal0 = new Vector4( axes[0], -Vector3.Dot( axes[0], origin ) ),
				WorldToLocal1 = new Vector4( axes[1], -Vector3.Dot( axes[1], origin ) ),
				WorldToLocal2 = new Vector4( axes[2], -Vector3.Dot( axes[2], origin ) ),
				BoxMin = new Vector4( mins - VolumeBoundsExtend, slice ),
				BoxMax = new Vector4( maxs + VolumeBoundsExtend, isSphere ? 1 : 0 ),
				Fade = fadeParameters,
				Center = new Vector4( origin, probeIndex ),
				Tint = new Vector4( 1, 1, 1, 0 ),
				LuminanceSH = luminance,
			}) );
		}

		lighting.Volumes = volumes.OrderByDescending( x => x.Indoor ).ThenBy( x => x.ArrayIndex ).Select( x => x.Volume ).ToList();
		lighting.ProbeVolumes = probes;
	}

	/// <summary>
	/// A cube's radiance reduced to the luminance of its constant and linear bands, as (normal, 1) is dotted with.
	/// The texture keeps 9 coefficients per colour channel, channels planar, per cube of the array.
	/// </summary>
	private static Vector4 NormalizationSH( float[] coefficients, int slice )
	{
		const int perChannel = 9;
		const int perCube = perChannel * 3;
		const float constantBand = 0.282095f;
		const float linearBand = 0.325735f;

		var offset = slice * perCube;
		if ( coefficients is null || slice < 0 || offset + perCube > coefficients.Length )
			return Vector4.Zero;

		Span<float> luma = stackalloc float[4];
		for ( var band = 0; band < 4; band++ )
		{
			luma[band] = coefficients[offset + band] * 0.2125f
				+ coefficients[offset + perChannel + band] * 0.7154f
				+ coefficients[offset + perChannel * 2 + band] * 0.0721f;
		}

		// The linear band is stored y, z, x.
		return new Vector4( luma[3] * linearBand, luma[1] * linearBand, luma[2] * linearBand, luma[0] * constantBand );
	}

	/// <summary>
	/// The sun, and the sky light that lights whatever no volume covers.
	/// </summary>
	private static void AddSunLighting( DeadlockMapLighting lighting, IReadOnlyList<MapEntity> entities )
	{
		var sun = entities.FirstOrDefault( x => x.Classname.Equals( "light_environment", StringComparison.OrdinalIgnoreCase ) );
		if ( sun is null )
			return;

		var forward = ReadTransform( sun ).Rotation.Forward;
		var color = SrgbToLinear( ReadColor( sun, "color", [255, 255, 255] ) ) * sun.GetFloat( "brightness", 1 ) * sun.GetFloat( "brightnessscale", 1 );

		if ( IsRealtime( sun ) && sun.GetBool( "enabled", true ) )
		{
			lighting.SunDirection = -forward;
			lighting.SunColor = color;
			lighting.SunShadowMask = BakedShadowMask( sun );
		}

		// Sky from above, and the sun and sky bouncing off the ground from below.
		var sky = SrgbToLinear( ReadColor( sun, "skycolor", [255, 255, 255] ) ) * sun.GetFloat( "skyintensity", 1 );
		var bounce = SrgbToLinear( ReadColor( sun, "skyambientbounce", [0, 0, 0] ) );
		var sunDown = forward.z;
		var sunBounce = color * bounce * 0.5f;
		var skyBounce = sky * bounce * 0.5f;

		var vertical = sky * 0.5f - skyBounce + sunBounce * sunDown;
		var constant = sky * 0.5f + skyBounce + sunBounce * MathF.Abs( sunDown );

		lighting.AmbientRed = new Vector4( 0, 0, vertical.x, constant.x );
		lighting.AmbientGreen = new Vector4( 0, 0, vertical.y, constant.y );
		lighting.AmbientBlue = new Vector4( 0, 0, vertical.z, constant.z );
	}

	/// <summary>
	/// The channel of the lightmap shadows that holds a stationary light, or nothing for one that bakes none.
	/// </summary>
	private static Vector4 BakedShadowMask( MapEntity entity ) => entity.GetInt( "bakedshadowindex", entity.GetInt( "bakelightindex", -1 ) ) switch
	{
		0 => new Vector4( 1, 0, 0, 0 ),
		1 => new Vector4( 0, 1, 0, 0 ),
		2 => new Vector4( 0, 0, 1, 0 ),
		3 => new Vector4( 0, 0, 0, 1 ),
		_ => Vector4.Zero,
	};

	/// <summary>
	/// A light2 light as the faces the game lights with: one frustum for a barn light, one to six for an omni or
	/// rect light depending on its cone, or a single box around everything an unshadowed one reaches.
	/// </summary>
	private List<DeadlockBarnLight> ComputeLightFaces( MapEntity entity, Func<string, int> addTexture )
	{
		var classname = entity.Classname.ToLowerInvariant();
		var range = entity.GetFloat( "range", 512 );
		if ( range <= 0.0001f )
			return [];

		var transform = ReadTransform( entity );
		var rotation = transform.Rotation;
		var origin = transform.Position;
		var forward = rotation.Forward;
		var left = rotation.Left;
		var up = rotation.Up;

		var color = SrgbToLinear( entity.GetInt( "colormode" ) != 0 ? ColorTemperature( entity.GetFloat( "colortemperature", 6600 ) ) : ReadColor( entity, "color", [255, 255, 255] ) );
		var intensity = MathF.Pow( 2, entity.GetFloat( "brightness" ) ) * entity.GetFloat( "brightnessscale", 1 );
		var castShadows = entity.GetInt( "castshadows", 1 );
		var skirt = entity.GetFloat( "skirt", 0.1f );
		var sizeParams = ReadVector( entity, "size_params", 0 );
		var orientation = new Vector4( rotation.x, rotation.y, rotation.z, rotation.w );
		var bakedShadowMask = BakedShadowMask( entity );
		var minRoughness = MathF.Max( MinSpecularRoughness, entity.GetFloat( "minroughness", MinSpecularRoughness ) );

		// Precomputed shadow maps, one per face, as (scale, offset) in the atlas.
		Vector4 ShadowRect( int face )
		{
			var rect = entity.GetVector( $"precomputed_shadowmap_atlas_rect_{face}", 4, null );
			if ( rect is null || rect[0] <= 0 )
				return Vector4.Zero;

			// Light space -1 to 1, y up, onto the rectangle, v down.
			return new Vector4( rect[2] + rect[0] * 0.5f, rect[3] + rect[1] * 0.5f, rect[0] * 0.5f, -rect[1] * 0.5f );
		}

		var hasPrecomputedShadows = entity.Has( "precomputed_shadowmap_atlas_rect_0" );

		Vector4 Parameters( Vector4 shadowRect ) => new( minRoughness, shadowRect.z > 0 ? 1 : 0, 1f / 128f, 0 );

		var (illumination, _) = LightObb( entity, castShadows, -1 );

		if ( classname == "light_barn" )
		{
			var softX = entity.GetFloat( "soft_x", 0.25f );
			var softY = entity.GetFloat( "soft_y", 0.25f );
			var skirtNear = entity.GetFloat( "skirt_near", 0.05f );
			var shape = entity.GetFloat( "shape" );
			var shear = entity.GetVector( "shear", 2, [0, 0] );

			// ValveResourceFormat's "right" for barn lights: up cross forward.
			var right = Vector3.Cross( up, forward ).Normal;

			Numerics.Matrix4x4 worldToFrustum;
			Vector4 position;
			float skirtNearScale, skirtFarScale;

			if ( sizeParams.z <= 0 )
			{
				// Parallel rays through a box.
				var eye = origin;
				var view = Numerics.Matrix4x4.CreateLookAtLeftHanded( eye, eye + forward, up );
				var shearMatrix = Numerics.Matrix4x4.Identity;
				shearMatrix.M31 = -shear[0] / range;
				shearMatrix.M32 = -shear[1] / range;
				var projection = Numerics.Matrix4x4.CreateOrthographicOffCenterLeftHanded( -sizeParams.x, sizeParams.x, -sizeParams.y, sizeParams.y, 0, range );

				worldToFrustum = view * shearMatrix * projection;
				skirtNearScale = skirtNear > 0 ? 1 / skirtNear : 0;
				skirtFarScale = skirt > 0 ? 1 / skirt : 0;

				var direction = (range * forward + shear[0] * right + shear[1] * up).Normal;
				position = new Vector4( -direction, 0 );
			}
			else
			{
				var near = 1 / sizeParams.z;
				var far = near + range;
				var centerX = shear[0] / range * near;
				var centerY = shear[1] / range * near;
				var eye = origin - near * forward - centerX * right - centerY * up;

				var projection = Numerics.Matrix4x4.CreatePerspectiveOffCenterLeftHanded( centerX - sizeParams.x, centerX + sizeParams.x, centerY - sizeParams.y, centerY + sizeParams.y, near, far );
				var view = Numerics.Matrix4x4.CreateLookAtLeftHanded( eye, eye + forward, up );
				worldToFrustum = view * projection;

				skirtNearScale = skirtNear > 0 ? (near + skirtNear * range) / (far * skirtNear) : 0;
				skirtFarScale = skirt > 0 ? (near + (1 - skirt) * range) / (near * skirt) : 0;

				position = new Vector4( eye, centerX * centerX + centerY * centerY + near * near );
			}

			var falloffDistSq = position.w;
			var scale = falloffDistSq > 0 ? (MathF.Sqrt( falloffDistSq ) + LinearBrightnessDistance) * (MathF.Sqrt( falloffDistSq ) + LinearBrightnessDistance) / falloffDistSq : 1;

			var cookie = addTexture( entity.GetString( "lightcookie" ) );
			var shapeParameters = cookie >= 0
				? new Vector4( 0, 0, 1, 1 )
				: new Vector4( 1 - softX, 1 - softY, shape > 0 ? MathF.Min( shape, 1 ) * 0.875f + 0.125f : 0, 0 );

			var shadowRect = ShadowRect( 0 );

			return
			[
				new DeadlockBarnLight
				{
					Frustum0 = Column( worldToFrustum, 0 ),
					Frustum1 = Column( worldToFrustum, 1 ),
					Frustum2 = Column( worldToFrustum, 2 ),
					Frustum3 = Column( worldToFrustum, 3 ),
					Illumination0 = illumination[0],
					Illumination1 = illumination[1],
					Illumination2 = illumination[2],
					Position = position,
					DistanceFade = new Vector4( 1, 0, skirtNearScale, skirtFarScale ),
					Color = new Vector4( color * intensity * scale, cookie >= 0 ? cookie + 1 : 0 ),
					Orientation = orientation,
					AngleFade = new Vector4( 1, 0, 0, 0 ),
					ShadowRect = shadowRect,
					Shape = shapeParameters,
					BakedShadowMask = bakedShadowMask,
					Parameters = Parameters( shadowRect ),
				},
			];
		}

		// Omni and rect lights. Rect lights light the hemisphere in front of them.
		var outerAngle = classname == "light_rect" ? 90 : entity.GetFloat( "outer_angle", 180 );
		var innerAngle = classname == "light_rect" ? 0 : entity.GetFloat( "inner_angle" );
		var luminaireShape = classname == "light_rect" ? -1 : entity.GetInt( "shape" );
		var angle = MathF.Max( 1, MathF.Min( outerAngle, 180 ) ) * MathF.PI / 180;
		var heightOffset = luminaireShape is 1 or 2 ? sizeParams.y : 0;
		var sideRange = angle < MathF.PI / 2 ? MathF.Max( range - heightOffset, 0 ) * MathF.Sin( angle ) + heightOffset : range;

		// Without shadows to render, one box around everything the light reaches does.
		var singleBox = castShadows == 0 || castShadows != 1 && !hasPrecomputedShadows;
		var omniCookie = addTexture( entity.GetString( "lightcookie" ) );
		var (fadeOffset, fadeRate) = DistanceFade( range, skirt );
		var (angleBias, angleScale) = AngleFade( outerAngle, innerAngle );
		var luminaireLength = luminaireShape switch
		{
			1 or 2 => sizeParams.y,
			0 => entity.GetFloat( "luminaire_size" ),
			_ => 0,
		};

		var omniColor = new Vector4( color * intensity * LinearBrightnessDistance * LinearBrightnessDistance, omniCookie >= 0 ? -(omniCookie + 1) : 0 );

		DeadlockBarnLight Face( Numerics.Matrix4x4 worldToFrustum, Vector4[] faceIllumination, Vector4 shadowRect ) => new()
		{
			Frustum0 = Column( worldToFrustum, 0 ),
			Frustum1 = Column( worldToFrustum, 1 ),
			Frustum2 = Column( worldToFrustum, 2 ),
			Frustum3 = Column( worldToFrustum, 3 ),
			Illumination0 = faceIllumination[0],
			Illumination1 = faceIllumination[1],
			Illumination2 = faceIllumination[2],
			Position = new Vector4( origin, 1 ),
			DistanceFade = new Vector4( fadeOffset, fadeRate, 0, 0 ),
			Color = omniColor,
			Orientation = orientation,
			AngleFade = new Vector4( angleBias, angleScale, luminaireLength, 0 ),
			ShadowRect = shadowRect,
			Shape = omniCookie >= 0 ? new Vector4( 0, 0, 1, 1 ) : new Vector4( 1, 1, 0, 0 ),
			BakedShadowMask = bakedShadowMask,
			Parameters = Parameters( shadowRect ),
		};

		if ( singleBox )
		{
			// Reaches behind the light when the cone opens past a hemisphere.
			var back = angle > MathF.PI / 2 ? -MathF.Cos( angle ) * range : 0;
			var view = Numerics.Matrix4x4.CreateLookAtLeftHanded( origin, origin + forward, up );
			var projection = Numerics.Matrix4x4.CreateOrthographicOffCenterLeftHanded( -sideRange, sideRange, -sideRange, sideRange, -back, range );

			return [Face( view * projection, illumination, Vector4.Zero )];
		}

		var faces = new List<DeadlockBarnLight>();

		void AddFace( Vector3 faceForward, Vector3 faceUp, float faceRange )
		{
			var index = faces.Count;
			var view = Numerics.Matrix4x4.CreateLookAtLeftHanded( origin, origin + faceForward, faceUp );
			var projection = Numerics.Matrix4x4.CreatePerspectiveLeftHanded( 2, 2, 1, 1 + faceRange );
			var (faceIllumination, valid) = LightObb( entity, castShadows, index );

			faces.Add( Face( view * projection, valid ? faceIllumination : new Vector4[3], ShadowRect( index ) ) );
		}

		var faceAngle = heightOffset > 0 ? MathF.Max( MathF.PI / 2, angle ) : angle;
		var faceCount = faceAngle <= MathF.PI / 4 ? 1 : faceAngle <= 3 * MathF.PI / 4 ? 5 : 6;

		AddFace( forward, up, range );

		if ( faceCount >= 5 )
		{
			AddFace( -up, forward, sideRange );
			AddFace( up, -forward, sideRange );
			AddFace( left, up, sideRange );
			AddFace( -left, up, sideRange );
		}

		if ( faceCount >= 6 )
			AddFace( -forward, up, MathF.Abs( MathF.Cos( angle ) ) * range );

		return faces;
	}

	/// <summary>
	/// The box the compiler found a light can reach, as rows of the transform into it (inside is -1 to 1). Face -1 is
	/// the whole light, other faces the omni light's sub frusta. Rows of zero accept everything.
	/// </summary>
	private static (Vector4[] Rows, bool Valid) LightObb( MapEntity entity, int castShadows, int face )
	{
		var rows = new Vector4[3];
		var stationary = entity.GetInt( "bakedshadowindex", -1 ) is >= 0 and <= 3;

		if ( entity.GetInt( "precomputedfieldsvalid" ) == 0 || !stationary && castShadows <= 0 )
			return (rows, false);

		var suffix = face >= 0 ? face.ToString( System.Globalization.CultureInfo.InvariantCulture ) : "";
		if ( face >= 0 && entity.GetInt( "precomputedsubfrusta" ) <= face )
			return (rows, false);

		var center = ReadVector( entity, $"precomputedobborigin{suffix}", 0 );
		var extent = ReadVector( entity, $"precomputedobbextent{suffix}", 0 );
		var angles = entity.GetVector( $"precomputedobbangles{suffix}", 3, [0, 0, 0] );
		if ( extent.x <= 0 || extent.y <= 0 || extent.z <= 0 )
			return (rows, false);

		var rotation = Rotation.From( angles[0], angles[1], angles[2] );
		Vector3[] axes = [rotation.Forward, rotation.Left, rotation.Up];

		// Shrunk a touch, so the box's own faces are inside.
		const float shrink = 0.999f;

		for ( var i = 0; i < 3; i++ )
		{
			var inverse = shrink / extent[i];
			rows[i] = new Vector4( axes[i] * inverse, -Vector3.Dot( center, axes[i] ) * inverse );
		}

		return (rows, true);
	}

	/// <summary>
	/// Distance fade of an omni light's skirt: saturate( offset + rate * distance ).
	/// </summary>
	private static (float Offset, float Rate) DistanceFade( float range, float skirt )
	{
		if ( skirt <= 0 )
			return (1, 0);

		if ( skirt >= 1 )
			return (1, -1 / range);

		return (1 / skirt, -1 / (range * skirt));
	}

	/// <summary>
	/// Cone fade: saturate( bias + scale * cos ), from full at the inner angle to nothing at the outer.
	/// </summary>
	private static (float Bias, float Scale) AngleFade( float outerAngle, float innerAngle )
	{
		if ( outerAngle >= 180 )
			return (1, 0);

		if ( outerAngle <= 0 )
			return (0, 0);

		var cosOuter = MathF.Min( MathF.Cos( outerAngle * MathF.PI / 180 ), 1 - 1e-7f );
		var cosInner = innerAngle > 0 ? MathF.Cos( Math.Clamp( innerAngle, 0, outerAngle - 0.001f ) * MathF.PI / 180 ) : 1;

		var denominator = cosOuter - cosInner;
		if ( denominator >= 0 )
			denominator = -1e-7f;

		return (cosOuter / denominator, 1 / denominator);
	}

	/// <summary>
	/// A column of a row vector matrix, which is a row of the transform the shaders apply with dot products.
	/// </summary>
	private static Vector4 Column( Numerics.Matrix4x4 m, int column ) => column switch
	{
		0 => new Vector4( m.M11, m.M21, m.M31, m.M41 ),
		1 => new Vector4( m.M12, m.M22, m.M32, m.M42 ),
		2 => new Vector4( m.M13, m.M23, m.M33, m.M43 ),
		_ => new Vector4( m.M14, m.M24, m.M34, m.M44 ),
	};

	private static Vector3 ReadVector( MapEntity entity, string key, float fallback )
	{
		var value = entity.GetVector( key, 3, [fallback, fallback, fallback] );
		return new Vector3( value[0], value[1], value[2] );
	}

	/// <summary>
	/// A vector stored as one key per component, such as light_probe_size_x, _y and _z.
	/// </summary>
	private static Vector3 ReadVector( MapEntity entity, string key, float fallback, string x, string y, string z )
	{
		return new Vector3( entity.GetFloat( $"{key}_{x}", fallback ), entity.GetFloat( $"{key}_{y}", fallback ), entity.GetFloat( $"{key}_{z}", fallback ) );
	}

	private static Vector3 SrgbToLinear( Color color )
	{
		static float Channel( float c ) => c <= 0.04045f ? c / 12.92f : MathF.Pow( (c + 0.055f) / 1.055f, 2.4f );
		return new Vector3( Channel( color.r ), Channel( color.g ), Channel( color.b ) );
	}
}
