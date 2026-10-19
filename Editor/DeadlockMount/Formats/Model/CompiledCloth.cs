using System;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// A model's cloth (the PHYS block's <c>m_pFeModel</c>): its nodes and where its animated pose holds them. The first
/// <see cref="StaticNodeCount"/> nodes follow the animation, the rest are simulated. Nodes that are skeleton bones
/// share their name; the cloth compiler adds a bone (<c>$cloth_m0p12</c>, at the skeleton's root) for each simulated
/// vertex of a cloth mesh.
/// </summary>
internal sealed class CompiledCloth
{
	public string[] NodeNames { get; }

	public int StaticNodeCount { get; }

	/// <summary>
	/// The node each node hangs from in the animated pose, which holds it at a fixed offset; -1 for none.
	/// </summary>
	public int[] SkeletonParents { get; }

	/// <summary>
	/// Each node's further parents (<c>m_CtrlSoftOffsets</c>), in the order the animated pose blends them in: each
	/// keeps <c>Alpha</c> of the position so far and takes the rest from that node's offset. Together with
	/// <see cref="SkeletonParents"/> they carry the cloth mesh's skinning, its heaviest bone first.
	/// </summary>
	public (int Node, float Alpha)[][] SoftParents { get; }

	/// <summary>
	/// The distance constraints between nodes (<c>m_Rods</c>): what holds the simulated nodes to each other and to
	/// the static ones they hang from.
	/// </summary>
	public (int A, int B)[] Rods { get; }

	public CompiledCloth( KVObject data )
	{
		NodeNames = data.GetStringArray( "m_CtrlName" );
		StaticNodeCount = Math.Clamp( data.GetInt32( "m_nStaticNodes" ), 0, NodeNames.Length );

		var skeletonParents = data.GetInt64Array( "m_SkelParents" );
		SkeletonParents = new int[NodeNames.Length];
		for ( var i = 0; i < NodeNames.Length; i++ )
			SkeletonParents[i] = i < skeletonParents.Length && skeletonParents[i] < NodeNames.Length ? (int)skeletonParents[i] : -1;

		var soft = new List<(int Node, float Alpha)>[NodeNames.Length];
		foreach ( var offset in data.GetObjects( "m_CtrlSoftOffsets" ) )
		{
			var child = offset.GetInt32( "nCtrlChild", -1 );
			var parent = offset.GetInt32( "nCtrlParent", -1 );
			if ( child < 0 || child >= NodeNames.Length || parent < 0 || parent >= NodeNames.Length )
				continue;

			(soft[child] ??= []).Add( (parent, Math.Clamp( offset.GetFloat( "flAlpha", 1 ), 0, 1 )) );
		}

		SoftParents = soft.Select( x => x?.ToArray() ?? [] ).ToArray();

		Rods = data.GetObjects( "m_Rods" )
			.Select( x => x.GetArray( "nNode" ) is [var a, var b] ? ((int)KVValue.ToInt64( a ), (int)KVValue.ToInt64( b )) : (-1, -1) )
			.Where( x => x.Item1 >= 0 && x.Item1 < NodeNames.Length && x.Item2 >= 0 && x.Item2 < NodeNames.Length )
			.ToArray();
	}
}
