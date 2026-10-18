using System;
using System.Buffers.Binary;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Pixel formats of compiled textures. Values match VTEX_FORMAT.
/// </summary>
internal enum VTexFormat : byte
{
	Unknown = 0,
	DXT1 = 1,
	DXT5 = 2,
	I8 = 3,
	RGBA8888 = 4,
	R16 = 5,
	RG1616 = 6,
	RGBA16161616 = 7,
	R16F = 8,
	RG1616F = 9,
	RGBA16161616F = 10,
	R32F = 11,
	RG3232F = 12,
	RGB323232F = 13,
	RGBA32323232F = 14,
	JPEG_RGBA8888 = 15,
	PNG_RGBA8888 = 16,
	JPEG_DXT5 = 17,
	PNG_DXT5 = 18,
	BC6H = 19,
	BC7 = 20,
	ATI2N = 21,
	IA88 = 22,
	ETC2 = 23,
	ETC2_EAC = 24,
	R11_EAC = 25,
	RG11_EAC = 26,
	ATI1N = 27,
	BGRA8888 = 28,
	WEBP_RGBA8888 = 29,
	WEBP_DXT5 = 30,
	R32_UINT = 31,
	A8 = 32,
	R8_UNORM = 33,
}

[Flags]
internal enum VTexFlags : ushort
{
	SuggestClampS = 1 << 0,
	SuggestClampT = 1 << 1,
	SuggestClampU = 1 << 2,
	NoLod = 1 << 3,
	CubeTexture = 1 << 4,
	VolumeTexture = 1 << 5,
	TextureArray = 1 << 6,
}

/// <summary>
/// A compiled texture (vtex_c). The pixel data follows the DATA block, every mip of every face or
/// slice, smallest mip first. Each mip may be compressed on its own.
/// </summary>
internal sealed class CompiledTexture
{
	private enum ExtraData : uint
	{
		Sheet = 2,
		Metadata = 3,
		CompressedMipSize = 4,
		CubemapRadianceSH = 5,
	}

	private enum MipCompression
	{
		None = 0,
		Lz4 = 1,
		Zstd = 2,
	}

	private readonly ResourceFile _resource;
	private readonly int _dataOffset;
	private readonly MipCompression _mipCompression;
	private readonly int[] _compressedMipSizes;

	public VTexFlags Flags { get; }
	public VTexFormat Format { get; }
	public int Width { get; }
	public int Height { get; }

	/// <summary>
	/// Slices of a volume texture, or array layers. One for plain 2D textures and cubemaps.
	/// </summary>
	public int Depth { get; }

	public int MipCount { get; }

	/// <summary>
	/// Average color, used by the engine for reflectivity estimates.
	/// </summary>
	public float[] Reflectivity { get; }

	/// <summary>
	/// The authored size when it was padded up to the compiled size, otherwise the compiled size.
	/// </summary>
	public int DisplayWidth { get; }
	public int DisplayHeight { get; }

	/// <summary>
	/// A baked cubemap's radiance as second order spherical harmonics: 9 coefficients per colour channel,
	/// channels planar, for each cube of an array in turn. Null for other textures.
	/// </summary>
	public float[] RadianceCoefficients { get; }

	/// <summary>
	/// The sprite sheet particles animate through, or null when the texture has none.
	/// </summary>
	public TextureSheet Sheet { get; }

	/// <summary>
	/// For a motion vector sheet, how far its texels move at most, in texels.
	/// </summary>
	public int MotionVectorsMaxDistance { get; }

	public bool IsCube => (Flags & VTexFlags.CubeTexture) != 0;
	public bool IsVolume => (Flags & VTexFlags.VolumeTexture) != 0;
	public bool IsArray => (Flags & VTexFlags.TextureArray) != 0;

	public bool IsBlockCompressed => Format is VTexFormat.DXT1 or VTexFormat.DXT5 or VTexFormat.BC6H or VTexFormat.BC7
		or VTexFormat.ATI1N or VTexFormat.ATI2N or VTexFormat.ETC2 or VTexFormat.ETC2_EAC or VTexFormat.R11_EAC or VTexFormat.RG11_EAC;

