using Deadlock;
using System;
using System.Numerics;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Builds an s&amp;box <see cref="Model"/> from a compiled Deadlock model. Its materials' large textures are
/// <paramref name="textureLevel"/> mips down (see <see cref="DeadlockMount.LoadTexture(string, int)"/>).
/// </summary>
internal sealed partial class ModelImporter( DeadlockMount mount, CompiledModel model, string name, int textureLevel = 0 )
{
	internal const int MaxLods = 8;
	private const int MaxInfluences = 4;
	private const int AllLods = (1 << MaxLods) - 1;

	/// <summary>
	/// One draw call's geometry. Bone influences still use model skeleton indices, they are mapped
	/// to the imported skeleton once every part has been read.
	/// </summary>
	private sealed class MeshPart
	{
		public string Material;
		public int LodMask;
		public DeadlockStaticVertex[] Vertices;
		public int[] Indices;
		public BBox Bounds;

		/// <summary>
		/// <see cref="MaxInfluences"/> bones and weights per vertex, or null for unskinned parts.
		/// </summary>
		public int[] Bones;
		public byte[] Weights;

		/// <summary>
		/// The first source vertex this part keeps; part vertex 0 is this one.
		/// </summary>
		public int FirstVertex;

		/// <summary>
		/// Morph deltas by flex name, indexed by part vertex, or null without morphs.
		/// </summary>
		public Dictionary<string, MorphDelta[]> Morphs;

		/// <summary>
		/// The body group choices this part shows in, or null when the model has no body groups.
		/// </summary>
		public (string Group, int Choice)[] Choices;

		/// <summary>
		/// Whether the part shows with the default body group choices.
		/// </summary>
		public bool IsDefault;
	}

	public Model Import()
	{
		var builder = Model.Builder.WithName( name );
		ReadBodyGroups();

		var parts = ReadParts();
		var skeleton = BuildSkeleton( parts );

		AddBodyGroups( builder, parts.Any( x => x.Choices is [(var group, _)] && group == _alwaysVisibleGroup ) );

		// Shift LODs down if the most detailed levels are empty, so something always draws up close.
		var combinedLods = parts.Aggregate( 0, ( mask, part ) => part.LodMask == AllLods ? mask : mask | part.LodMask );
		var lowestLod = combinedLods == 0 ? 0 : BitOperations.TrailingZeroCount( combinedLods );
		var lodCount = combinedLods == 0 ? 1 : 32 - BitOperations.LeadingZeroCount( (uint)combinedLods ) - lowestLod;

		for ( var lod = 1; lod < lodCount && lod + lowestLod < model.LodSwitchDistances.Length; lod++ )
			builder.WithLodDistance( lod, model.LodSwitchDistances[lod + lowestLod] );

		BBox? bounds = null;

		foreach ( var part in parts )
		{
			var lodMask = part.LodMask == AllLods ? AllLods : part.LodMask >> lowestLod;
			var mesh = CreateMesh( part, skeleton );
			var inEveryLod = (lodMask & ((1 << lodCount) - 1)) == (1 << lodCount) - 1;

			if ( part.Choices is null )
			{
				if ( inEveryLod )
				{
					builder.AddMesh( mesh );
				}
				else
				{
					for ( var lod = 0; lod < MaxLods; lod++ )
					{
						if ( (lodMask & (1 << lod)) != 0 )
							builder.AddMesh( mesh, lod );
					}
				}
			}
			else
			{
				// A body group mesh goes in once per choice and LOD it shows in.
				foreach ( var (group, choice) in part.Choices )
				{
					for ( var lod = 0; lod < MaxLods; lod++ )
					{
						if ( inEveryLod ? lod < lodCount : (lodMask & (1 << lod)) != 0 )
							builder.AddMesh( mesh, lod, group, choice );
					}
				}
			}

			if ( (lodMask & 1) != 0 && part.IsDefault )
			{
				var positions = part.Vertices.Select( x => x.Position ).ToArray();
				builder.AddTraceMesh( positions, part.Indices );
				bounds = bounds?.AddBBox( part.Bounds ) ?? part.Bounds;
			}
		}

		AddMaterialGroups( builder );

		var boneNames = AddBones( builder, skeleton );
		AddAttachments( builder, boneNames );
		AddHitboxes( builder, boneNames );
		AddPhysics( builder, boneNames );

		if ( bounds is { } viewBounds )
			builder.WithViewBounds( viewBounds );

		if ( _flexSets.Count > 0 )
			builder.WithData( new DeadlockFlexRig { Sets = _flexSets } );

		// The AnimGraph 2 graphs the game plays the model with, for the Deadlock Animator.
		var graphs = model.Data.GetObjects( "m_animGraph2Refs" )
			.Select( x => new DeadlockAnimGraphRef { Identifier = x.GetString( "m_sIdentifier" ), Graph = x.GetString( "m_hGraph" ) } )
			.Where( x => !string.IsNullOrEmpty( x.Graph ) )
			.ToList();

		if ( graphs.Count > 0 )
			builder.WithData( new DeadlockAnimGraphs { Graphs = graphs } );

		return builder.Create();
	}

