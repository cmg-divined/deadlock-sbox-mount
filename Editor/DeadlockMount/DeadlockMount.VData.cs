using System;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

public sealed partial class DeadlockMount
{
	private Dictionary<string, KVObject> _vdata;
	private readonly Dictionary<string, KVObject> _resolvedVData = new( StringComparer.OrdinalIgnoreCase );

	private void ClearVData()
	{
		lock ( _resolvedVData )
		{
			_vdata = null;
			_resolvedVData.Clear();
		}
	}

	/// <summary>
	/// A VData entry by name, with what it inherits through <c>_base</c> merged in, or null. Entity subclasses
	/// live here: a <c>citadel_breakable_prop</c> names its kind in <c>subclass_name</c> and the model comes from VData.
	/// </summary>
	internal KVObject GetVData( string entryName )
	{
		if ( string.IsNullOrEmpty( entryName ) )
			return null;

		lock ( _resolvedVData )
		{
			_vdata ??= ReadAllVData();
			return Resolve( entryName, 0 );
		}
	}

	private KVObject Resolve( string entryName, int depth )
	{
		if ( _resolvedVData.TryGetValue( entryName, out var cached ) )
			return cached;

		if ( depth > 16 || !_vdata.TryGetValue( entryName, out var entry ) )
			return null;

		var result = new KVObject();

		if ( entry.GetString( "_base" ) is { Length: > 0 } baseName && Resolve( baseName, depth + 1 ) is { } inherited )
		{
			foreach ( var key in inherited.Keys )
				result[key] = inherited[key];
		}

		foreach ( var key in entry.Keys )
			result[key] = entry[key];

		_resolvedVData[entryName] = result;
		return result;
	}

	/// <summary>
	/// Every entry of every scripts/*.vdata_c, earlier search paths winning.
	/// </summary>
	private Dictionary<string, KVObject> ReadAllVData()
	{
		var entries = new Dictionary<string, KVObject>( StringComparer.OrdinalIgnoreCase );

		foreach ( var path in _files.Keys.Where( x => x.StartsWith( "scripts/", StringComparison.OrdinalIgnoreCase ) && x.EndsWith( ".vdata_c", StringComparison.OrdinalIgnoreCase ) ).ToArray() )
		{
			try
			{
				if ( new ResourceFile( ReadFile( path ) ).ReadKeyValues( "DATA" ) is not { } data )
					continue;

				foreach ( var key in data.Keys )
				{
					if ( data[key] is KVObject entry )
						entries.TryAdd( key, entry );
				}
			}
			catch ( Exception e ) when ( e is InvalidDataException or IOException or NotSupportedException )
			{
				Log.Trace( $"Couldn't read VData '{path}': {e.Message}" );
			}
		}

		return entries;
	}
}