	/// <summary>
	/// Bytes per pixel, or per 4x4 block for block compressed formats.
	/// </summary>
	public int BlockSize => Format switch
	{
		VTexFormat.DXT1 or VTexFormat.ATI1N or VTexFormat.ETC2 or VTexFormat.R11_EAC => 8,
		VTexFormat.DXT5 or VTexFormat.BC6H or VTexFormat.BC7 or VTexFormat.ATI2N or VTexFormat.ETC2_EAC or VTexFormat.RG11_EAC => 16,
		VTexFormat.RGBA8888 or VTexFormat.BGRA8888 or VTexFormat.RG1616 or VTexFormat.RG1616F or VTexFormat.R32F or VTexFormat.R32_UINT => 4,
		VTexFormat.R16 or VTexFormat.R16F or VTexFormat.IA88 => 2,
		VTexFormat.RGBA16161616 or VTexFormat.RGBA16161616F or VTexFormat.RG3232F => 8,
		VTexFormat.RGB323232F => 12,
		VTexFormat.RGBA32323232F => 16,
		_ => 1,
	};

	public CompiledTexture( byte[] data )
	{
		_resource = new ResourceFile( data );

		if ( _resource.Version != 1 )
			throw new NotSupportedException( $"Texture resource version {_resource.Version} is not supported." );

		var block = _resource.FindBlock( "DATA" ) ?? throw new InvalidDataException( "Texture has no DATA block." );
		var header = _resource.GetBlockData( block );
		_dataOffset = block.Offset + block.Size;

		var version = BinaryPrimitives.ReadUInt16LittleEndian( header );
		if ( version != 1 )
			throw new NotSupportedException( $"Texture header version {version} is not supported." );

		Flags = (VTexFlags)BinaryPrimitives.ReadUInt16LittleEndian( header[2..] );
		Reflectivity =
		[
			BinaryPrimitives.ReadSingleLittleEndian( header[4..] ),
			BinaryPrimitives.ReadSingleLittleEndian( header[8..] ),
			BinaryPrimitives.ReadSingleLittleEndian( header[12..] ),
			BinaryPrimitives.ReadSingleLittleEndian( header[16..] ),
		];
		Width = BinaryPrimitives.ReadUInt16LittleEndian( header[20..] );
		Height = BinaryPrimitives.ReadUInt16LittleEndian( header[22..] );
		Depth = Math.Max( (int)BinaryPrimitives.ReadUInt16LittleEndian( header[24..] ), 1 );
		Format = (VTexFormat)header[26];
		MipCount = Math.Max( (int)header[27], 1 );

		DisplayWidth = Width;
		DisplayHeight = Height;

		// The extra data table offset is relative to its own field.
		var extraOffset = 32 + (int)BinaryPrimitives.ReadUInt32LittleEndian( header[32..] );
		var extraCount = (int)BinaryPrimitives.ReadUInt32LittleEndian( header[36..] );

		for ( var i = 0; i < extraCount; i++ )
		{
			var entry = extraOffset + i * 12;
			var type = (ExtraData)BinaryPrimitives.ReadUInt32LittleEndian( header[entry..] );
			var offset = entry + 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian( header[(entry + 4)..] );
			var size = (int)BinaryPrimitives.ReadUInt32LittleEndian( header[(entry + 8)..] );
			var extra = header[offset..];

			switch ( type )
			{
				case ExtraData.Metadata:
				{
					var displayWidth = BinaryPrimitives.ReadUInt16LittleEndian( extra[2..] );
					var displayHeight = BinaryPrimitives.ReadUInt16LittleEndian( extra[4..] );

					if ( displayWidth > 0 && displayHeight > 0 && displayWidth <= Width && displayHeight <= Height )
					{
						DisplayWidth = displayWidth;
						DisplayHeight = displayHeight;
					}

					MotionVectorsMaxDistance = BinaryPrimitives.ReadInt16LittleEndian( extra[6..] );
					break;
				}

				case ExtraData.Sheet:
					Sheet = TextureSheet.Read( extra[..size] );
					break;

				case ExtraData.CompressedMipSize:
				{
					_mipCompression = (MipCompression)BinaryPrimitives.ReadUInt32LittleEndian( extra );
					var sizesOffset = 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian( extra[4..] );
					var count = (int)BinaryPrimitives.ReadUInt32LittleEndian( extra[8..] );

					_compressedMipSizes = new int[count];
					for ( var mip = 0; mip < count; mip++ )
						_compressedMipSizes[mip] = BinaryPrimitives.ReadInt32LittleEndian( extra[(sizesOffset + mip * 4)..] );

					break;
				}

				case ExtraData.CubemapRadianceSH:
				{
					var coefficientsOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian( extra );
					var count = (int)BinaryPrimitives.ReadUInt32LittleEndian( extra[4..] );

					RadianceCoefficients = new float[count];
					for ( var c = 0; c < count; c++ )
						RadianceCoefficients[c] = BinaryPrimitives.ReadSingleLittleEndian( extra[(coefficientsOffset + c * 4)..] );

					break;
				}
			}
		}
	}

