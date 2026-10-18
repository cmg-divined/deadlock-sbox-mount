using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// The subset of DXGI formats vertex attributes use. Values match DXGI_FORMAT.
/// </summary>
internal enum DxgiFormat : uint
{
	Unknown = 0,
	R32G32B32A32_FLOAT = 2,
	R32G32B32A32_UINT = 3,
	R32G32B32A32_SINT = 4,
	R32G32B32_FLOAT = 6,
	R32G32B32_UINT = 7,
	R32G32B32_SINT = 8,
	R16G16B16A16_FLOAT = 10,
	R16G16B16A16_UNORM = 11,
	R16G16B16A16_UINT = 12,
	R16G16B16A16_SNORM = 13,
	R16G16B16A16_SINT = 14,
	R32G32_FLOAT = 16,
	R32G32_UINT = 17,
	R32G32_SINT = 18,
	R10G10B10A2_UNORM = 24,
	R10G10B10A2_UINT = 25,
	R8G8B8A8_UNORM = 28,
	R8G8B8A8_UNORM_SRGB = 29,
	R8G8B8A8_UINT = 30,
	R8G8B8A8_SNORM = 31,
	R8G8B8A8_SINT = 32,
	R16G16_FLOAT = 34,
	R16G16_UNORM = 35,
	R16G16_UINT = 36,
	R16G16_SNORM = 37,
	R16G16_SINT = 38,
	R32_FLOAT = 41,
	R32_UINT = 42,
	R32_SINT = 43,
	R8G8_UNORM = 49,
	R8G8_UINT = 50,
	R8G8_SNORM = 51,
	R8G8_SINT = 52,
	R16_FLOAT = 54,
	R16_UNORM = 56,
	R16_UINT = 57,
	R16_SNORM = 58,
	R16_SINT = 59,
	R8_UNORM = 61,
	R8_UINT = 62,
	R8_SNORM = 63,
	R8_SINT = 64,
	B8G8R8A8_UNORM = 87,
}

/// <summary>
/// One attribute of a vertex buffer's input layout.
/// </summary>
internal readonly record struct InputLayoutField( string SemanticName, int SemanticIndex, DxgiFormat Format, int Offset, int Slot );

/// <summary>
/// A decoded vertex or index buffer.
/// </summary>
internal sealed class MeshBuffer
{
	public int ElementCount { get; init; }
	public int ElementSize { get; init; }
	public InputLayoutField[] Fields { get; init; } = [];
	public byte[] Data { get; init; }

	/// <summary>
	/// Finds an attribute by semantic, or null.
	/// </summary>
	public InputLayoutField? Find( string semanticName, int semanticIndex = 0 )
	{
		foreach ( var field in Fields )
		{
			if ( field.SemanticIndex == semanticIndex && field.SemanticName == semanticName )
				return field;
		}

		return null;
	}

	/// <summary>
	/// The bytes of one attribute of one vertex.
	/// </summary>
	public ReadOnlySpan<byte> GetAttributeBytes( InputLayoutField field, int vertex )
	{
		return Data.AsSpan( vertex * ElementSize + field.Offset );
	}

