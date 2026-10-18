//=========================================================================================================================
// Deadlock's world shaders: environment_blend (up to three vertex painted layers, revealed through masks, with seam
// borders, a UV overlay and world projected overlays), environment_layer (one such layer) and environment_simple
// (one vertex painted texture).
//
// Each builds what the game writes to its G-buffer, and is then lit as the game's deferred lighting pass lights it
// (deadlock_lighting.hlsl). The material's features are uniforms rather than combos, so one compile covers them all.
//=========================================================================================================================

HEADER
{
	Description = "Deadlock environment_blend, environment_layer and environment_simple";
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

	// environment_simple: one texture, with roughness, metalness, self illumination and tint amount painted per vertex.
	float g_flSimple < Default( 0 ); >;
}

struct VertexInput
{
	#include "deadlock_vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vPaint : TEXCOORD5;
	float4 vBlendParams : TEXCOORD8;
	float3 vPositionOs : TEXCOORD9;
};

VS
{
	#include "deadlock_vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessDeadlockVertex( v );

		// The object tint (the renderer's and the draw call's) times the vertex paint tint, faded in by its alpha.
		// Both are authored in gamma space.
		float4 tint = i.vVertexColor * v.vMeshTint;
		float3 linearTint = SrgbGammaToLinear( tint.rgb );

		if ( g_flSimple > 0.5 )
			linearTint = lerp( 1.0, linearTint, v.vBlendParams.w );

		float3 paint = lerp( 1.0, SrgbGammaToLinear( v.vMeshColor.rgb ), v.vMeshColor.a );

		i.vPaint = float4( linearTint * paint, tint.a );
		i.vBlendParams = v.vBlendParams;
		i.vPositionOs = v.vPositionOs;
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

	//
	// Textures
	//
	CreateInputTexture2D( DeadlockColor1, Srgb, 8, "None", "_color", "Layer 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( DeadlockNormalRoughness1, Linear, 8, "None", "_normal", "Layer 1", Default4( 0.5, 0.5, 1, 1 ) );
	CreateInputTexture2D( DeadlockPacked1, Linear, 8, "None", "_mask", "Layer 1", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( DeadlockColor2, Srgb, 8, "None", "_color", "Layer 2", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( DeadlockNormalRoughness2, Linear, 8, "None", "_normal", "Layer 2", Default4( 0.5, 0.5, 1, 1 ) );
	CreateInputTexture2D( DeadlockPacked2, Linear, 8, "None", "_mask", "Layer 2", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( DeadlockRevealMask2, Linear, 8, "None", "_mask", "Layer 2", Default4( 0.5, 0.5, 0.5, 0.5 ) );
	CreateInputTexture2D( DeadlockColor3, Srgb, 8, "None", "_color", "Layer 3", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( DeadlockNormalRoughness3, Linear, 8, "None", "_normal", "Layer 3", Default4( 0.5, 0.5, 1, 1 ) );
	CreateInputTexture2D( DeadlockPacked3, Linear, 8, "None", "_mask", "Layer 3", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( DeadlockRevealMask3, Linear, 8, "None", "_mask", "Layer 3", Default4( 0.5, 0.5, 0.5, 0.5 ) );
	CreateInputTexture2D( DeadlockColorOverlay, Srgb, 8, "None", "_color", "Overlay", Default4( 0.5, 0.5, 0.5, 1 ) );
	CreateInputTexture2D( DeadlockNormalRoughnessOverlay, Linear, 8, "None", "_normal", "Overlay", Default4( 0.5, 0.5, 0.5, 1 ) );
	CreateInputTexture2D( DeadlockRevealOverlay, Linear, 8, "None", "_mask", "Overlay", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( DeadlockColorWorldOverlay, Srgb, 8, "None", "_color", "World Overlay", Default4( 0.5, 0.5, 0.5, 1 ) );
	CreateInputTexture2D( DeadlockColorWorldOverlay2, Srgb, 8, "None", "_color", "World Overlay", Default4( 0.5, 0.5, 0.5, 1 ) );

	Texture2D g_tColor1 < Channel( RGBA, Box( DeadlockColor1 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormalRoughness1 < Channel( RGBA, Box( DeadlockNormalRoughness1 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tPacked1 < Channel( RGBA, Box( DeadlockPacked1 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tColor2 < Channel( RGBA, Box( DeadlockColor2 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormalRoughness2 < Channel( RGBA, Box( DeadlockNormalRoughness2 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tPacked2 < Channel( RGBA, Box( DeadlockPacked2 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tRevealMask2 < Channel( RGBA, Box( DeadlockRevealMask2 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tColor3 < Channel( RGBA, Box( DeadlockColor3 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormalRoughness3 < Channel( RGBA, Box( DeadlockNormalRoughness3 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tPacked3 < Channel( RGBA, Box( DeadlockPacked3 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tRevealMask3 < Channel( RGBA, Box( DeadlockRevealMask3 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tColorOverlay < Channel( RGBA, Box( DeadlockColorOverlay ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormalRoughnessOverlay < Channel( RGBA, Box( DeadlockNormalRoughnessOverlay ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tRevealOverlay < Channel( RGBA, Box( DeadlockRevealOverlay ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tColorWorldOverlay < Channel( RGBA, Box( DeadlockColorWorldOverlay ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tColorWorldOverlay2 < Channel( RGBA, Box( DeadlockColorWorldOverlay2 ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;

	TextureAttribute( RepresentativeTexture, g_tColor1 );
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor1 );

	//
	// Which parts of the shader the material uses
	//
	float g_flLayerCount < Default( 1 ); >;
	float g_flUvOverlay < Default( 0 ); >;
	float g_flWorldOverlay < Default( 0 ); >;
	float g_flWorldOverlayObjectSpace < Default( 0 ); >;

	//
	// Layers: colour correction and tint (rows of 3x4 matrices), masks, contrast and brightness adjustments, and
	// UV transforms as ( scale * sin, scale * cos, offset ) of a rotation and scale after an offset
	//
	float4 g_vAlbedoCorrect1R < Default4( 1, 0, 0, 0 ); >;
	float4 g_vAlbedoCorrect1G < Default4( 0, 1, 0, 0 ); >;
	float4 g_vAlbedoCorrect1B < Default4( 0, 0, 1, 0 ); >;
	float4 g_vColorTint1R < Default4( 1, 0, 0, 0 ); >;
	float4 g_vColorTint1G < Default4( 0, 1, 0, 0 ); >;
	float4 g_vColorTint1B < Default4( 0, 0, 1, 0 ); >;
	float g_flMaskColorTint1 < Default( 1 ); >;
	float g_flMaskVertexColorTint1 < Default( 1 ); >;
	float g_flModelTint1 < Default( 1 ); >;
	float g_flVertexColorStrength1 < Default( 1 ); >;
	float g_flLayerNormalStrength1 < Default( 1 ); >;
	float g_flLayerSelfIllumScale1 < Default( 1 ); >;
	float3 g_vLayerSelfIllumTint1 < Default3( 1, 1, 1 ); >;
	float2 g_vLayerRoughnessContrastBrightness1 < Default2( 1, 0 ); >;
	float2 g_vLayerTintMaskContrastBrightness1 < Default2( 1, 0 ); >;
	float2 g_vLayerMetalnessContrastBrightness1 < Default2( 1, 0 ); >;
	float4 g_vUvTransform1 < Default4( 0, 1, 0, 0 ); >;
	float g_flBiPlanar1 < Default( 0 ); >;
	float g_flBiPlanarTiling1 < Default( 196 ); >;

	float4 g_vAlbedoCorrect2R < Default4( 1, 0, 0, 0 ); >;
	float4 g_vAlbedoCorrect2G < Default4( 0, 1, 0, 0 ); >;
	float4 g_vAlbedoCorrect2B < Default4( 0, 0, 1, 0 ); >;
	float4 g_vColorTint2R < Default4( 1, 0, 0, 0 ); >;
	float4 g_vColorTint2G < Default4( 0, 1, 0, 0 ); >;
	float4 g_vColorTint2B < Default4( 0, 0, 1, 0 ); >;
	float g_flMaskColorTint2 < Default( 1 ); >;
	float g_flMaskVertexColorTint2 < Default( 1 ); >;
	float g_flModelTint2 < Default( 1 ); >;
	float g_flVertexColorStrength2 < Default( 1 ); >;
	float g_flLayerNormalStrength2 < Default( 1 ); >;
	float g_flLayerSelfIllumScale2 < Default( 1 ); >;
	float3 g_vLayerSelfIllumTint2 < Default3( 1, 1, 1 ); >;
	float2 g_vLayerRoughnessContrastBrightness2 < Default2( 1, 0 ); >;
	float2 g_vLayerTintMaskContrastBrightness2 < Default2( 1, 0 ); >;
	float2 g_vLayerMetalnessContrastBrightness2 < Default2( 1, 0 ); >;
	float4 g_vUvTransform2 < Default4( 0, 1, 0, 0 ); >;
	float g_flBiPlanar2 < Default( 0 ); >;
	float g_flBiPlanarTiling2 < Default( 196 ); >;

	float4 g_vAlbedoCorrect3R < Default4( 1, 0, 0, 0 ); >;
	float4 g_vAlbedoCorrect3G < Default4( 0, 1, 0, 0 ); >;
	float4 g_vAlbedoCorrect3B < Default4( 0, 0, 1, 0 ); >;
	float4 g_vColorTint3R < Default4( 1, 0, 0, 0 ); >;
	float4 g_vColorTint3G < Default4( 0, 1, 0, 0 ); >;
	float4 g_vColorTint3B < Default4( 0, 0, 1, 0 ); >;
	float g_flMaskColorTint3 < Default( 1 ); >;
	float g_flMaskVertexColorTint3 < Default( 1 ); >;
	float g_flModelTint3 < Default( 1 ); >;
	float g_flVertexColorStrength3 < Default( 1 ); >;
	float g_flLayerNormalStrength3 < Default( 1 ); >;
	float g_flLayerSelfIllumScale3 < Default( 1 ); >;
	float3 g_vLayerSelfIllumTint3 < Default3( 1, 1, 1 ); >;
	float2 g_vLayerRoughnessContrastBrightness3 < Default2( 1, 0 ); >;
	float2 g_vLayerTintMaskContrastBrightness3 < Default2( 1, 0 ); >;
	float2 g_vLayerMetalnessContrastBrightness3 < Default2( 1, 0 ); >;
	float4 g_vUvTransform3 < Default4( 0, 1, 0, 0 ); >;
	float g_flBiPlanar3 < Default( 0 ); >;
	float g_flBiPlanarTiling3 < Default( 196 ); >;

	// Layers 2 and 3 are revealed by their masks against the vertex paint, with optional borders along the seam.
	float g_flRevealOffset2 < Default( 0 ); >;
	float g_flRevealSoftness2 < Default( 0.125 ); >;
	float g_flRevealInvert2 < Default( 0 ); >;
	float g_flRevealUseSecondaryUv2 < Default( 0 ); >;
	float4 g_vRevealUvTransform2 < Default4( 0, 1, 0, 0 ); >;
	float g_flBorderEffects2 < Default( 0 ); >;
	float2 g_vBorderWidth2 < Default2( 0.125, 0.125 ); >;
	float2 g_vBorderSoftness2 < Default2( 0.5, 0.5 ); >;
	float3 g_vBorderLayerAmount2 < Default3( 1, 1, 1 ); >;
	float g_flBorderBlendMode2 < Default( 0 ); >;
	float4 g_vBorderTint2 < Default4( 0.5, 0.5, 0.5, 1 ); >;
	float g_flBorderRoughnessStrength2 < Default( 0 ); >;
	float g_flBorderRoughness2 < Default( 0.5 ); >;

	float g_flRevealOffset3 < Default( 0 ); >;
	float g_flRevealSoftness3 < Default( 0.125 ); >;
	float g_flRevealInvert3 < Default( 0 ); >;
	float g_flRevealUseSecondaryUv3 < Default( 0 ); >;
	float4 g_vRevealUvTransform3 < Default4( 0, 1, 0, 0 ); >;
	float g_flBorderEffects3 < Default( 0 ); >;
	float2 g_vBorderWidth3 < Default2( 0.125, 0.125 ); >;
	float2 g_vBorderSoftness3 < Default2( 0.5, 0.5 ); >;
	float3 g_vBorderLayerAmount3 < Default3( 1, 1, 1 ); >;
	float g_flBorderBlendMode3 < Default( 0 ); >;
	float4 g_vBorderTint3 < Default4( 0.5, 0.5, 0.5, 1 ); >;
	float g_flBorderRoughnessStrength3 < Default( 0 ); >;
	float g_flBorderRoughness3 < Default( 0.5 ); >;

	// UV overlay: one texture laid over every layer, plus a mask that pushes layer 2 in on top of the vertex paint.
	float4 g_vOverlayUvTransform < Default4( 0, 1, 0, 0 ); >;
	float g_flUseSecondaryUvForOverlay < Default( 1 ); >;
	float g_flOverlayDerivedFrame < Default( 1 ); >;
	float g_flOverlayDarknessContrast < Default( 1 ); >;
	float g_flOverlayBrightnessContrast < Default( 1 ); >;
	float3 g_vOverlayTintMask < Default3( 0, 0, 0 ); >;
	float3 g_vOverlayStrength < Default3( 1, 1, 1 ); >;
	float3 g_vOverlayNormalStrength < Default3( 1, 1, 1 ); >;
	float3 g_vOverlayRoughnessStrength < Default3( 0, 0, 0 ); >;
	float g_flRevealOverlayInvert2 < Default( 0 ); >;
	float g_flRevealOverlayStrength2 < Default( 1 ); >;

	// World overlays: biplanar projected colour modulation shared by every layer, one or two of them.
	float3 g_vWorldOverlayTintMask < Default3( 1, 1, 1 ); >;
	float2 g_vWorldOverlayTiling < Default2( 512, 512 ); >;
	float2 g_vWorldOverlayBrightnessContrast < Default2( 1, 1 ); >;
	float2 g_vWorldOverlayDarknessContrast < Default2( 1, 1 ); >;
	float2 g_vWorldOverlayStrengths1 < Default2( 1, 0 ); >;
	float2 g_vWorldOverlayStrengths2 < Default2( 1, 0 ); >;
	float2 g_vWorldOverlayStrengths3 < Default2( 1, 0 ); >;

	float2 ApplyUvTransform( float2 uv, float4 transform )
	{
		float2 offset = uv + transform.zw;
		return float2( transform.y * offset.x - transform.x * offset.y, transform.x * offset.x + transform.y * offset.y );
	}

	float ContrastBrightness( float value, float2 contrastBrightness )
	{
		return saturate( ( ( value - 0.5 ) + contrastBrightness.y ) * contrastBrightness.x + 0.5 );
	}

	float3 ApplyColorMatrix( float3 color, float4 r, float4 g, float4 b )
	{
		float4 c = float4( color, 1 );
		return float3( dot( r, c ), dot( g, c ), dot( b, c ) );
	}

	// A hemi-octahedral normal as a surface gradient, which layers and overlays add up before one normalize.
	float2 SurfaceGradient( float2 hemiOct )
	{
		float3 normal = DecodeHemiOctahedronNormal( hemiOct );
		float3 a = abs( normal );
		return float2( -normal.x, normal.y ) / max( a.z, 0.0078125 * max( a.x, a.y ) );
	}

	//
	// Frames a layer's normal map is resolved against
	//

	struct LayerFrame
	{
		float2 Uv;
		float3 Tangent;
		float3 Bitangent;
	};

	// Mikkelsen's screen space tangent frame, for coordinates that aren't the mesh's own.
	LayerFrame DerivedFrame( float2 uv, float3 position, float3 normal )
	{
		float3 positionDdx = ddx_fine( position );
		float3 positionDdy = ddy_fine( position );

		float3 sigmaX = positionDdx - normal * dot( positionDdx, normal );
		float3 sigmaY = positionDdy - normal * dot( positionDdy, normal );
		float handedness = dot( positionDdy, cross( normal, positionDdx ) ) < 0.0 ? -1.0 : 1.0;

		float2 uvDdx = ddx_fine( uv );
		float2 uvDdy = ddy_fine( uv );
		float area = dot( uvDdx, float2( uvDdy.y, -uvDdy.x ) );
		float winding = area < 0.0 ? -1.0 : 1.0;

		float2 projection = float2( uvDdy.y, -uvDdx.y ) * winding;
		float3 tangent = sigmaX * projection.x + sigmaY * projection.y;

		if ( abs( area ) > 0.0 )
			tangent = normalize( tangent );

		LayerFrame frame;
		frame.Uv = uv;
		frame.Tangent = tangent;
		frame.Bitangent = cross( normal, tangent ) * ( winding * handedness );
		return frame;
	}

	// The biggest and smallest components of a normal, as axis permutations, for planar projections.
	void PlanarAxes( float3 normal, out int3 major, out int3 median, out float3 absNormal )
	{
		absNormal = abs( normal );

		major = ( absNormal.x > absNormal.y && absNormal.x > absNormal.z ) ? int3( 0, 1, 2 )
			: ( ( absNormal.y > absNormal.z ) ? int3( 1, 2, 0 ) : int3( 2, 0, 1 ) );

		int3 minor = ( absNormal.x < absNormal.y && absNormal.x < absNormal.z ) ? int3( 0, 1, 2 )
			: ( ( absNormal.y < absNormal.z ) ? int3( 1, 2, 0 ) : int3( 2, 0, 1 ) );

		median = int3( 3, 3, 3 ) - minor - major;
	}

	float2 PlanarUv( float3 position, int3 axis )
	{
		float2 planar = float2( position[axis.y], position[axis.z] );

		if ( axis.x == 1 )
			planar = planar.yx;

		return float2( planar.x, -planar.y );
	}

	// Layers only sample the dominant plane.
	LayerFrame BiPlanarFrame( float3 position, float3 normal, float tiling )
	{
		int3 major, median;
		float3 absNormal;
		PlanarAxes( normal, major, median, absNormal );

		return DerivedFrame( PlanarUv( position, major ) / ( tiling == 0.0 ? 1.0 : tiling ), position, normal );
	}

	// Blends the two biggest planes once either is more than about 35 degrees off its axis.
	float4 SampleBiPlanar( Texture2D tex, float3 position, float3 normal, float tiling )
	{
		int3 major, median;
		float3 absNormal;
		PlanarAxes( normal, major, median, absNormal );

		float invTiling = 1.0 / ( tiling == 0.0 ? 1.0 : tiling );
		float2 majorUv = PlanarUv( position, major ) * invTiling;
		float2 medianUv = PlanarUv( position, median ) * invTiling;

		float3 positionDdx = ddx_fine( position ) * invTiling;
		float3 positionDdy = ddy_fine( position ) * invTiling;

		float4 majorSample = tex.SampleGrad( g_sAniso, majorUv, float2( positionDdx[major.y], positionDdx[major.z] ), float2( positionDdy[major.y], positionDdy[major.z] ) );
		float4 medianSample = tex.SampleGrad( g_sAniso, medianUv, float2( positionDdx[median.y], positionDdx[median.z] ), float2( positionDdy[median.y], positionDdy[median.z] ) );

		float2 weights = saturate( ( float2( absNormal[major.x], absNormal[median.x] ) - 0.5773 ) * 2.3657441 );
		weights /= weights.x + weights.y;

		return lerp( majorSample, medianSample, weights.y );
	}

	// An overlay texel is a signed multiplier around 0.5: above brightens, below darkens, each shaped by a contrast.
	float3 OverlayFactor( float3 overlay, float darknessContrast, float brightnessContrast )
	{
		float3 signedOverlay = overlay * 2.0 - 1.0;
		float3 brighten = ( 1.0 - pow( max( 1.0 - max( 0.0, signedOverlay ), 1e-6 ), brightnessContrast ) ) * brightnessContrast;
		float3 darken = ( pow( max( 1.0 + min( 0.0, signedOverlay ), 1e-6 ), darknessContrast ) - 1.0 ) * darknessContrast;
		return max( 0.0, brighten + darken + 1.0 );
	}

	// 0: unmasked, 1: masked by the layer's tint mask, 2: inverted
	float OverlayTintMask( float mode, float tintMask )
	{
		return mode < 0.5 ? 1.0 : ( mode > 1.5 ? 1.0 - tintMask : tintMask );
	}

	//
	// Layers
	//

	struct Layer
	{
		float3 Albedo;
		float3 NormalOffset;	// world space surface gradient, subtracted from the geometric normal
		float Roughness;
		float Metalness;
		float TintMask;
		float SelfIllum;
	};

	Layer SampleLayer( Texture2D colorTexture, Texture2D normalTexture, Texture2D packedTexture, LayerFrame frame, float3 vertexColor,
		float4 correctR, float4 correctG, float4 correctB, float4 tintR, float4 tintG, float4 tintB,
		float2 roughnessCB, float2 tintMaskCB, float2 metalnessCB, float normalStrength, float selfIllumScale,
		float maskColorTint, float maskVertexColorTint, float modelTint, float vertexColorStrength )
	{
		float4 color = colorTexture.Sample( g_sAniso, frame.Uv );
		float4 normalRoughness = normalTexture.Sample( g_sAniso, frame.Uv );
		float4 packed = packedTexture.Sample( g_sAniso, frame.Uv );

		Layer layer;
		layer.TintMask = ContrastBrightness( color.a, tintMaskCB );
		layer.Roughness = ContrastBrightness( normalRoughness.b, roughnessCB );
		layer.Metalness = ContrastBrightness( packed.g, metalnessCB );
		layer.SelfIllum = packed.r * selfIllumScale;

		float2 gradient = SurfaceGradient( normalRoughness.rg ) * normalStrength;
		layer.NormalOffset = frame.Tangent * gradient.x + frame.Bitangent * gradient.y;

		float3 corrected = ApplyColorMatrix( color.rgb, correctR, correctG, correctB );
		float3 tinted = ApplyColorMatrix( corrected, tintR, tintG, tintB );
		float3 albedo = lerp( corrected, tinted, maskColorTint > 0.5 ? layer.TintMask : 1.0 );

		float vertexColorAmount = modelTint > 0.5 ? ( maskVertexColorTint > 0.5 ? layer.TintMask : 1.0 ) * vertexColorStrength : 0.0;
		layer.Albedo = lerp( albedo, albedo * vertexColor, vertexColorAmount );

		return layer;
	}

	// Where along the vertex paint a layer starts showing: the mask stretched about its middle by the offset, then
	// squeezed into the band the softness leaves at either end.
	float RevealThreshold( float revealMask, float invert, float offset, float softness )
	{
		float reveal = invert > 0.5 ? 1.0 - revealMask : revealMask;

		float halfOffset = offset * 0.5;
		float middle = 0.5 + halfOffset;
		float remapped = reveal < 0.5 ? saturate( reveal * 2.0 ) * middle : saturate( ( reveal - 0.5 ) * 2.0 ) * ( 0.5 - halfOffset ) + middle;

		return saturate( clamp( remapped, 1e-4, 0.9999 ) ) * ( 1.0 - 2.0 * softness ) + softness;
	}

	// How strongly the seam around a reveal edge decorates layer 1 (x), the revealed layer (y) and layer 3 (z).
	float3 BorderAmount( float blend, float threshold, float2 width, float2 softness, float3 layerAmount )
	{
		bool below = blend < threshold;

		float edgeSoftness = below ? softness.x : softness.y;
		float outer = below ? max( 1e-4, threshold - width.x ) : min( 0.9999, threshold + width.y );
		float inner = below ? max( 1e-4, threshold - 0.5 * width.x ) : min( 0.9999, threshold + 0.5 * width.y );

		return layerAmount * smoothstep( lerp( inner, outer, edgeSoftness ), lerp( inner, threshold, edgeSoftness ), blend );
	}

	// 0: multiply, 1: mod2x around the tint's middle, 2: blend towards the tint.
	float3 ApplyBorderTint( float3 albedo, float4 tint, float amount, float blendMode )
	{
		float weight = amount * tint.a;

		if ( blendMode < 0.5 )
			return albedo * lerp( 1.0, SrgbGammaToLinear( tint.rgb ), weight );

		if ( blendMode < 1.5 )
			return albedo * lerp( 1.0, tint.rgb * 1.9921569, weight );

		return lerp( albedo, SrgbGammaToLinear( tint.rgb ), weight );
	}

	void ApplyBorder( inout Layer a, inout Layer b, float amountA, float amountB, float4 tint, float blendMode, float roughness, float roughnessStrength )
	{
		a.Albedo = ApplyBorderTint( a.Albedo, tint, amountA, blendMode );
		b.Albedo = ApplyBorderTint( b.Albedo, tint, amountB, blendMode );
		a.Roughness = lerp( a.Roughness, roughness, roughnessStrength * amountA );
		b.Roughness = lerp( b.Roughness, roughness, roughnessStrength * amountB );
	}

	void ApplyWorldOverlay( inout Layer layer, float3 factor, float tintMaskMode, float strength )
	{
		layer.Albedo = lerp( layer.Albedo, layer.Albedo * factor, OverlayTintMask( tintMaskMode, layer.TintMask ) * strength );
	}

	void ApplyUvOverlay( inout Layer layer, float3 factor, float3 normalOffset, float roughness, float tintMaskMode, float strength, float normalStrength, float roughnessStrength )
	{
		float mask = OverlayTintMask( tintMaskMode, layer.TintMask );
		layer.Albedo = lerp( layer.Albedo, layer.Albedo * factor, mask * strength );
		layer.NormalOffset += normalOffset * ( normalStrength * mask );
		layer.Roughness = saturate( layer.Roughness + roughness * roughnessStrength * mask );
	}

	void BlendLayer( inout Layer result, inout float3 illum, Layer layer, float3 layerIllum, float weight )
	{
		result.Albedo = lerp( result.Albedo, layer.Albedo, weight );
		result.NormalOffset = lerp( result.NormalOffset, layer.NormalOffset, weight );
		result.Roughness = lerp( result.Roughness, layer.Roughness, weight );
		result.Metalness = lerp( result.Metalness, layer.Metalness, weight );
		result.SelfIllum = lerp( result.SelfIllum, layer.SelfIllum, weight );
		illum = lerp( illum, layerIllum, weight );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float2 uv2 = i.vTextureCoords.zw;
		float3 positionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		// The interpolated frame, scaled by the interpolated normal's length the way the game's shaders take it.
		float inverseLength = 1.0 / length( i.vNormalWs );
		float3 geometricNormal = i.vNormalWs * inverseLength;
		#if S_RENDER_BACKFACES
			geometricNormal *= i.face ? 1 : -1;
		#endif

		LayerFrame meshFrame;
		meshFrame.Uv = uv;
		meshFrame.Tangent = i.vTangentUWs * inverseLength;
		meshFrame.Bitangent = i.vTangentVWs * inverseLength;

		float3 vertexColor = i.vPaint.rgb;
		float4 blendParams = i.vBlendParams;

		Layer result;
		float3 illum;

		[branch]
		if ( g_flSimple > 0.5 )
		{
			// One texture, its alpha masking the vertex tint; the rest is painted per vertex. No normal map.
			float4 color = g_tColor1.Sample( g_sAniso, uv );

			result.Albedo = lerp( color.rgb, color.rgb * vertexColor, color.a );
			result.NormalOffset = 0;
			result.Roughness = blendParams.x;
			result.Metalness = blendParams.y;
			result.TintMask = color.a;
			result.SelfIllum = blendParams.z;
			illum = result.Albedo * blendParams.z;
		}
		else
		{
			// Every layer above the first is revealed by its mask weighed against a channel of the vertex paint.
			float blend2 = blendParams.x;
			float blend3 = 0;

			if ( g_flLayerCount > 2.5 )
			{
				blend3 = blendParams.y;
				blend2 = blend3 < 1.0 ? blend2 : 0.0;
				blend3 = blend3 > 1e-5 ? blend3 : 0.0;
			}

			blend2 = blend2 > 1e-5 ? blend2 : 0.0;

			// The UV overlay's reveal mask pushes layer 2 in on top of the paint.
			float2 overlayUv = ApplyUvTransform( g_flUseSecondaryUvForOverlay > 0.5 ? uv2 : uv, g_vOverlayUvTransform );

			if ( g_flUvOverlay > 0.5 )
			{
				float revealOverlay = g_tRevealOverlay.Sample( g_sAniso, overlayUv ).r;
				revealOverlay = g_flRevealOverlayInvert2 > 0.5 ? 1.0 - revealOverlay : revealOverlay;
				blend2 = saturate( blend2 + revealOverlay * g_flRevealOverlayStrength2 );
			}

			LayerFrame frame1 = meshFrame;
			frame1.Uv = ApplyUvTransform( uv, g_vUvTransform1 );
			if ( g_flBiPlanar1 > 0.5 )
				frame1 = BiPlanarFrame( positionWs, geometricNormal, g_flBiPlanarTiling1 );

			Layer layer1 = SampleLayer( g_tColor1, g_tNormalRoughness1, g_tPacked1, frame1, vertexColor, g_vAlbedoCorrect1R, g_vAlbedoCorrect1G, g_vAlbedoCorrect1B, g_vColorTint1R, g_vColorTint1G, g_vColorTint1B, g_vLayerRoughnessContrastBrightness1, g_vLayerTintMaskContrastBrightness1, g_vLayerMetalnessContrastBrightness1, g_flLayerNormalStrength1, g_flLayerSelfIllumScale1, g_flMaskColorTint1, g_flMaskVertexColorTint1, g_flModelTint1, g_flVertexColorStrength1 );

			// Self illumination is taken before any seam or overlay decoration touches the albedo.
			illum = layer1.Albedo * layer1.SelfIllum * g_vLayerSelfIllumTint1;

			Layer layer2 = layer1;
			Layer layer3 = layer1;
			float3 illum2 = illum;
			float3 illum3 = illum;
			float threshold2 = 0;
			float threshold3 = 0;

			[branch]
			if ( g_flLayerCount > 1.5 )
			{
				LayerFrame frame2 = meshFrame;
				frame2.Uv = ApplyUvTransform( uv, g_vUvTransform2 );
				if ( g_flBiPlanar2 > 0.5 )
					frame2 = BiPlanarFrame( positionWs, geometricNormal, g_flBiPlanarTiling2 );

				layer2 = SampleLayer( g_tColor2, g_tNormalRoughness2, g_tPacked2, frame2, vertexColor, g_vAlbedoCorrect2R, g_vAlbedoCorrect2G, g_vAlbedoCorrect2B, g_vColorTint2R, g_vColorTint2G, g_vColorTint2B, g_vLayerRoughnessContrastBrightness2, g_vLayerTintMaskContrastBrightness2, g_vLayerMetalnessContrastBrightness2, g_flLayerNormalStrength2, g_flLayerSelfIllumScale2, g_flMaskColorTint2, g_flMaskVertexColorTint2, g_flModelTint2, g_flVertexColorStrength2 );
				illum2 = layer2.Albedo * layer2.SelfIllum * g_vLayerSelfIllumTint2;

				float revealMask = g_tRevealMask2.Sample( g_sAniso, ApplyUvTransform( g_flRevealUseSecondaryUv2 > 0.5 ? uv2 : uv, g_vRevealUvTransform2 ) ).r;
				threshold2 = RevealThreshold( revealMask, g_flRevealInvert2, g_flRevealOffset2, g_flRevealSoftness2 );

				// Each seam decorates the layers below it too.
				if ( g_flBorderEffects2 > 0.5 && blend2 > 0.0 )
				{
					float3 amount = BorderAmount( blend2, threshold2, g_vBorderWidth2, g_vBorderSoftness2, float3( g_vBorderLayerAmount2.xy, 0.0 ) );
					ApplyBorder( layer1, layer2, amount.x, amount.y, g_vBorderTint2, g_flBorderBlendMode2, g_flBorderRoughness2, g_flBorderRoughnessStrength2 );
				}
			}

			[branch]
			if ( g_flLayerCount > 2.5 )
			{
				LayerFrame frame3 = meshFrame;
				frame3.Uv = ApplyUvTransform( uv, g_vUvTransform3 );
				if ( g_flBiPlanar3 > 0.5 )
					frame3 = BiPlanarFrame( positionWs, geometricNormal, g_flBiPlanarTiling3 );

				layer3 = SampleLayer( g_tColor3, g_tNormalRoughness3, g_tPacked3, frame3, vertexColor, g_vAlbedoCorrect3R, g_vAlbedoCorrect3G, g_vAlbedoCorrect3B, g_vColorTint3R, g_vColorTint3G, g_vColorTint3B, g_vLayerRoughnessContrastBrightness3, g_vLayerTintMaskContrastBrightness3, g_vLayerMetalnessContrastBrightness3, g_flLayerNormalStrength3, g_flLayerSelfIllumScale3, g_flMaskColorTint3, g_flMaskVertexColorTint3, g_flModelTint3, g_flVertexColorStrength3 );
				illum3 = layer3.Albedo * layer3.SelfIllum * g_vLayerSelfIllumTint3;

				float revealMask = g_tRevealMask3.Sample( g_sAniso, ApplyUvTransform( g_flRevealUseSecondaryUv3 > 0.5 ? uv2 : uv, g_vRevealUvTransform3 ) ).r;
				threshold3 = RevealThreshold( revealMask, g_flRevealInvert3, g_flRevealOffset3, g_flRevealSoftness3 );

				if ( g_flBorderEffects3 > 0.5 && blend3 > 0.0 )
				{
					float3 amount = BorderAmount( blend3, threshold3, g_vBorderWidth3, g_vBorderSoftness3, g_vBorderLayerAmount3 );
					ApplyBorder( layer1, layer2, amount.x, amount.y, g_vBorderTint3, g_flBorderBlendMode3, g_flBorderRoughness3, g_flBorderRoughnessStrength3 );
					layer3.Albedo = ApplyBorderTint( layer3.Albedo, g_vBorderTint3, amount.z, g_flBorderBlendMode3 );
					layer3.Roughness = lerp( layer3.Roughness, g_flBorderRoughness3, g_flBorderRoughnessStrength3 * amount.z );
				}
			}

			// World overlays, projected on the two biggest planes.
			[branch]
			if ( g_flWorldOverlay > 0.5 )
			{
				float3 overlayPosition = g_flWorldOverlayObjectSpace > 0.5 ? i.vPositionOs : positionWs;

				for ( int overlay = 0; overlay < 2; overlay++ )
				{
					if ( overlay >= (int)g_flWorldOverlay )
						break;

					float4 texel = overlay == 0
						? SampleBiPlanar( g_tColorWorldOverlay, overlayPosition, geometricNormal, g_vWorldOverlayTiling[0] )
						: SampleBiPlanar( g_tColorWorldOverlay2, overlayPosition, geometricNormal, g_vWorldOverlayTiling[1] );

					float3 factor = OverlayFactor( texel.rgb, g_vWorldOverlayDarknessContrast[overlay], g_vWorldOverlayBrightnessContrast[overlay] );

					ApplyWorldOverlay( layer1, factor, g_vWorldOverlayTintMask.x, g_vWorldOverlayStrengths1[overlay] );
					ApplyWorldOverlay( layer2, factor, g_vWorldOverlayTintMask.y, g_vWorldOverlayStrengths2[overlay] );
					ApplyWorldOverlay( layer3, factor, g_vWorldOverlayTintMask.z, g_vWorldOverlayStrengths3[overlay] );
				}
			}

			// The UV overlay, with its own tangent frame unless it rides on the untransformed primary UVs.
			[branch]
			if ( g_flUvOverlay > 0.5 )
			{
				LayerFrame derived = DerivedFrame( overlayUv, positionWs, geometricNormal );
				float3 overlayTangent = g_flOverlayDerivedFrame > 0.5 ? derived.Tangent : meshFrame.Tangent;
				float3 overlayBitangent = g_flOverlayDerivedFrame > 0.5 ? derived.Bitangent : meshFrame.Bitangent;

				float4 overlayColor = g_tColorOverlay.Sample( g_sAniso, overlayUv );
				float4 overlayNormalRoughness = g_tNormalRoughnessOverlay.Sample( g_sAniso, overlayUv );

				float2 gradient = SurfaceGradient( overlayNormalRoughness.rg );
				float3 normalOffset = overlayTangent * gradient.x + overlayBitangent * gradient.y;
				float roughness = overlayNormalRoughness.b * 2.0 - 1.0;
				float3 factor = OverlayFactor( overlayColor.rgb, g_flOverlayDarknessContrast, g_flOverlayBrightnessContrast );

				ApplyUvOverlay( layer1, factor, normalOffset, roughness, g_vOverlayTintMask.x, g_vOverlayStrength.x, g_vOverlayNormalStrength.x, g_vOverlayRoughnessStrength.x );
				ApplyUvOverlay( layer2, factor, normalOffset, roughness, g_vOverlayTintMask.y, g_vOverlayStrength.y, g_vOverlayNormalStrength.y, g_vOverlayRoughnessStrength.y );
				ApplyUvOverlay( layer3, factor, normalOffset, roughness, g_vOverlayTintMask.z, g_vOverlayStrength.z, g_vOverlayNormalStrength.z, g_vOverlayRoughnessStrength.z );
			}

			// Composite
			result = layer1;

			if ( g_flLayerCount > 1.5 )
			{
				float weight2 = blend2 >= 1.0 ? 1.0 : smoothstep( -g_flRevealSoftness2, g_flRevealSoftness2, blend2 - threshold2 );
				BlendLayer( result, illum, layer2, illum2, weight2 );
			}

			if ( g_flLayerCount > 2.5 )
			{
				float weight3 = blend3 >= 1.0 ? 1.0 : smoothstep( -g_flRevealSoftness3, g_flRevealSoftness3, blend3 - threshold3 );
				BlendLayer( result, illum, layer3, illum3, weight3 );
			}
		}

		float3 normal = normalize( geometricNormal - result.NormalOffset );

		// The G-buffer packs self illumination into the albedo: albedo and glow mixed by their strengths, glowing
		// by up to ten times.
		float selfIllum = result.SelfIllum;
		float3 albedo = ( result.Albedo * 0.1 + illum ) / ( 0.1 + selfIllum );
		float glow = saturate( selfIllum * 0.1 ) * 10.0;

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = albedo;
		m.Emission = albedo * glow;
		m.Normal = normal;
		m.GeometricNormal = geometricNormal;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;
		m.Roughness = saturate( result.Roughness );
		m.Metalness = saturate( result.Metalness );
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

			float3 color = DeadlockShade( s );
			return DoAtmospherics( m.WorldPosition, m.ScreenPosition.xy, float4( color, 1 ) );
		#endif
	}
}
