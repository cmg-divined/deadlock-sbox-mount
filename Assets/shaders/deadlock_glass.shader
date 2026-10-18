//=========================================================================================================================
// Deadlock's pbr.vfx with F_GLASS: windows and display cases. The glass is drawn opaque, after everything behind it,
// with what's behind it composited in: the part of the light the reflection leaves goes through, tinted by the glass
// colour the more steeply it's looked through, while the rest of the surface is lit as ordinary pbr. What's behind
// is blurred and refracted as the game's glass does it, and the glass can glow (shop signs).
//
// It's lit as the game's deferred lighting pass lights a surface (deadlock_lighting.hlsl).
//=========================================================================================================================

HEADER
{
	Description = "Deadlock pbr glass";
}

FEATURES
{
	#include "common/features.hlsl"
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
	// The glass composites what's behind it itself, so nothing blends.
	#define BLEND_MODE_ALREADY_SET 1
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
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
};

VS
{
	#include "deadlock_vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessDeadlockVertex( v );
		i.vMeshColor = v.vMeshColor;
		i.vMeshTint = v.vMeshTint;
		return FinalizeDeadlockVertex( i );
	}
}

PS
{
	// Drawn with the translucent objects, after what it shows, without blending.
	#define DEPTH_STATE_ALREADY_SET 1
	#define S_TRANSLUCENT 1

	// sbox_pixel supplies culling; suppress the duplicate declaration in vr_common_ps_code.
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "deadlock_lighting.hlsl"

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );

	BoolAttribute( bWantsFBCopyTexture, true );
	Texture2D g_tFrameBufferCopyTexture < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;

	CreateInputTexture2D( DeadlockColor, Srgb, 8, "None", "_color", "Deadlock", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( DeadlockNormalRoughness, Linear, 8, "None", "_normal", "Deadlock", Default4( 0.5, 0.5, 1, 0 ) );
	CreateInputTexture2D( DeadlockTintMask, Linear, 8, "None", "_mask", "Deadlock", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( DeadlockGlass, Linear, 8, "None", "_mask", "Deadlock", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( DeadlockSelfIllumMask, Linear, 8, "None", "_mask", "Deadlock", Default4( 1, 1, 1, 1 ) );

	Texture2D g_tColor < Channel( RGBA, Box( DeadlockColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormalRoughness < Channel( RGBA, Box( DeadlockNormalRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tTintMask < Channel( RGBA, Box( DeadlockTintMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tGlass < Channel( RGBA, Box( DeadlockGlass ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tSelfIllumMask < Channel( RGBA, Box( DeadlockSelfIllumMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );

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
	float g_flGlassReflectance < Default( 0.04 ); >;

	// Self illumination, as deadlock_pbr has it.
	float g_flSelfIllum < Default( 0 ); >;
	float4 g_vSelfIllumMaskChannel < Default4( 0, 0, 0, 0 ); >;
	float g_flSelfIllumMaskFallback < Default( 1 ); >;
	float3 g_vSelfIllumTint < Default3( 1, 1, 1 ); >;
	float g_flSelfIllumScale < Default( 0 ); >;
	float g_flSelfIllumAlbedoFactor < Default( 1 ); >;
	float3 g_vSelfIllumFresnelTint < Default3( 1, 1, 1 ); >;
	float g_flSelfIllumFresnelExponent < Default( 5 ); >;
	float2 g_vSelfIllumFresnelRange < Default2( 0, 0 ); >;
	float g_flSelfIllumSecondaryUv < Default( 0 ); >;
	float2 g_vSelfIllumScrollSpeed < Default2( 0, 0 ); >;
	float g_flSelfIllumScrollQuantize < Default( 0 ); >;

	// What's behind: blurred over a disc this wide (screen fractions) scaled between the factors by roughness, jittered
	// by noise, and refracted along the normal (the game's cloak blur and refraction).
	float g_flGlassBlur < Default( 0.005 ); >;
	float2 g_vGlassBlurRoughnessFactors < Default2( 1, 1 ); >;
	float g_flGlassBlurNoise < Default( 0.5 ); >;
	float g_flGlassRefract < Default( 0.075 ); >;

	float3 ApplyColorMatrix( float3 color, float4 r, float4 g, float4 b )
	{
		float4 c = float4( color, 1 );
		return float3( dot( r, c ), dot( g, c ), dot( b, c ) );
	}

	// Interleaved gradient noise, 0 to 1 per pixel.
	float GlassNoise( float2 pixel )
	{
		return frac( 52.9829189 * frac( dot( pixel, float2( 0.06711056, 0.00583715 ) ) ) );
	}

	// What's behind the glass: nine taps over a disc around a point refracted along the view space normal (more at
	// grazing angles), jittered by noise. Taps landing on something in front of the glass are left out; with none
	// left, it's what's straight behind.
	float3 GlassBackground( float2 screenPosition, float3 normal, float3 toCamera, float roughness, float depth )
	{
		const float2 taps[9] =
		{
			float2( 0, 0 ), float2( -0.0876, 0.9703 ), float2( 0.4802, 0.5651 ), float2( 0.1851, 0.158 ), float2( -0.2616, -0.0617 ),
			float2( -0.5477, -0.6603 ), float2( -0.5325, 0.0711 ), float2( -0.0751, -0.8954 ), float2( 0.6384, -0.4054 ),
		};

		float radius = g_flGlassBlur * lerp( g_vGlassBlurRoughnessFactors.x, g_vGlassBlurRoughnessFactors.y, roughness );
		float2 uv = screenPosition * g_vInvViewportSize;

		float2 refraction = Vector3WsToVs( normal ).xy * float2( 1, -1 ) * g_flGlassRefract * ( 1.4 - saturate( dot( normal, toCamera ) ) );
		float2 noise = float2( GlassNoise( screenPosition ), GlassNoise( screenPosition + 31.0 ) ) * 2.0 - 1.0;
		float2 center = uv + refraction + noise * radius * g_flGlassBlurNoise;

		float4 sum = 0;
		for ( int k = 0; k < 9; k++ )
		{
			float2 tap = saturate( center + taps[k] * radius );
			if ( Depth::GetLinear( tap / g_vInvViewportSize ) < depth - 1.0 )
				continue;

			sum += float4( g_tFrameBufferCopyTexture.SampleLevel( g_sBilinearClamp, tap * g_vFrameBufferCopyInvSizeAndUvScale.zw, 0 ).rgb, 1 );
		}

		if ( sum.w > 0 )
			return sum.rgb / sum.w;

		return g_tFrameBufferCopyTexture.SampleLevel( g_sBilinearClamp, uv * g_vFrameBufferCopyInvSizeAndUvScale.zw, 0 ).rgb;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;

		float4 color = g_tColor.Sample( g_sAniso, uv );
		float4 normalRoughness = g_tNormalRoughness.Sample( g_sAniso, uv );
		float tintMask = g_tTintMask.Sample( g_sAniso, uv ).r;
		float glass = g_tGlass.Sample( g_sAniso, uv ).r;

		// The object tint and the vertex colour, both authored in gamma space, either side of the colour correction.
		float4 objectTint = i.vVertexColor * i.vMeshTint;
		float3 vertexColor = SrgbGammaToLinear( objectTint.rgb ) * SrgbGammaToLinear( i.vMeshColor.rgb );
		float vertexColorAmount = lerp( 1.0, tintMask, g_flMaskVertexColorTint ) * g_flVertexColorStrength;

		float3 albedo = color.rgb;
		if ( g_flApplyTintToVertexColors > 0.5 )
			albedo = lerp( albedo, albedo * vertexColor, vertexColorAmount );

		albedo = ApplyColorMatrix( albedo, g_vAlbedoCorrectR, g_vAlbedoCorrectG, g_vAlbedoCorrectB );
		albedo = lerp( albedo, ApplyColorMatrix( albedo, g_vColorTintR, g_vColorTintG, g_vColorTintB ), lerp( 1.0, tintMask, g_flMaskColorTint ) );

		if ( g_flApplyTintToVertexColors <= 0.5 )
			albedo = lerp( albedo, albedo * vertexColor, vertexColorAmount );

		float3 normalTs = DecodeHemiOctahedronNormal( normalRoughness.rg );
		float3 normalWs = TransformNormal( normalTs, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		#if S_RENDER_BACKFACES
			normalWs *= i.face ? 1 : -1;
		#endif

		float3 positionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 view = CalculatePositionToCameraDirWs( positionWs );

		// Self illumination replaces the albedo rather than adding to it.
		float3 emission = 0;
		if ( g_flSelfIllum > 0.5 )
		{
			float2 illumUv = g_flSelfIllumSecondaryUv > 0.5 ? i.vTextureCoords.zw : uv;
			float quantize = g_flSelfIllumScrollQuantize;
			illumUv += frac( g_vSelfIllumScrollSpeed * ( quantize != 0 ? round( g_flTime / quantize ) * quantize : g_flTime ) );

			float illumMask = dot( g_vSelfIllumMaskChannel, float4( 1, 1, 1, 1 ) ) <= 0 ? g_flSelfIllumMaskFallback : dot( g_tSelfIllumMask.Sample( g_sAniso, illumUv ), g_vSelfIllumMaskChannel );
			float3 illumTint = lerp( g_vSelfIllumTint, g_vSelfIllumTint * albedo, g_flSelfIllumAlbedoFactor );

			float fresnel = pow( max( 0.0, dot( view, normalWs ) ), g_flSelfIllumFresnelExponent );
			float range = g_vSelfIllumFresnelRange.y - g_vSelfIllumFresnelRange.x;
			float fresnelBlend = range != 0 ? saturate( ( fresnel - g_vSelfIllumFresnelRange.x ) / range ) : ( fresnel > g_vSelfIllumFresnelRange.x ? 1.0 : 0.0 );
			float3 fresnelMask = lerp( g_vSelfIllumFresnelTint, float3( 1, 1, 1 ), fresnelBlend );

			illumTint *= fresnelMask;
			albedo *= lerp( float3( 1, 1, 1 ), fresnelMask, illumMask );
			albedo = lerp( albedo, illumTint, illumMask * saturate( g_flSelfIllumScale ) );
			emission = albedo * illumMask * g_flSelfIllumScale;
		}

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = albedo;
		m.Emission = emission;
		m.Normal = normalWs;
		m.GeometricNormal = i.vNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;
		m.Roughness = saturate( normalRoughness.b );
		m.Metalness = saturate( color.a );
		m.AmbientOcclusion = 1;
		m.Opacity = 1;

		#if S_MODE_DEPTH
			return ShadingModelStandard::Shade( m );
		#else
			float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );

			float depth = dot( positionWs - g_vCameraPositionWs, g_vCameraDirWs );
			float3 background = GlassBackground( i.vPositionSs.xy, m.Normal, view, m.Roughness, depth );

			[branch]
			if ( !DeadlockLightingEnabled() )
			{
				m.Opacity = 1.0 - glass;
				float4 lit = ShadingModelStandard::Shade( m );
				return float4( lerp( background, lit.rgb, m.Opacity ), 1 );
			}

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
			s.LightmapUV = i.vLightmapUV;
			s.Reflectance = g_flGlassReflectance;
			s.Transmission = glass;
			s.Background = background;

			return DoAtmospherics( m.WorldPosition, m.ScreenPosition.xy, float4( DeadlockShade( s ), 1 ) );
		#endif
	}
}
