using System;
using System.Numerics;
namespace Deadlock.Particles;


using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
/// <summary>
/// The local player a particle system can query.
/// </summary>
public interface IParticlePlayer
{
    /// <summary>Gets a position on the player.</summary>
    /// <param name="position">Which position.</param>
    /// <param name="value">The world position.</param>
    /// <returns>False when there is no player.</returns>
    bool TryGetPosition(ParticleEntityPos position, out Vector3 value);

    /// <summary>Gets the direction the player faces.</summary>
    /// <param name="eyes">Whether to take the direction the eyes look along rather than the level one the body faces.</param>
    Vector3 GetForward(bool eyes);
}
