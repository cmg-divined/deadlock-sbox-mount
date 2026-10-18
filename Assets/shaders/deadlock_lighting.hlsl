//=========================================================================================================================
// Deadlock's deferred lighting pass (deferred_lighting_cs with D_BAKED_LIGHTING_FROM_LIGHTMAP), as forward shading.
//
// A Deadlock material shader fills a DeadlockSurface with what the game's G-buffer would hold, and DeadlockShade lights
// it the way the pass does: second order harmonic lightmaps or light probe ambient cubes, box projected cubemaps
// normalized against the baked light, the sun, and the stationary and dynamic light2 lights, all with the game's BRDF.
// The map's lighting comes from the Deadlock Map Lighting component as scene render attributes.
//=========================================================================================================================

#ifndef DEADLOCK_LIGHTING_HLSL
#define DEADLOCK_LIGHTING_HLSL

#include "common/classes/Bindless.hlsl"

//
// Layouts, matching the structs in DeadlockMapLighting.cs
//

struct DeadlockEnvironmentVolume
{
	float4 WorldToLocal0;
	float4 WorldToLocal1;
	float4 WorldToLocal2;
	float4 BoxMin;			// w: cube array slice, -1 for none
	float4 BoxMax;			// w: 1 for a sphere
	float4 Fade;			// box: inverse edge fade widths. sphere: scale, bias, radius
	float4 Center;			// sphere centre; w: light probe volume, -1 for none
	float4 Tint;
	float4 LuminanceSH;
};

struct DeadlockProbeVolume
{
	float4 WorldToLocal0;
	float4 WorldToLocal1;
	float4 WorldToLocal2;
	float4 BorderMin;		// w: ambient cube atlas
	float4 BorderMax;		// w: direct light shadows atlas
	float4 AtlasScale;
	float4 AtlasOffset;
};

struct DeadlockBarnLight
{
	float4 Frustum0;
	float4 Frustum1;
	float4 Frustum2;
	float4 Frustum3;
	float4 Illumination0;
	float4 Illumination1;
	float4 Illumination2;
	float4 Position;		// w: squared distance of full brightness, 0 for parallel rays along -xyz
	float4 DistanceFade;	// distance fade scale and bias, near and far skirts
	float4 Color;			// w: cookie texture, negative for a spherical cookie
	float4 Orientation;
	float4 AngleFade;		// cone fade scale and bias, luminaire half length
	float4 ShadowRect;
	float4 Shape;
	float4 BakedShadowMask;
	float4 Parameters;		// min roughness, shadow strength, shadow filter radius, flags
};

struct DeadlockParticleLight
{
	float4 Position;		// w: reach
	float4 Color;			// w: specular amount
	float4 Falloff;			// saturate( 1 / ( x d² + y d + z ) - w )
	float4 Fade;			// distance fade offset and rate (smoothstep), cone fade bias and scale (saturate)
	float4 Direction;		// cone axis; w: 1 to measure distances to the luminaire rather than its centre
	float4 Segment;			// half the luminaire
};

//
// Scene attributes
//

int g_nDeadlockLighting < Attribute( "DeadlockLighting" ); Default( 0 ); >;

// The map can be placed anywhere; its lighting is in its own space.
float4 g_vDeadlockWorldToMap0 < Attribute( "DeadlockWorldToMap0" ); Default4( 1, 0, 0, 0 ); >;
float4 g_vDeadlockWorldToMap1 < Attribute( "DeadlockWorldToMap1" ); Default4( 0, 1, 0, 0 ); >;
float4 g_vDeadlockWorldToMap2 < Attribute( "DeadlockWorldToMap2" ); Default4( 0, 0, 1, 0 ); >;

int g_nDeadlockLightmapIrradiance < Attribute( "DeadlockLightmapIrradiance" ); Default( 0 ); >;
int g_nDeadlockLightmapIrradianceR < Attribute( "DeadlockLightmapIrradianceR" ); Default( 0 ); >;
int g_nDeadlockLightmapIrradianceG < Attribute( "DeadlockLightmapIrradianceG" ); Default( 0 ); >;
int g_nDeadlockLightmapIrradianceB < Attribute( "DeadlockLightmapIrradianceB" ); Default( 0 ); >;
int g_nDeadlockLightmapShadows < Attribute( "DeadlockLightmapShadows" ); Default( 0 ); >;
float2 g_vDeadlockLightmapUvScale < Attribute( "DeadlockLightmapUvScale" ); Default2( 1, 1 ); >;

int g_nDeadlockEnvironmentMaps < Attribute( "DeadlockEnvironmentMaps" ); Default( 0 ); >;
float g_flDeadlockEnvironmentMapMips < Attribute( "DeadlockEnvironmentMapMips" ); Default( 0 ); >;
int g_nDeadlockEnvironmentVolumeCount < Attribute( "DeadlockEnvironmentVolumeCount" ); Default( 0 ); >;
StructuredBuffer<DeadlockEnvironmentVolume> g_DeadlockEnvironmentVolumes < Attribute( "DeadlockEnvironmentVolumes" ); >;
int g_nDeadlockProbeVolumeCount < Attribute( "DeadlockProbeVolumeCount" ); Default( 0 ); >;
StructuredBuffer<DeadlockProbeVolume> g_DeadlockProbeVolumes < Attribute( "DeadlockProbeVolumes" ); >;