	/// <summary>
	/// Adds the model's skins. Every group lists a material per slot, replacing the first group's material in that slot.
	/// </summary>
	private void AddMaterialGroups( ModelBuilder builder )
	{
		var groups = model.MaterialGroups.ToArray();
		if ( groups.Length <= 1 )
			return;

		foreach ( var (groupName, materials) in groups )
		{
			var group = builder.AddMaterialGroup( groupName );
			foreach ( var material in materials )
				group.AddMaterial( mount.LoadMaterial( material, textureLevel ) );
		}
	}

	private List<MeshPart> ReadParts()
	{
		var parts = new List<MeshPart>();

		foreach ( var mesh in model.Meshes )
		{
			if ( !IsImported( mesh.MeshIndex ) )
				continue;

			var choices = GetMeshChoices( mesh.MeshIndex );
			var isDefault = IsDefaultMesh( mesh.MeshIndex );

			var lodMask = (int)(model.GetLodMask( mesh.MeshIndex ) & AllLods);
			var remap = model.GetBoneRemapTable( mesh.MeshIndex );
			var skinned = model.Bones.Length > 0 && mesh.BoneWeightCount > 0;
			var morphs = ReadMorphs( mesh );

			// The morph grid has a cell for every vertex of every draw call, in draw call order.
			var gridOffset = 0;

			foreach ( var drawCall in mesh.DrawCalls )
			{
				var gridStart = gridOffset + drawCall.BaseVertex;
				gridOffset += drawCall.VertexCount;

				if ( !drawCall.IsTriangleList )
				{
					Log.Trace( $"{name}: skipping {drawCall.PrimitiveType} draw call in mesh '{mesh.Name}'." );
					continue;
				}

				if ( drawCall.IndexCount < 3 || drawCall.VertexBuffers.Length == 0 )
					continue;

				var part = ReadPart( mesh, drawCall, remap, skinned );
				part.LodMask = lodMask == 0 ? AllLods : lodMask;
				part.Choices = choices;
				part.IsDefault = isDefault;

				if ( morphs is not null )
					part.Morphs = SliceMorphs( morphs, gridStart + part.FirstVertex, part.Vertices.Length );

				parts.Add( part );
			}
		}

		return parts;
	}

