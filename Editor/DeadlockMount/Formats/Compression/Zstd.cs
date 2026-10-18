using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Managed Zstandard decoder (RFC 8878). Decodes every frame in the input; dictionaries are not supported.
/// </summary>
internal sealed partial class Zstd
{
	private const uint FrameMagic = 0xFD2FB528;
	private const uint SkippableMagicMask = 0xFFFFFFF0;
	private const uint SkippableMagic = 0x184D2A50;
	private const int MaxBlockSize = 128 * 1024;

	private byte[] _output;
	private int _length;
	private int _frameStart;

	private readonly byte[] _literals = new byte[MaxBlockSize];
	private HuffmanTable _huffman;
	private FseTable _literalLengths;
	private FseTable _offsets;
	private FseTable _matchLengths;
	private readonly uint[] _repeatOffsets = new uint[3];

	private Zstd( int sizeHint )
	{
		_output = new byte[Math.Max( sizeHint, 256 )];
	}

	/// <summary>
	/// Decompresses every Zstandard frame in <paramref name="input"/>.
	/// </summary>
	/// <param name="sizeHint">Expected decompressed size, used for the initial allocation.</param>
	public static byte[] Decompress( ReadOnlySpan<byte> input, int sizeHint = 0 )
	{
		var decoder = new Zstd( sizeHint );
		decoder.DecodeFrames( input );

		return decoder._length == decoder._output.Length ? decoder._output : decoder._output.AsSpan( 0, decoder._length ).ToArray();
	}

	/// <summary>
	/// Decompresses into <paramref name="output"/>, which must be exactly the decompressed size.
	/// </summary>
	public static void Decompress( ReadOnlySpan<byte> input, Span<byte> output )
	{
		var decoder = new Zstd( output.Length );
		decoder.DecodeFrames( input );

		if ( decoder._length != output.Length )
			throw new InvalidDataException( $"Zstd decoded {decoder._length} bytes, expected {output.Length}." );

		decoder._output.AsSpan( 0, decoder._length ).CopyTo( output );
	}

	private void DecodeFrames( ReadOnlySpan<byte> input )
	{
		var position = 0;

		while ( position < input.Length )
		{
			if ( input.Length - position < 4 )
				throw new InvalidDataException( "Zstd frame header is truncated." );

			var magic = BinaryPrimitives.ReadUInt32LittleEndian( input[position..] );
			position += 4;

			if ( (magic & SkippableMagicMask) == SkippableMagic )
			{
				position += 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian( input[position..] );
				continue;
			}

			if ( magic != FrameMagic )
				throw new InvalidDataException( $"Invalid Zstd frame magic 0x{magic:X8}." );

			position = DecodeFrame( input, position );
		}
	}

	private int DecodeFrame( ReadOnlySpan<byte> input, int position )
	{
		var descriptor = input[position++];
		var contentSizeFlag = descriptor >> 6;
		var singleSegment = (descriptor & 0x20) != 0;
		var hasChecksum = (descriptor & 0x04) != 0;
		var dictionaryIdFlag = descriptor & 3;

		if ( (descriptor & 0x08) != 0 )
			throw new InvalidDataException( "Zstd frame header reserved bit is set." );

		if ( !singleSegment )
			position++; // window descriptor, the whole frame is kept in memory anyway

		var dictionaryIdSize = dictionaryIdFlag == 3 ? 4 : dictionaryIdFlag;
		if ( ReadLittleEndian( input.Slice( position, dictionaryIdSize ) ) != 0 )
			throw new NotSupportedException( "Zstd frames with dictionaries are not supported." );

		position += dictionaryIdSize;

		var contentSizeBytes = contentSizeFlag switch
		{
			0 => singleSegment ? 1 : 0,
			1 => 2,
			2 => 4,
			_ => 8,
		};

		var contentSize = ReadLittleEndian( input.Slice( position, contentSizeBytes ) );
		if ( contentSizeBytes == 2 )
			contentSize += 256;

		position += contentSizeBytes;

		if ( contentSizeBytes > 0 && contentSize < int.MaxValue )
			EnsureCapacity( (int)contentSize );

		_frameStart = _length;
		_huffman = null;
		_literalLengths = _offsets = _matchLengths = null;
		_repeatOffsets[0] = 1;
		_repeatOffsets[1] = 4;
		_repeatOffsets[2] = 8;

		while ( true )
		{
			if ( input.Length - position < 3 )
				throw new InvalidDataException( "Zstd block header is truncated." );

			var header = input[position] | (input[position + 1] << 8) | (input[position + 2] << 16);
			position += 3;

			var isLast = (header & 1) != 0;
			var blockType = (header >> 1) & 3;
			var blockSize = header >> 3;

			switch ( blockType )
			{
				case 0:
					Append( input.Slice( position, blockSize ) );
					position += blockSize;
					break;

				case 1:
					EnsureCapacity( blockSize );
					_output.AsSpan( _length, blockSize ).Fill( input[position] );
					_length += blockSize;
					position++;
					break;

				case 2:
					DecodeCompressedBlock( input.Slice( position, blockSize ) );
					position += blockSize;
					break;

				default:
					throw new InvalidDataException( "Zstd block type is reserved." );
			}

			if ( isLast )
				break;
		}

		if ( hasChecksum )
			position += 4;

		return position;
	}

