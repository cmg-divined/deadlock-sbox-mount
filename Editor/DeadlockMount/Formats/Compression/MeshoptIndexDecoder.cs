using System;
using System.Buffers.Binary;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Decodes triangle index buffers compressed with meshoptimizer's index codec (versions 0 and 1).
/// Ported from ValveResourceFormat's port of meshoptimizer, both MIT licensed.
/// </summary>
internal static class MeshoptIndexDecoder
{
	private const byte IndexHeader = 0xE0;
	private const int MaxVersion = 1;

	public static byte[] Decode( int indexCount, int indexSize, ReadOnlySpan<byte> buffer )
	{
		if ( indexCount % 3 != 0 )
			throw new InvalidDataException( "Meshopt index count is not a multiple of 3." );

		if ( indexSize is not (2 or 4) )
			throw new InvalidDataException( $"Meshopt index size {indexSize} is invalid." );

		var dataOffset = 1 + indexCount / 3;

		// Header, one code byte per triangle and the 16 byte auxiliary code table.
		if ( buffer.Length < dataOffset + 16 )
			throw new InvalidDataException( "Meshopt index buffer is too short." );

		if ( (buffer[0] & 0xF0) != IndexHeader )
			throw new InvalidDataException( "Meshopt index buffer header is invalid." );

		var version = buffer[0] & 0x0F;
		if ( version > MaxVersion )
			throw new InvalidDataException( $"Meshopt index codec version {version} is not supported." );

		Span<(uint A, uint B)> edgeFifo = stackalloc (uint, uint)[16];
		Span<uint> vertexFifo = stackalloc uint[16];
		var edgeFifoOffset = 0;
		var vertexFifoOffset = 0;

		var next = 0u;
		var last = 0u;
		var fecMax = version >= 1 ? 13 : 15;

		var codes = buffer[1..dataOffset];
		var data = buffer[dataOffset..];
		var position = 0;

		// Each triangle reads at most 16 bytes, so the auxiliary table at the end is never overread.
		var dataSafeEnd = data.Length - 16;
		var codeAuxTable = data[dataSafeEnd..];

		var result = new byte[indexCount * indexSize];
		var output = 0;

		foreach ( var code in codes )
		{
			if ( code < 0xF0 )
			{
				var fe = code >> 4;
				var (a, b) = edgeFifo[(edgeFifoOffset - 1 - fe) & 15];
				var fec = code & 15;
				uint c;

				if ( fec < fecMax )
				{
					var cf = vertexFifo[(vertexFifoOffset - 1 - fec) & 15];
					c = fec == 0 ? next : cf;

					if ( fec == 0 )
						next++;

					PushVertex( vertexFifo, ref vertexFifoOffset, c, fec == 0 );
				}
				else
				{
					if ( position > dataSafeEnd )
						throw new InvalidDataException( "Meshopt index data is truncated." );

					// fec 13 and 14 encode a delta of -1 and +1 from the last free index.
					last = c = fec != 15 ? last + (uint)(fec * 2 - 27) : DecodeIndex( data, last, ref position );
					PushVertex( vertexFifo, ref vertexFifoOffset, c );
				}

				PushEdge( edgeFifo, ref edgeFifoOffset, c, b );
				PushEdge( edgeFifo, ref edgeFifoOffset, a, c );

				WriteTriangle( result, ref output, indexSize, a, b, c );
			}
			else if ( code < 0xFE )
			{
				var codeAux = codeAuxTable[code & 15];
				var feb = codeAux >> 4;
				var fec = codeAux & 15;

				var a = next++;

				var bf = vertexFifo[(vertexFifoOffset - feb) & 15];
				var b = feb == 0 ? next : bf;
				if ( feb == 0 )
					next++;

				var cf = vertexFifo[(vertexFifoOffset - fec) & 15];
				var c = fec == 0 ? next : cf;
				if ( fec == 0 )
					next++;

				WriteTriangle( result, ref output, indexSize, a, b, c );

				PushVertex( vertexFifo, ref vertexFifoOffset, a );
				PushVertex( vertexFifo, ref vertexFifoOffset, b, feb == 0 );
				PushVertex( vertexFifo, ref vertexFifoOffset, c, fec == 0 );

				PushEdge( edgeFifo, ref edgeFifoOffset, b, a );
				PushEdge( edgeFifo, ref edgeFifoOffset, c, b );
				PushEdge( edgeFifo, ref edgeFifoOffset, a, c );
			}
			else
			{
				if ( position > dataSafeEnd )
					throw new InvalidDataException( "Meshopt index data is truncated." );

				var codeAux = data[position++];
				var fea = code == 0xFE ? 0 : 15;
				var feb = codeAux >> 4;
				var fec = codeAux & 15;

				// A zero aux code outside the table resets the vertex counter.
				if ( codeAux == 0 )
					next = 0;

				var a = fea == 0 ? next++ : 0;
				var b = feb == 0 ? next++ : vertexFifo[(vertexFifoOffset - feb) & 15];
				var c = fec == 0 ? next++ : vertexFifo[(vertexFifoOffset - fec) & 15];

				if ( fea == 15 )
					last = a = DecodeIndex( data, last, ref position );

				if ( feb == 15 )
					last = b = DecodeIndex( data, last, ref position );

				if ( fec == 15 )
					last = c = DecodeIndex( data, last, ref position );

				WriteTriangle( result, ref output, indexSize, a, b, c );

				PushVertex( vertexFifo, ref vertexFifoOffset, a );
				PushVertex( vertexFifo, ref vertexFifoOffset, b, feb == 0 || feb == 15 );
				PushVertex( vertexFifo, ref vertexFifoOffset, c, fec == 0 || fec == 15 );

				PushEdge( edgeFifo, ref edgeFifoOffset, b, a );
				PushEdge( edgeFifo, ref edgeFifoOffset, c, b );
				PushEdge( edgeFifo, ref edgeFifoOffset, a, c );
			}
		}

		if ( position != dataSafeEnd )
			throw new InvalidDataException( "Meshopt index data is malformed." );

		return result;
	}

