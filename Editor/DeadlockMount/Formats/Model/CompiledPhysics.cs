using System;
using System.Buffers.Binary;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// A rigid transform as Rubikon stores joint frames: position, then rotation.
/// </summary>
internal readonly record struct PhysicsFrame( NVector3 Position, NQuaternion Rotation );

/// <summary>
/// What every shape carries besides its geometry: indices into the aggregate's surface hashes and collision attributes.
/// </summary>
internal readonly record struct PhysicsShapeInfo( int SurfaceIndex, int CollisionAttributeIndex );

internal sealed record PhysicsSphere( PhysicsShapeInfo Info, NVector3 Center, float Radius );

internal sealed record PhysicsCapsule( PhysicsShapeInfo Info, NVector3 CenterA, NVector3 CenterB, float Radius );

internal sealed record PhysicsHull( PhysicsShapeInfo Info, NVector3[] Points );

/// <summary>
/// A triangle mesh. Materials are per triangle, indexing the aggregate's surface hashes; empty when uniform.
/// </summary>
internal sealed record PhysicsMesh( PhysicsShapeInfo Info, NVector3[] Vertices, int[] Indices, int[] Materials );

/// <summary>
/// One rigid body of a physics aggregate, in the space of its bone (or the model, for unboned bodies).
/// </summary>
internal sealed class PhysicsPart
{
	/// <summary>
	/// The bone this body follows, or null for a body that isn't attached to the skeleton.
	/// </summary>
	public string BoneName;

	/// <summary>
	/// The body's model space transform in the bind pose: rotation rows then translation, 3x4 row major.
	/// Null when the aggregate has no bind poses (a single unboned body).
	/// </summary>
	public float[] BindPose;

	/// <summary>
	/// The mass in kilograms, zero to derive it from volume and surface density.
	/// </summary>
	public float Mass;

	public float InertiaScale = 1;
	public float LinearDamping;
	public float AngularDamping;
	public int CollisionAttributeIndex;

	public List<PhysicsSphere> Spheres = [];
	public List<PhysicsCapsule> Capsules = [];
	public List<PhysicsHull> Hulls = [];
	public List<PhysicsMesh> Meshes = [];

	public int ShapeCount => Spheres.Count + Capsules.Count + Hulls.Count + Meshes.Count;
}

/// <summary>
/// Rubikon joint types, as stored in compiled models.
/// </summary>
internal enum PhysicsJointType
{
	Null = 0,
	Spherical = 1,
	Prismatic = 2,
	Revolute = 3,

	/// <summary>
	/// The ragdoll joint: a swing cone plus a twist range.
	/// </summary>
	Conical = 4,

	Weld = 6,
	Wheel = 16,
}

/// <summary>
/// A joint between two bodies. Frames are in each body's space; angular limits are in radians.
/// </summary>
internal sealed record PhysicsJoint(
	PhysicsJointType Type,
	int Body1,
	int Body2,
	PhysicsFrame Frame1,
	PhysicsFrame Frame2,
	bool EnableCollision,
	bool EnableLinearLimit,
	float LinearLimitMin,
	float LinearLimitMax,
	bool EnableSwingLimit,
	float SwingLimit,
	bool EnableTwistLimit,
	float TwistLimitMin,
	float TwistLimitMax,
	float Friction,
	bool EnableAngularMotor,
	float MaxTorque,
	float AngularFrequency,
	float AngularDampingRatio );

/// <summary>
/// A model's physics (PHYS block, VPhysXAggregateData_t): bodies with their collision shapes, and the
/// joints between them. Character ragdolls are one body per simulated bone.
/// </summary>
internal sealed class CompiledPhysics
{
	public PhysicsPart[] Parts { get; }

	public PhysicsJoint[] Joints { get; }

	/// <summary>
	/// Surface property name hashes (MurmurHash2, lower case), indexed by <see cref="PhysicsShapeInfo.SurfaceIndex"/>.
	/// </summary>
	public uint[] SurfacePropertyHashes { get; }

	/// <summary>
	/// Each collision attribute's "interact as" tags, such as <c>player</c> or <c>hitbox</c>.
	/// </summary>
	public string[][] CollisionTags { get; }

