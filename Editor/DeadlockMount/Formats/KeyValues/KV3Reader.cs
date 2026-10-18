using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Reads binary KeyValues3 (versions 1 to 5) as found in DATA, CTRL, MDAT and similar blocks.
/// The layout follows ValveResourceFormat's reader.
/// </summary>
internal static class KV3Reader
{
	private const uint LegacyMagic = 0x03564B56; // VKV3
	private const uint Magic = 0x4B563300; // KV3 followed by the version byte
	private const uint Trailer = 0xFFEEDD00;

	private enum Compression
	{
		None = 0,
		Lz4 = 1,
		Zstd = 2,
	}

	private enum NodeType : byte
	{
		Null = 1,
		Boolean = 2,
		Int64 = 3,
		UInt64 = 4,
		Double = 5,
		String = 6,
		BinaryBlob = 7,
		Array = 8,
		Object = 9,
		ArrayTyped = 10,
		Int32 = 11,
		UInt32 = 12,
		BooleanTrue = 13,
		BooleanFalse = 14,
		Int64Zero = 15,
		Int64One = 16,
		DoubleZero = 17,
		DoubleOne = 18,
		Float = 19,
		Int16 = 20,
		UInt16 = 21,
		Int8 = 22,
		UInt8 = 23,
		ArrayTypeByteLength = 24,
		ArrayTypeAuxiliaryBuffer = 25,
		UInt32Zero = 26,
		UInt32One = 27,
	}

	/// <summary>
	/// A cursor over one region of a decoded buffer.
	/// </summary>
	private sealed class Lane( byte[] data, int start, int end )
	{
		private readonly byte[] _data = data;
		private int _position = start;

		public static Lane Empty { get; } = new( [], 0, 0 );

		public bool IsEmpty => _position >= end;

		public ReadOnlySpan<byte> Take( int count )
		{
			if ( count < 0 || count > end - _position )
				throw new InvalidDataException( "KV3 data is truncated." );

			var span = _data.AsSpan( _position, count );
			_position += count;
			return span;
		}

		public byte ReadByte() => Take( 1 )[0];
		public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian( Take( 2 ) );
		public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian( Take( 2 ) );
		public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian( Take( 4 ) );
		public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian( Take( 4 ) );
		public float ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian( Take( 4 ) );
		public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian( Take( 8 ) );
		public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian( Take( 8 ) );
		public double ReadDouble() => BinaryPrimitives.ReadDoubleLittleEndian( Take( 8 ) );

		public string ReadNullTerminatedString()
		{
			var remaining = _data.AsSpan( _position, end - _position );
			var length = remaining.IndexOf( (byte)0 );
			if ( length < 0 )
				throw new InvalidDataException( "KV3 string is unterminated." );

			var value = Encoding.UTF8.GetString( remaining[..length] );
			_position += length + 1;
			return value;
		}
	}

	/// <summary>
	/// The 1, 2, 4 and 8 byte value lanes of a buffer.
	/// </summary>
	private sealed class Lanes
	{
		public Lane Bytes1 = Lane.Empty;
		public Lane Bytes2 = Lane.Empty;
		public Lane Bytes4 = Lane.Empty;
		public Lane Bytes8 = Lane.Empty;
	}

	private sealed class Context
	{
		public int Version;
		public string[] Strings;
		public Lane Types;
		public Lane ObjectLengths = Lane.Empty;
		public Lane BlobLengths = Lane.Empty;
		public Lane Blobs = Lane.Empty;
		public Lanes Buffer;
		public Lanes Auxiliary;
	}

	/// <summary>
	/// The format a binary KeyValues3 document says it's in: what its data describes and in which version, e.g. a
	/// particle system format. Empty for data too short to have one.
	/// </summary>
	public static Guid ReadFormat( ReadOnlySpan<byte> data ) => data.Length >= 20 ? new Guid( data.Slice( 4, 16 ) ) : Guid.Empty;

