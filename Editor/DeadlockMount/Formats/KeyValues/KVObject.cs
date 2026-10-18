using System;
using System.Globalization;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// A KeyValues3 object. Values are null, bool, a boxed number, string, byte[] (blobs),
/// <see cref="KVObject"/> or object[] (arrays).
/// </summary>
internal sealed class KVObject
{
	private readonly Dictionary<string, object> _values;

	public KVObject( int capacity = 0 )
	{
		_values = new Dictionary<string, object>( capacity );
	}

	public object this[string key]
	{
		get => _values.GetValueOrDefault( key );
		set => _values[key] = value;
	}

	public int Count => _values.Count;

	public IEnumerable<string> Keys => _values.Keys;

	public bool ContainsKey( string key ) => _values.ContainsKey( key );

	public string GetString( string key, string fallback = null ) => this[key] as string ?? fallback;

	public KVObject GetObject( string key ) => this[key] as KVObject;

	public object[] GetArray( string key ) => this[key] as object[];

	public byte[] GetBlob( string key ) => this[key] as byte[];

	public long GetInt64( string key, long fallback = 0 ) => this[key] is { } value ? KVValue.ToInt64( value ) : fallback;

	public int GetInt32( string key, int fallback = 0 ) => (int)GetInt64( key, fallback );

	public float GetFloat( string key, float fallback = 0 ) => this[key] is { } value ? KVValue.ToFloat( value ) : fallback;

	public bool GetBool( string key, bool fallback = false ) => this[key] is { } value ? KVValue.ToBool( value ) : fallback;

	/// <summary>
	/// The objects in an array, skipping anything that is not an object. Empty if the key is missing.
	/// </summary>
	public IEnumerable<KVObject> GetObjects( string key ) => GetArray( key )?.OfType<KVObject>() ?? [];

	public long[] GetInt64Array( string key ) => GetArray( key )?.Select( KVValue.ToInt64 ).ToArray() ?? [];

	public float[] GetFloatArray( string key ) => GetArray( key )?.Select( KVValue.ToFloat ).ToArray() ?? [];

	public string[] GetStringArray( string key ) => GetArray( key )?.Select( x => x as string ?? "" ).ToArray() ?? [];
}

/// <summary>
/// Conversions for loosely typed KeyValues3 values.
/// </summary>
internal static class KVValue
{
	/// <summary>
	/// Converts to a signed integer. Unsigned 64 bit values keep their bits, so masks round trip.
	/// </summary>
	public static long ToInt64( object value ) => value switch
	{
		ulong u => unchecked((long)u),
		bool b => b ? 1 : 0,
		string s => long.Parse( s, CultureInfo.InvariantCulture ),
		IConvertible convertible => convertible.ToInt64( CultureInfo.InvariantCulture ),
		_ => throw new InvalidCastException( $"KeyValues value of type {value?.GetType().Name ?? "null"} is not a number." ),
	};

	public static float ToFloat( object value ) => value switch
	{
		bool b => b ? 1 : 0,
		string s => float.Parse( s, CultureInfo.InvariantCulture ),
		IConvertible convertible => convertible.ToSingle( CultureInfo.InvariantCulture ),
		_ => throw new InvalidCastException( $"KeyValues value of type {value?.GetType().Name ?? "null"} is not a number." ),
	};

	public static bool ToBool( object value ) => value switch
	{
		bool b => b,
		string s => s.Equals( "true", StringComparison.OrdinalIgnoreCase ) || s == "1",
		IConvertible convertible => convertible.ToDouble( CultureInfo.InvariantCulture ) != 0,
		_ => false,
	};
}