	public CompiledPhysics( KVObject data )
	{
		var boneNames = data.GetStringArray( "m_boneNames" );
		var bindPoses = data.GetArray( "m_bindPose" ) ?? [];
		var parts = data.GetObjects( "m_parts" ).ToArray();

		Parts = new PhysicsPart[parts.Length];
		for ( var i = 0; i < parts.Length; i++ )
		{
			Parts[i] = ReadPart( parts[i] );
			Parts[i].BoneName = i < boneNames.Length && boneNames[i].Length > 0 ? boneNames[i] : null;
			Parts[i].BindPose = i < bindPoses.Length ? ReadFloats( bindPoses[i], 12 ) : null;
		}

		Joints = data.GetObjects( "m_joints" ).Select( ReadJoint ).ToArray();
		SurfacePropertyHashes = data.GetInt64Array( "m_surfacePropertyHashes" ).Select( x => unchecked((uint)x) ).ToArray();
		CollisionTags = data.GetObjects( "m_collisionAttributes" )
			.Select( x => x.ContainsKey( "m_InteractAsStrings" ) ? x.GetStringArray( "m_InteractAsStrings" ) : x.GetStringArray( "m_PhysicsTagStrings" ) )
			.ToArray();
	}

	private static PhysicsPart ReadPart( KVObject data )
	{
		var part = new PhysicsPart
		{
			Mass = data.GetFloat( "m_flMass" ),
			InertiaScale = data.GetFloat( "m_flInertiaScale", 1 ),
			LinearDamping = data.GetFloat( "m_flLinearDamping" ),
			AngularDamping = data.GetFloat( "m_flAngularDamping" ),
			CollisionAttributeIndex = data.GetInt32( "m_nCollisionAttributeIndex" ),
		};

		if ( data.GetObject( "m_rnShape" ) is { } shape )
			ReadShapes( part, shape, null );

		return part;
	}

	/// <summary>
	/// Adds a shape container's shapes to a part. Compounds nest further containers, which take on the
	/// compound's own surface and attributes.
	/// </summary>
	private static void ReadShapes( PhysicsPart part, KVObject shapes, PhysicsShapeInfo? inherited )
	{
		foreach ( var (info, sphere) in ReadDescriptors( shapes, "m_spheres", "m_Spheres", "m_Sphere", inherited ) )
			part.Spheres.Add( new PhysicsSphere( info, ReadVector( sphere["m_vCenter"] ), sphere.GetFloat( "m_flRadius" ) ) );

		foreach ( var (info, capsule) in ReadDescriptors( shapes, "m_capsules", "m_Capsules", "m_Capsule", inherited ) )
		{
			var centers = capsule.GetArray( "m_vCenter" ) ?? [];
			part.Capsules.Add( new PhysicsCapsule( info,
				centers.Length > 0 ? ReadVector( centers[0] ) : default,
				centers.Length > 1 ? ReadVector( centers[1] ) : default,
				capsule.GetFloat( "m_flRadius" ) ) );
		}

		foreach ( var (info, hull) in ReadDescriptors( shapes, "m_hulls", "m_Hulls", "m_Hull", inherited ) )
			part.Hulls.Add( new PhysicsHull( info, ReadHullPoints( hull ) ) );

		foreach ( var (info, mesh) in ReadDescriptors( shapes, "m_meshes", "m_Meshes", "m_Mesh", inherited ) )
		{
			var triangles = ReadInts( mesh["m_Triangles"], "m_nIndex" );
			var materials = mesh["m_Materials"] is { } m ? ReadInts( m, null ) : [];
			part.Meshes.Add( new PhysicsMesh( info, ReadVertices( mesh ), triangles, materials ) );
		}

		foreach ( var (info, compound) in ReadDescriptors( shapes, "m_compounds", null, "m_Compound", inherited ) )
			ReadShapes( part, compound, info );
	}

	/// <summary>
	/// A container's shapes of one kind. Top level shapes are descriptors wrapping the geometry with their
	/// own surface and attributes; shapes inside a compound are bare geometry.
	/// </summary>
	private static IEnumerable<(PhysicsShapeInfo Info, KVObject Shape)> ReadDescriptors( KVObject shapes, string key, string compoundKey,
		string member, PhysicsShapeInfo? inherited )
	{
		if ( inherited is { } info )
		{
			if ( compoundKey is null )
				yield break;

			foreach ( var shape in shapes.GetObjects( compoundKey ) )
				yield return (info, shape);

			yield break;
		}

		foreach ( var descriptor in shapes.GetObjects( key ) )
		{
			if ( descriptor.GetObject( member ) is not { } shape )
				continue;

			yield return (new PhysicsShapeInfo( descriptor.GetInt32( "m_nSurfacePropertyIndex" ), descriptor.GetInt32( "m_nCollisionAttributeIndex" ) ), shape);
		}
	}

