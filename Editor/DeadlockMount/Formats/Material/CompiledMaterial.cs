using System;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// A compiled material (vmat_c): the shader it renders with and its parameters.
/// </summary>
internal sealed class CompiledMaterial
{
	public string Name { get; }

	/// <summary>
	/// The shader, e.g. <c>citadel_hero.vfx</c>.
	/// </summary>
	public string ShaderName { get; }

	/// <summary>
	/// Integer parameters, including the static combos (<c>F_*</c>) the material was compiled with.
	/// </summary>
	public Dictionary<string, long> IntParams { get; } = new( StringComparer.OrdinalIgnoreCase );

	public Dictionary<string, float> FloatParams { get; } = new( StringComparer.OrdinalIgnoreCase );

	public Dictionary<string, float[]> VectorParams { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Texture parameters, e.g. <c>g_tColor</c>, mapped to compiled texture paths without the <c>_c</c>.
	/// </summary>
	public Dictionary<string, string> TextureParams { get; } = new( StringComparer.OrdinalIgnoreCase );

	public Dictionary<string, long> IntAttributes { get; } = new( StringComparer.OrdinalIgnoreCase );

	public Dictionary<string, float> FloatAttributes { get; } = new( StringComparer.OrdinalIgnoreCase );

	public Dictionary<string, float[]> VectorAttributes { get; } = new( StringComparer.OrdinalIgnoreCase );

	public Dictionary<string, string> StringAttributes { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Parameters computed at runtime from compiled expressions, by name.
	/// </summary>
	public Dictionary<string, byte[]> DynamicParams { get; } = new( StringComparer.OrdinalIgnoreCase );

	public CompiledMaterial( byte[] data )
	{
		var resource = new ResourceFile( data );
		var root = resource.ReadKeyValues( "DATA" ) ?? throw new InvalidDataException( "Material has no DATA block." );

		Name = root.GetString( "m_materialName", "" );
		ShaderName = root.GetString( "m_shaderName", "" );

		foreach ( var entry in root.GetObjects( "m_intParams" ) )
			IntParams[entry.GetString( "m_name", "" )] = entry.GetInt64( "m_nValue" );

		foreach ( var entry in root.GetObjects( "m_floatParams" ) )
			FloatParams[entry.GetString( "m_name", "" )] = entry.GetFloat( "m_flValue" );

		foreach ( var entry in root.GetObjects( "m_vectorParams" ) )
			VectorParams[entry.GetString( "m_name", "" )] = ReadVector( entry, "m_value" );

		foreach ( var entry in root.GetObjects( "m_textureParams" ) )
			TextureParams[entry.GetString( "m_name", "" )] = entry.GetString( "m_pValue", "" );

		foreach ( var entry in root.GetObjects( "m_intAttributes" ) )
			IntAttributes[entry.GetString( "m_name", "" )] = entry.GetInt64( "m_nValue" );

		foreach ( var entry in root.GetObjects( "m_floatAttributes" ) )
			FloatAttributes[entry.GetString( "m_name", "" )] = entry.GetFloat( "m_flValue" );

		foreach ( var entry in root.GetObjects( "m_vectorAttributes" ) )
			VectorAttributes[entry.GetString( "m_name", "" )] = ReadVector( entry, "m_value" );

		foreach ( var entry in root.GetObjects( "m_stringAttributes" ) )
			StringAttributes[entry.GetString( "m_name", "" )] = entry.GetString( "m_value", "" );

		foreach ( var entry in root.GetObjects( "m_dynamicParams" ).Concat( root.GetObjects( "m_dynamicTextureParams" ) ) )
		{
			if ( entry["m_value"] is byte[] expression )
				DynamicParams[entry.GetString( "m_name", "" )] = expression;
		}
	}

	private static float[] ReadVector( KVObject entry, string key )
	{
		var values = entry.GetFloatArray( key );
		var result = new float[4];
		values.AsSpan( 0, Math.Min( values.Length, 4 ) ).CopyTo( result );
		return result;
	}

	public long GetInt( string name, long fallback = 0 ) => IntParams.GetValueOrDefault( name, fallback );

	public float GetFloat( string name, float fallback = 0 ) => FloatParams.GetValueOrDefault( name, fallback );

	public float[] GetVector( string name, float[] fallback = null ) => VectorParams.GetValueOrDefault( name ) ?? fallback;

	public string GetTexture( string name ) => TextureParams.GetValueOrDefault( name );
}
