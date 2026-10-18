#ifndef DEADLOCK_VERTEX_HLSL
#define DEADLOCK_VERTEX_HLSL

// Vertex processing for the mount's compact vertices (deadlock_vertexinput.hlsl): s&box's VS_CommonProcessing with
// the game's packed tangent frame and lightmap coordinates decoded here rather than by the engine.

// The engine's vertex helpers: transforms, instancing data, the compute skinning cache. Without
// common/vertexinput.hlsl, its own VS_CommonProcessing is left out.
#include "vr_common_vs_code.fxc"

// The 32 bit tangent frame (VertexDecoding.UnpackTangentFrame in the mount): an octahedral normal in bits 12 to 31,
// the tangent's rotation around it in bits 1 to 11, the bitangent sign in bit 0.
void DecodeDeadlockTangentFrame( uint packed, out float3 normal, out float4 tangent )
{
	float x = float( ( packed >> 12 ) & 0x3FF ) / 1023.0 * 2.0 - 1.0;
	float y = float( ( packed >> 22 ) & 0x3FF ) / 1023.0 * 2.0 - 1.0;
	float z = 1.0 - abs( x ) - abs( y );

	// Points below the octahedron's equator fold back over it.
	float fold = saturate( -z );
	x += x >= 0.0 ? -fold : fold;
	y += y >= 0.0 ? -fold : fold;
	normal = normalize( float3( x, y, z ) );

	// A tangent derived from the normal alone, then rotated into place by the stored angle.
	float tangentSign = normal.z >= 0.0 ? 1.0 : -1.0;
	float reciprocal = 1.0 / ( tangentSign + normal.z );
	float3 reference = float3(
		-tangentSign * normal.x * normal.x * reciprocal + 1.0,
		-tangentSign * normal.x * normal.y * reciprocal,
		-tangentSign * normal.x );

	float angle = float( ( packed >> 1 ) & 0x7FF ) / 2047.0 * 6.28318530718;
	float s, c;
	sincos( angle, s, c );

	tangent.xyz = reference * c + cross( normal, reference ) * s;
	tangent.w = ( packed & 1 ) != 0 ? 1.0 : -1.0;
}

// Two 16 bit fractions, or (-1, -1) for geometry the lightmaps don't cover (all bits set).
float2 DecodeDeadlockLightmapUV( uint packed )
{
	if ( packed == 0xFFFFFFFF )
		return float2( -1.0, -1.0 );

	return float2( packed & 0xFFFF, packed >> 16 ) / 65535.0;
}

PixelInput ProcessDeadlockVertex( VertexInput i )
{
	PixelInput o;

	ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( i.nInstanceTransformID );
	o.vVertexColor.rgba = extraShaderData.vTint;

	#if ( D_CS_VERTEX_ANIMATION )
	{
		uint nVertexId = extraShaderData.nVertexCacheOffset + uint( i.nVertexCacheIndex );
		CachedAnimatedVertex_t vertex = FetchCachedVertex( nVertexId );

		float3 vNormal;
		float3 vTangent;
		float flSign;
		DecodeCachedVertexTangentSpace( vertex, vNormal, vTangent, flSign );

		o.vPositionWs.xyz = vertex.vPosWs;
		o.vNormalWs.xyz = vNormal;
		o.vTangentUWs.xyz = vTangent;
		o.vTangentVWs.xyz = cross( vNormal, vTangent ) * flSign;
	}
	#else
	{
		float3 vNormalOs;
		float4 vTangentUOs_flTangentVSign;
		DecodeDeadlockTangentFrame( i.nTangentFrame, vNormalOs, vTangentUOs_flTangentVSign );

		float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID, i.nBoneIndex.x );
		o.vPositionWs = mul( matObjectToWorld, float4( i.vPositionOs.xyz, 1.0 ) );

		float flTransformSign;
		o.vNormalWs.xyz = TransformNormalInverseTranspose( vNormalOs.xyz, (float3x3)matObjectToWorld, flTransformSign );

		// The tangent, kept perpendicular to the normal.
		float3 vTangentUWs = mul( matObjectToWorld, float4( vTangentUOs_flTangentVSign.xyz, 0.0 ) );
		vTangentUWs.xyz = normalize( vTangentUWs.xyz - ( o.vNormalWs.xyz * dot( vTangentUWs.xyz, o.vNormalWs.xyz ) ) );

		o.vTangentUWs.xyz = vTangentUWs.xyz;
		o.vTangentVWs.xyz = cross( o.vNormalWs.xyz, vTangentUWs.xyz ) * vTangentUOs_flTangentVSign.w * flTransformSign;
	}
	#endif

	o.vPositionPs.xyzw = Position3WsToPs( o.vPositionWs.xyz );
	o.vTextureCoords.xy = i.vTexCoord.xy;
	o.vTextureCoords.zw = i.vTexCoord2.xy;
	o.vLightmapUV.xy = DecodeDeadlockLightmapUV( i.nLightmapUV );

	return o;
}

PixelInput FinalizeDeadlockVertex( PixelInput i )
{
	PixelInput o = i;
	o.vPositionWs.xyz -= g_vHighPrecisionLightingOffsetWs.xyz;
	return o;
}

#endif
