using System;
using System.Runtime.InteropServices;

namespace Deadlock.Particles.Renderers;

/// <summary>
/// One corner of a particle quad, in world space, as <c>deadlock_particle.shader</c> reads it.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct DeadlockParticleVertex
{
	[VertexLayout.Position] public Vector3 Position;

	/// <summary>
	/// The particle's colour, in gamma space unless the renderer says otherwise, and its alpha.
	/// </summary>
	[VertexLayout.Color] public Vector4 Color;

	/// <summary>
	/// Each texture layer's coordinates in the current sheet frame (xy) and the next (zw).
	/// </summary>
	[VertexLayout.TexCoord( 0 )] public Vector4 Layer0;
	[VertexLayout.TexCoord( 1 )] public Vector4 Layer1;
	[VertexLayout.TexCoord( 2 )] public Vector4 Layer2;
	[VertexLayout.TexCoord( 3 )] public Vector4 Layer3;
	[VertexLayout.TexCoord( 4 )] public Vector4 Layer4;

	/// <summary>
	/// How far between the two sheet frames (x), and where on the card the corner is (yz).
	/// </summary>
	[VertexLayout.TexCoord( 5 )] public Vector4 Card;

	/// <summary>
	/// Where the particle is lit from (xyz): its centre, so a whole card lights the same.
	/// </summary>
	[VertexLayout.TexCoord( 6 )] public Vector4 LightingPosition;

	public void SetLayer( int layer, Vector4 uvs )
	{
		switch ( layer )
		{
			case 0: Layer0 = uvs; break;
			case 1: Layer1 = uvs; break;
			case 2: Layer2 = uvs; break;
			case 3: Layer3 = uvs; break;
			case 4: Layer4 = uvs; break;
		}
	}
}

/// <summary>
/// The camera particles are drawn for: where it is and its axes, which billboards face.
/// </summary>
internal readonly record struct ParticleView( System.Numerics.Vector3 Location, System.Numerics.Vector3 Right, System.Numerics.Vector3 Up,
	System.Numerics.Vector3 Forward, float ProjectionScale )
{
	public static ParticleView FromCamera( Transform camera, float fieldOfView )
	{
		var rotation = camera.Rotation;
		return new ParticleView( camera.Position, rotation.Right, rotation.Up, rotation.Forward,
			1f / MathF.Tan( MathF.Max( fieldOfView, 1f ) * 0.5f * (MathF.PI / 180f) ) );
	}
}

/// <summary>
/// A renderer's quads: built on the CPU each draw, uploaded to a vertex buffer of its own and drawn with the quad
/// index buffer every renderer shares.
/// </summary>
internal sealed class ParticleQuadBuffer : IDisposable
{
	/// <summary>
	/// Quads past this many in one draw aren't drawn.
	/// </summary>
	public const int MaxQuads = 65536;

	private static GpuBuffer<uint> _indices;
	private static int _indexQuads;

	/// <summary>
	/// Buffers outgrown while drawing. Buffers can only be disposed of on the main thread, so they wait for
	/// <see cref="ReleaseOutgrown"/>.
	/// </summary>
	private static readonly System.Collections.Concurrent.ConcurrentQueue<GpuBuffer> Outgrown = new();

	/// <summary>
	/// Disposes of the buffers drawing has outgrown. Called on the main thread.
	/// </summary>
	public static void ReleaseOutgrown()
	{
		while ( Outgrown.TryDequeue( out var buffer ) )
			buffer.Dispose();
	}

	private GpuBuffer<DeadlockParticleVertex> _vertices;
	private DeadlockParticleVertex[] _staging = [];

	/// <summary>
	/// Room for <paramref name="quads"/> quads' vertices to be written to, four per quad.
	/// </summary>
	public Span<DeadlockParticleVertex> Begin( int quads )
	{
		quads = Math.Min( quads, MaxQuads );

		if ( _staging.Length < quads * 4 )
			_staging = new DeadlockParticleVertex[Math.Max( quads * 4, _staging.Length * 2 )];

		return _staging.AsSpan( 0, quads * 4 );
	}

	/// <summary>
	/// Uploads the first <paramref name="quads"/> quads written since <see cref="Begin"/> and draws them.
	/// </summary>
	public void Draw( int quads, Material material, RenderAttributes attributes )
	{
		quads = Math.Min( quads, MaxQuads );
		if ( quads <= 0 )
			return;

		var vertexCount = quads * 4;

		if ( _vertices is null || _vertices.ElementCount < vertexCount )
		{
			if ( _vertices is not null )
				Outgrown.Enqueue( _vertices );

			_vertices = new GpuBuffer<DeadlockParticleVertex>( Math.Max( vertexCount, 256 ) * 2, GpuBuffer.UsageFlags.Vertex, "Deadlock particle quads" );
		}

		_vertices.SetData( _staging.AsSpan( 0, vertexCount ) );
		Graphics.Draw( _vertices, Indices( quads ), material, 0, quads * 6, attributes );
	}

	/// <summary>
	/// Two triangles per quad, its corners in order around it.
	/// </summary>
	private static GpuBuffer<uint> Indices( int quads )
	{
		if ( _indices is not null && _indexQuads >= quads )
			return _indices;

		var count = Math.Max( quads, Math.Max( 1024, _indexQuads * 2 ) );
		var indices = new uint[count * 6];

		for ( var i = 0; i < count; i++ )
		{
			var corner = (uint)(i * 4);
			indices[i * 6 + 0] = corner;
			indices[i * 6 + 1] = corner + 1;
			indices[i * 6 + 2] = corner + 2;
			indices[i * 6 + 3] = corner;
			indices[i * 6 + 4] = corner + 2;
			indices[i * 6 + 5] = corner + 3;
		}

		if ( _indices is not null )
			Outgrown.Enqueue( _indices );

		_indices = new GpuBuffer<uint>( indices.Length, GpuBuffer.UsageFlags.Index, "Deadlock particle quad indices" );
		_indices.SetData( indices.AsSpan() );
		_indexQuads = count;

		return _indices;
	}

	public void Dispose()
	{
		_vertices?.Dispose();
		_vertices = null;
	}
}