	private MeshPart ReadPart( CompiledMesh mesh, DrawCall drawCall, int[] remap, bool skinned )
	{
		var indexBuffer = mesh.IndexBuffers[drawCall.IndexBuffer];
		var indexStart = drawCall.StartIndex + drawCall.IndexBindOffset / indexBuffer.ElementSize;

		// Only keep the vertices this draw call references.
		var indices = new int[drawCall.IndexCount];
		var first = int.MaxValue;
		var last = int.MinValue;

		for ( var i = 0; i < indices.Length; i++ )
		{
			var index = indexBuffer.ReadIndex( indexStart + i );
			indices[i] = index;
			first = Math.Min( first, index );
			last = Math.Max( last, index );
		}

		for ( var i = 0; i < indices.Length; i++ )
			indices[i] -= first;

		var vertexCount = last - first + 1;
		var vertices = new DeadlockStaticVertex[vertexCount];

		for ( var i = 0; i < vertices.Length; i++ )
		{
			vertices[i].Normal = Vector3.Up;
			vertices[i].Tangent = new Vector4( 1, 0, 0, 1 );
			vertices[i].Color = Color32.White;
			vertices[i].Tint = Color32.White;
			vertices[i].LightmapUV = DeadlockStaticVertex.NoLightmap;
			vertices[i].BlendParams = new Vector4( 0, 0, 0, 1 );
		}

		var part = new MeshPart { Material = drawCall.Material, Vertices = vertices, Indices = indices, FirstVertex = first };

		var hasTangent = false;
		var hasTexCoord = false;
		var hasColor = false;
		var hasLightmapUV = false;
		var hasBlendParams = false;

		for ( var b = 0; b < drawCall.VertexBuffers.Length; b++ )
		{
			var buffer = mesh.VertexBuffers[drawCall.VertexBuffers[b]];
			var offset = drawCall.BaseVertex + drawCall.VertexBindOffsets[b] / buffer.ElementSize + first;

			if ( offset < 0 || offset + vertexCount > buffer.ElementCount )
				throw new InvalidOperationException( $"Draw call in mesh '{mesh.Name}' references vertices outside its buffer." );

			// Each buffer numbers its texture coordinates and colors from zero, so only the first one to have them counts.
			var texCoord0 = hasTexCoord ? null : buffer.Find( "TEXCOORD" );
			var texCoord1 = hasTexCoord ? null : buffer.Find( "TEXCOORD", 1 );
			var color = hasColor ? null : buffer.Find( "COLOR" );
			var tangent = buffer.Find( "TANGENT" );

			hasTexCoord |= texCoord0 is not null;
			hasColor |= color is not null;

			ReadAttributes( buffer, offset, vertices, buffer.Find( "POSITION" ), buffer.Find( "NORMAL" ), tangent, texCoord0, texCoord1, color, ref hasTangent );

			// Lightmap coordinates only mean something on draw calls the map compiler baked into its lightmaps.
			var lightmapUV = hasLightmapUV || !drawCall.HasBakedLightingFromLightMap ? null : buffer.Find( "TEXCOORD", 3 );
			var blendParams = hasBlendParams ? null : buffer.Find( "TEXCOORD", 4 );

			hasLightmapUV |= lightmapUV is not null;
			hasBlendParams |= blendParams is not null;

			ReadWorldAttributes( buffer, offset, vertices, lightmapUV, blendParams );

			if ( skinned && part.Bones is null && buffer.Find( "BLENDINDICES" ) is { } blendIndices )
			{
				var blendWeights = buffer.Find( "BLENDWEIGHT" ) ?? buffer.Find( "BLENDWEIGHTS" );
				ReadInfluences( part, buffer, offset, blendIndices, blendWeights, mesh.BoneWeightCount, remap );
			}
		}

		ApplyTint( vertices, drawCall );

		part.Bounds = BBox.FromPoints( vertices.Select( x => x.Position ) );
		return part;
	}

	/// <summary>
	/// Draw calls can carry a tint and alpha, the object tint Deadlock's shaders combine with the vertex colour.
	/// </summary>
	private static void ApplyTint( DeadlockStaticVertex[] vertices, DrawCall drawCall )
	{
		var tint = drawCall.TintColor is { Length: >= 3 } drawCallTint ? drawCallTint : [1f, 1f, 1f];
		if ( tint[0] == 1 && tint[1] == 1 && tint[2] == 1 && drawCall.Alpha == 1 )
			return;

		var color = new Color32(
			(byte)Math.Clamp( tint[0] * 255f + 0.5f, 0, 255 ),
			(byte)Math.Clamp( tint[1] * 255f + 0.5f, 0, 255 ),
			(byte)Math.Clamp( tint[2] * 255f + 0.5f, 0, 255 ),
			(byte)Math.Clamp( drawCall.Alpha * 255f + 0.5f, 0, 255 ) );

		for ( var i = 0; i < vertices.Length; i++ )
			vertices[i].Tint = color;
	}

	private static void ReadWorldAttributes( MeshBuffer buffer, int offset, DeadlockStaticVertex[] vertices, InputLayoutField? lightmapUV, InputLayoutField? blendParams )
	{
		if ( lightmapUV is null && blendParams is null )
			return;

		Span<float> values = stackalloc float[4];

		for ( var i = 0; i < vertices.Length; i++ )
		{
			if ( lightmapUV is { } uv )
			{
				buffer.Read( uv, offset + i, values );
				vertices[i].LightmapUV = new Vector2( values[0], values[1] );
			}

			if ( blendParams is { } blend )
			{
				values[3] = 1;
				buffer.Read( blend, offset + i, values );
				vertices[i].BlendParams = new Vector4( values[0], values[1], values[2], values[3] );
			}
		}
	}

