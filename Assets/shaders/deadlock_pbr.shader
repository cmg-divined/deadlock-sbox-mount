//=========================================================================================================================
// Deadlock's pbr.vfx, which renders both heroes (F_USE_NPR_LIGHTING) and props.
//
// Textures keep Deadlock's packing: g_tNormalRoughness holds a hemi-octahedral normal in RG and roughness in B.
// Every mask is a texture plus a channel selector set by the mount, so one shader reads both the pbr layout
// (metalness in color alpha, separate mask textures) and the environment layout (packed masks).
//
// In a mounted map, Deadlock Map Lighting lights it the way the game does (deadlock_lighting.hlsl); elsewhere
// s&box's lights do.
//=========================================================================================================================

HEADER
{
	Description = "Deadlock pbr";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Transparency" );
	Feature( F_TRANSLUCENT, 0..1, "Transparency" );
	Feature( F_UNLIT, 0..1, "Deadlock" );
	Feature( F_USE_NPR_LIGHTING, 0..1, "Deadlock" );
	Feature( F_NPR_OUTLINE, 0..1, "Deadlock" );
	Feature( F_NPR_SOLID_OUTLINE, 0..1, "Deadlock" );
	Feature( F_JITTER_VERTICES, 0..2 (0="No", 1="World Space", 2="Object Space"), "Deadlock" );
	Feature( F_WRITE_DEPTH_BEFORE_ALPHA_BLENDING, 0..1, "Transparency" );
	Feature( F_BLOOM_LAYER, 0..1, "Deadlock" );
	FeatureRule( Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT ), "Additive requires translucency" );
	FeatureRule( Requires1( F_NPR_OUTLINE, F_USE_NPR_LIGHTING ), "The NPR outline is for NPR materials" );
	FeatureRule( Requires1( F_NPR_SOLID_OUTLINE, F_NPR_OUTLINE ), "The solid outline is an outline pass" );
	FeatureRule( Allow1( F_NPR_SOLID_OUTLINE, F_TRANSLUCENT ), "The solid outline blends on its own" );
	FeatureRule( Requires1( F_WRITE_DEPTH_BEFORE_ALPHA_BLENDING, F_TRANSLUCENT ), "Writing depth is for translucent materials" );
	FeatureRule( Allow1( F_BLOOM_LAYER, F_TRANSLUCENT ), "Bloom amounts are for opaque materials" );
	FeatureRule( Allow1( F_BLOOM_LAYER, F_NPR_OUTLINE ), "The outline doesn't bloom" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
	#define S_UV2 1
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
	// pbr.vfx's outline pass, drawn by a second renderer (Deadlock NPR Outline) with copies of the materials.
	StaticCombo( S_NPR_OUTLINE, F_NPR_OUTLINE, Sys( ALL ) );
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "deadlock_vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vMeshColor : COLOR0;
	float4 vMeshTint : TEXCOORD5;
	#if S_NPR_OUTLINE
		float flNprOutlineMask : TEXCOORD9;
	#endif
};

