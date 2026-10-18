using System;

namespace Sandbox.Mounting.Deadlock;

internal sealed partial class ModelImporter
{
	/// <summary>
	/// Ball joint swing cones can't open any wider.
	/// </summary>
	private const float MaxSwingLimit = 89.1f;

	/// <summary>
	/// Hulls with more points than this may triangulate past the native half-edge budget.
	/// </summary>
	private const int MaxUnsimplifiedHullPoints = 44;

	/// <summary>
	/// Adds the model's rigid bodies and the joints between them, the ragdoll for characters.
	/// </summary>
	/// <param name="boneNames">Imported bone names by model bone index.</param>
	private void AddPhysics( ModelBuilder builder, string[] boneNames )
	{
		if ( model.ReadPhysics() is not { Parts.Length: > 0 } physics )
			return;

		// Bodies without shapes (heroes have one on root_motion) can't simulate, so they're left out and joints renumbered.
		var bodyIndices = new int[physics.Parts.Length];
		var bodyCount = 0;
		var surfaceSlots = new Dictionary<int, int>();

		for ( var i = 0; i < physics.Parts.Length; i++ )
		{
			var part = physics.Parts[i];
			bodyIndices[i] = -1;

			if ( part.ShapeCount == 0 )
				continue;

			var boneName = ResolveBone( part.BoneName, boneNames );
			var body = builder.AddBody( part.Mass, GetPartSurface( physics, part ), boneName )
				.SetDamping( part.LinearDamping, part.AngularDamping )
				.SetInertiaScale( part.InertiaScale > 0 && float.IsFinite( part.InertiaScale ) ? part.InertiaScale : 1 );

			if ( boneName is not null && part.BindPose is { } bindPose )
				body.SetBindPose( ToTransform( bindPose ) );

			AddShapes( builder, body, physics, part, surfaceSlots, null );
			bodyIndices[i] = bodyCount++;
		}

		foreach ( var joint in physics.Joints )
		{
			var body1 = joint.Body1 >= 0 && joint.Body1 < bodyIndices.Length ? bodyIndices[joint.Body1] : -1;
			var body2 = joint.Body2 >= 0 && joint.Body2 < bodyIndices.Length ? bodyIndices[joint.Body2] : -1;

			if ( body1 < 0 || body2 < 0 || body1 == body2 )
				continue;

			AddJoint( builder, joint, body1, body2 );
		}
	}

	/// <summary>
	/// The model's collision as one model per collision attribute, each with that attribute's "interact as"
	/// tags (solid, playerclip, sky...), which decide what it collides with. For world collision, where
	/// clips and camera blockers sit beside solid geometry in one body.
	/// </summary>
	public List<(string[] Tags, Model Model)> ImportCollisionGroups()
	{
		var result = new List<(string[] Tags, Model Model)>();
		if ( model.ReadPhysics() is not { Parts.Length: > 0 } physics )
			return result;

		var attributes = physics.Parts.SelectMany( GetShapeInfos ).Select( x => x.CollisionAttributeIndex ).Distinct().Order().ToArray();

		foreach ( var attribute in attributes )
		{
			var builder = Model.Builder.WithName( $"{name}_{attribute}" );
			var surfaceSlots = new Dictionary<int, int>();

			foreach ( var part in physics.Parts )
			{
				if ( !GetShapeInfos( part ).Any( x => x.CollisionAttributeIndex == attribute ) )
					continue;

				var body = builder.AddBody( part.Mass, GetPartSurface( physics, part, attribute ), null )
					.SetDamping( part.LinearDamping, part.AngularDamping );

				AddShapes( builder, body, physics, part, surfaceSlots, attribute );
			}

			var tags = attribute >= 0 && attribute < physics.CollisionTags.Length ? physics.CollisionTags[attribute] : [];
			result.Add( (tags, builder.Create()) );
		}

		return result;
	}