	public static KVObject Read( ReadOnlySpan<byte> data )
	{
		var magic = BinaryPrimitives.ReadUInt32LittleEndian( data );

		if ( magic == LegacyMagic )
			throw new NotSupportedException( "Legacy VKV3 binary KeyValues are not supported." );

		if ( (magic & 0xFFFFFF00) != Magic )
			throw new InvalidDataException( $"Not binary KeyValues3 (magic 0x{magic:X8})." );

		var version = (int)(magic & 0xFF);
		if ( version is < 1 or > 5 )
			throw new NotSupportedException( $"KeyValues3 version {version} is not supported." );

		var root = ReadDocument( version, data );
		return root as KVObject ?? throw new InvalidDataException( "KeyValues3 root is not an object." );
	}

	private static object ReadDocument( int version, ReadOnlySpan<byte> data )
	{
		var position = 4 + 16; // magic and format guid
		var compression = (Compression)ReadInt32( data, ref position );

		if ( compression is not (Compression.None or Compression.Lz4 or Compression.Zstd) )
			throw new InvalidDataException( $"Unknown KV3 compression method {(int)compression}." );

		int countBytes1, countBytes2 = 0, countBytes4, countBytes8, countTypes = 0;
		int sizeUncompressedTotal, sizeCompressedTotal, countBlobs = 0, sizeBlobs = 0;

		if ( version == 1 )
		{
			countBytes1 = ReadInt32( data, ref position );
			countBytes4 = ReadInt32( data, ref position );
			countBytes8 = ReadInt32( data, ref position );
			sizeUncompressedTotal = ReadInt32( data, ref position );
			sizeCompressedTotal = data.Length - position;
		}
		else
		{
			var dictionaryId = ReadUInt16( data, ref position );
			ReadUInt16( data, ref position ); // compression frame size
			countBytes1 = ReadInt32( data, ref position );
			countBytes4 = ReadInt32( data, ref position );
			countBytes8 = ReadInt32( data, ref position );
			countTypes = ReadInt32( data, ref position );
			ReadUInt16( data, ref position ); // object count
			ReadUInt16( data, ref position ); // array count
			sizeUncompressedTotal = ReadInt32( data, ref position );
			sizeCompressedTotal = ReadInt32( data, ref position );
			countBlobs = ReadInt32( data, ref position );
			sizeBlobs = ReadInt32( data, ref position );

			if ( dictionaryId != 0 )
				throw new NotSupportedException( "KV3 compression dictionaries are not supported." );
		}

		if ( version >= 4 )
		{
			countBytes2 = ReadInt32( data, ref position );
			ReadInt32( data, ref position ); // size of the blob compressed sizes
		}

		int sizeUncompressed1, sizeCompressed1, sizeUncompressed2 = 0, sizeCompressed2 = 0;
		int countBytes1Buffer2 = 0, countBytes2Buffer2 = 0, countBytes4Buffer2 = 0, countBytes8Buffer2 = 0, countObjectsBuffer2 = 0;

		if ( version >= 5 )
		{
			sizeUncompressed1 = ReadInt32( data, ref position );
			sizeCompressed1 = ReadInt32( data, ref position );
			sizeUncompressed2 = ReadInt32( data, ref position );
			sizeCompressed2 = ReadInt32( data, ref position );
			countBytes1Buffer2 = ReadInt32( data, ref position );
			countBytes2Buffer2 = ReadInt32( data, ref position );
			countBytes4Buffer2 = ReadInt32( data, ref position );
			countBytes8Buffer2 = ReadInt32( data, ref position );
			ReadInt32( data, ref position ); // node count
			countObjectsBuffer2 = ReadInt32( data, ref position );
			ReadInt32( data, ref position ); // array count
			ReadInt32( data, ref position ); // array element count
		}
		else
		{
			sizeUncompressed1 = sizeUncompressedTotal;
			sizeCompressed1 = sizeCompressedTotal;
		}

		var context = new Context { Version = version };

