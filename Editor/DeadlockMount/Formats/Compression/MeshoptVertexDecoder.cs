using System;
using System.IO;
using System.Numerics;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Decodes vertex buffers compressed with meshoptimizer's vertex codec (versions 0 and 1).
/// Ported from ValveResourceFormat's port of meshoptimizer, both MIT licensed.
/// </summary>
internal static class MeshoptVertexDecoder
{
	private const byte VertexHeader = 0xA0;
	private const int MaxVersion = 1;

	private const int VertexBlockSizeBytes = 8192;
	private const int VertexBlockMaxSize = 256;
	private const int ByteGroupSize = 16;
	private const int ByteGroupDecodeLimit = 24;
	private const int TailMinSizeV0 = 32;
	private const int TailMinSizeV1 = 24;

	private static readonly int[] BitsV0 = [0, 2, 4, 8];
	private static readonly int[] BitsV1 = [0, 1, 2, 4, 8];

	public static byte[] Decode( int vertexCount, int vertexSize, ReadOnlySpan<byte> buffer )
	{
		if ( vertexSize <= 0 || vertexSize > 256 || vertexSize % 4 != 0 )
			throw new InvalidDataException( $"Meshopt vertex size {vertexSize} is invalid." );

		if ( buffer.Length < 1 || (buffer[0] & 0xF0) != VertexHeader )
			throw new InvalidDataException( "Meshopt vertex buffer header is invalid." );

		var version = buffer[0] & 0x0F;
		if ( version > MaxVersion )
			throw new InvalidDataException( $"Meshopt vertex codec version {version} is not supported." );

		buffer = buffer[1..];

		var tailSize = vertexSize + (version == 0 ? 0 : vertexSize / 4);
		var tailSizePadded = Math.Max( tailSize, version == 0 ? TailMinSizeV0 : TailMinSizeV1 );

		if ( buffer.Length < tailSizePadded )
			throw new InvalidDataException( "Meshopt vertex buffer is too small for its tail." );

		var result = new byte[vertexCount * vertexSize];

		var lastVertex = buffer.Slice( buffer.Length - tailSize, vertexSize ).ToArray();

		var channels = version == 0 ? default : buffer.Slice( buffer.Length - tailSize + vertexSize, vertexSize / 4 );
		var blockSize = GetVertexBlockSize( vertexSize );

		var scratch = new byte[VertexBlockMaxSize * 4];
		var transposed = new byte[VertexBlockSizeBytes];

		for ( var offset = 0; offset < vertexCount; offset += blockSize )
		{
			var count = Math.Min( blockSize, vertexCount - offset );
			buffer = DecodeVertexBlock( buffer, result.AsSpan( offset * vertexSize ), count, vertexSize, lastVertex, channels, version, scratch, transposed );
		}

		if ( buffer.Length != tailSizePadded )
			throw new InvalidDataException( "Meshopt vertex buffer has an unexpected tail size." );

		return result;
	}

	private static int GetVertexBlockSize( int vertexSize )
	{
		var result = (VertexBlockSizeBytes / vertexSize) & ~(ByteGroupSize - 1);
		return Math.Min( result, VertexBlockMaxSize );
	}

	private static byte Unzigzag8( byte v ) => (byte)((0 - (v & 1)) ^ (v >> 1));

	private static ushort Unzigzag16( ushort v ) => (ushort)((0 - (v & 1)) ^ (v >> 1));

	private static ReadOnlySpan<byte> DecodeBytesGroup( ReadOnlySpan<byte> data, Span<byte> buffer, int bits )
	{
		switch ( bits )
		{
			case 0:
				buffer[..ByteGroupSize].Clear();
				return data;

			case 8:
				data[..ByteGroupSize].CopyTo( buffer );
				return data[ByteGroupSize..];

			case 1:
			case 2:
			case 4:
			{
				// Each value takes `bits` bits; the all-ones value means "read a full byte from the stream instead".
				var headerBytes = ByteGroupSize * bits / 8;
				var dataOffset = headerBytes;
				var perByte = 8 / bits;
				var sentinel = (1 << bits) - 1;

				for ( var i = 0; i < ByteGroupSize; i++ )
				{
					int header = data[i / perByte];

					// 1 bit groups store their values in reverse bit order within each byte.
					var shift = bits == 1 ? i % 8 : 8 - bits * (i % perByte + 1);
					var value = (header >> shift) & sentinel;

					if ( value == sentinel )
						buffer[i] = data[dataOffset++];
					else
						buffer[i] = (byte)value;
				}

				return data[dataOffset..];
			}

			default:
				throw new InvalidDataException( "Meshopt byte group has an unexpected bit length." );
		}
	}

