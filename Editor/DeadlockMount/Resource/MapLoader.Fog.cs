using System;

namespace Sandbox.Mounting.Deadlock;

internal sealed partial class MapLoader
{
	/// <summary>
	/// The map's fog, on s&amp;box's Source 2 fog: env_gradient_fog's distance and height fog, env_cubemap_fog's fog
	/// coloured by the sky, and volumetric fog lit by the fog irradiance the map compiler baked, in the volumes the map
	/// gives it. The fog controller's strengths scale every volume, as s&amp;box's own map loading does.
	/// </summary>
	private void BuildFog( IReadOnlyList<(MapEntity Entity, GameObject GameObject)> entities )
	{
		var controller = entities.FirstOrDefault( x => x.Entity.Classname.Equals( "env_volumetric_fog_controller", StringComparison.OrdinalIgnoreCase ) && !x.Entity.GetBool( "StartDisabled" ) );
		var volumeScale = 1f;

		if ( controller.Entity is { } controllerEntity )
		{
			volumeScale = controllerEntity.GetFloat( "FogStrength", 1 ) * controllerEntity.GetFloat( "IndirectStrength", 1 );

			var fog = controller.GameObject.Components.Create<VolumetricFogController>();
			fog.BakedFogTexture = Host.LoadTexture( controllerEntity.GetString( "fogirradiancevolume" ) );
			fog.GlobalScale = volumeScale;
		}

		foreach ( var (entity, gameObject) in entities )
		{
			if ( entity.GetBool( "StartDisabled" ) )
				continue;

			switch ( entity.Classname.ToLowerInvariant() )
			{
				case "env_volumetric_fog_volume":
				{
					var volume = gameObject.Components.Create<VolumetricFogVolume>();
					volume.Bounds = new BBox( ReadVector( entity, "box_mins", -64 ), ReadVector( entity, "box_maxs", 64 ) );
					volume.Strength = entity.GetFloat( "FogStrength", 1 ) * volumeScale;
					volume.FalloffExponent = entity.GetFloat( "FalloffExponent", 1 );
					volume.Color = ReadColor( entity, "TintColor", [255, 255, 255] );
					break;
				}

				case "env_gradient_fog":
					AddGradientFog( entity, gameObject );
					break;

				case "env_cubemap_fog":
					AddCubemapFog( entity, gameObject, entities );
					break;
			}
		}
	}

	/// <summary>
	/// Fog by distance from the camera and height, the height fog running from fogstartheight to fogendheight.
	/// </summary>
	private static void AddGradientFog( MapEntity entity, GameObject gameObject )
	{
		var startHeight = entity.GetFloat( "fogstartheight" );
		var endHeight = entity.GetFloat( "fogendheight", startHeight + 1 );

		// s&box's gradient fog starts at its GameObject's height.
		var holder = new GameObject( true, "gradient fog" );
		holder.SetParent( gameObject, false );
		holder.WorldPosition = gameObject.WorldPosition.WithZ( startHeight );

		var strength = entity.GetFloat( "fogstrength", 1 );
		var color = ReadColor( entity, "fogcolor", [255, 255, 255] );

		var fog = holder.Components.Create<GradientFog>();
		fog.Color = new Color( color.r * strength, color.g * strength, color.b * strength, entity.GetFloat( "fogmaxopacity", 1 ) );
		fog.StartDistance = entity.GetFloat( "fogstart" );
		fog.EndDistance = entity.GetFloat( "fogend" );
		fog.FalloffExponent = entity.GetFloat( "fogfalloffexponent", 1 );
		fog.Height = endHeight - startHeight;
		fog.VerticalFalloffExponent = entity.GetFloat( "fogverticalexponent", 1 );
	}

	/// <summary>
	/// Fog that fades the distance into the sky, sampled from the sky's cubemap at its exposure.
	/// </summary>
	private void AddCubemapFog( MapEntity entity, GameObject gameObject, IReadOnlyList<(MapEntity Entity, GameObject GameObject)> entities )
	{
		var fog = gameObject.Components.Create<CubemapFog>();

		// Source 1 is the sky entity, which s&box's cubemap fog takes from the scene's sky without a material of its own.
		var skyMaterial = entity.GetInt( "cubemapfogsource" ) switch
		{
			1 => entities.FirstOrDefault( x => x.Entity.Classname.Equals( "env_sky", StringComparison.OrdinalIgnoreCase )
				&& x.Entity.TargetName.Equals( entity.GetString( "cubemapfogskyentity", "" ), StringComparison.OrdinalIgnoreCase ) ).Entity?.GetString( "skyname" ),
			2 => entity.GetString( "cubemapfogskymaterial" ),
			_ => null,
		};

		if ( entity.GetInt( "cubemapfogsource" ) == 2 && !string.IsNullOrEmpty( skyMaterial ) )
			fog.Sky = Host.LoadMaterial( skyMaterial );

		// The sky material's exposure, which the fog sees the sky at too.
		var exposure = 1f;
		if ( !string.IsNullOrEmpty( skyMaterial ) && Host.FileExists( CompiledWorld.NormalizePath( skyMaterial ) + "_c" ) )
		{
			var material = new CompiledMaterial( Host.ReadFile( CompiledWorld.NormalizePath( skyMaterial ) + "_c" ) );
			exposure = MathF.Pow( 2, material.GetFloat( "g_flBrightnessExposureBias" ) + material.GetFloat( "g_flRenderOnlyExposureBias" ) );
		}

		fog.Tint = new Color( exposure, exposure, exposure );

		// s&box takes a blur and biases the cubemap's mip by one minus it.
		fog.Blur = 1 - entity.GetFloat( "cubemapfoglodbiase" );
		fog.StartDistance = entity.GetFloat( "cubemapfogstartdistance" );
		fog.EndDistance = entity.GetFloat( "cubemapfogenddistance", 4096 );
		fog.FalloffExponent = entity.GetFloat( "cubemapfogfalloffexponent", 1 );
		fog.HeightStart = entity.GetFloat( "cubemapfogheightstart" );
		fog.HeightWidth = entity.GetFloat( "cubemapfogheightwidth" );
		fog.HeightExponent = entity.GetFloat( "cubemapfogheightexponent", 1 );
	}
}
