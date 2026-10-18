#nullable enable annotations

using System;
using System.Numerics;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Deadlock.Animation
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;
    using AnimationGraph = Deadlock.Animation.ModelAnimation2.AnimationGraph;

    // A bone mask operation (Esoterica BoneMaskTask)
    public struct BoneMaskTask
    {
        public enum TaskType : byte
        {
            Mask = 0,
            GenerateMask,
            Blend,
            Scale,
            Combine,
        }

        public float Weight;
        public sbyte SourceTaskIdx;
        public sbyte TargetTaskIdx;
        public byte MaskSetIdx;
        public TaskType Type;

        /// <summary>Creates a mask generate task (all weights set to <paramref name="weight"/>).</summary>
        public static BoneMaskTask Generate(float weight)
            => new() { Type = TaskType.GenerateMask, Weight = weight, SourceTaskIdx = -1, TargetTaskIdx = -1, MaskSetIdx = 0xFF };

        /// <summary>Creates a skeleton bone mask reference task.</summary>
        public static BoneMaskTask Mask(byte maskSetIdx)
            => new() { Type = TaskType.Mask, SourceTaskIdx = -1, TargetTaskIdx = -1, MaskSetIdx = maskSetIdx };

        /// <summary>Creates a scale task.</summary>
        public static BoneMaskTask Scale(sbyte sourceIdx, float scale)
            => new() { Type = TaskType.Scale, SourceTaskIdx = sourceIdx, TargetTaskIdx = -1, Weight = scale, MaskSetIdx = 0xFF };

        /// <summary>Creates a combine (multiply) task.</summary>
        public static BoneMaskTask Combine(sbyte sourceIdx, sbyte targetIdx)
            => new() { Type = TaskType.Combine, SourceTaskIdx = sourceIdx, TargetTaskIdx = targetIdx, Weight = -1f, MaskSetIdx = 0xFF };

        /// <summary>Creates a blend task (lerp from source result to target result).</summary>
        public static BoneMaskTask Blend(sbyte sourceIdx, sbyte targetIdx, float blendWeight)
            => new() { Type = TaskType.Blend, SourceTaskIdx = sourceIdx, TargetTaskIdx = targetIdx, Weight = blendWeight, MaskSetIdx = 0xFF };
    }

    // MaxTasks tasks kept by value, so copying a list copies them. One field each rather than an [InlineArray], which
    // the s&box sandbox doesn't allow.
    public struct BoneMaskTaskBuffer
    {
        private BoneMaskTask t0;
        private BoneMaskTask t1;
        private BoneMaskTask t2;
        private BoneMaskTask t3;
        private BoneMaskTask t4;
        private BoneMaskTask t5;
        private BoneMaskTask t6;
        private BoneMaskTask t7;
        private BoneMaskTask t8;
        private BoneMaskTask t9;
        private BoneMaskTask t10;
        private BoneMaskTask t11;
        private BoneMaskTask t12;
        private BoneMaskTask t13;
        private BoneMaskTask t14;
        private BoneMaskTask t15;
        private BoneMaskTask t16;
        private BoneMaskTask t17;
        private BoneMaskTask t18;
        private BoneMaskTask t19;
        private BoneMaskTask t20;
        private BoneMaskTask t21;
        private BoneMaskTask t22;
        private BoneMaskTask t23;
        private BoneMaskTask t24;
        private BoneMaskTask t25;
        private BoneMaskTask t26;
        private BoneMaskTask t27;
        private BoneMaskTask t28;
        private BoneMaskTask t29;
        private BoneMaskTask t30;
        private BoneMaskTask t31;

        public BoneMaskTask this[int index]
        {
            readonly get
            {
                switch (index)
                {
                    case 0: return t0;
                    case 1: return t1;
                    case 2: return t2;
                    case 3: return t3;
                    case 4: return t4;
                    case 5: return t5;
                    case 6: return t6;
                    case 7: return t7;
                    case 8: return t8;
                    case 9: return t9;
                    case 10: return t10;
                    case 11: return t11;
                    case 12: return t12;
                    case 13: return t13;
                    case 14: return t14;
                    case 15: return t15;
                    case 16: return t16;
                    case 17: return t17;
                    case 18: return t18;
                    case 19: return t19;
                    case 20: return t20;
                    case 21: return t21;
                    case 22: return t22;
                    case 23: return t23;
                    case 24: return t24;
                    case 25: return t25;
                    case 26: return t26;
                    case 27: return t27;
                    case 28: return t28;
                    case 29: return t29;
                    case 30: return t30;
                    case 31: return t31;
                    default: throw new IndexOutOfRangeException();
                }
            }
            set
            {
                switch (index)
                {
                    case 0: t0 = value; break;
                    case 1: t1 = value; break;
                    case 2: t2 = value; break;
                    case 3: t3 = value; break;
                    case 4: t4 = value; break;
                    case 5: t5 = value; break;
                    case 6: t6 = value; break;
                    case 7: t7 = value; break;
                    case 8: t8 = value; break;
                    case 9: t9 = value; break;
                    case 10: t10 = value; break;
                    case 11: t11 = value; break;
                    case 12: t12 = value; break;
                    case 13: t13 = value; break;
                    case 14: t14 = value; break;
                    case 15: t15 = value; break;
                    case 16: t16 = value; break;
                    case 17: t17 = value; break;
                    case 18: t18 = value; break;
                    case 19: t19 = value; break;
                    case 20: t20 = value; break;
                    case 21: t21 = value; break;
                    case 22: t22 = value; break;
                    case 23: t23 = value; break;
                    case 24: t24 = value; break;
                    case 25: t25 = value; break;
                    case 26: t26 = value; break;
                    case 27: t27 = value; break;
                    case 28: t28 = value; break;
                    case 29: t29 = value; break;
                    case 30: t30 = value; break;
                    case 31: t31 = value; break;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }
    }

    // A command list of bone mask operations (Esoterica BoneMaskTaskList). Value type: assignment
    // copies the whole list, matching the C++ inline-vector semantics.
    public struct BoneMaskTaskList
    {
        public const int MaxTasks = 32;

        // A default mask list with a single generate task (all weights = 1.0f), C++ s_defaultMaskList
        public static BoneMaskTaskList Default
        {
            get
            {
                var list = new BoneMaskTaskList();
                list.EmplaceTask(1.0f);
                return list;
            }
        }

        // Written through the buffer's indexer, which the compiler doesn't count as an assignment.
#pragma warning disable CS0649
        private BoneMaskTaskBuffer tasks;
#pragma warning restore CS0649
        private int count;

        public readonly bool HasTasks => count > 0;

        public void Reset() => count = 0;

        public readonly sbyte LastTaskIdx => (sbyte)(count - 1);

        public sbyte CopyFrom(in BoneMaskTaskList other)
        {
            this = other;
            return LastTaskIdx;
        }

        private void Add(in BoneMaskTask task)
        {
            Debug.Assert(count < MaxTasks);
            if (count < MaxTasks)
            {
                tasks[count++] = task;
            }
        }

        /// <summary>Emplaces a skeleton bone mask reference task.</summary>
        public void EmplaceTask(byte maskIndex) => Add(BoneMaskTask.Mask(maskIndex));

        /// <summary>Emplaces a mask generate task with a uniform weight.</summary>
        public void EmplaceTask(float uniformWeight) => Add(BoneMaskTask.Generate(uniformWeight));

        /// <summary>Blends this task list's result to the result of the supplied list.</summary>
        public sbyte BlendTo(in BoneMaskTaskList taskList, float blendWeight)
        {
            var sourceTaskIdx = LastTaskIdx;
            var targetTaskIdx = AppendTaskListAndFixDependencies(taskList);
            Add(BoneMaskTask.Blend(sourceTaskIdx, targetTaskIdx, blendWeight));
            return LastTaskIdx;
        }

        /// <summary>Combines (multiplies) the result of this task list with the result of the supplied list.</summary>
        public sbyte CombineWith(in BoneMaskTaskList taskList)
        {
            var sourceTaskIdx = LastTaskIdx;
            var targetTaskIdx = AppendTaskListAndFixDependencies(taskList);
            Add(BoneMaskTask.Combine(sourceTaskIdx, targetTaskIdx));
            return LastTaskIdx;
        }

        /// <summary>Sets this task list to a blend between two task lists.</summary>
        public sbyte SetToBlendBetweenTaskLists(in BoneMaskTaskList sourceTaskList, in BoneMaskTaskList targetTaskList, float blendWeight)
        {
            if (blendWeight == 0f)
            {
                this = sourceTaskList;
            }
            else if (blendWeight == 1f)
            {
                this = targetTaskList;
            }
            else
            {
                this = sourceTaskList;
                BlendTo(targetTaskList, blendWeight);
            }

            return LastTaskIdx;
        }

        /// <summary>Creates a blend from the current registered tasks to a generated mask.</summary>
        public sbyte BlendToGeneratedMask(float generatedMaskWeight, float blendWeight)
        {
            Debug.Assert(blendWeight >= 0f && blendWeight <= 1f);

            if (blendWeight == 0f)
            {
                // Do nothing
            }
            else if (blendWeight == 1f)
            {
                count = 0;
                Add(BoneMaskTask.Generate(generatedMaskWeight));
            }
            else
            {
                var sourceTaskIdx = LastTaskIdx;
                Add(BoneMaskTask.Generate(generatedMaskWeight));
                var targetTaskIdx = LastTaskIdx;
                Add(BoneMaskTask.Blend(sourceTaskIdx, targetTaskIdx, blendWeight));
            }

            return LastTaskIdx;
        }

        /// <summary>Creates a blend from a generated mask to the current registered tasks.</summary>
        public sbyte BlendFromGeneratedMask(float generatedMaskWeight, float blendWeight)
            => BlendToGeneratedMask(generatedMaskWeight, 1f - blendWeight);

        private sbyte AppendTaskListAndFixDependencies(in BoneMaskTaskList taskList)
        {
            var dependencyOffset = (sbyte)(LastTaskIdx + 1);

            for (var i = 0; i < taskList.count; i++)
            {
                var task = taskList.tasks[i];
                if (task.Type is BoneMaskTask.TaskType.Blend or BoneMaskTask.TaskType.Combine)
                {
                    task.SourceTaskIdx += dependencyOffset;
                    task.TargetTaskIdx += dependencyOffset;
                }

                Add(task);
            }

            return LastTaskIdx;
        }

        /// <summary>
        /// Executes the task list, writing the resulting per-bone weight into
        /// <paramref name="result"/>.
        /// </summary>
        public readonly void GenerateBoneMask(Skeleton? skeleton, BoneMaskPool pool, float[] result)
        {
            Debug.Assert(HasTasks);

            Span<int> maskBufferIndices = stackalloc int[count];

            for (var i = 0; i < count; i++)
            {
                var task = tasks[i];

                switch (task.Type)
                {
                    case BoneMaskTask.TaskType.Mask:
                    {
                        maskBufferIndices[i] = pool.Acquire(result.Length);
                        var buffer = pool.GetBuffer(maskBufferIndices[i]);
                        var maskWeights = skeleton?.GetResolvedMaskWeights(task.MaskSetIdx);
                        if (maskWeights != null)
                        {
                            var n = Math.Min(maskWeights.Length, buffer.Length);
                            maskWeights.AsSpan(0, n).CopyTo(buffer);
                            buffer.AsSpan(n).Clear();
                        }
                        else
                        {
                            Array.Fill(buffer, 1f);
                        }

                        break;
                    }

                    case BoneMaskTask.TaskType.GenerateMask:
                    {
                        maskBufferIndices[i] = pool.Acquire(result.Length);
                        Array.Fill(pool.GetBuffer(maskBufferIndices[i]), task.Weight);
                        break;
                    }

                    case BoneMaskTask.TaskType.Scale:
                    {
                        var sourceBuffer = pool.GetBuffer(maskBufferIndices[task.SourceTaskIdx]);
                        maskBufferIndices[i] = maskBufferIndices[task.SourceTaskIdx];
                        for (var b = 0; b < sourceBuffer.Length; b++)
                        {
                            sourceBuffer[b] *= task.Weight;
                        }

                        break;
                    }

                    case BoneMaskTask.TaskType.Combine:
                    case BoneMaskTask.TaskType.Blend:
                    {
                        var sourceBuffer = pool.GetBuffer(maskBufferIndices[task.SourceTaskIdx]);
                        var targetBuffer = pool.GetBuffer(maskBufferIndices[task.TargetTaskIdx]);
                        maskBufferIndices[i] = maskBufferIndices[task.TargetTaskIdx];

                        if (task.Type == BoneMaskTask.TaskType.Combine)
                        {
                            for (var b = 0; b < targetBuffer.Length; b++)
                            {
                                targetBuffer[b] *= sourceBuffer[b];
                            }
                        }
                        else // Blend: lerp from the source result towards the target result
                        {
                            for (var b = 0; b < targetBuffer.Length; b++)
                            {
                                targetBuffer[b] = float.Lerp(sourceBuffer[b], targetBuffer[b], task.Weight);
                            }
                        }

                        break;
                    }
                }
            }

            pool.GetBuffer(maskBufferIndices[count - 1]).AsSpan(0, result.Length).CopyTo(result);
            pool.ReleaseAll();
        }
    }

    /// <summary>
    /// Reusable per-bone weight buffers for bone mask task list evaluation. Buffers persist for the
    /// lifetime of the graph and are recycled via <see cref="ReleaseAll"/> after each evaluation.
    /// </summary>
    public class BoneMaskPool
    {
        private readonly List<float[]> buffers;
        private int firstFree;

        // A task list evaluation acquires at most one buffer per task
        public BoneMaskPool(int boneCount)
        {
            buffers = new List<float[]>(BoneMaskTaskList.MaxTasks);
            for (var i = 0; i < BoneMaskTaskList.MaxTasks; i++)
            {
                buffers.Add(new float[boneCount]);
            }
        }

        public int Acquire(int size)
        {
            if (firstFree == buffers.Count)
            {
                buffers.Add(new float[size]);
            }
            else if (buffers[firstFree].Length < size)
            {
                buffers[firstFree] = new float[size];
            }

            return firstFree++;
        }

        public float[] GetBuffer(int index) => buffers[index];

        public void ReleaseAll() => firstFree = 0;
    }

    partial class BoneMaskValueNode
    {
        protected BoneMaskTaskList TaskList;
        BoneMaskTaskList cachedValue;

        // Returns the node's value, evaluating it at most once per graph update (matches the C++ WasUpdated guard).
        public BoneMaskTaskList GetValue(GraphContext ctx)
        {
            if (!WasUpdated(ctx))
            {
                MarkNodeActive(ctx);
                cachedValue = GetValueInternal(ctx);
            }

            return cachedValue;
        }

        protected virtual BoneMaskTaskList GetValueInternal(GraphContext ctx) => TaskList;
    }

    partial class BoneMaskNode
    {
        public override void Instantiate(GraphContext ctx)
        {
            // ctx.Skeleton (Animation.Skeleton, holding the mask definitions) is not wired up yet, so it can
            // be null — fall back to a uniform-weight mask rather than crashing at graph load.
            var maskIndex = ctx.Skeleton?.GetBoneMaskIndex(BoneMaskID) ?? -1;
            if (maskIndex != -1)
            {
                Debug.Assert(maskIndex >= 0 && maskIndex < 255);
                TaskList.EmplaceTask((byte)maskIndex);
            }
            else
            {
                // Deadlock's shared hero graphs name masks not every hero's skeleton defines (Reload_*_ALT,
                // UpperBodyReplace_Shopping...): those blend nothing in, without a warning on every graph load.
                TaskList.EmplaceTask(0.0f);
            }
        }
    }

    partial class FixedWeightBoneMaskNode
    {
        public override void Instantiate(GraphContext ctx)
        {
            TaskList.EmplaceTask(BoneWeight);
        }
    }

    partial class BoneMaskBlendNode
    {
        BoneMaskValueNode SourceBoneMask;
        BoneMaskValueNode TargetBoneMask;
        FloatValueNode BlendWeightValueNode;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceMaskNodeIdx, ref SourceBoneMask);
            ctx.SetNodeFromIndex(TargetMaskNodeIdx, ref TargetBoneMask);
            ctx.SetNodeFromIndex(BlendWeightValueNodeIdx, ref BlendWeightValueNode);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);
            SourceBoneMask.Initialize(ctx);
            TargetBoneMask.Initialize(ctx);
            BlendWeightValueNode.Initialize(ctx);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            SourceBoneMask.Shutdown(ctx);
            TargetBoneMask.Shutdown(ctx);
            BlendWeightValueNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        protected override BoneMaskTaskList GetValueInternal(GraphContext ctx)
        {
            var blendWeight = BlendWeightValueNode.GetValue(ctx);

            // If we dont need to perform the blend, set the value to the required source
            if (blendWeight <= 0.0f)
            {
                TaskList = SourceBoneMask.GetValue(ctx);
            }
            else if (blendWeight >= 1.0f)
            {
                TaskList = TargetBoneMask.GetValue(ctx);
            }
            else // Actually perform the blend
            {
                TaskList.SetToBlendBetweenTaskLists(SourceBoneMask.GetValue(ctx), TargetBoneMask.GetValue(ctx), blendWeight);
            }

            return TaskList;
        }
    }

    partial class BoneMaskSelectorNode
    {
        IDValueNode ParameterValueNode;
        BoneMaskValueNode? DefaultMaskValueNode;
        BoneMaskValueNode[] MaskOptions;
        int SelectedMaskIndex;
        int NewMaskIndex;
        float CurrentTimeInBlend;
        bool Blending;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ParameterValueNodeIdx, ref ParameterValueNode);
            ctx.SetOptionalNodeFromIndex(DefaultMaskNodeIdx, ref DefaultMaskValueNode);
            ctx.SetNodesFromIndexArray(MaskNodeIndices, ref MaskOptions);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);
            ParameterValueNode.Initialize(ctx);
            DefaultMaskValueNode?.Initialize(ctx);

            foreach (var option in MaskOptions)
            {
                option.Initialize(ctx);
            }

            SelectedMaskIndex = TrySelectMask(ctx);
            NewMaskIndex = -1;
            Blending = false;
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            SelectedMaskIndex = -1;
            NewMaskIndex = -1;
            Blending = false;

            foreach (var option in MaskOptions)
            {
                option.Shutdown(ctx);
            }

            DefaultMaskValueNode?.Shutdown(ctx);
            ParameterValueNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        protected override BoneMaskTaskList GetValueInternal(GraphContext ctx)
        {
            // Perform selection
            //-------------------------------------------------------------------------
            if (SwitchDynamically)
            {
                // Only try to select a new mask if we are not blending
                if (!Blending)
                {
                    NewMaskIndex = TrySelectMask(ctx);

                    // If the new mask is the same as the current one, do nothing
                    if (NewMaskIndex == SelectedMaskIndex)
                    {
                        NewMaskIndex = -1;
                    }
                    else if (BlendTimeSeconds > 0f) // Start a blend to the new mask
                    {
                        CurrentTimeInBlend = 0f;
                        Blending = true;
                    }
                    else // Immediately switch mask
                    {
                        SelectedMaskIndex = NewMaskIndex;
                        NewMaskIndex = -1;
                    }
                }
            }

            // Generate task list
            //-------------------------------------------------------------------------

            if (Blending)
            {
                CurrentTimeInBlend += ctx.DeltaTime;
                var blendWeight = CurrentTimeInBlend / BlendTimeSeconds;

                // If the blend is complete, then update the selected mask index
                if (blendWeight >= 1.0f)
                {
                    TaskList.CopyFrom(GetBoneMaskForIndex(ctx, NewMaskIndex));
                    SelectedMaskIndex = NewMaskIndex;
                    NewMaskIndex = -1;
                    Blending = false;
                }
                else // Perform blend and return the result
                {
                    TaskList.SetToBlendBetweenTaskLists(GetBoneMaskForIndex(ctx, SelectedMaskIndex), GetBoneMaskForIndex(ctx, NewMaskIndex), blendWeight);
                }
            }
            else
            {
                TaskList.CopyFrom(GetBoneMaskForIndex(ctx, SelectedMaskIndex));
            }

            return TaskList;
        }

        private int TrySelectMask(GraphContext ctx) => ParameterValues.IndexOf(ParameterValueNode.GetValue(ctx));

        private BoneMaskTaskList GetBoneMaskForIndex(GraphContext ctx, int optionIndex)
        {
            Debug.Assert(optionIndex >= -1 && optionIndex < MaskOptions.Length);

            if (optionIndex != -1)
            {
                return MaskOptions[optionIndex].GetValue(ctx);
            }

            if (DefaultMaskValueNode != null)
            {
                return DefaultMaskValueNode.GetValue(ctx);
            }

            return BoneMaskTaskList.Default;
        }
    }

    partial class BoneMaskSwitchNode
    {
        BoolValueNode SwitchValueNode;
        BoneMaskValueNode TrueValueNode;
        BoneMaskValueNode FalseValueNode;
        int SelectedMaskIndex;
        float CurrentTimeInBlend;
        bool Blending;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SwitchValueNodeIdx, ref SwitchValueNode);
            ctx.SetNodeFromIndex(TrueValueNodeIdx, ref TrueValueNode);
            ctx.SetNodeFromIndex(FalseValueNodeIdx, ref FalseValueNode);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);

            SwitchValueNode.Initialize(ctx);
            TrueValueNode.Initialize(ctx);
            FalseValueNode.Initialize(ctx);

            SelectedMaskIndex = SwitchValueNode.GetValue(ctx) ? 1 : 0;
            Blending = false;
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            Blending = false;
            FalseValueNode.Shutdown(ctx);
            TrueValueNode.Shutdown(ctx);
            SwitchValueNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        protected override BoneMaskTaskList GetValueInternal(GraphContext ctx)
        {
            // Perform selection
            //-------------------------------------------------------------------------
            if (SwitchDynamically)
            {
                // Only try to select a new mask if we are not blending
                if (!Blending)
                {
                    var newMaskIdx = SwitchValueNode.GetValue(ctx) ? 1 : 0;

                    // If the new mask is the same as the current one, do nothing
                    if (newMaskIdx != SelectedMaskIndex)
                    {
                        SelectedMaskIndex = newMaskIdx;

                        if (BlendTimeSeconds > 0f)
                        {
                            CurrentTimeInBlend = 0f;
                            Blending = true;
                        }
                    }
                }
            }

            // Generate task list
            //-------------------------------------------------------------------------

            if (Blending)
            {
                CurrentTimeInBlend += ctx.DeltaTime;
                var blendWeight = CurrentTimeInBlend / BlendTimeSeconds;

                // If the blend is complete, then update the selected mask index
                if (blendWeight >= 1.0f)
                {
                    TaskList.CopyFrom(GetBoneMaskForIndex(ctx, SelectedMaskIndex));
                    Blending = false;
                }
                else // Perform blend and return the result
                {
                    var sourceList = GetBoneMaskForIndex(ctx, SelectedMaskIndex == 1 ? 0 : 1);
                    var targetList = GetBoneMaskForIndex(ctx, SelectedMaskIndex == 1 ? 1 : 0);
                    TaskList.SetToBlendBetweenTaskLists(sourceList, targetList, blendWeight);
                }
            }
            else
            {
                TaskList.CopyFrom(GetBoneMaskForIndex(ctx, SelectedMaskIndex));
            }

            return TaskList;
        }

        private BoneMaskTaskList GetBoneMaskForIndex(GraphContext ctx, int index)
            => index == 1 ? TrueValueNode.GetValue(ctx) : FalseValueNode.GetValue(ctx);
    }

    partial class VirtualParameterBoneMaskNode
    {
        BoneMaskValueNode ChildNode;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);
            ChildNode.Initialize(ctx);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            ChildNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        // Caching is handled once-per-update by the BoneMaskValueNode base.
        protected override BoneMaskTaskList GetValueInternal(GraphContext ctx) => ChildNode.GetValue(ctx);
    }
}
