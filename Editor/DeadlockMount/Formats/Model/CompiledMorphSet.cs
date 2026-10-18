using System;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// One vertex's change under a morph, in model space.
/// </summary>
internal readonly record struct MorphVertexDelta( int Vertex, float PositionX, float PositionY, float PositionZ, float NormalX, float NormalY, float NormalZ );

/// <summary>
/// A slider that drives morphs through the flex rules.
/// </summary>
internal sealed record FlexControllerInfo( string Name, string Type, float Min, float Max );

/// <summary>
/// The program that computes one flex's weight from the controllers: opcodes and their data, run on a stack.
/// </summary>
internal sealed record FlexRuleInfo( int Flex, int[] OpCodes, int[] OpData );

/// <summary>
/// A mesh's morph set (MRPH block): the flexes, the controllers and rules that drive them, and the vertex
/// deltas, which live in a texture atlas. Morph rectangles copy atlas texels onto a grid with one cell per
/// vertex, each bundle quantized to 8 bits per channel with its own range and offset.
/// </summary>
internal sealed class CompiledMorphSet
{
	private enum BundleType
	{
		None = 0,
		PositionSpeed = 1,
		NormalWrinkle = 2,
	}

	private readonly KVObject _data;

	/// <summary>
	/// Grid width; vertex N is at row N / Width, column N % Width.
	/// </summary>
	public int Width { get; }

	public int Height { get; }

	public string AtlasPath { get; }

	/// <summary>
	/// Flex names by flex index, which the rules write to. These name the morph targets.
	/// </summary>
	public string[] Flexes { get; }

	public FlexControllerInfo[] Controllers { get; }

	public FlexRuleInfo[] Rules { get; }

	public CompiledMorphSet( KVObject data )
	{
		_data = data;

		Width = data.GetInt32( "m_nWidth" );
		Height = data.GetInt32( "m_nHeight" );
		AtlasPath = data.GetString( "m_pTextureAtlas", "" );
		Flexes = data.GetObjects( "m_FlexDesc" ).Select( x => x.GetString( "m_szFacs", "" ) ).ToArray();

		Controllers = data.GetObjects( "m_FlexControllers" )
			.Select( x => new FlexControllerInfo( x.GetString( "m_szName", "" ), x.GetString( "m_szType", "" ), x.GetFloat( "min" ), x.GetFloat( "max", 1 ) ) )
			.ToArray();

		Rules = data.GetObjects( "m_FlexRules" ).Select( ReadRule ).ToArray();
	}

	private static FlexRuleInfo ReadRule( KVObject rule )
	{
		var ops = rule.GetObjects( "m_FlexOps" ).ToArray();
		var codes = new int[ops.Length];
		var data = new int[ops.Length];

		for ( var i = 0; i < ops.Length; i++ )
		{
			codes[i] = ReadOpCode( ops[i]["m_OpCode"] );
			data[i] = ops[i].GetInt32( "m_Data" );
		}

		return new FlexRuleInfo( rule.GetInt32( "m_nFlex" ), codes, data );
	}

	private static int ReadOpCode( object value ) => value switch
	{
		string name => name switch
		{
			"FLEX_OP_CONST" => 1,
			"FLEX_OP_FETCH1" => 2,
			"FLEX_OP_FETCH2" => 3,
			"FLEX_OP_ADD" => 4,
			"FLEX_OP_SUB" => 5,
			"FLEX_OP_MUL" => 6,
			"FLEX_OP_DIV" => 7,
			"FLEX_OP_NEG" => 8,
			"FLEX_OP_EXP" => 9,
			"FLEX_OP_OPEN" => 10,
			"FLEX_OP_CLOSE" => 11,
			"FLEX_OP_COMMA" => 12,
			"FLEX_OP_MAX" => 13,
			"FLEX_OP_MIN" => 14,
			"FLEX_OP_2WAY_0" => 15,
			"FLEX_OP_2WAY_1" => 16,
			"FLEX_OP_NWAY" => 17,
			"FLEX_OP_COMBO" => 18,
			"FLEX_OP_DOMINATE" => 19,
			"FLEX_OP_DME_LOWER_EYELID" => 20,
			"FLEX_OP_DME_UPPER_EYELID" => 21,
			"FLEX_OP_SQRT" => 22,
			"FLEX_OP_REMAPVALCLAMPED" => 23,
			"FLEX_OP_SIN" => 24,
			"FLEX_OP_COS" => 25,
			"FLEX_OP_ABS" => 26,
			_ => 0,
		},
		null => 0,
		_ => (int)KVValue.ToInt64( value ),
	};

	private static BundleType ReadBundleType( object value ) => value switch
	{
		string name when name.EndsWith( "POSITION_SPEED", StringComparison.Ordinal ) => BundleType.PositionSpeed,
		string name when name.EndsWith( "NORMAL_WRINKLE", StringComparison.Ordinal ) => BundleType.NormalWrinkle,
		string => BundleType.None,
		null => BundleType.None,
		_ => (BundleType)KVValue.ToInt64( value ),
	};