	/// <summary>
	/// Decodes one attribute of one vertex into floats, normalizing UNORM and SNORM formats.
	/// </summary>
	/// <returns>The number of components written.</returns>
	public int Read( InputLayoutField field, int vertex, Span<float> output )
	{
		var data = GetAttributeBytes( field, vertex );

		switch ( field.Format )
		{
			case DxgiFormat.R32G32B32A32_FLOAT: return ReadFloats( data, output, 4 );
			case DxgiFormat.R32G32B32_FLOAT: return ReadFloats( data, output, 3 );
			case DxgiFormat.R32G32_FLOAT: return ReadFloats( data, output, 2 );
			case DxgiFormat.R32_FLOAT: return ReadFloats( data, output, 1 );

			case DxgiFormat.R16G16B16A16_FLOAT: return ReadHalves( data, output, 4 );
			case DxgiFormat.R16G16_FLOAT: return ReadHalves( data, output, 2 );
			case DxgiFormat.R16_FLOAT: return ReadHalves( data, output, 1 );

			case DxgiFormat.R32G32B32A32_UINT: return ReadUInt32s( data, output, 4 );
			case DxgiFormat.R32G32B32_UINT: return ReadUInt32s( data, output, 3 );
			case DxgiFormat.R32G32_UINT: return ReadUInt32s( data, output, 2 );
			case DxgiFormat.R32_UINT: return ReadUInt32s( data, output, 1 );

			case DxgiFormat.R32G32B32A32_SINT: return ReadInt32s( data, output, 4 );
			case DxgiFormat.R32G32B32_SINT: return ReadInt32s( data, output, 3 );
			case DxgiFormat.R32G32_SINT: return ReadInt32s( data, output, 2 );
			case DxgiFormat.R32_SINT: return ReadInt32s( data, output, 1 );

			case DxgiFormat.R16G16B16A16_UNORM: return ReadUInt16s( data, output, 4, 1 / 65535f );
			case DxgiFormat.R16G16_UNORM: return ReadUInt16s( data, output, 2, 1 / 65535f );
			case DxgiFormat.R16_UNORM: return ReadUInt16s( data, output, 1, 1 / 65535f );
			case DxgiFormat.R16G16B16A16_UINT: return ReadUInt16s( data, output, 4, 1 );
			case DxgiFormat.R16G16_UINT: return ReadUInt16s( data, output, 2, 1 );
			case DxgiFormat.R16_UINT: return ReadUInt16s( data, output, 1, 1 );

			case DxgiFormat.R16G16B16A16_SNORM: return ReadInt16s( data, output, 4, normalize: true );
			case DxgiFormat.R16G16_SNORM: return ReadInt16s( data, output, 2, normalize: true );
			case DxgiFormat.R16_SNORM: return ReadInt16s( data, output, 1, normalize: true );
			case DxgiFormat.R16G16B16A16_SINT: return ReadInt16s( data, output, 4, normalize: false );
			case DxgiFormat.R16G16_SINT: return ReadInt16s( data, output, 2, normalize: false );
			case DxgiFormat.R16_SINT: return ReadInt16s( data, output, 1, normalize: false );

			case DxgiFormat.R8G8B8A8_UNORM:
			case DxgiFormat.R8G8B8A8_UNORM_SRGB: return ReadBytes( data, output, 4, 1 / 255f );
			case DxgiFormat.R8G8_UNORM: return ReadBytes( data, output, 2, 1 / 255f );
			case DxgiFormat.R8_UNORM: return ReadBytes( data, output, 1, 1 / 255f );
			case DxgiFormat.R8G8B8A8_UINT: return ReadBytes( data, output, 4, 1 );
			case DxgiFormat.R8G8_UINT: return ReadBytes( data, output, 2, 1 );
			case DxgiFormat.R8_UINT: return ReadBytes( data, output, 1, 1 );

			case DxgiFormat.R8G8B8A8_SNORM: return ReadSBytes( data, output, 4, normalize: true );
			case DxgiFormat.R8G8_SNORM: return ReadSBytes( data, output, 2, normalize: true );
			case DxgiFormat.R8_SNORM: return ReadSBytes( data, output, 1, normalize: true );
			case DxgiFormat.R8G8B8A8_SINT: return ReadSBytes( data, output, 4, normalize: false );
			case DxgiFormat.R8G8_SINT: return ReadSBytes( data, output, 2, normalize: false );
			case DxgiFormat.R8_SINT: return ReadSBytes( data, output, 1, normalize: false );

			case DxgiFormat.B8G8R8A8_UNORM:
				output[0] = data[2] / 255f;
				output[1] = data[1] / 255f;
				output[2] = data[0] / 255f;
				output[3] = data[3] / 255f;
				return 4;

			case DxgiFormat.R10G10B10A2_UNORM:
			case DxgiFormat.R10G10B10A2_UINT:
			{
				var packed = BinaryPrimitives.ReadUInt32LittleEndian( data );
				var normalize = field.Format == DxgiFormat.R10G10B10A2_UNORM;
				output[0] = (packed & 0x3FF) / (normalize ? 1023f : 1f);
				output[1] = ((packed >> 10) & 0x3FF) / (normalize ? 1023f : 1f);
				output[2] = ((packed >> 20) & 0x3FF) / (normalize ? 1023f : 1f);
				output[3] = (packed >> 30) / (normalize ? 3f : 1f);
				return 4;
			}

			default:
				throw new NotSupportedException( $"Vertex attribute {field.SemanticName} uses unsupported format {field.Format}." );
		}
	}

	private static int ReadFloats( ReadOnlySpan<byte> data, Span<float> output, int count )
	{
		for ( var i = 0; i < count; i++ )
			output[i] = BinaryPrimitives.ReadSingleLittleEndian( data[(i * 4)..] );

		return count;
	}

	private static int ReadHalves( ReadOnlySpan<byte> data, Span<float> output, int count )
	{
		for ( var i = 0; i < count; i++ )
			output[i] = (float)BinaryPrimitives.ReadHalfLittleEndian( data[(i * 2)..] );

		return count;
	}

