using System;
using System.Numerics;
namespace Deadlock.Particles;


using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
/// <summary>
/// The scene lighting a particle system can query.
/// </summary>
public interface IParticleLighting
{
    /// <summary>Lighting for systems nothing lights: the default ambient grey everywhere.</summary>
    static IParticleLighting Unlit { get; } = new UnlitLighting();

    /// <summary>The indirect (ambient) light arriving at a world position.</summary>
    /// <param name="position">World position to sample at.</param>
    /// <returns>Linear light colour.</returns>
    Vector3 SampleAmbientLight(Vector3 position) => new(0.3f);

    // todo: sample from ambient cube octree?

    private sealed class UnlitLighting : IParticleLighting;
}
