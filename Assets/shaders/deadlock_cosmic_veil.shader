//=========================================================================================================================
// Deadlock's cosmic_veil.vfx: the curtains across doorways that hide what's behind them. Unlit; it shows what's behind
// it, blurred and jittered, through smoke its warp mask stirs. Seen from the front it's veiled: grey and broken up.
// Seen from behind (single sided veils only) it's clearer and its hue shifts.
//
// The game draws veils into a depth buffer of their own, to draw only the nearest veil and to leave the blur's taps
// that land off the veil out. Without it every tap counts, which is what the taps landing on the veil get anyway.
//=========================================================================================================================

HEADER
{
	Description = "Deadlock cosmic veil";
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
	#define S_UV2 1
	// What's behind is composited here, so nothing blends.
	#define BLEND_MODE_ALREADY_SET 1
	// Both faces draw, the back ones unveiled.
	#define S_RENDER_BACKFACES 1
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "deadlock_vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vMeshTint : TEXCOORD5;
};

VS
{
	#include "deadlock_vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessDeadlockVertex( v );
		i.vMeshTint = v.vMeshTint;
		return FinalizeDeadlockVertex( i );
	}
}

PS
{
	// Drawn with the translucent objects, after what it shows, without blending.
	#define DEPTH_STATE_ALREADY_SET 1
	#define S_TRANSLUCENT 1

	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );

	BoolAttribute( bWantsFBCopyTexture, true );
	BoolAttribute( DoNotCastShadows, true );
	Texture2D g_tFrameBufferCopyTexture < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;

	SamplerState g_sVeilAnisoClamp < Filter( ANISOTROPIC ); MaxAniso( 8 ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	// r refraction, g hue shift.
	CreateInputTexture2D( DeadlockMasks, Linear, 8, "None", "_mask", "Veil", Default4( 0.5, 0.5, 0.5, 0 ) );
	// r warp, g reveal.
	CreateInputTexture2D( DeadlockWarpMask, Linear, 8, "None", "_mask", "Veil", Default4( 0.5, 0.5, 0.5, 0 ) );

	Texture2D g_tMasks < Channel( RGBA, Box( DeadlockMasks ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tWarpMask < Channel( RGBA, Box( DeadlockWarpMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	TextureAttribute( RepresentativeTexture, g_tWarpMask );

	float g_flRevealMax < Default( 1 ); >;
	float2 g_vWarpMaskBrightnessAmounts < Default2( 1, 1 ); >;
	float g_flDoubleSidedVeil < Default( 0 ); >;
	float2 g_vHueShiftAmounts < Default2( -0.1, 0.1 ); >;
	float2 g_vRefractBlurAmounts < Default2( 0.005, 0.005 ); >;
	float2 g_vRefractBlurAmountsVeiled < Default2( 0.005, 0.005 ); >;
	float2 g_vRefractBrightnessAmounts < Default2( 1, 0.2 ); >;
	float2 g_vRefractBrightnessAmountsVeiled < Default2( 1, 0.2 ); >;
	float2 g_vRefractNoiseAmounts < Default2( 0.5, 0.5 ); >;
	float2 g_vRefractNoiseAmountsVeiled < Default2( 0.5, 0.5 ); >;
	float g_flSmokeRateVariance < Default( -0.8 ); >;
	float g_flSmokeSpeed < Default( 1 ); >;
	float g_flSmokeStepRotation < Default( 0.01 ); >;
	float g_flSmokeStepSize < Default( 0.005 ); >;
	float2 g_vSmokeSpatialFrequencies < Default2( 12, 17 ); >;

	// The game's sine and cosine of an angle: a parabola over each half turn.
	float2 VeilSinCos( float angle )
	{
		float2 turns = float2( angle, angle + 1.5707964 );
		float4 a = ( turns - floor( turns * 0.15915494 ) * 6.2831855 ).xxyy * 0.63661975;
		float4 b = saturate( ( float4( 1, 3, 1, 3 ) - a ) * ( a + float4( -1, -3, -1, -3 ) ) + 1 );
		return b.xz - b.yw;
	}

	// The smoke: the texture coordinates pushed along a sine field 25 times, turning a little each step.
	float2 VeilSmoke( float2 uv )
	{
		float variance = 0.5 * g_flSmokeRateVariance;
		float speedY = ( 0.5 + variance ) * g_flSmokeSpeed;
		float speedX = ( 0.5 - variance ) * g_flSmokeSpeed;
		float2 turn = VeilSinCos( g_flSmokeStepRotation );

		float2 p = uv * 2 - 1;

		[loop]
		for ( int step = 0; step < 25; step++ )
		{
			float2 push = float2(
				VeilSinCos( g_vSmokeSpatialFrequencies.y * p.y + g_flTime * speedY ).x,
				VeilSinCos( g_vSmokeSpatialFrequencies.x * p.x + g_flTime * speedX ).y );

			float2 q = p + push * g_flSmokeStepSize;
			p = float2( turn.y * q.x - turn.x * q.y, turn.x * q.x + turn.y * q.y );
		}

		return p * 0.5 + 0.5;
	}

	float3 Behind( float2 viewportUv )
	{
		return g_tFrameBufferCopyTexture.SampleLevel( g_sPointClamp, saturate( viewportUv ) * g_vFrameBufferCopyInvSizeAndUvScale.zw, 0 ).rgb;
	}

	float3 VeilRgbToHsv( float3 c )
	{
		float value = max( c.r, max( c.g, c.b ) );
		float range = value - min( c.r, min( c.g, c.b ) );
		if ( range == 0 )
			return float3( 0, 0, value );

		float3 t = ( value.xxx - c ) / range;
		float3 u = t.xyz - t.zxy;
		float2 w = u.xy + float2( 2, 4 );

		float hue = c.r >= value ? u.z : ( c.g >= value ? w.x : w.y );
		return float3( frac( hue / 6 ), range / value, value );
	}

	float3 VeilHsvToRgb( float hue, float saturation, float value )
	{
		if ( saturation == 0 )
			return value.xxx;

		float h = hue * 6;
		float sector = floor( h );
		float f = h - sector;
		float p = value * ( 1 - saturation );
		float q = value * ( 1 - saturation * f );
		float t = value * ( 1 - saturation * ( 1 - f ) );

		if ( sector == 0 ) return float3( value, t, p );
		if ( sector == 1 ) return float3( q, value, p );
		if ( sector == 2 ) return float3( p, value, t );
		if ( sector == 3 ) return float3( p, q, value );
		if ( sector == 4 ) return float3( t, p, value );
		return float3( value, p, q );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		bool veiled = i.face || g_flDoubleSidedVeil > 0.5;

		float2 warpMask = g_tWarpMask.Sample( g_sVeilAnisoClamp, uv ).rg;
		float warp = warpMask.r;

		// The reveal mask against the object tint's alpha.
		float revealAlpha = ( i.vVertexColor * i.vMeshTint ).a;
		if ( revealAlpha * g_flRevealMax < warpMask.g )
			discard;

		float2 smokeUv = warp > 0 ? lerp( uv, VeilSmoke( uv ), warp ) : uv;
		float2 masks = g_tMasks.Sample( g_sVeilAnisoClamp, smokeUv ).rg;
		float refract = masks.r;

		float noise = veiled ? lerp( g_vRefractNoiseAmountsVeiled.x, g_vRefractNoiseAmountsVeiled.y, refract ) : lerp( g_vRefractNoiseAmounts.x, g_vRefractNoiseAmounts.y, refract );
		float blur = veiled ? lerp( g_vRefractBlurAmountsVeiled.x, g_vRefractBlurAmountsVeiled.y, refract ) : lerp( g_vRefractBlurAmounts.x, g_vRefractBlurAmounts.y, refract );

		// What's behind, jittered by blue noise and averaged over a disc of taps.
		float2 viewportUv = ( i.vPositionSs.xy - g_vViewportOffset ) * g_vInvViewportSize;
		uint2 noiseSize;
		g_tBlueNoise.GetDimensions( noiseSize.x, noiseSize.y );
		float2 jitter = g_tBlueNoise.Load( int3( uint2( i.vPositionSs.xy ) % noiseSize, 0 ) ).xy * 2 - 1;
		float2 center = viewportUv + jitter * blur * noise;

		float3 color = Behind( center );
		color += Behind( center + float2( -0.0876, 0.9703 ) * blur );
		color += Behind( center + float2( 0.4802, 0.5651 ) * blur );
		color += Behind( center + float2( 0.1851, 0.1580 ) * blur );
		color += Behind( center + float2( -0.2616, -0.0617 ) * blur );
		color += Behind( center + float2( -0.5477, -0.6603 ) * blur );
		color += Behind( center + float2( -0.5325, 0.0711 ) * blur );
		color += Behind( center + float2( -0.0751, -0.8954 ) * blur );
		color += Behind( center + float2( 0.6384, -0.4054 ) * blur );
		color /= 9;

		float2 brightness;
		if ( veiled )
		{
			color = dot( color, float3( 0.2125, 0.7154, 0.0721 ) ).xxx;
			brightness = g_vRefractBrightnessAmountsVeiled;
		}
		else
		{
			float peak = max( color.r, max( color.g, color.b ) );
			float3 shifted = color;
			if ( peak > 0 )
			{
				float3 hsv = VeilRgbToHsv( color / peak );
				float hue = hsv.x + lerp( g_vHueShiftAmounts.x, g_vHueShiftAmounts.y, masks.g );
				shifted = VeilHsvToRgb( hue, hsv.y, hsv.z ) * peak;
			}

			color = lerp( color, shifted, lerp( 0.7, 1.0, refract ) );
			brightness = g_vRefractBrightnessAmounts;
		}

		color *= lerp( brightness.x, brightness.y, refract ) * lerp( g_vWarpMaskBrightnessAmounts.x, g_vWarpMaskBrightnessAmounts.y, warp );

		float3 positionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		return DoAtmospherics( positionWs, i.vPositionSs.xy, float4( color, 1 ) );
	}
}
