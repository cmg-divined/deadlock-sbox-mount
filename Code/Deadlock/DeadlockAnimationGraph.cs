using System;
using System.Text.Json.Serialization;
using Deadlock.Animation;

namespace Deadlock;

/// <summary>
/// A Deadlock AnimGraph 2 graph (a <c>.vnmgraph</c>) as the Deadlock mount serves it, at
/// <c>mount://deadlock/animgraphs/....vnmgraph</c>: the graph and every file it uses (child graphs, clips and the
/// skeleton), as their KeyValues data. <see cref="DeadlockAnimator"/> plays it.
/// </summary>
public sealed class DeadlockAnimationGraph : GameResource, IAnimationFileLoader
{
	/// <summary>
	/// The graph's own path, e.g. <c>animgraphs/animgraph2/hero/hero.vnmgraph+drifter.vnmgraph</c>.
	/// </summary>
	[Hide, JsonIgnore]
	public string Root { get; set; }

	/// <summary>
	/// The graph and the files it uses, by path.
	/// </summary>
	[Hide, JsonIgnore]
	public IReadOnlyDictionary<string, KVObject> Files { get; set; } = new Dictionary<string, KVObject>();

	public KVObject Load( string path ) => path is not null && Files.TryGetValue( path.Replace( '\\', '/' ), out var data ) ? data : null;

	/// <summary>
	/// A mounted graph by the path Deadlock refers to it by, or null when the Deadlock mount has no such file.
	/// </summary>
	public static DeadlockAnimationGraph Find( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		path = path.Replace( '\\', '/' );
		if ( path.EndsWith( "_c" ) )
			path = path[..^2];

		if ( !path.StartsWith( "mount://" ) )
			path = $"mount://deadlock/{path}";

		return GameResource.Load<DeadlockAnimationGraph>( path ) is { Root: not null } graph ? graph : null;
	}
}

/// <summary>
/// A Deadlock VData file (a <c>.vdata</c>, such as <c>scripts/heroes.vdata</c>) as the Deadlock mount serves it: its
/// entries by name.
/// </summary>
public sealed class DeadlockVData : GameResource
{
	[Hide, JsonIgnore]
	public KVObject Data { get; set; }

	/// <summary>
	/// An entry, with what it inherits through <c>_base</c> merged under it, or null.
	/// </summary>
	public KVObject Get( string entry ) => Resolve( entry, 0 );

	private KVObject Resolve( string entry, int depth )
	{
		if ( Data is null || string.IsNullOrEmpty( entry ) || depth > 16 || Data[entry] is not { IsCollection: true } own )
			return null;

		var result = KVObject.Collection();

		if ( own.GetStringProperty( "_base" ) is { Length: > 0 } baseName && Resolve( baseName, depth + 1 ) is { } inherited )
		{
			foreach ( var (key, value) in inherited.Children )
				result[key] = value;
		}

		foreach ( var (key, value) in own.Children )
			result[key] = value;

		return result;
	}

	/// <summary>
	/// A mounted VData file by its path, e.g. <c>scripts/heroes.vdata</c>, or null.
	/// </summary>
	public static DeadlockVData Find( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		if ( !path.StartsWith( "mount://" ) )
			path = $"mount://deadlock/{path}";

		return GameResource.Load<DeadlockVData>( path ) is { Data: not null } vdata ? vdata : null;
	}
}

/// <summary>
/// The AnimGraph 2 graphs a mounted Deadlock model is animated with (its <c>m_animGraph2Refs</c>). A hero model's
/// first graph is the one the game plays it with. Read it with <c>Model.GetData&lt;DeadlockAnimGraphs&gt;()</c>.
/// </summary>
[Sandbox.ModelEditor.GameData( "deadlock_anim_graphs" )]
public sealed class DeadlockAnimGraphs
{
	[Hide]
	public List<DeadlockAnimGraphRef> Graphs { get; set; } = [];
}

public sealed class DeadlockAnimGraphRef
{
	public string Identifier { get; set; }
	public string Graph { get; set; }
}
