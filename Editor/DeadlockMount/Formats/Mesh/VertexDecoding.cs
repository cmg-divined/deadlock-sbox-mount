using System;
using System.Buffers.Binary;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Decodes the vertex attributes that need more than a format conversion: packed normal and
/// tangent frames, and bone indices and weights. Follows ValveResourceFormat's interpretation.
/// </summary>
internal static class VertexDecoding
{
	/// <summary>
	/// Decodes the normal of a vertex, and its tangent when the format packs one alongside.
	/// </summary>
	/// <param name="normal">Receives x, y, z.</param>
	/// <param name="tangent">Receives x, y, z and the bitangent sign in w.</param>
	/// <returns>True if a tangent was decoded.</returns>
	public static bool ReadNormal( MeshBuffer buffer, InputLayoutField field, int vertex, Span<float> normal, Span<float> tangent )
	{
		var data = buffer.GetAttributeBytes( field, vertex );

		switch ( field.Format )
		{
			case DxgiFormat.R32_UINT:
				UnpackTangentFrame( BinaryPrimitives.ReadUInt32LittleEndian( data ), normal, tangent );
				return true;

			case DxgiFormat.R8G8B8A8_UNORM:
				UnpackNormal( data[0], data[1], normal );
				UnpackNormal( data[2], data[3], tangent );
				tangent[3] = data[3] < 128 ? -1 : 1;
				return true;

			default:
				Span<float> values = stackalloc float[4];
				buffer.Read( field, vertex, values );
				values[..3].CopyTo( normal );
				return false;
		}
	}

	/// <summary>
	/// Unpacks a 32 bit tangent frame: an octahedral normal in 20 bits, the tangent's rotation
	/// around it in 11 bits and the bitangent sign in the lowest bit.
	/// </summary>
	internal static void UnpackTangentFrame( uint packed, Span<float> normal, Span<float> tangent )
	{
		var sign = packed & 1;
		float angleBits = (packed >> 1) & 0x7FF;
		float xBits = (packed >> 12) & 0x3FF;
		float yBits = (packed >> 22) & 0x3FF;

		var x = xBits / 1023f * 2f - 1f;
		var y = yBits / 1023f * 2f - 1f;
		var z = 1f - MathF.Abs( x ) - MathF.Abs( y );

		// Points below the octahedron's equator fold back over it.
		var fold = Math.Clamp( -z, 0, 1 );
		x += x >= 0 ? -fold : fold;
		y += y >= 0 ? -fold : fold;

		var length = MathF.Sqrt( x * x + y * y + z * z );
		x /= length;
		y /= length;
		z /= length;

		normal[0] = x;
		normal[1] = y;
		normal[2] = z;

		// A tangent derived from the normal alone, then rotated into place by the stored angle.
		var tangentSign = z >= 0 ? 1f : -1f;
		var reciprocal = 1f / (tangentSign + z);
		var tx = -tangentSign * (x * x) * reciprocal + 1f;
		var ty = -tangentSign * (x * y) * reciprocal;
		var tz = -tangentSign * x;

		var angle = angleBits / 2047f * MathF.Tau;
		var cos = MathF.Cos( angle );
		var sin = MathF.Sin( angle );

		// cross( normal, tangent )
		var cx = y * tz - z * ty;
		var cy = z * tx - x * tz;
		var cz = x * ty - y * tx;

		tangent[0] = tx * cos + cx * sin;
		tangent[1] = ty * cos + cy * sin;
		tangent[2] = tz * cos + cz * sin;
		tangent[3] = sign == 0 ? -1 : 1;
	}

