using System;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

internal sealed partial class Zstd
{
	private enum SequenceKind
	{
		LiteralLength,
		Offset,
		MatchLength,
	}

	private static readonly int[] LiteralLengthBaselines =
	[
		0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 28, 32, 40,
		48, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536,
	];

	private static readonly byte[] LiteralLengthExtraBits =
	[
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1,
		1, 1, 2, 2, 3, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
	];

	private static readonly int[] MatchLengthBaselines =
	[
		3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
		31, 32, 33, 34, 35, 37, 39, 41, 43, 47, 51, 59, 67, 83, 99, 131, 259, 515, 1027, 2051, 4099, 8195, 16387, 32771, 65539,
	];

	private static readonly byte[] MatchLengthExtraBits =
	[
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 4, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
	];

	private static readonly FseTable DefaultLiteralLengths = FseTable.Build(
	[
		4, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 1, 1, 1, 1, 1, -1, -1, -1, -1,
	], 6 );

	private static readonly FseTable DefaultOffsets = FseTable.Build(
	[
		1, 1, 1, 1, 1, 1, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1,
	], 5 );

	private static readonly FseTable DefaultMatchLengths = FseTable.Build(
	[
		1, 4, 3, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
		1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1, -1, -1,
	], 6 );

	private static FseTable ReadSequenceTable( ReadOnlySpan<byte> section, ref int position, int mode, SequenceKind kind, FseTable previous )
	{
		switch ( mode )
		{
			case 0:
				return kind switch
				{
					SequenceKind.LiteralLength => DefaultLiteralLengths,
					SequenceKind.Offset => DefaultOffsets,
					_ => DefaultMatchLengths,
				};

			case 1:
				return FseTable.Rle( section[position++] );

			case 2:
			{
				var maxAccuracyLog = kind == SequenceKind.Offset ? 8 : 9;
				var table = FseTable.Read( section[position..], maxAccuracyLog, out var size );
				position += size;
				return table;
			}

			default:
				return previous ?? throw new InvalidDataException( "Zstd repeat mode has no previous table." );
		}
	}

	/// <summary>
	/// A finite state entropy decoding table.
	/// </summary>
	private sealed class FseTable
	{
		public int AccuracyLog { get; private init; }
		public byte[] Symbols { get; private init; }
		public byte[] NumBits { get; private init; }
		public ushort[] NewStateBase { get; private init; }

		public int NextState( int state, ref BackwardBitReader reader )
		{
			return NewStateBase[state] + (int)reader.Read( NumBits[state] );
		}

		public static FseTable Rle( byte symbol )
		{
			return new FseTable { AccuracyLog = 0, Symbols = [symbol], NumBits = [0], NewStateBase = [0] };
		}

		/// <summary>
		/// Reads a table description, returning the bytes it used through <paramref name="size"/>.
		/// </summary>
		public static FseTable Read( ReadOnlySpan<byte> source, int maxAccuracyLog, out int size )
		{
			var reader = new ForwardBitReader( source );
			var accuracyLog = reader.Read( 4 ) + 5;

			if ( accuracyLog > maxAccuracyLog )
				throw new InvalidDataException( "Zstd FSE accuracy log is too large." );

			Span<short> counts = stackalloc short[256];
			var remaining = 1 << accuracyLog;
			var symbol = 0;

			while ( remaining > 0 && symbol < counts.Length )
			{
				var bits = HighestBit( (ulong)remaining + 1 ) + 1;
				var value = reader.Read( bits );

				// Small values are written with one bit less.
				var lowerMask = (1 << (bits - 1)) - 1;
				var threshold = (1 << bits) - 1 - (remaining + 1);

				if ( (value & lowerMask) < threshold )
				{
					reader.Rewind( 1 );
					value &= lowerMask;
				}
				else if ( value > lowerMask )
				{
					value -= threshold;
				}

				var probability = value - 1;
				remaining -= Math.Abs( probability );
				counts[symbol++] = (short)probability;

				if ( probability != 0 )
					continue;

				// A zero probability is followed by 2 bit repeat flags for further zeros.
				while ( true )
				{
					var repeat = reader.Read( 2 );

					for ( var i = 0; i < repeat && symbol < counts.Length; i++ )
						counts[symbol++] = 0;

					if ( repeat != 3 )
						break;
				}
			}

			if ( remaining != 0 || symbol >= counts.Length )
				throw new InvalidDataException( "Zstd FSE table description is corrupt." );

			size = reader.BytesRead;
			if ( size > source.Length )
				throw new InvalidDataException( "Zstd FSE table description is truncated." );

			return Build( counts[..symbol], accuracyLog );
		}

