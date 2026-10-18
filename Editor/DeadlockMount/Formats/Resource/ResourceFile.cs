using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// A block inside a compiled resource, such as DATA, CTRL or MVTX.
/// </summary>
internal readonly record struct ResourceBlock( string Type, int Offset, int Size );

/// <summary>
/// A compiled Source 2 resource (a file ending in <c>_c</c>): a small header followed by typed blocks.
/// </summary>
internal sealed class ResourceFile
{
	private const ushort KnownHeaderVersion = 12;

	/// <summary>
	/// The whole file. Blocks are slices of this.
	/// </summary>
	public byte[] Data { get; }

	/// <summary>
	/// The resource type specific version.
	/// </summary>
	public ushort Version { get; }

	public IReadOnlyList<ResourceBlock> Blocks { get; }

	public ResourceFile( byte[] data )
	{
		Data = data;

		if ( data.Length < 16 )
			throw new InvalidDataException( "Resource is too small for its header." );

		var header = data.AsSpan();
		var headerVersion = BinaryPrimitives.ReadUInt16LittleEndian( header[4..] );

		if ( headerVersion != KnownHeaderVersion )
			throw new InvalidDataException( $"Resource has unexpected header version {headerVersion}." );

		Version = BinaryPrimitives.ReadUInt16LittleEndian( header[6..] );

		// The block table offset is relative to its own field.
		var position = 8 + (int)BinaryPrimitives.ReadUInt32LittleEndian( header[8..] );
		var blockCount = (int)BinaryPrimitives.ReadUInt32LittleEndian( header[12..] );
		var blocks = new ResourceBlock[blockCount];

		for ( var i = 0; i < blockCount; i++ )
		{
			if ( position + 12 > data.Length )
				throw new InvalidDataException( "Resource block table is truncated." );

			var type = Encoding.ASCII.GetString( data, position, 4 );
			var offset = position + 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian( header[(position + 4)..] );
			var size = (int)BinaryPrimitives.ReadUInt32LittleEndian( header[(position + 8)..] );

			if ( size > 0 && (offset < 0 || (long)offset + size > data.Length) )
				throw new InvalidDataException( $"Resource block {type} extends past the end of the file." );

			blocks[i] = new ResourceBlock( type, offset, size );
			position += 12;
		}

		Blocks = blocks;
	}

	public ReadOnlySpan<byte> GetBlockData( ResourceBlock block ) => Data.AsSpan( block.Offset, block.Size );

	public ReadOnlySpan<byte> GetBlockData( int index ) => GetBlockData( Blocks[index] );

	/// <summary>
	/// Finds the first block of a type, or null.
	/// </summary>
	public ResourceBlock? FindBlock( string type )
	{
		foreach ( var block in Blocks )
		{
			if ( block.Type == type )
				return block;
		}

		return null;
	}

	/// <summary>
	/// Reads a KeyValues3 block by index.
	/// </summary>
	public KVObject ReadKeyValues( int index ) => KV3Reader.Read( GetBlockData( index ) );

	/// <summary>
	/// Reads the first KeyValues3 block of a type, or null if there is none.
	/// </summary>
	public KVObject ReadKeyValues( string type )
	{
		return FindBlock( type ) is { } block ? KV3Reader.Read( GetBlockData( block ) ) : null;
	}

	/// <summary>
	/// The KeyValues3 format of the first block of a type, or empty if there is none.
	/// </summary>
	public Guid ReadKeyValuesFormat( string type )
	{
		return FindBlock( type ) is { } block ? KV3Reader.ReadFormat( GetBlockData( block ) ) : Guid.Empty;
	}
}
