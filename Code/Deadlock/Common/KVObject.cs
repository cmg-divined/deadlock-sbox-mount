using System;
using System.Collections;
using System.Globalization;

namespace Deadlock;

/// <summary>
/// The type of value a <see cref="KVObject"/> holds.
/// </summary>
public enum KVValueType
{
	Null,
	Collection,
	Array,
	BinaryBlob,
	Boolean,
	String,
	Int16,
	Int32,
	Int64,
	UInt16,
	UInt32,
	UInt64,
	FloatingPoint,
	FloatingPoint64,
	Pointer,
}

/// <summary>
/// The KV3 flag a value was written with (resource references and the like).
/// </summary>
public enum KVFlag
{
	None,
	Resource,
	ResourceName,
	Panorama,
	SoundEvent,
	SubClass,
	EntityName,
	Localize,
}

/// <summary>
/// A KeyValues3 node: a collection of named children, an array, or a single value. Particle definitions arrive as
/// trees of these, built by the mount from the compiled files.
/// </summary>
/// <remarks>
/// Mirrors the part of ValveKeyValue's <c>KVObject</c> that ValveResourceFormat's particle code uses, so that code
/// runs unchanged. Collections keep their members in order.
/// </remarks>
public sealed class KVObject : IEnumerable<KeyValuePair<string, KVObject>>
{
	private readonly object _value;
	private readonly List<string> _names;
	private readonly List<KVObject> _items;

	public KVValueType ValueType { get; }

	public KVFlag Flag { get; set; }

	private KVObject( KVValueType type, object value, int capacity = 0 )
	{
		ValueType = type;
		_value = value;

		if ( type == KVValueType.Collection )
		{
			_names = new List<string>( capacity );
			_items = new List<KVObject>( capacity );
		}
		else if ( type == KVValueType.Array )
		{
			_items = new List<KVObject>( capacity );
		}
	}

	public KVObject() : this( KVValueType.Collection, null ) { }
	public KVObject( string value ) : this( value is null ? KVValueType.Null : KVValueType.String, value ) { }
	public KVObject( bool value ) : this( KVValueType.Boolean, value ) { }
	public KVObject( short value ) : this( KVValueType.Int16, value ) { }
	public KVObject( ushort value ) : this( KVValueType.UInt16, value ) { }
	public KVObject( int value ) : this( KVValueType.Int32, value ) { }
	public KVObject( uint value ) : this( KVValueType.UInt32, value ) { }
	public KVObject( long value ) : this( KVValueType.Int64, value ) { }
	public KVObject( ulong value ) : this( KVValueType.UInt64, value ) { }
	public KVObject( float value ) : this( KVValueType.FloatingPoint, value ) { }
	public KVObject( double value ) : this( KVValueType.FloatingPoint64, value ) { }

	public static KVObject Null() => new( KVValueType.Null, null );
	public static KVObject Blob( byte[] data ) => new( KVValueType.BinaryBlob, data ?? [] );

	public static KVObject Array() => new( KVValueType.Array, null );
	public static KVObject Array( int capacity ) => new( KVValueType.Array, null, capacity );

	public static KVObject Array( IEnumerable<KVObject> elements )
	{
		var array = Array();
		foreach ( var element in elements )
			array.Add( element );

		return array;
	}

	public static KVObject Collection() => new( KVValueType.Collection, null );
	public static KVObject Collection( int capacity ) => new( KVValueType.Collection, null, capacity );

	public static KVObject Collection( IEnumerable<KeyValuePair<string, KVObject>> children )
	{
		var collection = Collection();
		foreach ( var (name, child) in children )
			collection.Add( name, child );

		return collection;
	}

	public static KVObject ListCollection() => Collection();
	public static KVObject ListCollection( int capacity ) => Collection( capacity );
	public static KVObject ListCollection( IEnumerable<KeyValuePair<string, KVObject>> children ) => Collection( children );

	public bool IsArray => ValueType == KVValueType.Array;
	public bool IsCollection => ValueType == KVValueType.Collection;
	public bool IsNull => ValueType == KVValueType.Null;

