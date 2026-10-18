#nullable enable annotations

using System;
using System.Numerics;
using Deadlock.Animation;
using Deadlock.Animation.ModelAnimation;

namespace Deadlock.Animation.ModelAnimation2
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;
    using ModelSkeleton = Deadlock.Animation.ModelAnimation.Skeleton;

    public partial class AnimationGraph
    {
        /// <summary>
        /// Everything a graph tree uses: its child graphs, the skeletons they animate and the clips they play, read
        /// through an <see cref="IAnimationFileLoader"/>.
        /// </summary>
        private sealed class GraphResources
        {
            public Dictionary<string, KVObject> Graphs { get; } = new(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, (ModelSkeleton Skeleton, Animation.Skeleton GraphSkeleton)> Skeletons { get; } = [];

            public Dictionary<(string Clip, string Skeleton), GraphClip> Clips { get; } = [];

            public string? MissingSkeleton { get; private set; }

            public static string GetSkeletonName(KVObject graph) => graph.GetStringProperty("m_skeleton") ?? string.Empty;

            public static GraphResources Load(string graphName, KVObject graphDefinition, IAnimationFileLoader fileLoader, Func<string, ClipAnimation?>? loadedClips = null)
            {
                var resources = new GraphResources();
                HashSet<(string Clip, string Skeleton)> clipKeys = [];

                if (!resources.Walk(graphName, graphDefinition, fileLoader, clipKeys, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                {
                    return resources;
                }

                var readClips = AnimationGraphLoader.LoadClips(
                    clipKeys.Select(static key => key.Clip).Where(clip => loadedClips?.Invoke(clip) == null),
                    fileLoader);

                foreach (var key in clipKeys)
                {
                    if ((loadedClips?.Invoke(key.Clip) ?? readClips.GetValueOrDefault(key.Clip)) is { } animation)
                    {
                        resources.Clips[key] = new GraphClip(animation, resources.Skeletons[key.Skeleton].Skeleton);
                    }
                }

                return resources;
            }

            private bool Walk(string graphName, KVObject graph, IAnimationFileLoader fileLoader, HashSet<(string Clip, string Skeleton)> clipKeys, HashSet<string> visited)
            {
                if (!visited.Add(graphName))
                {
                    return true;
                }

                var skeletonName = GetSkeletonName(graph);

                if (!Skeletons.TryGetValue(skeletonName, out _))
                {
                    if (skeletonName.Length == 0 || fileLoader.Load(skeletonName) is not { } skeletonData)
                    {
                        MissingSkeleton = skeletonName;
                        return false;
                    }

                    Skeletons[skeletonName] = (ModelSkeleton.FromSkeletonData(skeletonData), new Animation.Skeleton(skeletonData));
                }

                foreach (var resourceName in graph.GetArray<string>("m_resources") ?? [])
                {
                    // Clips are loaded together afterwards; everything else is needed to find more of them.
                    if (resourceName.EndsWith(".vnmclip", StringComparison.OrdinalIgnoreCase))
                    {
                        clipKeys.Add((resourceName, skeletonName));
                        continue;
                    }

                    if (!resourceName.EndsWith(".vnmgraph", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!Graphs.TryGetValue(resourceName, out var childDefinition))
                    {
                        childDefinition = fileLoader.Load(resourceName);
                        if (childDefinition == null)
                        {
                            continue;
                        }

                        Graphs[resourceName] = childDefinition;
                    }

                    if (!Walk(resourceName, childDefinition, fileLoader, clipKeys, visited))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    /// <summary>
    /// Loads the clips a graph tree plays.
    /// </summary>
    public static class AnimationGraphLoader
    {
        public static Dictionary<string, ClipAnimation> LoadClips(IEnumerable<string> clipNames, IAnimationFileLoader fileLoader)
        {
            var result = new Dictionary<string, ClipAnimation>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in clipNames)
            {
                if (result.ContainsKey(name) || fileLoader.Load(name) is not { } data)
                {
                    continue;
                }

                result[name] = new ClipAnimation(new AnimationClip(name, data));
            }

            return result;
        }
    }
}
