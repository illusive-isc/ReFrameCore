using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf.preview;
using nadena.dev.ndmf.runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using ForcedStates = jp.illusive_isc.ReFrame.Core.Editor.ReFrameBakedVisibilityResolver.ForcedStates;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>NDMF Preview 用のレンダーフィルター。</summary>
    internal class ReFrameDeletePreview : IRenderFilter
    {
        public bool CanEnableRenderers => true;

        /// <summary>NDMF のプレビュー切り替えメニューに出す項目。重いときに利用者が切れるようにする。</summary>
        static readonly TogglablePreviewNode EnableNode = TogglablePreviewNode.Create(
            () => "ReFrame",
            qualifiedName: "jp.illusive-isc.reframe/DeletePreview",
            true
        );

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes()
        {
            yield return EnableNode;
        }

        public bool IsEnabled(ComputeContext context) => context.Observe(EnableNode.IsEnabled);

        /// <summary>FX を歩いて「何がどう固定されるか」を出す処理は重いので、アバターごとに覚えておく。
        /// 監視している値が変わったときだけ作り直される (Modular Avatar の ReactiveObjectAnalyzer と同じ作り)。</summary>
        static PropCache<GameObject, ForcedStates> _forcedStatesCache;

        static ForcedStates CachedForcedStates(ComputeContext context, GameObject avatarRoot)
        {
            _forcedStatesCache ??= new PropCache<GameObject, ForcedStates>(
                "ReFrameForcedStates",
                (ctx, root) =>
                {
                    if (!ctx.Observe(root, o => o.activeInHierarchy))
                        return new ForcedStates();
                    return root.TryGetComponent<VRCAvatarDescriptor>(out var descriptor)
                        ? ResolveForcedActiveStates(ctx, descriptor)
                        : new ForcedStates();
                }
            );
            return _forcedStatesCache.Get(context, avatarRoot);
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            return context.GetAvatarRoots().SelectMany(av => RootsForAvatar(context, av)).ToImmutableList();
        }

        IEnumerable<RenderGroup> RootsForAvatar(ComputeContext context, GameObject avatarRoot)
        {
            if (!context.ActiveInHierarchy(avatarRoot))
                yield break;
            if (!avatarRoot.TryGetComponent<VRCAvatarDescriptor>(out var descriptor))
                yield break;

            // Quest 変換プレビューの使い回しを、このアバターの分だけ作り直させる
            ReFrameQuestMaterialPreview.ResetScope();

            var forced = CachedForcedStates(context, avatarRoot);
            if (forced.IsEmpty)
                yield break;

            var mergeMap = BuildMergeArmatureMap(context, avatarRoot);
            var renderers = context.GetComponentsInChildren<Renderer>(avatarRoot, true);
            foreach (var renderer in renderers)
            {

                if (renderer is not MeshRenderer and not SkinnedMeshRenderer)
                    continue;

                var currentGameObjectActive = context.ActiveInHierarchy(renderer.gameObject);
                var currentRendererEnabled = context.Observe(renderer, r => r.enabled);
                var currentlyVisible = currentGameObjectActive && currentRendererEnabled;

                var overrideEnabled = true;

                if (forced.RendererEnabled.TryGetValue(renderer, out var forcedRendererEnabled) && !forcedRendererEnabled)
                    overrideEnabled = false;

                if (overrideEnabled)
                {
                    var cursor = renderer.transform;
                    while (cursor != null && !RuntimeUtil.IsAvatarRoot(cursor))
                    {
                        if (forced.GameObjectActive.TryGetValue(cursor, out var forcedActive))
                        {
                            if (!forcedActive)
                            {
                                overrideEnabled = false;
                                break;
                            }
                        }
                        else if (!context.Observe(cursor, t => t.gameObject.activeSelf))
                        {
                            overrideEnabled = false;
                            break;
                        }
                        cursor = cursor.parent;
                    }
                }

                Vector3? localPosition = null;
                Quaternion? localRotation = null;
                Vector3? localScale = null;

                if (renderer is MeshRenderer && forced.Transforms.TryGetValue(renderer.transform, out var forcedTransform))
                {
                    context.Observe(renderer.transform);
                    (localPosition, localRotation, localScale) = ReFrameBakedVisibilityResolver
                        .ResolveFinalLocalTransform(forcedTransform, renderer.transform);
                }

                var blendShapeWeights = ImmutableDictionary<string, float>.Empty;
                if (
                    renderer is SkinnedMeshRenderer smr
                    && forced.BlendShapeWeights.TryGetValue(smr, out var weights)
                )
                {
                    context.Observe(smr, r => r.sharedMesh);
                    blendShapeWeights = weights.ToImmutableDictionary();
                }

                var materialOverrides = ImmutableDictionary<int, Material>.Empty;
                if (forced.MaterialOverrides.TryGetValue(renderer, out var materials))
                    materialOverrides = materials.ToImmutableDictionary();

                materialOverrides = ReFrameQuestMaterialPreview.Apply(
                    context,
                    descriptor,
                    renderer,
                    materialOverrides
                );
                var meshOverride = ReFrameQuestMaterialPreview.CutShellMesh(renderer, materialOverrides);

                var boneOverrides = ImmutableDictionary<Transform, BoneOverride>.Empty;
                if (renderer is SkinnedMeshRenderer boneSmr && boneSmr.bones != null)
                {
                    var builder = ImmutableDictionary.CreateBuilder<Transform, BoneOverride>();
                    foreach (var bone in boneSmr.bones)
                    {
                        for (var cursor = bone; cursor != null && !RuntimeUtil.IsAvatarRoot(cursor); cursor = cursor.parent)
                        {
                            if (builder.ContainsKey(cursor))
                                break;
                            var boneForced = ResolveForcedBone(cursor, forced, mergeMap);
                            if (boneForced == null)
                                continue;
                            context.Observe(cursor);
                            var (bonePos, boneRot, boneScale) = ReFrameBakedVisibilityResolver
                                .ResolveFinalLocalTransform(boneForced, cursor);
                            builder[cursor] = new BoneOverride(bonePos, boneRot, boneScale);
                        }
                    }
                    if (builder.Count > 0)
                        boneOverrides = builder.ToImmutable();
                }

                var data = new PreviewData(
                    overrideEnabled,
                    localPosition,
                    localRotation,
                    localScale,
                    blendShapeWeights,
                    materialOverrides,
                    boneOverrides,
                    meshOverride
                );
                if (
                    overrideEnabled == currentlyVisible
                    && localPosition == null
                    && localRotation == null
                    && localScale == null
                    && blendShapeWeights.Count == 0
                    && materialOverrides.Count == 0
                    && boneOverrides.Count == 0
                    && meshOverride == null
                )
                    continue;

                yield return RenderGroup.For(renderer).WithData(data, (a, b) => a.Equals(b));
            }
        }

        /// <summary>MA Merge Armature の統合元ボーン → 統合先ボーンの対応表。</summary>
        static Dictionary<Transform, Transform> BuildMergeArmatureMap(ComputeContext context, GameObject avatarRoot)
        {
            var map = new Dictionary<Transform, Transform>();
            foreach (var merge in context.GetComponentsInChildren<ModularAvatarMergeArmature>(avatarRoot, true))
            {
                context.Observe(merge, m => (m.mergeTargetObject, m.prefix, m.suffix));
                var pairs = merge.GetBonesMapping();
                if (pairs == null)
                    continue;
                foreach (var (baseBone, mergeBone) in pairs)
                    map[mergeBone] = baseBone;
            }
            return map;
        }

        /// <summary>ボーンの固定値を引く。統合元の複製ボーンは統合先へ辿り直して引く。</summary>
        static ReFrameBakedVisibilityResolver.ForcedTransform ResolveForcedBone(
            Transform bone,
            ForcedStates forced,
            Dictionary<Transform, Transform> mergeMap
        )
        {
            for (var cursor = bone; cursor != null; cursor = mergeMap.TryGetValue(cursor, out var next) ? next : null)
            {
                if (forced.Transforms.TryGetValue(cursor, out var boneForced))
                    return boneForced;
            }
            return null;
        }

        /// <summary>アバター配下のすべての ReFrameDeleteComponent の Enabled なエントリを集め、それぞれが FX / layerType == FX の MA Merge Animator 上で解決する表示状態を集約する。</summary>
        static ForcedStates ResolveForcedActiveStates(ComputeContext context, VRCAvatarDescriptor descriptor)
        {
            var result = new ForcedStates();
            var controllers = CollectFxControllers(context, descriptor);
            if (controllers.Count == 0)
                return result;

            var components = context.GetComponentsInChildren<ReFrameDeleteComponent>(
                descriptor.gameObject,
                true
            );

            components = ReFrameDeleteComponent.FilterActive(components);

            var allTargets = new List<(string Name, float Value)>();
            var anyPreviewRequested = false;
            var allOverrides = new List<(string TreeName, string ParameterName, float Value, bool Always)>();
            var allDeletedObjectPaths = new List<string>();
            foreach (var component in components)
            {

                allDeletedObjectPaths.AddRange(
                    context.Observe(
                        component,
                        c => c.EnumerateDeleteObjectPaths().ToList(),
                        (a, b) => a.SequenceEqual(b)
                    )
                );

                var targets = context.Observe(
                    component,
                    c => c.EnumerateDeleteTargets().Select(t => (t.ParameterName, t.Value)).ToList(),
                    (a, b) => a.SequenceEqual(b)
                );
                allTargets.AddRange(targets);

                allOverrides.AddRange(
                    context.Observe(
                        component,
                        c => c.EnumerateBlendTreeOverrides().ToList(),
                        (a, b) => a.SequenceEqual(b)
                    )
                );

                if (context.Observe(component, c => c.previewHiddenInHierarchy))
                    anyPreviewRequested = true;
            }

            var allValues = new Dictionary<string, float>();
            if (anyPreviewRequested)
            {
                if (descriptor.expressionParameters != null)
                    context.Observe(descriptor.expressionParameters);
                allValues = ReFrameBakedVisibilityResolver.BuildDefaultParameterValues(descriptor);
            }
            foreach (var t in allTargets)
                allValues[t.Name] = t.Value;

            var treeOverrides = ReFrameAnimatorUtil.BuildTreeOverrides(
                allOverrides,
                allTargets.Select(t => t.Name)
            );

            ReFrameBakedVisibilityResolver.BeginResolveScope();
            try
            {
                foreach (var (controller, pathRoot) in controllers)
                {
                    context.Observe(controller);
                    ReFrameBakedVisibilityResolver.ResolveActiveStates(
                        controller,
                        pathRoot,
                        result,
                        allValues,
                        treeOverrides
                    );
                }
            }
            finally
            {
                ReFrameBakedVisibilityResolver.EndResolveScope();
            }

            ReFrameBakedVisibilityResolver.ResolveDeletedObjects(
                allDeletedObjectPaths,
                descriptor.transform,
                result
            );

            ReFrameBakedVisibilityResolver.ResolveFixedBlendShapes(
                components.SelectMany(c => c.EnumerateBlendShapeTargets()),
                descriptor.transform,
                result
            );

            return result;
        }

        static List<(AnimatorController Controller, Transform PathRoot)> CollectFxControllers(
            ComputeContext context,
            VRCAvatarDescriptor descriptor
        )
        {

            context.Observe(descriptor, d => d.baseAnimationLayers);
            foreach (
                var merge in context.GetComponentsInChildren<ModularAvatarMergeAnimator>(
                    descriptor.gameObject,
                    true
                )
            )
            {
                context.Observe(merge, m => m.enabled);
                context.Observe(merge, m => m.layerType);
                context.Observe(merge, m => m.animator);
                context.Observe(merge, m => m.pathMode);
            }

            return ReFrameBakedVisibilityResolver.CollectFxControllers(descriptor);
        }

        public Task<IRenderFilterNode> Instantiate(
            RenderGroup group,
            IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context
        )
        {
            return Task.FromResult<IRenderFilterNode>(new Node(group.GetData<PreviewData>()));
        }

        /// <summary>1 つのボーン Transform に対する localPosition/Rotation/Scale の上書き値。</summary>
        readonly struct BoneOverride : IEquatable<BoneOverride>
        {
            public readonly Vector3? Position;
            public readonly Quaternion? Rotation;
            public readonly Vector3? Scale;

            public BoneOverride(Vector3? position, Quaternion? rotation, Vector3? scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }

            public bool Equals(BoneOverride other) =>
                Position == other.Position && Rotation == other.Rotation && Scale == other.Scale;
        }

        /// <summary>Renderer 1 つ分のプレビュー結果 (enabled、任意で確定した localPosition/Rotation/Scale、 BlendShape ウェイト、マテリアル差し替え、ボーンごとの Transform 上書き)。</summary>
        readonly struct PreviewData : IEquatable<PreviewData>
        {
            public readonly bool Enabled;
            public readonly Vector3? LocalPosition;
            public readonly Quaternion? LocalRotation;
            public readonly Vector3? LocalScale;
            public readonly ImmutableDictionary<string, float> BlendShapeWeights;
            public readonly ImmutableDictionary<int, Material> MaterialOverrides;
            public readonly ImmutableDictionary<Transform, BoneOverride> BoneOverrides;
            public readonly Mesh MeshOverride;

            public PreviewData(
                bool enabled,
                Vector3? localPosition,
                Quaternion? localRotation,
                Vector3? localScale,
                ImmutableDictionary<string, float> blendShapeWeights,
                ImmutableDictionary<int, Material> materialOverrides,
                ImmutableDictionary<Transform, BoneOverride> boneOverrides,
                Mesh meshOverride
            )
            {
                MeshOverride = meshOverride;
                Enabled = enabled;
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                LocalScale = localScale;
                BlendShapeWeights = blendShapeWeights;
                MaterialOverrides = materialOverrides;
                BoneOverrides = boneOverrides;
            }

            public bool Equals(PreviewData other) =>
                Enabled == other.Enabled
                && LocalPosition == other.LocalPosition
                && LocalRotation == other.LocalRotation
                && LocalScale == other.LocalScale
                && DictionariesEqual(BlendShapeWeights, other.BlendShapeWeights)
                && DictionariesEqual(MaterialOverrides, other.MaterialOverrides)
                && DictionariesEqual(BoneOverrides, other.BoneOverrides)
                && MeshOverride == other.MeshOverride;

            static bool DictionariesEqual<TKey, TValue>(
                ImmutableDictionary<TKey, TValue> a,
                ImmutableDictionary<TKey, TValue> b
            )
            {
                if (a.Count != b.Count)
                    return false;
                foreach (var kv in a)
                {
                    if (!b.TryGetValue(kv.Key, out var value) || !Equals(kv.Value, value))
                        return false;
                }
                return true;
            }
        }

        /// <summary>取り残された影武者ボーンの掃除。</summary>
        static class ShadowSweeper
        {
            [InitializeOnLoadMethod]
            static void Install()
            {

                Sweep();

                AssemblyReloadEvents.beforeAssemblyReload += () => Sweep();
            }

            internal static int Sweep()
            {
                var doomed = new List<GameObject>();
                foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
                {
                    if (transform == null || transform.gameObject == null)
                        continue;
                    if (!transform.name.StartsWith(ReFrameDeleteComponent.PreviewShadowPrefix))
                        continue;

                    var parent = transform.parent;
                    if (parent != null && parent.name.StartsWith(ReFrameDeleteComponent.PreviewShadowPrefix))
                        continue;
                    doomed.Add(transform.gameObject);
                }
                foreach (var gameObject in doomed)
                    if (gameObject != null)
                        UnityEngine.Object.DestroyImmediate(gameObject);
                return doomed.Count;
            }
        }

        class Node : IRenderFilterNode
        {
            public RenderAspects WhatChanged =>
                (_data.MaterialOverrides.Count > 0 ? RenderAspects.Material : 0)
                | (_data.BlendShapeWeights.Count > 0 ? RenderAspects.Shapes : 0)
                | (_data.MeshOverride != null ? RenderAspects.Mesh : 0);

            readonly PreviewData _data;

            public Node(PreviewData data)
            {
                _data = data;
            }

            public void OnFrame(Renderer original, Renderer proxy)
            {
                proxy.enabled = _data.Enabled;

                if (
                    proxy is MeshRenderer
                    && (_data.LocalPosition.HasValue || _data.LocalRotation.HasValue || _data.LocalScale.HasValue)
                )
                {
                    ComputeProxyLocalTransform(
                        original.transform,
                        _data.LocalPosition,
                        _data.LocalRotation,
                        _data.LocalScale,
                        out var proxyPosition,
                        out var proxyRotation,
                        out var proxyScale
                    );
                    proxy.transform.localPosition = proxyPosition;
                    proxy.transform.localRotation = proxyRotation;
                    proxy.transform.localScale = proxyScale;
                }

                if (_data.MeshOverride != null)
                {
                    if (proxy is SkinnedMeshRenderer meshProxy)
                        meshProxy.sharedMesh = _data.MeshOverride;
                    else if (proxy.TryGetComponent<MeshFilter>(out var proxyFilter))
                        proxyFilter.sharedMesh = _data.MeshOverride;
                }

                if (proxy is SkinnedMeshRenderer proxySmr && proxySmr.sharedMesh != null)
                {
                    foreach (var kv in _data.BlendShapeWeights)
                    {
                        var index = proxySmr.sharedMesh.GetBlendShapeIndex(kv.Key);
                        if (index >= 0)
                            proxySmr.SetBlendShapeWeight(index, kv.Value);
                    }
                }

                if (_data.MaterialOverrides.Count > 0)
                {
                    var materials = proxy.sharedMaterials;
                    var changed = false;
                    foreach (var kv in _data.MaterialOverrides)
                    {
                        if (kv.Key < 0 || kv.Key >= materials.Length)
                            continue;

                        if (kv.Value == null)
                            continue;
                        materials[kv.Key] = kv.Value;
                        changed = true;
                    }
                    if (changed)
                        proxy.sharedMaterials = materials;
                }

                if (proxy is SkinnedMeshRenderer boneProxy && _data.BoneOverrides.Count > 0)
                    ApplyBoneOverrides(boneProxy);
            }

            /// <summary>proxy の bones 配列のうち、上書き対象のボーン (と、その影響を受ける子孫ボーン) だけを 「シャドウ」transform に差し替える。</summary>
            void ApplyBoneOverrides(SkinnedMeshRenderer proxy)
            {
                var realBones = proxy.bones;
                if (realBones == null || realBones.Length == 0)
                    return;

                var cache = new Dictionary<Transform, Transform>();
                var newBones = new Transform[realBones.Length];
                var changed = false;
                for (var i = 0; i < realBones.Length; i++)
                {
                    newBones[i] = ResolveEffectiveBone(realBones[i], cache);
                    if (newBones[i] != realBones[i])
                        changed = true;
                }
                if (changed)
                    proxy.bones = newBones;
            }

            /// <summary>realBone 自身か、その祖先のいずれかが上書き対象なら「シャドウ」transform を返す (上書き対象自身ならその値を、そうでなければ実ボーンの現在値をそのまま複製した 「素通し」のシャドウを、正しい親 (祖先のシャドウ、または無関係なら実の親) に付けて返す)。</summary>
            Transform ResolveEffectiveBone(Transform realBone, Dictionary<Transform, Transform> cache)
            {
                if (realBone == null)
                    return null;
                if (cache.TryGetValue(realBone, out var cached))
                    return cached;

                var effectiveParent = ResolveEffectiveBone(realBone.parent, cache);
                var hasOverride = _data.BoneOverrides.TryGetValue(realBone, out var ov);

                Transform result;
                if (!hasOverride && effectiveParent == realBone.parent)
                {
                    result = realBone;
                }
                else
                {
                    var shadow = GetOrCreateShadow(realBone, effectiveParent);
                    var localPosition = ov.Position ?? realBone.localPosition;
                    var localRotation = ov.Rotation ?? realBone.localRotation;
                    var localScale = ov.Scale ?? realBone.localScale;

                    if (ReFrameDeleteComponent.IsPreviewShadow(effectiveParent))
                    {

                        shadow.SetParent(effectiveParent, false);
                        shadow.localPosition = localPosition;
                        shadow.localRotation = localRotation;
                        shadow.localScale = localScale;
                    }
                    else
                    {

                        if (shadow.parent != null)
                            shadow.SetParent(null, false);
                        if (effectiveParent != null)
                        {
                            shadow.SetPositionAndRotation(
                                effectiveParent.TransformPoint(localPosition),
                                effectiveParent.rotation * localRotation
                            );
                            shadow.localScale = Vector3.Scale(effectiveParent.lossyScale, localScale);
                        }
                        else
                        {
                            shadow.localPosition = localPosition;
                            shadow.localRotation = localRotation;
                            shadow.localScale = localScale;
                        }
                    }
                    result = shadow;
                }
                cache[realBone] = result;
                return result;
            }

            readonly Dictionary<Transform, Transform> _boneShadows = new();

            /// <summary>この Node が自分で作った影武者。</summary>
            readonly HashSet<Transform> _ownedShadows = new();

            /// <summary>影武者を用意する。</summary>
            Transform GetOrCreateShadow(Transform realBone, Transform parent)
            {
                if (_boneShadows.TryGetValue(realBone, out var shadow) && shadow != null)
                    return shadow;

                var name = ReFrameDeleteComponent.PreviewShadowPrefix + ": " + realBone.name;
                if (parent != null)
                {
                    for (var i = 0; i < parent.childCount; i++)
                    {
                        var child = parent.GetChild(i);
                        if (child.name != name)
                            continue;

                        _boneShadows[realBone] = child;
                        return child;
                    }
                }

                var go = new GameObject(name);

                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                    go,
                    NDMFPreviewSceneManager.GetPreviewScene()
                );
                go.hideFlags = HideFlags.DontSave;
                shadow = go.transform;
                _boneShadows[realBone] = shadow;
                _ownedShadows.Add(shadow);
                return shadow;
            }

            public void Dispose()
            {

                foreach (var shadow in _ownedShadows)
                {
                    if (shadow != null)
                        UnityEngine.Object.DestroyImmediate(shadow.gameObject);
                }
                _ownedShadows.Clear();
                _boneShadows.Clear();
            }

            /// <summary>NDMF の proxy は、元オブジェクトの現在のワールド変換をそのまま鏡写しする「shadow bone」の 子としてローカル恒等姿勢に置かれる (ProxyObjectController.OnPreFrame 参照)。</summary>
            static void ComputeProxyLocalTransform(
                Transform original,
                Vector3? targetLocalPosition,
                Quaternion? targetLocalRotation,
                Vector3? targetLocalScale,
                out Vector3 proxyLocalPosition,
                out Quaternion proxyLocalRotation,
                out Vector3 proxyLocalScale
            )
            {
                var parent = original.parent;
                var finalPosition = targetLocalPosition ?? original.localPosition;
                var finalRotation = targetLocalRotation ?? original.localRotation;
                var finalScale = targetLocalScale ?? original.localScale;

                var targetWorldPosition = parent != null ? parent.TransformPoint(finalPosition) : finalPosition;
                var targetWorldRotation = parent != null ? parent.rotation * finalRotation : finalRotation;

                proxyLocalPosition = original.InverseTransformPoint(targetWorldPosition);
                proxyLocalRotation = Quaternion.Inverse(original.rotation) * targetWorldRotation;

                var currentScale = original.localScale;
                proxyLocalScale = new Vector3(
                    SafeDivide(finalScale.x, currentScale.x),
                    SafeDivide(finalScale.y, currentScale.y),
                    SafeDivide(finalScale.z, currentScale.z)
                );
            }

            static float SafeDivide(float a, float b) => Mathf.Approximately(b, 0f) ? 1f : a / b;
        }
    }
}