	/// <summary>
	/// Adds a part's shapes to a body, or only those of one collision attribute.
	/// </summary>
	private void AddShapes( ModelBuilder builder, PhysicsBodyBuilder body, CompiledPhysics physics, PhysicsPart part, Dictionary<int, int> surfaceSlots, int? attribute )
	{
		bool Include( PhysicsShapeInfo info ) => attribute is not { } only || info.CollisionAttributeIndex == only;

		foreach ( var sphere in part.Spheres.Where( x => Include( x.Info ) ) )
			body.AddSphere( new Sphere( ToVector( sphere.Center ), sphere.Radius ) );

		foreach ( var capsule in part.Capsules.Where( x => Include( x.Info ) ) )
			body.AddCapsule( new Capsule( ToVector( capsule.CenterA ), ToVector( capsule.CenterB ), capsule.Radius ) );

		foreach ( var hull in part.Hulls.Where( x => Include( x.Info ) ) )
		{
			if ( hull.Points.Length < 4 )
				continue;

			// s&box re-cooks hulls from points into triangles, which can exceed its 256 half-edge budget past
			// 44 points (6V-12 half-edges) and drop the hull, so those are simplified like the Source 1 mount does.
			PhysicsBodyBuilder.HullSimplify? simplify = hull.Points.Length > MaxUnsimplifiedHullPoints
				? new PhysicsBodyBuilder.HullSimplify { Method = PhysicsBodyBuilder.SimplifyMethod.IVR }
				: null;

			body.AddHull( hull.Points.Select( ToVector ).ToArray(), simplify: simplify );
		}

		foreach ( var mesh in part.Meshes.Where( x => Include( x.Info ) ) )
			AddPhysicsMesh( builder, body, physics, mesh, surfaceSlots );
	}

	private static IEnumerable<PhysicsShapeInfo> GetShapeInfos( PhysicsPart part )
	{
		return part.Capsules.Select( x => x.Info )
			.Concat( part.Spheres.Select( x => x.Info ) )
			.Concat( part.Hulls.Select( x => x.Info ) )
			.Concat( part.Meshes.Select( x => x.Info ) );
	}

	private void AddJoint( ModelBuilder builder, PhysicsJoint joint, int body1, int body2 )
	{
		// Frames and limits mean the same to s&box's joints as to Rubikon's, which s&box's own compiled models rely on too.
		var frame1 = ToTransform( joint.Frame1 );
		var frame2 = ToTransform( joint.Frame2 );

		switch ( joint.Type )
		{
			case PhysicsJointType.Conical:
			case PhysicsJointType.Spherical:
			{
				var ball = builder.AddBallJoint( body1, body2, frame1, frame2, joint.EnableCollision ).WithFriction( GetFriction( joint ) );

				if ( joint.EnableSwingLimit )
					ball.WithSwingLimit( Math.Clamp( joint.SwingLimit.RadianToDegree(), 0, MaxSwingLimit ) );

				if ( joint.EnableTwistLimit )
					ball.WithTwistLimit( joint.TwistLimitMin.RadianToDegree(), Math.Max( joint.TwistLimitMin, joint.TwistLimitMax ).RadianToDegree() );

				break;
			}

			case PhysicsJointType.Revolute:
			{
				var hinge = builder.AddHingeJoint( body1, body2, frame1, frame2, joint.EnableCollision ).WithFriction( GetFriction( joint ) );

				if ( joint.EnableTwistLimit )
					hinge.WithTwistLimit( joint.TwistLimitMin.RadianToDegree(), Math.Max( joint.TwistLimitMin, joint.TwistLimitMax ).RadianToDegree() );

				break;
			}

			case PhysicsJointType.Prismatic:
			{
				var slider = builder.AddSliderJoint( body1, body2, frame1, frame2, joint.EnableCollision ).WithFriction( GetFriction( joint ) );

				if ( joint.EnableLinearLimit )
					slider.WithLimit( joint.LinearLimitMin, Math.Max( joint.LinearLimitMin, joint.LinearLimitMax ) );

				break;
			}

			case PhysicsJointType.Weld:
				builder.AddFixedJoint( body1, body2, frame1, frame2, joint.EnableCollision );
				break;

			default:
				Log.Trace( $"{name}: skipping unsupported {joint.Type} joint between bodies {body1} and {body2}." );
				break;
		}
	}