	private static void ReadAttributes( MeshBuffer buffer, int offset, DeadlockStaticVertex[] vertices, InputLayoutField? position, InputLayoutField? normal,
		InputLayoutField? tangent, InputLayoutField? texCoord0, InputLayoutField? texCoord1, InputLayoutField? color, ref bool hasTangent )
	{
		Span<float> values = stackalloc float[4];
		Span<float> normalValues = stackalloc float[3];
		Span<float> tangentValues = stackalloc float[4];

		for ( var i = 0; i < vertices.Length; i++ )
		{
			var source = offset + i;
			ref var vertex = ref vertices[i];

			if ( position is { } p )
			{
				buffer.Read( p, source, values );
				vertex.Position = new Vector3( values[0], values[1], values[2] );
			}

			if ( normal is { } n )
			{
				if ( VertexDecoding.ReadNormal( buffer, n, source, normalValues, tangentValues ) )
				{
					vertex.Tangent = new Vector4( tangentValues[0], tangentValues[1], tangentValues[2], tangentValues[3] );
					hasTangent = true;
				}

				vertex.Normal = new Vector3( normalValues[0], normalValues[1], normalValues[2] );
			}

			if ( tangent is { } t && !hasTangent )
			{
				values[3] = 1;
				buffer.Read( t, source, values );
				vertex.Tangent = new Vector4( values[0], values[1], values[2], values[3] );
			}

			if ( texCoord0 is { } uv0 )
			{
				buffer.Read( uv0, source, values );
				vertex.TexCoord0 = new Vector2( values[0], values[1] );
			}

			if ( texCoord1 is { } uv1 )
			{
				buffer.Read( uv1, source, values );
				vertex.TexCoord1 = new Vector2( values[0], values[1] );
			}

			if ( color is { } c )
			{
				values[3] = 1;
				buffer.Read( c, source, values );
				vertex.Color = new Color( values[0], values[1], values[2], values[3] ).ToColor32();
			}
		}

		if ( tangent is not null )
			hasTangent = true;
	}

	private void ReadInfluences( MeshPart part, MeshBuffer buffer, int offset, InputLayoutField blendIndices, InputLayoutField? blendWeights, int boneWeightCount, int[] remap )
	{
		var vertexCount = part.Vertices.Length;
		var bones = new int[vertexCount * MaxInfluences];
		var weights = new byte[vertexCount * MaxInfluences];

		Span<int> indices = stackalloc int[8];
		Span<float> sourceWeights = stackalloc float[8];
		Span<int> merged = stackalloc int[8];
		Span<float> mergedWeights = stackalloc float[8];

		for ( var i = 0; i < vertexCount; i++ )
		{
			var count = VertexDecoding.ReadBlend( buffer, blendIndices, blendWeights, boneWeightCount, offset + i, indices, sourceWeights );

			// Map to skeleton bones, merging influences that land on the same bone.
			var mergedCount = 0;
			for ( var j = 0; j < count; j++ )
			{
				if ( sourceWeights[j] <= 0 )
					continue;

				var local = indices[j];
				var bone = remap is not null && local < remap.Length ? remap[local] : local;
				if ( bone < 0 || bone >= model.Bones.Length )
					bone = 0;

				var existing = merged[..mergedCount].IndexOf( bone );
				if ( existing >= 0 )
				{
					mergedWeights[existing] += sourceWeights[j];
				}
				else
				{
					merged[mergedCount] = bone;
					mergedWeights[mergedCount] = sourceWeights[j];
					mergedCount++;
				}
			}

			if ( mergedCount == 0 )
			{
				merged[0] = 0;
				mergedWeights[0] = 1;
				mergedCount = 1;
			}

			// Keep the strongest influences.
			for ( var a = 0; a < mergedCount; a++ )
			{
				for ( var b = a + 1; b < mergedCount; b++ )
				{
					if ( mergedWeights[b] > mergedWeights[a] )
					{
						(mergedWeights[a], mergedWeights[b]) = (mergedWeights[b], mergedWeights[a]);
						(merged[a], merged[b]) = (merged[b], merged[a]);
					}
				}
			}

			var kept = Math.Min( mergedCount, MaxInfluences );
			QuantizeWeights( mergedWeights[..kept], weights.AsSpan( i * MaxInfluences, MaxInfluences ) );

			for ( var j = 0; j < MaxInfluences; j++ )
				bones[i * MaxInfluences + j] = j < kept ? merged[j] : merged[0];
		}

		part.Bones = bones;
		part.Weights = weights;
	}

