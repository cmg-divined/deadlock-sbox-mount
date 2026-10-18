using System;
using System.IO;
using ParticleSpriteSheet = Deadlock.Particles.ParticleSpriteSheet;

namespace Sandbox.Mounting.Deadlock;

public sealed partial class DeadlockMount
{
	private readonly Dictionary<string, ParticleSpriteSheet> _spriteSheets = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// A texture's sprite sheet, as the particle code reads it, or null when the texture has none. Many particle
	/// systems share textures, so sheets are read once.
	/// </summary>
	internal ParticleSpriteSheet ReadTextureSheet( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		path = path.Replace( '\\', '/' );

		lock ( _spriteSheets )
		{
			if ( _spriteSheets.TryGetValue( path, out var cached ) )
				return cached;
		}

		ParticleSpriteSheet sheet = null;

		if ( FileExists( path + "_c" ) )
		{
			try
			{
				var texture = ReadTextureHeader( path );
				if ( texture.Sheet is { } source )
					sheet = ToParticleSpriteSheet( source, texture.MotionVectorsMaxDistance );
			}
			catch ( Exception e ) when ( e is InvalidDataException or NotSupportedException or ArgumentOutOfRangeException )
			{
				Log.Warning( $"Couldn't read the sprite sheet of '{path}': {e.Message}" );
			}
		}

		lock ( _spriteSheets )
			_spriteSheets[path] = sheet;

		return sheet;
	}

	private static ParticleSpriteSheet ToParticleSpriteSheet( TextureSheet sheet, int motionVectorsMaxDistance ) => new()
	{
		MotionVectorsMaxDistance = motionVectorsMaxDistance,
		Sequences = [.. sheet.Sequences.Select( sequence => new ParticleSpriteSheet.Sequence
		{
			Id = sequence.Id,
			Clamp = sequence.Clamp,
			AlphaCrop = sequence.AlphaCrop,
			NoColor = sequence.NoColor,
			NoAlpha = sequence.NoAlpha,
			TotalTime = sequence.TotalTime,
			Name = sequence.Name,
			Frames = [.. sequence.Frames.Select( frame => new ParticleSpriteSheet.Frame
			{
				DisplayTime = frame.DisplayTime,
				Images = [.. frame.Images.Select( image => new ParticleSpriteSheet.Image
				{
					CroppedMin = image.CroppedMin,
					CroppedMax = image.CroppedMax,
					UncroppedMin = image.UncroppedMin,
					UncroppedMax = image.UncroppedMax,
				} )],
			} )],
		} )],
	};

	private void ClearParticles()
	{
		lock ( _spriteSheets )
			_spriteSheets.Clear();
	}
}
