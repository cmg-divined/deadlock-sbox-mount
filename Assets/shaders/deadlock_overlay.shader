//=========================================================================================================================
// Deadlock's citadel_overlay: graffiti, signs and grime laid on the world as their own geometry, flush with the surface
// under them. The game blends them into its G-buffer before lighting; here each is lit as the deferred pass would light
// it and blended over what's beneath.
//
// Overlays sit on the surfaces they decorate, so they are pulled a little towards the camera along the view ray, which
// moves them in depth only, to keep them in front.
//=========================================================================================================================

HEADER
{
	Description = "Deadlock citadel_overlay";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
	#define S_TRANSLUCENT 1
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "deadlock_vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vTint : TEXCOORD5;
};

VS
{
	#include "deadlock_vertex.hlsl"

	// How far, as a fraction of the distance to the camera, overlays are pulled towards it.
	#define OVERLAY_DEPTH_OFFSET 0.0005

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessDeadlockVertex( v );

		float3 toCamera = g_vCameraPositionWs - i.vPositionWs.xyz;
		i.vPositionWs.xyz += toCamera * OVERLAY_DEPTH_OFFSET;
		i.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );

		// The object tint, authored in gamma space.
		float4 tint = i.vVertexColor * v.vMeshTint;
		i.vTint = float4( SrgbGammaToLinear( tint.rgb ), tint.a );
		return FinalizeDeadlockVertex( i );
	}
}

PS
{
	// sbox_pixel supplies culling; suppress the duplicate declaration in vr_common_ps_code.
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "deadlock_lighting.hlsl"

	CreateInputTexture2D( DeadlockColor, Srgb, 8, "None", "_color", "Overlay", Default4( 1, 1, 1, 0 ) );
	CreateInputTexture2D( DeadlockNormalRoughness, Linear, 8, "None", "_normal", "Overlay", Default4( 0.5, 0.5, 1, 1 ) );
	CreateInputTexture2D( DeadlockSelfIllumTintMask, Linear, 8, "None", "_mask", "Overlay", Default4( 0, 1, 0, 0 ) );

	Texture2D g_tColor < Channel( RGBA, Box( DeadlockColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormalRoughness < Channel( RGBA, Box( DeadlockNormalRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tSelfIllumTintMask < Channel( RGBA, Box( DeadlockSelfIllumTintMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );

	float3 g_vColorTint < Default3( 1, 1, 1 ); >;
	float2 g_vAlbedoTexcoordUVOffset < Default2( 0, 0 ); >;
	float2 g_vAlbedoScrollSpeed < Default2( 0, 0 ); >;
	float2 g_vNormalAndRoughnessTexcoordUVOffset < Default2( 0, 0 ); >;
	float2 g_vNormalAndRoughnessScrollSpeed < Default2( 0, 0 ); >;
	float2 g_vSelfIllumTexcoordOffset < Default2( 0, 0 ); >;
	float2 g_vSelfIllumScrollSpeed < Default2( 0, 0 ); >;
	float2 g_vColorTranslucencyInputRange < Default2( 0, 1 ); >;
	float2 g_vColorTranslucencyOutputRange < Default2( 0, 1 ); >;
	float3 g_vSelfIllumTint < Default3( 1, 1, 1 ); >;
	float g_flSelfIllumScale < Default( 0 ); >;
	float g_flSelfIllumAlbedoFactor < Default( 1 ); >;

	float RemapTranslucency( float translucency, float2 input, float2 output )
	{
		return saturate( ( translucency - input.x ) / ( input.y - input.x ) ) * ( output.y - output.x ) + output.x;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;

		float4 normalRoughness = g_tNormalRoughness.Sample( g_sAniso, uv + frac( g_vNormalAndRoughnessTexcoordUVOffset + g_vNormalAndRoughnessScrollSpeed * g_flTime ) );
		float4 color = g_tColor.Sample( g_sAniso, uv + frac( g_vAlbedoTexcoordUVOffset + g_vAlbedoScrollSpeed * g_flTime ) );
		float4 selfIllumTintMask = g_tSelfIllumTintMask.Sample( g_sTrilinearWrap, uv + frac( g_vSelfIllumTexcoordOffset + g_vSelfIllumScrollSpeed * g_flTime ) );

		float3 albedo = lerp( color.rgb, color.rgb * g_vColorTint * i.vTint.rgb, selfIllumTintMask.y );
		float translucency = i.vTint.a * color.a;

		float3 illumColor = lerp( g_vSelfIllumTint, g_vSelfIllumTint * albedo, g_flSelfIllumAlbedoFactor );
		albedo = lerp( albedo, illumColor, selfIllumTintMask.x * saturate( g_flSelfIllumScale ) );
		float glow = saturate( selfIllumTintMask.x * g_flSelfIllumScale * 0.1 ) * 10.0;

		float3 normalTs = DecodeHemiOctahedronNormal( normalRoughness.xy );
		float3 normal = TransformNormal( normalTs, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = albedo;
		m.Emission = albedo * glow;
		m.Normal = normal;
		m.GeometricNormal = normalize( i.vNormalWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;
		m.Roughness = saturate( normalRoughness.z );
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.Opacity = RemapTranslucency( translucency, g_vColorTranslucencyInputRange, g_vColorTranslucencyOutputRange );

		float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );

		[branch]
		if ( !DeadlockLightingEnabled() )
			return ShadingModelStandard::Shade( m );

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

		return DoAtmospherics( m.WorldPosition, m.ScreenPosition.xy, float4( DeadlockShade( s ), m.Opacity ) );
	}
}