		// Before version 5, zstd stores the buffer and the blobs as two consecutive frames covered by one compressed size.
		var zstdIncludesBlobs = version < 5 && compression == Compression.Zstd;
		var buffer1 = Decompress( data, ref position, compression, sizeCompressed1, zstdIncludesBlobs ? sizeUncompressed1 + sizeBlobs : sizeUncompressed1 );

		var buffer1Lanes = new Lanes();
		var offset = 0;

		offset = ReadLanes( buffer1, offset, buffer1Lanes, countBytes1, countBytes2, countBytes4, countBytes8, alignEmpty: version < 5 );

		var countStrings = buffer1Lanes.Bytes4.ReadInt32();
		context.Strings = new string[countStrings];
		context.Auxiliary = buffer1Lanes;

		Lane blobSizes = null;

		if ( version >= 5 )
		{
			for ( var i = 0; i < countStrings; i++ )
				context.Strings[i] = buffer1Lanes.Bytes1.ReadNullTerminatedString();

			var buffer2 = Decompress( data, ref position, compression, sizeCompressed2, sizeUncompressed2 );
			var buffer2Lanes = new Lanes();
			context.Buffer = buffer2Lanes;

			var objectLengthsSize = countObjectsBuffer2 * 4;
			context.ObjectLengths = new Lane( buffer2, 0, objectLengthsSize );

			var offset2 = ReadLanes( buffer2, objectLengthsSize, buffer2Lanes, countBytes1Buffer2, countBytes2Buffer2, countBytes4Buffer2, countBytes8Buffer2, alignEmpty: false );

			context.Types = new Lane( buffer2, offset2, offset2 + countTypes );
			offset2 += countTypes;

			if ( countBlobs == 0 )
				ExpectTrailer( BinaryPrimitives.ReadUInt32LittleEndian( buffer2.AsSpan( offset2 ) ) );
			else
				blobSizes = new Lane( buffer2, offset2, sizeUncompressed2 );
		}
		else
		{
			context.Buffer = buffer1Lanes;

			var stringsStart = offset;
			var strings = new Lane( buffer1, offset, sizeUncompressed1 );

			for ( var i = 0; i < countStrings; i++ )
			{
				var value = strings.ReadNullTerminatedString();
				context.Strings[i] = value;
				offset += Encoding.UTF8.GetByteCount( value ) + 1;
			}

			var typesLength = version == 1 ? sizeUncompressedTotal - offset - 4 : countTypes - (offset - stringsStart);
			context.Types = new Lane( buffer1, offset, offset + typesLength );
			offset += typesLength;

			if ( countBlobs == 0 )
				ExpectTrailer( BinaryPrimitives.ReadUInt32LittleEndian( buffer1.AsSpan( offset ) ) );
			else
				blobSizes = new Lane( buffer1, offset, sizeUncompressed1 );
		}

		if ( countBlobs > 0 )
		{
			var lengthBytes = blobSizes.Take( countBlobs * 4 ).ToArray();
			context.BlobLengths = new Lane( lengthBytes, 0, lengthBytes.Length );
			ExpectTrailer( blobSizes.ReadUInt32() );

			context.Blobs = ReadBlobs( data, ref position, compression, version, sizeBlobs, lengthBytes, blobSizes,
				sizeCompressedTotal - sizeCompressed1 - sizeCompressed2, zstdIncludesBlobs ? buffer1 : null, sizeUncompressed1 );

			ExpectTrailer( (uint)ReadInt32( data, ref position ) );
		}

		var (rootType, _) = ReadType( context );
		return ReadValue( context, rootType, context.Buffer );
	}

	private static int ReadLanes( byte[] buffer, int offset, Lanes lanes, int count1, int count2, int count4, int count8, bool alignEmpty )
	{
		if ( count1 > 0 )
		{
			lanes.Bytes1 = new Lane( buffer, offset, offset + count1 );
			offset += count1;
		}

		if ( count2 > 0 )
		{
			offset = Align( offset, 2 );
			lanes.Bytes2 = new Lane( buffer, offset, offset + count2 * 2 );
			offset += count2 * 2;
		}

		if ( count4 > 0 )
		{
			offset = Align( offset, 4 );
			lanes.Bytes4 = new Lane( buffer, offset, offset + count4 * 4 );
			offset += count4 * 4;
		}

		if ( count8 > 0 )
		{
			offset = Align( offset, 8 );
			lanes.Bytes8 = new Lane( buffer, offset, offset + count8 * 8 );
			offset += count8 * 8;
		}
		else if ( alignEmpty )
		{
			// Before version 5 the lanes end aligned even when the 8 byte lane is empty.
			offset = Align( offset, 8 );
		}

		return offset;
	}

