using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Vertex for meshes rendered in model space.
/// </summary>
/// <remarks>
/// Vertex buffers are created from <see cref="Layout"/> rather than from the struct's layout attributes: s&amp;box
/// frees the native layouts it caches per struct on every hotload, which leaves the meshes made before it skinning
/// from freed layouts. A buffer created with an explicit layout keeps its own.
/// </remarks>
[StructLayout( LayoutKind.Sequential )]
internal struct DeadlockStaticVertex
{
	public Vector3 Position;
	public Vector3 Normal;
	public Vector4 Tangent;
	public Vector2 TexCoord0;
	public Vector2 TexCoord1;

	/// <summary>
	/// The mesh's own vertex colour, or the vertex paint of world geometry.
	/// </summary>
	public Color32 Color;

	/// <summary>
	/// The object tint the game gives a draw call or an aggregate fragment, in gamma space with alpha. The shaders
	/// combine it with the vertex colour in their own ways, so it is kept apart from it.
	/// </summary>
	public Color32 Tint;

	/// <summary>
	/// Where world geometry is in the map's lightmaps, before the map's lightmap UV scale. Negative for geometry the
	/// lightmaps don't cover, which the light probes light instead.
	/// </summary>
	public Vector2 LightmapUV;

	/// <summary>
	/// Vertex painted parameters of the environment shaders, 0 to 1: layer reveal for environment_blend, roughness,
	/// metalness, self illumination and tint amount for environment_simple.
	/// </summary>
	public Vector4 BlendParams;

	public static readonly Vector2 NoLightmap = new( -1, -1 );

	public static readonly VertexAttribute[] Layout =
	[
		new( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
		new( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3 ),
		new( VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 4 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 0 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 1 ),
		new( VertexAttributeType.Color, VertexAttributeFormat.UInt8, 4, 0 ),
		new( VertexAttributeType.Color, VertexAttributeFormat.UInt8, 4, 1 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 3 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 4, 4 ),
	];
}

/// <summary>
/// The compact vertex the mount's shaders read (48 bytes, against 88 for <see cref="DeadlockStaticVertex"/>), in the
/// formats the game stores its vertices in: the tangent frame packed into 32 bits (<see cref="VertexEncoding"/>),
/// lightmap coordinates as 16 bit fractions, colours and vertex paint as bytes. Assets/shaders/deadlock_vertex.hlsl
/// decodes it.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct DeadlockPackedVertex
{
	public Vector3 Position;
	public uint TangentFrame;
	public Vector2 TexCoord0;
	public Vector2 TexCoord1;
	public Color32 Color;
	public Color32 Tint;
	public uint LightmapUV;
	public Color32 BlendParams;

	public static readonly VertexAttribute[] Layout =
	[
		new( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.UInt32, 1, 6 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 0 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 1 ),
		new( VertexAttributeType.Color, VertexAttributeFormat.UInt8, 4, 0 ),
		new( VertexAttributeType.Color, VertexAttributeFormat.UInt8, 4, 1 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.UInt32, 1, 3 ),
		new( VertexAttributeType.Color, VertexAttributeFormat.UInt8, 4, 2 ),
	];

	public DeadlockPackedVertex( in DeadlockStaticVertex vertex )
	{
		Position = vertex.Position;
		TangentFrame = VertexEncoding.PackTangentFrame( vertex.Normal.x, vertex.Normal.y, vertex.Normal.z, vertex.Tangent.x, vertex.Tangent.y, vertex.Tangent.z, vertex.Tangent.w );
		TexCoord0 = vertex.TexCoord0;
		TexCoord1 = vertex.TexCoord1;
		Color = vertex.Color;
		Tint = vertex.Tint;
		LightmapUV = VertexEncoding.PackLightmapUV( vertex.LightmapUV.x, vertex.LightmapUV.y );
		BlendParams = PackBlendParams( vertex.BlendParams );
	}

	public static Color32 PackBlendParams( Vector4 value ) => new(
		VertexEncoding.PackUnorm8( value.x ), VertexEncoding.PackUnorm8( value.y ), VertexEncoding.PackUnorm8( value.z ), VertexEncoding.PackUnorm8( value.w ) );
}

/// <summary>
/// <see cref="DeadlockPackedVertex"/> for skinned meshes: s&amp;box skins them in a compute shader that reads the
/// normal and tangent as floats, so those stay alongside (84 bytes, against 96).
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct DeadlockPackedSkinnedVertex
{
	public Vector3 Position;
	public Vector3 Normal;
	public Vector4 Tangent;
	public uint TangentFrame;
	public Vector2 TexCoord0;
	public Vector2 TexCoord1;
	public Color32 Color;
	public Color32 Tint;
	public uint LightmapUV;
	public Color32 BlendParams;
	public Color32 BlendIndices;
	public Color32 BlendWeights;

	public static readonly VertexAttribute[] Layout =
	[
		new( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
		new( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3 ),
		new( VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 4 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.UInt32, 1, 6 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 0 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 1 ),
		new( VertexAttributeType.Color, VertexAttributeFormat.UInt8, 4, 0 ),
		new( VertexAttributeType.Color, VertexAttributeFormat.UInt8, 4, 1 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.UInt32, 1, 3 ),
		new( VertexAttributeType.Color, VertexAttributeFormat.UInt8, 4, 2 ),
		new( VertexAttributeType.BlendIndices, VertexAttributeFormat.UInt8, 4 ),
		new( VertexAttributeType.BlendWeights, VertexAttributeFormat.UInt8, 4 ),
	];

	public DeadlockPackedSkinnedVertex( in DeadlockStaticVertex vertex )
	{
		Position = vertex.Position;
		Normal = vertex.Normal;
		Tangent = vertex.Tangent;
		TangentFrame = VertexEncoding.PackTangentFrame( vertex.Normal.x, vertex.Normal.y, vertex.Normal.z, vertex.Tangent.x, vertex.Tangent.y, vertex.Tangent.z, vertex.Tangent.w );
		TexCoord0 = vertex.TexCoord0;
		TexCoord1 = vertex.TexCoord1;
		Color = vertex.Color;
		Tint = vertex.Tint;
		LightmapUV = VertexEncoding.PackLightmapUV( vertex.LightmapUV.x, vertex.LightmapUV.y );
		BlendParams = DeadlockPackedVertex.PackBlendParams( vertex.BlendParams );
	}
}

/// <summary>
/// Vertex for meshes skinned to the model's skeleton, with up to four bone influences.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct DeadlockSkinnedVertex
{
	public Vector3 Position;
	public Vector3 Normal;
	public Vector4 Tangent;
	public Vector2 TexCoord0;
	public Vector2 TexCoord1;
	public Color32 Color;
	public Color32 Tint;
	public Vector2 LightmapUV;
	public Vector4 BlendParams;
	public Color32 BlendIndices;
	public Color32 BlendWeights;

	public static readonly VertexAttribute[] Layout =
	[
		.. DeadlockStaticVertex.Layout,
		new( VertexAttributeType.BlendIndices, VertexAttributeFormat.UInt8, 4 ),
		new( VertexAttributeType.BlendWeights, VertexAttributeFormat.UInt8, 4 ),
	];

	public DeadlockSkinnedVertex( DeadlockStaticVertex vertex )
	{
		Position = vertex.Position;
		Normal = vertex.Normal;
		Tangent = vertex.Tangent;
		TexCoord0 = vertex.TexCoord0;
		TexCoord1 = vertex.TexCoord1;
		Color = vertex.Color;
		Tint = vertex.Tint;
		LightmapUV = vertex.LightmapUV;
		BlendParams = vertex.BlendParams;
	}
}
