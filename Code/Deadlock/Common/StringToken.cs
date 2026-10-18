using System;
using System.Collections.Concurrent;
using System.Text;

namespace Deadlock;

/// <summary>
/// A name as Source 2 hashes it: MurmurHash2 of the lowercased name, seeded with 0x31415926. Graph data refers to
/// names (parameters, bone masks, IDs) by these tokens.
/// </summary>
/// <remarks>
/// Stands in for ValveResourceFormat's StringToken, without its vectorised hashing or table of known names: names are
/// remembered as they're hashed, which is all the animation code needs to print them.
/// </remarks>
public readonly struct StringToken : IEquatable<StringToken>
{
	private const uint Seed = 0x31415926;
	private const uint M = 0x5bd1e995;
	private const int R = 24;

	private static readonly ConcurrentDictionary<uint, string> Names = new();

	/// <summary>
	/// Every name hashed so far, by token.
	/// </summary>
	public static ConcurrentDictionary<uint, string> InvertedTable => Names;

	public uint Token { get; }

	public string Name => Token == 0 ? string.Empty : GetKnownString( Token );

	public bool IsValid => Token != 0;

	/// <summary>
	/// Hashes and remembers a name. An empty name is the unset token.
	/// </summary>
	public StringToken( string name )
	{
		Token = string.IsNullOrEmpty( name ) ? 0 : Store( name );
	}

	public StringToken( uint token, bool allowUnknown = false )
	{
		Token = token;
	}

	/// <summary>
	/// Hashes a name without remembering it. An empty name is unset.
	/// </summary>
	public static StringToken Lookup( ReadOnlySpan<char> name ) => name.IsEmpty ? default : new StringToken( Get( name ), true );

	public static uint Get( ReadOnlySpan<char> key )
	{
		if ( key.IsEmpty )
			return 0;

		var bytes = Encoding.UTF8.GetBytes( key.ToString().ToLowerInvariant() );
		return MurmurHash2( bytes );
	}

	/// <summary>
	/// Hashes a name and remembers it, so the token can be printed as the name. The first spelling stored wins.
	/// </summary>
	public static uint Store( ReadOnlySpan<char> key )
	{
		var token = Get( key );

		if ( token != 0 && !Names.ContainsKey( token ) )
			Names.TryAdd( token, key.ToString() );

		return token;
	}

	public static string GetKnownString( uint hash ) => Names.TryGetValue( hash, out var known ) ? known : $"unknown_key_{hash}";

	private static uint MurmurHash2( byte[] data )
	{
		var length = data.Length;
		var hash = Seed ^ (uint)length;
		var i = 0;

		while ( length >= 4 )
		{
			var k = (uint)(data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24));
			k *= M;
			k ^= k >> R;
			k *= M;

			hash *= M;
			hash ^= k;

			i += 4;
			length -= 4;
		}

		switch ( length )
		{
			case 3:
				hash ^= (uint)data[i + 2] << 16;
				hash ^= (uint)data[i + 1] << 8;
				hash ^= data[i];
				hash *= M;
				break;
			case 2:
				hash ^= (uint)data[i + 1] << 8;
				hash ^= data[i];
				hash *= M;
				break;
			case 1:
				hash ^= data[i];
				hash *= M;
				break;
		}

		hash ^= hash >> 13;
		hash *= M;
		hash ^= hash >> 15;

		return hash;
	}

	public uint ToUInt32() => Token;

	public static StringToken FromUInt32( uint token ) => new( token, true );

	public static StringToken FromString( string name ) => new( name );

	public bool Equals( StringToken other ) => Token == other.Token;

	public override bool Equals( object obj ) => obj is StringToken other && Equals( other );

	public override int GetHashCode() => (int)Token;

	public override string ToString() => Name;

	public static bool operator ==( StringToken left, StringToken right ) => left.Equals( right );
	public static bool operator !=( StringToken left, StringToken right ) => !left.Equals( right );

	public static implicit operator uint( StringToken symbol ) => symbol.Token;
	public static implicit operator StringToken( uint token ) => new( token, true );
	public static implicit operator StringToken( string name ) => new( name );
}
