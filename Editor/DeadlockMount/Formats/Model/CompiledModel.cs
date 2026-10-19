using System;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// A bone of a model skeleton, relative to its parent. Rotation is a quaternion (x, y, z, w).
/// </summary>
internal readonly record struct CompiledBone( string Name, int Parent, float[] Position, float[] Rotation );

/// <summary>
/// A mesh group that is a body group choice. Compiled names read <c>group_@choice_#&amp;choicename</c>, e.g.
/// <c>head_@1_#&amp;smug</c>; the choice name is optional.
/// </summary>
internal readonly record struct BodyGroupChoice( string Group, int Choice, string ChoiceName );

/// <summary>
/// A compiled model (vmdl_c): its skeleton, level of detail and mesh group tables and its embedded meshes.
/// </summary>
internal sealed class CompiledModel
{
	public ResourceFile Resource { get; }

	/// <summary>
	/// The DATA block, PermModelData_t.
	/// </summary>
	public KVObject Data { get; }

	public string Name => Data.GetString( "m_name", "" );

	public CompiledBone[] Bones { get; }

	public IReadOnlyList<CompiledMesh> Meshes { get; }

	/// <summary>
	/// Paths of meshes stored in their own vmesh_c, indexed by mesh index. Empty entries are embedded meshes.
	/// </summary>
	public string[] ReferencedMeshes { get; }

	public float[] LodSwitchDistances { get; }

	public ulong DefaultMeshGroupMask { get; }

	public string[] MeshGroups { get; }

	private readonly long[] _lodMasks;
	private readonly long[] _meshGroupMasks;
	private readonly long[] _remapTable;
	private readonly long[] _remapStarts;

	public CompiledModel( byte[] data )
	{
		Resource = new ResourceFile( data );
		Data = Resource.ReadKeyValues( "DATA" ) ?? throw new InvalidDataException( "Model has no DATA block." );

		Bones = ReadSkeleton( Data.GetObject( "m_modelSkeleton" ) );
		ReferencedMeshes = Data.GetStringArray( "m_refMeshes" );
		LodSwitchDistances = Data.GetFloatArray( "m_lodGroupSwitchDistances" );
		MeshGroups = Data.GetStringArray( "m_meshGroups" );
		DefaultMeshGroupMask = unchecked((ulong)Data.GetInt64( "m_nDefaultMeshGroupMask", -1 ));

		_lodMasks = Data.GetInt64Array( "m_refLODGroupMasks" );
		_meshGroupMasks = Data.GetInt64Array( "m_refMeshGroupMasks" );
		_remapTable = Data.GetInt64Array( "m_remappingTable" );
		_remapStarts = Data.GetInt64Array( "m_remappingTableStarts" );

		Meshes = ReadEmbeddedMeshes();
	}

	private static CompiledBone[] ReadSkeleton( KVObject skeleton )
	{
		if ( skeleton is null )
			return [];

		var names = skeleton.GetStringArray( "m_boneName" );
		var parents = skeleton.GetInt64Array( "m_nParent" );
		var positions = skeleton.GetArray( "m_bonePosParent" ) ?? [];
		var rotations = skeleton.GetArray( "m_boneRotParent" ) ?? [];

		var bones = new CompiledBone[names.Length];
		for ( var i = 0; i < bones.Length; i++ )
		{
			bones[i] = new CompiledBone(
				names[i],
				i < parents.Length ? (int)parents[i] : -1,
				ToFloats( i < positions.Length ? positions[i] : null, 3 ),
				ToFloats( i < rotations.Length ? rotations[i] : null, 4, w: 1 ) );
		}

		return bones;
	}

	private static float[] ToFloats( object value, int count, float w = 0 )
	{
		var result = new float[count];
		if ( count == 4 )
			result[3] = w;

		if ( value is object[] array )
		{
			for ( var i = 0; i < count && i < array.Length; i++ )
				result[i] = KVValue.ToFloat( array[i] );
		}

		return result;
	}

	private List<CompiledMesh> ReadEmbeddedMeshes()
	{
		var meshes = new List<CompiledMesh>();
		var control = Resource.ReadKeyValues( "CTRL" );

		if ( control is null )
			return meshes;

		foreach ( var entry in control.GetObjects( "embedded_meshes" ) )
		{
			// Older models keep the buffers in a VBIB/MBUF block, newer ones describe MVTX/MIDX blocks inline.
			if ( entry.ContainsKey( "vbib_block" ) )
			{
				meshes.Add( new CompiledMesh(
					Resource,
					entry.GetString( "name", "" ),
					entry.GetInt32( "mesh_index" ),
					Resource.ReadKeyValues( entry.GetInt32( "data_block" ) ),
					bufferBlock: entry.GetInt32( "vbib_block" ),
					vertexBuffers: null,
					indexBuffers: null,
					morphBlock: entry.GetInt32( "morph_block", -1 ) ) );
			}
			else
			{
				meshes.Add( new CompiledMesh(
					Resource,
					entry.GetString( "m_Name", "" ),
					entry.GetInt32( "m_nMeshIndex" ),
					Resource.ReadKeyValues( entry.GetInt32( "m_nDataBlock" ) ),
					bufferBlock: -1,
					vertexBuffers: entry.GetObjects( "m_vertexBuffers" ).ToArray(),
					indexBuffers: entry.GetObjects( "m_indexBuffers" ).ToArray(),
					morphBlock: entry.GetInt32( "m_nMorphBlock", -1 ) ) );
			}
		}

		return meshes;
	}

