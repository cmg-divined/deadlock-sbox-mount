using System;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Managed LZ4 block decoder.
/// </summary>
internal static class Lz4
{
	private const int MinMatch = 4;

	/// <summary>
	/// Decodes one LZ4 block into <paramref name="output"/>, writing from <paramref name="outputOffset"/>.
	/// Matches may reach back past <paramref name="outputOffset"/>, which is how chained blocks use the
	/// data decoded before them as a dictionary.
	/// </summary>
	/// <returns>The number of bytes written.</returns>
	public static int Decode( ReadOnlySpan<byte> input, Span<byte> output, int outputOffset = 0 )
	{
		var ip = 0;
		var op = outputOffset;

		while ( ip < input.Length )
		{
			var token = input[ip++];

			var literalLength = token >> 4;
			if ( literalLength == 15 )
				literalLength += ReadLength( input, ref ip );

			if ( literalLength > input.Length - ip || literalLength > output.Length - op )
				throw new InvalidDataException( "LZ4 literal run is out of bounds." );

			input.Slice( ip, literalLength ).CopyTo( output[op..] );
			ip += literalLength;
			op += literalLength;

			// The last sequence of a block is literals only.
			if ( ip == input.Length )
				break;

			if ( ip + 2 > input.Length )
				throw new InvalidDataException( "LZ4 match offset is truncated." );

			var offset = input[ip] | (input[ip + 1] << 8);
			ip += 2;

			var matchLength = token & 15;
			if ( matchLength == 15 )
				matchLength += ReadLength( input, ref ip );

			matchLength += MinMatch;

			var source = op - offset;
			if ( offset == 0 || source < 0 || matchLength > output.Length - op )
				throw new InvalidDataException( "LZ4 match is out of bounds." );

			if ( offset >= matchLength )
			{
				output.Slice( source, matchLength ).CopyTo( output[op..] );
			}
			else
			{
				// Overlapping copies repeat the last `offset` bytes, so go byte by byte.
				for ( var i = 0; i < matchLength; i++ )
					output[op + i] = output[source + i];
			}

			op += matchLength;
		}

		return op - outputOffset;
	}

	private static int ReadLength( ReadOnlySpan<byte> input, ref int ip )
	{
		var length = 0;
		byte value;

		do
		{
			if ( ip >= input.Length )
				throw new InvalidDataException( "LZ4 length is truncated." );

			value = input[ip++];
			length += value;
		}
		while ( value == 255 );

		return length;
	}
}
