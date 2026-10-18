//=========================================================================================================================
// Deadlock's bloom, as the game builds it (bloom_downsample and gaussian_blur_bloom): a thresholded source at a quarter
// of the screen, halved four times, each level blurred across and down, then the levels summed back up by weight.
// Deadlock Post Processing runs the passes; D_PASS picks one.
//=========================================================================================================================

HEADER
{
	Description = "Deadlock bloom";
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

	// 0: the source, a quarter of the screen; 1: a level half the size of the one above; 2, 3: the blur across,
	// then down; 4: a level plus the levels below it.
	DynamicCombo( D_PASS, 0..4, Sys( ALL ) );

	Texture2D g_tSource < Attribute( "BloomSource" ); SrgbRead( false ); >;
	Texture2D g_tLevel < Attribute( "BloomLevel" ); SrgbRead( false ); >;

	// s&box's bloom layer: what blooms without a threshold (the bloom amount override), written as a fraction.
	Texture2D g_tEffects < Attribute( "QuarterResEffectsBloomInputTexture" ); SrgbRead( false ); >;
	float g_flEffectsRange < Attribute( "BloomEffectsRange" ); Default( 64 ); >;

	float2 g_vTargetInvSize < Attribute( "BloomTargetInvSize" ); >;
	float2 g_vSourceInvSize < Attribute( "BloomSourceInvSize" ); >;

	// The threshold as (luminance * exposure) * x + y, saturated; the camera's exposure; the largest bloom strength.
	float2 g_vThreshold < Attribute( "BloomThreshold" ); Default2( 1.001, -1.001 ); >;
	float g_flBloomExposure < Attribute( "BloomExposure" ); Default( 1 ); >;
	float g_flBloomScale < Attribute( "BloomScale" ); Default( 1 ); >;

	// The combine: the levels below come in at one weight, this level at another.
	float g_flBelowWeight < Attribute( "BloomBelowWeight" ); Default( 1 ); >;
	float g_flLevelWeight < Attribute( "BloomLevelWeight" ); Default( 1 ); >;

	float3 Fetch( Texture2D source, float2 uv )
	{
		return source.SampleLevel( g_sBilinearClamp, uv, 0 ).rgb;
	}

	// One of the source's taps, weighted by how far its luminance passes the threshold.
	float3 Threshold( float2 uv )
	{
		float3 color = clamp( Fetch( g_tSource, uv ), 0.0, 65504.0 );
		float luminance = dot( color, float3( 0.3, 0.59, 0.11 ) );
		return color * saturate( luminance * g_flBloomExposure * g_vThreshold.x + g_vThreshold.y );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 targetSize = 1.0 / g_vTargetInvSize;
		float2 pixel = i.vTexCoord * targetSize;

		#if D_PASS == 0
			// Four bilinear taps, each a 2x2 average, cover the 4x4 pixels under this one.
			float2 uv = pixel * 4.0 * g_vSourceInvSize;
			float2 texel = g_vSourceInvSize;
			float3 color = ( Threshold( uv + texel * float2( -1, -1 ) ) + Threshold( uv + texel * float2( -1, 1 ) )
				+ Threshold( uv + texel * float2( 1, -1 ) ) + Threshold( uv + texel * float2( 1, 1 ) ) ) * 0.25;

			color += g_tEffects.SampleLevel( g_sBilinearClamp, i.vTexCoord, 0 ).rgb * g_flEffectsRange;
			return float4( color * g_flBloomScale, 1 );
		#elif D_PASS == 1
			// One bilinear tap, a 2x2 average.
			return float4( Fetch( g_tSource, pixel * 2.0 * g_vSourceInvSize ), 1 );
		#elif D_PASS == 2 || D_PASS == 3
			// The game's four tap blur, its taps between texels.
			const float offsets[4] = { -2.196197, -0.430063, 1.0, 2.196197 };
			const float weights[4] = { 0.121597, 0.529213, 0.227595, 0.121597 };
			float2 direction = D_PASS == 2 ? float2( 1, 0 ) : float2( 0, 1 );

			float3 color = 0;
			for ( int k = 0; k < 4; k++ )
				color += Fetch( g_tSource, min( pixel + direction * offsets[k], targetSize ) * g_vSourceInvSize ) * weights[k];

			return float4( color, 1 );
		#else
			// The levels below, upsampled with a tent of four bilinear taps, plus this level.
			float4 corners = float4( ( pixel - 1.0 ) * g_vTargetInvSize, ( pixel + 1.0 ) * g_vTargetInvSize );
			float3 below = ( Fetch( g_tSource, corners.xy ) + Fetch( g_tSource, corners.xw ) + Fetch( g_tSource, corners.zw ) + Fetch( g_tSource, corners.zy ) ) * 0.25;

			return float4( below * g_flBelowWeight + Fetch( g_tLevel, i.vTexCoord ) * g_flLevelWeight, 1 );
		#endif
	}
}