float4 g_vDeadlockAmbientR < Attribute( "DeadlockAmbientR" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vDeadlockAmbientG < Attribute( "DeadlockAmbientG" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vDeadlockAmbientB < Attribute( "DeadlockAmbientB" ); Default4( 0, 0, 0, 0 ); >;

float3 g_vDeadlockSunDirection < Attribute( "DeadlockSunDirection" ); Default3( 0, 0, 0 ); >;
float3 g_vDeadlockSunColor < Attribute( "DeadlockSunColor" ); Default3( 0, 0, 0 ); >;
float4 g_vDeadlockSunShadowMask < Attribute( "DeadlockSunShadowMask" ); Default4( 0, 0, 0, 0 ); >;

int g_nDeadlockBarnLightCount < Attribute( "DeadlockBarnLightCount" ); Default( 0 ); >;
StructuredBuffer<DeadlockBarnLight> g_DeadlockBarnLights < Attribute( "DeadlockBarnLights" ); >;

// Lights particle effects place (DeadlockParticleLights), in map space.
int g_nDeadlockParticleLightCount < Attribute( "DeadlockParticleLightCount" ); Default( 0 ); >;
StructuredBuffer<DeadlockParticleLight> g_DeadlockParticleLights < Attribute( "DeadlockParticleLights" ); >;
int g_nDeadlockShadowAtlas < Attribute( "DeadlockShadowAtlas" ); Default( 0 ); >;

int g_nDeadlockBrdfLookup < Attribute( "DeadlockBrdfLookup" ); Default( 0 ); >;

// A uniform grid over the map (DeadlockMapLighting's) listing per cell the volumes (list 0) and lights (list 1) that
// can reach into it, so a point visits only those instead of all of them. Each list keeps the global order, so the
// result is the same. Per cell: first and count of each list; the lists follow.
StructuredBuffer<uint> g_DeadlockGrid < Attribute( "DeadlockGrid" ); >;
float4 g_vDeadlockGridOrigin < Attribute( "DeadlockGridOrigin" ); Default4( 0, 0, 0, 0 ); >;	// w: 1 / cell size
float4 g_vDeadlockGridSize < Attribute( "DeadlockGridSize" ); Default4( 0, 0, 0, 0 ); >;		// w: 1 with a grid
float2 g_vDeadlockCubemapNormalization < Attribute( "DeadlockCubemapNormalization" ); Default2( 2, 1 ); >;
float2 g_vDeadlockLightmapSpecular < Attribute( "DeadlockLightmapSpecular" ); Default2( -1, 2 ); >;

//
// What a Deadlock G-buffer holds for a pixel
//

struct DeadlockSurface
{
	float3 Position;				// world space
	float3 PositionWithOffset;		// camera relative, for s&box's sun shadows
	float4 ScreenPosition;
	float3 Normal;
	float3 ShadowReceiverNormal;	// from screen derivatives, taken in uniform control flow
	float3 Albedo;
	float Roughness;
	float Metalness;
	float3 Emission;				// self illumination, the albedo times its strength
	float2 LightmapUV;				// before the map's scale; negative where the lightmaps don't reach
	float Reflectance;				// a dielectric's specular colour, 0.04 except for glass
	float Transmission;				// glass: how much of what's behind shows through, tinted by the albedo
	float3 Background;				// glass: what's behind
};

DeadlockSurface DeadlockSurfaceDefaults()
{
	DeadlockSurface s = (DeadlockSurface)0;
	s.Reflectance = 0.04;
	return s;
}

// The luminance weights the pass uses, which differ slightly where it reads the lightmap harmonics.
#define DeadlockLuminance float3( 0.2125, 0.7154, 0.0721 )
#define DeadlockLuminanceSH float3( 0.2126, 0.7152, 0.0722 )

bool DeadlockLightingEnabled()
{
	return g_nDeadlockLighting != 0;
}

struct DeadlockGridRange
{
	int Cell;
	uint First;
	uint Count;
};

// The volumes (list 0) or lights (list 1) of the grid cell around a map space point; everything without a grid,
// nothing outside it (no volume or light reaches there).
DeadlockGridRange DeadlockGridLookup( float3 position, uint list, uint total )
{
	DeadlockGridRange range;
	range.Cell = -1;
	range.First = 0;
	range.Count = total;

	if ( g_vDeadlockGridSize.w == 0.0 )
		return range;

	float3 cell = floor( ( position - g_vDeadlockGridOrigin.xyz ) * g_vDeadlockGridOrigin.w );
	if ( any( cell < 0.0 ) || any( cell >= g_vDeadlockGridSize.xyz ) )
	{
		range.Count = 0;
		return range;
	}

	range.Cell = (int)( ( cell.z * g_vDeadlockGridSize.y + cell.y ) * g_vDeadlockGridSize.x + cell.x );
	uint header = (uint)range.Cell * 4 + list * 2;
	range.First = g_DeadlockGrid[header];
	range.Count = g_DeadlockGrid[header + 1];
	return range;
}

// The k-th volume or light of a range.
uint DeadlockGridItem( DeadlockGridRange range, uint k )
{
	return range.Cell >= 0 ? g_DeadlockGrid[range.First + k] : k;
}

float3 DeadlockToMap( float3 position )
{
	float4 p = float4( position, 1.0 );
	return float3( dot( g_vDeadlockWorldToMap0, p ), dot( g_vDeadlockWorldToMap1, p ), dot( g_vDeadlockWorldToMap2, p ) );
}

float3 DeadlockDirectionToMap( float3 direction )
{
	return normalize( float3( dot( g_vDeadlockWorldToMap0.xyz, direction ), dot( g_vDeadlockWorldToMap1.xyz, direction ), dot( g_vDeadlockWorldToMap2.xyz, direction ) ) );
}

//
// Second order harmonics: a constant band and a linear band per colour channel. The linear bands are stored y, z, x
// against the normal, as (dc, -y, z, -x).
//

float4 DeadlockSHBasis( float3 direction )
{
	return float4( 0.886227548, -1.0233277 * direction.y, 1.0233277 * direction.z, -1.0233277 * direction.x );
}

// Irradiance along a direction, with the linear band's dominant direction sharpened into a third band.
float3 DeadlockEvaluateSH( float4 shR, float4 shG, float4 shB, float3 direction )
{
	float3 dc = float3( shR.x, shG.x, shB.x );
	float4 basis = DeadlockSHBasis( direction );

	float3 dominant = float3(
		-dot( float3( shR.w, shG.w, shB.w ), DeadlockLuminanceSH ),
		-dot( float3( shR.y, shG.y, shB.y ), DeadlockLuminanceSH ),
		dot( float3( shR.z, shG.z, shB.z ), DeadlockLuminanceSH ) );
	dominant /= max( 1e-5, length( dominant ) );

	float3 ratio = abs( float3(
		dot( float3( -shR.w, -shR.y, shR.z ), dominant ),
		dot( float3( -shG.w, -shG.y, shG.z ), dominant ),
		dot( float3( -shB.w, -shB.y, shB.z ), dominant ) ) / max( 1e-5, dc ) );

	float cosTheta = dot( dominant, direction );
	float3 zonal = dc * ratio * ( 0.08 + ratio * 0.6 ) * 0.25 * ( 0.3153915703 * ( 3.0 * cosTheta * cosTheta - 1.0 ) );

	return max( 0.0, float3( dot( shR, basis ), dot( shG, basis ), dot( shB, basis ) ) * 0.318309873 + zonal );
}

float3 DeadlockAmbient( float3 direction )
{
	float4 d = float4( direction, 1.0 );
	return float3( dot( g_vDeadlockAmbientR, d ), dot( g_vDeadlockAmbientG, d ), dot( g_vDeadlockAmbientB, d ) );
}

float3 DeadlockQuaternionAxisX( float4 q )
{
	return float3( 1.0 - 2.0 * q.y * q.y - 2.0 * q.z * q.z, 2.0 * q.x * q.y + 2.0 * q.w * q.z, 2.0 * q.x * q.z - 2.0 * q.w * q.y );
}

float3 DeadlockQuaternionAxisY( float4 q )
{
	return float3( 2.0 * q.x * q.y - 2.0 * q.w * q.z, 1.0 - 2.0 * q.x * q.x - 2.0 * q.z * q.z, 2.0 * q.y * q.z + 2.0 * q.w * q.x );
}

// GGX with height correlated Smith visibility and Schlick fresnel, and the direct light's multiple scattering.
void DeadlockDirectLight( float3 normal, float3 toCamera, float3 toLight, float roughness, float3 specularColor, float3 multiScatter, float3 color, inout float3 diffuse, inout float3 specular )
{
	float NdotL = saturate( dot( normal, toLight ) );
	float3 halfVector = normalize( toLight + toCamera );

	float alpha = roughness * roughness;
	float alpha2 = alpha * alpha;
	float NdotH = dot( halfVector, normal );
	float d = ( NdotH * alpha2 - NdotH ) * NdotH + 1.0;

	float NdotV = saturate( dot( normal, toCamera ) );
	float k = 1.0 - alpha;
	float visibility = 0.5 / max( 1e-5, NdotL * ( NdotV * k + alpha ) + NdotV * ( NdotL * k + alpha ) );

	float3 fresnel = specularColor + ( 1.0 - specularColor ) * pow( max( 0.0, 1.0 - saturate( dot( toLight, halfVector ) ) ), 5.0 );

	specular += fresnel * ( alpha2 / max( 1e-8, d * d ) * visibility * NdotL ) * multiScatter * color;
	diffuse += color * NdotL;
}

// A barn light's shape: soft edged, square to round by a superellipse exponent.
float DeadlockBarnShape( float2 ndc, float4 shape )
{
	float2 a = abs( ndc );

	if ( shape.z == 0.0 )
		return smoothstep( 1.0, shape.x, a.x ) * smoothstep( 1.0, shape.y, a.y );

	float power = 2.0 / shape.z;
	float inverse = -0.5 * shape.z;
	float inner = shape.x * shape.y * pow( max( pow( shape.y * a.x, power ) + pow( shape.x * a.y, power ), 1.17549435e-38 ), inverse );
	float outer = pow( max( pow( a.x, power ) + pow( a.y, power ), 1.17549435e-38 ), inverse );

	return inner < outer ? smoothstep( outer, inner, 1.0 ) : ( outer > 1.0 ? 1.0 : 0.0 );
}

// One light2 face at a point: the direction to it and the colour it lights with, false where it doesn't reach.
bool DeadlockBarnLightAt( DeadlockBarnLight light, float3 position, float4 bakedShadows, out float3 toLight, out float3 color )
{
	toLight = 0;
	color = 0;

	float bakedVisibility = 1.0 - dot( bakedShadows, light.BakedShadowMask );
	if ( bakedVisibility == 0.0 )
		return false;

	float4 p = float4( position, 1.0 );
	float4 clip = float4( dot( light.Frustum0, p ), dot( light.Frustum1, p ), dot( light.Frustum2, p ), dot( light.Frustum3, p ) );
	float3 ndc = clip.xyz / clip.w;

	if ( !all( ndc > float3( -1.0, -1.0, 0.0 ) ) || !all( ndc < 1.0 ) )
		return false;

	float3 box = float3( dot( light.Illumination0, p ), dot( light.Illumination1, p ), dot( light.Illumination2, p ) );
	if ( any( abs( box ) > 1.0 ) )
		return false;

	float fade = light.DistanceFade.z > 0.0 ? smoothstep( 0.0, 1.0, ndc.z * light.DistanceFade.z ) : 1.0;
	if ( light.DistanceFade.w > 0.0 )
		fade *= smoothstep( 0.0, 1.0, ( 1.0 - ndc.z ) * light.DistanceFade.w );

	float attenuation;

	if ( light.Position.w != 0.0 )
	{
		float3 delta = light.Position.xyz - position;
		float distanceSquared = dot( delta, delta );
		float distance = sqrt( distanceSquared );

		// The cone fades by the direction to the nearest point of the luminaire.
		float3 segment = DeadlockQuaternionAxisY( light.Orientation ) * light.AngleFade.z;
		float3 segmentStart = delta - segment;
		float3 segmentDirection = ( delta + segment ) - segmentStart;
		float along = dot( -segmentStart, segmentDirection );
		float3 nearest = along <= 0.0 ? segmentStart : segmentStart + segmentDirection * min( 1.0, along / dot( segmentDirection, segmentDirection ) );

		toLight = delta / distance;
		attenuation = fade * ( light.Position.w / max( distanceSquared, light.Position.w ) )
			* smoothstep( 0.0, 1.0, light.DistanceFade.x + light.DistanceFade.y * distance )
			* saturate( light.AngleFade.x + light.AngleFade.y * dot( DeadlockQuaternionAxisX( light.Orientation ), normalize( nearest ) ) );
	}
	else
	{
		toLight = light.Position.xyz;
		attenuation = fade;
	}

	color = light.Color.rgb * bakedVisibility * bakedVisibility * attenuation;

	if ( light.Color.w == 0.0 )
	{
		color *= DeadlockBarnShape( ndc.xy, light.Shape );
	}
	else if ( light.Color.w < 0.0 )
	{
		// Spherical cookie, by the direction from the light in its own space.
		float4 q = float4( -light.Orientation.xyz, light.Orientation.w );
		float3 v = -toLight;
		float3 t = 2.0 * cross( q.xyz, v );
		float3 local = v + q.w * t + cross( q.xyz, t );
		float2 uv = float2( atan2( local.y, -local.x ) * 0.159154937, acos( clamp( local.z, -1.0, 1.0 ) ) * 0.318309873 );
		color *= Bindless::GetTexture2DSrgb( (int)-light.Color.w ).SampleLevel( g_sTrilinearWrap, uv * light.Shape.zw + light.Shape.xy, 0 ).rgb;
	}
	else
	{
		float2 uv = ndc.xy * float2( 0.5, -0.5 ) + 0.5;
		color *= Bindless::GetTexture2DSrgb( (int)light.Color.w ).SampleLevel( g_sTrilinearClamp, uv * light.Shape.zw + light.Shape.xy, 0 ).rgb;
	}

	if ( all( color == 0.0 ) )
		return false;

	// Shadow maps the map compiler rendered, reversed depth: a texel at or above the reference occludes.
	if ( light.ShadowRect.z > 0.0 && g_nDeadlockShadowAtlas != 0 )
	{
		float3 shadowCoords = float3( ndc.xy, 1.0 - ndc.z );
		if ( ( (uint)light.Parameters.w & 4u ) != 0u )
			shadowCoords.xy = shadowCoords.yx * float2( 1.0, -1.0 );

		float reference = saturate( shadowCoords.z + 1e-6 );
		float2 uv = clamp( shadowCoords.xy, -1.0, 1.0 ) * light.ShadowRect.zw + light.ShadowRect.xy;
		float4 depths = Bindless::GetTexture2D( g_nDeadlockShadowAtlas ).GatherRed( g_sBilinearClamp, uv );
		float occluded = dot( step( reference, depths ), float4( 0.25, 0.25, 0.25, 0.25 ) );

		color *= lerp( 1.0, 1.0 - occluded, light.Parameters.y );
	}

	return any( color != 0.0 );
}

// A particle light at a point: the engine's falloff, 1 / ( c + l d + q d² ) less what's left at its reach (light2
// lights' is inverse square), with the distance at least a unit; a light2 skirt; and the cone, by the direction to
// the nearest point of the luminaire, as light2 lights fade theirs.
bool DeadlockParticleLightAt( DeadlockParticleLight light, float3 position, out float3 toLight, out float3 color )
{
	toLight = 0;
	color = 0;

	float3 delta = light.Position.xyz - position;
	float3 segmentStart = delta - light.Segment.xyz;
	float3 segmentDirection = 2.0 * light.Segment.xyz;
	float along = dot( -segmentStart, segmentDirection );
	float3 nearest = along <= 0.0 ? segmentStart : segmentStart + segmentDirection * min( 1.0, along / dot( segmentDirection, segmentDirection ) );

	float3 measured = light.Direction.w > 0.0 ? nearest : delta;
	float distanceSquared = dot( measured, measured );
	if ( distanceSquared >= light.Position.w * light.Position.w )
		return false;

	distanceSquared = max( distanceSquared, 1.0 );
	float distance = sqrt( distanceSquared );
	toLight = measured * rsqrt( max( dot( measured, measured ), 1e-8 ) );

	float falloff = saturate( 1.0 / max( light.Falloff.x * distanceSquared + light.Falloff.y * distance + light.Falloff.z, 1e-8 ) - light.Falloff.w );
	falloff *= smoothstep( 0.0, 1.0, light.Fade.x + light.Fade.y * distance );
	falloff *= saturate( light.Fade.z + light.Fade.w * dot( light.Direction.xyz, nearest * rsqrt( max( dot( nearest, nearest ), 1e-8 ) ) ) );

	color = light.Color.rgb * falloff;
	return any( color > 0.0 );
}

void DeadlockParticleLights( DeadlockSurface s, float3 toCamera, float roughness, float3 specularColor, float3 multiScatter, inout float3 diffuse, inout float3 specular )
{
	[loop]
	for ( uint k = 0; k < (uint)g_nDeadlockParticleLightCount; k++ )
	{
		DeadlockParticleLight light = g_DeadlockParticleLights[k];

		float3 toLight;
		float3 color;
		if ( !DeadlockParticleLightAt( light, s.Position, toLight, color ) )
			continue;

		float3 lightSpecular = 0;
		DeadlockDirectLight( s.Normal, toCamera, toLight, roughness, specularColor, multiScatter, color, diffuse, lightSpecular );
		specular += lightSpecular * light.Color.w;
	}
}

void DeadlockBarnLights( DeadlockSurface s, float4 bakedShadows, float3 toCamera, float roughness, float3 specularColor, float3 multiScatter, inout float3 diffuse, inout float3 specular )
{
	DeadlockGridRange range = DeadlockGridLookup( s.Position, 1, (uint)g_nDeadlockBarnLightCount );

	[loop]
	for ( uint k = 0; k < range.Count; k++ )
	{
		DeadlockBarnLight light = g_DeadlockBarnLights[DeadlockGridItem( range, k )];

		float3 toLight;
		float3 color;
		if ( DeadlockBarnLightAt( light, s.Position, bakedShadows, toLight, color ) )
			DeadlockDirectLight( s.Normal, toCamera, toLight, max( roughness, light.Parameters.x ), specularColor, multiScatter, color, diffuse, specular );
	}

	DeadlockParticleLights( s, toCamera, max( roughness, 0.04 ), specularColor, multiScatter, diffuse, specular );
}

// The sun's visibility at a point: its channel of the baked shadows, and s&box's cascades for whatever moves.
float DeadlockSunVisibility( float3 normal, float4 bakedShadows, float3 positionWithOffset, float3 receiverNormal, float4 screenPosition )
{
	float visibility = dot( normal, g_vDeadlockSunDirection ) > 0.0 ? 1.0 - dot( bakedShadows, g_vDeadlockSunShadowMask ) : 0.0;

	if ( visibility > 0.0 && g_DirectionalLightEnabled && g_DirectionalLightCascadeCount > 0 )
		visibility *= DirectionalLightShadow::GetVisibilityWithOffset( positionWithOffset, receiverNormal, screenPosition );

	return visibility;
}

// The reflection direction the pass samples cubemaps along, bent towards the normal as roughness grows.
float3 DeadlockReflection( float3 normal, float3 toCamera, float roughness )
{
	float roughness2 = roughness * roughness;
	float a = saturate( 1.0 - roughness2 );
	return normalize( lerp( normal, reflect( -toCamera, normal ), a * ( sqrt( a ) + roughness2 ) ) );
}

// Split sum reflection energy, from the game's BRDF lookup, with its multiple scattering term.
void DeadlockEnvironmentBrdf( float roughness, float NdotV, float3 specularColor, out float3 environmentBrdf, out float3 multiScatter )
{
	float4 lookup = g_nDeadlockBrdfLookup != 0
		? Bindless::GetTexture2DArray( g_nDeadlockBrdfLookup ).SampleLevel( g_sBilinearClamp, float3( float2( roughness, sqrt( 1.0 - NdotV ) ) * 0.984375 + 0.0078125, 1.0 ), 0 )
		: float4( 1.0, 1.0, 0, 0 );

	environmentBrdf = lerp( lookup.xxx, lookup.yyy, specularColor );
	float missing = 1.0 - lookup.y;
	float3 average = specularColor + ( 1.0 - specularColor ) * 0.0476190485;
	multiScatter = environmentBrdf * average / ( 1.0 - average * missing ) * missing;
}

// Light probe ambient cube irradiance in one volume, along a normal.
float3 DeadlockProbeAmbientCube( DeadlockProbeVolume probe, float3 local, float3 normal )
{
	// The atlas stacks the six ambient cube faces in depth.
	float3 coords = local;
	coords.z *= 1.0 / 6.0;
	coords = clamp( coords, probe.BorderMin.xyz, probe.BorderMax.xyz );
	coords.z *= 6.0;
	coords = coords * probe.AtlasScale.xyz + probe.AtlasOffset.xyz;
	coords.z *= 1.0 / 6.0;

	float3 faces = lerp( float3( 0.0, 1.0, 2.0 ) / 6.0, float3( 3.0, 4.0, 5.0 ) / 6.0, step( normal, 0.0 ) );
	float3 weights = normal * normal;

	Texture3D cubes = Bindless::GetTexture3D( (int)probe.BorderMin.w );
	return cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, faces.x ), 0 ).rgb * weights.x
		+ cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, faces.y ), 0 ).rgb * weights.y
		+ cubes.SampleLevel( g_sBilinearClamp, coords + float3( 0, 0, faces.z ), 0 ).rgb * weights.z;
}