	/// <summary>
	/// Members of a collection, elements of an array, or zero for a value.
	/// </summary>
	public int Count => _items?.Count ?? 0;

	public IEnumerable<string> Keys => _names ?? (IEnumerable<string>)[];

	/// <summary>
	/// A collection's member values or an array's elements.
	/// </summary>
	public IEnumerable<KVObject> Values => _items ?? (IEnumerable<KVObject>)[];

	/// <summary>
	/// A collection's members by name. Array elements have no names.
	/// </summary>
	public IEnumerable<KeyValuePair<string, KVObject>> Children
	{
		get
		{
			if ( _items is null )
				yield break;

			for ( var i = 0; i < _items.Count; i++ )
				yield return new KeyValuePair<string, KVObject>( _names?[i], _items[i] );
		}
	}

	public KVObject this[string key]
	{
		get => TryGetValue( key, out var child ) ? child : null;
		set
		{
			var index = IndexOf( key );
			if ( index >= 0 )
				_items[index] = value;
			else
				Add( key, value );
		}
	}

	public KVObject this[int index] => _items[index];

	public bool ContainsKey( string name ) => IndexOf( name ) >= 0;

	public bool TryGetValue( string name, out KVObject child )
	{
		var index = IndexOf( name );
		child = index >= 0 ? _items[index] : null;
		return index >= 0;
	}

	private int IndexOf( string name )
	{
		if ( _names is null )
			return -1;

		for ( var i = 0; i < _names.Count; i++ )
		{
			if ( string.Equals( _names[i], name, StringComparison.Ordinal ) )
				return i;
		}

		return -1;
	}

	public void Add( string key, KVObject value )
	{
		if ( _names is null )
			throw new InvalidOperationException( $"Can't add a named member to a {ValueType}." );

		_names.Add( key );
		_items.Add( value );
	}

	public void Add( KVObject value )
	{
		if ( !IsArray )
			throw new InvalidOperationException( $"Can't add an element to a {ValueType}." );

		_items.Add( value );
	}

	public bool TryAdd( string key, KVObject value )
	{
		if ( ContainsKey( key ) )
			return false;

		Add( key, value );
		return true;
	}

	public bool Remove( string key )
	{
		var index = IndexOf( key );
		if ( index < 0 )
			return false;

		RemoveAt( index );
		return true;
	}

	public void RemoveAt( int index )
	{
		_names?.RemoveAt( index );
		_items.RemoveAt( index );
	}

	public void Clear()
	{
		_names?.Clear();
		_items?.Clear();
	}

	/// <summary>
	/// A copy of an array's elements.
	/// </summary>
	public Span<KVObject> AsArraySpan() => _items is null ? Span<KVObject>.Empty : _items.ToArray().AsSpan();

	public byte[] AsBlob() => _value as byte[] ?? [];

	public IEnumerator<KeyValuePair<string, KVObject>> GetEnumerator() => Children.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public override string ToString() => ToString( CultureInfo.InvariantCulture );

	public string ToString( IFormatProvider provider ) => _value switch
	{
		null => ValueType == KVValueType.Null ? "" : $"[{ValueType}]",
		string text => text,
		bool flag => flag ? "true" : "false",
		IFormattable formattable => formattable.ToString( null, provider ),
		_ => _value.ToString(),
	};

	public bool ToBoolean() => ToBoolean( CultureInfo.InvariantCulture );

	public bool ToBoolean( IFormatProvider provider ) => _value switch
	{
		bool flag => flag,
		string text => bool.TryParse( text, out var parsed ) ? parsed : ToDouble( provider ) != 0,
		_ => ToDouble( provider ) != 0,
	};

	public double ToDouble() => ToDouble( CultureInfo.InvariantCulture );

	public double ToDouble( IFormatProvider provider ) => _value switch
	{
		bool flag => flag ? 1 : 0,
		short x => x,
		ushort x => x,
		int x => x,
		uint x => x,
		long x => x,
		ulong x => x,
		float x => x,
		double x => x,
		string text => double.TryParse( text, NumberStyles.Float, provider, out var parsed ) ? parsed : 0,
		_ => 0,
	};