	private void DecodeCompressedBlock( ReadOnlySpan<byte> block )
	{
		var position = 0;
		var literals = DecodeLiterals( block, ref position );
		DecodeSequences( block[position..], literals );
	}

	private ReadOnlySpan<byte> DecodeLiterals( ReadOnlySpan<byte> block, ref int position )
	{
		var b0 = block[0];
		var literalsType = b0 & 3;
		var sizeFormat = (b0 >> 2) & 3;

		if ( literalsType is 0 or 1 )
		{
			int regeneratedSize;

			switch ( sizeFormat )
			{
				case 1:
					regeneratedSize = (b0 >> 4) | (block[1] << 4);
					position = 2;
					break;

				case 3:
					regeneratedSize = (b0 >> 4) | (block[1] << 4) | (block[2] << 12);
					position = 3;
					break;

				default:
					regeneratedSize = b0 >> 3;
					position = 1;
					break;
			}

			if ( literalsType == 0 )
			{
				var raw = block.Slice( position, regeneratedSize );
				position += regeneratedSize;
				return raw;
			}

			_literals.AsSpan( 0, regeneratedSize ).Fill( block[position] );
			position++;
			return _literals.AsSpan( 0, regeneratedSize );
		}

		int regenerated;
		int compressed;
		var streamCount = sizeFormat == 0 ? 1 : 4;

		switch ( sizeFormat )
		{
			case 0:
			case 1:
			{
				var header = b0 | (block[1] << 8) | (block[2] << 16);
				regenerated = (header >> 4) & 0x3FF;
				compressed = (header >> 14) & 0x3FF;
				position = 3;
				break;
			}

			case 2:
			{
				var header = BinaryPrimitives.ReadUInt32LittleEndian( block );
				regenerated = (int)((header >> 4) & 0x3FFF);
				compressed = (int)((header >> 18) & 0x3FFF);
				position = 4;
				break;
			}

			default:
			{
				var header = BinaryPrimitives.ReadUInt32LittleEndian( block ) | ((ulong)block[4] << 32);
				regenerated = (int)((header >> 4) & 0x3FFFF);
				compressed = (int)((header >> 22) & 0x3FFFF);
				position = 5;
				break;
			}
		}

		if ( regenerated > MaxBlockSize )
			throw new InvalidDataException( "Zstd literals section is too large." );

		var data = block.Slice( position, compressed );
		position += compressed;

		if ( literalsType == 2 )
		{
			_huffman = HuffmanTable.Read( data, out var tableSize );
			data = data[tableSize..];
		}
		else if ( _huffman is null )
		{
			throw new InvalidDataException( "Zstd treeless literals block has no previous Huffman table." );
		}

		var output = _literals.AsSpan( 0, regenerated );

		if ( streamCount == 1 )
		{
			_huffman.DecodeStream( data, output );
			return output;
		}

		var size1 = BinaryPrimitives.ReadUInt16LittleEndian( data );
		var size2 = BinaryPrimitives.ReadUInt16LittleEndian( data[2..] );
		var size3 = BinaryPrimitives.ReadUInt16LittleEndian( data[4..] );
		var streams = data[6..];
		var segment = (regenerated + 3) / 4;

		if ( size1 + size2 + size3 > streams.Length || segment * 3 > regenerated )
			throw new InvalidDataException( "Zstd literals jump table is corrupt." );

		_huffman.DecodeStream( streams[..size1], output[..segment] );
		_huffman.DecodeStream( streams.Slice( size1, size2 ), output.Slice( segment, segment ) );
		_huffman.DecodeStream( streams.Slice( size1 + size2, size3 ), output.Slice( segment * 2, segment ) );
		_huffman.DecodeStream( streams[(size1 + size2 + size3)..], output[(segment * 3)..] );

		return output;
	}

