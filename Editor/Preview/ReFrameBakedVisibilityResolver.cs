using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>ReFrameDeleteComponent の Enabled なエントリーが、ビルド時に固定する GameObject の Active 状態・ Renderer の enabled 状態 (ReFrameAnimatorUtil の BlendTree 解決ロジックと同じ判定) を、 実アセットに対して読み取り専用で解決する共通ロジック。</summary>
    internal static class ReFrameBakedVisibilityResolver
    {
        /// <summary>解決した「固定される表示状態」。</summary>
        internal class ForcedStates
        {
            public readonly Dictionary<Transform, bool> GameObjectActive = new();
            public readonly Dictionary<Renderer, bool> RendererEnabled = new();
            public readonly Dictionary<Transform, ForcedTransform> Transforms = new();
            public readonly Dictionary<SkinnedMeshRenderer, Dictionary<string, float>> BlendShapeWeights = new();
            public readonly Dictionary<Renderer, Dictionary<int, Material>> MaterialOverrides = new();

            public bool IsEmpty =>
                GameObjectActive.Count == 0
                && RendererEnabled.Count == 0
                && Transforms.Count == 0
                && BlendShapeWeights.Count == 0
                && MaterialOverrides.Count == 0;
        }

        /// <summary>1 つの Transform に対して集まった、軸/成分ごとの固定値。</summary>
        internal class ForcedTransform
        {
            public float? PosX, PosY, PosZ;
            public float? ScaleX, ScaleY, ScaleZ;
            public float? RotX, RotY, RotZ, RotW;
            public float? EulerX, EulerY, EulerZ;

            public bool HasPosition => PosX.HasValue || PosY.HasValue || PosZ.HasValue;
            public bool HasScale => ScaleX.HasValue || ScaleY.HasValue || ScaleZ.HasValue;
            public bool HasQuaternionRotation =>
                RotX.HasValue || RotY.HasValue || RotZ.HasValue || RotW.HasValue;
            public bool HasEulerRotation => EulerX.HasValue || EulerY.HasValue || EulerZ.HasValue;
        }

        /// <summary>ForcedTransform (軸ごとの部分集合) と対象の現在の実際の Transform から、最終的に固定される localPosition / localRotation / localScale を解決する (ReFrameAnimatorUtil のベイクと同じく、 カーブが無い軸は現在の実際の値をそのまま使う)。</summary>
        internal static (Vector3? Position, Quaternion? Rotation, Vector3? Scale) ResolveFinalLocalTransform(
            ForcedTransform forced,
            Transform original
        )
        {
            Vector3? position = null;
            if (forced.HasPosition)
            {
                var current = original.localPosition;
                position = new Vector3(
                    forced.PosX ?? current.x,
                    forced.PosY ?? current.y,
                    forced.PosZ ?? current.z
                );
            }

            Quaternion? rotation = null;
            if (forced.HasQuaternionRotation)
            {
                var current = original.localRotation;
                rotation = new Quaternion(
                    forced.RotX ?? current.x,
                    forced.RotY ?? current.y,
                    forced.RotZ ?? current.z,
                    forced.RotW ?? current.w
                );
            }
            else if (forced.HasEulerRotation)
            {
                var current = original.localEulerAngles;
                rotation = Quaternion.Euler(
                    forced.EulerX ?? current.x,
                    forced.EulerY ?? current.y,
                    forced.EulerZ ?? current.z
                );
            }

            Vector3? scale = null;
            if (forced.HasScale)
            {
                var current = original.localScale;
                scale = new Vector3(
                    forced.ScaleX ?? current.x,
                    forced.ScaleY ?? current.y,
                    forced.ScaleZ ?? current.z
                );
            }

            return (position, rotation, scale);
        }

        /// <summary>アバターの FX コントローラー (baseAnimationLayers の FX レイヤー、および layerType == FX で enabled な ModularAvatarMergeAnimator の animator) を、各コントローラの アニメーションカーブのパスが実際に相対的になっている「パスルート」(Transform) と組にして 集める。</summary>
        internal static List<(AnimatorController Controller, Transform PathRoot)> CollectFxControllers(
            VRCAvatarDescriptor descriptor
        )
        {
            var result = new List<(AnimatorController, Transform)>();

            if (descriptor.baseAnimationLayers != null)
            {
                foreach (var layer in descriptor.baseAnimationLayers)
                {
                    if (
                        layer.type == VRCAvatarDescriptor.AnimLayerType.FX
                        && layer.animatorController is AnimatorController fx
                    )
                        result.Add((fx, descriptor.transform));
                }
            }

            foreach (var merge in descriptor.GetComponentsInChildren<ModularAvatarMergeAnimator>(true))
            {
                if (
                    merge.enabled
                    && merge.layerType == VRCAvatarDescriptor.AnimLayerType.FX
                    && merge.animator is AnimatorController controller
                )
                    result.Add((controller, ResolveMergeAnimatorPathRoot(merge, descriptor.transform)));
            }

            return result;
        }

        /// <summary>アバターが持つ全てのコントローラー (FX に限らず baseAnimationLayers / specialAnimationLayers の全レイヤーと、layerType を問わない MA Merge Animator) を パスルートと組にして集める。</summary>
        internal static List<(AnimatorController Controller, Transform PathRoot)> CollectAllControllers(
            VRCAvatarDescriptor descriptor
        )
        {
            var result = new List<(AnimatorController, Transform)>();

            void AddLayers(VRCAvatarDescriptor.CustomAnimLayer[] layers)
            {
                if (layers == null)
                    return;
                foreach (var layer in layers)
                    if (layer.animatorController is AnimatorController controller)
                        result.Add((controller, descriptor.transform));
            }

            AddLayers(descriptor.baseAnimationLayers);
            AddLayers(descriptor.specialAnimationLayers);

            foreach (var merge in descriptor.GetComponentsInChildren<ModularAvatarMergeAnimator>(true))
            {
                if (merge.enabled && merge.animator is AnimatorController controller)
                    result.Add((controller, ResolveMergeAnimatorPathRoot(merge, descriptor.transform)));
            }

            return result;
        }

        /// <summary>ReFrameSweepPass (Optimizing) が実体ごと破棄する分を、Hierarchy プレビュー用に 近似する。</summary>
        internal static void ResolveSweptObjects(VRCAvatarDescriptor descriptor, ForcedStates result)
        {
            if (descriptor == null)
                return;
            var avatarRoot = descriptor.transform;

            var activatable = new HashSet<Transform>();
            var animated = new HashSet<Transform>();
            foreach (var (controller, pathRoot) in CollectAllControllers(descriptor))
            {
                if (controller == null || pathRoot == null)
                    continue;
                foreach (var clip in CollectClips(controller))
                {
                    var floats = new Dictionary<EditorCurveBinding, float>();
                    var objects = new Dictionary<EditorCurveBinding, Object>();
                    ExtractClipBindingsCached(clip, floats, objects);
                    foreach (var kv in floats)
                    {
                        var target = pathRoot.Find(kv.Key.path);
                        if (target == null)
                            continue;
                        animated.Add(target);
                        if (kv.Key.type != typeof(GameObject) || kv.Key.propertyName != "m_IsActive")
                            continue;
                        if (kv.Value < 0.5f)
                            continue;
                        activatable.Add(target);
                    }
                }
            }

            var skeleton = CollectSkeleton(avatarRoot);

            foreach (var target in avatarRoot.GetComponentsInChildren<Transform>(true))
            {
                if (target == avatarRoot || skeleton.Contains(target))
                    continue;
                if (result.GameObjectActive.TryGetValue(target, out var forced))
                {

                    if (forced)
                        continue;
                }
                else if (target.gameObject.activeSelf)
                {
                    continue;
                }
                if (activatable.Contains(target))
                    continue;
                result.GameObjectActive[target] = false;
            }

            CollapseEmptyContainers(avatarRoot, result, skeleton, animated);
        }

        /// <summary>骨格 (スキンメッシュが参照するボーン、その先祖、そしてその配下すべて) を集める。</summary>
        static HashSet<Transform> CollectSkeleton(Transform avatarRoot)
        {
            var skeleton = new HashSet<Transform>();

            void AddChain(Transform bone)
            {
                if (bone == null)
                    return;
                var cursor = bone;
                while (cursor != null && cursor != avatarRoot)
                {
                    if (!skeleton.Add(cursor))
                        break;

                    if (cursor.GetComponent<ModularAvatarMergeArmature>() != null)
                        break;
                    cursor = cursor.parent;
                }
                foreach (var descendant in bone.GetComponentsInChildren<Transform>(true))
                    skeleton.Add(descendant);
            }

            foreach (var renderer in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                AddChain(renderer.rootBone);
                if (renderer.bones == null)
                    continue;
                foreach (var bone in renderer.bones)
                    AddChain(bone);
            }

            return skeleton;
        }

        /// <summary>子が全部消え、自分自身には Transform 以外のコンポーネントが無いオブジェクトを畳む。</summary>
        static void CollapseEmptyContainers(
            Transform avatarRoot,
            ForcedStates result,
            HashSet<Transform> skeleton,
            HashSet<Transform> animated
        )
        {
            var all = new List<Transform>(avatarRoot.GetComponentsInChildren<Transform>(true));
            all.Remove(avatarRoot);
            all.Sort((a, b) => Depth(b).CompareTo(Depth(a)));

            foreach (var target in all)
            {
                if (result.GameObjectActive.TryGetValue(target, out var already) && !already)
                    continue;
                if (skeleton.Contains(target))
                    continue;

                if (animated.Contains(target))
                    continue;

                var hasOwnContent = false;
                foreach (var component in target.GetComponents<Component>())
                {
                    if (component is Transform)
                        continue;

                    if (component is VRC.SDKBase.IEditorOnly)
                        continue;
                    hasOwnContent = true;
                    break;
                }
                if (hasOwnContent)
                    continue;

                var allChildrenGone = true;
                for (var i = 0; i < target.childCount; i++)
                {
                    var child = target.GetChild(i);
                    if (result.GameObjectActive.TryGetValue(child, out var childGone) && !childGone)
                        continue;

                    if (child.GetComponent<ModularAvatarMergeArmature>() != null)
                        continue;

                    if (IsRendererOnlyAndDisabled(child, result))
                        continue;
                    allChildrenGone = false;
                    break;
                }
                if (allChildrenGone)
                    result.GameObjectActive[target] = false;
            }
        }

        /// <summary>「メッシュを描くだけの GameObject で、その描画が止められている」か。</summary>
        static bool IsRendererOnlyAndDisabled(Transform target, ForcedStates result)
        {
            if (target.childCount > 0)
                return false;

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
                return false;
            if (!result.RendererEnabled.TryGetValue(renderer, out var enabled) || enabled)
                return false;

            foreach (var component in target.GetComponents<Component>())
            {
                if (component is Transform || component is Renderer || component is MeshFilter)
                    continue;
                if (component is VRC.SDKBase.IEditorOnly)
                    continue;
                return false;
            }
            return true;
        }

        static int Depth(Transform target)
        {
            var depth = 0;
            var cursor = target;
            while (cursor.parent != null)
            {
                depth++;
                cursor = cursor.parent;
            }
            return depth;
        }

        /// <summary>コントローラーが参照する AnimationClip をすべて列挙する (BlendTree を含む)。</summary>
        static IEnumerable<AnimationClip> CollectClips(AnimatorController controller)
        {
            var seen = new HashSet<Motion>();
            var found = new List<AnimationClip>();

            void WalkMotion(Motion motion)
            {
                if (motion == null || !seen.Add(motion))
                    return;
                if (motion is AnimationClip clip)
                {
                    found.Add(clip);
                    return;
                }
                if (motion is BlendTree tree)
                    foreach (var child in tree.children)
                        WalkMotion(child.motion);
            }

            void WalkStateMachineForClips(AnimatorStateMachine sm)
            {
                if (sm == null)
                    return;
                foreach (var childState in sm.states)
                    if (childState.state != null)
                        WalkMotion(childState.state.motion);
                foreach (var child in sm.stateMachines)
                    WalkStateMachineForClips(child.stateMachine);
            }

            foreach (var layer in controller.layers)
                WalkStateMachineForClips(layer.stateMachine);

            return found;
        }

        /// <summary>ModularAvatarMergeAnimator のカーブパスが実際に相対的になっている Transform を判定する。</summary>
        internal static Transform ResolveMergeAnimatorPathRoot(ModularAvatarMergeAnimator merge, Transform avatarRoot)
        {
            if (merge.pathMode == MergeAnimatorPathMode.Absolute)
                return avatarRoot;
            var target = merge.relativePathRoot?.Get(avatarRoot);
            return target != null ? target.transform : merge.transform;
        }

        /// <summary>アバターの VRCExpressionParameters に登録された各パラメーターの defaultValue を集める。</summary>
        internal static Dictionary<string, float> BuildDefaultParameterValues(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<string, float>();
            var parameters = descriptor.expressionParameters?.parameters;
            if (parameters == null)
                return result;
            foreach (var p in parameters)
            {
                if (p == null || string.IsNullOrEmpty(p.name))
                    continue;
                result[p.name] = p.defaultValue;
            }
            return result;
        }

        /// <summary>controller 上で allValues に含まれる全パラメーターを固定したときに確定する表示状態を result へ書き足す。</summary>
        internal static void ResolveActiveStates(
            AnimatorController controller,
            Transform pathRoot,
            ForcedStates result,
            IReadOnlyDictionary<string, float> allValues,
            IReadOnlyDictionary<string, float> treeOverrides = null
        )
        {
            treeOverrides ??= EmptyOverrides;
            foreach (var layer in controller.layers)
                if (layer.stateMachine != null)
                    WalkStateMachine(layer.stateMachine, controller, pathRoot, result, allValues, treeOverrides);
        }

        static readonly Dictionary<string, float> EmptyOverrides = new();

        /// <summary>[ReFrameDeleteObject] で名指し削除される GameObject を result に足す。</summary>
        internal static void ResolveFixedBlendShapes(
            IEnumerable<(string Path, string ShapeName, float Weight)> targets,
            Transform avatarRoot,
            ForcedStates result
        )
        {
            if (targets == null || avatarRoot == null)
                return;
            foreach (var target in targets)
            {
                var transform = avatarRoot.Find(target.Path);
                var smr = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
                if (smr == null)
                    continue;
                if (!result.BlendShapeWeights.TryGetValue(smr, out var weights))
                    result.BlendShapeWeights[smr] = weights = new Dictionary<string, float>();
                weights[target.ShapeName] = target.Weight;
            }
        }

        internal static void ResolveDeletedObjects(
            IEnumerable<string> paths,
            Transform avatarRoot,
            ForcedStates result
        )
        {
            if (paths == null || avatarRoot == null)
                return;

            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;
                var target = avatarRoot.Find(path);
                if (target == null)
                    continue;
                result.GameObjectActive[target] = false;
            }
        }

        static void WalkStateMachine(
            AnimatorStateMachine sm,
            AnimatorController controller,
            Transform avatarRoot,
            ForcedStates result,
            IReadOnlyDictionary<string, float> allValues,
            IReadOnlyDictionary<string, float> treeOverrides
        )
        {
            foreach (var childState in sm.states)
                ResolveMotion(childState.state.motion, controller, avatarRoot, result, allValues, treeOverrides);
            foreach (var child in sm.stateMachines)
                WalkStateMachine(child.stateMachine, controller, avatarRoot, result, allValues, treeOverrides);
        }

        /// <summary>通っている各 BlendTree ノード自身の blendParameter が allValues に含まれていれば、 その値で枝を絞り込む。</summary>
        static void ResolveMotion(
            Motion motion,
            AnimatorController controller,
            Transform avatarRoot,
            ForcedStates result,
            IReadOnlyDictionary<string, float> allValues,
            IReadOnlyDictionary<string, float> treeOverrides
        )
        {
            if (motion is not BlendTree bt)
                return;

            if (Is2DBlendType(bt.blendType))
            {

                if (
                    allValues.TryGetValue(bt.blendParameter, out var xValue)
                    && !string.IsNullOrEmpty(bt.blendParameterY)
                    && allValues.TryGetValue(bt.blendParameterY, out var yValue)
                )
                {
                    const float epsilon = 1e-4f;
                    foreach (var child in bt.children)
                    {
                        if (
                            Mathf.Abs(child.position.x - xValue) > epsilon
                            || Mathf.Abs(child.position.y - yValue) > epsilon
                        )
                            continue;
                        if (child.motion is AnimationClip clip)
                            CollectClipActiveStates(clip, avatarRoot, result);
                        else
                            ResolveMotion(child.motion, controller, avatarRoot, result, allValues, treeOverrides);
                        return;
                    }

                    var blendedFloats = ReFrameBlendUtil.Pool.RentFloats();
                    var blendedObjects = ReFrameBlendUtil.Pool.RentObjects();
                    if (
                        Try2DGbiBlend(
                            bt.children,
                            xValue,
                            yValue,
                            controller,
                            allValues,
                            blendedFloats,
                            blendedObjects
                        )
                    )
                        ApplyBindingsToForcedStates(blendedFloats, blendedObjects, avatarRoot, result);
                    ReFrameBlendUtil.Pool.ReturnFloats(blendedFloats);
                    ReFrameBlendUtil.Pool.ReturnObjects(blendedObjects);
                }
                return;
            }

            if (bt.blendType == BlendTreeType.Simple1D)
            {

                var hasValue = treeOverrides.TryGetValue(bt.name + "\n" + bt.blendParameter, out var raw);
                if (!hasValue)
                    hasValue = allValues.TryGetValue(bt.blendParameter, out raw);
                if (hasValue)
                {
                    var def = FindParameter(controller, bt.blendParameter);
                    if (def != null)
                    {
                        var matchValue = NormalizeValue(raw, def.type);
                        if (TryFindSimple1DChild(bt.children, matchValue, def.type, out var matchedChild))
                        {
                            if (matchedChild.motion is AnimationClip clip)
                                CollectClipActiveStates(clip, avatarRoot, result);
                            else

                                ResolveMotion(matchedChild.motion, controller, avatarRoot, result, allValues, treeOverrides);
                            return;
                        }

                        if (
                            def.type == AnimatorControllerParameterType.Float
                            && TryFindSimple1DNeighbors(bt.children, matchValue, out var lo, out var hi, out var t)
                        )
                        {
                            var loFloats = ReFrameBlendUtil.Pool.RentFloats();
                            var loObjects = ReFrameBlendUtil.Pool.RentObjects();
                            var hiFloats = ReFrameBlendUtil.Pool.RentFloats();
                            var hiObjects = ReFrameBlendUtil.Pool.RentObjects();
                            var loOk = TryEvaluateMotionRawState(
                                lo.motion,
                                controller,
                                allValues,
                                loFloats,
                                loObjects
                            );
                            var hiOk = TryEvaluateMotionRawState(
                                hi.motion,
                                controller,
                                allValues,
                                hiFloats,
                                hiObjects
                            );
                            if (loOk && hiOk)
                            {
                                var mergedFloats = ReFrameBlendUtil.Pool.RentFloats();
                                var mergedObjects = ReFrameBlendUtil.Pool.RentObjects();
                                ReFrameBlendUtil.MergeInterpolated(loFloats, hiFloats, t, mergedFloats);
                                ReFrameBlendUtil.MergeDominant(loObjects, hiObjects, t, mergedObjects);
                                ApplyBindingsToForcedStates(mergedFloats, mergedObjects, avatarRoot, result);
                                ReFrameBlendUtil.Pool.ReturnFloats(mergedFloats);
                                ReFrameBlendUtil.Pool.ReturnObjects(mergedObjects);
                            }
                            ReFrameBlendUtil.Pool.ReturnFloats(loFloats);
                            ReFrameBlendUtil.Pool.ReturnObjects(loObjects);
                            ReFrameBlendUtil.Pool.ReturnFloats(hiFloats);
                            ReFrameBlendUtil.Pool.ReturnObjects(hiObjects);
                        }
                        return;
                    }
                }

            }

            foreach (var child in bt.children)
                ResolveMotion(child.motion, controller, avatarRoot, result, allValues, treeOverrides);
        }

        static bool Is2DBlendType(BlendTreeType type) =>
            type
                is BlendTreeType.SimpleDirectional2D
                    or BlendTreeType.FreeformDirectional2D
                    or BlendTreeType.FreeformCartesian2D;

        static void CollectClipActiveStates(AnimationClip clip, Transform avatarRoot, ForcedStates result)
        {
            var floats = ReFrameBlendUtil.Pool.RentFloats();
            var objects = ReFrameBlendUtil.Pool.RentObjects();
            ExtractClipBindingsCached(clip, floats, objects);
            ApplyBindingsToForcedStates(floats, objects, avatarRoot, result);
            ReFrameBlendUtil.Pool.ReturnFloats(floats);
            ReFrameBlendUtil.Pool.ReturnObjects(objects);
        }

        /// <summary>1 回の解決パス (BeginResolveScope〜EndResolveScope の間) の中でだけ有効な、 AnimationClip -> 抽出済みバインディングのキャッシュ。</summary>
        static Dictionary<
            AnimationClip,
            (Dictionary<EditorCurveBinding, float> Floats, Dictionary<EditorCurveBinding, Object> Objects)
        > _clipCache;

        /// <summary>ResolveForcedActiveStates/CollectDesiredHidden のように、1 回の解決パスの中で ResolveActiveStates を複数のパラメーターについて連続して呼ぶ呼び出し元は、ループの前後で これと EndResolveScope を呼んでクリップキャッシュを有効にすること (try/finally 推奨)。</summary>
        public static void BeginResolveScope()
        {
            _clipCache = new Dictionary<
                AnimationClip,
                (Dictionary<EditorCurveBinding, float> Floats, Dictionary<EditorCurveBinding, Object> Objects)
            >();
            _parameterCache = new Dictionary<AnimatorController, Dictionary<string, AnimatorControllerParameter>>();
        }

        /// <summary>AnimatorController.parameters は読むたびにネイティブ側から配列を作り直すので、
        /// BlendTree ごとに引くと重い。1 回の解決パスの中では名前引きの表にして使い回す。</summary>
        static Dictionary<AnimatorController, Dictionary<string, AnimatorControllerParameter>> _parameterCache;

        /// <summary>コントローラーのパラメーターを名前で引く。無ければ null。</summary>
        static AnimatorControllerParameter FindParameter(AnimatorController controller, string name)
        {
            if (controller == null || string.IsNullOrEmpty(name))
                return null;

            if (_parameterCache == null)
                return controller.parameters.FirstOrDefault(p => p.name == name);

            if (!_parameterCache.TryGetValue(controller, out var table))
            {
                table = new Dictionary<string, AnimatorControllerParameter>();
                foreach (var parameter in controller.parameters)
                    if (parameter != null && !string.IsNullOrEmpty(parameter.name))
                        table[parameter.name] = parameter;
                _parameterCache[controller] = table;
            }

            return table.TryGetValue(name, out var found) ? found : null;
        }

        public static void EndResolveScope()
        {
            _clipCache = null;
            _parameterCache = null;
        }

        static void ExtractClipBindingsCached(
            AnimationClip clip,
            Dictionary<EditorCurveBinding, float> outFloats,
            Dictionary<EditorCurveBinding, Object> outObjects
        )
        {
            if (_clipCache != null)
            {
                if (!_clipCache.TryGetValue(clip, out var cached))
                {
                    cached = (
                        new Dictionary<EditorCurveBinding, float>(),
                        new Dictionary<EditorCurveBinding, Object>()
                    );
                    ExtractClipBindings(clip, cached.Floats, cached.Objects);
                    _clipCache[clip] = cached;
                }
                foreach (var kv in cached.Floats)
                    outFloats[kv.Key] = kv.Value;
                foreach (var kv in cached.Objects)
                    outObjects[kv.Key] = kv.Value;
                return;
            }
            ExtractClipBindings(clip, outFloats, outObjects);
        }

        /// <summary>AnimationClip の先頭キーフレームの値だけを、生の EditorCurveBinding 単位で読み取る。</summary>
        static void ExtractClipBindings(
            AnimationClip clip,
            Dictionary<EditorCurveBinding, float> outFloats,
            Dictionary<EditorCurveBinding, Object> outObjects
        )
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.keys.Length == 0)
                    continue;
                outFloats[binding] = curve.keys[0].value;
            }

            const string materialPrefix = "m_Materials.Array.data[";
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (!binding.propertyName.StartsWith(materialPrefix))
                    continue;
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (keys == null || keys.Length == 0 || keys[0].value is not Material)
                    continue;
                outObjects[binding] = keys[0].value;
            }
        }

        /// <summary>(無段階ブレンドで合成済みも含む) 生の EditorCurveBinding 単位の値を ForcedStates へ書き込む。</summary>
        static void ApplyBindingsToForcedStates(
            Dictionary<EditorCurveBinding, float> floats,
            Dictionary<EditorCurveBinding, Object> objects,
            Transform avatarRoot,
            ForcedStates result
        )
        {
            foreach (var kv in floats)
            {
                var binding = kv.Key;
                var value = kv.Value;

                if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
                {
                    var target = ResolveBindingTarget(binding.path, avatarRoot);
                    if (target != null)
                        result.GameObjectActive[target] = value >= 0.5f;
                    continue;
                }

                if (binding.propertyName == "m_Enabled" && typeof(Renderer).IsAssignableFrom(binding.type))
                {
                    var target = ResolveBindingTarget(binding.path, avatarRoot);
                    if (target != null && target.GetComponent(binding.type) is Renderer renderer)
                        result.RendererEnabled[renderer] = value >= 0.5f;
                    continue;
                }

                if (binding.type == typeof(Transform))
                {
                    var target = ResolveBindingTarget(binding.path, avatarRoot);
                    if (target == null)
                        continue;
                    if (!result.Transforms.TryGetValue(target, out var forced))
                        result.Transforms[target] = forced = new ForcedTransform();
                    switch (binding.propertyName)
                    {
                        case "m_LocalPosition.x":
                            forced.PosX = value;
                            break;
                        case "m_LocalPosition.y":
                            forced.PosY = value;
                            break;
                        case "m_LocalPosition.z":
                            forced.PosZ = value;
                            break;
                        case "m_LocalScale.x":
                            forced.ScaleX = value;
                            break;
                        case "m_LocalScale.y":
                            forced.ScaleY = value;
                            break;
                        case "m_LocalScale.z":
                            forced.ScaleZ = value;
                            break;
                        case "m_LocalRotation.x":
                            forced.RotX = value;
                            break;
                        case "m_LocalRotation.y":
                            forced.RotY = value;
                            break;
                        case "m_LocalRotation.z":
                            forced.RotZ = value;
                            break;
                        case "m_LocalRotation.w":
                            forced.RotW = value;
                            break;
                        case "localEulerAnglesRaw.x":
                            forced.EulerX = value;
                            break;
                        case "localEulerAnglesRaw.y":
                            forced.EulerY = value;
                            break;
                        case "localEulerAnglesRaw.z":
                            forced.EulerZ = value;
                            break;
                    }
                    continue;
                }

                if (
                    binding.propertyName.StartsWith("blendShape.")
                    && typeof(SkinnedMeshRenderer).IsAssignableFrom(binding.type)
                )
                {
                    var target = ResolveBindingTarget(binding.path, avatarRoot);
                    if (target == null || target.GetComponent<SkinnedMeshRenderer>() is not { } smr)
                        continue;
                    if (!result.BlendShapeWeights.TryGetValue(smr, out var weights))
                        result.BlendShapeWeights[smr] = weights = new Dictionary<string, float>();
                    weights[binding.propertyName["blendShape.".Length..]] = value;
                }
            }

            const string materialPrefix = "m_Materials.Array.data[";
            foreach (var kv in objects)
            {
                var binding = kv.Key;
                if (!binding.propertyName.StartsWith(materialPrefix) || kv.Value is not Material material)
                    continue;
                var end = binding.propertyName.IndexOf(']', materialPrefix.Length);
                if (end < 0 || !int.TryParse(binding.propertyName[materialPrefix.Length..end], out var index))
                    continue;

                var target = ResolveBindingTarget(binding.path, avatarRoot);
                if (target == null || target.GetComponent<Renderer>() is not { } renderer)
                    continue;
                if (!result.MaterialOverrides.TryGetValue(renderer, out var materials))
                    result.MaterialOverrides[renderer] = materials = new Dictionary<int, Material>();
                materials[index] = material;
            }
        }

        static Transform ResolveBindingTarget(string bindingPath, Transform avatarRoot) =>
            string.IsNullOrEmpty(bindingPath) ? avatarRoot : ReFrameUtil.Find(avatarRoot, bindingPath.Split('/'));

        /// <summary>Simple1D ツリーの子から value に一致するものを選ぶ。</summary>
        static bool TryFindSimple1DChild(
            ChildMotion[] children,
            float value,
            AnimatorControllerParameterType paramType,
            out ChildMotion matched
        )
        {
            matched = default;
            if (children.Length == 0)
                return false;

            foreach (var child in children)
            {
                if (Matches(child.threshold, value, paramType))
                {
                    matched = child;
                    return true;
                }
            }

            var min = children[0];
            var max = children[0];
            foreach (var child in children)
            {
                if (child.threshold < min.threshold)
                    min = child;
                if (child.threshold > max.threshold)
                    max = child;
            }
            if (value < min.threshold)
            {
                matched = min;
                return true;
            }
            if (value > max.threshold)
            {
                matched = max;
                return true;
            }
            return false;
        }

        /// <summary>value が children のどの閾値とも一致せず (クランプ対象の範囲外でもなく)、2 つの閾値の間に 挟まれている場合に、その両側の子 (lo/hi) と線形補間の重み t (0=lo, 1=hi) を返す (ReFrameAnimatorUtil.TryFindSimple1DNeighbors と同じロジック)。</summary>
        static bool TryFindSimple1DNeighbors(
            ChildMotion[] children,
            float value,
            out ChildMotion lo,
            out ChildMotion hi,
            out float t
        )
        {
            lo = default;
            hi = default;
            t = 0f;
            var hasLo = false;
            var hasHi = false;
            foreach (var c in children)
            {
                if (c.threshold <= value && (!hasLo || c.threshold > lo.threshold))
                {
                    lo = c;
                    hasLo = true;
                }
                if (c.threshold > value && (!hasHi || c.threshold < hi.threshold))
                {
                    hi = c;
                    hasHi = true;
                }
            }
            if (!hasLo || !hasHi || Mathf.Approximately(lo.threshold, hi.threshold))
                return false;
            t = (value - lo.threshold) / (hi.threshold - lo.threshold);
            return true;
        }

        /// <summary>Motion を末端まで解決し、その状態を表す float/Object カーブの値を outFloats/outObjects に書き込む (実際には反映せず値の収集のみ。</summary>
        static bool TryEvaluateMotionRawState(
            Motion motion,
            AnimatorController controller,
            IReadOnlyDictionary<string, float> allValues,
            Dictionary<EditorCurveBinding, float> outFloats,
            Dictionary<EditorCurveBinding, Object> outObjects
        )
        {
            if (motion is AnimationClip clip)
            {
                ExtractClipBindingsCached(clip, outFloats, outObjects);
                return true;
            }

            if (motion is not BlendTree nested)
                return false;

            if (Is2DBlendType(nested.blendType))
            {
                if (
                    allValues.TryGetValue(nested.blendParameter, out var xValue)
                    && !string.IsNullOrEmpty(nested.blendParameterY)
                    && allValues.TryGetValue(nested.blendParameterY, out var yValue)
                )
                {
                    const float epsilon = 1e-4f;
                    foreach (var c in nested.children)
                    {
                        if (
                            Mathf.Abs(c.position.x - xValue) > epsilon
                            || Mathf.Abs(c.position.y - yValue) > epsilon
                        )
                            continue;
                        return TryEvaluateMotionRawState(c.motion, controller, allValues, outFloats, outObjects);
                    }
                    return Try2DGbiBlend(
                        nested.children,
                        xValue,
                        yValue,
                        controller,
                        allValues,
                        outFloats,
                        outObjects
                    );
                }
                return false;
            }

            if (nested.blendType == BlendTreeType.Simple1D)
            {
                if (!allValues.TryGetValue(nested.blendParameter, out var raw))
                    return false;
                var def = FindParameter(controller, nested.blendParameter);
                if (def == null)
                    return false;
                var matchValue = NormalizeValue(raw, def.type);

                if (TryFindSimple1DChild(nested.children, matchValue, def.type, out var exact))
                    return TryEvaluateMotionRawState(exact.motion, controller, allValues, outFloats, outObjects);

                if (
                    def.type == AnimatorControllerParameterType.Float
                    && TryFindSimple1DNeighbors(nested.children, matchValue, out var lo, out var hi, out var t)
                )
                {
                    var loFloats = ReFrameBlendUtil.Pool.RentFloats();
                    var loObjects = ReFrameBlendUtil.Pool.RentObjects();
                    var hiFloats = ReFrameBlendUtil.Pool.RentFloats();
                    var hiObjects = ReFrameBlendUtil.Pool.RentObjects();
                    var ok =
                        TryEvaluateMotionRawState(lo.motion, controller, allValues, loFloats, loObjects)
                        && TryEvaluateMotionRawState(hi.motion, controller, allValues, hiFloats, hiObjects);
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

        /// <summary>2D BlendTree の全ての子 (Motion が null のものは除く) を Gradient Band Interpolation で 重み付けし、各子を末端まで再帰的に解決したうえで重み付き合成する。</summary>
        static bool Try2DGbiBlend(
            ChildMotion[] children,
            float x,
            float y,
            AnimatorController controller,
            IReadOnlyDictionary<string, float> allValues,
            Dictionary<EditorCurveBinding, float> outFloats,
            Dictionary<EditorCurveBinding, Object> outObjects
        )
        {
            var validChildren = new List<ChildMotion>();
            foreach (var c in children)
                if (c.motion != null)
                    validChildren.Add(c);
            if (validChildren.Count == 0)
                return false;

            var points = new List<Vector2>(validChildren.Count);
            foreach (var c in validChildren)
                points.Add(c.position);
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
                    !TryEvaluateMotionRawState(
                        validChildren[i].motion,
                        controller,
                        allValues,
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