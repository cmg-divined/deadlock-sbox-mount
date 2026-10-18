using System;

namespace Deadlock;

/// <summary>
/// Deadlock's material dynamic expressions (a material's DynamicParams): small bytecode programs the game runs for a
/// parameter, such as <c>float4(.7,1,0,frac(time()*.1))</c> scrolling water or <c>$ent_random</c> varying signs. The
/// mount applies the ones its shaders have a place for: constant ones once, ones using <c>time()</c> every frame,
/// per entity ones on each object's own copy of the material.
/// </summary>
public static class DeadlockMaterialExpressions
{
	/// <summary>
	/// The attribute every entity has: "A random value [0.0, 1.0] used to make sure entities are not all in sync".
	/// </summary>
	public static readonly uint EntRandom = Hash( "$ent_random" );

	private const int TimeFunction = 27;

	private static readonly List<(Material Material, string Parameter, byte[] Code)> Animated = new();
	private static readonly Dictionary<Material, List<(string Parameter, byte[] Code)>> PerEntityParameters = new();

	/// <summary>
	/// Runs an expression. Attributes come from <paramref name="attribute"/> by their name's hash; an attribute it
	/// doesn't know, a function or instruction this doesn't, gives null.
	/// </summary>
	public static Vector4? Evaluate( byte[] code, float time, Func<uint, Vector4?> attribute = null )
	{
		var stack = new Stack<Vector4>();
		var locals = new Vector4[256];
		var pc = 0;

		try
		{
			for ( var steps = 0; steps < 4096; steps++ )
			{
				var op = code[pc++];
				switch ( op )
				{
					case 0x00:
						return stack.Count > 0 ? stack.Pop() : null;

					// Jump, and branch on the condition popped: absolute offsets.
					case 0x02:
						pc = BitConverter.ToUInt16( code, pc );
						break;

					case 0x04:
					{
						var whenTrue = BitConverter.ToUInt16( code, pc );
						var whenFalse = BitConverter.ToUInt16( code, pc + 2 );
						pc = stack.Pop().x != 0 ? whenTrue : whenFalse;
						break;
					}

					case 0x06:
					{
						var function = code[pc];
						pc += 2;
						if ( Call( function, stack, time ) is not { } result )
							return null;

						stack.Push( result );
						break;
					}

					case 0x07:
					{
						var value = BitConverter.ToSingle( code, pc );
						pc += 4;
						stack.Push( Splat( value ) );
						break;
					}

					case 0x08:
						locals[code[pc++]] = stack.Pop();
						break;

					case 0x09:
						stack.Push( locals[code[pc++]] );
						break;

					case >= 0x0D and <= 0x17:
					{
						var b = stack.Pop();
						var a = stack.Pop();
						stack.Push( Binary( op, a, b ) );
						break;
					}

					case 0x18:
						stack.Push( Splat( 0 ) - stack.Pop() );
						break;

					case 0x19:
					{
						var hash = BitConverter.ToUInt32( code, pc );
						pc += 4;
						if ( attribute?.Invoke( hash ) is not { } value )
							return null;

						stack.Push( value );
						break;
					}

					// Swizzle: two bits per component pick the source.
					case 0x1E:
					{
						var value = stack.Pop();
						var mask = code[pc++];
						stack.Push( new Vector4( Component( value, mask & 3 ), Component( value, (mask >> 2) & 3 ), Component( value, (mask >> 4) & 3 ), Component( value, (mask >> 6) & 3 ) ) );
						break;
					}

					default:
						return null;
				}
			}
		}
		catch ( Exception e ) when ( e is IndexOutOfRangeException or ArgumentException or InvalidOperationException )
		{
			return null;
		}

		return null;
	}

	private static Vector4 Splat( float value ) => new( value, value, value, value );

	private static float Component( Vector4 value, int index ) => index switch { 0 => value.x, 1 => value.y, 2 => value.z, _ => value.w };

	private static Vector4 Binary( byte op, Vector4 a, Vector4 b )
	{
		static Vector4 Each( Vector4 a, Vector4 b, Func<float, float, float> f ) => new( f( a.x, b.x ), f( a.y, b.y ), f( a.z, b.z ), f( a.w, b.w ) );
		static float Bool( bool value ) => value ? 1 : 0;

		return op switch
		{
			0x0D => Each( a, b, ( x, y ) => Bool( x == y ) ),
			0x0E => Each( a, b, ( x, y ) => Bool( x != y ) ),
			0x0F => Each( a, b, ( x, y ) => Bool( x > y ) ),
			0x10 => Each( a, b, ( x, y ) => Bool( x >= y ) ),
			0x11 => Each( a, b, ( x, y ) => Bool( x < y ) ),
			0x12 => Each( a, b, ( x, y ) => Bool( x <= y ) ),
			0x13 => a + b,
			0x14 => a - b,
			0x15 => Each( a, b, ( x, y ) => x * y ),
			0x16 => Each( a, b, ( x, y ) => x / y ),
			_ => Each( a, b, ( x, y ) => y != 0 ? x % y : 0 ),
		};
	}