	/// <summary>
	/// The LOD mask of a mesh: bit N set means it is drawn at LOD N. Meshes without an entry are in every LOD.
	/// </summary>
	public long GetLodMask( int meshIndex )
	{
		return meshIndex < _lodMasks.Length && _lodMasks[meshIndex] != 0 ? _lodMasks[meshIndex] : -1;
	}

	/// <summary>
	/// The lowest (most detailed) LOD level any mesh is in.
	/// </summary>
	public int LowestLod
	{
		get
		{
			long combined = 0;
			foreach ( var mask in _lodMasks )
				combined |= mask;

			return combined == 0 ? 0 : System.Numerics.BitOperations.TrailingZeroCount( combined );
		}
	}

	/// <summary>
	/// The mesh group mask of a mesh: bit N set means it belongs to <see cref="MeshGroups"/>[N].
	/// </summary>
	public ulong GetMeshGroupMask( int meshIndex )
	{
		return meshIndex < _meshGroupMasks.Length ? unchecked((ulong)_meshGroupMasks[meshIndex]) : ulong.MaxValue;
	}

	/// <summary>
	/// The body group choice each mesh group stands for, by mesh group index. Null for mesh groups that aren't
	/// a choice, such as <c>autodefault</c>, which holds meshes outside any body group.
	/// </summary>
	public BodyGroupChoice?[] BodyGroupChoices => MeshGroups.Select( ParseBodyGroupChoice ).ToArray();

	private static BodyGroupChoice? ParseBodyGroupChoice( string meshGroup )
	{
		// The first "_@" followed by a number, then the end or "_#&"; choice names may contain anything.
		for ( var at = meshGroup.IndexOf( "_@", StringComparison.Ordinal ); at > 0; at = meshGroup.IndexOf( "_@", at + 2, StringComparison.Ordinal ) )
		{
			var rest = meshGroup[(at + 2)..];
			var hash = rest.IndexOf( "_#&", StringComparison.Ordinal );
			var number = hash >= 0 ? rest[..hash] : rest;

			if ( int.TryParse( number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var choice ) )
				return new BodyGroupChoice( meshGroup[..at], choice, hash >= 0 ? rest[(hash + 3)..] : null );
		}

		return null;
	}

	/// <summary>
	/// Whether a mesh is visible with the default mesh group selection.
	/// </summary>
	public bool IsVisibleByDefault( int meshIndex )
	{
		return MeshGroups.Length <= 1 || (GetMeshGroupMask( meshIndex ) & DefaultMeshGroupMask) != 0;
	}

	/// <summary>
	/// Maps the bone indices a mesh's vertices use to model skeleton bone indices, or null if the mesh has none.
	/// </summary>
	public int[] GetBoneRemapTable( int meshIndex )
	{
		if ( meshIndex >= _remapStarts.Length )
			return null;

		var start = (int)_remapStarts[meshIndex];
		var end = meshIndex + 1 < _remapStarts.Length ? (int)_remapStarts[meshIndex + 1] : _remapTable.Length;

		return _remapTable[start..end].Select( x => (int)x ).ToArray();
	}

	/// <summary>
	/// Material groups (skins) as their name and material paths.
	/// </summary>
	public IEnumerable<(string Name, string[] Materials)> MaterialGroups =>
		Data.GetObjects( "m_materialGroups" ).Select( x => (x.GetString( "m_name", "" ), x.GetStringArray( "m_materials" )) );

	/// <summary>
	/// The model's bodies and joints (PHYS block), or null if it has no physics.
	/// </summary>
	public CompiledPhysics ReadPhysics()
	{
		return Resource.ReadKeyValues( "PHYS" ) is { } physics ? new CompiledPhysics( physics ) : null;
	}

	/// <summary>
	/// The model's cloth (in the PHYS block), or null if it has none.
	/// </summary>
	public CompiledCloth ReadCloth()
	{
		return Resource.ReadKeyValues( "PHYS" )?.GetObject( "m_pFeModel" ) is { } cloth ? new CompiledCloth( cloth ) : null;
	}

	public ModelAttachment[] ReadAttachments() => ModelMarkup.ReadAttachments( Meshes.Select( x => x.Data ) );

	public ModelHitboxSet[] ReadHitboxSets() => ModelMarkup.ReadHitboxSets( Meshes.Select( x => x.Data ) );
}