	private static byte[] Decompress( ReadOnlySpan<byte> data, ref int position, Compression compression, int compressedSize, int uncompressedSize )
	{
		var output = GC.AllocateUninitializedArray<byte>( uncompressedSize );

		switch ( compression )
		{
			case Compression.None:
				data.Slice( position, uncompressedSize ).CopyTo( output );
				position += uncompressedSize;
				break;

			case Compression.Lz4:
				if ( Lz4.Decode( data.Slice( position, compressedSize ), output ) != uncompressedSize )
					throw new InvalidDataException( "KV3 LZ4 buffer decoded to the wrong size." );

				position += compressedSize;
				break;

			case Compression.Zstd:
				Zstd.Decompress( data.Slice( position, compressedSize ), output );
				position += compressedSize;
				break;
		}

		return output;
	}

	private static Lane ReadBlobs( ReadOnlySpan<byte> data, ref int position, Compression compression, int version, int sizeBlobs,
		byte[] lengthBytes, Lane frameSizes, int zstdCompressedSize, byte[] zstdBuffer, int zstdBlobsOffset )
	{
		if ( compression == Compression.Zstd && version < 5 )
			return new Lane( zstdBuffer, zstdBlobsOffset, zstdBlobsOffset + sizeBlobs );

		if ( compression != Compression.Lz4 )
			return new Lane( Decompress( data, ref position, compression, zstdCompressedSize, sizeBlobs ), 0, sizeBlobs );

		// Each blob is split into LZ4 frames that never span two blobs. Frames are chained, so later frames can
		// reference earlier output, which decoding into one contiguous buffer gives us for free.
		var blobs = GC.AllocateUninitializedArray<byte>( sizeBlobs );
		var blobOffset = 0;

		for ( var i = 0; i < lengthBytes.Length; i += 4 )
		{
			var blobEnd = blobOffset + BinaryPrimitives.ReadInt32LittleEndian( lengthBytes.AsSpan( i ) );

			while ( blobOffset < blobEnd )
			{
				var frameSize = frameSizes.ReadUInt16();
				var decoded = Lz4.Decode( data.Slice( position, frameSize ), blobs.AsSpan( 0, blobEnd ), blobOffset );
				position += frameSize;

				if ( decoded < 1 )
					throw new InvalidDataException( "KV3 LZ4 blob frame is empty." );

				blobOffset += decoded;
			}
		}

		return new Lane( blobs, 0, sizeBlobs );
	}

	private static (NodeType Type, byte Flags) ReadType( Context context )
	{
		var value = context.Types.ReadByte();
		byte flags = 0;

		if ( context.Version >= 3 )
		{
			if ( (value & 0x80) != 0 )
				flags = context.Types.ReadByte();

			// No known writer sets this bit; readers skip the byte that follows it.
			if ( (value & 0x40) != 0 )
				context.Types.ReadByte();

			value &= 0x3F;
		}
		else
		{
			if ( (value & 0x80) != 0 )
				flags = context.Types.ReadByte();

			value &= 0x7F;
		}

		return ((NodeType)value, flags);
	}

	private static string GetString( Context context, int id )
	{
		return (uint)id < (uint)context.Strings.Length ? context.Strings[id] : string.Empty;
	}

	/// <summary>
	/// Reads a value. Primitives come from <paramref name="lane"/>, which is the auxiliary buffer for elements of
	/// auxiliary typed arrays; strings, blobs and container lengths always come from the main buffer.
	/// </summary>
	private static object ReadValue( Context context, NodeType type, Lanes lane )
	{
		var buffer = context.Buffer;

