using System;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// A map's world (vwrld_c): its world nodes, entity lumps and baked lighting.
/// </summary>
internal sealed class CompiledWorld
{
	/// <summary>
	/// World node resources, without the .vwnod extension, e.g. <c>maps/dl_midtown/worldnodes/node000</c>.
	/// </summary>
	public string[] NodePrefixes { get; }

	/// <summary>
	/// Entity lump resources, without the _c suffix.
	/// </summary>
	public string[] EntityLumps { get; }

	/// <summary>
	/// The lighting info: lightmap textures, their version and UV scale. Null for maps without baked lighting.
	/// </summary>
	public KVObject LightingInfo { get; }

	public CompiledWorld( byte[] data )
	{
		var world = new ResourceFile( data ).ReadKeyValues( "DATA" ) ?? throw new InvalidDataException( "World has no DATA block." );

		NodePrefixes = world.GetObjects( "m_worldNodes" ).Select( x => NormalizePath( x.GetString( "m_worldNodePrefix", "" ) ) ).ToArray();
		EntityLumps = world.GetStringArray( "m_entityLumps" ).Select( NormalizePath ).ToArray();
		LightingInfo = world.GetObject( "m_worldLightingInfo" );
	}

	public static string NormalizePath( string path ) => path.Replace( '\\', '/' );
}

/// <summary>
/// A 3x4 transform, row major: each row is three rotation (and scale) terms then a translation, so a point maps
/// to <c>(dot(row0.xyz, p) + row0.w, ...)</c>.
/// </summary>
internal readonly record struct Matrix3x4( float[] Values )
{
	public static Matrix3x4 Identity => new( [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0] );

	/// <summary>
	/// Reads three rows of four, or a flat array of twelve.
	/// </summary>
	public static Matrix3x4 Read( object value )
	{
		var result = new float[12];

		if ( value is object[] { Length: 3 } rows && rows[0] is object[] )
		{
			for ( var row = 0; row < 3; row++ )
			{
				var columns = rows[row] as object[] ?? [];
				for ( var column = 0; column < 4 && column < columns.Length; column++ )
					result[row * 4 + column] = KVValue.ToFloat( columns[column] );
			}

			return new Matrix3x4( result );
		}

		if ( value is object[] flat )
		{
			for ( var i = 0; i < 12 && i < flat.Length; i++ )
				result[i] = KVValue.ToFloat( flat[i] );

			return new Matrix3x4( result );
		}

		return Identity;
	}

	public float this[int row, int column] => Values[row * 4 + column];
}

/// <summary>
/// A placed model: m_sceneObjects entries.
/// </summary>
internal sealed record WorldSceneObject(
	int Index,
	string Model,
	Matrix3x4 Transform,
	float[] TintColor,
	string Skin,
	uint Flags,
	int LodOverride,
	float FadeStartDistance,
	float FadeEndDistance,
	string Layer );

/// <summary>
/// One draw call of an aggregate's model, drawn at a transform. Fragments without one are in world space already.
/// </summary>
/// <param name="LodGroupMask">LOD levels of the setup the fragment shows at; 0 for every level.</param>
internal sealed record WorldAggregateFragment( int DrawCall, Matrix3x4? Transform, byte[] TintColor, uint LodGroupMask, int LodSetupIndex, uint Flags );

/// <summary>
/// Where an aggregate's LOD levels switch: distances from an origin, scaled by the largest object in it.
/// </summary>
internal sealed record WorldAggregateLodSetup( float[] Origin, float MaxObjectScale, float[] SwitchDistances );

/// <summary>
/// Merged world geometry: m_aggregateSceneObjects entries. Its model holds one mesh whose draw calls the
/// fragments place.
/// </summary>
internal sealed record WorldAggregate( string Model, uint AllFlags, uint AnyFlags, string Layer, WorldAggregateFragment[] Fragments, WorldAggregateLodSetup[] LodSetups );

/// <summary>
/// A material replacing one draw call of a scene object's model, or all of them.
/// </summary>
internal sealed record WorldMaterialOverride( int SceneObject, int SubSceneObject, uint DrawCall, string Material )
{
	public bool IsWholeModel => DrawCall == uint.MaxValue;
}

