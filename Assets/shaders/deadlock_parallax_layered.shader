//=========================================================================================================================
// Deadlock's environment_parallax_layered: windows. A glass layer over two layers seen through it, a near one (the
// room or street behind) and a far one (the skyline), each shifted by the view angle as if it were set back behind
// the glass, with emissive lights in them. It is opaque; nothing behind the window is drawn.
//
// It builds what the game writes to its G-buffer and is lit as the game's deferred lighting pass lights it.
//=========================================================================================================================

HEADER
{
	Description = "Deadlock environment_parallax_layered";
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
	float4 vPaint : TEXCOORD5;
};

VS
{
	#include "deadlock_vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessDeadlockVertex( v );

		// The object tint times the vertex paint tint, faded in by its alpha; both authored in gamma space.
		float4 tint = i.vVertexColor * v.vMeshTint;
		float3 paint = lerp( 1.0, SrgbGammaToLinear( v.vMeshColor.rgb ), v.vMeshColor.a );

		i.vPaint = float4( SrgbGammaToLinear( tint.rgb ) * paint, tint.a );
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

	CreateInputTexture2D( DeadlockColor, Srgb, 8, "None", "_color", "Glass", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( DeadlockMask, Linear, 8, "None", "_mask", "Glass", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( DeadlockNormalRoughness, Linear, 8, "None", "_normal", "Glass", Default4( 0.5, 0.5, 1, 1 ) );
	CreateInputTexture2D( DeadlockColor2, Srgb, 8, "None", "_color", "Near", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( DeadlockMask2, Linear, 8, "None", "_mask", "Near", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( DeadlockNormalRoughness2, Linear, 8, "None", "_normal", "Near", Default4( 0.5, 0.5, 1, 1 ) );
	CreateInputTexture2D( DeadlockColor3, Srgb, 8, "None", "_color", "Far", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( DeadlockReflectionOcclusionMask, Linear, 8, "None", "_mask", "Glass", Default4( 0, 0, 0, 0 ) );

	Texture2D g_tColor < Channel( RGBA, Box( DeadlockColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tMask < Channel( RGBA, Box( DeadlockMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tNormalRoughness < Channel( RGBA, Box( DeadlockNormalRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tColor2 < Channel( RGBA, Box( DeadlockColor2 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tMask2 < Channel( RGBA, Box( DeadlockMask2 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tNormalRoughness2 < Channel( RGBA, Box( DeadlockNormalRoughness2 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tColor3 < Channel( RGBA, Box( DeadlockColor3 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tReflectionOcclusionMask < Channel( RGBA, Box( DeadlockReflectionOcclusionMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	TextureAttribute( RepresentativeTexture, g_tColor );
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor );

	// Colour correction of each layer, as rows of a 3x4 matrix.
	float4 g_vAlbedoCorrect1R < Default4( 1, 0, 0, 0 ); >;
	float4 g_vAlbedoCorrect1G < Default4( 0, 1, 0, 0 ); >;
	float4 g_vAlbedoCorrect1B < Default4( 0, 0, 1, 0 ); >;
	float4 g_vAlbedoCorrect2R < Default4( 1, 0, 0, 0 ); >;
	float4 g_vAlbedoCorrect2G < Default4( 0, 1, 0, 0 ); >;
	float4 g_vAlbedoCorrect2B < Default4( 0, 0, 1, 0 ); >;
	float4 g_vAlbedoCorrect3R < Default4( 1, 0, 0, 0 ); >;
	float4 g_vAlbedoCorrect3G < Default4( 0, 1, 0, 0 ); >;
	float4 g_vAlbedoCorrect3B < Default4( 0, 0, 1, 0 ); >;

	float g_flVertexColorStrength1 < Default( 1 ); >;
	float g_flVertexColorStrength2 < Default( 1 ); >;
	float g_flVertexColorStrength3 < Default( 1 ); >;

	float3 g_vEmissiveTint2 < Default3( 1, 1, 1 ); >;
	float g_flEmissiveLevel2 < Default( 0 ); >;
	float3 g_vEmissiveTint3 < Default3( 1, 1, 1 ); >;
	float g_flEmissiveLevel3 < Default( 0 ); >;

	// How far behind the glass each layer is, and how steep a view angle still shifts it.
	float g_flLayer2Offset < Default( 0 ); >;
	float g_flParallaxViewAngleLimit2 < Default( 1 ); >;
	float g_flLayer2UseSecondaryUv < Default( 0 ); >;
	float g_flLayer2ShadingBlendStrength < Default( 0 ); >;
	float g_flLayer3Offset < Default( 0 ); >;
	float g_flParallaxViewAngleLimit3 < Default( 1 ); >;
	float g_flLayer3UseSecondaryUv < Default( 0 ); >;
	float g_flReflectionOcclusionOffset < Default( 0 ); >;
	float g_flReflectionOcclusionViewAngleLimit < Default( 1 ); >;
	float g_flReflectionOcclusionUseSecondaryUv < Default( 0 ); >;

	float3 ApplyColorMatrix( float3 color, float4 r, float4 g, float4 b )
	{
		float4 c = float4( color, 1 );
		return float3( dot( r, c ), dot( g, c ), dot( b, c ) );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float2 uv2 = i.vTextureCoords.zw;
		float3 positionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		float3 positionDdx = ddx_fine( positionWs );
		float3 positionDdy = ddy_fine( positionWs );

		float inverseLength = 1.0 / length( i.vNormalWs );
		float3 normal = i.vNormalWs * inverseLength;
		float3 tangent = i.vTangentUWs * inverseLength;
		float3 bitangent = i.vTangentVWs * inverseLength;

		float3 toCamera = CalculatePositionToCameraDirWs( positionWs );
		float inverseNdotV = 1.0 / dot( toCamera, i.vNormalWs );
		float offset2 = g_flLayer2Offset * inverseNdotV;
		float offset3 = g_flLayer3Offset * inverseNdotV;
		float offsetOcclusion = g_flReflectionOcclusionOffset * inverseNdotV;

		// The view in the secondary UVs' own tangent frame, from screen derivatives.
		float2 uv2Ddx = ddx_fine( uv2 );
		float2 uv2Ddy = ddy_fine( uv2 );
		float area = dot( uv2Ddx, float2( uv2Ddy.y, -uv2Ddy.x ) );
		float winding = area < 0.0 ? -1.0 : 1.0;
		float2 projection = float2( uv2Ddy.y, -uv2Ddx.y ) * winding;
		float3 derivedTangent = ( positionDdx - normal * dot( positionDdx, normal ) ) * projection.x + ( positionDdy - normal * dot( positionDdy, normal ) ) * projection.y;
		if ( abs( area ) > 0.0 )
			derivedTangent = normalize( derivedTangent );

		float handedness = dot( positionDdy, cross( normal, positionDdx ) ) < 0.0 ? -1.0 : 1.0;
		float2 view = float2( dot( toCamera, tangent ), dot( toCamera, bitangent ) );
		float2 viewSecondary = float2( dot( toCamera, derivedTangent ), dot( toCamera, cross( normal, derivedTangent ) * ( winding * handedness ) ) );

		// Each layer behind the glass is shifted along the view, the shift limited at grazing angles.
		float2 limit2 = g_flParallaxViewAngleLimit2;
		float2 limit3 = g_flParallaxViewAngleLimit3;
		float2 limitOcclusion = g_flReflectionOcclusionViewAngleLimit;

		float2 uvLayer2;
		float2 viewLayer2;
		if ( g_flLayer2UseSecondaryUv > 0.5 )
		{
			viewLayer2 = clamp( viewSecondary, -limit2, limit2 );
			uvLayer2 = uv2 - viewLayer2 * offset2;
		}
		else
		{
			uvLayer2 = uv - clamp( view, -limit2, limit2 ) * offset2;
			viewLayer2 = viewSecondary;
		}

		float2 uvLayer3;
		float2 viewLayer3;
		if ( g_flLayer3UseSecondaryUv > 0.5 )
		{
			viewLayer3 = clamp( viewLayer2, -limit3, limit3 );
			uvLayer3 = uv2 - viewLayer3 * offset3;
		}
		else
		{
			uvLayer3 = uv - max( min( view, limit3 ), -limit3 ) * offset3;
			viewLayer3 = viewLayer2;
		}

		float2 uvOcclusion = g_flReflectionOcclusionUseSecondaryUv > 0.5
			? uv2 - clamp( viewLayer3, -limitOcclusion, limitOcclusion ) * offsetOcclusion
			: uv - max( min( view, limit3 ), -limitOcclusion ) * offsetOcclusion;

		float3 vertexColor = i.vPaint.rgb;

		float3 color1 = ApplyColorMatrix( g_tColor.Sample( g_sAniso, uv ).rgb, g_vAlbedoCorrect1R, g_vAlbedoCorrect1G, g_vAlbedoCorrect1B );
		color1 = lerp( color1, color1 * vertexColor, g_flVertexColorStrength1 );

		float4 texel2 = g_tColor2.Sample( g_sAniso, uvLayer2 );
		float3 color2 = ApplyColorMatrix( texel2.rgb, g_vAlbedoCorrect2R, g_vAlbedoCorrect2G, g_vAlbedoCorrect2B );
		color2 = lerp( color2, color2 * vertexColor, g_flVertexColorStrength2 );

		float4 texel3 = g_tColor3.Sample( g_sAniso, uvLayer3 );
		float3 color3 = ApplyColorMatrix( texel3.rgb, g_vAlbedoCorrect3R, g_vAlbedoCorrect3G, g_vAlbedoCorrect3B );
		color3 = lerp( color3, color3 * vertexColor, g_flVertexColorStrength3 );

		// The masks cut each layer away to show the one behind.
		float mask1 = g_tMask.Sample( g_sAniso, uv ).x;
		float mask2 = g_tMask2.Sample( g_sAniso, uvLayer2 ).x;
		float through1 = 1.0 - mask1;
		float through2 = 1.0 - mask2;

		float emissive2 = g_flEmissiveLevel2 * texel2.a * through1 * dot( g_vEmissiveTint2, DeadlockLuminance );
		float emissive3 = g_flEmissiveLevel3 * texel3.a * through2 * dot( g_vEmissiveTint3, DeadlockLuminance );

		float3 albedo = lerp( lerp( color3, color2, mask2 ), color1, mask1 );
		float throughBoth = through1 * through2;
		albedo = lerp( albedo, g_vEmissiveTint2 * albedo, ( 1.0 - throughBoth ) * saturate( emissive2 ) );
		float selfIllum = lerp( through1 * emissive2, emissive3, throughBoth );
		albedo = lerp( albedo, g_vEmissiveTint3 * albedo, throughBoth * saturate( emissive3 ) );

		// Reflections are kept off where the occlusion mask says the glass is dark behind.
		float4 normalRoughness1 = g_tNormalRoughness.Sample( g_sAniso, uv );
		float4 normalRoughness2 = g_tNormalRoughness2.Sample( g_sAniso, uvLayer2 );
		float occluded = step( g_tReflectionOcclusionMask.Sample( g_sAniso, uvOcclusion ).x, 0.5 );
		normalRoughness1.z = lerp( lerp( 1.0, normalRoughness1.z, occluded ), normalRoughness1.z, g_flLayer2ShadingBlendStrength );
		normalRoughness2.z = lerp( 1.0, normalRoughness2.z, occluded );
		float4 normalRoughness = lerp( normalRoughness1, normalRoughness2, through1 * g_flLayer2ShadingBlendStrength );

		float3 normalTs = DecodeHemiOctahedronNormal( normalRoughness.xy );
		float3 a = abs( normalTs );
		float2 gradient = float2( -normalTs.x, normalTs.y ) / max( a.z, 0.0078125 * max( a.x, a.y ) );
		float3 shadingNormal = normalize( normal - ( tangent * gradient.x + bitangent * gradient.y ) );

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = albedo;
		m.Emission = albedo * saturate( selfIllum * 0.1 ) * 10.0;
		m.Normal = shadingNormal;
		m.GeometricNormal = normal;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;
		m.Roughness = saturate( normalRoughness.z );
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.Opacity = 1;

		#if S_MODE_DEPTH
			return ShadingModelStandard::Shade( m );
		#else
			float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );

			[branch]
			if ( !DeadlockLightingEnabled() )
				return ShadingModelStandard::Shade( m );

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
			s.LightmapUV = i.vLightmapUV;

			return DoAtmospherics( m.WorldPosition, m.ScreenPosition.xy, float4( DeadlockShade( s ), 1 ) );
		#endif
	}
}
