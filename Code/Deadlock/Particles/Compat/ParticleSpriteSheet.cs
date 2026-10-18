using System;

namespace Deadlock.Particles;

using Vector2 = System.Numerics.Vector2;

/// <summary>
/// A texture's sprite sheet (its SHEET extra data): sequences of frames, each frame one or more rectangles of the
/// texture, shown for a while in turn.
/// </summary>
public sealed class ParticleSpriteSheet
{
	public Sequence[] Sequences { get; init; } = [];

	/// <summary>
	/// For a motion vector sheet, how far its texels move at most, in texels (the texture's metadata).
	/// </summary>
	public float MotionVectorsMaxDistance { get; init; }

	public sealed class Sequence
	{
		public uint Id { get; init; }
		public Frame[] Frames { get; init; } = [];

		/// <summary>
		/// The sequence's length, in the units its frames' display times are in.
		/// </summary>
		public float TotalTime { get; init; }

		public string Name { get; init; } = "";

		/// <summary>
		/// Whether the sequence holds its last frame rather than looping back to the first.
		/// </summary>
		public bool Clamp { get; init; }

		public bool AlphaCrop { get; init; }
		public bool NoColor { get; init; }
		public bool NoAlpha { get; init; }

		public float EffectiveTotalTime => TotalTime > 0f ? TotalTime : 1f;

		/// <summary>
		/// The frame shown at a position along the sequence, the one after it and how far towards it the position
		/// is. A looping sequence crosses from its last frame back to its first.
		/// </summary>
		public (int Frame, int NextFrame, float Blend) GetFrameAtPosition( float position )
		{
			var lastFrame = Frames.Length - 1;
			if ( lastFrame < 1 )
				return (0, 0, 0f);

			var frameStart = 0f;

			for ( var frame = 0; frame < lastFrame; frame++ )
			{
				var displayTime = Frames[frame].DisplayTime;

				if ( frameStart + displayTime > position )
					return (frame, frame + 1, CrossedFraction( position - frameStart, displayTime ));

				frameStart += displayTime;
			}

			return Clamp
				? (lastFrame, lastFrame, 0f)
				: (lastFrame, 0, CrossedFraction( position - frameStart, EffectiveTotalTime - frameStart ));
		}

		private static float CrossedFraction( float into, float span ) => span > 0f ? into / span : 0f;
	}

	public sealed class Frame
	{
		/// <summary>
		/// The frame's rectangle in each of the sheet's layers.
		/// </summary>
		public Image[] Images { get; init; } = [];

		public float DisplayTime { get; init; }
	}

	/// <summary>
	/// A frame's rectangle, in texture coordinates: cropped to what the frame draws, and as authored.
	/// </summary>
	public sealed class Image
	{
		public Vector2 CroppedMin { get; init; }
		public Vector2 CroppedMax { get; init; }
		public Vector2 UncroppedMin { get; init; }
		public Vector2 UncroppedMax { get; init; }
	}
}
