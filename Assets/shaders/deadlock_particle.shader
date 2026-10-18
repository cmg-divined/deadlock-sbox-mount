//=========================================================================================================================
// Deadlock's spritecard particles (sprites, trails and ropes), as ValveResourceFormat's particle_spritecard shader
// draws them: up to five texture layers mixed by channel and blend mode, sheet frames cross-faded, alpha remapped, HSV
// shifted and desaturated, lit the way the game lights particles (the light probes along the diagonal, the sun and
// the light2 lights, no surface facing), feathered against the scene and fogged.
//
// The quads are built on the CPU (Code/Deadlock/Particles/Renderers), corners in world space with every layer's
// coordinates worked out. They draw premultiplied, so alpha blending and additive share one blend state (additive
// writes no alpha); modulate (mod2x) has its own.
//=========================================================================================================================

HEADER
{
	Description = "Deadlock particle spritecard";
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
	#include "common/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionWs : POSITION < Semantic( None ); >;
	float4 vColor : COLOR0 < Semantic( None ); >;
	float4 vLayer0 : TEXCOORD0 < Semantic( None ); >;
	float4 vLayer1 : TEXCOORD1 < Semantic( None ); >;
	float4 vLayer2 : TEXCOORD2 < Semantic( None ); >;
	float4 vLayer3 : TEXCOORD3 < Semantic( None ); >;
	float4 vLayer4 : TEXCOORD4 < Semantic( None ); >;
	float4 vCard : TEXCOORD5 < Semantic( None ); >;
	float4 vLightingPosition : TEXCOORD6 < Semantic( None ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vLayer1 : TEXCOORD5;
	float4 vLayer2 : TEXCOORD8;
	float4 vLayer3 : TEXCOORD9;
	float4 vLayer4 : TEXCOORD10;
	float4 vCard : TEXCOORD12;				// frame blend, card coordinates
	float3 vLightingPosition : TEXCOORD13;	// where the particle is lit from, in the world
};

VS
{
	float g_flDepthBias < Attribute( "DepthBias" ); Default( 0 ); >;
	int g_nGammaCorrectVertexColors < Attribute( "GammaCorrectVertexColors" ); Default( 1 ); >;
	float3 g_vColorScale < Attribute( "ColorScale" ); Default3( 1, 1, 1 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i;

		i.vPositionWs = v.vPositionWs - g_vHighPrecisionLightingOffsetWs.xyz;
		i.vPositionPs = Position3WsToPs( v.vPositionWs );

		// Depth bias: the depth of the point moved along the view by the bias, where it's drawn.
		if ( g_flDepthBias != 0.0 )
		{
			float4 biased = Position3WsToPs( v.vPositionWs + g_vCameraDirWs * g_flDepthBias );
			i.vPositionPs.z = i.vPositionPs.w * ( biased.z / biased.w );
		}

		float4 color = v.vColor;
		if ( g_nGammaCorrectVertexColors != 0 )
			color.rgb = SrgbGammaToLinear( color.rgb );
		color.rgb *= g_vColorScale;

		i.vVertexColor = color;
		i.vNormalWs = -g_vCameraDirWs;
		i.vTextureCoords = v.vLayer0;
		i.vLightmapUV = 0;
		i.vLayer1 = v.vLayer1;
		i.vLayer2 = v.vLayer2;
		i.vLayer3 = v.vLayer3;
		i.vLayer4 = v.vLayer4;
		i.vCard = v.vCard;
		i.vLightingPosition = v.vLightingPosition.xyz;

		return i;
	}
}

PS
{
	#define S_TRANSLUCENT 1
	#define BLEND_MODE_ALREADY_SET 1
	#define DEPTH_STATE_ALREADY_SET 1
	#define CULL_MODE_ALREADY_SET 1

	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "deadlock_lighting.hlsl"

	// Modulate (mod2x) multiplies what's behind; opaque ropes write depth and don't blend.
	DynamicCombo( D_MOD2X, 0..1, Sys( ALL ) );
	DynamicCombo( D_OPAQUE, 0..1, Sys( ALL ) );

	RenderState( CullMode, NONE );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );

	#if D_OPAQUE
		RenderState( DepthWriteEnable, true );
		RenderState( BlendEnable, false );
	#else
		RenderState( DepthWriteEnable, false );
		RenderState( BlendEnable, true );
		#if D_MOD2X
			RenderState( SrcBlend, DEST_COLOR );
			RenderState( DstBlend, SRC_COLOR );
		#else
			RenderState( SrcBlend, ONE );
			RenderState( DstBlend, INV_SRC_ALPHA );
		#endif
	#endif

	#define PARTICLE_OUTPUT_BLEND_MODE_ADD 1
	#define PARTICLE_OUTPUT_BLEND_MODE_MOD2X 5

	#define SPRITECARD_TEXTURE_ZOOM 1
	#define SPRITECARD_TEXTURE_1D_COLOR_LOOKUP 2
	#define SPRITECARD_TEXTURE_UVDISTORTION 3
	#define SPRITECARD_TEXTURE_UVDISTORTION_ZOOM 4
	#define SPRITECARD_TEXTURE_ANIMMOTIONVEC 6

	#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGB 0
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA 1
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_A 2
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_A 3
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_ALPHAMASK 4
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_RGBMASK 5
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA_RGBALPHA 6
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_A_RGBALPHA 7
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_A_RGBALPHA 8
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_R 9
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_G 10
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_B 11
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_RALPHA 12
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_GALPHA 13
	#define SPRITECARD_TEXTURE_CHANNEL_MIX_BALPHA 14

	#define SPRITECARD_TEXTURE_BLEND_MULTIPLY 0
	#define SPRITECARD_TEXTURE_BLEND_MOD2X 1
	#define SPRITECARD_TEXTURE_BLEND_REPLACE 2
	#define SPRITECARD_TEXTURE_BLEND_ADD 3
	#define SPRITECARD_TEXTURE_BLEND_SUBTRACT 4
	#define SPRITECARD_TEXTURE_BLEND_AVERAGE 5
	#define SPRITECARD_TEXTURE_BLEND_LUMINANCE 6

	#define ParticleLuma601 float3( 0.299, 0.587, 0.114 )
	#define ParticleLuma float3( 0.2125, 0.7154, 0.0721 )

	int g_nBlendMode < Attribute( "BlendMode" ); Default( 0 ); >;
	float g_flOverbrightFactor < Attribute( "OverbrightFactor" ); Default( 1 ); >;
	float g_flAddSelfAmount < Attribute( "AddSelfAmount" ); Default( 1 ); >;
	float g_flDiffuseAmount < Attribute( "DiffuseAmount" ); Default( 1 ); >;
	float g_flSelfIllumAmount < Attribute( "SelfIllumAmount" ); Default( 0 ); >;
	float g_flDesaturation < Attribute( "Desaturation" ); Default( 0 ); >;
	float3 g_vHsvShift < Attribute( "HsvShift" ); Default3( 0, 1, 1 ); >;
	float2 g_vAlphaRemapRange < Attribute( "AlphaRemapRange" ); Default2( 1, 0 ); >;
	int g_nBlendFrames < Attribute( "BlendFrames" ); Default( 1 ); >;
	int g_nMaxLuminanceFrameBlend < Attribute( "MaxLuminanceFrameBlend" ); Default( 0 ); >;
	int g_nOutline < Attribute( "Outline" ); Default( 0 ); >;
	float4 g_vOutlineColor < Attribute( "OutlineColor" ); Default4( 1, 1, 1, 1 ); >;
	float4 g_vOutlineRanges < Attribute( "OutlineRanges" ); Default4( 0.5, 0.7, 0.6, 0.8 ); >;
	float2 g_vFeatheringRange < Attribute( "FeatheringRange" ); Default2( 0, 0 ); >;
	float g_flFeatheringFilter < Attribute( "FeatheringFilter" ); Default( 1 ); >;
	float2 g_vMotionVectorScale < Attribute( "MotionVectorScale" ); Default2( 0, 0 ); >;

	int g_nLayerCount < Attribute( "LayerCount" ); Default( 1 ); >;

	// Bindless texture indices, already pointing at the sRGB view where the layer is colour.
	int g_nLayer0Texture < Attribute( "Layer0Texture" ); Default( 0 ); >;
	int g_nLayer1Texture < Attribute( "Layer1Texture" ); Default( 0 ); >;
	int g_nLayer2Texture < Attribute( "Layer2Texture" ); Default( 0 ); >;
	int g_nLayer3Texture < Attribute( "Layer3Texture" ); Default( 0 ); >;
	int g_nLayer4Texture < Attribute( "Layer4Texture" ); Default( 0 ); >;

	// Channels, blend mode, blend amount and effect per layer.
	float4 g_vLayer0Settings < Attribute( "Layer0Settings" ); Default4( 1, 0, 1, 0 ); >;
	float4 g_vLayer1Settings < Attribute( "Layer1Settings" ); Default4( 1, 0, 1, 0 ); >;
	float4 g_vLayer2Settings < Attribute( "Layer2Settings" ); Default4( 1, 0, 1, 0 ); >;
	float4 g_vLayer3Settings < Attribute( "Layer3Settings" ); Default4( 1, 0, 1, 0 ); >;
	float4 g_vLayer4Settings < Attribute( "Layer4Settings" ); Default4( 1, 0, 1, 0 ); >;

	// UV distortion strength and zoom rate per layer.
	float4 g_vLayer0Effects < Attribute( "Layer0Effects" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vLayer1Effects < Attribute( "Layer1Effects" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vLayer2Effects < Attribute( "Layer2Effects" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vLayer3Effects < Attribute( "Layer3Effects" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vLayer4Effects < Attribute( "Layer4Effects" ); Default4( 0, 0, 0, 0 ); >;

	float ParticleMax3( float3 v ) { return max( max( v.x, v.y ), v.z ); }
	float ParticleMin3( float3 v ) { return min( min( v.x, v.y ), v.z ); }

	float3 ParticleLinearToGamma( float3 color )
	{
		float3 linearSegment = color * 12.92;
		float3 expSegment = 1.055 * pow( max( color, 0.0 ), 1.0 / 2.4 ) - 0.055;
		return lerp( expSegment, linearSegment, step( color, 0.0031308 ) );
	}

	int LayerTexture( int layer )
	{
		switch ( layer )
		{
			case 1: return g_nLayer1Texture;
			case 2: return g_nLayer2Texture;
			case 3: return g_nLayer3Texture;
			case 4: return g_nLayer4Texture;
		}
		return g_nLayer0Texture;
	}

	float4 LayerSettings( int layer )
	{
		switch ( layer )
		{
			case 1: return g_vLayer1Settings;
			case 2: return g_vLayer2Settings;
			case 3: return g_vLayer3Settings;
			case 4: return g_vLayer4Settings;
		}
		return g_vLayer0Settings;
	}

	float4 LayerEffects( int layer )
	{
		switch ( layer )
		{
			case 1: return g_vLayer1Effects;
			case 2: return g_vLayer2Effects;
			case 3: return g_vLayer3Effects;
			case 4: return g_vLayer4Effects;
		}
		return g_vLayer0Effects;
	}

	// A layer's coordinates in the current frame (xy) and the next (zw).
	float4 LayerUvs( PixelInput i, int layer )
	{
		switch ( layer )
		{
			case 1: return i.vLayer1;
			case 2: return i.vLayer2;
			case 3: return i.vLayer3;
			case 4: return i.vLayer4;
		}
		return i.vTextureCoords;
	}

	float4 SampleLayer( int layer, float2 uv )
	{
		int index = LayerTexture( layer );
		return index > 0 ? Bindless::GetTexture2D( index ).Sample( g_sTrilinearWrap, uv ) : float4( 1, 1, 1, 1 );
	}

	// Zooms in or out on the centre, between a wide and a tight sample.
	float4 ZoomSample( int layer, float2 uv, float rate )
	{
		float phase = frac( rate );
		float eased = phase / ( ( -0.4 * ( 1.0 - phase ) ) + 1.0 );
		float2 centred = uv - 0.5;
		float4 wide = SampleLayer( layer, ( centred * lerp( 4.0, 1.0, eased ) ) + 0.5 );
		float4 tight = SampleLayer( layer, ( centred * lerp( 1.0, 0.0625, eased ) ) + 0.5 );
		return lerp( tight, wide, eased * eased );
	}

	// Distorts the previous layer's sample by this one's red and green.
	float4 DistortPrevious( int previousLayer, float4 source, float2 previousUv, float strength )
	{
		float2 offset = ( ParticleLinearToGamma( source.rgb ).xy - 0.5 ) * 2.0;
		return SampleLayer( previousLayer, previousUv - ( offset * ( strength * 0.125 * source.a ) ) );
	}

	// A one dimensional colour lookup by the luminance of what's below.
	float4 SampleGradientRamp( int layer, float4 previous, int maskMode )
	{
		// Per channel mode: whether the ramp reads straight colour (x) or alpha (y).
		float2 flags = float2( 0, 0 );
		if ( maskMode == 0 || maskMode == 4 || maskMode == 5 || maskMode == 9 || maskMode == 10 || maskMode == 11 )
			flags.x = 1.0;
		if ( maskMode == 2 || maskMode == 7 )
			flags.y = 1.0;

		float4 source = lerp( lerp( float4( previous.rgb * previous.aaa, 1.0 ), previous, flags.x ), previous.aaaa, flags.y );
		float4 ramp = SampleLayer( layer, float2( dot( ParticleLinearToGamma( source.rgb ), ParticleLuma ), 0.5 ) );
		return lerp( float4( ramp.rgb, previous.a ), float4( ramp.rgb, dot( ramp.rgb, ParticleLuma601 ) ), flags.y );
	}

	float2 SampleFlow( int layer, float2 uv )
	{
		return ( ( SampleLayer( layer, uv ).yw * 2.0 ) - 1.0 ) * g_vMotionVectorScale;
	}

	// Subtract zeroes the accumulator's contribution in pass-through modes, so subtracting a colour only layer doesn't
	// also subtract alpha it never supplied.
	float4 ApplyChannelMask( float4 accum, float4 layer, int maskMode, float sub )
	{
		switch ( maskMode )
		{
			case SPRITECARD_TEXTURE_CHANNEL_MIX_RGB:
				return float4( layer.rgb, lerp( accum.a, 0.0, sub ) );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_A:
				return float4( lerp( accum.rgb, 0.0, sub ), layer.a );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_A:
				return float4( layer.rgb, accum.a * layer.a );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_ALPHAMASK:
				return float4( lerp( accum.rgb, layer.rgb, layer.a ), accum.a );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_RGBMASK:
				return float4( lerp( accum.rgb, layer.rgb, dot( layer.rgb, ParticleLuma601 ) ), accum.a );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA_RGBALPHA:
				return float4( layer.rgb, dot( layer.rgb, ParticleLuma601 ) );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_A_RGBALPHA:
				return float4( lerp( accum.rgb, 0.0, sub ), dot( layer.rgb, ParticleLuma601 ) );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_A_RGBALPHA:
				return float4( accum.rgb, accum.a * dot( layer.rgb, ParticleLuma601 ) );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_R:
				return float4( layer.r, lerp( float3( layer.r, layer.r, accum.a ), 0.0, sub ) );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_G:
				return float4( lerp( layer.g, 0.0, sub ), layer.g, lerp( float2( layer.g, accum.a ), 0.0, sub ) );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_B:
				return float4( lerp( float2( layer.b, layer.b ), 0.0, sub ), layer.b, lerp( accum.a, 0.0, sub ) );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_RALPHA:
				return float4( layer.r, lerp( float2( layer.r, layer.r ), 0.0, sub ), layer.r );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_GALPHA:
				return float4( lerp( layer.g, 0.0, sub ), layer.g, lerp( layer.g, 0.0, sub ), layer.g );
			case SPRITECARD_TEXTURE_CHANNEL_MIX_BALPHA:
				return float4( lerp( float2( layer.b, layer.b ), 0.0, sub ), layer.b, layer.b );
		}
		return layer;
	}

	float4 ApplyLayerBlend( float4 accum, float4 masked, int blendMode )
	{
		switch ( blendMode )
		{
			case SPRITECARD_TEXTURE_BLEND_MOD2X: return accum * masked * 2.0;
			case SPRITECARD_TEXTURE_BLEND_REPLACE: return masked;
			case SPRITECARD_TEXTURE_BLEND_ADD: return accum + masked;
			case SPRITECARD_TEXTURE_BLEND_SUBTRACT: return accum - masked;
			case SPRITECARD_TEXTURE_BLEND_AVERAGE: return ( accum + masked ) * 0.5;
			case SPRITECARD_TEXTURE_BLEND_LUMINANCE: return float4( accum.rgb * dot( masked.rgb, ParticleLuma ), masked.a );
		}
		return accum * masked;
	}

	float3 ApplyHsvShift( float3 sourceColor )
	{
		float overbright = max( ParticleMax3( sourceColor ), 1.0 );
		float3 rgb = saturate( sourceColor / overbright );
		float valueMax = ParticleMax3( rgb );
		float chroma = valueMax - ParticleMin3( rgb );
		float3 hsv = float3( 0.0, 0.0, valueMax );

		if ( chroma != 0.0 )
		{
			float3 distance = ( valueMax - rgb ) / chroma;
			float3 delta = distance - distance.zxy;

			if ( rgb.r >= valueMax ) hsv = float3( delta.z, chroma / valueMax, valueMax );
			else if ( rgb.g >= valueMax ) hsv = float3( delta.x + 2.0, chroma / valueMax, valueMax );
			else hsv = float3( delta.y + 4.0, chroma / valueMax, valueMax );

			hsv.x = frac( hsv.x * ( 1.0 / 6.0 ) );
		}

		float hue = frac( hsv.x + g_vHsvShift.x );
		float2 satVal = saturate( hsv.yz * g_vHsvShift.yz );

		if ( satVal.x == 0.0 )
			return satVal.y * overbright;

		float sector = hue * 6.0;
		float index = floor( sector );
		float f = sector - index;
		float p = satVal.y * ( 1.0 - satVal.x );
		float q = satVal.y * ( 1.0 - ( satVal.x * f ) );
		float t = satVal.y * ( 1.0 - ( satVal.x * ( 1.0 - f ) ) );

		if ( index == 0.0 ) return float3( satVal.y, t, p ) * overbright;
		if ( index == 1.0 ) return float3( q, satVal.y, p ) * overbright;
		if ( index == 2.0 ) return float3( p, satVal.y, t ) * overbright;
		if ( index == 3.0 ) return float3( p, q, satVal.y ) * overbright;
		if ( index == 4.0 ) return float3( t, p, satVal.y ) * overbright;
		return float3( satVal.y, p, q ) * overbright;
	}

	//
	// The game lights a particle at one point, facing nowhere in particular: the light probes' irradiance averaged
	// along the diagonal both ways, the sun behind its baked and cascade shadows, and the light2 lights. Without a
	// map's lighting it's lit as fullbright.
	//
	float3 ParticleLighting( float3 positionWs, float4 screenPosition )
	{
		if ( !DeadlockLightingEnabled() )
			return 1.0;

		float3 position = DeadlockToMap( positionWs );
		const float3 diagonal = float3( 0.57735027, 0.57735027, 0.57735027 );

		float3 irradiance;
		float4 bakedShadows;
		float3 specular;
		float luminance;
		DeadlockVolumes( position, diagonal, diagonal, 1.0, true, false, irradiance, bakedShadows, specular, luminance );

		float3 ambient = 0.5 * ( irradiance + DeadlockProbeIrradiance( position, -diagonal ) );

		float sun = saturate( 1.0 - dot( bakedShadows, g_vDeadlockSunShadowMask ) );
		if ( sun > 0.0 && g_DirectionalLightEnabled && g_DirectionalLightCascadeCount > 0 )
			sun *= DirectionalLightShadow::GetVisibilityWithOffset( positionWs - g_vHighPrecisionLightingOffsetWs.xyz, -g_vCameraDirWs, screenPosition );

		float3 direct = g_vDeadlockSunColor * sun;

		DeadlockGridRange lights = DeadlockGridLookup( position, 1, (uint)g_nDeadlockBarnLightCount );

		[loop]
		for ( uint k = 0; k < lights.Count; k++ )
		{
			float3 toLight;
			float3 color;
			if ( DeadlockBarnLightAt( g_DeadlockBarnLights[DeadlockGridItem( lights, k )], position, bakedShadows, toLight, color ) )
				direct += color;
		}

		return ambient + direct;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float frameBlend = i.vCard.x;
		float2 cardUv = i.vCard.yz;
		float3 positionWs = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;

		//
		// Texture layers
		//
		float4 accum = 1.0;
		float2 previousUv = i.vTextureCoords.xy;
		int previousLayer = 0;
		float2 flow = 0;
		float2 flowNext = 0;

		// A motion vector sheet moves the first two colour layers' frames towards each other.
		[loop]
		for ( int m = 1; m < g_nLayerCount; m++ )
		{
			if ( (int)LayerSettings( m ).w == SPRITECARD_TEXTURE_ANIMMOTIONVEC )
			{
				float4 motionUvs = LayerUvs( i, m );
				flow = SampleFlow( m, motionUvs.xy ) * frameBlend;
				flowNext = SampleFlow( m, motionUvs.zw ) * ( 1.0 - frameBlend );
				break;
			}
		}

		int colourLayers = 0;

		[loop]
		for ( int l = 0; l < g_nLayerCount; l++ )
		{
			float4 settings = LayerSettings( l );
			int effectMode = (int)settings.w;

			if ( effectMode == SPRITECARD_TEXTURE_ANIMMOTIONVEC )
				continue;

			float4 uvs = LayerUvs( i, l );
			float2 layerUv = uvs.xy;
			float2 layerUvNext = uvs.zw;

			if ( colourLayers < 2 )
			{
				layerUv -= flow;
				layerUvNext += flowNext;
			}

			colourLayers++;

			float4 layer = SampleLayer( l, layerUv );

			if ( g_nBlendFrames != 0 )
			{
				float4 nextFrame = SampleLayer( l, layerUvNext );

				if ( l == 0 && g_nMaxLuminanceFrameBlend != 0 )
				{
					float weight = ( 1.0 - frameBlend ) * sqrt( max( 0.001, dot( layer.rgb, ParticleLuma ) ) );
					float nextWeight = frameBlend * sqrt( max( 0.001, dot( nextFrame.rgb, ParticleLuma ) ) );
					layer = ( ( layer * weight ) + ( nextFrame * nextWeight ) ) / ( weight + nextWeight );
				}
				else
				{
					layer = lerp( layer, nextFrame, frameBlend );
				}
			}

			float4 effects = LayerEffects( l );

			if ( effectMode == SPRITECARD_TEXTURE_ZOOM || effectMode == SPRITECARD_TEXTURE_UVDISTORTION_ZOOM )
				layer = ZoomSample( l, layerUv, effects.y );

			if ( effectMode == SPRITECARD_TEXTURE_UVDISTORTION || effectMode == SPRITECARD_TEXTURE_UVDISTORTION_ZOOM )
				layer = DistortPrevious( previousLayer, layer, previousUv, effects.x );

			previousUv = layerUv;
			previousLayer = l;

			int maskMode = (int)settings.x;

			if ( effectMode == SPRITECARD_TEXTURE_1D_COLOR_LOOKUP )
				layer = SampleGradientRamp( l, l == 0 ? layer : accum, maskMode );

			int blendMode = (int)settings.y;
			float sub = blendMode == SPRITECARD_TEXTURE_BLEND_SUBTRACT ? 1.0 : 0.0;
			float4 masked = ApplyChannelMask( accum, layer, maskMode, sub );
			float4 blended = ApplyLayerBlend( accum, masked, blendMode );
			accum = max( lerp( accum, blended, settings.z ), 0.0 );
		}

		//
		// Colour and alpha
		//
		float3 color = i.vVertexColor.rgb * accum.rgb;

		if ( any( g_vHsvShift != float3( 0.0, 1.0, 1.0 ) ) )
			color = ApplyHsvShift( color );

		float rawAlpha = accum.a;
		float alpha = rawAlpha;

		if ( g_vAlphaRemapRange.x < g_vAlphaRemapRange.y )
			alpha = smoothstep( g_vAlphaRemapRange.x, g_vAlphaRemapRange.y, rawAlpha );

		if ( g_nOutline != 0 && rawAlpha >= g_vOutlineRanges.x && rawAlpha <= g_vOutlineRanges.w )
		{
			float outlineRamp = rawAlpha <= g_vOutlineRanges.z
				? smoothstep( g_vOutlineRanges.x, g_vOutlineRanges.z, rawAlpha )
				: 1.0 - smoothstep( g_vOutlineRanges.y, g_vOutlineRanges.w, rawAlpha );

			color = lerp( color, g_vOutlineColor.rgb, outlineRamp );
			alpha = lerp( alpha, g_vOutlineColor.a, outlineRamp );
		}

		color = lerp( color, dot( color, ParticleLuma ), g_flDesaturation );
		alpha *= i.vVertexColor.a;

		// Soft particles: fade out where the card nears the scene behind it.
		if ( g_vFeatheringRange.y > g_vFeatheringRange.x )
		{
			float3 scenePosition = Depth::GetWorldPosition( i.vPositionSs.xy );
			float spread = 1.0 - alpha;
			float halfWidening = 1.0;

			if ( g_nBlendFrames == 0 )
			{
				spread += length( ( cardUv * 2.0 ) - 1.0 );
				halfWidening = 0.5;
			}

			float offset = spread * ( g_vFeatheringRange.y * g_flFeatheringFilter * halfWidening );
			alpha *= saturate( ( distance( positionWs, scenePosition ) - offset - g_vFeatheringRange.x ) / ( g_vFeatheringRange.y - g_vFeatheringRange.x ) );
		}

		#if D_OPAQUE
			clip( alpha - 0.5 );
		#endif

		//
		// Modulate: what's behind times twice the colour, grey (0.5) where transparent.
		//
		#if D_MOD2X
			float3 mod2x = lerp( 0.5, lerp( 0.5, color, i.vVertexColor.rgb ), alpha );
			return float4( saturate( mod2x ), alpha );
		#else
			//
			// Lighting
			//
			float3 lit = color * g_flSelfIllumAmount;

			[branch]
			if ( g_flDiffuseAmount != 0.0 )
				lit += color * ParticleLighting( i.vLightingPosition, i.vPositionSs ) * g_flDiffuseAmount;

			color = g_flOverbrightFactor * lit * g_flAddSelfAmount;

			bool additive = g_nBlendMode == PARTICLE_OUTPUT_BLEND_MODE_ADD;

			#if D_OPAQUE
				return DoAtmospherics( positionWs, i.vPositionSs.xy, float4( color, 1.0 ) );
			#else
				// Additive draws over a fogged scene, so it's dimmed by the fog in front of it rather than fogged again.
				float4 fogged = DoAtmospherics( positionWs, i.vPositionSs.xy, float4( color, 1.0 ), additive );
				color = additive ? color * fogged.a : fogged.rgb;

				return float4( color * alpha, additive ? 0.0 : alpha );
			#endif
		#endif
	}
}
