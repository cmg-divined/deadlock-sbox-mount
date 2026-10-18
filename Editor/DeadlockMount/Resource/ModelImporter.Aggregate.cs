using System;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Where world geometry goes for culling: a cell of a grid over the map, apart for geometry that casts no shadows
/// and for aggregates with LODs of their own (<see cref="LodAggregate"/>, else -1), whose switch distances apply.
/// </summary>
internal readonly record struct WorldCell( int X, int Y, int Z, bool NoShadows, int LodAggregate );

/// <summary>
/// A world aggregate's geometry in one <see cref="WorldCell"/> with one material and LOD levels: its fragments placed,
/// tinted and merged, in compact vertices, ready to upload.
/// </summary>
internal sealed class AggregateChunk
{
	public WorldCell Cell;
	public string Material;
	public uint LodMask;
	public List<DeadlockPackedVertex> Vertices = [];
	public List<int> Indices = [];
	public Vector3 Min = new( float.MaxValue );
	public Vector3 Max = new( float.MinValue );

	public BBox Bounds => new( Min, Max );
}

internal sealed partial class ModelImporter
{
	/// <summary>
	/// Horizontal size of the cells world geometry is grouped in, in units; cells are twice as tall. A cell is one
	/// scene object, culled as a whole, drawing one mesh per material.
	/// </summary>
	public const float WorldCellSize = 2048;

	/// <summary>
	/// A world aggregate's geometry, split by the <see cref="WorldCell"/> each fragment's centre falls in. Every
	/// fragment's draw call is placed by its transform and tinted, in world space. Touches no engine state, so
	/// aggregates can be read in parallel.
	/// </summary>
	public List<AggregateChunk> ReadAggregate( WorldAggregate aggregate, int aggregateIndex )
	{
		var chunks = new Dictionary<(WorldCell, string, uint), AggregateChunk>();
		if ( model.Meshes.Count == 0 )
			return [];

		var mesh = model.Meshes[0];
		var drawCalls = mesh.DrawCalls.ToArray();
		var parts = new MeshPart[drawCalls.Length];
		var hasLods = aggregate.LodSetups.Length > 0;

		for ( var fragmentIndex = 0; fragmentIndex < aggregate.Fragments.Length; fragmentIndex++ )
		{
			var fragment = aggregate.Fragments[fragmentIndex];
			if ( fragment.DrawCall < 0 || fragment.DrawCall >= drawCalls.Length )
				continue;

			var drawCall = drawCalls[fragment.DrawCall];
			if ( !drawCall.IsTriangleList || drawCall.IndexCount < 3 || drawCall.VertexBuffers.Length == 0 )
				continue;

			var part = parts[fragment.DrawCall] ??= ReadPart( mesh, drawCall, null, false );
			var bounds = fragment.Transform is { } transform ? TransformBounds( part.Bounds, transform ) : part.Bounds;
			var center = bounds.Center;
			var noShadows = ((fragment.Flags | aggregate.AllFlags) & ObjectTypeFlags.NoShadows) != 0;

			// Fragments spanning several cells (long roads, the sky's shell) get one of their own, so the cell
			// they'd fall in isn't drawn from everywhere with them.
			var oversized = MathF.Max( bounds.Size.x, bounds.Size.y ) > WorldCellSize * 4;

			var cell = oversized
				? new WorldCell( int.MaxValue, aggregateIndex, fragmentIndex, noShadows, hasLods ? aggregateIndex : -1 )
				: new WorldCell(
					(int)MathF.Floor( center.x / WorldCellSize ),
					(int)MathF.Floor( center.y / WorldCellSize ),
					(int)MathF.Floor( center.z / (WorldCellSize * 2) ),
					noShadows,
					hasLods ? aggregateIndex : -1 );

			var lodMask = hasLods ? fragment.LodGroupMask : 0;
			var key = (cell, part.Material, lodMask);

			if ( !chunks.TryGetValue( key, out var chunk ) )
				chunks[key] = chunk = new AggregateChunk { Cell = cell, Material = part.Material, LodMask = lodMask };

			AppendFragment( chunk, part, fragment );
		}

		return [.. chunks.Values];
	}