	private static void PushEdge( Span<(uint A, uint B)> fifo, ref int offset, uint a, uint b )
	{
		fifo[offset] = (a, b);
		offset = (offset + 1) & 15;
	}

	private static void PushVertex( Span<uint> fifo, ref int offset, uint v, bool advance = true )
	{
		fifo[offset] = v;
		offset = (offset + (advance ? 1 : 0)) & 15;
	}

	private static uint DecodeIndex( ReadOnlySpan<byte> data, uint last, ref int position )
	{
		var v = DecodeVByte( data, ref position );
		var delta = (uint)((v >> 1) ^ -(v & 1));
		return last + delta;
	}

	private static uint DecodeVByte( ReadOnlySpan<byte> data, ref int position )
	{
		uint lead = data[position++];
		if ( lead < 128 )
			return lead;

		var result = lead & 127;
		var shift = 7;

		for ( var i = 0; i < 4; i++ )
		{
			uint group = data[position++];
			result |= (group & 127) << shift;
			shift += 7;

			if ( group < 128 )
				break;
		}

		return result;
	}

	private static void WriteTriangle( byte[] destination, ref int offset, int indexSize, uint a, uint b, uint c )
	{
		var span = destination.AsSpan( offset );

		if ( indexSize == 2 )
		{
			BinaryPrimitives.WriteUInt16LittleEndian( span, (ushort)a );
			BinaryPrimitives.WriteUInt16LittleEndian( span[2..], (ushort)b );
			BinaryPrimitives.WriteUInt16LittleEndian( span[4..], (ushort)c );
		}
		else
		{
			BinaryPrimitives.WriteUInt32LittleEndian( span, a );
			BinaryPrimitives.WriteUInt32LittleEndian( span[4..], b );
			BinaryPrimitives.WriteUInt32LittleEndian( span[8..], c );
		}

		offset += indexSize * 3;
	}
}
