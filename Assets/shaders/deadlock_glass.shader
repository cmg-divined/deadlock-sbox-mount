//=========================================================================================================================
// Deadlock's pbr.vfx with F_GLASS: windows and display cases. The glass is drawn opaque, after everything behind it,
// with what's behind it composited in: the part of the light the reflection leaves goes through, tinted by the glass
// colour the more steeply it's looked through, while the rest of the surface is lit as ordinary pbr.
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

	Texture2D g_tColor < Channel( RGBA, Box( DeadlockColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormalRoughness < Channel( RGBA, Box( DeadlockNormalRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tTintMask < Channel( RGBA, Box( DeadlockTintMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tGlass < Channel( RGBA, Box( DeadlockGlass ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

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

	float3 ApplyColorMatrix( float3 color, float4 r, float4 g, float4 b )
	{
		float4 c = float4( color, 1 );
		return float3( dot( r, c ), dot( g, c ), dot( b, c ) );
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

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = albedo;
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

			float2 screenUv = i.vPositionSs.xy * g_vInvViewportSize;
			float3 background = g_tFrameBufferCopyTexture.SampleLevel( g_sBilinearClamp, screenUv * g_vFrameBufferCopyInvSizeAndUvScale.zw, 0 ).rgb;

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
			s.LightmapUV = i.vLightmapUV;
			s.Reflectance = g_flGlassReflectance;
			s.Transmission = glass;
			s.Background = background;

			return DoAtmospherics( m.WorldPosition, m.ScreenPosition.xy, float4( DeadlockShade( s ), 1 ) );
		#endif
	}
}
