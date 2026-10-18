using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// A hero a <c>Deadlock Hero</c> can play: its VData name (<c>hero_drifter</c>), the name the game shows, its model
/// and the AnimGraph 2 graph that model plays.
/// </summary>
internal sealed record DeadlockHeroChoice( string Name, string Title, string Model, string Graph );

public sealed partial class DeadlockMount
{
	/// <summary>
	/// The mounted Deadlock, for editor tools.
	/// </summary>
	internal static DeadlockMount Current { get; private set; }

	private List<DeadlockHeroChoice> _heroes;

	/// <summary>
	/// The heroes, if <see cref="GetHeroes"/> has read them yet.
	/// </summary>
	internal IReadOnlyList<DeadlockHeroChoice> CachedHeroes => _heroes;

	/// <summary>
	/// The heroes that can be played, by the name the game shows: released and pre-release heroes that aren't
	/// disabled, whose model and animation graph are in the game's content. Read once, when first asked for.
	/// </summary>
	internal IReadOnlyList<DeadlockHeroChoice> GetHeroes()
	{
		lock ( _files )
		{
			if ( _heroes is not null )
				return _heroes;
		}

		var heroes = new List<DeadlockHeroChoice>();

		try
		{
			var titles = ReadHeroTitles();
			var data = new ResourceFile( ReadFile( "scripts/heroes.vdata_c" ) ).ReadKeyValues( "DATA" );

			foreach ( var name in data.Keys.Where( x => x.StartsWith( "hero_", StringComparison.Ordinal ) ) )
			{
				object Inherited( string key )
				{
					var entry = data.GetObject( name );
					for ( var depth = 0; entry is not null && depth < 8; depth++ )
					{
						if ( entry.ContainsKey( key ) )
							return entry[key];

						entry = entry.GetString( "_base" ) is { } baseName ? data.GetObject( baseName ) : null;
					}

					return null;
				}

				if ( Inherited( "m_bDisabled" ) is true )
					continue;

				if ( Inherited( "m_eHeroDevelopmentState" ) is not ("EHeroDevState_Release" or "EHeroDevState_PreRelease") )
					continue;

				if ( Inherited( "m_strModelName" ) is not string model || !FileExists( model + "_c" ) )
					continue;

				var graph = ReadModelGraph( model );
				if ( graph is null || !FileExists( graph + "_c" ) )
					continue;

				heroes.Add( new DeadlockHeroChoice( name, titles.GetValueOrDefault( name ) ?? name["hero_".Length..], model, graph ) );
			}
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
		{
			Log.Warning( $"Couldn't read Deadlock's heroes: {e.Message}" );
		}

		heroes.Sort( ( a, b ) => string.Compare( a.Title, b.Title, StringComparison.OrdinalIgnoreCase ) );

		lock ( _files )
		{
			_heroes ??= heroes;
			return _heroes;
		}
	}

	/// <summary>
	/// The first AnimGraph 2 graph a model lists, the one the game plays it with.
	/// </summary>
	private string ReadModelGraph( string model )
	{
		var data = new ResourceFile( ReadFile( model + "_c" ) ).ReadKeyValues( "DATA" );
		return data?.GetObjects( "m_animGraph2Refs" ).Select( x => x.GetString( "m_hGraph" ) ).FirstOrDefault( x => !string.IsNullOrEmpty( x ) );
	}

	/// <summary>
	/// Hero names as the game shows them in English, by VData name. They live in a loose localization file beside
	/// the VPKs.
	/// </summary>
	private Dictionary<string, string> ReadHeroTitles()
	{
		var titles = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var path = System.IO.Path.Combine( _gameDirectory, "citadel", "resource", "localization", "citadel_gc_hero_names", "citadel_gc_hero_names_english.txt" );

		if ( !System.IO.File.Exists( path ) )
			return titles;

		foreach ( Match match in Regex.Matches( System.IO.File.ReadAllText( path ), "\"(hero_[A-Za-z0-9_]+):n\"\\s+\"([^\"]*)\"" ) )
			titles.TryAdd( match.Groups[1].Value, match.Groups[2].Value );

		return titles;
	}
}
