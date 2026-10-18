// The Deadlock mount's compact vertex (DeadlockPackedVertex and DeadlockPackedSkinnedVertex in the mount's
// ModelVertices.cs), declared inside each mount shader's VertexInput in place of common/vertexinput.hlsl:
// the game's own formats, which deadlock_vertex.hlsl decodes.

float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
float2 vTexCoord2 : TEXCOORD1 < Semantic( LowPrecisionUv1 ); >;

// Octahedral normal (20 bits), tangent rotation around it (11 bits) and bitangent sign (1 bit).
uint nTangentFrame : TEXCOORD6 < Semantic( None ); >;

// Lightmap coordinate as two 16 bit fractions; all bits set off the lightmaps.
uint nLightmapUV : TEXCOORD3 < Semantic( None ); >;

float4 vMeshColor : COLOR0 < Semantic( Color ); >;
float4 vMeshTint : COLOR1 < Semantic( None ); >;

// Vertex painted parameters of the environment shaders.
float4 vBlendParams : COLOR2 < Semantic( None ); >;

// Skinned meshes: s&box's compute skinning writes them to a cache the vertex shader reads instead.
#if ( D_CS_VERTEX_ANIMATION )
	float4 vBlendWeight : BLENDWEIGHT < Semantic( BlendWeight ); >;
	float nVertexIndex : TEXCOORD14 < Semantic( MorphIndex ); >;
	float nVertexCacheIndex : TEXCOORD15 < Semantic( MorphIndex ); >;
#endif

// Instancing data, as common/vertexinput.hlsl has it.
uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
uint nBoneIndex : BLENDINDICES < Semantic( BlendIndices ); >;