		public static FseTable Build( ReadOnlySpan<short> counts, int accuracyLog )
		{
			var size = 1 << accuracyLog;
			var symbols = new byte[size];
			var numBits = new byte[size];
			var newStateBase = new ushort[size];

			Span<ushort> nextState = stackalloc ushort[counts.Length];

			// "Less than 1" probabilities each take one cell at the end of the table.
			var highThreshold = size;
			for ( var s = 0; s < counts.Length; s++ )
			{
				if ( counts[s] != -1 )
					continue;

				symbols[--highThreshold] = (byte)s;
				nextState[s] = 1;
			}

			var step = (size >> 1) + (size >> 3) + 3;
			var mask = size - 1;
			var position = 0;

			for ( var s = 0; s < counts.Length; s++ )
			{
				if ( counts[s] <= 0 )
					continue;

				nextState[s] = (ushort)counts[s];

				for ( var i = 0; i < counts[s]; i++ )
				{
					symbols[position] = (byte)s;

					do
					{
						position = (position + step) & mask;
					}
					while ( position >= highThreshold );
				}
			}

			if ( position != 0 )
				throw new InvalidDataException( "Zstd FSE distribution does not fill its table." );

			for ( var i = 0; i < size; i++ )
			{
				var state = nextState[symbols[i]]++;
				numBits[i] = (byte)(accuracyLog - HighestBit( state ));
				newStateBase[i] = (ushort)((state << numBits[i]) - size);
			}

			return new FseTable { AccuracyLog = accuracyLog, Symbols = symbols, NumBits = numBits, NewStateBase = newStateBase };
		}

		/// <summary>
		/// Decodes two interleaved FSE states until the stream runs out, as Huffman weights are stored.
		/// </summary>
		public int DecodeInterleaved( ReadOnlySpan<byte> source, Span<byte> output )
		{
			var reader = new BackwardBitReader( source );
			var state1 = (int)reader.Read( AccuracyLog );
			var state2 = (int)reader.Read( AccuracyLog );
			var count = 0;

			while ( true )
			{
				if ( count + 2 > output.Length )
					throw new InvalidDataException( "Zstd Huffman weights overflow." );

				output[count++] = Symbols[state1];
				state1 = NextState( state1, ref reader );

				if ( reader.Offset < 0 )
				{
					output[count++] = Symbols[state2];
					break;
				}

				output[count++] = Symbols[state2];
				state2 = NextState( state2, ref reader );

				if ( reader.Offset < 0 )
				{
					output[count++] = Symbols[state1];
					break;
				}
			}

			return count;
		}
	}

	/// <summary>
	/// A Huffman decoding table for compressed literals.
	/// </summary>
	private sealed class HuffmanTable
	{
		private const int MaxBits = 16;

		private int _maxBits;
		private byte[] _symbols;
		private byte[] _numBits;