	/// <summary>
	/// Unpacks a normal stored as two bytes.
	/// </summary>
	private static void UnpackNormal( float x, float y, Span<float> output )
	{
		x -= 128f;
		y -= 128f;

		var zSignBit = x < 0 ? 1f : 0f;
		var tSignBit = y < 0 ? 1f : 0f;
		var zSign = -(2 * zSignBit - 1);
		var tSign = -(2 * tSignBit - 1);

		x = x * zSign - zSignBit - 64;
		y = y * tSign - tSignBit - 64;

		var xSignBit = x < 0 ? 1f : 0f;
		var ySignBit = y < 0 ? 1f : 0f;
		var xSign = -(2 * xSignBit - 1);
		var ySign = -(2 * ySignBit - 1);

		x = (x * xSign - xSignBit) / 63f;
		y = (y * ySign - ySignBit) / 63f;
		var z = 1f - x - y;

		var inverseLength = 1f / MathF.Sqrt( x * x + y * y + z * z );
		output[0] = x * inverseLength * xSign;
		output[1] = y * inverseLength * ySign;
		output[2] = z * inverseLength * zSign;
	}

	/// <summary>
	/// Reads the bone influences of a vertex as mesh-local bone indices and weights.
	/// </summary>
	/// <param name="indices">Up to 8 bone indices.</param>
	/// <param name="weights">Up to 8 weights, summing to roughly 1.</param>
	/// <returns>The number of influences written.</returns>
	public static int ReadBlend( MeshBuffer buffer, InputLayoutField indicesField, InputLayoutField? weightsField, int boneWeightCount, int vertex, Span<int> indices, Span<float> weights )
	{
		var data = buffer.GetAttributeBytes( indicesField, vertex );
		int count;

		switch ( indicesField.Format )
		{
			case DxgiFormat.R8G8B8A8_UINT:
			case DxgiFormat.R16G16B16A16_UINT:
				// The 16 bit variant packs 8 byte indices.
				count = indicesField.Format == DxgiFormat.R8G8B8A8_UINT ? 4 : 8;
				for ( var i = 0; i < count; i++ )
					indices[i] = data[i];
				break;

			case DxgiFormat.R16G16B16A16_SINT:
			case DxgiFormat.R32G32B32A32_SINT:
				// The 32 bit variant packs 8 16 bit indices.
				count = indicesField.Format == DxgiFormat.R16G16B16A16_SINT ? 4 : 8;
				for ( var i = 0; i < count; i++ )
					indices[i] = BinaryPrimitives.ReadUInt16LittleEndian( data[(i * 2)..] );
				break;

			case DxgiFormat.R16G16_SINT:
				count = 2;
				indices[0] = BinaryPrimitives.ReadUInt16LittleEndian( data );
				indices[1] = BinaryPrimitives.ReadUInt16LittleEndian( data[2..] );
				break;

			default:
				throw new NotSupportedException( $"Blend indices use unsupported format {indicesField.Format}." );
		}

		if ( weightsField is not { } field )
		{
			// Without weights, the declared number of bones share the vertex equally.
			var shared = Math.Clamp( boneWeightCount, 1, count );
			for ( var i = 0; i < count; i++ )
				weights[i] = i < shared ? 1f / shared : 0f;

			return count;
		}

		var weightData = buffer.GetAttributeBytes( field, vertex );

		switch ( field.Format )
		{
			case DxgiFormat.R8G8B8A8_UNORM:
			case DxgiFormat.R16G16B16A16_UNORM:
			{
				// The 16 bit variant packs 8 byte weights.
				var weightCount = field.Format == DxgiFormat.R8G8B8A8_UNORM ? 4 : 8;
				for ( var i = 0; i < count; i++ )
					weights[i] = i < weightCount ? weightData[i] / 255f : 0f;
				break;
			}

			case DxgiFormat.R16G16_UNORM:
				for ( var i = 0; i < count; i++ )
					weights[i] = i < 2 ? BinaryPrimitives.ReadUInt16LittleEndian( weightData[(i * 2)..] ) / 65535f : 0f;
				break;

			default:
				throw new NotSupportedException( $"Blend weights use unsupported format {field.Format}." );
		}

		return count;
	}
}
