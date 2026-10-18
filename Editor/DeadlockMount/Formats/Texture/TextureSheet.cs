using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// A texture's sprite sheet (its SHEET extra data): sequences of frames, each frame one or more rectangles of the
/// texture shown for a while. Particle renderers animate through them.
/// </summary>
internal sealed class TextureSheet
{
	public Sequence[] Sequences { get; init; } = [];

	public sealed record Sequence( uint Id, bool Clamp, bool AlphaCrop, bool NoColor, bool NoAlpha, float TotalTime, string Name,
		Frame[] Frames, IReadOnlyDictionary<string, float> FloatParams );

	public sealed record Frame( float DisplayTime, Image[] Images );

	/// <summary>
	/// A frame's rectangle in texture coordinates, cropped to what it draws and as authored.
	/// </summary>
	public readonly record struct Image( Vector2 CroppedMin, Vector2 CroppedMax, Vector2 UncroppedMin, Vector2 UncroppedMax );

	/// <summary>
	/// Reads the SHEET data. Offsets in it are relative to the field holding them, except the first, which is
	/// relative to the start of the data.
	/// </summary>
	public static TextureSheet Read( ReadOnlySpan<byte> data )
	{
		var sequencesOffset = BinaryPrimitives.ReadInt32LittleEndian( data );
		var sequenceCount = (int)BinaryPrimitives.ReadUInt32LittleEndian( data[4..] );

		var sequences = new Sequence[sequenceCount];
		var position = sequencesOffset;

		for ( var s = 0; s < sequenceCount; s++ )
		{
			var id = BinaryPrimitives.ReadUInt32LittleEndian( data[position..] );
			var clamp = data[position + 4] != 0;
			var alphaCrop = data[position + 5] != 0;
			var noColor = data[position + 6] != 0;
			var noAlpha = data[position + 7] != 0;
			var framesOffset = position + 8 + BinaryPrimitives.ReadInt32LittleEndian( data[(position + 8)..] );
			var frameCount = (int)BinaryPrimitives.ReadUInt32LittleEndian( data[(position + 12)..] );
			var totalTime = BinaryPrimitives.ReadSingleLittleEndian( data[(position + 16)..] );
			var name = ReadOffsetString( data, position + 20 );
			var floatParamsOffset = position + 24 + BinaryPrimitives.ReadInt32LittleEndian( data[(position + 24)..] );
			var floatParamCount = (int)BinaryPrimitives.ReadUInt32LittleEndian( data[(position + 28)..] );

			var floatParams = new Dictionary<string, float>( floatParamCount );
			for ( var p = 0; p < floatParamCount; p++ )
			{
				var entry = floatParamsOffset + p * 8;
				floatParams[ReadOffsetString( data, entry )] = BinaryPrimitives.ReadSingleLittleEndian( data[(entry + 4)..] );
			}

			var frames = new Frame[frameCount];
			for ( var f = 0; f < frameCount; f++ )
			{
				var entry = framesOffset + f * 12;
				var displayTime = BinaryPrimitives.ReadSingleLittleEndian( data[entry..] );
				var imagesOffset = entry + 4 + BinaryPrimitives.ReadInt32LittleEndian( data[(entry + 4)..] );
				var imageCount = (int)BinaryPrimitives.ReadUInt32LittleEndian( data[(entry + 8)..] );

				var images = new Image[imageCount];
				for ( var i = 0; i < imageCount; i++ )
				{
					var image = data[(imagesOffset + i * 32)..];
					images[i] = new Image(
						new Vector2( BinaryPrimitives.ReadSingleLittleEndian( image ), BinaryPrimitives.ReadSingleLittleEndian( image[4..] ) ),
						new Vector2( BinaryPrimitives.ReadSingleLittleEndian( image[8..] ), BinaryPrimitives.ReadSingleLittleEndian( image[12..] ) ),
						new Vector2( BinaryPrimitives.ReadSingleLittleEndian( image[16..] ), BinaryPrimitives.ReadSingleLittleEndian( image[20..] ) ),
						new Vector2( BinaryPrimitives.ReadSingleLittleEndian( image[24..] ), BinaryPrimitives.ReadSingleLittleEndian( image[28..] ) ) );
				}

				frames[f] = new Frame( displayTime, images );
			}

			sequences[s] = new Sequence( id, clamp, alphaCrop, noColor, noAlpha, totalTime, name, frames, floatParams );
			position += 32;
		}

		return new TextureSheet { Sequences = sequences };
	}

	private static string ReadOffsetString( ReadOnlySpan<byte> data, int field )
	{
		var start = field + BinaryPrimitives.ReadInt32LittleEndian( data[field..] );
		if ( start <= field || start >= data.Length )
			return string.Empty;

		var length = data[start..].IndexOf( (byte)0 );
		return Encoding.UTF8.GetString( length < 0 ? data[start..] : data.Slice( start, length ) );
	}
}