	/// <summary>
	/// The box around a box moved by a 3x4 transform.
	/// </summary>
	private static BBox TransformBounds( BBox bounds, Matrix3x4 transform )
	{
		var m = transform.Values;
		var min = new Vector3( float.MaxValue );
		var max = new Vector3( float.MinValue );

		for ( var corner = 0; corner < 8; corner++ )
		{
			var p = new Vector3(
				(corner & 1) != 0 ? bounds.Maxs.x : bounds.Mins.x,
				(corner & 2) != 0 ? bounds.Maxs.y : bounds.Mins.y,
				(corner & 4) != 0 ? bounds.Maxs.z : bounds.Mins.z );

			var moved = new Vector3(
				m[0] * p.x + m[1] * p.y + m[2] * p.z + m[3],
				m[4] * p.x + m[5] * p.y + m[6] * p.z + m[7],
				m[8] * p.x + m[9] * p.y + m[10] * p.z + m[11] );

			min = Vector3.Min( min, moved );
			max = Vector3.Max( max, moved );
		}

		return new BBox( min, max );
	}

	/// <summary>
	/// Appends a copy of a draw call's geometry, transformed and tinted the way the fragment places it.
	/// </summary>
	private static void AppendFragment( AggregateChunk chunk, MeshPart part, WorldAggregateFragment fragment )
	{
		var baseVertex = chunk.Vertices.Count;
		var tint = fragment.TintColor;
		var tinted = tint[0] != 255 || tint[1] != 255 || tint[2] != 255;
		chunk.Vertices.EnsureCapacity( baseVertex + part.Vertices.Length );
		chunk.Indices.EnsureCapacity( chunk.Indices.Count + part.Indices.Length );

		if ( fragment.Transform is not { } transform )
		{
			foreach ( var source in part.Vertices )
			{
				var vertex = tinted ? Tint( source, tint ) : source;
				chunk.Vertices.Add( new DeadlockPackedVertex( vertex ) );
				chunk.Min = Vector3.Min( chunk.Min, vertex.Position );
				chunk.Max = Vector3.Max( chunk.Max, vertex.Position );
			}

			foreach ( var index in part.Indices )
				chunk.Indices.Add( baseVertex + index );

			return;
		}

		var m = transform.Values;
		var a = new System.Numerics.Matrix4x4(
			m[0], m[4], m[8], 0,
			m[1], m[5], m[9], 0,
			m[2], m[6], m[10], 0,
			0, 0, 0, 1 );

		var determinant = a.GetDeterminant();

		// Normals transform by the inverse transpose, so non-uniform scale keeps them perpendicular.
		var normalMatrix = System.Numerics.Matrix4x4.Invert( a, out var inverse ) ? System.Numerics.Matrix4x4.Transpose( inverse ) : a;

		foreach ( var source in part.Vertices )
		{
			var vertex = tinted ? Tint( source, tint ) : source;
			var p = vertex.Position;
			vertex.Position = new Vector3(
				m[0] * p.x + m[1] * p.y + m[2] * p.z + m[3],
				m[4] * p.x + m[5] * p.y + m[6] * p.z + m[7],
				m[8] * p.x + m[9] * p.y + m[10] * p.z + m[11] );

			var n = System.Numerics.Vector3.TransformNormal( new System.Numerics.Vector3( vertex.Normal.x, vertex.Normal.y, vertex.Normal.z ), normalMatrix );
			vertex.Normal = new Vector3( n.X, n.Y, n.Z ).Normal;

			var t = System.Numerics.Vector3.TransformNormal( new System.Numerics.Vector3( vertex.Tangent.x, vertex.Tangent.y, vertex.Tangent.z ), a );
			var tangent = new Vector3( t.X, t.Y, t.Z ).Normal;
			vertex.Tangent = new Vector4( tangent.x, tangent.y, tangent.z, determinant < 0 ? -vertex.Tangent.w : vertex.Tangent.w );

			chunk.Vertices.Add( new DeadlockPackedVertex( vertex ) );
			chunk.Min = Vector3.Min( chunk.Min, vertex.Position );
			chunk.Max = Vector3.Max( chunk.Max, vertex.Position );
		}

		// A mirroring transform turns triangles inside out, so their winding flips back.
		var indices = part.Indices;
		for ( var i = 0; i + 2 < indices.Length; i += 3 )
		{
			chunk.Indices.Add( baseVertex + indices[i] );

			if ( determinant < 0 )
			{
				chunk.Indices.Add( baseVertex + indices[i + 2] );
				chunk.Indices.Add( baseVertex + indices[i + 1] );
			}
			else
			{
				chunk.Indices.Add( baseVertex + indices[i + 1] );
				chunk.Indices.Add( baseVertex + indices[i + 2] );
			}
		}
	}