	private static float GetFriction( PhysicsJoint joint ) => float.IsFinite( joint.Friction ) ? Math.Max( joint.Friction, 0 ) : 0;

	private void AddPhysicsMesh( ModelBuilder builder, PhysicsBodyBuilder body, CompiledPhysics physics, PhysicsMesh mesh, Dictionary<int, int> surfaceSlots )
	{
		if ( mesh.Vertices.Length < 3 || mesh.Indices.Length < 3 || mesh.Indices.Length % 3 != 0 )
			return;

		var vertices = mesh.Vertices.Select( ToVector ).ToArray();
		var indices = new uint[mesh.Indices.Length];

		for ( var i = 0; i < indices.Length; i++ )
		{
			if ( mesh.Indices[i] < 0 || mesh.Indices[i] >= vertices.Length )
				return;

			indices[i] = (uint)mesh.Indices[i];
		}

		// Meshes with several surfaces index the model's surface list per triangle; others use the body's surface.
		byte[] materials = [];
		if ( mesh.Materials.Length == indices.Length / 3 && mesh.Materials.Distinct().Skip( 1 ).Any() )
		{
			materials = new byte[mesh.Materials.Length];

			for ( var i = 0; i < materials.Length; i++ )
			{
				if ( !surfaceSlots.TryGetValue( mesh.Materials[i], out var slot ) )
				{
					slot = surfaceSlots.Count;
					surfaceSlots[mesh.Materials[i]] = slot;
					builder.AddSurface( GetSurface( physics, mesh.Materials[i] ) );
				}

				materials[i] = (byte)Math.Min( slot, byte.MaxValue );
			}
		}

		body.AddMesh( vertices, indices, materials );
	}

	/// <summary>
	/// A body has one surface; the first shape's stands for all of them.
	/// </summary>
	private Surface GetPartSurface( CompiledPhysics physics, PhysicsPart part, int? attribute = null )
	{
		var info = GetShapeInfos( part ).FirstOrDefault( x => attribute is not { } only || x.CollisionAttributeIndex == only );
		return GetSurface( physics, info.SurfaceIndex );
	}

	private Surface GetSurface( CompiledPhysics physics, int surfaceIndex )
	{
		var hashes = physics.SurfacePropertyHashes;
		return mount.GetSurface( surfaceIndex >= 0 && surfaceIndex < hashes.Length ? hashes[surfaceIndex] : 0 );
	}