	/// <summary>
	/// Decodes every morph's deltas from the atlas, keyed by morph name. Grid cells a morph leaves
	/// unchanged are left out, and deltas are in grid (vertex) order.
	/// </summary>
	public Dictionary<string, MorphVertexDelta[]> DecodeDeltas( CompiledTexture atlas )
	{
		var result = new Dictionary<string, MorphVertexDelta[]>( StringComparer.OrdinalIgnoreCase );

		if ( atlas.Format is not (VTexFormat.RGBA8888 or VTexFormat.BGRA8888) )
			throw new NotSupportedException( $"Morph atlas format {atlas.Format} is not supported." );

		var pixels = atlas.ReadMips( 0, 1 );
		var swapRedBlue = atlas.Format == VTexFormat.BGRA8888;
		var textureWidth = atlas.Width;
		var textureHeight = atlas.Height;

		var bundleTypes = (_data.GetArray( "m_bundleTypes" ) ?? []).Select( ReadBundleType ).ToArray();
		var cellCount = Width * Height;

		Span<float> texel = stackalloc float[4];

		foreach ( var morph in _data.GetObjects( "m_morphDatas" ) )
		{
			var name = morph.GetString( "m_name", "" );
			if ( name.Length == 0 )
				continue;

			var positions = new float[cellCount * 3];
			var normals = new float[cellCount * 3];
			var touched = new bool[cellCount];

			foreach ( var rect in morph.GetObjects( "m_morphRectDatas" ) )
			{
				var left = rect.GetInt32( "m_nXLeftDst" );
				var top = rect.GetInt32( "m_nYTopDst" );
				var width = (int)MathF.Round( rect.GetFloat( "m_flUWidthSrc" ) * textureWidth );
				var height = (int)MathF.Round( rect.GetFloat( "m_flVHeightSrc" ) * textureHeight );
				var bundles = rect.GetObjects( "m_bundleDatas" ).ToArray();

				for ( var b = 0; b < bundles.Length && b < bundleTypes.Length; b++ )
				{
					var type = bundleTypes[b];
					if ( type is not (BundleType.PositionSpeed or BundleType.NormalWrinkle) )
						continue;

					var bundle = bundles[b];
					var sourceLeft = (int)MathF.Round( bundle.GetFloat( "m_flULeftSrc" ) * textureWidth );
					var sourceTop = (int)MathF.Round( bundle.GetFloat( "m_flVTopSrc" ) * textureHeight );
					var ranges = Pad4( bundle.GetFloatArray( "m_ranges" ) );
					var offsets = Pad4( bundle.GetFloatArray( "m_offsets" ) );
					var target = type == BundleType.PositionSpeed ? positions : normals;

					for ( var row = 0; row < height; row++ )
					{
						var sourceRow = sourceTop + row;
						var gridRow = top + row;

						if ( sourceRow < 0 || sourceRow >= textureHeight || gridRow < 0 || gridRow >= Height )
							continue;

						for ( var column = 0; column < width; column++ )
						{
							var sourceColumn = sourceLeft + column;
							var gridColumn = left + column;

							// Rects that overrun the grid are clipped rather than wrapped onto the next row.
							if ( sourceColumn < 0 || sourceColumn >= textureWidth || gridColumn < 0 || gridColumn >= Width )
								continue;

							var pixel = (sourceRow * textureWidth + sourceColumn) * 4;
							texel[0] = pixels[pixel + (swapRedBlue ? 2 : 0)];
							texel[1] = pixels[pixel + 1];
							texel[2] = pixels[pixel + (swapRedBlue ? 0 : 2)];

							var cell = gridRow * Width + gridColumn;
							for ( var c = 0; c < 3; c++ )
								target[cell * 3 + c] = Dequantize( texel[c], ranges[c], offsets[c] );

							touched[cell] = true;
						}
					}
				}
			}

			var deltas = new List<MorphVertexDelta>();
			for ( var cell = 0; cell < cellCount; cell++ )
			{
				if ( !touched[cell] )
					continue;

				var p = cell * 3;
				if ( positions[p] == 0 && positions[p + 1] == 0 && positions[p + 2] == 0
					&& normals[p] == 0 && normals[p + 1] == 0 && normals[p + 2] == 0 )
					continue;

				deltas.Add( new MorphVertexDelta( cell, positions[p], positions[p + 1], positions[p + 2], normals[p], normals[p + 1], normals[p + 2] ) );
			}

			if ( deltas.Count > 0 )
				result[name] = [.. deltas];
		}

		return result;
	}

	/// <summary>
	/// Offsets put zero on a quantization level, but decoding it leaves float rounding; anything within half a
	/// step of zero is that level.
	/// </summary>
	private static float Dequantize( float value, float range, float offset )
	{
		var decoded = value / 255f * range + offset;
		return MathF.Abs( decoded ) < MathF.Abs( range ) / 510f ? 0f : decoded;
	}

	private static float[] Pad4( float[] values )
	{
		var result = new float[4];
		values.AsSpan( 0, Math.Min( values.Length, 4 ) ).CopyTo( result );
		return result;
	}
}
