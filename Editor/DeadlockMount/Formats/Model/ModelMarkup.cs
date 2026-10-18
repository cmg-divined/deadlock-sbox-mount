using System;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// A named point on the skeleton. Offset and rotation are relative to the bone, or the model when Bone is null.
/// </summary>
/// <param name="InfluenceCount">How many bones the attachment blends between; only the strongest is kept here.</param>
internal sealed record ModelAttachment( string Name, string Bone, NVector3 Offset, NQuaternion Rotation, int InfluenceCount );

internal enum HitboxShape
{
	Box = 0,
	Sphere = 1,
	Capsule = 2,
}

/// <summary>
/// A hitbox in its bone's space. Boxes span Min to Max, spheres are centered on Min, capsules run from Min to Max.
/// </summary>
/// <param name="Group">The hit group: 1 head, 2 chest, 3 stomach, 4 and 5 arms, 6 and 7 legs, as in Source.</param>
internal sealed record ModelHitbox( string Name, string Bone, string SurfaceProperty, HitboxShape Shape, NVector3 Min, NVector3 Max, float Radius, int Group );

internal sealed record ModelHitboxSet( string Name, ModelHitbox[] Hitboxes );

/// <summary>
/// The attachments and hitbox sets a model's meshes carry (CRenderMesh m_attachments and m_hitboxsets).
/// Every mesh of a model repeats the same markup, so the first definition of each name wins.
/// </summary>
internal static class ModelMarkup
{
	public static ModelAttachment[] ReadAttachments( IEnumerable<KVObject> meshes )
	{
		var result = new Dictionary<string, ModelAttachment>( StringComparer.Ordinal );

		foreach ( var attachment in meshes.SelectMany( x => ReadMap( x, "m_attachments" ) ) )
		{
			var name = attachment.GetString( "m_name", "" );
			if ( name.Length == 0 || result.ContainsKey( name ) )
				continue;

			var count = attachment.GetInt32( "m_nInfluences", 1 );
			var names = attachment.GetStringArray( "m_influenceNames" );
			var offsets = attachment.GetArray( "m_vInfluenceOffsets" ) ?? [];
			var rotations = attachment.GetArray( "m_vInfluenceRotations" ) ?? [];
			var weights = attachment.GetFloatArray( "m_influenceWeights" );
			var rootTransform = attachment.GetArray( "m_bInfluenceRootTransform" ) ?? [];

			// Blended attachments keep their strongest influence.
			var best = 0;
			for ( var i = 1; i < count && i < weights.Length; i++ )
			{
				if ( weights[i] > weights[best] )
					best = i;
			}

			var bone = best < names.Length && names[best].Length > 0 ? names[best] : null;
			if ( best < rootTransform.Length && KVValue.ToBool( rootTransform[best] ) )
				bone = null;

			var offset = ReadFloats( best < offsets.Length ? offsets[best] : null, 3 );
			var rotation = ReadFloats( best < rotations.Length ? rotations[best] : null, 4, w: 1 );

			result[name] = new ModelAttachment( name, bone,
				new NVector3( offset[0], offset[1], offset[2] ),
				NQuaternion.Normalize( new NQuaternion( rotation[0], rotation[1], rotation[2], rotation[3] ) ),
				Math.Max( count, 1 ) );
		}

		return [.. result.Values];
	}

	public static ModelHitboxSet[] ReadHitboxSets( IEnumerable<KVObject> meshes )
	{
		var result = new Dictionary<string, ModelHitboxSet>( StringComparer.Ordinal );

		foreach ( var set in meshes.SelectMany( x => ReadMap( x, "m_hitboxsets" ) ) )
		{
			var name = set.GetString( "m_name", "" );
			if ( result.ContainsKey( name ) )
				continue;

			var hitboxes = set.GetObjects( "m_HitBoxes" ).Select( x => new ModelHitbox(
				x.GetString( "m_name", "" ),
				x.GetString( "m_sBoneName", "" ),
				x.GetString( "m_sSurfaceProperty", "" ),
				(HitboxShape)x.GetInt32( "m_nShapeType" ),
				ReadVector( x["m_vMinBounds"] ),
				ReadVector( x["m_vMaxBounds"] ),
				x.GetFloat( "m_flShapeRadius" ),
				x.GetInt32( "m_nGroupId" ) ) ).ToArray();

			result[name] = new ModelHitboxSet( name, hitboxes );
		}

		return [.. result.Values];
	}

	/// <summary>
	/// A map's values: stored as an array of key/value pairs, or as an object.
	/// </summary>
	private static IEnumerable<KVObject> ReadMap( KVObject data, string key )
	{
		return data[key] switch
		{
			object[] pairs => pairs.OfType<KVObject>().Select( x => x.GetObject( "value" ) ?? x ),
			KVObject map => map.Keys.Select( map.GetObject ).Where( x => x is not null ),
			_ => [],
		};
	}

	private static NVector3 ReadVector( object value )
	{
		var floats = ReadFloats( value, 3 );
		return new NVector3( floats[0], floats[1], floats[2] );
	}

	private static float[] ReadFloats( object value, int count, float w = 0 )
	{
		var result = new float[count];
		if ( count == 4 )
			result[3] = w;

		if ( value is object[] array )
		{
			for ( var i = 0; i < count && i < array.Length; i++ )
				result[i] = KVValue.ToFloat( array[i] );
		}

		return result;
	}
}
