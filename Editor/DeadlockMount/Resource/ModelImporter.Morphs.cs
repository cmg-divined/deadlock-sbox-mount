using Deadlock;
using System;
using System.IO;
using System.Text.Json;

namespace Sandbox.Mounting.Deadlock;

internal sealed partial class ModelImporter
{
	private readonly List<DeadlockFlexSet> _flexSets = [];
	private readonly HashSet<string> _flexSetKeys = [];

	/// <summary>
	/// Decodes a mesh's morph deltas, keyed by flex name, or null if it has none. Records its flex rig too.
	/// </summary>
	private Dictionary<string, MorphVertexDelta[]> ReadMorphs( CompiledMesh mesh )
	{
		if ( mesh.MorphSet is not { } set )
			return null;

		AddFlexSet( set );

		var atlas = set.AtlasPath + "_c";
		if ( set.AtlasPath.Length == 0 || !mount.FileExists( atlas ) )
		{
			Log.Warning( $"{name}: mesh '{mesh.Name}' has morphs but its atlas '{set.AtlasPath}' is missing." );
			return null;
		}

		try
		{
			return set.DecodeDeltas( new CompiledTexture( mount.ReadFile( atlas ) ) );
		}
		catch ( Exception e ) when ( e is InvalidDataException or NotSupportedException )
		{
			Log.Warning( $"{name}: mesh '{mesh.Name}' morphs couldn't be decoded: {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// The morph deltas of a part whose vertices are grid cells [start, start + count), indexed by part vertex.
	/// </summary>
	private static Dictionary<string, MorphDelta[]> SliceMorphs( Dictionary<string, MorphVertexDelta[]> morphs, int start, int count )
	{
		var result = new Dictionary<string, MorphDelta[]>( StringComparer.OrdinalIgnoreCase );

		foreach ( var (morphName, deltas) in morphs )
		{
			// Mesh.AddMorph rejects unnamed morphs, and nothing could address one anyway.
			if ( string.IsNullOrWhiteSpace( morphName ) )
				continue;

			var from = LowerBound( deltas, start );
			var to = LowerBound( deltas, start + count );
			if ( from == to )
				continue;

			var slice = new MorphDelta[to - from];
			for ( var i = 0; i < slice.Length; i++ )
			{
				var delta = deltas[from + i];
				slice[i] = new MorphDelta(
					delta.Vertex - start,
					new Vector3( delta.PositionX, delta.PositionY, delta.PositionZ ),
					new Vector3( delta.NormalX, delta.NormalY, delta.NormalZ ) );
			}

			result[morphName] = slice;
		}

		return result.Count > 0 ? result : null;
	}

	/// <summary>
	/// The first delta at or after a vertex; deltas are in vertex order.
	/// </summary>
	private static int LowerBound( MorphVertexDelta[] deltas, int vertex )
	{
		int low = 0, high = deltas.Length;

		while ( low < high )
		{
			var middle = (low + high) / 2;
			if ( deltas[middle].Vertex < vertex )
				low = middle + 1;
			else
				high = middle;
		}

		return low;
	}

	private void AddFlexSet( CompiledMorphSet set )
	{
		var flexSet = new DeadlockFlexSet
		{
			Flexes = [.. set.Flexes],
			Controllers = set.Controllers.Select( x => new DeadlockFlexControllerInfo { Name = x.Name, Type = x.Type, Min = x.Min, Max = x.Max } ).ToList(),
			Rules = set.Rules.Select( x => new DeadlockFlexRule { Flex = x.Flex, OpCodes = [.. x.OpCodes], OpData = [.. x.OpData] } ).ToList(),
		};

		// Every LOD carries the same rig, so only distinct ones are kept.
		if ( _flexSetKeys.Add( JsonSerializer.Serialize( flexSet ) ) )
			_flexSets.Add( flexSet );
	}
}