// The cubemap and light probe volumes, front to back until they cover the point: probe irradiance and baked shadows
// (when probe lit), reflections and the luminance the reflections are normalized against (when sampling cubes).
void DeadlockVolumes( float3 position, float3 normal, float3 reflection, float roughness, bool probeLit, bool sampleCubes,
	out float3 probeIrradiance, out float4 probeShadows, out float3 cubeSpecular, out float specularLuminance )
{
	probeIrradiance = 0;
	probeShadows = 0;
	cubeSpecular = 0;
	specularLuminance = 0;

	float lod = g_flDeadlockEnvironmentMapMips * sqrt( roughness );
	float4 p = float4( position, 1.0 );

	float4 luminanceSH = 0;
	float probeWeight = 0;
	float cubeWeight = 0;

	DeadlockGridRange range = DeadlockGridLookup( position, 0, (uint)g_nDeadlockEnvironmentVolumeCount );

	[loop]
	for ( uint k = 0; k < range.Count; k++ )
	{
		DeadlockEnvironmentVolume volume = g_DeadlockEnvironmentVolumes[DeadlockGridItem( range, k )];

		float edge;
		float3 projected;
		float3 direction;

		if ( volume.BoxMax.w != 0.0 )
		{
			float3 delta = position - volume.Center.xyz;
			edge = 1.0 - saturate( length( delta ) * volume.Fade.x + volume.Fade.y );
			if ( edge == 0.0 )
				continue;

			float b = dot( delta, reflection );
			float discriminant = b * b - ( dot( delta, delta ) - volume.Fade.z * volume.Fade.z );
			projected = discriminant > 0.0 ? normalize( delta + reflection * ( sqrt( discriminant ) - b ) ) : reflection;
			direction = reflection;
		}
		else
		{
			float3 local = float3( dot( volume.WorldToLocal0, p ), dot( volume.WorldToLocal1, p ), dot( volume.WorldToLocal2, p ) );
			float3 fadeMin = saturate( ( local - volume.BoxMin.xyz ) * volume.Fade.xyz );
			float3 fadeMax = saturate( ( volume.BoxMax.xyz - local ) * volume.Fade.xyz );
			edge = min( min( fadeMin.x, min( fadeMin.y, fadeMin.z ) ), min( fadeMax.x, min( fadeMax.y, fadeMax.z ) ) );
			if ( edge == 0.0 )
				continue;

			// Box projected: where the reflection leaves the box.
			direction = float3( dot( volume.WorldToLocal0.xyz, reflection ), dot( volume.WorldToLocal1.xyz, reflection ), dot( volume.WorldToLocal2.xyz, reflection ) );
			float3 exits = max( ( volume.BoxMax.xyz - local ) / direction, ( volume.BoxMin.xyz - local ) / direction );
			projected = normalize( local + direction * abs( min( exits.x, min( exits.y, exits.z ) ) ) );
		}

		float weight = edge * edge * ( 3.0 - 2.0 * edge );
		bool hasCube = volume.BoxMin.w >= 0.0 && g_nDeadlockEnvironmentMaps != 0;
		float cubeContribution = hasCube ? weight * ( 1.0 - cubeWeight ) : 0.0;

		int probeIndex = (int)volume.Center.w;
		if ( probeLit && probeIndex >= 0 )
		{
			DeadlockProbeVolume probe = g_DeadlockProbeVolumes[probeIndex];
			float probeContribution = weight * ( 1.0 - probeWeight );
			float3 local = float3( dot( probe.WorldToLocal0, p ), dot( probe.WorldToLocal1, p ), dot( probe.WorldToLocal2, p ) );

			if ( (int)probe.BorderMax.w != 0 )
				probeShadows += Bindless::GetTexture3D( (int)probe.BorderMax.w ).SampleLevel( g_sBilinearClamp, saturate( local ) * probe.AtlasScale.xyz + probe.AtlasOffset.xyz, 0 ) * probeContribution;

			probeIrradiance += DeadlockProbeAmbientCube( probe, local, normal ) * probeContribution;
			probeWeight += probeContribution;
		}

		if ( hasCube && sampleCubes )
		{
			float3 cube = Bindless::GetTextureCubeArray( g_nDeadlockEnvironmentMaps ).SampleLevel( g_sTrilinearClamp, float4( lerp( projected, direction, roughness ), volume.BoxMin.w ), lod ).rgb;
			cubeSpecular += volume.Tint.rgb * cubeContribution * cube;
			luminanceSH += volume.LuminanceSH * cubeContribution;
		}

		cubeWeight += cubeContribution;

		if ( ( !sampleCubes || cubeWeight > 0.99 ) && ( !probeLit || probeWeight > 0.99 ) )
			break;
	}

	// The sky light, for what the volumes leave uncovered.
	float remaining = 1.0 - cubeWeight;
	float4 ambientLuminance = g_vDeadlockAmbientR * DeadlockLuminance.r + g_vDeadlockAmbientG * DeadlockLuminance.g + g_vDeadlockAmbientB * DeadlockLuminance.b;

	probeIrradiance += DeadlockAmbient( normal ) * ( 1.0 - probeWeight );
	specularLuminance = dot( float4( normal, 1.0 ), luminanceSH + ambientLuminance * remaining );
	cubeSpecular += DeadlockAmbient( reflection ) * remaining;
}

