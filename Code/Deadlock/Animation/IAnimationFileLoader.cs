using System;

namespace Deadlock.Animation;

/// <summary>
/// Loads the compiled AnimGraph 2 files a graph refers to (graphs, clips and skeletons) as their KeyValues data.
/// </summary>
public interface IAnimationFileLoader
{
	/// <summary>
	/// The data of a compiled file by the path the game refers to it by, e.g. <c>animgraphs/foo.vnmgraph</c>, or
	/// null when there's no such file.
	/// </summary>
	KVObject Load( string path );
}
