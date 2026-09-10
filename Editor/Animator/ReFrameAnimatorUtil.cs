using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>ReFrame 共通関数 (アニメーション操作)。</summary>
    public static class ReFrameAnimatorUtil
    {
        /// <summary>パラメーター名と、そのパラメーターを固定する実際の値の組。</summary>
        public readonly struct ParameterTarget
        {
            public readonly string Name;
            public readonly float Value;

            /// <summary>true なら、このパラメーターを条件に使っている遷移を「条件だけ抜く」のではなく 他の条件が残っていても遷移ごと削除する ([ReFrameCutTransitions])。</summary>
            public readonly bool CutTransitions;

            public ParameterTarget(string name, float value, bool cutTransitions = false)
            {
                Name = name;
                Value = value;
                CutTransitions = cutTransitions;
            }

            public ParameterTarget(string name, bool value)
                : this(name, value ? 1f : 0f) { }
        }

        /// <summary>VirtualAnimatorController から、各対象のパラメーターを value に固定した状態のアニメーションを まとめて削除する。</summary>
        public static bool RemoveParameters(
            VirtualAnimatorController controller,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates,
            IReadOnlyDictionary<string, float> treeOverrides,
            HashSet<string> foundParameters,
            params ParameterTarget[] targets
        )
        {
            if (controller == null || targets == null || targets.Length == 0)
                return false;
            treeOverrides ??= EmptyOverrides;

            var allValues = new Dictionary<string, float>();
            foreach (var t in targets)
                allValues[t.Name] = t.Value;

            var changed = false;
            foreach (var target in targets)
                changed |= RemoveParameter(
                    controller,
                    bakeRoot,
                    target.Name,
                    target.Value,
                    target.CutTransitions,
                    bakedActiveStates,
                    allValues,
                    treeOverrides,
                    foundParameters
                );
            return changed;
        }

        /// <summary>焼き付けでマテリアルのプロパティを書くときに、書いてよい (複製済みの) マテリアルを 返す関数。</summary>
        public static System.Func<Renderer, int, Material> MaterialWriter;

        static readonly Dictionary<string, float> EmptyOverrides = new();

        /// <summary>[ReFrameBlendTreeOverride] の宣言を、この関数群が引く形のキー (ツリー名 + "\n" + パラメーター名) に変換する。</summary>
        public static Dictionary<string, float> BuildTreeOverrides(
            IEnumerable<(string TreeName, string ParameterName, float Value, bool Always)> overrides,
            IEnumerable<string> deletingParameters
        )
        {
            var result = new Dictionary<string, float>();
            if (overrides == null)
                return result;
            var deleting = new HashSet<string>(deletingParameters ?? Enumerable.Empty<string>());
            foreach (var (treeName, parameterName, value, always) in overrides)
            {
                if (!always && !deleting.Contains(parameterName))
                    continue;
                result[treeName + "\n" + parameterName] = value;
            }
            return result;
        }

        /// <summary>[ReFrameBlendTreeOverride(Always = true)] を、削除対象パラメーターの有無に関わらず 適用する。</summary>
        public static bool ApplyTreeOverrides(
            VirtualAnimatorController controller,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates,
            IReadOnlyDictionary<string, float> treeOverrides
        )
        {
            if (controller == null || treeOverrides == null || treeOverrides.Count == 0)
                return false;
            var changed = false;
            foreach (var layer in controller.Layers)
                if (layer.StateMachine != null)
                    changed |= ApplyTreeOverridesInStateMachine(
                        layer.StateMachine,
                        controller,
                        bakeRoot,
                        bakedActiveStates,
                        treeOverrides
                    );
            return changed;
        }

        static bool ApplyTreeOverridesInStateMachine(
            VirtualStateMachine sm,
            VirtualAnimatorController controller,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates,
            IReadOnlyDictionary<string, float> treeOverrides
        )
        {
            var changed = false;
            foreach (var childState in sm.States)
            {
                var state = childState.State;
                if (state == null)
                    continue;

                var newMotion = CleanMotion(
                    state.Motion,
                    controller,
                    null,
                    0f,
                    AnimatorControllerParameterType.Float,
                    bakeRoot,
                    bakedActiveStates,
                    EmptyOverrides,
                    treeOverrides,
                    ref changed
                );
                if (!ReferenceEquals(newMotion, state.Motion))
                {
                    state.Motion = newMotion;
                    changed = true;
                }
            }
            foreach (var child in sm.StateMachines)
                if (child.StateMachine != null)
                    changed |= ApplyTreeOverridesInStateMachine(
                        child.StateMachine,
                        controller,
                        bakeRoot,
                        bakedActiveStates,
                        treeOverrides
                    );
            return changed;
        }

        static bool RemoveParameter(
            VirtualAnimatorController controller,
            Transform bakeRoot,
            string parameterName,
            float value,
            bool cutTransitions,
            Dictionary<Transform, bool> bakedActiveStates,
            IReadOnlyDictionary<string, float> allValues,
            IReadOnlyDictionary<string, float> treeOverrides,
            HashSet<string> foundParameters
        )
        {
            if (string.IsNullOrEmpty(parameterName))
                return false;

            if (!controller.Parameters.TryGetValue(parameterName, out var paramDef))
                return false;
            foundParameters?.Add(parameterName);
            var normalized = NormalizeValue(value, paramDef.type);

            var changed = false;
            foreach (var layer in controller.Layers)
            {
                if (layer.StateMachine != null)
                    changed |= ProcessStateMachine(
                        layer.StateMachine,
                        controller,
                        parameterName,
                        normalized,
                        paramDef.type,
                        cutTransitions,
                        bakeRoot,
                        bakedActiveStates,
                        allValues,
                    treeOverrides
                    );
            }

            if (controller.Parameters.ContainsKey(parameterName))
            {
                controller.Parameters = controller.Parameters.Remove(parameterName);
                changed = true;
            }
            return changed;
        }

        static bool ProcessStateMachine(
            VirtualStateMachine sm,
            VirtualAnimatorController controller,
            string param,
            float value,
            AnimatorControllerParameterType paramType,
            bool cutTransitions,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates,
            IReadOnlyDictionary<string, float> allValues,
            IReadOnlyDictionary<string, float> treeOverrides
        )
        {
            var changed = false;
            foreach (var childState in sm.States)
            {
                var state = childState.State;
                if (state == null)
                    continue;

                var newMotion = CleanMotion(
                    state.Motion,
                    controller,
                    param,
                    value,
                    paramType,
                    bakeRoot,
                    bakedActiveStates,
                    allValues,
                treeOverrides,
                    ref changed
                );
                if (!ReferenceEquals(newMotion, state.Motion))
                {
                    state.Motion = newMotion;
                    changed = true;
                }
                if (state.TimeParameter == param)
                {
                    state.TimeParameter = null;
                    changed = true;
                }
                if (state.SpeedParameter == param)
                {
                    state.SpeedParameter = null;
                    changed = true;
                }
                if (state.CycleOffsetParameter == param)
                {
                    state.CycleOffsetParameter = null;
                    changed = true;
                }
                if (state.MirrorParameter == param)
                {
                    state.MirrorParameter = null;
                    changed = true;
                }

                var cleanedTransitions = CleanTransitions(state.Transitions, param, value, paramType, cutTransitions, ref changed);
                if (!ReferenceEquals(cleanedTransitions, state.Transitions))
                    state.Transitions = cleanedTransitions;

                var cleanedBehaviours = CleanParameterDrivers(state.Behaviours, param, ref changed);
                if (!ReferenceEquals(cleanedBehaviours, state.Behaviours))
                    state.Behaviours = cleanedBehaviours;
            }

            var cleanedAnyState = CleanTransitions(sm.AnyStateTransitions, param, value, paramType, cutTransitions, ref changed);
            if (!ReferenceEquals(cleanedAnyState, sm.AnyStateTransitions))
                sm.AnyStateTransitions = cleanedAnyState;

            var cleanedEntry = CleanTransitions(sm.EntryTransitions, param, value, paramType, cutTransitions, ref changed);
            if (!ReferenceEquals(cleanedEntry, sm.EntryTransitions))
                sm.EntryTransitions = cleanedEntry;

            foreach (var child in sm.StateMachines)
            {
                var childSm = child.StateMachine;
                if (childSm == null)
                    continue;

                if (sm.StateMachineTransitions.TryGetValue(childSm, out var childTransitions))
                {
                    var cleanedChildTransitions = CleanTransitions(childTransitions, param, value, paramType, cutTransitions, ref changed);
                    if (!ReferenceEquals(cleanedChildTransitions, childTransitions))
                        sm.StateMachineTransitions = sm.StateMachineTransitions.SetItem(
                            childSm,
                            cleanedChildTransitions
                        );
                }

                changed |= ProcessStateMachine(
                    childSm,
                    controller,
                    param,
                    value,
                    paramType,
                    cutTransitions,
                    bakeRoot,
                    bakedActiveStates,
                    allValues,
                    treeOverrides
                );
            }
            return changed;
        }

        /// <summary>param の条件を、固定する値 value で実際に評価して整理する。</summary>
        static ImmutableList<T> CleanTransitions<T>(
            ImmutableList<T> transitions,
            string param,
            float value,
            AnimatorControllerParameterType paramType,
            bool cutTransitions,
            ref bool changed
        )
            where T : VirtualTransitionBase
        {
            if (transitions == null || transitions.Count == 0)
                return transitions;

            var result = new List<T>(transitions.Count);
            var localChanged = false;
            foreach (var transition in transitions)
            {
                if (transition == null)
                    continue;
                var conditions = transition.Conditions;
                var filtered = conditions.Where(c => c.parameter != param).ToImmutableList();
                if (filtered.Count == conditions.Count)
                {
                    result.Add(transition);
                    continue;
                }
                localChanged = true;

                if (cutTransitions)
                    continue;

                var unsatisfiable = conditions.Any(c =>
                    c.parameter == param && !ConditionHolds(c, value, paramType)
                );
                if (unsatisfiable)
                    continue;

                var hasExitTime = transition is VirtualStateTransition st && st.ExitTime.HasValue;
                if (filtered.Count == 0 && !hasExitTime)
                    continue;
                transition.Conditions = filtered;
                result.Add(transition);
            }
            return localChanged ? result.ToImmutableList() : transitions;
        }

        /// <summary>パラメーターを value に固定したとき、その条件が成立するか。</summary>
        static bool ConditionHolds(
            AnimatorCondition condition,
            float value,
            AnimatorControllerParameterType paramType
        )
        {
            var threshold = condition.threshold;
            switch (condition.mode)
            {
                case AnimatorConditionMode.If:
                    return value != 0f;
                case AnimatorConditionMode.IfNot:
                    return value == 0f;
                case AnimatorConditionMode.Greater:
                    return value > threshold;
                case AnimatorConditionMode.Less:
                    return value < threshold;
                case AnimatorConditionMode.Equals:
                    return paramType == AnimatorControllerParameterType.Int
                        ? Mathf.RoundToInt(value) == Mathf.RoundToInt(threshold)
                        : Mathf.Approximately(value, threshold);
                case AnimatorConditionMode.NotEqual:
                    return paramType == AnimatorControllerParameterType.Int
                        ? Mathf.RoundToInt(value) != Mathf.RoundToInt(threshold)
                        : !Mathf.Approximately(value, threshold);
                default:

                    return true;
            }
        }

        /// <summary>ステートに付いた VRCAvatarParameterDriver から、削除対象パラメーターへの割り当て (parameters リストの要素) だけを取り除く。</summary>
        static ImmutableList<StateMachineBehaviour> CleanParameterDrivers(
            ImmutableList<StateMachineBehaviour> behaviours,
            string param,
            ref bool changed
        )
        {
            if (behaviours == null || behaviours.Count == 0)
                return behaviours;

            var result = new List<StateMachineBehaviour>(behaviours.Count);
            var localChanged = false;
            foreach (var behaviour in behaviours)
            {
                var driver = behaviour as VRCAvatarParameterDriver;
                if (driver == null || driver.parameters == null)
                {
                    result.Add(behaviour);
                    continue;
                }

                var removed = driver.parameters.RemoveAll(p => p != null && p.name == param);
                if (removed == 0)
                {
                    result.Add(behaviour);
                    continue;
                }

                localChanged = true;
                if (driver.parameters.Count > 0)
                    result.Add(behaviour);

            }
            if (!localChanged)
                return behaviours;
            changed = true;
            return result.ToImmutableList();
        }

        /// <summary>Motion を再帰的に処理し、差し替え後の Motion を返す (null なら削除)。</summary>
        static VirtualMotion CleanMotion(
            VirtualMotion motion,
            VirtualAnimatorController controller,
            string param,
            float value,
            AnimatorControllerParameterType paramType,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates,
            IReadOnlyDictionary<string, float> allValues,
            IReadOnlyDictionary<string, float> treeOverrides,
            ref bool changed,
            bool conditional = false
        )
        {
            if (motion is not VirtualBlendTree bt)
                return motion;

            if (Is2DBlendType(bt.BlendType))
            {

                if (
                    allValues.TryGetValue(bt.BlendParameter, out var xValue)
                    && !string.IsNullOrEmpty(bt.BlendParameterY)
                    && allValues.TryGetValue(bt.BlendParameterY, out var yValue)
                )
                {
                    changed = true;
                    return Resolve2DMatchedBranch(
                        bt,
                        xValue,
                        yValue,
                        controller,
                        param,
                        value,
                        paramType,
                        bakeRoot,
                        bakedActiveStates,
                        allValues,
                treeOverrides,
                        ref changed,
                        conditional
                    );
                }
                if (bt.BlendParameter == param || bt.BlendParameterY == param)
                    Debug.LogWarning(
                        $"[ReFrameCore] Blend tree '{bt.Name}' blends on '{bt.BlendParameter}'/"
                            + $"'{bt.BlendParameterY}' but only one axis is being fixed; "
                            + "both axes must be removal targets at the same time. Skipping this node."
                    );

            }
            else if (bt.BlendType == BlendTreeType.Simple1D)
            {
                var hasFixedValue = false;
                var matchValue = 0f;
                var matchType = paramType;

                if (treeOverrides.TryGetValue(bt.Name + "\n" + bt.BlendParameter, out var forced))
                {
                    hasFixedValue = true;
                    matchValue = controller.Parameters.TryGetValue(bt.BlendParameter, out var forcedDef)
                        ? NormalizeValue(forced, forcedDef.type)
                        : forced;
                    if (controller.Parameters.TryGetValue(bt.BlendParameter, out var forcedDef2))
                        matchType = forcedDef2.type;
                }
                else if (bt.BlendParameter == param)
                {
                    hasFixedValue = true;
                    matchValue = value;
                }
                else if (
                    allValues.TryGetValue(bt.BlendParameter, out var otherRaw)
                    && controller.Parameters.TryGetValue(bt.BlendParameter, out var otherDef)
                )
                {
                    hasFixedValue = true;
                    matchType = otherDef.type;
                    matchValue = NormalizeValue(otherRaw, otherDef.type);
                }

                if (hasFixedValue)
                {
                    changed = true;
                    return ResolveMatchedBranch(
                        bt,
                        controller,
                        param,
                        value,
                        matchValue,
                        matchType,
                        bakeRoot,
                        bakedActiveStates,
                        allValues,
                treeOverrides,
                        ref changed,
                        conditional
                    );
                }

            }
            else if (bt.BlendParameter == param)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] Blend tree '{bt.Name}' blends on '{param}' but is not a 1D tree; "
                        + "value-based removal is only supported for 1D trees. Skipping this node."
                );
            }

            var children = bt.Children;
            var childConditional = conditional || !IsPassThroughTree(bt);
            var list = new List<VirtualBlendTree.VirtualChildMotion>(children.Count);
            var localChanged = false;
            foreach (var child in children)
            {
                var c = child;
                var newMotion = CleanMotion(
                    c.Motion,
                    controller,
                    param,
                    value,
                    paramType,
                    bakeRoot,
                    bakedActiveStates,
                    allValues,
                treeOverrides,
                    ref changed,
                    childConditional
                );
                if (newMotion == null)
                {
                    localChanged = true;
                    continue;
                }
                if (!ReferenceEquals(newMotion, c.Motion))
                {
                    c.Motion = newMotion;
                    localChanged = true;
                }
                list.Add(c);
            }
            if (localChanged)
            {
                bt.Children = list.ToImmutableList();
                changed = true;
            }
            return bt;
        }

        /// <summary>Simple1D ツリーのうち matchValue に一致する枝を選び、AnimationClip ならベイクして削除、 BlendTree ならその子ツリーを繰り上げて返す。</summary>
        static VirtualMotion ResolveMatchedBranch(
            VirtualBlendTree bt,
            VirtualAnimatorController controller,
            string param,
            float value,
            float matchValue,
            AnimatorControllerParameterType matchType,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates,
            IReadOnlyDictionary<string, float> allValues,
            IReadOnlyDictionary<string, float> treeOverrides,
            ref bool changed,
            bool conditional
        )
        {
            var matchedChild = FindSimple1DChild(bt.Children, matchValue, matchType);
            if (matchedChild != null)
            {
                var child = matchedChild;
                if (child.Motion is VirtualClip clip)
                {
                    if (conditional)
                        return clip;
                    BakeClip(clip, bakeRoot, bakedActiveStates);
                    return null;
                }
                return CleanMotion(
                    child.Motion,
                    controller,
                    param,
                    value,
                    matchType,
                    bakeRoot,
                    bakedActiveStates,
                    allValues,
                treeOverrides,
                    ref changed,
                    conditional
                );
            }

            if (
                matchType == AnimatorControllerParameterType.Float
                && TryFindSimple1DNeighbors(bt.Children, matchValue, out var lo, out var hi, out var t)
            )
            {
                var loFloats = ReFrameBlendUtil.Pool.RentFloats();
                var loObjects = ReFrameBlendUtil.Pool.RentObjects();
                var hiFloats = ReFrameBlendUtil.Pool.RentFloats();
                var hiObjects = ReFrameBlendUtil.Pool.RentObjects();
                var loOk = TryEvaluateMotionState(
                    lo.Motion,
                    controller,
                    param,
                    value,
                    matchType,
                    allValues,
                treeOverrides,
                    loFloats,
                    loObjects
                );
                var hiOk = TryEvaluateMotionState(
                    hi.Motion,
                    controller,
                    param,
                    value,
                    matchType,
                    allValues,
                treeOverrides,
                    hiFloats,
                    hiObjects
                );
                if (loOk && hiOk)
                {
                    var mergedFloats = ReFrameBlendUtil.Pool.RentFloats();
                    var mergedObjects = ReFrameBlendUtil.Pool.RentObjects();
                    ReFrameBlendUtil.MergeInterpolated(loFloats, hiFloats, t, mergedFloats);
                    ReFrameBlendUtil.MergeDominant(loObjects, hiObjects, t, mergedObjects);
                    VirtualMotion replacement = null;
                    if (conditional)
                        replacement = CreateConstantClip($"{bt.Name} ({bt.BlendParameter}={matchValue})", mergedFloats, mergedObjects);
                    else
                        BakeBlendedState(mergedFloats, mergedObjects, bakeRoot, bakedActiveStates);
                    ReFrameBlendUtil.Pool.ReturnFloats(loFloats);
                    ReFrameBlendUtil.Pool.ReturnObjects(loObjects);
                    ReFrameBlendUtil.Pool.ReturnFloats(hiFloats);
                    ReFrameBlendUtil.Pool.ReturnObjects(hiObjects);
                    ReFrameBlendUtil.Pool.ReturnFloats(mergedFloats);
                    ReFrameBlendUtil.Pool.ReturnObjects(mergedObjects);
                    changed = true;
                    return replacement;
                }
                ReFrameBlendUtil.Pool.ReturnFloats(loFloats);
                ReFrameBlendUtil.Pool.ReturnObjects(loObjects);
                ReFrameBlendUtil.Pool.ReturnFloats(hiFloats);
                ReFrameBlendUtil.Pool.ReturnObjects(hiObjects);
            }

            if (conditional)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] Value {matchValue} does not match any threshold of blend tree '{bt.Name}' "
                        + $"('{bt.BlendParameter}') and it sits under an unfixed selector; leaving it as is."
                );
                return bt;
            }
            Debug.LogWarning(
                $"[ReFrameCore] Value {matchValue} does not match any threshold of blend tree '{bt.Name}' "
                    + $"('{bt.BlendParameter}'). The branch was removed without baking a state."
            );
            return null;
        }

        /// <summary>value が children のどの閾値とも一致せず (クランプ対象の範囲外でもなく)、2 つの閾値の間に 挟まれている場合に、その両側の子 (lo/hi) と線形補間の重み t (0=lo, 1=hi) を返す。</summary>
        static bool TryFindSimple1DNeighbors(
            ImmutableList<VirtualBlendTree.VirtualChildMotion> children,
            float value,
            out VirtualBlendTree.VirtualChildMotion lo,
            out VirtualBlendTree.VirtualChildMotion hi,
            out float t
        )
        {
            lo = null;
            hi = null;
            t = 0f;
            VirtualBlendTree.VirtualChildMotion loCand = null;
            VirtualBlendTree.VirtualChildMotion hiCand = null;
            foreach (var c in children)
            {
                if (c.Threshold <= value && (loCand == null || c.Threshold > loCand.Threshold))
                    loCand = c;
                if (c.Threshold > value && (hiCand == null || c.Threshold < hiCand.Threshold))
                    hiCand = c;
            }
            if (loCand == null || hiCand == null || Mathf.Approximately(loCand.Threshold, hiCand.Threshold))
                return false;
            lo = loCand;
            hi = hiCand;
            t = (value - loCand.Threshold) / (hiCand.Threshold - loCand.Threshold);
            return true;
        }

        /// <summary>Motion を末端まで解決し、その状態を表す float/Object カーブの値を outFloats/outObjects に書き込む (実際にオブジェクトへは反映せず、値の収集のみ行う。</summary>
        static bool TryEvaluateMotionState(
            VirtualMotion motion,
            VirtualAnimatorController controller,
            string param,
            float value,
            AnimatorControllerParameterType paramType,
            IReadOnlyDictionary<string, float> allValues,
            IReadOnlyDictionary<string, float> treeOverrides,
            Dictionary<EditorCurveBinding, float> outFloats,
            Dictionary<EditorCurveBinding, Object> outObjects
        )
        {
            if (motion is VirtualClip clip)
            {
                foreach (var binding in clip.GetFloatCurveBindings())
                {
                    var curve = clip.GetFloatCurve(binding);
                    if (curve != null && curve.keys.Length > 0)
                        outFloats[binding] = curve.keys[0].value;
                }
                foreach (var binding in clip.GetObjectCurveBindings())
                {
                    var keys = clip.GetObjectCurve(binding);
                    if (keys != null && keys.Length > 0)
                        outObjects[binding] = keys[0].value;
                }
                return true;
            }

            if (motion is not VirtualBlendTree nested)
                return false;

            if (Is2DBlendType(nested.BlendType))
            {
                if (
                    allValues.TryGetValue(nested.BlendParameter, out var xValue)
                    && !string.IsNullOrEmpty(nested.BlendParameterY)
                    && allValues.TryGetValue(nested.BlendParameterY, out var yValue)
                )
                {
                    const float epsilon = 1e-4f;
                    foreach (var c in nested.Children)
                    {
                        if (
                            Mathf.Abs(c.Position.x - xValue) > epsilon
                            || Mathf.Abs(c.Position.y - yValue) > epsilon
                        )
                            continue;
                        return TryEvaluateMotionState(
                            c.Motion,
                            controller,
                            param,
                            value,
                            paramType,
                            allValues,
                treeOverrides,
                            outFloats,
                            outObjects
                        );
                    }
                    return Try2DGbiBlend(
                        nested.Children,
                        xValue,
                        yValue,
                        controller,
                        param,
                        value,
                        paramType,
                        allValues,
                treeOverrides,
                        outFloats,
                        outObjects
                    );
                }
                return false;
            }

            if (nested.BlendType == BlendTreeType.Simple1D)
            {
                var hasFixedValue = false;
                var matchValue = 0f;
                var matchType = paramType;
                if (nested.BlendParameter == param)
                {
                    hasFixedValue = true;
                    matchValue = value;
                }
                else if (
                    allValues.TryGetValue(nested.BlendParameter, out var otherRaw)
                    && controller.Parameters.TryGetValue(nested.BlendParameter, out var otherDef)
                )
                {
                    hasFixedValue = true;
                    matchType = otherDef.type;
                    matchValue = NormalizeValue(otherRaw, otherDef.type);
                }

                if (!hasFixedValue)
                    return false;

                var exact = FindSimple1DChild(nested.Children, matchValue, matchType);
                if (exact != null)
                    return TryEvaluateMotionState(
                        exact.Motion,
                        controller,
                        param,
                        value,
                        matchType,
                        allValues,
                treeOverrides,
                        outFloats,
                        outObjects
                    );

                if (
                    matchType == AnimatorControllerParameterType.Float
                    && TryFindSimple1DNeighbors(nested.Children, matchValue, out var lo, out var hi, out var t)
                )
                {
                    var loFloats = ReFrameBlendUtil.Pool.RentFloats();
                    var loObjects = ReFrameBlendUtil.Pool.RentObjects();
                    var hiFloats = ReFrameBlendUtil.Pool.RentFloats();
                    var hiObjects = ReFrameBlendUtil.Pool.RentObjects();
                    var ok =
                        TryEvaluateMotionState(
                            lo.Motion,
                            controller,
                            param,
                            value,
                            matchType,
                            allValues,
                treeOverrides,
                            loFloats,
                            loObjects
                        )
                        && TryEvaluateMotionState(
                            hi.Motion,
                            controller,
                            param,
                            value,
                            matchType,
                            allValues,
                treeOverrides,
                            hiFloats,
                            hiObjects
                        );
                    if (ok)
                    {
                        ReFrameBlendUtil.MergeInterpolated(loFloats, hiFloats, t, outFloats);
                        ReFrameBlendUtil.MergeDominant(loObjects, hiObjects, t, outObjects);
                    }
                    ReFrameBlendUtil.Pool.ReturnFloats(loFloats);
                    ReFrameBlendUtil.Pool.ReturnObjects(loObjects);
                    ReFrameBlendUtil.Pool.ReturnFloats(hiFloats);
                    ReFrameBlendUtil.Pool.ReturnObjects(hiObjects);
                    if (ok)
                        return true;
                }
                return false;
            }

            return false;
        }

        /// <summary>合成した float/Object の値を、定数カーブだけの 1 フレームのクリップにする (固定していない選択ツリーの下ではシーンへ焼けないので、枝をこれで置き換える)。</summary>
        static VirtualClip CreateConstantClip(
            string name,
            Dictionary<EditorCurveBinding, float> floats,
            Dictionary<EditorCurveBinding, Object> objects
        )
        {
            var clip = VirtualClip.Create(name);
            foreach (var kv in floats)
                clip.SetFloatCurve(kv.Key, AnimationCurve.Constant(0f, 0f, kv.Value));
            foreach (var kv in objects)
                clip.SetObjectCurve(
                    kv.Key,
                    new[] { new ObjectReferenceKeyframe { time = 0f, value = kv.Value } }
                );
            return clip;
        }

        /// <summary>無段階ブレンドで合成した float/Object カーブの値を bakeRoot 上のオブジェクトへ直接反映する。</summary>
        static void BakeBlendedState(
            Dictionary<EditorCurveBinding, float> floats,
            Dictionary<EditorCurveBinding, Object> objects,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates
        )
        {
            foreach (var kv in floats)
                ApplyFloatBinding(kv.Key, kv.Value, bakeRoot, bakedActiveStates);
            foreach (var kv in objects)
                ApplyObjectBinding(kv.Key, kv.Value, bakeRoot);
        }

        /// <summary>2D 系ツリー (SimpleDirectional2D 等) のうち、(x, y) の位置に厳密に一致する子を 1 つ選ぶ (Simple1D の閾値一致と同じ考え方を 2 軸に拡張しただけ)。</summary>
        static VirtualMotion Resolve2DMatchedBranch(
            VirtualBlendTree bt,
            float x,
            float y,
            VirtualAnimatorController controller,
            string param,
            float value,
            AnimatorControllerParameterType paramType,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates,
            IReadOnlyDictionary<string, float> allValues,
            IReadOnlyDictionary<string, float> treeOverrides,
            ref bool changed,
            bool conditional
        )
        {
            const float epsilon = 1e-4f;
            foreach (var child in bt.Children)
            {
                if (
                    Mathf.Abs(child.Position.x - x) > epsilon
                    || Mathf.Abs(child.Position.y - y) > epsilon
                )
                    continue;
                if (child.Motion is VirtualClip clip)
                {
                    if (conditional)
                        return clip;
                    BakeClip(clip, bakeRoot, bakedActiveStates);
                    return null;
                }
                return CleanMotion(
                    child.Motion,
                    controller,
                    param,
                    value,
                    paramType,
                    bakeRoot,
                    bakedActiveStates,
                    allValues,
                treeOverrides,
                    ref changed,
                    conditional
                );
            }

            var blendedFloats = ReFrameBlendUtil.Pool.RentFloats();
            var blendedObjects = ReFrameBlendUtil.Pool.RentObjects();
            var blended = Try2DGbiBlend(
                bt.Children,
                x,
                y,
                controller,
                param,
                value,
                paramType,
                allValues,
                treeOverrides,
                blendedFloats,
                blendedObjects
            );
            VirtualMotion blendedReplacement = null;
            if (blended && conditional)
                blendedReplacement = CreateConstantClip($"{bt.Name} ({x}, {y})", blendedFloats, blendedObjects);
            else if (blended)
                BakeBlendedState(blendedFloats, blendedObjects, bakeRoot, bakedActiveStates);
            ReFrameBlendUtil.Pool.ReturnFloats(blendedFloats);
            ReFrameBlendUtil.Pool.ReturnObjects(blendedObjects);
            if (blended)
            {
                changed = true;
                return blendedReplacement;
            }

            if (conditional)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] Position ({x}, {y}) does not match any child of blend tree '{bt.Name}' "
                        + $"('{bt.BlendParameter}'/'{bt.BlendParameterY}') and it sits under an unfixed selector; "
                        + "leaving it as is."
                );
                return bt;
            }
            Debug.LogWarning(
                $"[ReFrameCore] Position ({x}, {y}) does not match any child of blend tree '{bt.Name}' "
                    + $"('{bt.BlendParameter}'/'{bt.BlendParameterY}'). The branch was removed without "
                    + "baking a state."
            );
            return null;
        }

        /// <summary>2D BlendTree の全ての子 (Motion が null のものは除く) を Gradient Band Interpolation で 重み付けし、各子を末端まで再帰的に解決したうえで重み付き合成する。</summary>
        static bool Try2DGbiBlend(
            ImmutableList<VirtualBlendTree.VirtualChildMotion> children,
            float x,
            float y,
            VirtualAnimatorController controller,
            string param,
            float value,
            AnimatorControllerParameterType paramType,
            IReadOnlyDictionary<string, float> allValues,
            IReadOnlyDictionary<string, float> treeOverrides,
            Dictionary<EditorCurveBinding, float> outFloats,
            Dictionary<EditorCurveBinding, Object> outObjects
        )
        {
            var validChildren = new List<VirtualBlendTree.VirtualChildMotion>();
            foreach (var c in children)
                if (c.Motion != null)
                    validChildren.Add(c);
            if (validChildren.Count == 0)
                return false;

            var points = new List<Vector2>(validChildren.Count);
            foreach (var c in validChildren)
                points.Add(c.Position);
            var weights = ReFrameBlendUtil.ComputeGbiWeights(points, new Vector2(x, y));

            var floatParts = new List<ReFrameBlendUtil.WeightedFloats>();
            var objectParts = new List<ReFrameBlendUtil.WeightedObjects<Object>>();
            var rentedFloats = new List<Dictionary<EditorCurveBinding, float>>();
            var rentedObjects = new List<Dictionary<EditorCurveBinding, Object>>();
            for (var i = 0; i < validChildren.Count; i++)
            {
                if (weights[i] <= 1e-4f)
                    continue;
                var childFloats = ReFrameBlendUtil.Pool.RentFloats();
                var childObjects = ReFrameBlendUtil.Pool.RentObjects();
                rentedFloats.Add(childFloats);
                rentedObjects.Add(childObjects);
                if (
                    !TryEvaluateMotionState(
                        validChildren[i].Motion,
                        controller,
                        param,
                        value,
                        paramType,
                        allValues,
                treeOverrides,
                        childFloats,
                        childObjects
                    )
                )
                {
                    foreach (var d in rentedFloats)
                        ReFrameBlendUtil.Pool.ReturnFloats(d);
                    foreach (var d in rentedObjects)
                        ReFrameBlendUtil.Pool.ReturnObjects(d);
                    return false;
                }
                floatParts.Add(new ReFrameBlendUtil.WeightedFloats(childFloats, weights[i]));
                objectParts.Add(new ReFrameBlendUtil.WeightedObjects<Object>(childObjects, weights[i]));
            }
            if (floatParts.Count == 0)
                return false;

            ReFrameBlendUtil.MergeWeightedFloats(floatParts, outFloats);
            ReFrameBlendUtil.MergeWeightedObjects(objectParts, outObjects);
            foreach (var d in rentedFloats)
                ReFrameBlendUtil.Pool.ReturnFloats(d);
            foreach (var d in rentedObjects)
                ReFrameBlendUtil.Pool.ReturnObjects(d);
            return true;
        }

        /// <summary>名前ベースの削除対象。</summary>
        public readonly struct RelatedBlendTreeTarget
        {
            public readonly string Name;

            /// <summary>true の場合、削除する前にそのノードの見た目を bakeRoot 上へ焼き付けてから削除する (どの枝を焼くかは、そのノード自身の BlendParameter が AnimatorController 上に持つ デフォルト値で解決する。</summary>
            public readonly bool Bake;

            public RelatedBlendTreeTarget(string name, bool bake = false)
            {
                Name = name;
                Bake = bake;
            }
        }

        /// <summary>パラメーターとは無関係に、Name が対象に含まれる VirtualBlendTree ノードをまとめて 削除する ([ReFrameDeleteRelatedBlendTree] 用)。</summary>
        public static bool RemoveNamedBlendTrees(
            VirtualAnimatorController controller,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates,
            IEnumerable<RelatedBlendTreeTarget> targets
        )
        {
            if (controller == null || targets == null)
                return false;
            var byName = new Dictionary<string, bool>();
            foreach (var t in targets)
            {
                if (string.IsNullOrEmpty(t.Name))
                    continue;
                byName.TryGetValue(t.Name, out var existingBake);
                byName[t.Name] = existingBake || t.Bake;
            }
            if (byName.Count == 0)
                return false;

            var changed = false;
            var removedStates = new HashSet<VirtualState>();

            foreach (var layer in controller.Layers)
                if (layer.StateMachine != null)
                    changed |= RemoveNamedStatesAndChildren(
                        layer.StateMachine,
                        byName,
                        controller,
                        bakeRoot,
                        bakedActiveStates,
                        removedStates
                    );

            if (removedStates.Count > 0)
                foreach (var layer in controller.Layers)
                    if (layer.StateMachine != null)
                        changed |= StripDanglingStateReferences(layer.StateMachine, removedStates);

            return changed;
        }

        static bool RemoveNamedStatesAndChildren(
            VirtualStateMachine sm,
            Dictionary<string, bool> names,
            VirtualAnimatorController controller,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates,
            HashSet<VirtualState> removedStates
        )
        {
            var changed = false;
            var kept = new List<VirtualStateMachine.VirtualChildState>(sm.States.Count);
            foreach (var childState in sm.States)
            {
                var state = childState.State;
                if (state == null)
                {
                    kept.Add(childState);
                    continue;
                }
                var topName = NamedRemovalKey(state.Motion);
                if (topName != null && names.TryGetValue(topName, out var bakeTop))
                {
                    if (bakeTop)
                        BakeMotionUsingDefaults(state.Motion, controller, bakeRoot, bakedActiveStates);
                    removedStates.Add(state);
                    changed = true;
                    continue;
                }

                var newMotion = RemoveNamedChildrenInMotion(
                    state.Motion,
                    names,
                    controller,
                    bakeRoot,
                    bakedActiveStates,
                    ref changed
                );
                if (!ReferenceEquals(newMotion, state.Motion))
                    state.Motion = newMotion;
                kept.Add(childState);
            }
            if (changed)
                sm.States = kept.ToImmutableList();

            foreach (var child in sm.StateMachines)
                changed |= RemoveNamedStatesAndChildren(
                    child.StateMachine,
                    names,
                    controller,
                    bakeRoot,
                    bakedActiveStates,
                    removedStates
                );

            return changed;
        }

        /// <summary>名前指定の削除 ([ReFrameDeleteRelatedBlendTree]) で照合に使う名前を返す。</summary>
        static string NamedRemovalKey(VirtualMotion motion) =>
            motion switch
            {
                VirtualBlendTree bt => bt.Name,
                VirtualClip clip => clip.Name,
                _ => null,
            };

        static VirtualMotion RemoveNamedChildrenInMotion(
            VirtualMotion motion,
            Dictionary<string, bool> names,
            VirtualAnimatorController controller,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates,
            ref bool changed
        )
        {
            if (motion is not VirtualBlendTree bt)
                return motion;

            var children = bt.Children;
            var list = new List<VirtualBlendTree.VirtualChildMotion>(children.Count);
            var localChanged = false;
            foreach (var child in children)
            {
                var c = child;
                var childName = NamedRemovalKey(c.Motion);
                if (childName != null && names.TryGetValue(childName, out var bake))
                {
                    if (bake)
                        BakeMotionUsingDefaults(c.Motion, controller, bakeRoot, bakedActiveStates);
                    localChanged = true;
                    continue;
                }
                var newMotion = RemoveNamedChildrenInMotion(
                    c.Motion,
                    names,
                    controller,
                    bakeRoot,
                    bakedActiveStates,
                    ref changed
                );
                if (!ReferenceEquals(newMotion, c.Motion))
                {
                    c.Motion = newMotion;
                    localChanged = true;
                }
                list.Add(c);
            }
            if (localChanged)
            {
                bt.Children = list.ToImmutableList();
                changed = true;
            }
            return bt;
        }

        /// <summary>[ReFrameDeleteRelatedBlendTree(Bake = true)] 用。</summary>
        static void BakeMotionUsingDefaults(
            VirtualMotion motion,
            VirtualAnimatorController controller,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates
        )
        {
            if (motion == null || bakeRoot == null)
                return;

            if (motion is VirtualClip clip)
            {
                BakeClip(clip, bakeRoot, bakedActiveStates);
                return;
            }

            if (motion is not VirtualBlendTree bt)
                return;

            if (bt.BlendType != BlendTreeType.Simple1D)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] RemoveNamedBlendTrees: '{bt.Name}' is not a Simple1D tree; "
                        + "cannot resolve a default value to bake. Removed without baking."
                );
                return;
            }
            if (!controller.Parameters.TryGetValue(bt.BlendParameter, out var paramDef))
            {
                Debug.LogWarning(
                    $"[ReFrameCore] RemoveNamedBlendTrees: parameter '{bt.BlendParameter}' for "
                        + $"'{bt.Name}' not found in the controller. Removed without baking."
                );
                return;
            }

            var defaultValue = paramDef.type switch
            {
                AnimatorControllerParameterType.Bool => paramDef.defaultBool ? 1f : 0f,
                AnimatorControllerParameterType.Int => paramDef.defaultInt,
                _ => paramDef.defaultFloat,
            };

            var matched = FindSimple1DChild(bt.Children, defaultValue, paramDef.type);
            if (matched == null)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] RemoveNamedBlendTrees: default value {defaultValue} does not match "
                        + $"any threshold of '{bt.Name}'. Removed without baking."
                );
                return;
            }

            BakeMotionUsingDefaults(matched.Motion, controller, bakeRoot, bakedActiveStates);
        }

        /// <summary>RemoveParameters / RemoveNamedBlendTrees の結果、子を全て失って空になった BlendTree ノード (Children.Count == 0) を、コントローラ全体からまとめて後始末する (ReFrameMenuUtil.PruneEmptySubMenus のアニメーター版)。</summary>
        public static void CollectDriverWrittenParameters(
            VirtualAnimatorController controller,
            HashSet<string> result
        )
        {
            if (controller == null || result == null)
                return;
            foreach (var layer in controller.Layers)
                if (layer?.StateMachine != null)
                    CollectDriverWrittenInStateMachine(layer.StateMachine, result);
        }

        static void CollectDriverWrittenInStateMachine(VirtualStateMachine sm, HashSet<string> result)
        {
            void Scan(IEnumerable<StateMachineBehaviour> behaviours)
            {
                if (behaviours == null)
                    return;
                foreach (var behaviour in behaviours)
                {
                    if (behaviour is not VRCAvatarParameterDriver driver || driver.parameters == null)
                        continue;
                    foreach (var parameter in driver.parameters)
                        if (!string.IsNullOrEmpty(parameter.name))
                            result.Add(parameter.name);
                }
            }

            Scan(sm.Behaviours);
            foreach (var childState in sm.States)
                if (childState.State != null)
                    Scan(childState.State.Behaviours);
            foreach (var child in sm.StateMachines)
                CollectDriverWrittenInStateMachine(child.StateMachine, result);
        }

        /// <summary>指定したパラメーターを条件に使っている遷移を、他の条件が何本残っていても丸ごと削除する。</summary>
        public static int RemoveTransitionsByParameter(
            VirtualAnimatorController controller,
            params string[] names
        )
        {
            if (controller == null || names == null || names.Length == 0)
                return 0;
            var targets = new HashSet<string>(names);
            var removed = 0;
            foreach (var layer in controller.Layers)
                if (layer?.StateMachine != null)
                    removed += RemoveTransitionsInStateMachine(layer.StateMachine, targets);
            return removed;
        }

        /// <summary>指定したパラメーターが「もう誰にも書き換えられない」ものとして、コントローラー上の 既定値に固定されたとみなし、遷移条件を評価し直す。</summary>
        public static int CleanTransitionsByFixedValue(
            VirtualAnimatorController controller,
            IEnumerable<string> parameterNames
        )
        {
            if (controller == null || parameterNames == null)
                return 0;

            var targets = new List<(string Name, float Value, AnimatorControllerParameterType Type)>();
            foreach (var name in parameterNames.Distinct())
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!controller.Parameters.TryGetValue(name, out var def))
                    continue;
                var value =
                    def.type == AnimatorControllerParameterType.Bool
                    || def.type == AnimatorControllerParameterType.Trigger
                        ? (def.defaultBool ? 1f : 0f)
                        : def.type == AnimatorControllerParameterType.Int
                            ? def.defaultInt
                            : def.defaultFloat;
                targets.Add((name, value, def.type));
            }
            if (targets.Count == 0)
                return 0;

            var removed = 0;
            foreach (var layer in controller.Layers)
                if (layer?.StateMachine != null)
                    removed += CleanTransitionsByFixedValueInStateMachine(layer.StateMachine, targets);
            return removed;
        }

        static int CleanTransitionsByFixedValueInStateMachine(
            VirtualStateMachine sm,
            List<(string Name, float Value, AnimatorControllerParameterType Type)> targets
        )
        {
            var removed = 0;

            ImmutableList<T> Clean<T>(ImmutableList<T> transitions)
                where T : VirtualTransitionBase
            {
                if (transitions == null || transitions.Count == 0)
                    return transitions;
                var current = transitions;
                foreach (var t in targets)
                {
                    var changed = false;
                    current = CleanTransitions(current, t.Name, t.Value, t.Type, false, ref changed);
                }
                removed += transitions.Count - current.Count;
                return current;
            }

            foreach (var childState in sm.States)
            {
                var state = childState.State;
                if (state == null)
                    continue;
                var kept = Clean(state.Transitions);
                if (!ReferenceEquals(kept, state.Transitions))
                    state.Transitions = kept;
            }

            var any = Clean(sm.AnyStateTransitions);
            if (!ReferenceEquals(any, sm.AnyStateTransitions))
                sm.AnyStateTransitions = any;

            var entry = Clean(sm.EntryTransitions);
            if (!ReferenceEquals(entry, sm.EntryTransitions))
                sm.EntryTransitions = entry;

            foreach (var child in sm.StateMachines)
                removed += CleanTransitionsByFixedValueInStateMachine(child.StateMachine, targets);

            return removed;
        }

        static int RemoveTransitionsInStateMachine(VirtualStateMachine sm, HashSet<string> targets)
        {
            var removed = 0;

            ImmutableList<T> Filter<T>(ImmutableList<T> transitions)
                where T : VirtualTransitionBase
            {
                if (transitions == null || transitions.Count == 0)
                    return transitions;
                var kept = transitions
                    .Where(t => t == null || !t.Conditions.Any(c => targets.Contains(c.parameter)))
                    .ToImmutableList();
                removed += transitions.Count - kept.Count;
                return kept.Count == transitions.Count ? transitions : kept;
            }

            foreach (var childState in sm.States)
            {
                var state = childState.State;
                if (state == null)
                    continue;
                var kept = Filter(state.Transitions);
                if (!ReferenceEquals(kept, state.Transitions))
                    state.Transitions = kept;
            }

            var any = Filter(sm.AnyStateTransitions);
            if (!ReferenceEquals(any, sm.AnyStateTransitions))
                sm.AnyStateTransitions = any;

            var entry = Filter(sm.EntryTransitions);
            if (!ReferenceEquals(entry, sm.EntryTransitions))
                sm.EntryTransitions = entry;

            foreach (var child in sm.StateMachines)
                removed += RemoveTransitionsInStateMachine(child.StateMachine, targets);

            return removed;
        }

        /// <summary>名前が一致するレイヤーを丸ごと取り除く。</summary>
        public static int RemoveNamedStates(
            VirtualAnimatorController controller,
            IEnumerable<(string LayerName, string StateName)> targets
        )
        {
            if (controller == null || targets == null)
                return 0;

            var byLayer = new Dictionary<string, HashSet<string>>();
            foreach (var (layerName, stateName) in targets)
            {
                if (string.IsNullOrEmpty(layerName) || string.IsNullOrEmpty(stateName))
                    continue;
                if (!byLayer.TryGetValue(layerName, out var set))
                    byLayer[layerName] = set = new HashSet<string>();
                set.Add(stateName);
            }
            if (byLayer.Count == 0)
                return 0;

            var removed = 0;
            var removedStates = new HashSet<VirtualState>();
            foreach (var layer in controller.Layers)
            {
                if (layer?.StateMachine == null || !byLayer.TryGetValue(layer.Name, out var names))
                    continue;
                removed += RemoveNamedStatesInStateMachine(layer.StateMachine, names, removedStates);
            }

            if (removedStates.Count > 0)
                foreach (var layer in controller.Layers)
                    if (layer?.StateMachine != null)
                        StripDanglingStateReferences(layer.StateMachine, removedStates);

            return removed;
        }

        static int RemoveNamedStatesInStateMachine(
            VirtualStateMachine sm,
            HashSet<string> names,
            HashSet<VirtualState> removedStates
        )
        {
            var removed = 0;
            var kept = new List<VirtualStateMachine.VirtualChildState>(sm.States.Count);
            foreach (var childState in sm.States)
            {
                var state = childState.State;
                if (state != null && names.Contains(state.Name))
                {
                    removedStates.Add(state);
                    removed++;
                    continue;
                }
                kept.Add(childState);
            }
            if (removed > 0)
                sm.States = kept.ToImmutableList();

            foreach (var child in sm.StateMachines)
                removed += RemoveNamedStatesInStateMachine(child.StateMachine, names, removedStates);

            return removed;
        }

        public static int RemoveNamedLayers(VirtualAnimatorController controller, params string[] names)
        {
            if (controller == null || names == null || names.Length == 0)
                return 0;

            var targets = new HashSet<string>(names);
            var first = controller.Layers.FirstOrDefault();
            var removed = 0;

            controller.RemoveLayers(layer =>
            {
                if (layer == null || !targets.Contains(layer.Name))
                    return false;
                if (ReferenceEquals(layer, first))
                {
                    Debug.LogWarning(
                        $"[ReFrameCore] Layer '{layer.Name}' is the first layer of the controller; keeping it."
                    );
                    return false;
                }
                removed++;
                return true;
            });

            return removed;
        }

        public static bool PruneEmptyBlendTrees(VirtualAnimatorController controller)
        {
            if (controller == null)
                return false;

            var changed = false;
            var removedStates = new HashSet<VirtualState>();

            foreach (var layer in controller.Layers)
                if (layer.StateMachine != null)
                    changed |= PruneEmptyInStateMachine(layer.StateMachine, removedStates);

            if (removedStates.Count > 0)
                foreach (var layer in controller.Layers)
                    if (layer.StateMachine != null)
                        changed |= StripDanglingStateReferences(layer.StateMachine, removedStates);

            return changed;
        }

        static bool PruneEmptyInStateMachine(VirtualStateMachine sm, HashSet<VirtualState> removedStates)
        {
            var changed = false;
            var kept = new List<VirtualStateMachine.VirtualChildState>(sm.States.Count);
            foreach (var childState in sm.States)
            {
                var state = childState.State;
                if (state == null)
                {
                    kept.Add(childState);
                    continue;
                }

                var newMotion = PruneEmptyChildren(state.Motion, ref changed);
                if (!ReferenceEquals(newMotion, state.Motion))
                    state.Motion = newMotion;

                if (state.Motion is VirtualBlendTree emptyBt && emptyBt.Children.Count == 0)
                {
                    removedStates.Add(state);
                    changed = true;
                    continue;
                }
                kept.Add(childState);
            }
            if (changed)
                sm.States = kept.ToImmutableList();

            foreach (var child in sm.StateMachines)
                changed |= PruneEmptyInStateMachine(child.StateMachine, removedStates);

            return changed;
        }

        static VirtualMotion PruneEmptyChildren(VirtualMotion motion, ref bool changed)
        {
            if (motion is not VirtualBlendTree bt)
                return motion;

            var children = bt.Children;
            var list = new List<VirtualBlendTree.VirtualChildMotion>(children.Count);
            var localChanged = false;
            foreach (var child in children)
            {
                var c = child;
                var newMotion = PruneEmptyChildren(c.Motion, ref changed);
                if (!ReferenceEquals(newMotion, c.Motion))
                {
                    c.Motion = newMotion;
                    localChanged = true;
                }
                if (c.Motion is VirtualBlendTree childBt && childBt.Children.Count == 0)
                {
                    localChanged = true;
                    continue;
                }
                list.Add(c);
            }
            if (localChanged)
            {
                bt.Children = list.ToImmutableList();
                changed = true;
            }
            return bt;
        }

        /// <summary>子が 1 つしか残らなかった BlendTree を、その子で置き換える (繰り上げ)。</summary>
        public static bool CollapseSingleChildBlendTrees(VirtualAnimatorController controller)
        {
            if (controller == null)
                return false;

            var changed = false;
            foreach (var layer in controller.Layers)
                if (layer.StateMachine != null)
                    changed |= CollapseSingleChildInStateMachine(layer.StateMachine);
            return changed;
        }

        static bool CollapseSingleChildInStateMachine(VirtualStateMachine sm)
        {
            var changed = false;
            foreach (var childState in sm.States)
            {
                var state = childState.State;
                if (state == null)
                    continue;
                var newMotion = CollapseSingleChild(state.Motion, ref changed);
                if (!ReferenceEquals(newMotion, state.Motion))
                    state.Motion = newMotion;
            }

            foreach (var child in sm.StateMachines)
                changed |= CollapseSingleChildInStateMachine(child.StateMachine);

            return changed;
        }

        static VirtualMotion CollapseSingleChild(VirtualMotion motion, ref bool changed)
        {
            if (motion is not VirtualBlendTree bt)
                return motion;

            var list = new List<VirtualBlendTree.VirtualChildMotion>(bt.Children.Count);
            var localChanged = false;
            foreach (var child in bt.Children)
            {
                var collapsed = CollapseSingleChild(child.Motion, ref changed);
                if (!ReferenceEquals(collapsed, child.Motion))
                {
                    child.Motion = collapsed;
                    localChanged = true;
                }
                list.Add(child);
            }
            if (localChanged)
            {
                bt.Children = list.ToImmutableList();
                changed = true;
            }

            if (!CanCollapseSingleChild(bt))
                return bt;

            changed = true;
            return bt.Children[0].Motion;
        }

        static bool CanCollapseSingleChild(VirtualBlendTree bt)
        {
            if (bt.Children.Count != 1)
                return false;
            if (bt.BlendType == BlendTreeType.Direct)
                return false;

            var only = bt.Children[0];
            if (only.Motion == null)
                return false;
            if (!Mathf.Approximately(only.TimeScale, 1f))
                return false;
            if (!Mathf.Approximately(only.CycleOffset, 0f))
                return false;
            return !only.Mirror;
        }

        /// <summary>どこからも辿り着けなくなったステートを取り除く。</summary>
        public static int PruneUnreachableStates(VirtualAnimatorController controller)
        {
            if (controller == null)
                return 0;

            var removed = 0;
            var removedStates = new HashSet<VirtualState>();

            foreach (var layer in controller.Layers)
                if (layer?.StateMachine != null)
                    removed += PruneUnreachableInLayer(layer.StateMachine, removedStates);

            if (removedStates.Count > 0)
                foreach (var layer in controller.Layers)
                    if (layer?.StateMachine != null)
                        StripDanglingStateReferences(layer.StateMachine, removedStates);

            return removed;
        }

        /// <summary>レイヤー 1 枚ぶんの到達解析。</summary>
        static int PruneUnreachableInLayer(
            VirtualStateMachine root,
            HashSet<VirtualState> removedStates
        )
        {
            var machines = new List<VirtualStateMachine>();
            var parentOf = new Dictionary<VirtualStateMachine, VirtualStateMachine>();
            void Collect(VirtualStateMachine sm)
            {
                machines.Add(sm);
                foreach (var child in sm.StateMachines)
                {
                    if (child.StateMachine == null)
                        continue;
                    parentOf[child.StateMachine] = sm;
                    Collect(child.StateMachine);
                }
            }
            Collect(root);

            var ownerOf = new Dictionary<VirtualState, VirtualStateMachine>();
            foreach (var sm in machines)
            foreach (var child in sm.States)
                if (child.State != null)
                    ownerOf[child.State] = sm;

            var reachable = new HashSet<VirtualState>();
            var entered = new HashSet<VirtualStateMachine>();
            var queue = new Queue<VirtualState>();

            void SeedState(VirtualState state)
            {
                if (state != null && reachable.Add(state))
                    queue.Enqueue(state);
            }

            void Follow(VirtualTransitionBase transition, VirtualStateMachine owner)
            {
                if (transition == null)
                    return;
                if (transition.DestinationState != null)
                    SeedState(transition.DestinationState);
                else if (transition.DestinationStateMachine != null)
                    EnterMachine(transition.DestinationStateMachine);
                else if (transition.IsExit)
                    ExitMachine(owner);
            }

            void EnterMachine(VirtualStateMachine sm)
            {
                if (sm == null || !entered.Add(sm))
                    return;
                SeedState(sm.DefaultState);
                foreach (var transition in sm.EntryTransitions)
                    Follow(transition, sm);
            }

            void ExitMachine(VirtualStateMachine sm)
            {
                if (!parentOf.TryGetValue(sm, out var parent))
                {

                    EnterMachineAgain(root);
                    return;
                }
                if (
                    parent.StateMachineTransitions.TryGetValue(sm, out var transitions)
                    && transitions.Count > 0
                )
                    foreach (var transition in transitions)
                        Follow(transition, parent);
                else
                    EnterMachineAgain(parent);
            }

            void EnterMachineAgain(VirtualStateMachine sm) => EnterMachine(sm);

            EnterMachine(root);
            foreach (var sm in machines)
            foreach (var transition in sm.AnyStateTransitions)
                Follow(transition, sm);

            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                var owner = ownerOf.TryGetValue(state, out var o) ? o : root;
                foreach (var transition in state.Transitions)
                    Follow(transition, owner);
            }

            var removed = 0;
            foreach (var sm in machines)
            {
                var kept = sm.States.Where(c => c.State == null || reachable.Contains(c.State))
                    .ToImmutableList();
                if (kept.Count == sm.States.Count)
                    continue;
                foreach (var child in sm.States)
                    if (child.State != null && !reachable.Contains(child.State))
                    {
                        removedStates.Add(child.State);
                        removed++;
                    }
                sm.States = kept;
            }
            return removed;
        }

        /// <summary>コントローラー内のどこからも参照されていないパラメーター定義を取り除く。</summary>
        public static List<string> RemoveUnreferencedParameters(
            VirtualAnimatorController controller,
            ISet<string> keep,
            IEnumerable<string> keepPrefixes = null
        )
        {
            var removed = new List<string>();
            if (controller == null)
                return removed;

            var referenced = new HashSet<string>();
            foreach (var layer in controller.Layers)
                if (layer?.StateMachine != null)
                    CollectReferencedParameters(layer.StateMachine, referenced);

            var prefixes = keepPrefixes?.Where(p => !string.IsNullOrEmpty(p)).ToList() ?? new List<string>();
            foreach (var name in controller.Parameters.Keys.ToList())
            {
                if (referenced.Contains(name) || (keep != null && keep.Contains(name)))
                    continue;
                if (prefixes.Any(p => name.StartsWith(p, System.StringComparison.Ordinal)))
                    continue;
                controller.Parameters = controller.Parameters.Remove(name);
                removed.Add(name);
            }
            return removed;
        }

        static void CollectReferencedParameters(VirtualStateMachine sm, HashSet<string> result)
        {
            void FromTransitions(IEnumerable<VirtualTransitionBase> transitions)
            {
                if (transitions == null)
                    return;
                foreach (var transition in transitions)
                    if (transition?.Conditions != null)
                        foreach (var condition in transition.Conditions)
                            result.Add(condition.parameter);
            }

            void FromMotion(VirtualMotion motion)
            {
                if (motion is not VirtualBlendTree tree)
                    return;
                result.Add(tree.BlendParameter);
                result.Add(tree.BlendParameterY);
                foreach (var child in tree.Children)
                {
                    result.Add(child.DirectBlendParameter);
                    FromMotion(child.Motion);
                }
            }

            void FromBehaviours(IEnumerable<StateMachineBehaviour> behaviours)
            {
                foreach (var behaviour in behaviours)
                    if (behaviour is VRCAvatarParameterDriver driver && driver.parameters != null)
                        foreach (var entry in driver.parameters)
                        {
                            result.Add(entry.name);
                            if (!string.IsNullOrEmpty(entry.source))
                                result.Add(entry.source);
                        }
            }

            foreach (var childState in sm.States)
            {
                var state = childState.State;
                if (state == null)
                    continue;
                FromTransitions(state.Transitions);
                FromMotion(state.Motion);
                foreach (var name in new[] { state.SpeedParameter, state.TimeParameter, state.MirrorParameter, state.CycleOffsetParameter })
                    if (!string.IsNullOrEmpty(name))
                        result.Add(name);
                FromBehaviours(state.Behaviours);
            }
            FromTransitions(sm.AnyStateTransitions);
            FromTransitions(sm.EntryTransitions);
            foreach (var pair in sm.StateMachineTransitions)
                FromTransitions(pair.Value);
            FromBehaviours(sm.Behaviours);
            foreach (var child in sm.StateMachines)
                if (child.StateMachine != null)
                    CollectReferencedParameters(child.StateMachine, result);
        }

        /// <summary>removedStates に含まれるステートを行き先にしている遷移 (各ステートの Transitions・ AnyStateTransitions・EntryTransitions・DefaultState) を後始末する。</summary>
        public static bool StripDanglingStateReferences(VirtualStateMachine sm, HashSet<VirtualState> removedStates)
        {
            var changed = false;

            foreach (var childState in sm.States)
            {
                var state = childState.State;
                if (state == null)
                    continue;
                var filtered = state.Transitions.Where(t => t?.DestinationState == null || !removedStates.Contains(t.DestinationState)).ToImmutableList();
                if (filtered.Count != state.Transitions.Count)
                {
                    state.Transitions = filtered;
                    changed = true;
                }
            }

            var filteredAny = sm.AnyStateTransitions.Where(t => t?.DestinationState == null || !removedStates.Contains(t.DestinationState)).ToImmutableList();
            if (filteredAny.Count != sm.AnyStateTransitions.Count)
            {
                sm.AnyStateTransitions = filteredAny;
                changed = true;
            }

            var filteredEntry = sm.EntryTransitions.Where(t => t?.DestinationState == null || !removedStates.Contains(t.DestinationState)).ToImmutableList();
            if (filteredEntry.Count != sm.EntryTransitions.Count)
            {
                sm.EntryTransitions = filteredEntry;
                changed = true;
            }

            if (sm.DefaultState != null && removedStates.Contains(sm.DefaultState))
            {
                sm.DefaultState = sm.States.Count > 0 ? sm.States[0].State : null;
                changed = true;
            }

            foreach (var child in sm.StateMachines)
                changed |= StripDanglingStateReferences(child.StateMachine, removedStates);

            return changed;
        }

        /// <summary>選ばれなかった子も常に再生されるツリーか (Simple1D で全子の閾値が同じ = 常時 ON のコンテナ)。</summary>
        static bool IsPassThroughTree(VirtualBlendTree bt)
        {
            if (bt.BlendType != BlendTreeType.Simple1D || bt.Children.Count == 0)
                return false;
            var first = bt.Children[0].Threshold;
            foreach (var child in bt.Children)
                if (!Mathf.Approximately(child.Threshold, first))
                    return false;
            return true;
        }

        static bool Is2DBlendType(BlendTreeType type) =>
            type
                is BlendTreeType.SimpleDirectional2D
                    or BlendTreeType.FreeformDirectional2D
                    or BlendTreeType.FreeformCartesian2D;

        /// <summary>末端の AnimationClip の値を bakeRoot 上のオブジェクトへ直接反映する (先頭キーの値を使用)。</summary>
        static void BakeClip(
            VirtualClip clip,
            Transform bakeRoot,
            Dictionary<Transform, bool> bakedActiveStates
        )
        {
            if (clip == null || bakeRoot == null)
                return;

            foreach (var binding in clip.GetFloatCurveBindings())
            {
                var curve = clip.GetFloatCurve(binding);
                if (curve == null || curve.keys.Length == 0)
                    continue;
                ApplyFloatBinding(binding, curve.keys[0].value, bakeRoot, bakedActiveStates);
            }

            foreach (var binding in clip.GetObjectCurveBindings())
            {
                var keys = clip.GetObjectCurve(binding);
                if (keys == null || keys.Length == 0)
                    continue;
                ApplyObjectBinding(binding, keys[0].value, bakeRoot);
            }
        }

        static void ApplyFloatBinding(
            EditorCurveBinding binding,
            float value,
            Transform root,
            Dictionary<Transform, bool> bakedActiveStates
        )
        {
            var target = ResolveBindingTarget(binding.path, root);
            if (target == null)
                return;

            if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
            {
                var active = value >= 0.5f;
                ReFrameUtil.SetActive(target.gameObject, active);
                if (bakedActiveStates != null)
                    bakedActiveStates[target] = active;
                return;
            }

            if (binding.propertyName.StartsWith("blendShape."))
            {
                if (target.TryGetComponent<SkinnedMeshRenderer>(out var smr))
                    ReFrameUtil.SetBlendShapeWeight(
                        smr,
                        binding.propertyName["blendShape.".Length..],
                        value
                    );
                return;
            }

            if (binding.type == typeof(Transform))
            {
                switch (binding.propertyName)
                {
                    case "m_LocalPosition.x":
                        ReFrameUtil.SetLocalPositionAxis(target, 0, value);
                        break;
                    case "m_LocalPosition.y":
                        ReFrameUtil.SetLocalPositionAxis(target, 1, value);
                        break;
                    case "m_LocalPosition.z":
                        ReFrameUtil.SetLocalPositionAxis(target, 2, value);
                        break;
                    case "m_LocalScale.x":
                        ReFrameUtil.SetLocalScaleAxis(target, 0, value);
                        break;
                    case "m_LocalScale.y":
                        ReFrameUtil.SetLocalScaleAxis(target, 1, value);
                        break;
                    case "m_LocalScale.z":
                        ReFrameUtil.SetLocalScaleAxis(target, 2, value);
                        break;
                    case "m_LocalRotation.x":
                        ReFrameUtil.SetLocalRotationComponent(target, 0, value);
                        break;
                    case "m_LocalRotation.y":
                        ReFrameUtil.SetLocalRotationComponent(target, 1, value);
                        break;
                    case "m_LocalRotation.z":
                        ReFrameUtil.SetLocalRotationComponent(target, 2, value);
                        break;
                    case "m_LocalRotation.w":
                        ReFrameUtil.SetLocalRotationComponent(target, 3, value);
                        break;
                    case "localEulerAnglesRaw.x":
                        ReFrameUtil.SetLocalEulerAnglesAxis(target, 0, value);
                        break;
                    case "localEulerAnglesRaw.y":
                        ReFrameUtil.SetLocalEulerAnglesAxis(target, 1, value);
                        break;
                    case "localEulerAnglesRaw.z":
                        ReFrameUtil.SetLocalEulerAnglesAxis(target, 2, value);
                        break;
                }
                return;
            }

            if (binding.propertyName == "m_Enabled" && target.TryGetComponent(binding.type, out var component))
            {
                ReFrameUtil.SetEnabled(component, value >= 0.5f);
                return;
            }

            if (
                binding.propertyName.StartsWith(MaterialPropertyPrefix)
                && target.TryGetComponent<Renderer>(out var renderer)
            )
                ApplyMaterialFloatBinding(renderer, binding.propertyName[MaterialPropertyPrefix.Length..], value);
        }

        const string MaterialPropertyPrefix = "material.";

        /// <summary>material.&lt;プロパティ&gt; の float カーブを焼き付ける。</summary>
        static void ApplyMaterialFloatBinding(Renderer renderer, string property, float value)
        {
            if (MaterialWriter == null || string.IsNullOrEmpty(property))
                return;

            var name = property;
            var component = -1;
            var isColor = false;
            var dot = property.LastIndexOf('.');
            if (dot > 0 && dot == property.Length - 2)
            {
                var suffix = property[property.Length - 1];
                component = "xyzw".IndexOf(suffix);
                if (component < 0)
                {
                    component = "rgba".IndexOf(suffix);
                    isColor = component >= 0;
                }
                if (component >= 0)
                    name = property[..dot];
            }

            var count = renderer.sharedMaterials.Length;
            for (var slot = 0; slot < count; slot++)
            {
                var material = MaterialWriter(renderer, slot);
                if (material == null)
                    continue;

                var isTextureST = name.EndsWith("_ST") && component >= 0 && !isColor
                    && material.HasProperty(name[..^3]);
                if (isTextureST)
                {
                    var textureName = name[..^3];
                    var scale = material.GetTextureScale(textureName);
                    var offset = material.GetTextureOffset(textureName);
                    switch (component)
                    {
                        case 0: scale.x = value; break;
                        case 1: scale.y = value; break;
                        case 2: offset.x = value; break;
                        default: offset.y = value; break;
                    }
                    material.SetTextureScale(textureName, scale);
                    material.SetTextureOffset(textureName, offset);
                    continue;
                }

                if (!material.HasProperty(name))
                    continue;
                if (component < 0)
                {
                    material.SetFloat(name, value);
                }
                else if (isColor)
                {
                    var color = material.GetColor(name);
                    color[component] = value;
                    material.SetColor(name, color);
                }
                else
                {
                    var vector = material.GetVector(name);
                    vector[component] = value;
                    material.SetVector(name, vector);
                }
            }
        }

        static void ApplyObjectBinding(EditorCurveBinding binding, Object value, Transform root)
        {
            const string materialPrefix = "m_Materials.Array.data[";
            if (!binding.propertyName.StartsWith(materialPrefix))
                return;
            var end = binding.propertyName.IndexOf(']', materialPrefix.Length);
            if (end < 0 || !int.TryParse(binding.propertyName[materialPrefix.Length..end], out var index))
                return;
            if (value is not Material material)
                return;

            var target = ResolveBindingTarget(binding.path, root);
            if (target == null || !target.TryGetComponent<Renderer>(out var renderer))
                return;
            ReFrameUtil.SetMaterial(renderer, index, material);
        }

        static Transform ResolveBindingTarget(string bindingPath, Transform root) =>
            string.IsNullOrEmpty(bindingPath) ? root : ReFrameUtil.Find(root, bindingPath.Split('/'));

        /// <summary>Simple1D ツリーの子から value に一致するものを選ぶ。</summary>
        static VirtualBlendTree.VirtualChildMotion FindSimple1DChild(
            ImmutableList<VirtualBlendTree.VirtualChildMotion> children,
            float value,
            AnimatorControllerParameterType paramType
        )
        {
            if (children.Count == 0)
                return null;

            foreach (var child in children)
            {
                if (Matches(child.Threshold, value, paramType))
                    return child;
            }

            var min = children[0];
            var max = children[0];
            foreach (var child in children)
            {
                if (child.Threshold < min.Threshold)
                    min = child;
                if (child.Threshold > max.Threshold)
                    max = child;
            }
            if (value < min.Threshold)
                return min;
            if (value > max.Threshold)
                return max;
            return null;
        }

        static bool Matches(float threshold, float target, AnimatorControllerParameterType paramType) =>
            paramType switch
            {
                AnimatorControllerParameterType.Bool => (threshold != 0f) == (target != 0f),
                AnimatorControllerParameterType.Int => Mathf.RoundToInt(threshold)
                    == Mathf.RoundToInt(target),
                _ => Mathf.Approximately(threshold, target),
            };

        static float NormalizeValue(float value, AnimatorControllerParameterType paramType) =>
            paramType switch
            {
                AnimatorControllerParameterType.Bool => value != 0f ? 1f : 0f,
                AnimatorControllerParameterType.Int => Mathf.RoundToInt(value),
                _ => value,
            };
    }
}
