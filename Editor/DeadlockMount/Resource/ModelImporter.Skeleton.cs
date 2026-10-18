using System;

namespace Sandbox.Mounting.Deadlock;

internal sealed partial class ModelImporter
{
	/// <summary>
	/// Vertices address bones with 8 bit indices.
	/// </summary>
	private const int MaxSkinningBones = 256;

	/// <summary>
	/// How the model's skeleton maps onto the imported one.
	/// </summary>
	private sealed class BoneLayout
	{
		/// <summary>
		/// Model skeleton bone index for each imported bone, parents always before their children.
		/// </summary>
		public int[] Order;

		/// <summary>
		/// The imported bone index vertices skinned to each model bone use. This is the bone itself, unless it
		/// didn't fit in the first <see cref="MaxSkinningBones"/>, then it's the closest ancestor that did.
		/// </summary>
		public int[] SkinTarget;
	}

	private BoneLayout BuildSkeleton( List<MeshPart> parts )
	{
		var bones = model.Bones;
		var count = bones.Length;
		var kept = new bool[count];

		if ( count <= MaxSkinningBones )
		{
			Array.Fill( kept, true );
		}
		else if ( ChooseSkinningBones( parts, kept ) is var dropped and > 0 )
		{
			Log.Warning( $"{name}: {dropped} of its {count} bones don't fit in the {MaxSkinningBones} vertices can use, so they're skinned to their nearest kept parent." );
		}

		// Bones vertices can use go first, so they get the low indices.
		var order = new List<int>( count );
		var state = new VisitState[count];

		for ( var i = 0; i < count; i++ )
		{
			if ( kept[i] )
				Emit( i, order, state );
		}

		for ( var i = 0; i < count; i++ )
			Emit( i, order, state );

		var importedIndex = new int[count];
		for ( var i = 0; i < order.Count; i++ )
			importedIndex[order[i]] = i;

		var skinTarget = new int[count];

		for ( var i = 0; i < count; i++ )
		{
			var target = SelfAndAncestors( i ).FirstOrDefault( x => kept[x], -1 );
			skinTarget[i] = target >= 0 ? importedIndex[target] : 0;
		}

		return new BoneLayout { Order = [.. order], SkinTarget = skinTarget };
	}

	/// <summary>
	/// Picks which bones vertices may use when the skeleton is too big, most influential first, always
	/// keeping a bone's parents with it.
	/// </summary>
	/// <returns>The number of bones with influence that didn't fit.</returns>
	private int ChooseSkinningBones( List<MeshPart> parts, bool[] kept )
	{
		var influence = new double[model.Bones.Length];

		foreach ( var part in parts )
		{
			if ( part.Bones is null )
				continue;

			for ( var i = 0; i < part.Bones.Length; i++ )
				influence[part.Bones[i]] += part.Weights[i];
		}

		var keptCount = 0;
		var dropped = 0;

		foreach ( var bone in Enumerable.Range( 0, influence.Length ).Where( x => influence[x] > 0 ).OrderByDescending( x => influence[x] ) )
		{
			var missing = SelfAndAncestors( bone ).TakeWhile( x => !kept[x] ).ToArray();
			if ( keptCount + missing.Length > MaxSkinningBones )
			{
				dropped++;
				continue;
			}

			foreach ( var b in missing )
				kept[b] = true;

			keptCount += missing.Length;
		}

		return dropped;
	}

	/// <summary>
	/// A bone followed by its parents up to the root. Stops early on a malformed parent loop.
	/// </summary>
	private IEnumerable<int> SelfAndAncestors( int bone )
	{
		for ( var steps = 0; bone >= 0 && steps < model.Bones.Length; steps++ )
		{
			yield return bone;
			bone = GetParent( bone );
		}
	}

	/// <summary>
	/// Adds a bone to <paramref name="order"/> after its parents.
	/// </summary>
	private void Emit( int bone, List<int> order, VisitState[] state )
	{
		// Visiting bones are skipped, so a malformed parent loop can't recurse forever.
		if ( state[bone] != VisitState.New )
			return;

		state[bone] = VisitState.Visiting;

		if ( GetParent( bone ) is var parent and >= 0 )
			Emit( parent, order, state );

		state[bone] = VisitState.Done;
		order.Add( bone );
	}

	private enum VisitState : byte
	{
		New,
		Visiting,
		Done,
	}

	private int GetParent( int bone )
	{
		var parent = model.Bones[bone].Parent;
		return parent >= 0 && parent < model.Bones.Length && parent != bone ? parent : -1;
	}

	/// <summary>
	/// Adds the skeleton, returning each model bone's imported name; duplicate names get a suffix.
	/// </summary>
	private string[] AddBones( ModelBuilder builder, BoneLayout skeleton )
	{
		var bones = model.Bones;
		if ( bones.Length == 0 )
			return [];

		// The builder wants bones in model space.
		var transforms = new Transform[bones.Length];
		var names = new string[bones.Length];
		var usedNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var result = new ModelBuilder.Bone[bones.Length];

		for ( var i = 0; i < skeleton.Order.Length; i++ )
		{
			var index = skeleton.Order[i];
			var bone = bones[index];
			var parent = GetParent( index );

			// Only possible with a parent loop, which leaves the bone at the root.
			if ( parent >= 0 && names[parent] is null )
				parent = -1;

			var local = new Transform(
				new Vector3( bone.Position[0], bone.Position[1], bone.Position[2] ),
				new Rotation( bone.Rotation[0], bone.Rotation[1], bone.Rotation[2], bone.Rotation[3] ).Normal );

			transforms[index] = parent >= 0 ? transforms[parent].ToWorld( local ) : local;

			// Parents are found by name, so names have to be unique.
			var boneName = string.IsNullOrWhiteSpace( bone.Name ) ? $"bone_{index}" : bone.Name;
			for ( var suffix = 1; !usedNames.Add( boneName ); suffix++ )
				boneName = $"{bone.Name}_{suffix}";

			names[index] = boneName;
			result[i] = new ModelBuilder.Bone( boneName, parent >= 0 ? names[parent] : null, transforms[index].Position, transforms[index].Rotation );
		}

		builder.AddBones( result );
		return names;
	}
}