	private static Vector4 Map( Vector4 v, Func<float, float> f ) => new( f( v.x ), f( v.y ), f( v.z ), f( v.w ) );

	private static float Frac( float x ) => x - MathF.Floor( x );

	private static Vector4? Call( byte function, Stack<Vector4> stack, float time )
	{
		Vector4 Pop() => stack.Pop();

		switch ( function )
		{
			case 0: return Map( Pop(), MathF.Sin );
			case 1: return Map( Pop(), MathF.Cos );
			case 2: return Map( Pop(), MathF.Tan );
			case 3: return Map( Pop(), Frac );
			case 4: return Map( Pop(), MathF.Floor );
			case 5: return Map( Pop(), MathF.Ceiling );
			case 6: return Map( Pop(), x => Math.Clamp( x, 0f, 1f ) );
			case 7:
			{
				var max = Pop();
				var min = Pop();
				var x = Pop();
				return new Vector4( Math.Clamp( x.x, min.x, max.x ), Math.Clamp( x.y, min.y, max.y ), Math.Clamp( x.z, min.z, max.z ), Math.Clamp( x.w, min.w, max.w ) );
			}
			case 8:
			{
				var t = Pop();
				var b = Pop();
				var a = Pop();
				return new Vector4( a.x + (b.x - a.x) * t.x, a.y + (b.y - a.y) * t.y, a.z + (b.z - a.z) * t.z, a.w + (b.w - a.w) * t.w );
			}
			case 19: return Map( Pop(), x => MathF.Sign( x ) );
			case 20: return Map( Pop(), MathF.Abs );
			case 21:
			{
				var e = Pop();
				var x = Pop();
				return new Vector4( MathF.Pow( x.x, e.x ), MathF.Pow( x.y, e.y ), MathF.Pow( x.z, e.z ), MathF.Pow( x.w, e.w ) );
			}
			case 24:
			{
				var w = Pop();
				var z = Pop();
				var y = Pop();
				var x = Pop();
				return new Vector4( x.x, y.x, z.x, w.x );
			}
			case 25:
			{
				var z = Pop();
				var y = Pop();
				var x = Pop();
				return new Vector4( x.x, y.x, z.x, 0 );
			}
			case 26:
			{
				var y = Pop();
				var x = Pop();
				return new Vector4( x.x, y.x, 0, 0 );
			}
			case TimeFunction: return Splat( time );
			case 28:
			{
				var b = Pop();
				var a = Pop();
				return new Vector4( MathF.Min( a.x, b.x ), MathF.Min( a.y, b.y ), MathF.Min( a.z, b.z ), MathF.Min( a.w, b.w ) );
			}
			case 29:
			{
				var b = Pop();
				var a = Pop();
				return new Vector4( MathF.Max( a.x, b.x ), MathF.Max( a.y, b.y ), MathF.Max( a.z, b.z ), MathF.Max( a.w, b.w ) );
			}
			default: return null;
		}
	}

	/// <summary>
	/// The instructions' functions and attributes, without running them; false if an instruction isn't understood.
	/// </summary>
	private static bool Scan( byte[] code, Action<byte> function, Action<uint> attribute )
	{
		var pc = 0;
		while ( pc < code.Length )
		{
			var op = code[pc++];
			switch ( op )
			{
				case 0x00: return true;
				case 0x02: pc += 2; break;
				case 0x04: pc += 4; break;
				case 0x06: function?.Invoke( code[pc] ); pc += 2; break;
				case 0x07: pc += 4; break;
				case 0x08 or 0x09 or 0x1E: pc += 1; break;
				case >= 0x0D and <= 0x18: break;
				case 0x19: attribute?.Invoke( BitConverter.ToUInt32( code, pc ) ); pc += 4; break;
				default: return false;
			}
		}

		return true;
	}

	public static bool UsesTime( byte[] code )
	{
		var uses = false;
		Scan( code, x => uses |= x == TimeFunction, null );
		return uses;
	}

	public static bool UsesAttribute( byte[] code, uint hash )
	{
		var uses = false;
		Scan( code, null, x => uses |= x == hash );
		return uses;
	}