/// <summary>
/// A world node (vwnod_c): the static geometry of a part of the world.
/// </summary>
internal sealed class CompiledWorldNode
{
	public string[] LayerNames { get; }
	public WorldSceneObject[] SceneObjects { get; }
	public WorldAggregate[] Aggregates { get; }
	public WorldMaterialOverride[] MaterialOverrides { get; }

	/// <summary>
	/// Features this loader doesn't place yet, by name and count, so a map using them can say so.
	/// </summary>
	public Dictionary<string, int> Unsupported { get; } = [];

	public CompiledWorldNode( byte[] data )
	{
		var node = new ResourceFile( data ).ReadKeyValues( "DATA" ) ?? throw new InvalidDataException( "World node has no DATA block." );

		LayerNames = node.GetStringArray( "m_layerNames" );
		var layerIndices = node.GetInt64Array( "m_sceneObjectLayerIndices" );

		var sceneObjects = node.GetObjects( "m_sceneObjects" ).ToArray();
		SceneObjects = new WorldSceneObject[sceneObjects.Length];

		for ( var i = 0; i < sceneObjects.Length; i++ )
		{
			var sceneObject = sceneObjects[i];
			var layer = i < layerIndices.Length ? (int)layerIndices[i] : -1;

			SceneObjects[i] = new WorldSceneObject(
				i,
				CompiledWorld.NormalizePath( sceneObject.GetString( "m_renderableModel", "" ) ),
				Matrix3x4.Read( sceneObject["m_vTransform"] ),
				sceneObject.GetFloatArray( "m_vTintColor" ),
				sceneObject.GetString( "m_skin", "" ),
				ReadFlags( sceneObject["m_nObjectTypeFlags"] ),
				sceneObject.GetInt32( "m_nLODOverride", -1 ),
				sceneObject.GetFloat( "m_flFadeStartDistance" ),
				sceneObject.GetFloat( "m_flFadeEndDistance" ),
				layer >= 0 && layer < LayerNames.Length ? LayerNames[layer] : null );

			if ( !string.IsNullOrEmpty( sceneObject.GetString( "m_renderable" ) ) )
				Count( "mesh scene objects" );
		}

		Aggregates = node.GetObjects( "m_aggregateSceneObjects" ).Select( ReadAggregate ).ToArray();

		MaterialOverrides = node.GetObjects( "m_materialOverrides" ).Select( x => new WorldMaterialOverride(
			x.GetInt32( "m_nSceneObjectIndex" ),
			x.GetInt32( "m_nSubSceneObject" ),
			unchecked((uint)x.GetInt64( "m_nDrawCallIndex" )),
			CompiledWorld.NormalizePath( x.GetString( "m_pMaterial", "" ) ) ) ).ToArray();

		Count( "clutter scene objects", node.GetArray( "m_clutterSceneObjects" )?.Length ?? 0 );
		Count( "info overlays", node.GetArray( "m_infoOverlays" )?.Length ?? 0 );
		Count( "extra vertex streams", node.GetArray( "m_extraVertexStreams" )?.Length ?? 0 );
	}

	private void Count( string feature, int count = 1 )
	{
		if ( count > 0 )
			Unsupported[feature] = Unsupported.GetValueOrDefault( feature ) + count;
	}

	private WorldAggregate ReadAggregate( KVObject aggregate )
	{
		var transforms = aggregate.GetArray( "m_fragmentTransforms" ) ?? [];
		var transformIndex = 0;
		var fragments = new List<WorldAggregateFragment>();

		foreach ( var fragment in aggregate.GetObjects( "m_aggregateMeshes" ) )
		{
			// Fragments with a transform take the next one in order.
			Matrix3x4? transform = null;
			if ( fragment.GetBool( "m_bHasTransform" ) && transformIndex < transforms.Length )
				transform = Matrix3x4.Read( transforms[transformIndex++] );

			var tint = fragment.GetInt64Array( "m_vTintColor" );

			fragments.Add( new WorldAggregateFragment(
				fragment.GetInt32( "m_nDrawCallIndex" ),
				transform,
				tint.Length >= 3 ? [(byte)tint[0], (byte)tint[1], (byte)tint[2]] : [255, 255, 255],
				unchecked((uint)fragment.GetInt64( "m_nLODGroupMask" )),
				fragment.GetInt32( "m_nLODSetupIndex", -1 ),
				ReadFlags( fragment["m_objectFlags"] ) ) );
		}

		var lodSetups = aggregate.GetObjects( "m_lodSetups" ).Select( x => new WorldAggregateLodSetup(
			x.GetFloatArray( "m_vLODOrigin" ),
			x.GetFloat( "m_fMaxObjectScale", 1 ),
			x.GetFloatArray( "m_fSwitchDistances" ) ) ).ToArray();

		var layer = aggregate.GetInt32( "m_nLayer", -1 );

		return new WorldAggregate(
			CompiledWorld.NormalizePath( aggregate.GetString( "m_renderableModel", "" ) ),
			ReadFlags( aggregate["m_allFlags"] ),
			ReadFlags( aggregate["m_anyFlags"] ),
			layer >= 0 && layer < LayerNames.Length ? LayerNames[layer] : null,
			[.. fragments],
			lodSetups );
	}

