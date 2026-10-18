using System;
using System.IO;
using Deadlock;
using ParticleKV = Deadlock.KVObject;
using ParticleSpriteSheet = Deadlock.Particles.ParticleSpriteSheet;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Loads a compiled Deadlock particle system (vpcf_c) as a <see cref="DeadlockParticleSystem"/>: its definition, in
/// the particle code's KeyValues and upgraded from the format it was compiled in, and the sprite sheets of the
/// textures its renderers draw with.
/// </summary>
internal sealed class ParticleLoader( string filePath ) : ResourceLoader<DeadlockMount>
{
	/// <summary>
	/// The particle formats Deadlock's systems are compiled in, by KV3 format id (ValveResourceFormat's KV3IDLookup).
	/// </summary>
	private static readonly Dictionary<Guid, string> Formats = new()
	{
		[new Guid( [0x1D, 0xCE, 0xD3, 0x39, 0x42, 0x94, 0x07, 0x44, 0x9B, 0x78, 0x83, 0x17, 0x14, 0x5B, 0x17, 0x32] )] = "vpcf61",
		[new Guid( [0x0D, 0xA9, 0x35, 0xC1, 0x52, 0xEF, 0xFA, 0x47, 0xA9, 0xB8, 0x83, 0xA3, 0x82, 0xA7, 0x74, 0x71] )] = "vpcf62",
		[new Guid( [0x9E, 0xA6, 0xE6, 0xA6, 0xD3, 0x52, 0x27, 0x45, 0x8B, 0x9C, 0xFF, 0x3B, 0xB9, 0x1A, 0xCA, 0x3E] )] = "vpcf63",
		[new Guid( [0x34, 0x34, 0x5F, 0xC9, 0xEF, 0xDF, 0x35, 0x4A, 0x99, 0x13, 0x02, 0x4B, 0xED, 0xDB, 0x41, 0x6A] )] = "vpcf64",
		[new Guid( [0xA9, 0x06, 0xE8, 0x48, 0x10, 0xC2, 0xD8, 0x4C, 0x81, 0x1A, 0x38, 0xB5, 0xEF, 0x63, 0xB1, 0x95] )] = "vpcf65",
		[new Guid( [0xE4, 0x1B, 0x57, 0xDD, 0x6E, 0x58, 0x41, 0x4F, 0x8B, 0x2D, 0x5B, 0xBC, 0x96, 0x9E, 0x1C, 0x53] )] = "vpcf66",
	};

	/// <summary>
	/// What renderers draw with when they name no texture.
	/// </summary>
	private static readonly string[] DefaultTextures = ["materials/particle/base_sprite.vtex", "materials/particle/base_trail.vtex"];

	protected override object Load()
	{
		var file = new ResourceFile( Host.ReadFile( filePath ) );
		var data = file.ReadKeyValues( "DATA" ) ?? throw new InvalidDataException( "Particle system has no DATA block." );

		// A format we don't know is newer than the upgrade chain, or older than Deadlock's content; either way it's
		// read as it is.
		var format = Formats.GetValueOrDefault( file.ReadKeyValuesFormat( "DATA" ), "vpcf66" );
		var name = filePath.EndsWith( "_c" ) ? filePath[..^2] : filePath;
		var definition = new global::Deadlock.Particles.ParticleSystem( name, ToParticleKeyValues( data ), format );

		return new DeadlockParticleSystem
		{
			Definition = definition,
			SpriteSheets = ReadSpriteSheets( definition.Data ),
		};
	}

	/// <summary>
	/// The sheets of the textures the renderers draw with, by texture path.
	/// </summary>
	private Dictionary<string, ParticleSpriteSheet> ReadSpriteSheets( ParticleKV data )
	{
		var textures = new HashSet<string>( DefaultTextures, StringComparer.OrdinalIgnoreCase );

		foreach ( var renderer in data["m_Renderers"]?.Values ?? [] )
		{
			foreach ( var input in renderer["m_vecTexturesInput"]?.Values ?? [] )
			{
				if ( input["m_hTexture"] is { ValueType: global::Deadlock.KVValueType.String } texture && ((string)texture).Length > 0 )
					textures.Add( ((string)texture).Replace( '\\', '/' ) );
			}
		}

		var sheets = new Dictionary<string, ParticleSpriteSheet>( StringComparer.OrdinalIgnoreCase );

		foreach ( var texture in textures )
		{
			if ( Host.ReadTextureSheet( texture ) is { } sheet )
				sheets[texture] = sheet;
		}

		return sheets;
	}

	/// <summary>
	/// A KeyValues3 tree as the particle code's KeyValues, keeping each value's type.
	/// </summary>
	internal static ParticleKV ToParticleKeyValues( object value )
	{
		switch ( value )
		{
			case KVObject obj:
			{
				var collection = ParticleKV.Collection( obj.Count );
				foreach ( var key in obj.Keys )
					collection.Add( key, ToParticleKeyValues( obj[key] ) );

				return collection;
			}

			case object[] array:
			{
				var elements = ParticleKV.Array( array.Length );
				foreach ( var element in array )
					elements.Add( ToParticleKeyValues( element ) );

				return elements;
			}

			case byte[] blob: return ParticleKV.Blob( blob );
			case string text: return new ParticleKV( text );
			case bool flag: return new ParticleKV( flag );
			case short x: return new ParticleKV( x );
			case ushort x: return new ParticleKV( x );
			case int x: return new ParticleKV( x );
			case uint x: return new ParticleKV( x );
			case long x: return new ParticleKV( x );
			case ulong x: return new ParticleKV( x );
			case float x: return new ParticleKV( x );
			case double x: return new ParticleKV( x );
			default: return ParticleKV.Null();
		}
	}
}