	/// <summary>
	/// Converts weights to bytes that sum to exactly 255, giving the rounding remainder to the largest fractions.
	/// </summary>
	private static void QuantizeWeights( ReadOnlySpan<float> weights, Span<byte> output )
	{
		var total = 0f;
		foreach ( var weight in weights )
			total += weight;

		Span<float> fractions = stackalloc float[MaxInfluences];
		var remaining = 255;

		for ( var i = 0; i < output.Length; i++ )
		{
			var scaled = i < weights.Length ? weights[i] / total * 255f : 0f;
			output[i] = (byte)scaled;
			fractions[i] = i < weights.Length ? scaled - output[i] : -1f;
			remaining -= output[i];
		}

		while ( remaining-- > 0 )
		{
			var best = 0;
			for ( var i = 1; i < output.Length; i++ )
			{
				if ( fractions[i] > fractions[best] )
					best = i;
			}

			output[best]++;
			fractions[best] = -1f;
		}
	}

	// The layout-taking CreateVertexBuffer is obsolete, but the attribute route caches layouts per struct and frees
	// them on hotload (see DeadlockStaticVertex), so the meshes keep explicit layouts.
#pragma warning disable CS0618
	private Mesh CreateMesh( MeshPart part, BoneLayout skeleton )
	{
		var material = mount.LoadMaterial( part.Material, textureLevel );
		var mesh = new Mesh( part.Material, material );
		var packed = UsesPackedVertices( material );

		if ( part.Bones is null )
		{
			if ( packed )
				mesh.CreateVertexBuffer( part.Vertices.Length, DeadlockPackedVertex.Layout, Pack( part.Vertices ).AsSpan() );
			else
				mesh.CreateVertexBuffer( part.Vertices.Length, DeadlockStaticVertex.Layout, part.Vertices.AsSpan() );
		}
		else
		{
			Color32 Indices( int vertex ) => new(
				(byte)skeleton.SkinTarget[part.Bones[vertex * MaxInfluences]],
				(byte)skeleton.SkinTarget[part.Bones[vertex * MaxInfluences + 1]],
				(byte)skeleton.SkinTarget[part.Bones[vertex * MaxInfluences + 2]],
				(byte)skeleton.SkinTarget[part.Bones[vertex * MaxInfluences + 3]] );

			Color32 Weights( int vertex ) => new(
				part.Weights[vertex * MaxInfluences], part.Weights[vertex * MaxInfluences + 1],
				part.Weights[vertex * MaxInfluences + 2], part.Weights[vertex * MaxInfluences + 3] );

			if ( packed )
			{
				var vertices = new DeadlockPackedSkinnedVertex[part.Vertices.Length];
				for ( var i = 0; i < vertices.Length; i++ )
					vertices[i] = new DeadlockPackedSkinnedVertex( part.Vertices[i] ) { BlendIndices = Indices( i ), BlendWeights = Weights( i ) };

				mesh.CreateVertexBuffer( vertices.Length, DeadlockPackedSkinnedVertex.Layout, vertices.AsSpan() );
			}
			else
			{
				var vertices = new DeadlockSkinnedVertex[part.Vertices.Length];
				for ( var i = 0; i < vertices.Length; i++ )
					vertices[i] = new DeadlockSkinnedVertex( part.Vertices[i] ) { BlendIndices = Indices( i ), BlendWeights = Weights( i ) };

				mesh.CreateVertexBuffer( vertices.Length, DeadlockSkinnedVertex.Layout, vertices.AsSpan() );
			}
		}

		mesh.CreateIndexBuffer( part.Indices.Length, part.Indices.AsSpan() );
		mesh.Bounds = part.Bounds;

		if ( part.Morphs is not null )
		{
			foreach ( var (morphName, deltas) in part.Morphs )
				mesh.AddMorph( morphName, deltas );
		}

		return mesh;
	}
#pragma warning restore CS0618

	/// <summary>
	/// Whether a material reads the mount's compact vertices: the mount's own shaders do; anything else (the default
	/// material standing in for a missing one, the sky) gets the full float layout s&amp;box's shaders read.
	/// </summary>
	internal static bool UsesPackedVertices( Material material )
		=> material?.Shader?.ResourcePath is { } shader && shader.Replace( '\\', '/' ).StartsWith( "shaders/deadlock_", StringComparison.OrdinalIgnoreCase );

	internal static DeadlockPackedVertex[] Pack( DeadlockStaticVertex[] vertices )
	{
		var packed = new DeadlockPackedVertex[vertices.Length];
		for ( var i = 0; i < vertices.Length; i++ )
			packed[i] = new DeadlockPackedVertex( vertices[i] );

		return packed;
	}
}
