using System;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

public sealed partial class DeadlockMount
{
	private const string SurfacePropertiesPath = "surfaceproperties/surfaceproperties.vsurf_c";

	/// <summary>
	/// A Deadlock surface property: its name and the surface it inherits from.
	/// </summary>
	private sealed record SurfaceProperty( string Name, uint BaseHash );

	/// <summary>
	/// s&amp;box has far fewer surfaces than Deadlock, so names without one map by what they contain.
	/// </summary>
	private static readonly (string Keyword, string Surface)[] SurfaceKeywords =
	[
		("flesh", "flesh"), ("player", "flesh"), ("trooper", "flesh"), ("npc", "flesh"),
		("metal", "metal"), ("grate", "metal"), ("chain", "metal"), ("canister", "metal"), ("can", "metal"), ("grenade", "metal"), ("weapon", "metal"),
		("wood", "wood"), ("ladder", "wood"),
		("glass", "glass"), ("window", "glass"),
		("dirt", "dirt"), ("mud", "dirt"), ("sand", "dirt"), ("gravel", "dirt"), ("clay", "dirt"),
		("grass", "grass"), ("foliage", "grass"),
		("water", "water"), ("wet", "water"), ("wade", "water"), ("slime", "water"),
		("cardboard", "cardboard"), ("paper", "cardboard"),
		("plastic", "plastic"), ("rubber", "rubber"), ("tire", "rubber"),
		("carpet", "fabric"), ("tile", "ceramic"), ("porcelain", "ceramic"), ("pottery", "ceramic"), ("marble", "ceramic"),
	];

	private Dictionary<uint, SurfaceProperty> _surfaceProperties;
	private readonly Dictionary<string, Surface> _surfaces = new( StringComparer.OrdinalIgnoreCase );

	private void ClearSurfaces()
	{
		_surfaceProperties = null;
		_surfaces.Clear();
	}

	/// <summary>
	/// The s&amp;box surface for a surface property hash, as physics shapes store them.
	/// </summary>
	internal Surface GetSurface( uint hash )
	{
		return GetSurface( SurfaceProperties.TryGetValue( hash, out var property ) ? property.Name : null );
	}

	/// <summary>
	/// The s&amp;box surface closest to a Deadlock surface property: the same name, else its base surface's,
	/// else one matching a keyword in its name, else the default surface.
	/// </summary>
	internal Surface GetSurface( string name )
	{
		name ??= "";

		lock ( _surfaces )
		{
			if ( _surfaces.TryGetValue( name, out var cached ) )
				return cached;
		}

		var surface = FindSurface( name );
		lock ( _surfaces )
			_surfaces[name] = surface;

		return surface;
	}

	private Surface FindSurface( string name )
	{
		var byName = SurfaceProperties.Values.GroupBy( x => x.Name, StringComparer.OrdinalIgnoreCase ).ToDictionary( x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase );

		// Walk up the inheritance chain; the depth limit guards against loops.
		var current = name;
		for ( var depth = 0; depth < 8 && !string.IsNullOrEmpty( current ); depth++ )
		{
			if ( Surface.FindByName( current.ToLowerInvariant() ) is { } exact )
				return exact;

			if ( !byName.TryGetValue( current, out var property ) || !SurfaceProperties.TryGetValue( property.BaseHash, out var parent ) || parent == property )
				break;

			current = parent.Name;
		}

		foreach ( var (keyword, surface) in SurfaceKeywords )
		{
			if ( name.Contains( keyword, StringComparison.OrdinalIgnoreCase ) && Surface.FindByName( surface ) is { } match )
				return match;
		}

		return Surface.FindByName( "default" );
	}

	/// <summary>
	/// Deadlock's surface properties by name hash, read from every search path, earlier ones winning.
	/// </summary>
	private Dictionary<uint, SurfaceProperty> SurfaceProperties
	{
		get
		{
			if ( _surfaceProperties is { } properties )
				return properties;

			properties = [];

			foreach ( var archive in _archives )
			{
				if ( !archive.Contains( SurfacePropertiesPath ) )
					continue;

				try
				{
					var data = new ResourceFile( archive.ReadFile( SurfacePropertiesPath ) ).ReadKeyValues( "DATA" );

					foreach ( var property in data?.GetObjects( "SurfacePropertiesList" ) ?? [] )
					{
						var hash = unchecked((uint)property.GetInt64( "m_nameHash" ));
						var baseHash = unchecked((uint)property.GetInt64( "m_baseNameHash" ));
						properties.TryAdd( hash, new SurfaceProperty( property.GetString( "surfacePropertyName", "" ), baseHash ) );
					}
				}
				catch ( Exception e ) when ( e is InvalidDataException or IOException )
				{
					Log.Warning( $"Couldn't read {Title}'s surface properties: {e.Message}" );
				}
			}

			return _surfaceProperties = properties;
		}
	}
}