	/// <summary>
	/// Faces or slices stored per mip: 6 per cube, times the depth or array size.
	/// </summary>
	private int GetImageCount( int mip )
	{
		var depth = IsVolume ? Math.Max( Depth >> mip, 1 ) : Depth;
		return IsCube ? depth * 6 : depth;
	}

	/// <summary>
	/// The decoded size of one mip level, covering every face and slice.
	/// </summary>
	public int GetMipSize( int mip )
	{
		var width = Math.Max( Width >> mip, 1 );
		var height = Math.Max( Height >> mip, 1 );

		if ( IsBlockCompressed )
		{
			// Blocks are 4x4, so small mips round up to a whole block.
			width = (width + 3) / 4;
			height = (height + 3) / 4;
		}

		return width * height * GetImageCount( mip ) * BlockSize;
	}

	/// <summary>
	/// Whether the pixel data is a whole PNG, JPEG or WebP file rather than a mip chain.
	/// </summary>
	public bool IsRawImage => Format is VTexFormat.PNG_RGBA8888 or VTexFormat.PNG_DXT5 or VTexFormat.JPEG_RGBA8888
		or VTexFormat.JPEG_DXT5 or VTexFormat.WEBP_RGBA8888 or VTexFormat.WEBP_DXT5;

	/// <summary>
	/// The embedded image file of a <see cref="IsRawImage"/> texture.
	/// </summary>
	public byte[] ReadRawImage() => _resource.Data.AsSpan( _dataOffset ).ToArray();

	/// <summary>
	/// Reads <paramref name="count"/> mips starting at <paramref name="firstMip"/>, smallest first, which is
	/// the layout the engine's texture builders take.
	/// </summary>
	public byte[] ReadMips( int firstMip = 0, int count = int.MaxValue )
	{
		var endMip = (int)Math.Min( (long)firstMip + count, MipCount );

		var total = 0;
		for ( var mip = firstMip; mip < endMip; mip++ )
			total += GetMipSize( mip );

		var output = GC.AllocateUninitializedArray<byte>( total );
		var source = _dataOffset;
		var destination = 0;

		for ( var mip = MipCount - 1; mip >= 0; mip-- )
		{
			var size = GetMipSize( mip );
			var stored = _compressedMipSizes is not null && mip < _compressedMipSizes.Length
				? Math.Min( _compressedMipSizes[mip], size )
				: size;

			if ( mip >= firstMip && mip < endMip )
			{
				DecodeMip( _resource.Data.AsSpan( source, stored ), output.AsSpan( destination, size ) );
				destination += size;
			}

			source += stored;
		}

		return output;
	}

	private void DecodeMip( ReadOnlySpan<byte> stored, Span<byte> output )
	{
		// Mips that wouldn't shrink are stored as they are.
		if ( stored.Length == output.Length || _mipCompression == MipCompression.None )
		{
			stored.CopyTo( output );
			return;
		}

		switch ( _mipCompression )
		{
			case MipCompression.Lz4:
				if ( Lz4.Decode( stored, output ) != output.Length )
					throw new InvalidDataException( "Texture mip decoded to the wrong size." );
				break;

			case MipCompression.Zstd:
				Zstd.Decompress( stored, output );
				break;

			default:
				throw new NotSupportedException( $"Texture mip compression {_mipCompression} is not supported." );
		}
	}
}
