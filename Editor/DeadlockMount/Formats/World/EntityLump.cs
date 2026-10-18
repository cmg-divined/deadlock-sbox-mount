using System;
using System.Globalization;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// An entity output: when the entity fires <see cref="Output"/>, call <see cref="Input"/> on <see cref="Target"/>.
/// </summary>
internal sealed record EntityConnection( string Output, string Target, string Input, string Parameter, float Delay, int TimesToFire );

/// <summary>
/// One map entity: its key values (KV3 typed values, or strings in older maps) and outputs.
/// </summary>
internal sealed class MapEntity
{
	public Dictionary<string, object> Values { get; }
	public EntityConnection[] Connections { get; }

	/// <summary>
	/// The lump the entity came from, e.g. <c>default_ents</c>.
	/// </summary>
	public string Lump { get; }

	public MapEntity( Dictionary<string, object> values, EntityConnection[] connections, string lump )
	{
		Values = values;
		Connections = connections;
		Lump = lump;
	}

	public string Classname => GetString( "classname" ) ?? "";
	public string TargetName => GetString( "targetname" ) ?? "";

	public bool Has( string key ) => Values.ContainsKey( key );

	public string GetString( string key, string fallback = null )
	{
		return Values.TryGetValue( key, out var value ) && value is not null ? FormatValue( value ) : fallback;
	}

	public float GetFloat( string key, float fallback = 0 )
	{
		if ( !Values.TryGetValue( key, out var value ) || value is null )
			return fallback;

		if ( value is string text )
			return float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed ) ? parsed : fallback;

		return value is object[] or KVObject ? fallback : KVValue.ToFloat( value );
	}

	public int GetInt( string key, int fallback = 0 )
	{
		if ( !Values.TryGetValue( key, out var value ) || value is null )
			return fallback;

		if ( value is string text )
			return int.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed ) ? parsed
				: float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) ? (int)number : fallback;

		return value is object[] or KVObject ? fallback : (int)KVValue.ToInt64( value );
	}

	public bool GetBool( string key, bool fallback = false )
	{
		if ( !Values.TryGetValue( key, out var value ) || value is null )
			return fallback;

		return value switch
		{
			bool flag => flag,
			string text when bool.TryParse( text, out var parsed ) => parsed,
			string text => GetInt( key, fallback ? 1 : 0 ) != 0,
			object[] or KVObject => fallback,
			_ => KVValue.ToInt64( value ) != 0,
		};
	}

	/// <summary>
	/// A vector, stored as a KV3 array or as space separated numbers.
	/// </summary>
	public float[] GetVector( string key, int count, float[] fallback = null )
	{
		if ( !Values.TryGetValue( key, out var value ) || value is null )
			return fallback;

		var result = new float[count];

		switch ( value )
		{
			case object[] array:
				for ( var i = 0; i < count && i < array.Length; i++ )
					result[i] = KVValue.ToFloat( array[i] );
				return result;

			case string text:
				var parts = text.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
				if ( parts.Length == 0 )
					return fallback;

				for ( var i = 0; i < count && i < parts.Length; i++ )
					float.TryParse( parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i] );
				return result;

			default:
				return fallback;
		}
	}

	/// <summary>
	/// A value as Hammer shows it: numbers in invariant culture, arrays space separated.
	/// </summary>
	public static string FormatValue( object value ) => value switch
	{
		string text => text,
		bool flag => flag ? "1" : "0",
		float number => number.ToString( CultureInfo.InvariantCulture ),
		double number => number.ToString( CultureInfo.InvariantCulture ),
		object[] array => string.Join( " ", array.Select( FormatValue ) ),
		KVObject => "",
		IFormattable formattable => formattable.ToString( null, CultureInfo.InvariantCulture ),
		_ => value?.ToString() ?? "",
	};
}

/// <summary>
/// An entity lump (vents_c): entities, and child lumps holding more of them.
/// </summary>
internal sealed class EntityLump
{
	public string Name { get; }
	public string[] ChildLumps { get; }
	public MapEntity[] Entities { get; }

	/// <summary>
	/// Entities stored in the pre-KV3 binary format, which this reader doesn't decode.
	/// </summary>
	public int LegacyEntities { get; }

	public EntityLump( byte[] data )
	{
		var lump = new ResourceFile( data ).ReadKeyValues( "DATA" ) ?? throw new InvalidDataException( "Entity lump has no DATA block." );

		Name = lump.GetString( "m_name", "" );
		ChildLumps = lump.GetStringArray( "m_childLumps" ).Select( CompiledWorld.NormalizePath ).ToArray();

		var entities = new List<MapEntity>();
		foreach ( var entity in lump.GetObjects( "m_entityKeyValues" ) )
		{
			if ( entity.GetObject( "keyValues3Data" )?.GetObject( "values" ) is not { } values )
			{
				LegacyEntities++;
				continue;
			}

			var keyValues = new Dictionary<string, object>( StringComparer.OrdinalIgnoreCase );
			foreach ( var key in values.Keys )
				keyValues[key] = values[key];

			var connections = entity.GetObjects( "m_connections" ).Select( x => new EntityConnection(
				x.GetString( "m_outputName", "" ),
				x.GetString( "m_targetName", "" ),
				x.GetString( "m_inputName", "" ),
				x.GetString( "m_overrideParam", "" ),
				x.GetFloat( "m_flDelay" ),
				x.GetInt32( "m_nTimesToFire", -1 ) ) ).ToArray();

			entities.Add( new MapEntity( keyValues, connections, Name ) );
		}

		Entities = [.. entities];
	}
}