	private static DeadlockStaticVertex Tint( DeadlockStaticVertex vertex, byte[] tint )
	{
		var color = vertex.Tint;
		vertex.Tint = new Color32( (byte)(color.r * tint[0] / 255), (byte)(color.g * tint[1] / 255), (byte)(color.b * tint[2] / 255), color.a );
		return vertex;
	}

	/// <summary>
	/// Uploads an aggregate chunk as a mesh with its material. Materials that don't read compact vertices (the
	/// default material standing in for a missing one) get them unpacked to the full float layout.
	/// </summary>
#pragma warning disable CS0618
	public static Mesh CreateChunkMesh( DeadlockMount mount, AggregateChunk chunk, int textureLevel )
	{
		var material = mount.LoadMaterial( chunk.Material, textureLevel );
		var mesh = new Mesh( chunk.Material, material );
		var vertices = System.Runtime.InteropServices.CollectionsMarshal.AsSpan( chunk.Vertices );

		if ( UsesPackedVertices( material ) )
		{
			mesh.CreateVertexBuffer( vertices.Length, DeadlockPackedVertex.Layout, vertices );
		}
		else
		{
			var full = new DeadlockStaticVertex[vertices.Length];
			for ( var i = 0; i < full.Length; i++ )
				full[i] = Unpack( vertices[i] );

			mesh.CreateVertexBuffer( full.Length, DeadlockStaticVertex.Layout, full.AsSpan() );
		}

		mesh.CreateIndexBuffer( chunk.Indices.Count, System.Runtime.InteropServices.CollectionsMarshal.AsSpan( chunk.Indices ) );
		mesh.Bounds = chunk.Bounds;
		return mesh;
	}
#pragma warning restore CS0618

	private static DeadlockStaticVertex Unpack( in DeadlockPackedVertex vertex )
	{
		Span<float> normal = stackalloc float[3];
		Span<float> tangent = stackalloc float[4];
		VertexDecoding.UnpackTangentFrame( vertex.TangentFrame, normal, tangent );

		return new DeadlockStaticVertex
		{
			Position = vertex.Position,
			Normal = new Vector3( normal[0], normal[1], normal[2] ),
			Tangent = new Vector4( tangent[0], tangent[1], tangent[2], tangent[3] ),
			TexCoord0 = vertex.TexCoord0,
			TexCoord1 = vertex.TexCoord1,
			Color = vertex.Color,
			Tint = vertex.Tint,
			LightmapUV = vertex.LightmapUV == VertexEncoding.NoLightmap
				? DeadlockStaticVertex.NoLightmap
				: new Vector2( (vertex.LightmapUV & 0xFFFF) / 65535f, (vertex.LightmapUV >> 16) / 65535f ),
			BlendParams = new Vector4( vertex.BlendParams.r / 255f, vertex.BlendParams.g / 255f, vertex.BlendParams.b / 255f, vertex.BlendParams.a / 255f ),
		};
	}
}
