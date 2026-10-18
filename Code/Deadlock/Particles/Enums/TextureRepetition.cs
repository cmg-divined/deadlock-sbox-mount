using System;
using System.Numerics;
namespace Deadlock.Particles
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// How a cable renderer's texture repeat count is interpreted along the rope.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/TextureRepetitionMode_t">TextureRepetitionMode_t</seealso>
    public enum TextureRepetitionMode
    {
        /// <summary>The repeat count applies per particle segment.</summary>
        TEXTURE_REPETITION_PARTICLE = 0,
        /// <summary>The repeat count is spread over the whole path.</summary>
        TEXTURE_REPETITION_PATH = 1,
    }
}