	private void DecodeSequences( ReadOnlySpan<byte> section, ReadOnlySpan<byte> literals )
	{
		var position = 0;
		int count = section[position++];

		if ( count >= 128 )
		{
			if ( count < 255 )
			{
				count = ((count - 128) << 8) + section[position++];
			}
			else
			{
				count = section[position] + (section[position + 1] << 8) + 0x7F00;
				position += 2;
			}
		}

		if ( count == 0 )
		{
			Append( literals );
			return;
		}

		var modes = section[position++];
		if ( (modes & 3) != 0 )
			throw new InvalidDataException( "Zstd sequence modes reserved bits are set." );

		_literalLengths = ReadSequenceTable( section, ref position, (modes >> 6) & 3, SequenceKind.LiteralLength, _literalLengths );
		_offsets = ReadSequenceTable( section, ref position, (modes >> 4) & 3, SequenceKind.Offset, _offsets );
		_matchLengths = ReadSequenceTable( section, ref position, (modes >> 2) & 3, SequenceKind.MatchLength, _matchLengths );

		var reader = new BackwardBitReader( section[position..] );
		var literalLengthState = (int)reader.Read( _literalLengths.AccuracyLog );
		var offsetState = (int)reader.Read( _offsets.AccuracyLog );
		var matchLengthState = (int)reader.Read( _matchLengths.AccuracyLog );

		var literalPosition = 0;

		for ( var i = 0; i < count; i++ )
		{
			int offsetCode = _offsets.Symbols[offsetState];
			int literalLengthCode = _literalLengths.Symbols[literalLengthState];
			int matchLengthCode = _matchLengths.Symbols[matchLengthState];

			if ( offsetCode > 31 || literalLengthCode >= LiteralLengthBaselines.Length || matchLengthCode >= MatchLengthBaselines.Length )
				throw new InvalidDataException( "Zstd sequence code is out of range." );

			var offsetValue = (1u << offsetCode) + (uint)reader.Read( offsetCode );
			var matchLength = MatchLengthBaselines[matchLengthCode] + (int)reader.Read( MatchLengthExtraBits[matchLengthCode] );
			var literalLength = LiteralLengthBaselines[literalLengthCode] + (int)reader.Read( LiteralLengthExtraBits[literalLengthCode] );

			if ( i != count - 1 )
			{
				literalLengthState = _literalLengths.NextState( literalLengthState, ref reader );
				matchLengthState = _matchLengths.NextState( matchLengthState, ref reader );
				offsetState = _offsets.NextState( offsetState, ref reader );
			}

			if ( literalLength > literals.Length - literalPosition )
				throw new InvalidDataException( "Zstd sequence reads past the literals." );

			Append( literals.Slice( literalPosition, literalLength ) );
			literalPosition += literalLength;

			CopyMatch( ResolveOffset( offsetValue, literalLength ), matchLength );
		}

		if ( reader.Offset != 0 )
			throw new InvalidDataException( "Zstd sequence bitstream was not fully consumed." );

		Append( literals[literalPosition..] );
	}

	private int ResolveOffset( uint offsetValue, int literalLength )
	{
		var history = _repeatOffsets;

		if ( offsetValue > 3 )
		{
			history[2] = history[1];
			history[1] = history[0];
			history[0] = offsetValue - 3;
			return (int)history[0];
		}

		// A literal length of zero shifts the repeat offsets by one.
		var index = (int)offsetValue - 1 + (literalLength == 0 ? 1 : 0);
		if ( index == 0 )
			return (int)history[0];

		var offset = index < 3 ? history[index] : history[0] - 1;

		if ( index > 1 )
			history[2] = history[1];

		history[1] = history[0];
		history[0] = offset;

		return (int)offset;
	}