		/// <summary>
		/// Reads a Huffman tree description, returning the bytes it used through <paramref name="size"/>.
		/// </summary>
		public static HuffmanTable Read( ReadOnlySpan<byte> source, out int size )
		{
			var header = source[0];
			Span<byte> weights = stackalloc byte[256];
			int weightCount;

			if ( header >= 128 )
			{
				// Weights are stored directly as 4 bit fields, high nibble first.
				weightCount = header - 127;
				size = 1 + (weightCount + 1) / 2;

				for ( var i = 0; i < weightCount; i++ )
				{
					var packed = source[1 + i / 2];
					weights[i] = (byte)(i % 2 == 0 ? packed >> 4 : packed & 15);
				}
			}
			else
			{
				size = 1 + header;

				var compressed = source.Slice( 1, header );
				var fse = FseTable.Read( compressed, 7, out var tableSize );
				weightCount = fse.DecodeInterleaved( compressed[tableSize..], weights );
			}

			return FromWeights( weights[..weightCount] );
		}

		private static HuffmanTable FromWeights( ReadOnlySpan<byte> weights )
		{
			if ( weights.Length > 255 )
				throw new InvalidDataException( "Zstd Huffman table has too many symbols." );

			ulong weightSum = 0;
			foreach ( var weight in weights )
			{
				if ( weight > MaxBits )
					throw new InvalidDataException( "Zstd Huffman weight is too large." );

				if ( weight > 0 )
					weightSum += 1UL << (weight - 1);
			}

			if ( weightSum == 0 )
				throw new InvalidDataException( "Zstd Huffman table has no weights." );

			// The last weight is implied by whatever completes the next power of two.
			var maxBits = HighestBit( weightSum ) + 1;
			var leftOver = (1UL << maxBits) - weightSum;

			if ( (leftOver & (leftOver - 1)) != 0 )
				throw new InvalidDataException( "Zstd Huffman weights are corrupt." );

			var lastWeight = HighestBit( leftOver ) + 1;
			var symbolCount = weights.Length + 1;

			Span<byte> bits = stackalloc byte[symbolCount];
			for ( var i = 0; i < weights.Length; i++ )
				bits[i] = (byte)(weights[i] > 0 ? maxBits + 1 - weights[i] : 0);

			bits[weights.Length] = (byte)(maxBits + 1 - lastWeight);

			if ( maxBits > MaxBits )
				throw new InvalidDataException( "Zstd Huffman table is too deep." );

			Span<int> rankCount = stackalloc int[MaxBits + 2];
			foreach ( var b in bits )
				rankCount[b]++;

			var tableSize = 1 << maxBits;
			var table = new HuffmanTable
			{
				_maxBits = maxBits,
				_symbols = new byte[tableSize],
				_numBits = new byte[tableSize],
			};

			// Codes are assigned starting with the longest, so each length owns one contiguous range.
			Span<int> rankStart = stackalloc int[MaxBits + 2];
			rankStart[maxBits] = 0;

			for ( var length = maxBits; length >= 1; length-- )
			{
				rankStart[length - 1] = rankStart[length] + rankCount[length] * (1 << (maxBits - length));
				table._numBits.AsSpan( rankStart[length], rankStart[length - 1] - rankStart[length] ).Fill( (byte)length );
			}

			if ( rankStart[0] != tableSize )
				throw new InvalidDataException( "Zstd Huffman code lengths do not fill the table." );

			for ( var symbol = 0; symbol < symbolCount; symbol++ )
			{
				var length = bits[symbol];
				if ( length == 0 )
					continue;

				var span = 1 << (maxBits - length);
				table._symbols.AsSpan( rankStart[length], span ).Fill( (byte)symbol );
				rankStart[length] += span;
			}

			return table;
		}

		public void DecodeStream( ReadOnlySpan<byte> source, Span<byte> output )
		{
			var reader = new BackwardBitReader( source );
			var mask = (1 << _maxBits) - 1;
			var state = (int)reader.Read( _maxBits );

			for ( var i = 0; i < output.Length; i++ )
			{
				output[i] = _symbols[state];

				var bits = _numBits[state];
				state = ((state << bits) + (int)reader.Read( bits )) & mask;
			}

			if ( reader.Offset != -_maxBits )
				throw new InvalidDataException( "Zstd Huffman stream was not fully consumed." );
		}
	}
}