VS
{
	#include "deadlock_vertex.hlsl"

	#if S_NPR_OUTLINE
		CreateInputTexture2D( DeadlockNprOutlineMask, Linear, 8, "None", "_mask", "Deadlock", Default4( 1, 0, 0, 0 ) );
		Texture2D g_tNprOutlineMask < Channel( RGBA, Box( DeadlockNprOutlineMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
		float g_flNprOutlineThickness < Default( 0.1 ); >;
	#endif

	// pbr.vfx's vertex jitter: the vertices wobble by two waves through their world (1) or object (2) position.
	StaticCombo( S_JITTER_VERTICES, F_JITTER_VERTICES, Sys( ALL ) );

	#if S_JITTER_VERTICES
		CreateInputTexture2D( DeadlockJitterMask, Linear, 8, "None", "_mask", "Deadlock", Default4( 1, 1, 1, 1 ) );
		Texture2D g_tJitterMask < Channel( RGBA, Box( DeadlockJitterMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
		float g_flJitterMaskSecondaryUv < Default( 0 ); >;
		float g_flJitterSynchronize < Default( 1 ); >;
		float g_flJitterQuantizeTime < Default( 0 ); >;
		float g_flJitterQuantizeDisplacement < Default( 0 ); >;
		float3 g_vJitterExpression < Default3( 0, 0, 0 ); >;
		float3 g_vJitterFrequenciesA < Default3( 0.02, 0.04, 0.08 ); >;
		float3 g_vJitterAmplitudesXA < Default3( 4, 4, 4 ); >;
		float3 g_vJitterAmplitudesYA < Default3( 0, 0, 0 ); >;
		float3 g_vJitterAmplitudesZA < Default3( 0, 0, 0 ); >;
		float g_flJitterSpeedA < Default( 10 ); >;
		float3 g_vJitterFrequenciesB < Default3( 0.05, 0.09, 0.17 ); >;
		float3 g_vJitterAmplitudesXB < Default3( 0, 0, 0 ); >;
		float3 g_vJitterAmplitudesYB < Default3( 0, 0, 0 ); >;
		float3 g_vJitterAmplitudesZB < Default3( 0, 0, 0 ); >;
		float g_flJitterSpeedB < Default( 7 ); >;

		// A triangle wave through the position, scrolling with time, smoothed and centred on 0 (-0.5 to 0.5).
		float3 JitterWave( float3 position, float3 frequencies, float time, float speed )
		{
			float3 wave = abs( frac( position * abs( frequencies ) + sign( frequencies ) * time * speed ) * 2.0 - 1.0 );
			return wave * wave * ( 3.0 - 2.0 * wave ) - 0.5;
		}

		// Each axis moves by its amplitudes' weighting of the two waves' three components.
		float3 JitterDisplacement( float3 position, float time )
		{
			float3 a = JitterWave( position, g_vJitterFrequenciesA, time, g_flJitterSpeedA );
			float3 b = JitterWave( position, g_vJitterFrequenciesB, time, g_flJitterSpeedB );

			float3 displacement = float3( dot( g_vJitterAmplitudesXA, a ), dot( g_vJitterAmplitudesYA, a ), dot( g_vJitterAmplitudesZA, a ) )
				+ float3( dot( g_vJitterAmplitudesXB, b ), dot( g_vJitterAmplitudesYB, b ), dot( g_vJitterAmplitudesZB, b ) )
				+ g_vJitterExpression;

			if ( g_flJitterQuantizeDisplacement != 0.0 )
				displacement = round( displacement / g_flJitterQuantizeDisplacement ) * g_flJitterQuantizeDisplacement;

			return displacement;
		}

		void ApplyJitter( VertexInput v, inout PixelInput i )
		{
			float3x4 objectToWorld = GetTransformMatrix( v.nInstanceTransformID, v.nBoneIndex.x );

			// Unsynchronized objects each start somewhere else in time; the game seeds this per instance, here by
			// where the object is.
			float time = g_flTime;
			if ( g_flJitterSynchronize <= 0.5 )
			{
				float3 origin = float3( objectToWorld[0][3], objectToWorld[1][3], objectToWorld[2][3] );
				time += frac( sin( dot( origin, float3( 12.9898, 78.233, 37.719 ) ) ) * 43758.5453 ) * 8192.0;
			}

			if ( g_flJitterQuantizeTime != 0.0 )
				time = round( time / g_flJitterQuantizeTime ) * g_flJitterQuantizeTime;

			float2 maskUv = g_flJitterMaskSecondaryUv > 0.5 ? i.vTextureCoords.zw : i.vTextureCoords.xy;
			float mask = g_tJitterMask.SampleLevel( g_sBilinearWrap, maskUv, 0 ).r;

			#if S_JITTER_VERTICES == 2
				float3 displacement = mul( objectToWorld, float4( JitterDisplacement( v.vPositionOs.xyz, time ), 0.0 ) ).xyz;
			#else
				float3 displacement = JitterDisplacement( i.vPositionWs.xyz, time );
			#endif

			i.vPositionWs.xyz += displacement * mask;
			i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );
		}
	#endif

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessDeadlockVertex( v );
		i.vMeshColor = v.vMeshColor;
		i.vMeshTint = v.vMeshTint;

		#if S_JITTER_VERTICES
			ApplyJitter( v, i );
		#endif

		#if S_NPR_OUTLINE
			// The model pushed out along its normals by the outline mask; its back faces are the outline.
			float mask = g_tNprOutlineMask.SampleLevel( g_sBilinearWrap, i.vTextureCoords.xy, 0 ).r;
			i.vPositionWs.xyz += normalize( i.vNormalWs.xyz ) * ( mask * g_flNprOutlineThickness );
			i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );
			i.flNprOutlineMask = mask;
		#endif

		return FinalizeDeadlockVertex( i );
	}
}

PS
{
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );
	StaticCombo( S_UNLIT, F_UNLIT, Sys( ALL ) );
	StaticCombo( S_USE_NPR_LIGHTING, F_USE_NPR_LIGHTING, Sys( ALL ) );
	StaticCombo( S_NPR_SOLID_OUTLINE, F_NPR_SOLID_OUTLINE, Sys( ALL ) );
	StaticCombo( S_WRITE_DEPTH_BEFORE_ALPHA_BLENDING, F_WRITE_DEPTH_BEFORE_ALPHA_BLENDING, Sys( ALL ) );

	// The bloom layer copy (Deadlock Bloom Layer): s&box's quarter resolution bloom layer, which the post processing
	// blooms without a threshold, gets what the game's bloom amount override blooms.
	StaticCombo( S_BLOOM_LAYER, F_BLOOM_LAYER, Sys( ALL ) );

	// The solid outline multiplies what's behind it, so it draws with the translucent objects, after the scene.
	#if S_NPR_SOLID_OUTLINE
		#define BLEND_MODE_ALREADY_SET 1
	#endif

	#if S_NPR_SOLID_OUTLINE || S_WRITE_DEPTH_BEFORE_ALPHA_BLENDING || S_BLOOM_LAYER
		#define DEPTH_STATE_ALREADY_SET 1
	#endif

	// sbox_pixel supplies culling; suppress the duplicate declaration in vr_common_ps_code.
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "deadlock_lighting.hlsl"

	#if S_NPR_SOLID_OUTLINE
		BoolAttribute( translucent, true );
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, DEST_COLOR );
		RenderState( DstBlend, ZERO );
		RenderState( DepthEnable, true );
		RenderState( DepthFunc, GREATER_EQUAL );
		RenderState( DepthWriteEnable, false );
	#endif

	// The game lays these translucent surfaces' depth down before blending them (ghosts, smoke), so only their
	// nearest layer shows. In one pass, writing depth while blending keeps out any layer drawn after a nearer one.
	#if S_WRITE_DEPTH_BEFORE_ALPHA_BLENDING
		RenderState( DepthEnable, true );
		RenderState( DepthFunc, GREATER_EQUAL );
		RenderState( DepthWriteEnable, true );
	#endif

	// The bloom layer's depth is point sampled down from the scene's, which an exact copy of a surface flickers
	// against; the pixel shader tests the full resolution depth instead.
	#if S_BLOOM_LAYER
		RenderState( DepthEnable, false );
		RenderState( DepthWriteEnable, false );
	#endif

	// pbr.vfx's F_OVERRIDE_BLOOM_AMOUNT: these pixels bloom by their colour times amount times self illumination
	// mask (up to 20), with no threshold. The bloom layer is 10 bit, so it's written as a fraction of its range.
	float g_flBloomAmount < Default( 0 ); >;
	#define BLOOM_LAYER_RANGE 64.0

	//
	// Textures
	//
	CreateInputTexture2D( DeadlockColor, Srgb, 8, "None", "_color", "Deadlock", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( DeadlockNormalRoughness, Linear, 8, "None", "_normal", "Deadlock", Default4( 0.5, 0.5, 1, 1 ) );
	CreateInputTexture2D( DeadlockMetalness, Linear, 8, "None", "_mask", "Deadlock", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( DeadlockTintMask, Linear, 8, "None", "_mask", "Deadlock", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( DeadlockSelfIllumMask, Linear, 8, "None", "_mask", "Deadlock", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( DeadlockAmbientOcclusion, Linear, 8, "None", "_mask", "Deadlock", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( DeadlockRimLightMask, Linear, 8, "None", "_mask", "Deadlock", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( DeadlockTransmissiveColor, Srgb, 8, "None", "_color", "Deadlock", Default4( 0, 0, 0, 1 ) );

	Texture2D g_tColor < Channel( RGBA, Box( DeadlockColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormalRoughness < Channel( RGBA, Box( DeadlockNormalRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tMetalness < Channel( RGBA, Box( DeadlockMetalness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tTintMask < Channel( RGBA, Box( DeadlockTintMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tSelfIllumMask < Channel( RGBA, Box( DeadlockSelfIllumMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tAmbientOcclusion < Channel( RGBA, Box( DeadlockAmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tRimLightMask < Channel( RGBA, Box( DeadlockRimLightMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tNprTransmissiveColor < Channel( RGBA, Box( DeadlockTransmissiveColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor );

	// Each mask reads dot( texel, channel ); an all zero channel uses the fallback instead.
	float4 g_vMetalnessChannel < Default4( 0, 0, 0, 0 ); >;
	float g_flMetalnessFallback < Default( 0 ); >;
	float4 g_vTintMaskChannel < Default4( 0, 0, 0, 0 ); >;
	float g_flTintMaskFallback < Default( 1 ); >;
	float4 g_vSelfIllumMaskChannel < Default4( 0, 0, 0, 0 ); >;
	float g_flSelfIllumMaskFallback < Default( 1 ); >;
	float4 g_vAmbientOcclusionChannel < Default4( 0, 0, 0, 0 ); >;
	float g_flAmbientOcclusionSecondaryUv < Default( 0 ); >;
	float4 g_vRimLightMaskChannel < Default4( 0, 0, 0, 0 ); >;

	//
	// Albedo: color correction and tint, as rows of the 3x4 matrices Deadlock builds from the material
	//
	float4 g_vAlbedoCorrectR < Default4( 1, 0, 0, 0 ); >;
	float4 g_vAlbedoCorrectG < Default4( 0, 1, 0, 0 ); >;
	float4 g_vAlbedoCorrectB < Default4( 0, 0, 1, 0 ); >;
	float4 g_vColorTintR < Default4( 1, 0, 0, 0 ); >;
	float4 g_vColorTintG < Default4( 0, 1, 0, 0 ); >;
	float4 g_vColorTintB < Default4( 0, 0, 1, 0 ); >;
	float g_flMaskColorTint < Default( 1 ); >;
	float g_flMaskVertexColorTint < Default( 1 ); >;
	float g_flVertexColorStrength < Default( 1 ); >;
	float g_flApplyTintToVertexColors < Default( 0 ); >;

	//
	// Texture coordinates: scroll, then scale (xy) and offset (zw) around a rotation about the center
	//
	float2 g_vAlbedoScrollSpeed < Default2( 0, 0 ); >;
	float g_flAlbedoScrollQuantize < Default( 0 ); >;
	float4 g_vAlbedoTransform < Default4( 1, 1, 0, 0 ); >;
	float g_flAlbedoRotation < Default( 0 ); >;
	float2 g_vNormalScrollSpeed < Default2( 0, 0 ); >;
	float g_flNormalScrollQuantize < Default( 0 ); >;
	float4 g_vNormalTransform < Default4( 1, 1, 0, 0 ); >;
	float g_flNormalRotation < Default( 0 ); >;

	//
	// Self illumination
	//
	float g_flSelfIllum < Default( 0 ); >;
	float3 g_vSelfIllumTint < Default3( 1, 1, 1 ); >;
	float g_flSelfIllumScale < Default( 0 ); >;
	float g_flSelfIllumAlbedoFactor < Default( 1 ); >;
	float3 g_vSelfIllumFresnelTint < Default3( 1, 1, 1 ); >;
	float g_flSelfIllumFresnelExponent < Default( 5 ); >;
	float2 g_vSelfIllumFresnelRange < Default2( 0, 0 ); >;
	float g_flSelfIllumSecondaryUv < Default( 0 ); >;
	float2 g_vSelfIllumScrollSpeed < Default2( 0, 0 ); >;
	float g_flSelfIllumScrollQuantize < Default( 0 ); >;
	float4 g_vSelfIllumTransform < Default4( 1, 1, 0, 0 ); >;
	float g_flSelfIllumRotation < Default( 0 ); >;

	//
	// Transparency
	//
	float g_flAlphaAnglePower < Default( 0 ); >;

	// Cut-outs grow more opaque with distance, up to the strength at the boost distance, so foliage keeps its body.
	float g_flAlphaBoostStrength < Default( 3 ); >;
	float g_flAlphaBoostDistance < Default( 20000 ); >;

	// Back faces normally flip the normal; foliage cards keep their front face's.
	float g_flUseFrontFaceNormalsForBackFaces < Default( 0 ); >;

	//
	// Fog: how much of each kind of fog the material takes (sky clouds keep most of their own colour)
	//
	float g_flGradientFogAmount < Default( 1 ); >;
	float g_flCubemapFogAmount < Default( 1 ); >;
	float g_flVolumeFogAmount < Default( 1 ); >;

	// The outline pass: lit with the albedo pulled towards the outline colours (npr_dummy.vmat's), or a solid tint
	// multiplied onto what's behind. g_flNprSolidOutline marks the materials whose outline copy takes the solid variant.
	float g_flNprOutlineEnabled < Default( 1 ); >;
	float g_flNprOutlineSelfDepth < Default( 4 ); >;
	float4 g_vNprOutlineDarkColor < Default4( 0.003035, 0.0003035, 0.0003035, 0.74902 ); >;
	float4 g_vNprOutlineBrightColor < Default4( 0.003035, 0.0003035, 0.0003035, 0.74902 ); >;
	float g_flNprSolidOutline < Default( 0 ); >;
	float3 g_vNprSolidOutlineTint < Default3( 0.01033, 0.01033, 0.01033 ); >;
	float g_flUnlitAdditiveScale < Default( 1 ); >;

	//
	// Non-photorealistic lighting. Deadlock sets these for every hero from materials/dev/npr_dummy.vmat; the
	// defaults are its values.
	//
	float g_flNprDirectLightWrap < Default( 0.8 ); Range( 0.01, 1 ); UiGroup( "NPR" ); >;
	float g_flNprDiffuseStepSharpness < Default( 0.9 ); Range( 0.01, 0.99 ); UiGroup( "NPR" ); >;
	float g_flNprDiffusePbrBlend < Default( 0.25 ); Range( 0, 1 ); UiGroup( "NPR" ); >;
	float g_flNprBounceDiffuse < Default( 1 ); Range( 0, 1 ); UiGroup( "NPR" ); >;
	float g_flNprPrimaryLightCameraWeight < Default( 0.35 ); Range( 0, 1 ); UiGroup( "NPR" ); >;
	float g_flNprExposureControl < Default( 1 ); Range( 0, 1 ); UiGroup( "NPR" ); >;
	float3 g_vNprExposureTargets < Default3( 1, 0.5, 0.1 ); UiGroup( "NPR" ); >;
	float g_flNprExposureControlPbrBlend < Default( 0 ); Range( 0, 1 ); UiGroup( "NPR" ); >;
	float g_flNprRimLightStrength < Default( 2 ); Range( 0, 3 ); UiGroup( "NPR" ); >;
	float g_flNprRimLightWrap < Default( 0.7 ); Range( 0.01, 0.99 ); UiGroup( "NPR" ); >;
	float g_flNprRimLightFalloff < Default( 0.7 ); Range( 0.05, 2 ); UiGroup( "NPR" ); >;
	float2 g_vNprRimLightUpRamp < Default2( 0.2, 0.2 ); UiGroup( "NPR" ); >;
	float g_flNprRimDepthOcclusion < Default( 1 ); Range( 0, 1 ); UiGroup( "NPR" ); >;
	float g_flNprRimOcclusionSampleDistance < Default( 0.5 ); Range( 0.5, 10 ); UiGroup( "NPR" ); >;
	float g_flNprRimOcclusionDepthToleranceMin < Default( 2.5 ); Range( 0.5, 10 ); UiGroup( "NPR" ); >;
	float g_flNprRimOcclusionDepthToleranceRange < Default( 0.5 ); Range( 0.5, 10 ); UiGroup( "NPR" ); >;

	// The exposure Deadlock Post Process applies, which the NPR bounce light is exposed against.
	float g_flDeadlockExposure < Attribute( "DeadlockExposure" ); Default( 0 ); >;

	float2 ScrollUv( float2 uv, float2 speed, float quantize )
	{
		float time = quantize != 0 ? round( g_flTime / quantize ) * quantize : g_flTime;
		return uv + frac( speed * time );
	}

	float2 TransformUv( float2 uv, float4 scaleOffset, float rotation )
	{
		float angle = radians( rotation );
		float s = sin( angle );
		float c = cos( angle );
		float2 centered = uv - 0.5;
		float2 rotated = float2( c * centered.x - s * centered.y, s * centered.x + c * centered.y );
		return scaleOffset.xy * rotated + 0.5 + scaleOffset.zw;
	}

	float SampleMask( Texture2D tex, float2 uv, float4 channel, float fallback )
	{
		if ( dot( channel, float4( 1, 1, 1, 1 ) ) <= 0 )
			return fallback;

		return dot( tex.Sample( g_sAniso, uv ), channel );
	}

	float3 ApplyColorMatrix( float3 color, float4 r, float4 g, float4 b )
	{
		float4 c = float4( color, 1 );
		return float3( dot( r, c ), dot( g, c ), dot( b, c ) );
	}

	// DecodeHemiOctahedronNormal comes from vr_common.fxc and matches Deadlock's decode.

	// DoAtmospherics with the material's share of each kind of fog: the game scales each fog's opacity by it.
	float4 DeadlockAtmospherics( float3 positionWs, float2 positionSs, float4 color, bool additive = false )
	{
		if ( !g_bFogEnabled )
			return color;

		float3 positionToCameraWs = positionWs - g_vCameraPositionWs;

		// Blending additively, what's behind already has the fog, so the fog in front fades this out instead.
		if ( additive )
		{
			if ( g_bGradientFogEnabled )
				color.a *= 1.0 - CalculateGradientFog( positionWs, positionToCameraWs ).a * g_flGradientFogAmount;

			if ( g_bCubemapFogEnabled )
				color.a *= 1.0 - CalculateCubemapFog( positionWs, positionToCameraWs ).a * g_flCubemapFogAmount;

			if ( g_bVolumetricFogEnabled )
				color.a *= lerp( 1.0, CalculateVolumetricFog( positionWs, positionSs ).a, g_flVolumeFogAmount );

			return color;
		}

		color.rgb = lerp( color.rgb, ApplyGradientFog( color.rgb, positionWs, positionToCameraWs ), g_flGradientFogAmount );
		color.rgb = lerp( color.rgb, ApplyCubemapFog( color.rgb, positionWs, positionToCameraWs ), g_flCubemapFogAmount );
		color.rgb = lerp( color.rgb, ApplyVolumetricFog( color.rgb, positionWs, positionSs ), g_flVolumeFogAmount );
		return color;
	}

	//
	// NPR lighting
	//

	// Symmetric S-curve about 0.5; k is the hardness, 1 being a straight line.
	float NprGain( float x, float k )
	{
		bool upper = x > 0.5;
		float folded = upper ? 1.0 - x : x;
		float curved = pow( 0.5, 1.0 - k ) * pow( max( folded, 0.0 ), k );
		return upper ? 1.0 - curved : curved;
	}

	// One light's diffuse term: the light wrapped around the terminator and pushed through the S-curve,
	// normalised by the wrap, blended back towards N.L.
	float NprDiffuse( float flNDotL, float flRoughness )
	{
		float NdotL = saturate( flNDotL );
		float wrapped = saturate( 0.5 + ( ( g_flNprDirectLightWrap - 0.5 ) + NdotL - 0.5 ) * 2.0 );
		float toon = NprGain( wrapped, 1.0 / ( 1.0 - g_flNprDiffuseStepSharpness ) );

		return lerp( toon / ( 2.0 * g_flNprDirectLightWrap ), NdotL, g_flNprDiffusePbrBlend );
	}

	// The direction the bounce light is banded against: up, leaning towards the camera.
	float3 NprPrimaryLightDirection( float3 cameraForward )
	{
		float3 towardsCamera = normalize( float3( -cameraForward.xy, 1e-8 ) );
		return normalize( lerp( float3( 0, 0, 1 ), towardsCamera, g_flNprPrimaryLightCameraWeight ) );
	}

	// How lit the bounce light is (0 to 1), banded: half Lambert against the primary direction, its lit half
	// shadowed by the sun, darkened by the occlusion, through the same S-curve as the direct light.
	float NprBounceBand( float3 primaryLight, float3 normal, float sunVisibility, float occlusion, out float banded )
	{
		float halfLambert = 0.5 + 0.5 * dot( primaryLight, normal );
		if ( halfLambert > 0.5 )
			halfLambert = lerp( 0.5, halfLambert, sunVisibility );

		float stepped = saturate( halfLambert * occlusion * 2.0 - 0.5 );
		float band = frac( stepped );
		banded = ( stepped - band ) + NprGain( band, 1.0 / ( 1.0 - g_flNprDiffuseStepSharpness ) );
		return ( banded + 0.5 ) * 0.5;
	}

	// The normal the bounce light is looked up along: down, horizontal towards the camera, or up, by the band.
	float3 NprBounceNormal( float lit, float banded, float3 toCamera )
	{
		float3 horizontal = normalize( float3( toCamera.xy, 0.0 ) + float3( 0.0001, 0, 0 ) );
		float3 bounceNormal = lit < 0.5
			? lerp( float3( 0, 0, -1 ), horizontal, banded + 0.5 )
			: lerp( horizontal, float3( 0, 0, 1 ), banded - 0.5 );

		return normalize( bounceNormal );
	}

	float3 RgbToXyY( float3 rgb )
	{
		float x = dot( float3( 0.4124, 0.3576, 0.1805 ), rgb );
		float y = dot( float3( 0.2126, 0.7152, 0.0722 ), rgb );
		float z = dot( float3( 0.0193, 0.1192, 0.9505 ), rgb );
		float sum = x + y + z;
		return sum > 0.0 ? float3( float2( x, y ) / sum, y ) : 0.0;
	}

	// Exposure control: the bounce light keeps its colour (towards the average's as it darkens) but takes a fixed
	// brightness after exposure, brighter facing up than down, so heroes read the same in any light.
	float3 NprExposureControl( float3 color, float3 averageXyY, float targetLuminance )
	{
		float3 colorXyY = RgbToXyY( color );
		float2 chroma = lerp( averageXyY.xy, colorXyY.xy, saturate( colorXyY.z / averageXyY.z ) );
		if ( chroma.x == 0.0 || chroma.y == 0.0 || targetLuminance == 0.0 )
			return lerp( 0.0, color, g_flNprExposureControlPbrBlend );

		float scale = targetLuminance / chroma.y;
		float3 xyz = float3( scale * chroma.x, targetLuminance, scale * ( 1.0 - chroma.x - chroma.y ) );
		float3 rgb = float3(
			dot( float3( 3.2406, -1.5372, -0.4986 ), xyz ),
			dot( float3( -0.9689, 1.8758, 0.0415 ), xyz ),
			dot( float3( 0.0557, -0.2040, 1.0570 ), xyz ) );

		return lerp( rgb, color, g_flNprExposureControlPbrBlend );
	}

	// Ambient occlusion with the light it bounces back, by albedo (Jimenez's fit).
	float3 NprMultiBounce( float3 albedo, float occlusion )
	{
		float3 a = albedo * 2.0404 - 0.3324;
		float3 b = albedo * 4.7951 - 0.6417;
		float3 c = albedo * 2.7552 + 0.6903;
		return ( a * occlusion - b ) * occlusion * occlusion + c * occlusion;
	}

	// The rim light: wrapped around the silhouette, only on parts facing up past the ramp. The outline pass has none.
	float NprRim( float3 normal, float3 fromCamera, float occlusion, float rimMask )
	{
		#if S_NPR_OUTLINE
			return 0.0;
		#endif

		float wrapScale = 1.0 + g_flNprRimLightWrap;
		float rim = pow( saturate( ( dot( normal, fromCamera ) + g_flNprRimLightWrap ) / ( wrapScale * wrapScale ) ), g_flNprRimLightFalloff );

		float range = g_vNprRimLightUpRamp.y - g_vNprRimLightUpRamp.x;
		rim *= range > 0.0 ? saturate( ( normal.z - g_vNprRimLightUpRamp.x ) / range ) : ( normal.z > g_vNprRimLightUpRamp.x ? 1.0 : 0.0 );

		return rim * g_flNprRimLightStrength * occlusion * rimMask;
	}

	// The rim only shows where what's behind the silhouette is farther away: the depth a little outside the surface
	// (along its normal, half towards up) against its own.
	float NprRimDepthOcclusion( float3 positionWs, float3 normalWs, float2 screenPosition )
	{
		if ( g_flNprRimDepthOcclusion <= 0.0 )
			return 1.0;

		float3 outside = positionWs + normalize( lerp( normalWs, float3( 0, 0, 1 ), 0.5 ) ) * g_flNprRimOcclusionSampleDistance;
		float4 clip = Position3WsToPs( outside );
		float2 uv = clip.xy / clip.w * float2( 0.5, -0.5 ) + 0.5;
		float2 samplePixel = clamp( uv * g_vViewportSize + g_vViewportOffset, g_vViewportOffset, g_vViewportOffset + g_vViewportSize - 1.0 );

		float behind = Depth::GetLinear( samplePixel );
		float here = Depth::GetLinear( screenPosition );
		return smoothstep( 0.0, 1.0, saturate( ( behind - ( g_flNprRimOcclusionDepthToleranceMin + here ) ) / g_flNprRimOcclusionDepthToleranceRange ) );
	}

	float3 AmbientDiffuse( CombinerInput combiner, float3 normal )
	{
		LightingTerms_t terms = InitLightingTerms();
		combiner.vNormalWs = normal;
		CalculateIndirectLighting( terms, combiner );
		return terms.vIndirectDiffuse.rgb;
	}

	// Heroes lit by s&box's lights (no Deadlock map lighting): the game's NPR model on s&box's lighting terms.
	float4 ShadeNpr( Material m, float rimMask, float3 transmissive )
	{
		Decals::Apply( m.WorldPosition, m );
		AdjustAlphaToCoverage( m );

		CombinerInput combiner = ShadingModelStandard::MaterialToCombinerInput( m );

		// Specular fades out on near black albedo.
		float albedoMax = max( m.Albedo.r, max( m.Albedo.g, m.Albedo.b ) );
		combiner.vSpecularColor = lerp( float3( 0.04, 0.04, 0.04 ), m.Albedo, m.Metalness ) * saturate( albedoMax * 25.0 );

		LightingTerms_t lighting = InitLightingTerms();
		ComputeDirectLighting( lighting, combiner );
		CalculateIndirectLighting( lighting, combiner );

		// Direct diffuse, recomputed with the toon curve. Derivatives need uniform control flow, so the receiver
		// normal is taken before any loop.
		const float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );
		float3 directDiffuse = 0;
		float sunVisibility = 1;

		if ( g_DirectionalLightEnabled )
		{
			float3 sunDirection = normalize( -g_DirectionalLightDirection.xyz );

			if ( g_DirectionalLightCascadeCount > 0 )
				sunVisibility = DirectionalLightShadow::GetVisibilityWithOffset( m.WorldPositionWithOffset, receiverNormal, m.ScreenPosition );

			// Baked surfaces receive the sun through the static light loop below.
			if ( !LightmappedLight::UsesLightmaps() && !ProbeLight::UsesProbes() )
				directDiffuse += g_DirectionalLightColor.rgb * NprDiffuse( dot( m.Normal, sunDirection ), m.Roughness ) * sunVisibility;
		}

		uint lightCount = Light::Count( m.ScreenPosition );

		[loop]
		for ( uint index = 0; index < lightCount; index++ )
		{
			Light light = Light::From( m.WorldPosition, m.ScreenPosition, index, m.LightmapUV, receiverNormal );
			if ( light.Attenuation <= 0 )
				continue;

			directDiffuse += light.Color * light.Attenuation * light.Visibility * NprDiffuse( dot( m.Normal, light.Direction ), m.Roughness );
		}

		// Bounce light, looked up along the banded normal; the unlit side picks up the transmissive colour from above.
		float3 view = CalculatePositionToCameraDirWs( m.WorldPosition );
		float3 bounce = lighting.vIndirectDiffuse.rgb;

		if ( g_flNprBounceDiffuse > 0 )
		{
			float banded;
			float lit = NprBounceBand( NprPrimaryLightDirection( g_vCameraDirWs ), m.Normal, sunVisibility, m.AmbientOcclusion, banded );
			float3 npr = AmbientDiffuse( combiner, NprBounceNormal( lit, banded, view ) )
				+ AmbientDiffuse( combiner, float3( 0, 0, 1 ) ) * transmissive * ( 1.0 - lit );

			bounce = lerp( bounce, npr, g_flNprBounceDiffuse );
		}

		bounce *= lerp( 1.0, NprMultiBounce( m.Albedo, m.AmbientOcclusion ), g_flNprDiffusePbrBlend );

		float rim = NprRim( m.Normal, -view, m.AmbientOcclusion, rimMask );

		[branch]
		if ( rim > 0.0 )
			rim *= NprRimDepthOcclusion( m.WorldPosition, m.Normal, m.ScreenPosition.xy );

		float NdotV = saturate( dot( m.Normal, view ) );
		float specularOcclusion = saturate( pow( NdotV + m.AmbientOcclusion, m.Roughness * m.Roughness ) - 1.0 + m.AmbientOcclusion );

		float3 diffuse = ( directDiffuse + bounce ) * combiner.vDiffuseColor.rgb;
		float3 specular = lighting.vSpecular.rgb + lighting.vIndirectSpecular.rgb * specularOcclusion;
		float3 color = diffuse + ( directDiffuse + bounce ) * rim + specular + combiner.vEmissive.rgb;

		if ( DepthNormals::WantsDepthNormals() )
			return DepthNormals::Output( m.Normal, m.Roughness, m.Opacity );

		return DeadlockAtmospherics( m.WorldPosition, m.ScreenPosition.xy, float4( color, m.Opacity ) );
	}

	// Heroes lit by the map's Deadlock lighting, as pbr.vfx's NPR path lights them: the sun and the light2 and particle
	// lights through the toon curve; the probes looked up along the banded normal, held to fixed brightnesses after
	// exposure; reflections normalised by that bounce light; the rim light.
	float4 ShadeNprDeadlock( Material m, float rimMask, float3 transmissive, float3 receiverNormal )
	{
		Decals::Apply( m.WorldPosition, m );
		AdjustAlphaToCoverage( m );

		if ( DepthNormals::WantsDepthNormals() )
			return DepthNormals::Output( m.Normal, m.Roughness, m.Opacity );

		// In the map's space, like the lighting.
		float3 toCamera = DeadlockDirectionToMap( CalculatePositionToCameraDirWs( m.WorldPosition ) );
		float3 position = DeadlockToMap( m.WorldPosition );
		float3 normal = DeadlockDirectionToMap( m.Normal );

		float roughness = max( m.Roughness, 0.05 );
		float roughness2 = roughness * roughness;
		float NdotV = saturate( dot( normal, toCamera ) );

		// Specular fades out on near black albedo.
		float albedoMax = max( m.Albedo.r, max( m.Albedo.g, m.Albedo.b ) );
		float3 specularColor = lerp( 0.04, m.Albedo, m.Metalness ) * saturate( albedoMax * 25.0 );
		float3 directMultiScatter = 1.0 + specularColor * ( 2.0 * roughness2 * roughness2 * NdotV );

		float3 reflection = DeadlockReflection( normal, toCamera, roughness );
		float3 irradiance;
		float4 bakedShadows;
		float3 cubeSpecular;
		float specularLuminance;
		DeadlockVolumes( position, normal, reflection, roughness, true, true, irradiance, bakedShadows, cubeSpecular, specularLuminance );

		float3 environmentBrdf;
		float3 multiScatter;
		DeadlockEnvironmentBrdf( roughness, NdotV, specularColor, environmentBrdf, multiScatter );

		//
		// Direct light.
		//
		float3 directDiffuse = 0;
		float3 directSpecular = 0;
		float3 unusedDiffuse = 0;
		float sunVisibility = 1;

		[branch]
		if ( any( g_vDeadlockSunColor > 0.0 ) )
		{
			sunVisibility = 1.0 - dot( bakedShadows, g_vDeadlockSunShadowMask );
			if ( sunVisibility > 0.0 && g_DirectionalLightEnabled && g_DirectionalLightCascadeCount > 0 )
				sunVisibility *= DirectionalLightShadow::GetVisibilityWithOffset( m.WorldPositionWithOffset, receiverNormal, m.ScreenPosition );

			float3 color = g_vDeadlockSunColor * sunVisibility;
			float NdotL = dot( normal, g_vDeadlockSunDirection );
			if ( NdotL > 0.0 )
			{
				directDiffuse += color * NprDiffuse( NdotL, m.Roughness );
				DeadlockDirectLight( normal, toCamera, g_vDeadlockSunDirection, roughness, specularColor, directMultiScatter, color, unusedDiffuse, directSpecular );
			}
		}

		DeadlockGridRange lights = DeadlockGridLookup( position, 1, (uint)g_nDeadlockBarnLightCount );

		[loop]
		for ( uint k = 0; k < lights.Count; k++ )
		{
			DeadlockBarnLight light = g_DeadlockBarnLights[DeadlockGridItem( lights, k )];

			float3 toLight;
			float3 color;
			if ( !DeadlockBarnLightAt( light, position, bakedShadows, toLight, color ) )
				continue;

			float NdotL = dot( normal, toLight );
			if ( NdotL <= 0.0 )
				continue;

			directDiffuse += color * NprDiffuse( NdotL, m.Roughness );
			DeadlockDirectLight( normal, toCamera, toLight, max( roughness, light.Parameters.x ), specularColor, directMultiScatter, color, unusedDiffuse, directSpecular );
		}

		[loop]
		for ( uint p = 0; p < (uint)g_nDeadlockParticleLightCount; p++ )
		{
			DeadlockParticleLight light = g_DeadlockParticleLights[p];

			float3 toLight;
			float3 color;
			if ( !DeadlockParticleLightAt( light, position, toLight, color ) )
				continue;

			float NdotL = dot( normal, toLight );
			if ( NdotL <= 0.0 )
				continue;

			float3 lightSpecular = 0;
			directDiffuse += color * NprDiffuse( NdotL, m.Roughness );
			DeadlockDirectLight( normal, toCamera, toLight, max( roughness, 0.04 ), specularColor, directMultiScatter, color, unusedDiffuse, lightSpecular );
			directSpecular += lightSpecular * light.Color.w;
		}

		//
		// Bounce light: the probes along the banded normal, and from above on the unlit side, through the transmissive
		// colour. With exposure control their brightness is the target for the direction, after exposure.
		//
		float3 bounce = irradiance;
		bool exposureControl = g_flNprExposureControl > 0.0 && g_flDeadlockExposure > 0.0;

		[branch]
		if ( g_flNprBounceDiffuse > 0 )
		{
			float3 primaryLight = NprPrimaryLightDirection( DeadlockDirectionToMap( g_vCameraDirWs ) );
			float banded;
			float lit = NprBounceBand( primaryLight, normal, sunVisibility, m.AmbientOcclusion, banded );
			float3 bounceNormal = NprBounceNormal( lit, banded, toCamera );

			float3 ambient = DeadlockProbeIrradiance( position, bounceNormal );
			float3 above = DeadlockProbeIrradiance( position, float3( 0, 0, 1 ) );

			[branch]
			if ( exposureControl )
			{
				// The six directions' average gives the colour the darker lookups keep.
				float3 average = ( DeadlockProbeIrradiance( position, float3( 1, 0, 0 ) ) + DeadlockProbeIrradiance( position, float3( -1, 0, 0 ) )
					+ DeadlockProbeIrradiance( position, float3( 0, 1, 0 ) ) + DeadlockProbeIrradiance( position, float3( 0, -1, 0 ) )
					+ above + DeadlockProbeIrradiance( position, float3( 0, 0, -1 ) ) ) / 6.0;
				float3 averageXyY = RgbToXyY( average );

				float3 targets = float3( g_vNprExposureTargets.yy, bounceNormal.z > 0.0 ? g_vNprExposureTargets.x : g_vNprExposureTargets.z );
				ambient = NprExposureControl( ambient, averageXyY, dot( targets, bounceNormal * bounceNormal ) / g_flDeadlockExposure );
				above = NprExposureControl( above, averageXyY, g_vNprExposureTargets.x / g_flDeadlockExposure );
			}

			bounce = lerp( bounce, ambient + above * transmissive * ( 1.0 - lit ), g_flNprBounceDiffuse );
		}

		float rim = NprRim( normal, -toCamera, m.AmbientOcclusion, rimMask );

		[branch]
		if ( rim > 0.0 )
			rim *= NprRimDepthOcclusion( m.WorldPosition, m.Normal, m.ScreenPosition.xy );

		// Reflections are normalised by the bounce light's brightness.
		float normalizationCeiling = max( roughness * g_vDeadlockCubemapNormalization.x + g_vDeadlockCubemapNormalization.y, 1.0 ) * ( exposureControl ? 32.0 : 1.0 );
		float normalization = specularLuminance > 0.0 ? min( dot( bounce, DeadlockLuminance ) / specularLuminance, normalizationCeiling ) : normalizationCeiling;
		float specularOcclusion = saturate( pow( NdotV + m.AmbientOcclusion, m.Roughness * m.Roughness ) - 1.0 + m.AmbientOcclusion );

		float3 diffuse = ( directDiffuse + bounce * lerp( 1.0, NprMultiBounce( m.Albedo, m.AmbientOcclusion ), g_flNprDiffusePbrBlend ) ) * m.Albedo * ( 1.0 - m.Metalness );
		float3 specular = directSpecular + environmentBrdf * cubeSpecular * normalization * specularOcclusion;
		float3 color = diffuse + ( directDiffuse + bounce ) * rim + specular + m.Emission;

		return DeadlockAtmospherics( m.WorldPosition, m.ScreenPosition.xy, float4( color, m.Opacity ) );
	}

	// Lit by the map's Deadlock lighting, as the game's deferred lighting pass lights its G-buffer.
	float4 ShadeDeadlock( Material m, float2 lightmapUV, float3 receiverNormal )
	{
		AdjustAlphaToCoverage( m );

		if ( DepthNormals::WantsDepthNormals() )
			return DepthNormals::Output( m.Normal, m.Roughness, m.Opacity );

		DeadlockSurface s = DeadlockSurfaceDefaults();
		s.Position = m.WorldPosition;
		s.PositionWithOffset = m.WorldPositionWithOffset;
		s.ScreenPosition = m.ScreenPosition;
		s.Normal = m.Normal;
		s.ShadowReceiverNormal = receiverNormal;
		s.Albedo = m.Albedo;
		s.Roughness = m.Roughness;
		s.Metalness = m.Metalness;
		s.Emission = m.Emission;
		s.LightmapUV = lightmapUV;

		float3 color = DeadlockShade( s );
		return DeadlockAtmospherics( m.WorldPosition, m.ScreenPosition.xy, float4( color, m.Opacity ) );
	}

	// The surface's colour; bloomMask is how much of it the bloom amount override takes (its self illumination).
	float4 ShadePixel( PixelInput i, out float bloomMask )
	{
		bloomMask = 1.0;

		float2 uv = i.vTextureCoords.xy;
		float2 uv2 = i.vTextureCoords.zw;

		float2 albedoUv = TransformUv( ScrollUv( uv, g_vAlbedoScrollSpeed, g_flAlbedoScrollQuantize ), g_vAlbedoTransform, g_flAlbedoRotation );
		float2 normalUv = TransformUv( ScrollUv( uv, g_vNormalScrollSpeed, g_flNormalScrollQuantize ), g_vNormalTransform, g_flNormalRotation );

		float4 color = g_tColor.Sample( g_sAniso, albedoUv );
		float4 normalRoughness = g_tNormalRoughness.Sample( g_sAniso, normalUv );
		float tintMask = SampleMask( g_tTintMask, albedoUv, g_vTintMaskChannel, g_flTintMaskFallback );

		// The object tint (the renderer's and the draw call's) and the vertex colour are both authored in gamma
		// space. Vertex colour goes either side of the colour correction.
		float4 objectTint = i.vVertexColor * i.vMeshTint;
		float4 vertexColor = float4( SrgbGammaToLinear( objectTint.rgb ) * SrgbGammaToLinear( i.vMeshColor.rgb ), objectTint.a * i.vMeshColor.a );
		float vertexColorAmount = lerp( 1.0, tintMask, g_flMaskVertexColorTint ) * g_flVertexColorStrength;

		float3 albedo = color.rgb;
		if ( g_flApplyTintToVertexColors > 0.5 )
			albedo = lerp( albedo, albedo * vertexColor.rgb, vertexColorAmount );

		albedo = ApplyColorMatrix( albedo, g_vAlbedoCorrectR, g_vAlbedoCorrectG, g_vAlbedoCorrectB );
		albedo = lerp( albedo, ApplyColorMatrix( albedo, g_vColorTintR, g_vColorTintG, g_vColorTintB ), lerp( 1.0, tintMask, g_flMaskColorTint ) );

		if ( g_flApplyTintToVertexColors <= 0.5 )
			albedo = lerp( albedo, albedo * vertexColor.rgb, vertexColorAmount );

		float3 normalTs = DecodeHemiOctahedronNormal( normalRoughness.rg );

		// Back faces flip the vertex normal under the normal map, unless the material keeps the front one.
		float3 vertexNormalWs = i.vNormalWs;
		#if S_RENDER_BACKFACES && !S_NPR_OUTLINE
			if ( !i.face && g_flUseFrontFaceNormalsForBackFaces <= 0.5 )
				vertexNormalWs = -vertexNormalWs;
		#endif

		float3 normalWs = TransformNormal( normalTs, vertexNormalWs, i.vTangentUWs, i.vTangentVWs );

		float2 aoUv = g_flAmbientOcclusionSecondaryUv > 0.5 ? uv2 : uv;
		float ambientOcclusion = saturate( SampleMask( g_tAmbientOcclusion, aoUv, g_vAmbientOcclusionChannel, 1.0 ) );

		#if S_NPR_OUTLINE
		{
			// Only the back faces, around the model, and nothing where the mask is clear.
			bool front = false;
			#if S_RENDER_BACKFACES
				front = i.face;
			#endif

			if ( front || i.flNprOutlineMask <= 0.0 || g_flNprOutlineEnabled <= 0.0 )
				discard;

			// The game keeps outlines off the hero with the stencil: here they stay out of the depth prepass and
			// drop wherever a surface sits just behind them (the model itself), so only the silhouette's remain.
			#if S_MODE_DEPTH
				discard;
			#endif

			float3 outlinePosition = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
			float outlineDepth = dot( outlinePosition - g_vCameraPositionWs, g_vCameraDirWs );
			if ( Depth::GetLinear( i.vPositionSs.xy ) - outlineDepth < g_flNprOutlineSelfDepth )
				discard;

			// Multiplied onto what's behind: the mask fades it out to white, which changes nothing.
			#if S_NPR_SOLID_OUTLINE
				return float4( lerp( 1.0, g_vNprSolidOutlineTint, i.flNprOutlineMask ), 1.0 );
			#endif

			// Brighter facing up, darker in occlusion; the vertex normal, not the normal map's.
			float brightness = ( 0.5 + 0.5 * normalize( i.vNormalWs ).z ) * ambientOcclusion;
			albedo = lerp( lerp( albedo, g_vNprOutlineDarkColor.rgb, g_vNprOutlineDarkColor.a ), lerp( albedo, g_vNprOutlineBrightColor.rgb, g_vNprOutlineBrightColor.a ), brightness );

			// Lit as if facing the camera: the hull's own normals face away, which would light it at grazing angles.
			normalWs = -g_vCameraDirWs;
		}
		#endif

		float3 positionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 view = CalculatePositionToCameraDirWs( positionWs );

		// Self illumination replaces the albedo rather than adding to it.
		float3 emission = 0;
		if ( g_flSelfIllum > 0.5 )
		{
			float2 illumUv = g_flSelfIllumSecondaryUv > 0.5 ? uv2 : uv;
			illumUv = TransformUv( ScrollUv( illumUv, g_vSelfIllumScrollSpeed, g_flSelfIllumScrollQuantize ), g_vSelfIllumTransform, g_flSelfIllumRotation );

			float illumMask = SampleMask( g_tSelfIllumMask, illumUv, g_vSelfIllumMaskChannel, g_flSelfIllumMaskFallback );
			float3 illumTint = lerp( g_vSelfIllumTint, g_vSelfIllumTint * albedo, g_flSelfIllumAlbedoFactor );
			bloomMask = illumMask;

			// Facing the camera keeps the untinted mask, grazing angles take the fresnel tint.
			float fresnel = pow( max( 0.0, dot( view, normalWs ) ), g_flSelfIllumFresnelExponent );
			float range = g_vSelfIllumFresnelRange.y - g_vSelfIllumFresnelRange.x;
			float fresnelBlend = range != 0 ? saturate( ( fresnel - g_vSelfIllumFresnelRange.x ) / range ) : ( fresnel > g_vSelfIllumFresnelRange.x ? 1.0 : 0.0 );
			float3 fresnelMask = lerp( g_vSelfIllumFresnelTint, float3( 1, 1, 1 ), fresnelBlend );

			illumTint *= fresnelMask;
			albedo *= lerp( float3( 1, 1, 1 ), fresnelMask, illumMask );
			albedo = lerp( albedo, illumTint, illumMask * saturate( g_flSelfIllumScale ) );
			emission = albedo * illumMask * g_flSelfIllumScale;
		}

		float opacity = 1;
		#if S_TRANSLUCENT
		{
			// Fades the surface by how squarely it faces the camera; a negative power fades the silhouette instead.
			float2 angleScaleBias = g_flAlphaAnglePower < 0 ? float2( -1, 1 ) : float2( 1, 0 );
			float angleFade = pow( max( 0.001, angleScaleBias.x * abs( dot( view, normalWs ) ) + angleScaleBias.y ), abs( g_flAlphaAnglePower ) );
			opacity = saturate( vertexColor.a * color.a * angleFade * g_flOpacityScale );
		}
		#elif S_ALPHA_TEST
		{
			// The game's cut-out: the angle fade against the camera's facing, the vertex alpha, and the boost.
			float alpha = color.a;
			if ( g_flAlphaAnglePower != 0 )
			{
				float2 angleScaleBias = g_flAlphaAnglePower < 0 ? float2( -1, 1 ) : float2( 1, 0 );
				alpha *= pow( max( 0.001, angleScaleBias.x * abs( dot( g_vCameraDirWs, normalWs ) ) + angleScaleBias.y ), abs( g_flAlphaAnglePower ) );
			}

			float boost = lerp( 1.0, g_flAlphaBoostStrength, saturate( length( positionWs - g_vCameraPositionWs ) / g_flAlphaBoostDistance ) );
			opacity = vertexColor.a * alpha * boost;
		}
		#endif

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = albedo;
		m.Emission = emission;
		m.Normal = normalWs;
		m.TangentNormal = normalTs;
		m.GeometricNormal = vertexNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.LightmapUV = i.vLightmapUV;
		m.TextureCoords = albedoUv;
		m.Roughness = saturate( normalRoughness.b );
		m.Metalness = saturate( SampleMask( g_tMetalness, albedoUv, g_vMetalnessChannel, g_flMetalnessFallback ) );
		m.AmbientOcclusion = ambientOcclusion;
		m.Opacity = opacity;

		#if S_MODE_DEPTH
			return ShadingModelStandard::Shade( m );
		#elif S_UNLIT
			AdjustAlphaToCoverage( m );
			float4 unlit = float4( m.Albedo * ( S_ADDITIVE_BLEND ? g_flUnlitAdditiveScale : 1.0 ), m.Opacity );
			return DeadlockAtmospherics( m.WorldPosition, m.ScreenPosition.xy, unlit, S_ADDITIVE_BLEND != 0 );
		#elif S_USE_NPR_LIGHTING
			float rimMask = SampleMask( g_tRimLightMask, albedoUv, g_vRimLightMaskChannel, 1.0 );
			float3 transmissive = g_tNprTransmissiveColor.Sample( g_sAniso, albedoUv ).rgb;

			// Derivatives need uniform control flow, so the shadow receiver normal is taken before branching.
			float3 nprReceiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );

			[branch]
			if ( DeadlockLightingEnabled() )
				return ShadeNprDeadlock( m, rimMask, transmissive, nprReceiverNormal );

			return ShadeNpr( m, rimMask, transmissive );
		#else
			// Derivatives need uniform control flow, so the shadow receiver normal is taken before branching.
			float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );

			[branch]
			if ( DeadlockLightingEnabled() )
				return ShadeDeadlock( m, i.vLightmapUV, receiverNormal );

			return ShadingModelStandard::Shade( m );
		#endif
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		#if S_BLOOM_LAYER
		{
			// Drawn again into the bloom layer at a quarter of the screen: only where the scene shows this surface.
			if ( g_flBloomAmount <= 0.0 )
				discard;

			float3 position = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
			float depth = dot( position - g_vCameraPositionWs, g_vCameraDirWs );
			if ( Depth::GetLinear( i.vPositionSs.xy * 4.0 ) < depth - max( 4.0, depth * 0.01 ) )
				discard;
		}
		#endif

		float bloomMask;
		float4 result = ShadePixel( i, bloomMask );

		#if S_BLOOM_LAYER
			return float4( result.rgb * min( g_flBloomAmount * bloomMask, 20.0 ) / BLOOM_LAYER_RANGE, 1.0 );
		#else
			return result;
		#endif
	}
}