	private void CopyMatch( int offset, int length )
	{
		if ( offset <= 0 || offset > _length - _frameStart )
			throw new InvalidDataException( "Zstd match offset is out of bounds." );

		EnsureCapacity( length );

		var source = _length - offset;
		if ( offset >= length )
		{
			_output.AsSpan( source, length ).CopyTo( _output.AsSpan( _length ) );
		}
		else
		{
			for ( var i = 0; i < length; i++ )
				_output[_length + i] = _output[source + i];
		}

		_length += length;
	}

	private void Append( ReadOnlySpan<byte> data )
	{
		EnsureCapacity( data.Length );
		data.CopyTo( _output.AsSpan( _length ) );
		_length += data.Length;
	}

	private void EnsureCapacity( int extra )
	{
		var needed = _length + extra;
		if ( needed > _output.Length )
			Array.Resize( ref _output, Math.Max( needed, _output.Length * 2 ) );
	}

	private static ulong ReadLittleEndian( ReadOnlySpan<byte> bytes )
	{
		ulong value = 0;
		for ( var i = 0; i < bytes.Length; i++ )
			value |= (ulong)bytes[i] << (i * 8);

		return value;
	}

	private static int HighestBit( ulong value ) => 63 - BitOperations.LeadingZeroCount( value );

	/// <summary>
	/// Reads <paramref name="count"/> bits starting at <paramref name="bitOffset"/>, least significant bit first.
	/// Bits past the end of <paramref name="source"/> read as zero.
	/// </summary>
	private static ulong ReadBits( ReadOnlySpan<byte> source, int count, long bitOffset )
	{
		if ( count == 0 )
			return 0;

		var bytePosition = (int)(bitOffset >> 3);
		var shift = (int)(bitOffset & 7);
		var byteCount = (shift + count + 7) >> 3;

		ulong value = 0;
		for ( var i = 0; i < byteCount && bytePosition + i < source.Length; i++ )
			value |= (ulong)source[bytePosition + i] << (i * 8);

		return (value >> shift) & ((1UL << count) - 1);
	}

	/// <summary>
	/// Reads a bitstream from its end towards its start, as Huffman and FSE streams are written.
	/// </summary>
	private ref struct BackwardBitReader
	{
		private readonly ReadOnlySpan<byte> _source;

		/// <summary>
		/// Bit position of the next unread bit's upper edge. Goes negative once the stream is overread,
		/// which is how decoders detect the end of a stream.
		/// </summary>
		public long Offset;

		public BackwardBitReader( ReadOnlySpan<byte> source )
		{
			if ( source.IsEmpty || source[^1] == 0 )
				throw new InvalidDataException( "Zstd bitstream has no end marker." );

			_source = source;
			Offset = source.Length * 8L - (8 - HighestBit( source[^1] ));
		}

		public ulong Read( int count )
		{
			if ( count == 0 )
				return 0;

			Offset -= count;

			if ( Offset >= 0 )
				return ReadBits( _source, count, Offset );

			// Bits before the start of the stream read as zero.
			var available = count + Offset;
			if ( available <= 0 )
				return 0;

			return ReadBits( _source, (int)available, 0 ) << (int)-Offset;
		}
	}

	/// <summary>
	/// Reads a bitstream forwards, as FSE table descriptions are written.
	/// </summary>
	private ref struct ForwardBitReader( ReadOnlySpan<byte> source )
	{
		private readonly ReadOnlySpan<byte> _source = source;
		private long _offset;

		public int Read( int count )
		{
			var value = (int)ReadBits( _source, count, _offset );
			_offset += count;
			return value;
		}

		public void Rewind( int count ) => _offset -= count;

		/// <summary>
		/// Bytes consumed so far, rounding up a partially read byte.
		/// </summary>
		public int BytesRead => (int)((_offset + 7) >> 3);
	}
}
