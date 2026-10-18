using Deadlock;
using Sandbox.Volumes;
using System;

namespace Sandbox.Mounting.Deadlock;

internal sealed partial class MapLoader
{
	/// <summary>
	/// post_processing_volume: a post process volume with Deadlock's tonemapping, bloom and vignette from its .vpost,
	/// and the exposure range the entity fixes. The master volume covers the whole map.
	/// </summary>
	private void AddPostProcessing( MapEntity entity, GameObject gameObject )
	{
		var volume = gameObject.Components.Create<PostProcessVolume>();
		volume.Priority = entity.GetBool( "master" ) ? 0 : 1;

		var bounds = LoadModel( GetEntityModel( entity ).Path )?.Bounds;
		volume.SceneVolume = entity.GetBool( "master" ) || bounds is null
			? new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite }
			: new SceneVolume { Type = SceneVolume.VolumeTypes.Box, Box = bounds.Value };

		var effect = gameObject.Components.Create<DeadlockPostProcess>();

		// The camera's exposure stays within the entity's range, which the game's maps close to one value.
		var exposure = 0f;
		if ( entity.GetBool( "enableexposure", true ) )
			exposure = Math.Clamp( 0f, entity.GetFloat( "minlogexposure" ), MathF.Max( entity.GetFloat( "minlogexposure" ), entity.GetFloat( "maxlogexposure" ) ) );

		effect.Exposure = exposure + entity.GetFloat( "exposurecompensation" );

		var path = CompiledWorld.NormalizePath( entity.GetString( "postprocessing", "" ) );
		if ( path.Length == 0 || !Host.FileExists( path + "_c" ) )
			return;

		var data = new ResourceFile( Host.ReadFile( path + "_c" ) ).ReadKeyValues( "DATA" );
		if ( data is null )
			return;

		if ( data.GetBool( "m_bHasTonemapParams" ) && data.GetObject( "m_toneMapParams" ) is { } tonemap )
		{
			effect.ExposureBias = tonemap.GetFloat( "m_flExposureBias" );
			effect.ShoulderStrength = tonemap.GetFloat( "m_flShoulderStrength" );
			effect.LinearStrength = tonemap.GetFloat( "m_flLinearStrength" );
			effect.LinearAngle = tonemap.GetFloat( "m_flLinearAngle" );
			effect.ToeStrength = tonemap.GetFloat( "m_flToeStrength" );
			effect.ToeNumerator = tonemap.GetFloat( "m_flToeNum" );
			effect.ToeDenominator = tonemap.GetFloat( "m_flToeDenom" );
			effect.WhitePoint = tonemap.GetFloat( "m_flWhitePoint" );
		}

		if ( data.GetBool( "m_bHasBloomParams" ) && data.GetObject( "m_bloomParams" ) is { } bloom )
		{
			effect.BloomMode = bloom.GetString( "m_blendMode" ) == "BLOOM_BLEND_SCREEN" ? DeadlockPostProcess.BloomBlend.Screen : DeadlockPostProcess.BloomBlend.Add;
			effect.BloomStrength = bloom.GetFloat( "m_flBloomStrength" );
			effect.BloomThreshold = bloom.GetFloat( "m_flBloomThreshold", 1 );
		}
		else
		{
			effect.BloomStrength = 0;
		}

		if ( data.GetBool( "m_bHasVignetteParams" ) && data.GetObject( "m_vignetteParams" ) is { } vignette )
		{
			var center = vignette.GetFloatArray( "m_vCenter" );
			var tint = vignette.GetFloatArray( "m_vColorTint" );

			effect.VignetteStrength = vignette.GetFloat( "m_flVignetteStrength" );
			effect.VignetteCenter = center.Length >= 2 ? new Vector2( center[0], center[1] ) : Vector2.Zero;
			effect.VignetteRadius = vignette.GetFloat( "m_flRadius" );
			effect.VignetteRoundness = vignette.GetFloat( "m_flRoundness" );
			effect.VignetteFeather = vignette.GetFloat( "m_flFeather" );
			effect.VignetteTint = tint.Length >= 3 ? new Color( tint[0], tint[1], tint[2] ) : Color.White;
		}
		else
		{
			effect.VignetteStrength = 0;
		}
	}
}