	public float ToSingle() => ToSingle( CultureInfo.InvariantCulture );
	public float ToSingle( IFormatProvider provider ) => _value is float x ? x : (float)ToDouble( provider );

	public long ToInt64() => ToInt64( CultureInfo.InvariantCulture );

	public long ToInt64( IFormatProvider provider ) => _value switch
	{
		bool flag => flag ? 1 : 0,
		short x => x,
		ushort x => x,
		int x => x,
		uint x => x,
		long x => x,
		ulong x => unchecked((long)x),
		float x => (long)x,
		double x => (long)x,
		string text => long.TryParse( text, NumberStyles.Integer, provider, out var parsed ) ? parsed : (long)ToDouble( provider ),
		_ => 0,
	};

	public ulong ToUInt64() => ToUInt64( CultureInfo.InvariantCulture );
	public ulong ToUInt64( IFormatProvider provider ) => _value is ulong x ? x : unchecked((ulong)ToInt64( provider ));

	public int ToInt32() => ToInt32( CultureInfo.InvariantCulture );
	public int ToInt32( IFormatProvider provider ) => unchecked((int)ToInt64( provider ));
	public uint ToUInt32() => ToUInt32( CultureInfo.InvariantCulture );
	public uint ToUInt32( IFormatProvider provider ) => unchecked((uint)ToInt64( provider ));
	public short ToInt16() => ToInt16( CultureInfo.InvariantCulture );
	public short ToInt16( IFormatProvider provider ) => unchecked((short)ToInt64( provider ));
	public ushort ToUInt16() => ToUInt16( CultureInfo.InvariantCulture );
	public ushort ToUInt16( IFormatProvider provider ) => unchecked((ushort)ToInt64( provider ));
	public byte ToByte() => ToByte( CultureInfo.InvariantCulture );
	public byte ToByte( IFormatProvider provider ) => unchecked((byte)ToInt64( provider ));
	public sbyte ToSByte() => ToSByte( CultureInfo.InvariantCulture );
	public sbyte ToSByte( IFormatProvider provider ) => unchecked((sbyte)ToInt64( provider ));

	public static explicit operator string( KVObject obj ) => obj?.ToString( CultureInfo.InvariantCulture );
	public static explicit operator bool( KVObject obj ) => obj.ToBoolean();
	public static explicit operator short( KVObject obj ) => obj.ToInt16();
	public static explicit operator ushort( KVObject obj ) => obj.ToUInt16();
	public static explicit operator int( KVObject obj ) => obj.ToInt32();
	public static explicit operator uint( KVObject obj ) => obj.ToUInt32();
	public static explicit operator long( KVObject obj ) => obj.ToInt64();
	public static explicit operator ulong( KVObject obj ) => obj.ToUInt64();
	public static explicit operator float( KVObject obj ) => obj.ToSingle();
	public static explicit operator double( KVObject obj ) => obj.ToDouble();
	public static explicit operator byte( KVObject obj ) => obj.ToByte();
	public static explicit operator sbyte( KVObject obj ) => obj.ToSByte();
	public static explicit operator byte[]( KVObject obj ) => obj.AsBlob();

	public static implicit operator KVObject( string value ) => new( value );
	public static implicit operator KVObject( bool value ) => new( value );
	public static implicit operator KVObject( short value ) => new( value );
	public static implicit operator KVObject( ushort value ) => new( value );
	public static implicit operator KVObject( int value ) => new( value );
	public static implicit operator KVObject( uint value ) => new( value );
	public static implicit operator KVObject( long value ) => new( value );
	public static implicit operator KVObject( ulong value ) => new( value );
	public static implicit operator KVObject( float value ) => new( value );
	public static implicit operator KVObject( double value ) => new( value );
	public static implicit operator KVObject( byte value ) => new( (int)value );
	public static implicit operator KVObject( sbyte value ) => new( (int)value );
	public static implicit operator KVObject( byte[] value ) => Blob( value );
}
