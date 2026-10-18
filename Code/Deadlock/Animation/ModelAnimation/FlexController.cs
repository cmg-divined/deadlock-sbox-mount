using System;
using System.Numerics;
namespace Deadlock.Animation.ModelAnimation
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;
    using AnimationGraph = Deadlock.Animation.ModelAnimation2.AnimationGraph;

    /// <summary>
    /// Represents a flex controller for model facial animation.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CFlexController">CFlexController</seealso>
    public class FlexController
    {
        /// <summary>
        /// Gets the name of the flex controller.
        /// </summary>
        public string Name { get; private set; }
        /// <summary>
        /// Gets the controller type. Morph sets compiled by the Dota SDK era tools store the flex name
        /// here instead of the modern <c>default</c>.
        /// </summary>
        public string Type { get; private set; }
        /// <summary>
        /// Gets the minimum value of the flex controller.
        /// </summary>
        public float Min { get; private set; }
        /// <summary>
        /// Gets the maximum value of the flex controller.
        /// </summary>
        public float Max { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FlexController"/> class.
        /// </summary>
        public FlexController(string name, string type, float min, float max)
        {
            Name = name;
            Type = type;
            Min = min;
            Max = max;
        }
    }
}