	/// <summary>
	/// Adds attachment points: where weapons, effects and cameras attach.
	/// </summary>
	private void AddAttachments( ModelBuilder builder, string[] boneNames )
	{
		// Many attachments share their bone's name (muzzle_fx on bone muzzle_fx), which s&box handles fine.
		var used = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var attachment in model.ReadAttachments() )
		{
			if ( !used.Add( attachment.Name ) )
				continue;

			var bone = ResolveBone( attachment.Bone, boneNames );
			var rotation = new Rotation( attachment.Rotation.X, attachment.Rotation.Y, attachment.Rotation.Z, attachment.Rotation.W ).Normal;
			var local = new Transform( ToVector( attachment.Offset ), rotation );

			// The native builder can't place an attachment without a parent bone, so model space attachments
			// hang off the root bone, at the same place.
			if ( bone is null && Array.FindIndex( model.Bones, x => x.Parent < 0 ) is >= 0 and var root && root < boneNames.Length )
			{
				var rootBone = model.Bones[root];
				var rootTransform = new Transform(
					new Vector3( rootBone.Position[0], rootBone.Position[1], rootBone.Position[2] ),
					new Rotation( rootBone.Rotation[0], rootBone.Rotation[1], rootBone.Rotation[2], rootBone.Rotation[3] ).Normal );

				bone = boneNames[root];
				local = rootTransform.ToLocal( local );
			}

			builder.AddAttachment( attachment.Name, local.Position, local.Rotation, bone );
		}
	}

	/// <summary>
	/// Adds hitbox sets, tagged with their hit groups the way the Source mounts tag them.
	/// </summary>
	private void AddHitboxes( ModelBuilder builder, string[] boneNames )
	{
		var sets = model.ReadHitboxSets();
		if ( sets.Length == 0 )
			return;

		// The default set comes first, since the first set is the active one.
		var setNames = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var set in sets.OrderBy( x => x.Name.Equals( "default", StringComparison.OrdinalIgnoreCase ) ? 0 : 1 ) )
		{
			if ( set.Hitboxes.Length == 0 )
				continue;

			var target = builder.AddHitboxSet( UniqueName( set.Name, "default", setNames ) );
			var names = new HashSet<string>( StringComparer.Ordinal );

			foreach ( var hitbox in set.Hitboxes )
			{
				if ( ResolveBone( hitbox.Bone, boneNames ) is not { } bone )
					continue;

				var hitboxName = UniqueName( hitbox.Name, $"hitbox_{names.Count}", names );
				var min = ToVector( hitbox.Min );
				var max = ToVector( hitbox.Max );

				var added = hitbox.Shape switch
				{
					HitboxShape.Sphere => target.AddSphere( hitboxName, bone, new Sphere( min, MathF.Max( hitbox.Radius, 0 ) ) ),
					HitboxShape.Capsule => target.AddCapsule( hitboxName, bone, new Capsule( min, max, MathF.Max( hitbox.Radius, 0 ) ) ),
					_ => target.AddBox( hitboxName, bone, new BBox( min, max ) ),
				};

				added.WithSurface( mount.GetSurface( hitbox.SurfaceProperty ).ResourceName ).AddTag( $"hitgroup_{hitbox.Group}" );

				if ( GetHitGroupTag( hitbox.Group ) is { } tag )
					added.AddTag( tag );
			}
		}
	}

	private static string GetHitGroupTag( int group ) => group switch
	{
		0 => "generic",
		1 => "head",
		2 => "chest",
		3 => "stomach",
		4 => "left_arm",
		5 => "right_arm",
		6 => "left_leg",
		7 => "right_leg",
		10 => "gear",
		_ => null,
	};

	private static string UniqueName( string name, string fallback, HashSet<string> used )
	{
		var source = string.IsNullOrWhiteSpace( name ) ? fallback : name;
		var result = source;

		for ( var suffix = 1; !used.Add( result ); suffix++ )
			result = $"{source}_{suffix}";

		return result;
	}

	/// <summary>
	/// The imported name of a model bone, or null if there's no such bone.
	/// </summary>
	private string ResolveBone( string boneName, string[] boneNames )
	{
		if ( string.IsNullOrEmpty( boneName ) )
			return null;

		var index = Array.FindIndex( model.Bones, x => x.Name == boneName );
		if ( index < 0 )
			index = Array.FindIndex( model.Bones, x => string.Equals( x.Name, boneName, StringComparison.OrdinalIgnoreCase ) );

		return index >= 0 && index < boneNames.Length ? boneNames[index] : null;
	}

	private static Vector3 ToVector( System.Numerics.Vector3 value ) => new( value.X, value.Y, value.Z );

	private static Transform ToTransform( PhysicsFrame frame )
	{
		var rotation = new Rotation( frame.Rotation.X, frame.Rotation.Y, frame.Rotation.Z, frame.Rotation.W ).Normal;
		return new Transform( ToVector( frame.Position ), rotation );
	}

	/// <summary>
	/// A 3x4 row major matrix: the columns are the X, Y and Z axes, then the translation.
	/// </summary>
	private static Transform ToTransform( float[] matrix )
	{
		var forward = new Vector3( matrix[0], matrix[4], matrix[8] ).Normal;
		var up = new Vector3( matrix[2], matrix[6], matrix[10] ).Normal;
		return new Transform( new Vector3( matrix[3], matrix[7], matrix[11] ), Rotation.LookAt( forward, up ) );
	}
}