	private static ReadOnlySpan<byte> DecodeBytes( ReadOnlySpan<byte> data, Span<byte> buffer, ReadOnlySpan<int> bits )
	{
		// One header byte holds the bit widths of four groups.
		var headerSize = (buffer.Length / ByteGroupSize + 3) / 4;

		if ( data.Length < headerSize )
			throw new InvalidDataException( "Meshopt byte group header is truncated." );

		var header = data[..headerSize];
		data = data[headerSize..];

		for ( var i = 0; i < buffer.Length; i += ByteGroupSize )
		{
			if ( data.Length < ByteGroupDecodeLimit )
				throw new InvalidDataException( "Meshopt byte group is truncated." );

			var group = i / ByteGroupSize;
			var bitsIndex = (header[group / 4] >> (group % 4 * 2)) & 3;

			data = DecodeBytesGroup( data, buffer[i..], bits[bitsIndex] );
		}

		return data;
	}

	private static void DecodeDeltas( int size, ReadOnlySpan<byte> buffer, Span<byte> transposed, int vertexCount, int vertexSize, ReadOnlySpan<byte> lastVertex, int rotation )
	{
		for ( var k = 0; k < 4; k += size )
		{
			var vertexOffset = k;

			uint previous = lastVertex[0];
			for ( var j = 1; j < size; ++j )
				previous |= (uint)lastVertex[j] << (8 * j);

			for ( var i = 0; i < vertexCount; i++ )
			{
				uint v = buffer[i];
				for ( var j = 1; j < size; ++j )
					v |= (uint)buffer[i + vertexCount * j] << (8 * j);

				v = size switch
				{
					1 => (uint)(Unzigzag8( (byte)v ) + previous),
					2 => (uint)(Unzigzag16( (ushort)v ) + previous),
					_ => BitOperations.RotateLeft( v, rotation ) ^ previous,
				};

				for ( var j = 0; j < size; ++j )
					transposed[vertexOffset + j] = (byte)(v >> (j * 8));

				previous = v;
				vertexOffset += vertexSize;
			}

			buffer = buffer[(vertexCount * size)..];
			lastVertex = lastVertex[size..];
		}
	}

	private static ReadOnlySpan<byte> DecodeVertexBlock( ReadOnlySpan<byte> data, Span<byte> vertexData, int vertexCount, int vertexSize,
		Span<byte> lastVertex, ReadOnlySpan<byte> channels, int version, byte[] scratch, byte[] transposed )
	{
		var vertexCountAligned = (vertexCount + ByteGroupSize - 1) & ~(ByteGroupSize - 1);
		var controlSize = version == 0 ? 0 : vertexSize / 4;

		if ( data.Length < controlSize )
			throw new InvalidDataException( "Meshopt vertex block control data is truncated." );

		var control = data[..controlSize];
		data = data[controlSize..];

		var buffer = scratch.AsSpan();

		for ( var k = 0; k < vertexSize; k += 4 )
		{
			var controlByte = version == 0 ? 0 : control[k / 4];

			for ( var j = 0; j < 4; ++j )
			{
				var mode = (controlByte >> (j * 2)) & 3;

				if ( mode == 3 )
				{
					// Literal bytes.
					if ( data.Length < vertexCount )
						throw new InvalidDataException( "Meshopt vertex block literal data is truncated." );

					data[..vertexCount].CopyTo( buffer.Slice( j * vertexCount, vertexCount ) );
					data = data[vertexCount..];
				}
				else if ( mode == 2 )
				{
					// All zero.
					buffer.Slice( j * vertexCount, vertexCount ).Clear();
				}
				else
				{
					var bits = version == 0 ? BitsV0 : BitsV1.AsSpan( mode );
					data = DecodeBytes( data, buffer.Slice( j * vertexCount, vertexCountAligned ), bits );
				}
			}

			var channel = version == 0 ? 0 : channels[k / 4];

			switch ( channel & 3 )
			{
				case 0:
					DecodeDeltas( 1, buffer, transposed.AsSpan( k ), vertexCount, vertexSize, lastVertex[k..], 0 );
					break;

				case 1:
					DecodeDeltas( 2, buffer, transposed.AsSpan( k ), vertexCount, vertexSize, lastVertex[k..], 0 );
					break;

				case 2:
					DecodeDeltas( 4, buffer, transposed.AsSpan( k ), vertexCount, vertexSize, lastVertex[k..], (32 - (channel >> 4)) & 31 );
					break;

				default:
					throw new InvalidDataException( "Meshopt vertex channel is invalid." );
			}
		}

		transposed.AsSpan( 0, vertexCount * vertexSize ).CopyTo( vertexData );
		transposed.AsSpan( vertexSize * (vertexCount - 1), vertexSize ).CopyTo( lastVertex );

		return data;
	}
}
