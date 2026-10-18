using System;
using System.Collections;

namespace Deadlock.Particles;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// A particle snapshot (a <c>.vsnap</c>'s SNAP block): per-particle attribute arrays that systems spawn from or
/// read positions out of.
/// </summary>
public class ParticleSnapshot
{
	/// <summary>
	/// Gets the number of particles.
	/// </summary>
	public uint NumParticles { get; private set; }

	/// <summary>
	/// Gets the particle attribute data by name and type: <c>float[]</c>, <c>int[]</c>, <c>Vector3[]</c>,
	/// <c>string[]</c> or <see cref="SkinningData"/>[].
	/// </summary>
	public IReadOnlyDictionary<(string Name, string Type), IEnumerable> AttributeData { get; private set; } = new Dictionary<(string Name, string Type), IEnumerable>();

	/// <summary>
	/// Builds a snapshot from its attribute arrays.
	/// </summary>
	public static ParticleSnapshot Create( uint numParticles, IReadOnlyDictionary<(string Name, string Type), IEnumerable> attributeData )
		=> new() { NumParticles = numParticles, AttributeData = attributeData };

	/// <summary>
	/// Maps a <see cref="ParticleField"/> to the snapshot attribute name it is stored under in
	/// <see cref="AttributeData"/>, or null when the field has no snapshot representation.
	/// </summary>
	public static string GetSnapshotAttributeName( ParticleField field ) => field switch
	{
		ParticleField.Position => "position",
		ParticleField.LifeDuration => "lifespan",
		ParticleField.PositionPrevious => "velocity",
		ParticleField.Radius => "radius",
		ParticleField.Roll => "rotation",
		ParticleField.RollSpeed => "rotation_speed",
		ParticleField.Color => "color",
		ParticleField.Alpha => "opacity",
		ParticleField.CreationTime => "creation_time",
		ParticleField.SequenceNumber => "sequence_number",
		ParticleField.TrailLength => "trail_length",
		ParticleField.ParticleId => "particle_id",
		ParticleField.Yaw => "yaw",
		ParticleField.SecondSequenceNumber => "sequence_number1",
		ParticleField.HitboxIndex => "hitbox",
		ParticleField.HitboxOffsetPosition => "hitbox_offset",
		ParticleField.AlphaAlternate => "alpha2",
		ParticleField.ScratchVector => "scratch_vec",
		ParticleField.ScratchFloat => "scratch_float",
		ParticleField.Pitch => "pitch",
		ParticleField.Normal => "normal",
		ParticleField.GlowRgb => "glow_rgb",
		ParticleField.GlowAlpha => "glow_alpha",
		ParticleField.ForceScale => "force_scale",
		ParticleField.ManualAnimationFrame => "manual_animation_frame",
		ParticleField.ShaderExtraData1 => "shader_extra_data_1",
		ParticleField.ShaderExtraData2 => "shader_extra_data_2",
		ParticleField.RopeSegmentId => "rope_segment_id",
		_ => null,
	};

	/// <summary>
	/// The four bones and weights a snapshot particle is skinned to.
	/// </summary>
	public class SkinningData
	{
		public string[] JointNames { get; set; }
		public float[] Weights { get; set; }
	}
}