	private static int ReadUInt32s( ReadOnlySpan<byte> data, Span<float> output, int count )
	{
		for ( var i = 0; i < count; i++ )
			output[i] = BinaryPrimitives.ReadUInt32LittleEndian( data[(i * 4)..] );

		return count;
	}

	private static int ReadInt32s( ReadOnlySpan<byte> data, Span<float> output, int count )
	{
		for ( var i = 0; i < count; i++ )
			output[i] = BinaryPrimitives.ReadInt32LittleEndian( data[(i * 4)..] );

		return count;
	}

	private static int ReadUInt16s( ReadOnlySpan<byte> data, Span<float> output, int count, float scale )
	{
		for ( var i = 0; i < count; i++ )
			output[i] = BinaryPrimitives.ReadUInt16LittleEndian( data[(i * 2)..] ) * scale;

		return count;
	}

	/// <summary>
	/// Signed normalized values map to [-1, 1]; the most negative integer clamps to -1 too.
	/// </summary>
	private static int ReadInt16s( ReadOnlySpan<byte> data, Span<float> output, int count, bool normalize )
	{
		for ( var i = 0; i < count; i++ )
		{
			float value = BinaryPrimitives.ReadInt16LittleEndian( data[(i * 2)..] );
			output[i] = normalize ? Math.Max( value / short.MaxValue, -1f ) : value;
		}

		return count;
	}

	private static int ReadBytes( ReadOnlySpan<byte> data, Span<float> output, int count, float scale )
	{
		for ( var i = 0; i < count; i++ )
			output[i] = data[i] * scale;

		return count;
	}

	private static int ReadSBytes( ReadOnlySpan<byte> data, Span<float> output, int count, bool normalize )
	{
		for ( var i = 0; i < count; i++ )
		{
			float value = (sbyte)data[i];
			output[i] = normalize ? Math.Max( value / sbyte.MaxValue, -1f ) : value;
		}

		return count;
	}

	/// <summary>
	/// Reads the index of a triangle corner from an index buffer.
	/// </summary>
	public int ReadIndex( int index )
	{
		return ElementSize == 2
			? BinaryPrimitives.ReadUInt16LittleEndian( Data.AsSpan( index * 2 ) )
			: (int)BinaryPrimitives.ReadUInt32LittleEndian( Data.AsSpan( index * 4 ) );
	}

	/// <summary>
	/// Reads the vertex and index buffers of a legacy VBIB or MBUF block.
	/// </summary>
	public static (List<MeshBuffer> Vertex, List<MeshBuffer> Index) ReadBufferBlock( ReadOnlySpan<byte> block )
	{
		var vertexOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian( block );
		var vertexCount = (int)BinaryPrimitives.ReadUInt32LittleEndian( block[4..] );
		var indexOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian( block[8..] );
		var indexCount = (int)BinaryPrimitives.ReadUInt32LittleEndian( block[12..] );

		var vertexBuffers = new List<MeshBuffer>( vertexCount );
		for ( var i = 0; i < vertexCount; i++ )
			vertexBuffers.Add( ReadBufferHeader( block, vertexOffset + i * 24, isVertex: true ) );

		// The index buffer offset is relative to its own field.
		var indexBuffers = new List<MeshBuffer>( indexCount );
		for ( var i = 0; i < indexCount; i++ )
			indexBuffers.Add( ReadBufferHeader( block, 8 + indexOffset + i * 24, isVertex: false ) );

		return (vertexBuffers, indexBuffers);
	}

	private static MeshBuffer ReadBufferHeader( ReadOnlySpan<byte> block, int position, bool isVertex )
	{
		var elementCount = (int)BinaryPrimitives.ReadUInt32LittleEndian( block[position..] );
		var sizeAndFlags = BinaryPrimitives.ReadInt32LittleEndian( block[(position + 4)..] );
		var elementSize = sizeAndFlags & 0x3FFFFFF;
		var isMeshopt = (sizeAndFlags & 0x4000000) == 0;
		var isZstd = (sizeAndFlags & 0x8000000) != 0;

		// Both offsets are relative to their own fields.
		var attributesPosition = position + 8 + (int)BinaryPrimitives.ReadUInt32LittleEndian( block[(position + 8)..] );
		var attributeCount = (int)BinaryPrimitives.ReadUInt32LittleEndian( block[(position + 12)..] );
		var dataPosition = position + 16 + (int)BinaryPrimitives.ReadUInt32LittleEndian( block[(position + 16)..] );
		var dataSize = BinaryPrimitives.ReadInt32LittleEndian( block[(position + 20)..] );

		var fields = new InputLayoutField[attributeCount];
		for ( var i = 0; i < attributeCount; i++ )
		{
			var attribute = block[(attributesPosition + i * 56)..];
			var nameLength = attribute[..32].IndexOf( (byte)0 );
			var name = Encoding.ASCII.GetString( attribute[..(nameLength < 0 ? 32 : nameLength)] ).ToUpperInvariant();

			fields[i] = new InputLayoutField(
				name,
				BinaryPrimitives.ReadInt32LittleEndian( attribute[32..] ),
				(DxgiFormat)BinaryPrimitives.ReadUInt32LittleEndian( attribute[36..] ),
				(int)BinaryPrimitives.ReadUInt32LittleEndian( attribute[40..] ),
				BinaryPrimitives.ReadInt32LittleEndian( attribute[44..] ) );
		}

		var raw = block.Slice( dataPosition, dataSize );
		var decodedSize = elementCount * elementSize;
		var data = decodedSize > dataSize
			? Decode( raw, elementCount, elementSize, isVertex, isZstd, isMeshopt )
			: raw.ToArray();

		return new MeshBuffer { ElementCount = elementCount, ElementSize = elementSize, Fields = fields, Data = data };
	}

