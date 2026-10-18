using System;
using System.IO;
using Deadlock;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Loads a compiled AnimGraph 2 graph (vnmgraph_c) as a <see cref="DeadlockAnimationGraph"/>: the graph with every
/// file it uses (child graphs, clips and skeletons), so the animator loads a whole tree at once.
/// </summary>
internal sealed class AnimationGraphLoader( string filePath ) : ResourceLoader<DeadlockMount>
{
	protected override object Load()
	{
		var root = filePath.EndsWith( "_c" ) ? filePath[..^2] : filePath;
		var files = new Dictionary<string, global::Deadlock.KVObject>( StringComparer.OrdinalIgnoreCase );
		var pending = new Queue<string>();
		pending.Enqueue( root );

		while ( pending.Count > 0 )
		{
			var path = pending.Dequeue();
			if ( files.ContainsKey( path ) || !Host.FileExists( path + "_c" ) )
				continue;

			var data = new ResourceFile( Host.ReadFile( path + "_c" ) ).ReadKeyValues( "DATA" );
			if ( data is null )
				continue;

			files[path] = ParticleLoader.ToParticleKeyValues( data );

			// Graphs name their skeleton and the clips and graphs they play; clips name only their skeleton.
			if ( data.GetString( "m_skeleton" ) is { Length: > 0 } skeleton )
				pending.Enqueue( skeleton.Replace( '\\', '/' ) );

			foreach ( var resource in data.GetStringArray( "m_resources" ) )
			{
				if ( resource.Length > 0 )
					pending.Enqueue( resource.Replace( '\\', '/' ) );
			}
		}

		if ( !files.ContainsKey( root ) )
			throw new InvalidDataException( "Animation graph has no DATA block." );

		return new DeadlockAnimationGraph { Root = root, Files = files };
	}
}

/// <summary>
/// Loads a compiled VData file (vdata_c) as a <see cref="DeadlockVData"/>.
/// </summary>
internal sealed class VDataLoader( string filePath ) : ResourceLoader<DeadlockMount>
{
	protected override object Load()
	{
		var data = new ResourceFile( Host.ReadFile( filePath ) ).ReadKeyValues( "DATA" )
			?? throw new InvalidDataException( "VData has no DATA block." );

		return new DeadlockVData { Data = ParticleLoader.ToParticleKeyValues( data ) };
	}
}