	/// <summary>
	/// Sets the shader parameters a Deadlock material parameter becomes. False for one the mount doesn't handle.
	/// </summary>
	public static bool Apply( Material material, string parameter, Vector4 value )
	{
		if ( TryLayer( parameter, "g_vScaleRotationTranslation", out var layer ) )
			material.Set( $"g_vUvTransform{layer}", UvTransform( value ) );
		else if ( TryLayer( parameter, "g_vRevealScaleRotationTranslation", out layer ) )
			material.Set( $"g_vRevealUvTransform{layer}", UvTransform( value ) );
		else if ( parameter == "g_vOverlayScaleRotationTranslation" )
			material.Set( "g_vOverlayUvTransform", UvTransform( value ) );
		else if ( parameter == "g_vSelfIllumTint1" )
			material.Set( "g_vSelfIllumTint", new Vector3( value.x, value.y, value.z ) );
		else if ( parameter == "g_flSelfIllumScale1" )
			material.Set( "g_flSelfIllumScale", value.x );
		else
			return false;

		return true;
	}

	private static bool TryLayer( string parameter, string prefix, out string layer )
	{
		layer = parameter.Length == prefix.Length + 1 && parameter.StartsWith( prefix, StringComparison.Ordinal ) && char.IsDigit( parameter[^1] ) ? parameter[^1..] : null;
		return layer is not null;
	}

	/// <summary>
	/// A (scale, rotation in degrees, translation) UV transform as the environment shader takes it, as the mount's
	/// importer builds it: ( scale * sin, scale * cos, offset over the scale ).
	/// </summary>
	private static Vector4 UvTransform( Vector4 value )
	{
		var scale = value.x != 0 ? value.x : 1;
		var angle = value.y * MathF.PI / 180;
		return new Vector4( scale * MathF.Sin( angle ), scale * MathF.Cos( angle ), value.z / scale, value.w / scale );
	}

	/// <summary>
	/// Keeps a parameter's expression running every frame.
	/// </summary>
	public static void Animate( Material material, string parameter, byte[] code )
	{
		lock ( Animated )
			Animated.Add( (material, parameter, code) );
	}

	/// <summary>
	/// Notes a parameter whose expression reads <c>$ent_random</c>, for <see cref="ForEntity"/>.
	/// </summary>
	public static void PerEntity( Material material, string parameter, byte[] code )
	{
		lock ( PerEntityParameters )
		{
			if ( !PerEntityParameters.TryGetValue( material, out var parameters ) )
				PerEntityParameters[material] = parameters = new();

			parameters.Add( (parameter, code) );
		}
	}

	public static bool HasPerEntity( Material material )
	{
		lock ( PerEntityParameters )
			return material is not null && PerEntityParameters.ContainsKey( material );
	}

	/// <summary>
	/// An entity's own copy of a material with per entity expressions, run with its random value (0 to 1).
	/// </summary>
	public static Material ForEntity( Material material, float random )
	{
		List<(string Parameter, byte[] Code)> parameters;
		lock ( PerEntityParameters )
		{
			if ( !PerEntityParameters.TryGetValue( material, out parameters ) )
				return material;
		}

		var copy = material.CreateCopy( $"{material.Name}_entity_{(int)(random * 100000)}" );
		Vector4? Attribute( uint hash ) => hash == EntRandom ? Splat( random ) : null;

		foreach ( var (parameter, code) in parameters )
		{
			if ( Evaluate( code, 0, Attribute ) is { } value )
				Apply( copy, parameter, value );
		}

		return copy;
	}

	internal static void Update( float time )
	{
		lock ( Animated )
		{
			Animated.RemoveAll( x => !x.Material.IsValid() );

			foreach ( var (material, parameter, code) in Animated )
			{
				if ( Evaluate( code, time ) is { } value )
					Apply( material, parameter, value );
			}
		}
	}

	/// <summary>
	/// A Source 2 string token: MurmurHash2 of the lowercase name, seeded 0x31415926. Attributes are hashed with their $.
	/// </summary>
	public static uint Hash( string name )
	{
		const uint M = 0x5bd1e995;
		var data = System.Text.Encoding.UTF8.GetBytes( name.ToLowerInvariant() );
		var length = data.Length;
		var h = 0x31415926u ^ (uint)length;
		var i = 0;

		while ( length >= 4 )
		{
			var k = BitConverter.ToUInt32( data, i );
			k *= M;
			k ^= k >> 24;
			k *= M;
			h *= M;
			h ^= k;
			i += 4;
			length -= 4;
		}

		switch ( length )
		{
			case 3: h ^= (uint)data[i + 2] << 16; goto case 2;
			case 2: h ^= (uint)data[i + 1] << 8; goto case 1;
			case 1: h ^= data[i]; h *= M; break;
		}

		h ^= h >> 13;
		h *= M;
		h ^= h >> 15;
		return h;
	}
}

/// <summary>
/// Runs the materials' <c>time()</c> expressions every frame.
/// </summary>
public sealed class DeadlockMaterialExpressionSystem : GameObjectSystem
{
	public DeadlockMaterialExpressionSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, () => DeadlockMaterialExpressions.Update( Time.Now ), "Deadlock Material Expressions" );
	}
}
