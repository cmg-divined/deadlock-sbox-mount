using System;
using System.Numerics;
namespace Deadlock.Particles;


using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
/// <summary>
/// Watches a simulated particle system so a drawing layer can keep up with it. The simulation holds
/// one per system and draws nothing itself, which is what lets it run without a graphics backend.
/// </summary>
public interface IParticleSystemObserver
{
    /// <summary>
    /// Called after the system finishes a simulated frame, once per substep, and never during a
    /// pre-simulation burst.
    /// </summary>
    /// <param name="particles">The system's live particles.</param>
    /// <param name="state">The system's render state.</param>
    void OnFrameSimulated(ParticleCollection particles, ParticleSystemState state);
}