	/// <summary>
	/// Reads a buffer described by KeyValues, as found in embedded meshes and newer mesh data.
	/// The data is either inline (<c>m_pData</c>) or in another block of the resource (<c>m_nBlockIndex</c>).
	/// </summary>
	public static MeshBuffer ReadDescribedBuffer( KVObject description, ResourceFile resource, bool isVertex )
	{
		var elementCount = description.GetInt32( "m_nElementCount" );
		var elementSize = description.GetInt32( "m_nElementSizeInBytes" );
		var decodedSize = elementCount * elementSize;

		var fields = description.GetObjects( "m_inputLayoutFields" ).Select( ReadField ).ToArray();

		byte[] data;

		if ( description.GetBlob( "m_pData" ) is { } inline )
		{
			data = inline.Length == decodedSize ? inline : Decode( inline, elementCount, elementSize, isVertex, false, true );
		}
		else
		{
			var raw = resource.GetBlockData( description.GetInt32( "m_nBlockIndex" ) );
			var isMeshopt = description.GetBool( "m_bMeshoptCompressed" );
			var isZstd = description.GetBool( "m_bCompressedZSTD" );

			data = isMeshopt || isZstd ? Decode( raw, elementCount, elementSize, isVertex, isZstd, isMeshopt ) : raw.ToArray();
		}

		if ( data.Length < decodedSize )
			throw new InvalidDataException( $"Mesh buffer has {data.Length} bytes, expected {decodedSize}." );

		return new MeshBuffer { ElementCount = elementCount, ElementSize = elementSize, Fields = fields, Data = data };
	}

	private static InputLayoutField ReadField( KVObject field )
	{
		var name = field["m_pSemanticName"] switch
		{
			string text => text,
			byte[] blob => Encoding.ASCII.GetString( blob ).TrimEnd( '\0' ),
			_ => "",
		};

		return new InputLayoutField(
			name.ToUpperInvariant(),
			field.GetInt32( "m_nSemanticIndex" ),
			ReadFormat( field["m_Format"] ),
			field.GetInt32( "m_nOffset" ),
			field.GetInt32( "m_nSlot" ) );
	}

	private static DxgiFormat ReadFormat( object value )
	{
		if ( value is string name )
		{
			name = name.StartsWith( "DXGI_FORMAT_", StringComparison.Ordinal ) ? name["DXGI_FORMAT_".Length..] : name;
			return Enum.TryParse<DxgiFormat>( name, out var parsed ) ? parsed : DxgiFormat.Unknown;
		}

		return value is null ? DxgiFormat.Unknown : (DxgiFormat)KVValue.ToInt64( value );
	}

	private static byte[] Decode( ReadOnlySpan<byte> raw, int elementCount, int elementSize, bool isVertex, bool isZstd, bool isMeshopt )
	{
		var decodedSize = elementCount * elementSize;
		byte[] decompressed = null;

		if ( isZstd )
		{
			decompressed = Zstd.Decompress( raw, decodedSize );
			raw = decompressed;
		}

		if ( !isMeshopt )
		{
			if ( raw.Length != decodedSize )
				throw new InvalidDataException( $"Mesh buffer decompressed to {raw.Length} bytes, expected {decodedSize}." );

			return decompressed ?? raw.ToArray();
		}

		return isVertex
			? MeshoptVertexDecoder.Decode( elementCount, elementSize, raw )
			: MeshoptIndexDecoder.Decode( elementCount, elementSize, raw );
	}
}
