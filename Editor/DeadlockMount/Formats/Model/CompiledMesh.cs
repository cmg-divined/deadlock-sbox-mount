using System;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// One draw call of a mesh: a range of an index buffer drawn with one material.
/// </summary>
internal sealed record DrawCall(
	string Material,
	string PrimitiveType,
	int BaseVertex,
	int VertexCount,
	int StartIndex,
	int IndexCount,
	int[] VertexBuffers,
	int[] VertexBindOffsets,
	int IndexBuffer,
	int IndexBindOffset,
	float[] TintColor,
	float Alpha,
	bool HasBakedLightingFromLightMap )
{
	public bool IsTriangleList => PrimitiveType is "RENDER_PRIM_TRIANGLES" or "5";
}

/// <summary>
/// A mesh embedded in a compiled model: its CRenderMesh data and its vertex and index buffers.
/// Buffers are decoded on first use, so meshes of unused LODs cost nothing.
/// </summary>
internal sealed class CompiledMesh
{
	private readonly ResourceFile _resource;
	private readonly int _bufferBlock;
	private readonly KVObject[] _vertexBufferDescriptions;
	private readonly KVObject[] _indexBufferDescriptions;
	private List<MeshBuffer> _vertexBuffers;
	private List<MeshBuffer> _indexBuffers;

	public string Name { get; }

	/// <summary>
	/// The mesh's index in the model's LOD, mesh group and bone remap tables.
	/// </summary>
	public int MeshIndex { get; }

	/// <summary>
	/// The mesh data, CRenderMesh.
	/// </summary>
	public KVObject Data { get; }

	private readonly int _morphBlock;
	private CompiledMorphSet _morphSet;

	public CompiledMesh( ResourceFile resource, string name, int meshIndex, KVObject data, int bufferBlock, KVObject[] vertexBuffers, KVObject[] indexBuffers, int morphBlock )
	{
		_resource = resource;
		_bufferBlock = bufferBlock;
		_vertexBufferDescriptions = vertexBuffers;
		_indexBufferDescriptions = indexBuffers;
		_morphBlock = morphBlock;

		Name = name;
		MeshIndex = meshIndex;
		Data = data;
	}

	/// <summary>
	/// The mesh's morph set (flexes, their rules and deltas), or null if it has none.
	/// </summary>
	public CompiledMorphSet MorphSet
	{
		get
		{
			if ( _morphSet is null && _morphBlock >= 0 && _morphBlock < _resource.Blocks.Count )
				_morphSet = new CompiledMorphSet( _resource.ReadKeyValues( _morphBlock ) );

			return _morphSet;
		}
	}

	/// <summary>
	/// How many bones influence each vertex, zero for unskinned meshes.
	/// </summary>
	public int BoneWeightCount => Data.GetObject( "m_skeleton" )?.GetInt32( "m_nBoneWeightCount" ) ?? 0;

	public IReadOnlyList<MeshBuffer> VertexBuffers
	{
		get
		{
			DecodeBuffers();
			return _vertexBuffers;
		}
	}

	public IReadOnlyList<MeshBuffer> IndexBuffers
	{
		get
		{
			DecodeBuffers();
			return _indexBuffers;
		}
	}

	private void DecodeBuffers()
	{
		if ( _vertexBuffers is not null )
			return;

		if ( _bufferBlock >= 0 )
		{
			(_vertexBuffers, _indexBuffers) = MeshBuffer.ReadBufferBlock( _resource.GetBlockData( _bufferBlock ) );
			return;
		}

		if ( _vertexBufferDescriptions is not null )
		{
			_vertexBuffers = _vertexBufferDescriptions.Select( x => MeshBuffer.ReadDescribedBuffer( x, _resource, isVertex: true ) ).ToList();
			_indexBuffers = _indexBufferDescriptions.Select( x => MeshBuffer.ReadDescribedBuffer( x, _resource, isVertex: false ) ).ToList();
			return;
		}

		// Old style meshes keep their buffer descriptions in the mesh data itself.
		_vertexBuffers = Data.GetObjects( "m_vertexBuffers" ).Select( x => MeshBuffer.ReadDescribedBuffer( x, _resource, isVertex: true ) ).ToList();
		_indexBuffers = Data.GetObjects( "m_indexBuffers" ).Select( x => MeshBuffer.ReadDescribedBuffer( x, _resource, isVertex: false ) ).ToList();
	}

	public IEnumerable<DrawCall> DrawCalls
	{
		get
		{
			foreach ( var sceneObject in Data.GetObjects( "m_sceneObjects" ) )
			{
				foreach ( var drawCall in sceneObject.GetObjects( "m_drawCalls" ) )
					yield return ReadDrawCall( drawCall );
			}
		}
	}

	private static DrawCall ReadDrawCall( KVObject drawCall )
	{
		var vertexBuffers = drawCall.GetObjects( "m_vertexBuffers" ).ToArray();
		var indexBuffer = drawCall.GetObject( "m_indexBuffer" );
		var primitiveType = drawCall["m_nPrimitiveType"] switch
		{
			string name => name,
			{ } value => KVValue.ToInt64( value ).ToString( System.Globalization.CultureInfo.InvariantCulture ),
			_ => "",
		};

		return new DrawCall(
			drawCall.GetString( "m_material" ) ?? drawCall.GetString( "m_pMaterial" ) ?? "",
			primitiveType,
			drawCall.GetInt32( "m_nBaseVertex" ),
			drawCall.GetInt32( "m_nVertexCount" ),
			drawCall.GetInt32( "m_nStartIndex" ),
			drawCall.GetInt32( "m_nIndexCount" ),
			vertexBuffers.Select( x => x.GetInt32( "m_hBuffer" ) ).ToArray(),
			vertexBuffers.Select( x => x.GetInt32( "m_nBindOffsetBytes" ) ).ToArray(),
			indexBuffer?.GetInt32( "m_hBuffer" ) ?? 0,
			indexBuffer?.GetInt32( "m_nBindOffsetBytes" ) ?? 0,
			drawCall.GetFloatArray( "m_vTintColor" ),
			drawCall.GetFloat( "m_flAlpha", 1 ),
			drawCall.GetBool( "m_bHasBakedLightingFromLightMap" ) );
	}
}