	/// <summary>
	/// Object type flags are stored as a number or as names joined with '|'.
	/// </summary>
	public static uint ReadFlags( object value )
	{
		if ( value is string names )
		{
			uint flags = 0;
			foreach ( var flagName in names.Split( '|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
			{
				if ( ObjectTypeFlagNames.TryGetValue( flagName, out var flag ) )
					flags |= flag;
				else if ( uint.TryParse( flagName, out var number ) )
					flags |= number;
			}

			return flags;
		}

		return value is null ? 0 : unchecked((uint)KVValue.ToInt64( value ));
	}

	private static readonly Dictionary<string, uint> ObjectTypeFlagNames = new( StringComparer.OrdinalIgnoreCase )
	{
		["OBJECT_TYPE_IMAGE_LOD"] = ObjectTypeFlags.ImageLod,
		["OBJECT_TYPE_GEOMETRY_LOD"] = ObjectTypeFlags.GeometryLod,
		["OBJECT_TYPE_DECAL"] = ObjectTypeFlags.Decal,
		["OBJECT_TYPE_MODEL"] = ObjectTypeFlags.Model,
		["OBJECT_TYPE_BLOCK_LIGHT"] = ObjectTypeFlags.BlockLight,
		["OBJECT_TYPE_NO_SHADOWS"] = ObjectTypeFlags.NoShadows,
		["OBJECT_TYPE_WORLDSPACE_TEXURE_BLEND"] = ObjectTypeFlags.WorldspaceTextureBlend,
		["OBJECT_TYPE_DISABLED_IN_LOW_QUALITY"] = ObjectTypeFlags.DisabledInLowQuality,
		["OBJECT_TYPE_NO_SUN_SHADOWS"] = ObjectTypeFlags.NoSunShadows,
		["OBJECT_TYPE_RENDER_WITH_DYNAMIC"] = ObjectTypeFlags.RenderWithDynamic,
		["OBJECT_TYPE_RENDER_TO_CUBEMAPS"] = ObjectTypeFlags.RenderToCubemaps,
		["OBJECT_TYPE_MODEL_HAS_LODS"] = ObjectTypeFlags.ModelHasLods,
		["OBJECT_TYPE_OVERLAY"] = ObjectTypeFlags.Overlay,
		["OBJECT_TYPE_PRECOMPUTED_VISMEMBERS"] = ObjectTypeFlags.PrecomputedVismembers,
	};
}

/// <summary>
/// Scene object flags (ObjectTypeFlags_t), the ones this loader uses.
/// </summary>
internal static class ObjectTypeFlags
{
	public const uint ImageLod = 0x1;
	public const uint GeometryLod = 0x2;
	public const uint Decal = 0x4;
	public const uint Model = 0x8;
	public const uint BlockLight = 0x10;
	public const uint NoShadows = 0x20;
	public const uint WorldspaceTextureBlend = 0x40;
	public const uint DisabledInLowQuality = 0x80;
	public const uint NoSunShadows = 0x100;
	public const uint RenderWithDynamic = 0x200;
	public const uint RenderToCubemaps = 0x400;
	public const uint ModelHasLods = 0x800;
	public const uint Overlay = 0x2000;
	public const uint PrecomputedVismembers = 0x4000;
}
