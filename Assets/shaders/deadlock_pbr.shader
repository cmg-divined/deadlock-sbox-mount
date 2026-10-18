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
	FeatureRule( Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT ), "Additive requires translucency" );
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
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );
	StaticCombo( S_UNLIT, F_UNLIT, Sys( ALL ) );
	StaticCombo( S_USE_NPR_LIGHTING, F_USE_NPR_LIGHTING, Sys( ALL ) );

	// sbox_pixel supplies culling; suppress the duplicate declaration in vr_common_ps_code.
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "deadlock_lighting.hlsl"

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
	float g_flUnlitAdditiveScale < Default( 1 ); >;

	//
	// Non-photorealistic lighting. Deadlock drives these from the engine rather than the material,
	// so these defaults are tuned by eye.
	//
	float g_flNprDirectLightWrap < Default( 0.6 ); Range( 0, 1 ); UiGroup( "NPR" ); >;
	float g_flNprDiffuseStepSharpness < Default( 0.6 ); Range( 0, 0.99 ); UiGroup( "NPR" ); >;
	float g_flNprDirectLightNormalization < Default( 0.85 ); Range( 0, 2 ); UiGroup( "NPR" ); >;
	float g_flNprDiffusePbrBlend < Default( 0.25 ); Range( 0, 1 ); UiGroup( "NPR" ); >;
	float g_flNprBounceDiffuse < Default( 1 ); Range( 0, 1 ); UiGroup( "NPR" ); >;
	float g_flNprTransmissiveScale < Default( 1 ); Range( 0, 4 ); UiGroup( "NPR" ); >;
	float g_flNprRimLightStrength < Default( 2.5 ); Range( 0, 10 ); UiGroup( "NPR" ); >;
	float g_flNprRimLightWrap < Default( 1 ); Range( 0, 2 ); UiGroup( "NPR" ); >;
	float g_flNprRimLightFalloff < Default( 1.5 ); Range( 0.1, 8 ); UiGroup( "NPR" ); >;
	float2 g_vNprRimLightUpRamp < Default2( -0.3, 0.7 ); UiGroup( "NPR" ); >;

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
	// blended back towards the standard term.
	float NprDiffuse( float flNDotL, float flRoughness )
	{
		float wrapped = saturate( 0.5 + ( ( g_flNprDirectLightWrap - 0.5 ) + flNDotL - 0.5 ) * 2.0 );
		float toon = NprGain( wrapped, 1.0 / ( 1.0 - g_flNprDiffuseStepSharpness ) );

		float exponent = ( 1.0 - flRoughness ) * 0.8 + 0.6;
		float standard = pow( flNDotL, exponent ) * ( exponent + 1.0 ) * 0.5;

		return lerp( g_flNprDirectLightNormalization * toon, standard, g_flNprDiffusePbrBlend );
	}

	float3 AmbientDiffuse( CombinerInput combiner, float3 normal )
	{
		LightingTerms_t terms = InitLightingTerms();
		combiner.vNormalWs = normal;
		CalculateIndirectLighting( terms, combiner );
		return terms.vIndirectDiffuse.rgb;
	}

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

		// Direct diffuse, recomputed with the toon curve. Derivatives need uniform control flow, so
		// the receiver normal is taken before any loop.
		const float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );
		float3 directDiffuse = 0;
		float3 primaryLight = float3( 0, 0, 1 );
		float primaryVisibility = 1;

		if ( g_DirectionalLightEnabled )
		{
			primaryLight = normalize( -g_DirectionalLightDirection.xyz );

			if ( g_DirectionalLightCascadeCount > 0 )
				primaryVisibility = DirectionalLightShadow::GetVisibilityWithOffset( m.WorldPositionWithOffset, receiverNormal, m.ScreenPosition );

			// Baked surfaces receive the sun through the static light loop below.
			if ( !LightmappedLight::UsesLightmaps() && !ProbeLight::UsesProbes() )
			{
				float flNDotL = dot( m.Normal, primaryLight );
				if ( flNDotL > 0 )
					directDiffuse += g_DirectionalLightColor.rgb * NprDiffuse( flNDotL, m.Roughness ) * primaryVisibility;
			}
		}

		uint lightCount = Light::Count( m.ScreenPosition );

		[loop]
		for ( uint index = 0; index < lightCount; index++ )
		{
			Light light = Light::From( m.WorldPosition, m.ScreenPosition, index, m.LightmapUV, receiverNormal );
			float flNDotL = dot( m.Normal, light.Direction );

			if ( light.Attenuation <= 0 || flNDotL <= 0 )
				continue;

			directDiffuse += light.Color * light.Attenuation * light.Visibility * NprDiffuse( flNDotL, m.Roughness );
		}

		// Indirect diffuse. The banded light amount picks where on a vertical gradient (down, horizontal
		// towards the camera, up) the ambient is sampled, and the unlit side picks up the transmissive
		// color from the sky.
		float3 view = CalculatePositionToCameraDirWs( m.WorldPosition );
		float3 bounce = lighting.vIndirectDiffuse.rgb;

		if ( g_flNprBounceDiffuse > 0 )
		{
			float halfLambert = 0.5 + 0.5 * dot( primaryLight, m.Normal );
			if ( halfLambert > 0.5 )
				halfLambert = lerp( 0.5, halfLambert, primaryVisibility );

			float stepped = saturate( halfLambert * m.AmbientOcclusion * 2.0 - 0.5 );
			float band = frac( stepped );
			float banded = ( stepped - band ) + NprGain( band, 1.0 / ( 1.0 - g_flNprDiffuseStepSharpness ) );
			float lit = ( banded + 0.5 ) * 0.5;

			float3 horizontal = normalize( float3( view.xy, 0.0 ) + float3( 0.0001, 0, 0 ) );
			float3 virtualNormal = lit < 0.5
				? lerp( float3( 0, 0, -1 ), horizontal, banded + 0.5 )
				: lerp( horizontal, float3( 0, 0, 1 ), banded - 0.5 );

			float3 ambient = AmbientDiffuse( combiner, normalize( virtualNormal ) );
			float3 sky = AmbientDiffuse( combiner, float3( 0, 0, 1 ) );
			float3 npr = ambient + sky * transmissive * g_flNprTransmissiveScale * ( 1.0 - lit );

			bounce = lerp( bounce, npr, g_flNprBounceDiffuse );
		}

		float3 diffuseAO = CalculateDiffuseAmbientOcclusion( combiner, lighting );
		float3 specularAO = CalculateSpecularAmbientOcclusion( combiner, lighting );
		bounce *= diffuseAO;

		// Rim light, tinted by the light the surface receives and kept on upward facing parts.
		float rimWrapScale = 1.0 + g_flNprRimLightWrap;
		float rim = pow( saturate( ( dot( m.Normal, -view ) + g_flNprRimLightWrap ) / ( rimWrapScale * rimWrapScale ) ), g_flNprRimLightFalloff );
		rim *= saturate( ( m.Normal.z - g_vNprRimLightUpRamp.x ) / max( g_vNprRimLightUpRamp.y - g_vNprRimLightUpRamp.x, 0.0001 ) );
		rim *= g_flNprRimLightStrength * m.AmbientOcclusion * rimMask;

		float3 diffuse = ( directDiffuse + bounce ) * combiner.vDiffuseColor.rgb;
		float3 specular = lighting.vSpecular.rgb + lighting.vIndirectSpecular.rgb * specularAO;
		float3 color = diffuse + ( directDiffuse + bounce ) * rim + specular + combiner.vEmissive.rgb;

		if ( DepthNormals::WantsDepthNormals() )
			return DepthNormals::Output( m.Normal, m.Roughness, m.Opacity );

		return DoAtmospherics( m.WorldPosition, m.ScreenPosition.xy, float4( color, m.Opacity ) );
	}

	// Heroes lit by the map's Deadlock lighting: the probes and cubemaps around them, and the sun and the light2 lights
	// through the toon curve, with the bounce light looked up along the banded virtual normal.
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
		// Direct light. The brightest light here is the primary one the bounce light is banded against.
		//
		float3 directDiffuse = 0;
		float3 directSpecular = 0;
		float3 unusedDiffuse = 0;
		float3 primaryLight = float3( 0, 0, 1 );
		float primaryVisibility = 1;
		float primaryStrength = 0;

		[branch]
		if ( any( g_vDeadlockSunColor > 0.0 ) )
		{
			float visibility = 1.0 - dot( bakedShadows, g_vDeadlockSunShadowMask );
			if ( visibility > 0.0 && g_DirectionalLightEnabled && g_DirectionalLightCascadeCount > 0 )
				visibility *= DirectionalLightShadow::GetVisibilityWithOffset( m.WorldPositionWithOffset, receiverNormal, m.ScreenPosition );

			float3 color = g_vDeadlockSunColor * visibility;
			float NdotL = dot( normal, g_vDeadlockSunDirection );

			if ( NdotL > 0.0 )
			{
				directDiffuse += color * NprDiffuse( NdotL, m.Roughness );
				DeadlockDirectLight( normal, toCamera, g_vDeadlockSunDirection, roughness, specularColor, directMultiScatter, color, unusedDiffuse, directSpecular );
			}

			primaryLight = g_vDeadlockSunDirection;
			primaryVisibility = visibility;
			primaryStrength = dot( g_vDeadlockSunColor, DeadlockLuminance );
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

			float strength = dot( color, DeadlockLuminance );
			if ( strength > primaryStrength )
			{
				primaryStrength = strength;
				primaryLight = toLight;
				primaryVisibility = 1;
			}

			float NdotL = dot( normal, toLight );
			if ( NdotL <= 0.0 )
				continue;

			directDiffuse += color * NprDiffuse( NdotL, m.Roughness );
			DeadlockDirectLight( normal, toCamera, toLight, max( roughness, light.Parameters.x ), specularColor, directMultiScatter, color, unusedDiffuse, directSpecular );
		}

		//
		// Bounce light. The banded light amount picks where on a vertical gradient (down, horizontal towards the
		// camera, up) the probes are looked up, and the unlit side picks up the transmissive colour from above.
		//
		float3 bounce = irradiance;

		[branch]
		if ( g_flNprBounceDiffuse > 0 )
		{
			float halfLambert = 0.5 + 0.5 * dot( primaryLight, normal );
			if ( halfLambert > 0.5 )
				halfLambert = lerp( 0.5, halfLambert, primaryVisibility );

			float stepped = saturate( halfLambert * m.AmbientOcclusion * 2.0 - 0.5 );
			float band = frac( stepped );
			float banded = ( stepped - band ) + NprGain( band, 1.0 / ( 1.0 - g_flNprDiffuseStepSharpness ) );
			float lit = ( banded + 0.5 ) * 0.5;

			float3 horizontal = normalize( float3( toCamera.xy, 0.0 ) + float3( 0.0001, 0, 0 ) );
			float3 virtualNormal = lit < 0.5
				? lerp( float3( 0, 0, -1 ), horizontal, banded + 0.5 )
				: lerp( horizontal, float3( 0, 0, 1 ), banded - 0.5 );

			float3 ambient = DeadlockProbeIrradiance( position, normalize( virtualNormal ) );
			float3 sky = DeadlockProbeIrradiance( position, float3( 0, 0, 1 ) );
			float3 npr = ambient + sky * transmissive * g_flNprTransmissiveScale * ( 1.0 - lit );

			bounce = lerp( bounce, npr, g_flNprBounceDiffuse );
		}

		bounce *= m.AmbientOcclusion;

		// Rim light, tinted by the light the surface receives and kept on upward facing parts.
		float rimWrapScale = 1.0 + g_flNprRimLightWrap;
		float rim = pow( saturate( ( dot( normal, -toCamera ) + g_flNprRimLightWrap ) / ( rimWrapScale * rimWrapScale ) ), g_flNprRimLightFalloff );
		rim *= saturate( ( normal.z - g_vNprRimLightUpRamp.x ) / max( g_vNprRimLightUpRamp.y - g_vNprRimLightUpRamp.x, 0.0001 ) );
		rim *= g_flNprRimLightStrength * m.AmbientOcclusion * rimMask;

		float normalizationCeiling = max( roughness * g_vDeadlockCubemapNormalization.x + g_vDeadlockCubemapNormalization.y, 1.0 );
		float normalization = specularLuminance > 0.0 ? min( dot( irradiance, DeadlockLuminance ) / specularLuminance, normalizationCeiling ) : normalizationCeiling;

		float3 diffuse = ( directDiffuse + bounce ) * m.Albedo * ( 1.0 - m.Metalness );
		float3 specular = directSpecular + ( environmentBrdf * cubeSpecular * normalization + multiScatter * irradiance ) * m.AmbientOcclusion;
		float3 color = diffuse + ( directDiffuse + bounce ) * rim + specular + m.Emission;

		return DoAtmospherics( m.WorldPosition, m.ScreenPosition.xy, float4( color, m.Opacity ) );
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
		return DoAtmospherics( m.WorldPosition, m.ScreenPosition.xy, float4( color, m.Opacity ) );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
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
			float2 illumUv = g_flSelfIllumSecondaryUv > 0.5 ? uv2 : uv;
			illumUv = TransformUv( ScrollUv( illumUv, g_vSelfIllumScrollSpeed, g_flSelfIllumScrollQuantize ), g_vSelfIllumTransform, g_flSelfIllumRotation );

			float illumMask = SampleMask( g_tSelfIllumMask, illumUv, g_vSelfIllumMaskChannel, g_flSelfIllumMaskFallback );
			float3 illumTint = lerp( g_vSelfIllumTint, g_vSelfIllumTint * albedo, g_flSelfIllumAlbedoFactor );

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
			opacity = color.a;
		#endif

		float2 aoUv = g_flAmbientOcclusionSecondaryUv > 0.5 ? uv2 : uv;

		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		m.Albedo = albedo;
		m.Emission = emission;
		m.Normal = normalWs;
		m.TangentNormal = normalTs;
		m.GeometricNormal = i.vNormalWs;
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.LightmapUV = i.vLightmapUV;
		m.TextureCoords = albedoUv;
		m.Roughness = saturate( normalRoughness.b );
		m.Metalness = saturate( SampleMask( g_tMetalness, albedoUv, g_vMetalnessChannel, g_flMetalnessFallback ) );
		m.AmbientOcclusion = saturate( SampleMask( g_tAmbientOcclusion, aoUv, g_vAmbientOcclusionChannel, 1.0 ) );
		m.Opacity = opacity;

		#if S_MODE_DEPTH
			return ShadingModelStandard::Shade( m );
		#elif S_UNLIT
			AdjustAlphaToCoverage( m );
			float4 unlit = float4( m.Albedo * ( S_ADDITIVE_BLEND ? g_flUnlitAdditiveScale : 1.0 ), m.Opacity );
			return DoAtmospherics( m.WorldPosition, m.ScreenPosition.xy, unlit, S_ADDITIVE_BLEND != 0 );
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
}