// Probe lit irradiance along any direction, for shading that looks the light up away from the surface normal.
float3 DeadlockProbeIrradiance( float3 position, float3 normal )
{
	float3 irradiance;
	float4 shadows;
	float3 specular;
	float luminance;
	DeadlockVolumes( position, normal, normal, 1.0, true, false, irradiance, shadows, specular, luminance );
	return irradiance;
}

float3 DeadlockShade( DeadlockSurface s )
{
	// Everything from here on is in the map's space; only s&box's sun shadows stay in the world's.
	float3 toCamera = DeadlockDirectionToMap( CalculatePositionToCameraDirWs( s.Position ) );
	s.Position = DeadlockToMap( s.Position );
	s.Normal = DeadlockDirectionToMap( s.Normal );

	float3 normal = s.Normal;
	float NdotV = saturate( dot( toCamera, normal ) );

	float roughness = max( s.Roughness, 0.05 );
	float roughness2 = roughness * roughness;
	float metalness = s.Metalness;
	float3 albedo = s.Albedo;

	float3 specularColor = lerp( s.Reflectance, albedo, metalness ) * saturate( max( albedo.r, max( albedo.g, albedo.b ) ) * 25.0 );
	float3 directMultiScatter = 1.0 + specularColor * ( 2.0 * roughness2 * roughness2 * NdotV );

	//
	// Baked light: the lightmaps, or the probes for whatever they don't cover
	//
	bool lightmapped = s.LightmapUV.x >= 0.0 && g_nDeadlockLightmapIrradiance != 0;

	float4 lightmapShadows = 0;
	float4 shR = 0, shG = 0, shB = 0;
	float lightmapSpecular = 0;
	float3 lightmapIrradiance = 0;

	[branch]
	if ( lightmapped )
	{
		float2 uv = s.LightmapUV * g_vDeadlockLightmapUvScale;

		float3 dc = Bindless::GetTexture2D( g_nDeadlockLightmapIrradiance ).SampleLevel( g_sTrilinearClamp, uv, 0 ).rgb;
		float3 r = ( Bindless::GetTexture2D( g_nDeadlockLightmapIrradianceR ).SampleLevel( g_sTrilinearClamp, uv, 0 ).rgb * 2.0 - 1.0 ) * dc.r * 1.73205078;
		float3 g = ( Bindless::GetTexture2D( g_nDeadlockLightmapIrradianceG ).SampleLevel( g_sTrilinearClamp, uv, 0 ).rgb * 2.0 - 1.0 ) * dc.g * 1.73205078;
		float3 b = ( Bindless::GetTexture2D( g_nDeadlockLightmapIrradianceB ).SampleLevel( g_sTrilinearClamp, uv, 0 ).rgb * 2.0 - 1.0 ) * dc.b * 1.73205078;

		shR = float4( dc.r, r );
		shG = float4( dc.g, g );
		shB = float4( dc.b, b );

		lightmapIrradiance = DeadlockEvaluateSH( shR, shG, shB, normal );

		if ( g_nDeadlockLightmapShadows != 0 )
			lightmapShadows = Bindless::GetTexture2D( g_nDeadlockLightmapShadows ).SampleLevel( g_sTrilinearClamp, uv, 0 );

		lightmapSpecular = lerp( saturate( g_vDeadlockLightmapSpecular.x + g_vDeadlockLightmapSpecular.y * roughness ), 0.0, metalness );
	}

	bool probeLit = !lightmapped;

	//
	// Cubemap and probe volumes, front to back until they cover the point
	//
	float4 probeShadows = 0;
	float3 probeIrradiance = 0;
	float3 cubeSpecular = 0;
	float specularLuminance = 0;

	float3 reflection = DeadlockReflection( normal, toCamera, roughness );

	[branch]
	if ( probeLit || lightmapSpecular < 1.0 )
		DeadlockVolumes( s.Position, normal, reflection, roughness, probeLit, true, probeIrradiance, probeShadows, cubeSpecular, specularLuminance );

	// Rough specular on lightmapped surfaces comes from the lightmap, along the reflection.
	[branch]
	if ( lightmapSpecular > 0.0 )
	{
		float a = saturate( 1.0 - roughness2 );
		float3 direction = lerp( normal, reflect( -toCamera, normal ), a * ( sqrt( a ) + roughness2 ) );
		float3 specular = DeadlockEvaluateSH( shR, shG, shB, direction );

		cubeSpecular = lerp( cubeSpecular, specular, lightmapSpecular );
		specularLuminance = lerp( specularLuminance, dot( specular, DeadlockLuminance ), lightmapSpecular );
	}

	float3 indirectDiffuse = probeLit ? lightmapIrradiance + probeIrradiance : lightmapIrradiance;
	float4 bakedShadows = probeLit ? probeShadows : lightmapShadows;

	//
	// Split sum reflection energy
	//
	float3 environmentBrdf;
	float3 multiScatter;
	DeadlockEnvironmentBrdf( roughness, NdotV, specularColor, environmentBrdf, multiScatter );

	//
	// The sun: baked shadows from the lightmap's channel, with s&box's cascades for whatever moves
	//
	float3 directDiffuse = 0;
	float3 directSpecular = 0;

	[branch]
	if ( any( g_vDeadlockSunColor > 0.0 ) )
	{
		float sunVisibility = DeadlockSunVisibility( normal, bakedShadows, s.PositionWithOffset, s.ShadowReceiverNormal, s.ScreenPosition );
		if ( sunVisibility > 0.0 )
			DeadlockDirectLight( normal, toCamera, g_vDeadlockSunDirection, roughness, specularColor, directMultiScatter, g_vDeadlockSunColor * sunVisibility, directDiffuse, directSpecular );
	}

	DeadlockBarnLights( s, bakedShadows, toCamera, roughness, specularColor, directMultiScatter, directDiffuse, directSpecular );

	//
	// Composite
	//
	float3 diffuseEnergy = 1.0 - ( environmentBrdf + multiScatter );
	float3 multiBounce = ( albedo * 2.0404 - 0.3324 ) - ( albedo * 4.7951 - 0.6417 ) + ( albedo * 2.7552 + 0.6903 );

	float normalizationCeiling = max( roughness * g_vDeadlockCubemapNormalization.x + g_vDeadlockCubemapNormalization.y, 1.0 );
	float normalization = specularLuminance > 0.0 ? min( dot( indirectDiffuse, DeadlockLuminance ) / specularLuminance, normalizationCeiling ) : normalizationCeiling;

	float3 diffuse = ( directDiffuse + indirectDiffuse * diffuseEnergy * multiBounce ) * ( albedo * ( 1.0 - metalness ) * ( 1.0 - s.Transmission ) );
	float3 indirectSpecular = ( environmentBrdf * ( cubeSpecular * normalization ) + multiScatter * indirectDiffuse ) * saturate( pow( NdotV + 1.0, roughness2 ) );

	// Glass lets through what the reflection leaves, tinted more the more steeply it's looked through.
	float3 transmitted = 0;
	if ( s.Transmission > 0.0 )
	{
		float3 tint = min( 1.0, exp( log( max( 0.01, albedo ) ) / max( 0.01, dot( normal, toCamera ) ) ) );
		transmitted = s.Transmission * ( 1.0 - metalness ) * diffuseEnergy * tint * s.Transmission * s.Background;
	}

	return diffuse + s.Emission + directSpecular + indirectSpecular + transmitted;
}

#endif // DEADLOCK_LIGHTING_HLSL
