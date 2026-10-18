using Microsoft.Win32.SafeHandles;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// A Valve Pak (VPK) directory and the archive chunks it indexes. Reads are positional
/// and thread safe, so loaders can pull files out in parallel.
/// </summary>
internal sealed class VpkArchive : IDisposable
{
	private const uint Signature = 0x55AA1234;
	private const ushort EmbeddedArchiveIndex = 0x7FFF;

	private readonly record struct Entry( ushort ArchiveIndex, uint Offset, uint Length, int PreloadOffset, ushort PreloadLength );

	private readonly string _archivePathFormat;
	private readonly byte[] _tree;
	private readonly long _embeddedDataOffset;
	private readonly Dictionary<string, Entry> _entries = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<ushort, SafeFileHandle> _archives = [];
	private readonly Lock _archiveLock = new();
	private bool _disposed;

	/// <summary>
	/// The path of the directory file this archive was opened from.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Every file path in the archive, using forward slashes.
	/// </summary>
	public IEnumerable<string> Files => _entries.Keys;

	public int Count => _entries.Count;

	public VpkArchive( string path )
	{
		FilePath = path;

		var suffixLength = path.EndsWith( "_dir.vpk", StringComparison.OrdinalIgnoreCase ) ? 8 : 4;
		_archivePathFormat = path[..^suffixLength] + "_{0:000}.vpk";

		using var stream = File.OpenRead( path );
		using var reader = new BinaryReader( stream );

		if ( reader.ReadUInt32() != Signature )
			throw new InvalidDataException( $"'{path}' is not a VPK." );

		var version = reader.ReadUInt32();
		if ( version is not (1 or 2) )
			throw new InvalidDataException( $"'{path}' has unsupported VPK version {version}." );

		var treeSize = reader.ReadUInt32();
		if ( version == 2 )
			stream.Position += 16; // file data, archive md5, other md5 and signature section sizes

		if ( treeSize == 0 || stream.Position + treeSize > stream.Length )
			throw new InvalidDataException( $"'{path}' has an invalid directory tree size." );

		_tree = GC.AllocateUninitializedArray<byte>( (int)treeSize );
		stream.ReadExactly( _tree );
		_embeddedDataOffset = stream.Position;

		ReadTree();
	}

	private void ReadTree()
	{
		var position = 0;

		while ( true )
		{
			var extension = ReadString( ref position );
			if ( extension.Length == 0 )
				break;

			var suffix = extension == " " ? "" : "." + extension;

			while ( true )
			{
				var directory = ReadString( ref position );
				if ( directory.Length == 0 )
					break;

				var prefix = directory == " " ? "" : directory.Replace( '\\', '/' ).Trim( '/' ) + "/";

				while ( true )
				{
					var name = ReadString( ref position );
					if ( name.Length == 0 )
						break;

					var entry = _tree.AsSpan( position, 18 );
					position += 18;

					// The first four bytes are the CRC32 of the whole file.
					var preloadLength = BinaryPrimitives.ReadUInt16LittleEndian( entry[4..] );
					var archiveIndex = BinaryPrimitives.ReadUInt16LittleEndian( entry[6..] );
					var offset = BinaryPrimitives.ReadUInt32LittleEndian( entry[8..] );
					var length = BinaryPrimitives.ReadUInt32LittleEndian( entry[12..] );

					if ( BinaryPrimitives.ReadUInt16LittleEndian( entry[16..] ) != 0xFFFF )
						throw new InvalidDataException( $"'{FilePath}' has a corrupt directory entry." );

					_entries[prefix + name + suffix] = new Entry( archiveIndex, offset, length, position, preloadLength );
					position += preloadLength;
				}
			}
		}
	}

	private string ReadString( ref int position )
	{
		var length = _tree.AsSpan( position ).IndexOf( (byte)0 );
		if ( length < 0 )
			throw new InvalidDataException( $"'{FilePath}' has an unterminated directory string." );

		var value = Encoding.UTF8.GetString( _tree, position, length );
		position += length + 1;
		return value;
	}

	public bool Contains( string path ) => _entries.ContainsKey( path );

	/// <summary>
	/// Reads a file out of the archive, or only its first <paramref name="maxLength"/> bytes.
	/// </summary>
	public byte[] ReadFile( string path, int maxLength = int.MaxValue )
	{
		if ( !_entries.TryGetValue( path, out var entry ) )
			throw new FileNotFoundException( $"'{path}' is not in '{FilePath}'.", path );

		var length = (int)Math.Min( (long)entry.PreloadLength + entry.Length, maxLength );
		var preloadLength = Math.Min( (int)entry.PreloadLength, length );

		var data = GC.AllocateUninitializedArray<byte>( length );
		_tree.AsSpan( entry.PreloadOffset, preloadLength ).CopyTo( data );

		if ( length == preloadLength )
			return data;

		var handle = GetArchive( entry.ArchiveIndex );
		var offset = (long)entry.Offset + (entry.ArchiveIndex == EmbeddedArchiveIndex ? _embeddedDataOffset : 0);
		var destination = data.AsSpan( preloadLength );

		while ( !destination.IsEmpty )
		{
			var read = RandomAccess.Read( handle, destination, offset );
			if ( read <= 0 )
				throw new EndOfStreamException( $"'{path}' is truncated in '{FilePath}'." );

			destination = destination[read..];
			offset += read;
		}

		return data;
	}

	private SafeFileHandle GetArchive( ushort index )
	{
		lock ( _archiveLock )
		{
			ObjectDisposedException.ThrowIf( _disposed, this );

			if ( !_archives.TryGetValue( index, out var handle ) )
			{
				var path = index == EmbeddedArchiveIndex ? FilePath : string.Format( _archivePathFormat, index );
				handle = File.OpenHandle( path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess );
				_archives.Add( index, handle );
			}

			return handle;
		}
	}

	public void Dispose()
	{
		lock ( _archiveLock )
		{
			if ( _disposed )
				return;

			_disposed = true;

			foreach ( var handle in _archives.Values )
				handle.Dispose();

			_archives.Clear();
		}
	}
}
