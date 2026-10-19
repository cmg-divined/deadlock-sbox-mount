using System;

namespace Sandbox.Mounting.Deadlock;

internal sealed partial class ModelImporter
{
	/// <summary>
	/// Most bones one cloth bone's vertices move over to; the lightest past this are left out.
	/// </summary>
	private const int MaxClothAnchors = 8;

	/// <summary>
	/// Per model bone, the bones (and shares) its vertices skin to instead, or null where it skins its own.
	/// </summary>
	private (int Bone, float Weight)[][] _clothAnchors;

	/// <summary>
	/// s&amp;box can't simulate cloth, and nothing animates the bones the cloth compiler adds for simulated vertices (at
	/// the skeleton's root), so their vertices would stay behind in the bind pose. They skin to what the cloth hangs
	/// from instead: the static nodes the fewest rods away (its pins, at a waist, a shoulder), each with its own
	/// skinning. Their own animated pose (<see cref="CompiledCloth.SkeletonParents"/>) isn't used: the game barely pulls
	/// cloth toward it, and long skirts and coat tails skinned to it swing out with the legs. Simulated bones inside
	/// the skeleton (chains under a bone) keep their bind pose under their parents.
	/// </summary>
	private void ReadClothAnchors()
	{
		if ( model.ReadCloth() is not { } cloth )
			return;

		var bones = model.Bones;
		var boneIndex = new Dictionary<string, int>( StringComparer.Ordinal );
		for ( var i = 0; i < bones.Length; i++ )
			boneIndex.TryAdd( bones[i].Name, i );

		var nodeBone = cloth.NodeNames.Select( x => boneIndex.GetValueOrDefault( x, -1 ) ).ToArray();
		bool IsLeftBehind( int node ) => node >= cloth.StaticNodeCount && nodeBone[node] >= 0 && GetParent( nodeBone[node] ) < 0;

		var neighbours = new List<int>[cloth.NodeNames.Length];
		foreach ( var (a, b) in cloth.Rods )
		{
			(neighbours[a] ??= []).Add( b );
			(neighbours[b] ??= []).Add( a );
		}

		var resolved = new Dictionary<int, Dictionary<int, float>>();

		// A node's animated position as bone weights: its own bone, or its parents' blend (nodes that aren't bones, or
		// are left behind themselves, resolve through theirs). A loop resolves to nothing.
		Dictionary<int, float> Resolve( int node )
		{
			if ( resolved.TryGetValue( node, out var weights ) )
				return weights;

			resolved[node] = weights = [];

			if ( nodeBone[node] >= 0 && !IsLeftBehind( node ) )
			{
				weights[nodeBone[node]] = 1;
				return weights;
			}

			var parents = new List<(int Node, float Weight)>();
			if ( cloth.SkeletonParents[node] >= 0 )
				parents.Add( (cloth.SkeletonParents[node], 1) );

			foreach ( var (parent, alpha) in cloth.SoftParents[node] )
			{
				for ( var i = 0; i < parents.Count; i++ )
					parents[i] = (parents[i].Node, parents[i].Weight * alpha);

				parents.Add( (parent, 1 - alpha) );
			}

			foreach ( var (parent, share) in parents )
			{
				// A copy: in a loop, the parent's weights can be this node's own, still being added to.
				foreach ( var (bone, weight) in Resolve( parent ).ToArray() )
					weights[bone] = weights.GetValueOrDefault( bone ) + share * weight;
			}

			return weights;
		}

		// The static nodes the fewest rods from a simulated one, or none if rods don't reach any.
		List<int> Pins( int node )
		{
			var pins = new List<int>();
			var hops = new Dictionary<int, int> { [node] = 0 };
			var queue = new Queue<int>();
			queue.Enqueue( node );

			while ( queue.TryDequeue( out var current ) )
			{
				if ( pins.Count > 0 && hops[current] >= hops[pins[0]] )
					break;

				foreach ( var next in neighbours[current] ?? [] )
				{
					if ( !hops.TryAdd( next, hops[current] + 1 ) )
						continue;

					if ( next < cloth.StaticNodeCount )
						pins.Add( next );
					else
						queue.Enqueue( next );
				}
			}

			return pins;
		}

		for ( var node = cloth.StaticNodeCount; node < cloth.NodeNames.Length; node++ )
		{
			if ( !IsLeftBehind( node ) )
				continue;

			// Without rods to a pin, the node's own animated pose.
			var pins = Pins( node );
			var weights = new Dictionary<int, float>();
			foreach ( var source in pins.Count > 0 ? pins : [node] )
			{
				foreach ( var (bone, weight) in Resolve( source ) )
					weights[bone] = weights.GetValueOrDefault( bone ) + weight;
			}

			var anchors = weights.Where( x => x.Value > 0 ).OrderByDescending( x => x.Value ).Take( MaxClothAnchors ).ToArray();
			var total = anchors.Sum( x => x.Value );
			if ( total <= 0 )
				continue;

			_clothAnchors ??= new (int, float)[bones.Length][];
			_clothAnchors[nodeBone[node]] = anchors.Select( x => (x.Key, x.Value / total) ).ToArray();
		}
	}
}
