//=========================================================================================================================
// Deadlock's post_process: exposure, bloom, vignette and its filmic tonemapping curve, as the game composites them.
// Takes the place of s&box's tonemapping; the output is linear, like s&box's tonemapper's.
//=========================================================================================================================

HEADER
{
	Description = "Deadlock post processing";
	DevShader = true;
}

MODES
{
	Default();
	Forward();
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
	float2 vTexCoord : TEXCOORD0;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		o.vPositionPs = float4( i.vPositionOs.xyz, 1.0f );
		o.vPositionPs.z = 1;
		o.vTexCoord = i.vTexCoord;
		return o;
	}
}

PS
{
	#include "postprocess/common.hlsl"

	Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( false ); >;
	Texture2D g_tBloom < Attribute( "BloomTexture" ); SrgbRead( false ); >;

	// The camera's exposure times the exposure bias.
	float g_flExposure < Attribute( "Exposure" ); Default( 1 ); >;

	// Bloom added before tonemapping, and screened over after it.
	float g_flBloomAdd < Attribute( "BloomAdd" ); Default( 0 ); >;
	float g_flBloomScreen < Attribute( "BloomScreen" ); Default( 0 ); >;

	// The filmic curve: shoulder strength, linear strength, linear angle, toe strength; toe numerator, toe
	// denominator, white point, and one over the curve at the white point.
	float4 g_vTonemapCurve < Attribute( "TonemapCurve" ); Default4( 0.15, 0.5, 0.1, 0.2 ); >;
	float4 g_vTonemapToe < Attribute( "TonemapToe" ); Default4( 0.02, 0.3, 16, 1 ); >;

	// Vignette: centre and the edge's multiplier and offset; tint; inner radius, feather, superellipse exponent and
	// its inverse.
	float g_flVignette < Attribute( "Vignette" ); Default( 0 ); >;
	float4 g_vVignetteCenter < Attribute( "VignetteCenter" ); Default4( 0, 0, 1, 0 ); >;
	float3 g_vVignetteTint < Attribute( "VignetteTint" ); Default3( 1, 1, 1 ); >;
	float4 g_vVignetteShape < Attribute( "VignetteShape" ); Default4( 0.5, 0.5, 2, -0.5 ); >;

	float3 LinearToSrgb( float3 c )
	{
		return lerp( pow( max( c, 0.0 ), 0.41666667 ) * 1.055 - 0.055, c * 12.92, step( c, 0.0031308 ) );
	}

	float3 SrgbToLinear( float3 c )
	{
		return lerp( pow( ( max( c, 0.0 ) + 0.055 ) / 1.055, 2.4 ), c / 12.92, step( c, 0.04045 ) );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTexCoord;
		float4 hdr = max( 0.0, g_tColorBuffer.Sample( g_sPointClamp, uv ) );
		float3 bloom = g_tBloom.SampleLevel( g_sBilinearClamp, uv, 0 ).rgb;

		float3 color = hdr.rgb * g_flExposure + bloom * g_flBloomAdd;

		[branch]
		if ( g_flVignette > 0.5 )
		{
			float inner = g_vVignetteShape.x;
			float outer = g_vVignetteShape.x + g_vVignetteShape.y;
			float2 d = abs( ( uv * 2.0 - 1.0 ) - g_vVignetteCenter.xy );

			float2 innerTerms = pow( inner * d, g_vVignetteShape.z );
			float2 outerTerms = pow( outer * d, g_vVignetteShape.z );
			float clear = 1.0 - smoothstep( inner * inner * pow( innerTerms.x + innerTerms.y, g_vVignetteShape.w ), outer * outer * pow( outerTerms.x + outerTerms.y, g_vVignetteShape.w ), 1.0 );

			color = lerp( g_vVignetteTint * color, color, clear ) * lerp( g_vVignetteCenter.z, 1.0, clear ) + ( g_vVignetteCenter.w - g_vVignetteCenter.w * clear );
		}

		// The curve, normalized to the white point.
		float3 x = min( color * 2.8, g_vTonemapToe.z );
		float3 a = x * g_vTonemapCurve.x;
		float3 mapped = saturate( ( ( x * ( a + g_vTonemapCurve.z * g_vTonemapCurve.y ) + g_vTonemapCurve.w * g_vTonemapToe.x )
			/ ( x * ( a + g_vTonemapCurve.y ) + g_vTonemapCurve.w * g_vTonemapToe.y ) - g_vTonemapToe.x / g_vTonemapToe.y ) * g_vTonemapToe.w );

		// The game screens bloom over the display encoded image.
		[branch]
		if ( g_flBloomScreen > 0.0 )
		{
			float3 encoded = LinearToSrgb( mapped );
			float3 b = bloom * g_flExposure;
			float3 glow = saturate( b / ( b + 0.187 ) * 1.035 ) * g_flBloomScreen;
			mapped = SrgbToLinear( encoded + glow - encoded * glow );
		}

		return float4( mapped, hdr.a );
	}
}