		switch ( type )
		{
			case NodeType.Null: return null;
			case NodeType.BooleanTrue: return true;
			case NodeType.BooleanFalse: return false;
			case NodeType.Int64Zero: return 0L;
			case NodeType.Int64One: return 1L;
			case NodeType.DoubleZero: return 0.0;
			case NodeType.DoubleOne: return 1.0;
			case NodeType.UInt32Zero: return 0u;
			case NodeType.UInt32One: return 1u;

			case NodeType.Boolean: return lane.Bytes1.ReadByte() != 0;
			case NodeType.Int8: return (int)(sbyte)lane.Bytes1.ReadByte();
			case NodeType.UInt8: return (uint)lane.Bytes1.ReadByte();
			case NodeType.Int16: return lane.Bytes2.ReadInt16();
			case NodeType.UInt16: return lane.Bytes2.ReadUInt16();
			case NodeType.Int32: return lane.Bytes4.ReadInt32();
			case NodeType.UInt32: return lane.Bytes4.ReadUInt32();
			case NodeType.Float: return lane.Bytes4.ReadSingle();
			case NodeType.Int64: return lane.Bytes8.ReadInt64();
			case NodeType.UInt64: return lane.Bytes8.ReadUInt64();
			case NodeType.Double: return lane.Bytes8.ReadDouble();

			case NodeType.String:
				return GetString( context, buffer.Bytes4.ReadInt32() );

			case NodeType.BinaryBlob when context.Version < 2:
				return buffer.Bytes1.Take( buffer.Bytes4.ReadInt32() ).ToArray();

			case NodeType.BinaryBlob:
				return context.Blobs.Take( context.BlobLengths.ReadInt32() ).ToArray();

			case NodeType.Array:
			{
				var array = new object[buffer.Bytes4.ReadInt32()];

				for ( var i = 0; i < array.Length; i++ )
				{
					var (elementType, _) = ReadType( context );
					array[i] = ReadValue( context, elementType, context.Buffer );
				}

				return array;
			}

			case NodeType.ArrayTyped:
			case NodeType.ArrayTypeByteLength:
			case NodeType.ArrayTypeAuxiliaryBuffer:
			{
				var length = type == NodeType.ArrayTyped ? buffer.Bytes4.ReadInt32() : buffer.Bytes1.ReadByte();
				var (elementType, _) = ReadType( context );
				var elementLane = type == NodeType.ArrayTypeAuxiliaryBuffer ? context.Auxiliary : buffer;
				var array = new object[length];

				for ( var i = 0; i < array.Length; i++ )
					array[i] = ReadValue( context, elementType, elementLane );

				return array;
			}

			case NodeType.Object:
			{
				var length = context.Version >= 5 ? context.ObjectLengths.ReadInt32() : buffer.Bytes4.ReadInt32();
				var result = new KVObject( length );

				for ( var i = 0; i < length; i++ )
				{
					var (memberType, _) = ReadType( context );
					var name = GetString( context, buffer.Bytes4.ReadInt32() );
					result[name] = ReadValue( context, memberType, context.Buffer );
				}

				return result;
			}

			default:
				throw new InvalidDataException( $"Unknown KV3 node type {(int)type}." );
		}
	}

	private static void ExpectTrailer( uint value )
	{
		if ( value != Trailer )
			throw new InvalidDataException( $"KV3 trailer mismatch (0x{value:X8})." );
	}

	private static int Align( int value, int alignment ) => (value + alignment - 1) & ~(alignment - 1);

	private static int ReadInt32( ReadOnlySpan<byte> data, ref int position )
	{
		var value = BinaryPrimitives.ReadInt32LittleEndian( data[position..] );
		position += 4;
		return value;
	}

	private static ushort ReadUInt16( ReadOnlySpan<byte> data, ref int position )
	{
		var value = BinaryPrimitives.ReadUInt16LittleEndian( data[position..] );
		position += 2;
		return value;
	}
}