	/// <summary>
	/// Hull points: since late 2023 in m_VertexPositions, with m_Vertices becoming per vertex edge indices.
	/// </summary>
	private static NVector3[] ReadHullPoints( KVObject hull )
	{
		return hull.ContainsKey( "m_VertexPositions" ) ? ReadVectors( hull["m_VertexPositions"] ) : ReadVectors( hull["m_Vertices"] );
	}

	private static NVector3[] ReadVertices( KVObject mesh ) => ReadVectors( mesh["m_Vertices"] );

	/// <summary>
	/// Vectors stored either as a blob of packed floats or as an array of three element arrays.
	/// </summary>
	private static NVector3[] ReadVectors( object value )
	{
		if ( value is byte[] blob )
		{
			var result = new NVector3[blob.Length / 12];
			for ( var i = 0; i < result.Length; i++ )
			{
				result[i] = new NVector3(
					BinaryPrimitives.ReadSingleLittleEndian( blob.AsSpan( i * 12 ) ),
					BinaryPrimitives.ReadSingleLittleEndian( blob.AsSpan( i * 12 + 4 ) ),
					BinaryPrimitives.ReadSingleLittleEndian( blob.AsSpan( i * 12 + 8 ) ) );
			}

			return result;
		}

		return value is object[] array ? array.Select( ReadVector ).ToArray() : [];
	}

	/// <summary>
	/// Integers stored as a blob of int32s, a flat array, or an array of objects with an index array member.
	/// </summary>
	private static int[] ReadInts( object value, string member )
	{
		if ( value is byte[] blob )
		{
			var result = new int[blob.Length / 4];
			for ( var i = 0; i < result.Length; i++ )
				result[i] = BinaryPrimitives.ReadInt32LittleEndian( blob.AsSpan( i * 4 ) );

			return result;
		}

		if ( value is not object[] array )
			return [];

		if ( member is not null && array.FirstOrDefault() is KVObject )
			return array.OfType<KVObject>().SelectMany( x => x.GetInt64Array( member ) ).Select( x => (int)x ).ToArray();

		return array.Select( x => (int)KVValue.ToInt64( x ) ).ToArray();
	}

	private static PhysicsJoint ReadJoint( KVObject joint )
	{
		var linear = joint.GetObject( "m_LinearLimit" );
		var swing = joint.GetObject( "m_SwingLimit" );
		var twist = joint.GetObject( "m_TwistLimit" );

		return new PhysicsJoint(
			(PhysicsJointType)joint.GetInt32( "m_nType" ),
			joint.GetInt32( "m_nBody1" ),
			joint.GetInt32( "m_nBody2" ),
			ReadFrame( joint["m_Frame1"] ),
			ReadFrame( joint["m_Frame2"] ),
			joint.GetBool( "m_bEnableCollision" ),
			joint.GetBool( "m_bEnableLinearLimit" ),
			linear?.GetFloat( "m_flMin" ) ?? 0,
			linear?.GetFloat( "m_flMax" ) ?? 0,
			joint.GetBool( "m_bEnableSwingLimit" ),
			swing?.GetFloat( "m_flMax" ) ?? 0,
			joint.GetBool( "m_bEnableTwistLimit" ),
			twist?.GetFloat( "m_flMin" ) ?? 0,
			twist?.GetFloat( "m_flMax" ) ?? 0,
			joint.GetFloat( "m_flFriction" ),
			joint.GetBool( "m_bEnableAngularMotor" ),
			joint.GetFloat( "m_flMaxTorque" ),
			joint.GetFloat( "m_flAngularFrequency" ),
			joint.GetFloat( "m_flAngularDampingRatio" ) );
	}

	/// <summary>
	/// A CTransform: position, a padding or scale float, then the rotation quaternion; 7 floats without the padding.
	/// </summary>
	private static PhysicsFrame ReadFrame( object value )
	{
		var floats = ReadFloats( value, 8 );
		if ( value is object[] { Length: 7 } )
			return new PhysicsFrame( new NVector3( floats[0], floats[1], floats[2] ), new NQuaternion( floats[3], floats[4], floats[5], floats[6] ) );

		return new PhysicsFrame( new NVector3( floats[0], floats[1], floats[2] ), new NQuaternion( floats[4], floats[5], floats[6], floats[7] ) );
	}

	private static NVector3 ReadVector( object value )
	{
		var floats = ReadFloats( value, 3 );
		return new NVector3( floats[0], floats[1], floats[2] );
	}

	private static float[] ReadFloats( object value, int count )
	{
		var result = new float[count];
		if ( value is object[] array )
		{
			for ( var i = 0; i < count && i < array.Length; i++ )
				result[i] = KVValue.ToFloat( array[i] );
		}

		return result;
	}
}
