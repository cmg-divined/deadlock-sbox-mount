using System;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Packs vertex attributes into the compact formats the game itself stores them in, which the mount's shaders
/// decode: the 32 bit tangent frame <see cref="VertexDecoding"/> reads, 16 bit lightmap coordinates and 8 bit
/// colours. Values decoded from those formats pack back to the same bits.
/// </summary>
internal static class VertexEncoding
{
	/// <summary>
	/// The packed lightmap coordinate of vertices the lightmaps don't cover. Lightmap coordinates only reach the
	/// lightmap UV scale's reciprocal (under 1), so the far corner is free.
	/// </summary>
	public const uint NoLightmap = 0xFFFFFFFF;

	/// <summary>
	/// Packs a normal and tangent (with the bitangent sign in its w) as an octahedral normal in 20 bits, the
	/// tangent's rotation around the normal in 11 bits and the sign in the lowest bit.
	/// </summary>
	public static uint PackTangentFrame( float nx, float ny, float nz, float tx, float ty, float tz, float tw )
	{
		var length = MathF.Sqrt( nx * nx + ny * ny + nz * nz );
		if ( !(length > 1e-12f) )
		{
			nx = 0;
			ny = 0;
			nz = 1;
		}
		else
		{
			nx /= length;
			ny /= length;
			nz /= length;
		}

		// Onto the octahedron, with the lower half folded over the upper.
		var l1 = MathF.Abs( nx ) + MathF.Abs( ny ) + MathF.Abs( nz );
		var ox = nx / l1;
		var oy = ny / l1;
		if ( nz < 0 )
		{
			var fx = (1 - MathF.Abs( oy )) * (ox >= 0 ? 1 : -1);
			var fy = (1 - MathF.Abs( ox )) * (oy >= 0 ? 1 : -1);
			ox = fx;
			oy = fy;
		}

		var xBits = (uint)Math.Clamp( (int)MathF.Round( (ox * 0.5f + 0.5f) * 1023f ), 0, 1023 );
		var yBits = (uint)Math.Clamp( (int)MathF.Round( (oy * 0.5f + 0.5f) * 1023f ), 0, 1023 );

		// The tangent's angle is measured in the frame the decoder builds from the quantized normal.
		Span<float> normal = stackalloc float[3];
		Span<float> reference = stackalloc float[4];
		VertexDecoding.UnpackTangentFrame( (xBits << 12) | (yBits << 22), normal, reference );

		// Unpacking angle 0 gives the reference tangent; its cross product with the normal is the quarter turn.
		var bx = normal[1] * reference[2] - normal[2] * reference[1];
		var by = normal[2] * reference[0] - normal[0] * reference[2];
		var bz = normal[0] * reference[1] - normal[1] * reference[0];

		var angle = MathF.Atan2( tx * bx + ty * by + tz * bz, tx * reference[0] + ty * reference[1] + tz * reference[2] );
		if ( angle < 0 )
			angle += MathF.Tau;

		var angleBits = (uint)((int)MathF.Round( angle / MathF.Tau * 2047f ) % 2047);
		var sign = tw < 0 ? 0u : 1u;

		return sign | (angleBits << 1) | (xBits << 12) | (yBits << 22);
	}

	/// <summary>
	/// Packs a lightmap coordinate as two 16 bit fractions, or <see cref="NoLightmap"/> for a negative one.
	/// </summary>
	public static uint PackLightmapUV( float u, float v )
	{
		if ( u < 0 || v < 0 )
			return NoLightmap;

		var x = (uint)Math.Clamp( (int)MathF.Round( u * 65535f ), 0, 65535 );
		var y = (uint)Math.Clamp( (int)MathF.Round( v * 65535f ), 0, 65535 );
		return x | (y << 16);
	}

	/// <summary>
	/// A 0 to 1 value as an 8 bit fraction.
	/// </summary>
	public static byte PackUnorm8( float value ) => (byte)Math.Clamp( (int)MathF.Round( value * 255f ), 0, 255 );
}
