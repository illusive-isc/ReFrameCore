using System.Collections.Generic;
using System.Linq;
using jp.illusive_isc.ReFrame.Core.Editor;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

[assembly: ExportsPlugin(typeof(ReFrameDeleteDefinition))]

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    public class ReFrameDeleteDefinition : Plugin<ReFrameDeleteDefinition>
    {
        public override string QualifiedName => "IllusoryOverride.ReFrameCore.Delete";
        public override string DisplayName => "ReFrameCore Delete";

        protected override void Configure()
        {

            InPhase(BuildPhase.Resolving)
                .Run(ReFrameVariantSelectPass.Instance)
                .Then.Run(ReFrameLayerRenamePass.Instance)
                .Then.Run(ReFrameDeletePass.Instance)
                .PreviewingWith(new ReFrameDeletePreview());

            InPhase(BuildPhase.Generating).Run(ReFrameNetworkIdPass.Instance);

            InPhase(BuildPhase.Optimizing)
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run(ReFrameSweepPass.Instance);

            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("com.anatawa12.avatar-optimizer")
                .Run(ReFrameNetworkIdFinalizePass.Instance);
        }
    }

    /// <summary>アバター上の ReFrameDeleteComponent をすべて収集し、各フィールドの [ReFrameDelete] で指定されたパラメーターを、その時点の値に固定した状態で アニメーション (ReFrameAnimatorUtil) / メニュー (ReFrameMenuUtil) / VRCExpressionParameters (ReFrameUtil) からまとめて非破壊に削除する。</summary>
    public class ReFrameVariantSelectPass : Pass<ReFrameVariantSelectPass>
    {
        protected override void Execute(BuildContext context)
        {
            var all = context.AvatarRootObject.GetComponentsInChildren<ReFrameDeleteComponent>(true);
            var removed = new List<string>();
            foreach (var component in all)
            {
                if (component == null || component.IsActiveForCurrentTarget)
                    continue;
                removed.Add(component.GetType().Name + (component.IsQuestVariant ? " (Quest 簡易対応版)" : " (PC 用)"));
                Object.DestroyImmediate(component);
            }
            if (removed.Count > 0)
                Debug.LogWarning(
                    "[ReFrameCore] ReFrameVariantSelectPass: ビルドターゲットが "
                        + EditorUserBuildSettings.activeBuildTarget
                        + " なので、使わない側の ReFrame を外しました: "
                        + string.Join(", ", removed)
                );
        }
    }

    [DependsOnContext(typeof(AnimatorServicesContext))]
    public class ReFrameDeletePass : Pass<ReFrameDeletePass>
    {
        protected override void Execute(BuildContext context)
        {
            var components =
                ReFrameDeleteComponent.ActiveIn(context.AvatarRootObject);
            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: found {components.Length} ReFrameDeleteComponent(s) "
                    + $"on '{context.AvatarRootObject.name}'."
            );
            if (components.Length == 0)
                return;
            DestroyedObjectPaths.Clear();

            DeduplicateExpressionParameters(context);

            CleanDeadAnimatorStructure(context);

            var allDeleteTargets = components.SelectMany(c => c.EnumerateDeleteTargets()).ToList();

            ReFrameAnimatorUtil.MaterialWriter = CreateMaterialWriter(context);

            var localTransformsBeforeBake = SnapshotLocalTransforms(context.AvatarRootObject.transform);

            var deletedNames = new HashSet<string>();

            if (allDeleteTargets.Count == 0)
            {
                Debug.LogWarning(
                    "[ReFrameCore] ReFrameDeletePass: no entries had Enabled = true; nothing to delete."
                );

                ApplyAlwaysTreeOverrides(context, components);
                FollowMergeArmatureBones(context, localTransformsBeforeBake);
                ApplyBlendShapes(context, components);

                if (components.Any(c => c != null && c.EnumerateMenuRemovals().Any()))
                    ProcessMenus(context, new string[0], components);
                DeleteObjects(context, components);
                ApplyMaxParticles(
                    context,
                    components.SelectMany(c => c.EnumerateMaxParticleTargets()).Distinct().ToArray()
                );
                ApplyQuestParticleMaterials(
                    context,
                    components
                        .SelectMany(c => c.EnumerateQuestParticleMaterialTargets())
                        .Distinct()
                        .ToArray()
                );
                WarnMissingAvatarOptimizer(context, components);
                WarnMissingConstraintConverter(context, components);
                ReFrameCoveredMeshCutter.Cut(context, components);
                DeletePhysBones(context, components);
                ConvertQuestMaterials(context, components);
                TrimQuestAssets(context, components);
            }
            else
            {
                Debug.LogWarning(
                    "[ReFrameCore] ReFrameDeletePass: targets = "
                        + string.Join(
                            ", ",
                            allDeleteTargets.Select(t =>
                                $"{t.ParameterName}={t.Value}" + (t.MenuOnly ? " (menu-only)" : "")
                            )
                        )
                );

                ExpandLinkedParameters(context, allDeleteTargets);

                ExpandWhenAllGoneParameters(components, allDeleteTargets);

                var names = allDeleteTargets.Select(t => t.ParameterName).Distinct().ToArray();
                var enabledParameterNames = new HashSet<string>(names);
                deletedNames.UnionWith(names);
                var cutTransitionParams = new HashSet<string>(
                    components.SelectMany(c => c.EnumerateCutTransitionParameters())
                );
                if (cutTransitionParams.Count > 0)
                    Debug.LogWarning(
                        "[ReFrameCore] ReFrameDeletePass: cut-transition parameters = "
                            + string.Join(", ", cutTransitionParams)
                    );
                var targets = allDeleteTargets
                    .Where(t => !t.MenuOnly)
                    .Select(t => new ReFrameAnimatorUtil.ParameterTarget(
                        t.ParameterName,
                        t.Value,
                        cutTransitionParams.Contains(t.ParameterName)
                    ))
                    .ToArray();
                var relatedBlendTreeTargets = components
                    .SelectMany(c => c.EnumerateRelatedBlendTreeTargets())
                    .Select(t => new ReFrameAnimatorUtil.RelatedBlendTreeTarget(t.Name, t.Bake))
                    .ToArray();
                if (relatedBlendTreeTargets.Length > 0)
                    Debug.LogWarning(
                        "[ReFrameCore] ReFrameDeletePass: related BlendTree targets = "
                            + string.Join(
                                ", ",
                                relatedBlendTreeTargets.Select(t => $"{t.Name}(bake={t.Bake})")
                            )
                    );

                var layerNames = components
                    .SelectMany(c => c.EnumerateDeleteLayerTargets())
                    .Distinct()
                    .ToArray();
                if (layerNames.Length > 0)
                    Debug.LogWarning(
                        "[ReFrameCore] ReFrameDeletePass: layer targets = " + string.Join(", ", layerNames)
                    );

                var stateNames = components
                    .SelectMany(c => c.EnumerateDeleteStateTargets())
                    .Distinct()
                    .ToArray();
                if (stateNames.Length > 0)
                    Debug.LogWarning(
                        "[ReFrameCore] ReFrameDeletePass: state targets = "
                            + string.Join(", ", stateNames.Select(s => s.LayerName + "/" + s.StateName))
                    );

                var treeOverrides = ReFrameAnimatorUtil.BuildTreeOverrides(
                    components.SelectMany(c => c.EnumerateBlendTreeOverrides()),
                    targets.Select(t => t.Name)
                );

                if (
                    targets.Length > 0
                    || relatedBlendTreeTargets.Length > 0
                    || layerNames.Length > 0
                    || stateNames.Length > 0
                    || treeOverrides.Count > 0
                )
                {
                    ProcessAnimators(
                        context,
                        targets,
                        relatedBlendTreeTargets,
                        layerNames,
                        stateNames,
                        treeOverrides
                    );
                }
                else
                {
                    Debug.LogWarning(
                        "[ReFrameCore] ReFrameDeletePass: all enabled entries are MenuOnly; skipping AnimatorController processing."
                    );
                }
                FollowMergeArmatureBones(context, localTransformsBeforeBake);

                var maxParticleTargets = components
                    .SelectMany(c => c.EnumerateMaxParticleTargets())
                    .Distinct()
                    .ToArray();

                ApplyBlendShapes(context, components);
                DeleteObjects(context, components);
                ApplyMaxParticles(context, maxParticleTargets);

                ApplyQuestParticleMaterials(
                    context,
                    components
                        .SelectMany(c => c.EnumerateQuestParticleMaterialTargets())
                        .Distinct()
                        .ToArray()
                );
                WarnMissingAvatarOptimizer(context, components);
                WarnMissingConstraintConverter(context, components);
                ReFrameCoveredMeshCutter.Cut(context, components);
                DeletePhysBones(context, components);
                ConvertQuestMaterials(context, components);
                DeleteQuestUnsupportedComponents(context, components);
                CutUndrivenTransitions(context, components);
                ProcessMenus(context, names, components);
                TrimQuestAssets(context, components);
                ProcessModularAvatarParameters(context, names);

                var expressionParameters = GetOrCloneExpressionParameters(context);
                if (expressionParameters == null)
                {
                    Debug.LogWarning(
                        "[ReFrameCore] ReFrameDeletePass: no expressionParameters on avatar descriptor; skipped."
                    );
                }
                else
                {
                    var paramsRemoved = ReFrameUtil.RemoveParameter(expressionParameters, names);
                    Debug.LogWarning(
                        $"[ReFrameCore] ReFrameDeletePass: ReFrameUtil.RemoveParameter -> removed {paramsRemoved} parameter(s)"
                    );
                }
            }

            UnsyncParameters(context, components, deletedNames);
            ReFrameAnimatorUtil.MaterialWriter = null;

            CleanDeadAnimatorStructure(context);

            context.GetState<ReFrameSweepRequest>().Enabled = components.Any(c =>
                c.SweepUnusedObjects
            );

            foreach (var comp in components)
                Object.DestroyImmediate(comp);
            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: destroyed {components.Length} ReFrameDeleteComponent(s)."
            );
        }

        /// <summary>アバター本体の FX と、layerType == FX の ModularAvatarMergeAnimator が 参照している (マージ前の) コントローラの両方から、対象パラメーターを削除する。</summary>
        static void UnsyncParameters(
            BuildContext context,
            ReFrameDeleteComponent[] components,
            HashSet<string> deletedNames
        )
        {
            var names = new HashSet<string>(
                components.SelectMany(c => c.EnumerateUnsyncParameters())
            );

            names.ExceptWith(deletedNames);
            if (names.Count == 0)
                return;

            var changed = 0;
            var freedBits = 0;
            var missing = new HashSet<string>(names);

            var expressionParameters = GetOrCloneExpressionParameters(context);
            if (expressionParameters?.parameters != null)
            {
                foreach (var parameter in expressionParameters.parameters)
                {
                    if (parameter == null || !names.Contains(parameter.name))
                        continue;
                    missing.Remove(parameter.name);
                    if (!parameter.networkSynced)
                        continue;
                    parameter.networkSynced = false;
                    changed++;
                    freedBits +=
                        parameter.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8;
                }
            }

            foreach (
                var maParams in context.AvatarRootObject.GetComponentsInChildren<ModularAvatarParameters>(
                    true
                )
            )
            {
                if (maParams.parameters == null)
                    continue;
                for (var i = 0; i < maParams.parameters.Count; i++)
                {
                    var config = maParams.parameters[i];
                    if (string.IsNullOrEmpty(config.nameOrPrefix) || !names.Contains(config.nameOrPrefix))
                        continue;
                    missing.Remove(config.nameOrPrefix);
                    if (config.localOnly)
                        continue;
                    config.localOnly = true;
                    maParams.parameters[i] = config;
                    changed++;
                    freedBits += config.syncType == ParameterSyncType.Bool ? 1 : 8;
                }
            }

            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: unsynced {changed} parameter(s), freed {freedBits} bit(s)"
                    + (missing.Count > 0
                        ? "  / 同期対象として見つからない指定: " + string.Join(", ", missing)
                        : string.Empty)
            );
        }

        /// <summary>[ReFrameCutUndrivenTransitions] で名前を挙げたパラメーターのうち、アバター上でその値を 書き込む主体が 1 つも残っていないものについて、それを条件に使っている遷移を削除する。</summary>
        static void CutUndrivenTransitions(BuildContext context, ReFrameDeleteComponent[] components)
        {

            var declared = new Dictionary<string, bool>();
            foreach (var (name, evaluateAsFixed) in components.SelectMany(c =>
                c.EnumerateCutUndrivenTransitionParameters()
            ))
                declared[name] = declared.TryGetValue(name, out var prev)
                    ? prev || evaluateAsFixed
                    : evaluateAsFixed;
            if (declared.Count == 0)
                return;

            var vcc = context.Extension<AnimatorServicesContext>().ControllerContext;
            var controllers = vcc.Controllers.Values.Where(c => c != null).Distinct().ToList();

            var driven = new HashSet<string>();
            foreach (var controller in controllers)
                ReFrameAnimatorUtil.CollectDriverWrittenParameters(controller, driven);
            CollectContactWrittenParameters(context, driven);

            var skipped = declared.Keys.Where(driven.Contains).ToArray();
            if (skipped.Length > 0)
                Debug.LogWarning(
                    "[ReFrameCore] ReFrameDeletePass: cut-undriven skipped (ParameterDriver か "
                        + "まだ動きうる VRCContactReceiver が書き込んでいる) = "
                        + string.Join(", ", skipped)
                );

            var cutWhole = declared
                .Where(kv => !kv.Value && !driven.Contains(kv.Key))
                .Select(kv => kv.Key)
                .ToArray();
            var evaluated = declared
                .Where(kv => kv.Value && !driven.Contains(kv.Key))
                .Select(kv => kv.Key)
                .ToArray();
            if (cutWhole.Length == 0 && evaluated.Length == 0)
                return;

            var removed = 0;
            var pruned = 0;
            foreach (var controller in controllers)
            {
                var cut = 0;
                if (cutWhole.Length > 0)
                    cut += ReFrameAnimatorUtil.RemoveTransitionsByParameter(controller, cutWhole);
                if (evaluated.Length > 0)
                    cut += ReFrameAnimatorUtil.CleanTransitionsByFixedValue(controller, evaluated);
                if (cut == 0)
                    continue;
                removed += cut;
                pruned += ReFrameAnimatorUtil.PruneUnreachableStates(controller);
            }
            Debug.LogWarning(
                "[ReFrameCore] ReFrameDeletePass: cut-undriven"
                    + (cutWhole.Length > 0 ? $" 遷移ごと=[{string.Join(", ", cutWhole)}]" : "")
                    + (evaluated.Length > 0 ? $" 固定値で評価=[{string.Join(", ", evaluated)}]" : "")
                    + $" -> removed {removed} transition(s) / pruned {pruned} unreachable state(s)"
                    + $" across {controllers.Count} controller(s)"
            );
        }

        /// <summary>まだ動きうる VRCContactReceiver が書き込むパラメーター名を集める。</summary>
        static void CollectContactWrittenParameters(BuildContext context, HashSet<string> result)
        {
            var asc = context.Extension<AnimatorServicesContext>();
            var root = context.AvatarRootTransform;
            foreach (
                var receiver in context.AvatarRootObject.GetComponentsInChildren<VRCContactReceiver>(
                    true
                )
            )
            {
                if (receiver == null || string.IsNullOrEmpty(receiver.parameter))
                    continue;
                if (receiver.isActiveAndEnabled || CanStillBeActivated(receiver.transform, root, asc))
                    result.Add(receiver.parameter);
            }
        }

        /// <summary>この Transform か、その祖先のどれかの m_IsActive を書くカーブが、 残ったクリップにまだ存在するか。</summary>
        static bool CanStillBeActivated(
            Transform target,
            Transform root,
            AnimatorServicesContext asc
        )
        {
            for (var current = target; current != null && current != root; current = current.parent)
            foreach (var path in asc.ObjectPathRemapper.GetAllPathsForObject(current))
            foreach (var clip in asc.AnimationIndex.GetClipsForObjectPath(path))
            foreach (var binding in clip.GetFloatCurveBindings())
                if (
                    binding.path == path
                    && binding.type == typeof(GameObject)
                    && binding.propertyName == "m_IsActive"
                )
                    return true;
            return false;
        }

        /// <summary>[ReFrameDeleteObject] で名指しされた GameObject をアバターから破棄する。</summary>
        static string RelativePathFrom(Transform root, Transform target)
        {
            var path = target.name;
            var cursor = target.parent;
            while (cursor != null && cursor != root)
            {
                path = cursor.name + "/" + path;
                cursor = cursor.parent;
            }
            return path;
        }

        /// <summary>[ReFrameSetMaxParticles] で宣言された ParticleSystem の maxParticles を下げる。</summary>
        static void ApplyMaxParticles(BuildContext context, (string Path, int MaxParticles)[] targets)
        {
            if (targets.Length == 0)
                return;

            var root = context.AvatarRootTransform;
            var applied = new List<string>();
            foreach (var target in targets)
            {
                var transform = root.Find(target.Path);
                if (transform == null)
                {

                    if (!IsUnderDestroyedObject(target.Path))
                        Debug.LogWarning(
                            $"[ReFrameCore] ReFrameDeletePass: [ReFrameSetMaxParticles] path '{target.Path}' not found; skipped."
                        );
                    continue;
                }
                var particles = transform.GetComponent<ParticleSystem>();
                if (particles == null)
                {
                    Debug.LogWarning(
                        $"[ReFrameCore] ReFrameDeletePass: [ReFrameSetMaxParticles] '{target.Path}' has no ParticleSystem; skipped."
                    );
                    continue;
                }

                var main = particles.main;
                if (main.maxParticles <= target.MaxParticles)
                    continue;
                applied.Add($"{target.Path}: {main.maxParticles} -> {target.MaxParticles}");
                main.maxParticles = target.MaxParticles;
            }

            if (applied.Count > 0)
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameDeletePass: maxParticles を {applied.Count} 件下げました。"
                        + "\n  "
                        + string.Join("\n  ", applied)
                );
        }

        /// <summary>[ReFrameQuestParticleMaterial] で宣言されたパーティクルのマテリアルを、Quest で 許可されたシェーダー (VRChat/Mobile/Particles/Additive / Multiply) を 使う新しいマテリアルへ差し替える。</summary>
        static void ApplyQuestParticleMaterials(
            BuildContext context,
            (string Path, ReFrameQuestParticleBlend Blend)[] targets
        )
        {
            if (targets.Length == 0)
                return;

            var root = context.AvatarRootTransform;
            var generated = new Dictionary<(Material, ReFrameQuestParticleBlend), Material>();
            var applied = new List<string>();

            foreach (var target in targets)
            {
                var transform = root.Find(target.Path);
                if (transform == null)
                {

                    if (!IsUnderDestroyedObject(target.Path))
                        Debug.LogWarning(
                            $"[ReFrameCore] ReFrameDeletePass: [ReFrameQuestParticleMaterial] path '{target.Path}' not found; skipped."
                        );
                    continue;
                }
                var renderer = transform.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.sharedMaterial == null)
                {
                    Debug.LogWarning(
                        $"[ReFrameCore] ReFrameDeletePass: [ReFrameQuestParticleMaterial] '{target.Path}' has no ParticleSystemRenderer/material; skipped."
                    );
                    continue;
                }

                var source = renderer.sharedMaterial;

                if (source.shader != null && source.shader.name.StartsWith("VRChat/Mobile/"))
                    continue;

                var key = (source, target.Blend);
                if (!generated.TryGetValue(key, out var replacement))
                {
                    var shaderName =
                        target.Blend == ReFrameQuestParticleBlend.Multiply
                            ? "VRChat/Mobile/Particles/Multiply"
                            : "VRChat/Mobile/Particles/Additive";
                    var shader = Shader.Find(shaderName);
                    if (shader == null)
                    {
                        Debug.LogWarning(
                            $"[ReFrameCore] ReFrameDeletePass: shader '{shaderName}' not found; skipped '{target.Path}'."
                        );
                        continue;
                    }
                    replacement = new Material(shader)
                    {
                        name = source.name + " (Quest)",
                        mainTexture = source.mainTexture,
                        mainTextureScale = source.mainTextureScale,
                        mainTextureOffset = source.mainTextureOffset,
                    };
                    context.AssetSaver.SaveAsset(replacement);
                    generated[key] = replacement;
                }

                renderer.sharedMaterial = replacement;
                applied.Add($"{target.Path}: {source.shader.name} -> {replacement.shader.name}");
            }

            if (applied.Count > 0)
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameDeletePass: パーティクルのマテリアルを {applied.Count} 件 Quest 対応へ差し替えました。"
                        + "\n  "
                        + string.Join("\n  ", applied)
                );
        }

        /// <summary>「Quest 対応」が ON のとき、Quest で使えないシェーダーのマテリアルを VRChat/Mobile/Toon Lit へ変換する (ReFrameQuestMaterialConverter)。</summary>
        static void ConvertQuestMaterials(BuildContext context, ReFrameDeleteComponent[] components)
        {
            foreach (var component in components)
            {
                if (component == null || !component.QuestConversionActive)
                    continue;
                ReFrameQuestMaterialConverter.Convert(context);
                return;
            }
        }

        /// <summary>「Quest 簡易対応」が ON のときの、アップロードを止めないぶんの手当て。</summary>
        static void TrimQuestAssets(BuildContext context, ReFrameDeleteComponent[] components)
        {
            var quest = false;

            var iconMode = ReFrameMenuIconMode.Keep;
            foreach (var component in components)
            {
                if (component == null)
                    continue;
                if (component.QuestConversionActive)
                    quest = true;
                if (component.menuIconMode == ReFrameMenuIconMode.Keep)
                    continue;
                if (iconMode == ReFrameMenuIconMode.Keep || component.menuIconMode < iconMode)
                    iconMode = component.menuIconMode;
            }
            if (!quest)
                return;

            var cutTris = ReFrameQuestAssetTrim.CutTransparent(context);
            if (cutTris > 0)
                Debug.Log($"[ReFrameCore] 透明なので削った三角形: {cutTris} 枚");

            var meshes = ReFrameQuestAssetTrim.RemoveVertexColors(context);
            if (meshes > 0)
                Debug.Log($"[ReFrameCore] 頂点カラーを消したメッシュ: {meshes} 個");

            var icons = ReFrameQuestAssetTrim.CompressMenuIcons(context, iconMode);
            if (icons > 0)
                Debug.Log(
                    iconMode == ReFrameMenuIconMode.Remove
                        ? $"[ReFrameCore] 外したメニューアイコン: {icons} 箱所"
                        : $"[ReFrameCore] 縮めて圧縮したメニューアイコン: {icons} 箱所"
                );
        }

        /// <summary>Inspector の一覧で選ばれた VRCPhysBone / VRCPhysBoneCollider を取り除く。</summary>
        static void WarnMissingConstraintConverter(BuildContext context, ReFrameDeleteComponent[] components)
        {
            var wanted = false;
            foreach (var component in components)
                if (component != null && component.QuestConversionActive)
                    wanted = true;
            if (!wanted)
                return;
            var root = context.AvatarRootObject;
            if (root.GetComponentInChildren<ModularAvatarConvertConstraints>(true) != null)
                return;
            var count = 0;
            foreach (var c in root.GetComponentsInChildren<Component>(true))
                if (c is UnityEngine.Animations.IConstraint)
                    count++;
            if (count == 0)
                return;
            Debug.LogWarning(
                $"[ReFrameCore] Quest 簡易対応が ON ですが MA Convert Constraints が無く、Unity 標準の Constraint {count} 個が非対応扱いになります。"
                    + "アバターに MA Convert Constraints を付けてください (Inspector のボタンから追加できます)。"
            );
        }

        static void WarnMissingAvatarOptimizer(BuildContext context, ReFrameDeleteComponent[] components)
        {
            var wanted = false;
            foreach (var component in components)
            {
                if (component != null && component.QuestConversionActive)
                {
                    wanted = true;
                    break;
                }
            }
            if (!wanted)
                return;

            var type = System.Type.GetType(
                "Anatawa12.AvatarOptimizer.TraceAndOptimize, com.anatawa12.avatar-optimizer.runtime"
            );
            if (type == null)
            {
                Debug.LogWarning(
                    "[ReFrameCore] Quest 簡易対応が ON ですが AvatarOptimizer が入っていません。"
                        + "ポリゴン・マテリアル・ボーンの削減は ReFrame では手が届きません。"
                );
                return;
            }
            if (context.AvatarRootObject.GetComponentInChildren(type, true) != null)
                return;

            Debug.LogWarning(
                "[ReFrameCore] Quest 簡易対応が ON ですが AvatarOptimizer の Trace and Optimize が"
                    + "アバターに付いていません。メッシュ側の削減が一切走らないまま出力されます。"
            );
        }

        static void DeletePhysBones(BuildContext context, ReFrameDeleteComponent[] components)
        {
            var physBoneKeys = new HashSet<string>();
            var colliderKeys = new HashSet<string>();
            foreach (var component in components)
            {

                if (component == null || !component.QuestConversionActive)
                    continue;
                if (component.deletedPhysBones != null)
                    physBoneKeys.UnionWith(component.deletedPhysBones);
                if (component.deletedPhysBoneColliders != null)
                    colliderKeys.UnionWith(component.deletedPhysBoneColliders);
            }
            if (physBoneKeys.Count == 0 && colliderKeys.Count == 0)
                return;

            var root = context.AvatarRootTransform;
            var removedColliders = new List<VRCPhysBoneCollider>();
            foreach (var key in colliderKeys)
            {
                var collider = ReFramePhysBoneCatalog.ResolveLoose<VRCPhysBoneCollider>(root, key);
                if (collider != null)
                    removedColliders.Add(collider);
            }

            var physBones = new List<VRCPhysBone>();
            foreach (var key in physBoneKeys)
            {
                var physBone = ReFramePhysBoneCatalog.ResolveLoose<VRCPhysBone>(root, key);
                if (physBone != null)
                    physBones.Add(physBone);
            }

            if (removedColliders.Count > 0)
            {
                foreach (var physBone in root.GetComponentsInChildren<VRCPhysBone>(true))
                {
                    if (physBone.colliders == null)
                        continue;
                    physBone.colliders.RemoveAll(c =>
                        c == null || removedColliders.Contains(c as VRCPhysBoneCollider)
                    );
                }
            }

            foreach (var physBone in physBones)
                Object.DestroyImmediate(physBone);
            foreach (var collider in removedColliders)
                Object.DestroyImmediate(collider);

            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: PhysBone を {physBones.Count} 本、"
                    + $"PhysBoneCollider を {removedColliders.Count} 個取り除きました。"
            );
        }

        static void DeleteQuestUnsupportedComponents(
            BuildContext context,
            ReFrameDeleteComponent[] components
        )
        {
            var requested = false;
            foreach (var component in components)
            {
                if (component == null || !component.QuestConversionActive)
                    continue;
                requested = true;
                break;
            }
            if (!requested)
                return;

            var root = context.AvatarRootTransform;
            var removed = new List<string>();

            var targets = new List<Component>();
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (ReFrameDeleteComponent.IsQuestUnsupportedComponent(component))
                    targets.Add(component);
            }

            targets.Sort((a, b) => (a is Rigidbody ? 1 : 0).CompareTo(b is Rigidbody ? 1 : 0));

            foreach (var target in targets)
            {
                if (target == null)
                    continue;
                removed.Add(
                    target.GetType().Name + " on " + RelativePathFrom(root, target.transform)
                );
                Object.DestroyImmediate(target);
            }

            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: Quest 非対応コンポーネントを {removed.Count} 個取り除きました。"
                    + (removed.Count > 0 ? "\n  " + string.Join("\n  ", removed) : "")
            );
        }

        /// <summary>[ReFrameBlendShape] の固定を SkinnedMeshRenderer に書く。</summary>
        static void ApplyBlendShapes(BuildContext context, ReFrameDeleteComponent[] components)
        {
            var targets = components
                .Where(c => c != null)
                .SelectMany(c => c.EnumerateBlendShapeTargets())
                .ToArray();
            if (targets.Length == 0)
                return;

            var root = context.AvatarRootTransform;
            var applied = new List<string>();
            var animated = new HashSet<string>();
            foreach (var (controller, _) in CollectProcessedControllers(context))
                foreach (var layer in controller.Layers)
                {
                    if (layer.StateMachine == null)
                        continue;
                    foreach (var clip in CollectClipsForBlendShapeCheck(layer.StateMachine))
                        foreach (var binding in clip.GetFloatCurveBindings())
                            if (binding.propertyName.StartsWith("blendShape."))
                                animated.Add(binding.path + "|" + binding.propertyName.Substring("blendShape.".Length));
                }

            foreach (var target in targets)
            {
                var transform = root.Find(target.Path);
                var renderer = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
                if (renderer == null || renderer.sharedMesh == null)
                {
                    Debug.LogWarning(
                        $"[ReFrameCore] ReFrameDeletePass: [ReFrameBlendShape] '{target.Path}' に SkinnedMeshRenderer が無いので飛ばしました。"
                    );
                    continue;
                }
                var index = renderer.sharedMesh.GetBlendShapeIndex(target.ShapeName);
                if (index < 0)
                {
                    Debug.LogWarning(
                        $"[ReFrameCore] ReFrameDeletePass: [ReFrameBlendShape] '{target.Path}' に BlendShape '{target.ShapeName}' が無いので飛ばしました。"
                    );
                    continue;
                }
                renderer.SetBlendShapeWeight(index, target.Weight);
                applied.Add($"{target.Path} / {target.ShapeName} = {target.Weight:F0}");
                if (animated.Contains(target.Path + "|" + target.ShapeName))
                    Debug.LogWarning(
                        $"[ReFrameCore] ReFrameDeletePass: '{target.Path}' の BlendShape '{target.ShapeName}' はアニメーションが動かしているので、固定値 {target.Weight:F0} は再生中に上書きされます。"
                    );
            }
            if (applied.Count > 0)
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameDeletePass: BlendShape を {applied.Count} 件固定しました。"
                        + (char)10 + "  " + string.Join((char)10 + "  ", applied)
                );
        }

        static IEnumerable<VirtualClip> CollectClipsForBlendShapeCheck(VirtualStateMachine sm)
        {
            foreach (var childState in sm.States)
                foreach (var clip in CollectClipsForBlendShapeCheck(childState.State?.Motion))
                    yield return clip;
            foreach (var child in sm.StateMachines)
                foreach (var clip in CollectClipsForBlendShapeCheck(child.StateMachine))
                    yield return clip;
        }

        static IEnumerable<VirtualClip> CollectClipsForBlendShapeCheck(VirtualMotion motion)
        {
            switch (motion)
            {
                case VirtualClip clip:
                    yield return clip;
                    break;
                case VirtualBlendTree tree:
                    foreach (var child in tree.Children)
                        foreach (var clip in CollectClipsForBlendShapeCheck(child.Motion))
                            yield return clip;
                    break;
            }
        }

        static void DeleteObjects(BuildContext context, ReFrameDeleteComponent[] components)
        {
            var paths = components
                .SelectMany(c => c.EnumerateDeleteObjectPaths())
                .Distinct()
                .ToArray();
            if (paths.Length == 0)
                return;

            var root = context.AvatarRootTransform;
            foreach (var path in paths)
            {
                var target = root.Find(path);
                if (target == null)
                {
                    Debug.LogWarning(
                        $"[ReFrameCore] ReFrameDeletePass: [ReFrameDeleteObject] path '{path}' not found; skipped."
                    );
                    continue;
                }
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameDeletePass: destroying object '{path}' "
                        + $"({target.GetComponentsInChildren<Component>(true).Length} component(s) in subtree)."
                );
                Object.DestroyImmediate(target.gameObject);
                DestroyedObjectPaths.Add(path);
            }
        }

        /// <summary>このビルドで DeleteObjects が破棄したパス。</summary>
        static readonly HashSet<string> DestroyedObjectPaths = new HashSet<string>();

        /// <summary>そのパスが、このビルドで破棄したオブジェクトの中にあるか。</summary>
        static bool IsUnderDestroyedObject(string path)
        {
            foreach (var destroyed in DestroyedObjectPaths)
                if (path == destroyed || path.StartsWith(destroyed + "/"))
                    return true;
            return false;
        }

        /// <summary>削除対象を「道連れで死ぬパラメーター」まで広げる。</summary>
        static void ExpandLinkedParameters(
            BuildContext context,
            List<(string ParameterName, float Value, bool MenuOnly)> targets
        )
        {
            var descriptor = context.AvatarDescriptor;
            if (descriptor == null)
                return;

            var links = ReFrameParameterLink.Build(descriptor);
            if (links.Count == 0)
                return;

            var known = new HashSet<string>(targets.Select(t => t.ParameterName));
            var added = new List<string>();

            foreach (var (parameter, value, menuOnly) in targets.ToList())
            {
                if (menuOnly || !links.TryGetValue(parameter, out var list))
                    continue;
                foreach (var link in list)
                {
                    if (!known.Add(link.Name))
                        continue;

                    var fixedValue = link.UseOwnerValue ? value : link.DefaultValue;
                    targets.Add((link.Name, fixedValue, false));
                    added.Add(link.Name + "=" + fixedValue + " (" + link.Reason + " of " + parameter + ")");
                }
            }

            if (added.Count > 0)
                Debug.LogWarning(
                    "[ReFrameCore] ReFrameDeletePass: linked parameters = "
                        + string.Join(", ", added)
                );
        }

        /// <summary>「関連が全部消えたときにだけ死ぬ」パラメーターを片付ける (ReFrameDeleteWhenAllGoneAttribute)。</summary>
        static void ExpandWhenAllGoneParameters(
            ReFrameDeleteComponent[] components,
            List<(string ParameterName, float Value, bool MenuOnly)> targets
        )
        {
            var rules = new List<ReFrameDeleteWhenAllGoneAttribute>();
            var seenTypes = new HashSet<System.Type>();
            foreach (var component in components)
            {
                if (component == null || !seenTypes.Add(component.GetType()))
                    continue;
                foreach (
                    var rule in component
                        .GetType()
                        .GetCustomAttributes(typeof(ReFrameDeleteWhenAllGoneAttribute), true)
                )
                    rules.Add((ReFrameDeleteWhenAllGoneAttribute)rule);
            }
            if (rules.Count == 0)
                return;

            var deleting = new HashSet<string>(
                targets.Where(t => !t.MenuOnly).Select(t => t.ParameterName)
            );
            var added = new List<string>();

            for (var pass = 0; pass <= rules.Count; pass++)
            {
                var changed = false;
                foreach (var rule in rules)
                {
                    if (string.IsNullOrEmpty(rule.Parameter) || deleting.Contains(rule.Parameter))
                        continue;
                    if (rule.Requires.Length == 0)
                        continue;

                    var allGone = true;
                    foreach (var required in rule.Requires)
                        if (!deleting.Contains(required))
                        {
                            allGone = false;
                            break;
                        }
                    if (!allGone)
                        continue;

                    deleting.Add(rule.Parameter);
                    targets.Add((rule.Parameter, rule.Value, false));
                    added.Add(rule.Parameter + "=" + rule.Value);
                    changed = true;
                }
                if (!changed)
                    break;
            }

            if (added.Count > 0)
                Debug.LogWarning(
                    "[ReFrameCore] ReFrameDeletePass: when-all-gone parameters = "
                        + string.Join(", ", added)
                );
        }

        /// <summary>値ベースの削除・BT 名指し削除・レイヤー削除の対象にするプレイアブルレイヤー。</summary>
        static readonly VRCAvatarDescriptor.AnimLayerType[] ProcessedLayerTypes =
        {
            VRCAvatarDescriptor.AnimLayerType.FX,
            VRCAvatarDescriptor.AnimLayerType.Gesture,
            VRCAvatarDescriptor.AnimLayerType.Base,
            VRCAvatarDescriptor.AnimLayerType.Action,
        };

        /// <summary>値ベースの削除・名指し削除・上書きの対象にするコントローラーと、そのクリップの パスを解決する基点 (bakeRoot) の組を集める。</summary>
        static List<(VirtualAnimatorController Controller, Transform BakeRoot)> CollectProcessedControllers(
            BuildContext context
        )
        {
            var vcc = context.Extension<AnimatorServicesContext>().ControllerContext;
            var avatarRoot = context.AvatarRootObject.transform;
            var controllers = new List<(VirtualAnimatorController Controller, Transform BakeRoot)>();

            foreach (var layerType in ProcessedLayerTypes)
                if (vcc.Controllers.TryGetValue(layerType, out var controller) && controller != null)
                    controllers.Add((controller, avatarRoot));

            foreach (
                var merge in context.AvatarRootObject.GetComponentsInChildren<ModularAvatarMergeAnimator>(true)
            )
            {
                if (!merge.enabled || System.Array.IndexOf(ProcessedLayerTypes, merge.layerType) < 0)
                    continue;
                if (merge.animator == null)
                    continue;
                if (vcc.Controllers.TryGetValue(merge, out var mergeController) && mergeController != null)
                    controllers.Add(
                        (mergeController, ReFrameBakedVisibilityResolver.ResolveMergeAnimatorPathRoot(merge, avatarRoot))
                    );
            }

            return controllers.Distinct().ToList();
        }

        /// <summary>VRChat が自分で値を入れる組み込みパラメーター。</summary>
        static readonly HashSet<string> BuiltinParameterNames = new()
        {
            "IsLocal", "Viseme", "Voice", "GestureLeft", "GestureRight", "GestureLeftWeight",
            "GestureRightWeight", "AngularY", "VelocityX", "VelocityY", "VelocityZ",
            "VelocityMagnitude", "Upright", "Grounded", "Seated", "AFK", "TrackingType", "VRMode",
            "MuteSelf", "InStation", "Earmuffs", "IsOnFriendsList", "AvatarVersion", "ScaleModified",
            "ScaleFactor", "ScaleFactorInverse", "EyeHeightAsMeters", "EyeHeightAsPercent",
            "IsAnimatorEnabled", "PreviewMode", "VRCFaceBlendH", "VRCFaceBlendV", "VRCEmote",
        };

        /// <summary>元から死んでいる構造を、削除設定と無関係に片付ける。</summary>
        static void CleanDeadAnimatorStructure(BuildContext context)
        {
            var keep = new HashSet<string>(BuiltinParameterNames);
            var expressionParameters = context.AvatarDescriptor?.expressionParameters;
            if (expressionParameters?.parameters != null)
                foreach (var parameter in expressionParameters.parameters)
                    if (parameter != null && !string.IsNullOrEmpty(parameter.name))
                        keep.Add(parameter.name);
            var keepPrefixes = new List<string>();
            foreach (
                var maParams in context.AvatarRootObject.GetComponentsInChildren<ModularAvatarParameters>(true)
            )
                if (maParams.parameters != null)
                    foreach (var config in maParams.parameters)
                    {
                        if (string.IsNullOrEmpty(config.nameOrPrefix))
                            continue;
                        if (config.isPrefix)
                            keepPrefixes.Add(config.nameOrPrefix);
                        else
                            keep.Add(config.nameOrPrefix);
                    }

            foreach (var item in context.AvatarRootObject.GetComponentsInChildren<ModularAvatarMenuItem>(true))
            {
                var control = item.Control;
                if (control == null)
                    continue;
                if (control.parameter != null && !string.IsNullOrEmpty(control.parameter.name))
                    keep.Add(control.parameter.name);
                if (control.subParameters != null)
                    foreach (var sub in control.subParameters)
                        if (sub != null && !string.IsNullOrEmpty(sub.name))
                            keep.Add(sub.name);
            }
            foreach (
                var merge in context.AvatarRootObject.GetComponentsInChildren<ModularAvatarMergeBlendTree>(true)
            )
                CollectBlendTreeParameters(merge.BlendTree as UnityEditor.Animations.BlendTree, keep);

            var prunedStates = 0;
            var removedParameters = new List<string>();
            foreach (var (controller, _) in CollectProcessedControllers(context))
            {
                prunedStates += ReFrameAnimatorUtil.PruneUnreachableStates(controller);
                removedParameters.AddRange(
                    ReFrameAnimatorUtil.RemoveUnreferencedParameters(controller, keep, keepPrefixes)
                        .Select(n => controller.Name + ":" + n)
                );
            }
            if (prunedStates > 0 || removedParameters.Count > 0)
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameDeletePass: 元から死んでいた構造を片付けました。入口の無いステート {prunedStates} 件、"
                        + $"参照されないパラメーター定義 {removedParameters.Count} 件"
                        + (removedParameters.Count > 0 ? " [" + string.Join(", ", removedParameters) + "]" : "")
                );
        }

        static void CollectBlendTreeParameters(UnityEditor.Animations.BlendTree tree, HashSet<string> result)
        {
            if (tree == null)
                return;
            if (!string.IsNullOrEmpty(tree.blendParameter))
                result.Add(tree.blendParameter);
            if (!string.IsNullOrEmpty(tree.blendParameterY))
                result.Add(tree.blendParameterY);
            foreach (var child in tree.children)
            {
                if (!string.IsNullOrEmpty(child.directBlendParameter))
                    result.Add(child.directBlendParameter);
                CollectBlendTreeParameters(child.motion as UnityEditor.Animations.BlendTree, result);
            }
        }

        /// <summary>焼き付けが書き込んでよいマテリアルを返す関数を作る。</summary>
        static System.Func<Renderer, int, Material> CreateMaterialWriter(BuildContext context)
        {
            var clones = new Dictionary<Material, Material>();
            return (renderer, slot) =>
            {
                if (renderer == null)
                    return null;
                var materials = renderer.sharedMaterials;
                if (slot < 0 || slot >= materials.Length || materials[slot] == null)
                    return null;
                var source = materials[slot];
                if (context.IsTemporaryAsset(source))
                    return source;
                if (!clones.TryGetValue(source, out var clone))
                {
                    clone = new Material(source) { name = source.name };
                    context.AssetSaver.SaveAsset(clone);
                    clones[source] = clone;
                }
                materials[slot] = clone;
                renderer.sharedMaterials = materials;
                return clone;
            };
        }

        static Dictionary<Transform, (Vector3 Position, Quaternion Rotation, Vector3 Scale)> SnapshotLocalTransforms(Transform root)
        {
            var snapshot = new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                snapshot[t] = (t.localPosition, t.localRotation, t.localScale);
            return snapshot;
        }

        /// <summary>焼き込みで動いた統合先のボーンに、MA Merge Armature の統合元が持つ同名の複製ボーンを合わせる (Merge Armature はワールド位置を保って付け替えるので、放っておくと統合元だけが元の場所に残る)。</summary>
        static void FollowMergeArmatureBones(
            BuildContext context,
            Dictionary<Transform, (Vector3 Position, Quaternion Rotation, Vector3 Scale)> before
        )
        {
            var moved = 0;
            foreach (var merge in context.AvatarRootObject.GetComponentsInChildren<ModularAvatarMergeArmature>(true))
            {
                var pairs = merge.GetBonesMapping();
                if (pairs == null)
                    continue;
                foreach (var (baseBone, mergeBone) in pairs)
                {
                    if (baseBone == null || mergeBone == null || !before.TryGetValue(baseBone, out var old))
                        continue;
                    if (
                        baseBone.localPosition == old.Position
                        && baseBone.localRotation == old.Rotation
                        && baseBone.localScale == old.Scale
                    )
                        continue;
                    mergeBone.localPosition = baseBone.localPosition;
                    mergeBone.localRotation = baseBone.localRotation;
                    mergeBone.localScale = baseBone.localScale;
                    moved++;
                }
            }
            if (moved > 0)
                Debug.LogWarning($"[ReFrameCore] ReFrameDeletePass: merge armature bones followed -> {moved}");
        }

        /// <summary>削除対象が 1 つも無いビルドで、[ReFrameBlendTreeOverride(Always = true)] だけを適用する。</summary>
        static void ApplyAlwaysTreeOverrides(BuildContext context, ReFrameDeleteComponent[] components)
        {
            var treeOverrides = ReFrameAnimatorUtil.BuildTreeOverrides(
                components.SelectMany(c => c.EnumerateBlendTreeOverrides()),
                System.Array.Empty<string>()
            );
            if (treeOverrides.Count == 0)
                return;

            var bakedActiveStates = new Dictionary<Transform, bool>();
            var changed = false;
            foreach (var (controller, bakeRoot) in CollectProcessedControllers(context))
                changed |= ReFrameAnimatorUtil.ApplyTreeOverrides(
                    controller,
                    bakeRoot,
                    bakedActiveStates,
                    treeOverrides
                );
            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: always tree overrides ({string.Join(", ", treeOverrides.Keys.Select(k => k.Replace("\n", "/")))}) -> changed={changed}"
            );
        }

        static void ProcessAnimators(
            BuildContext context,
            ReFrameAnimatorUtil.ParameterTarget[] targets,
            ReFrameAnimatorUtil.RelatedBlendTreeTarget[] relatedBlendTreeTargets,
            string[] layerNames,
            (string LayerName, string StateName)[] stateNames,
            Dictionary<string, float> treeOverrides
        )
        {
            var bakedActiveStates = new Dictionary<Transform, bool>();
            var distinctControllers = CollectProcessedControllers(context);

            var changed = false;
            var found = new HashSet<string>();
            foreach (var (controller, bakeRoot) in distinctControllers)
                changed |= ReFrameAnimatorUtil.RemoveParameters(
                    controller,
                    bakeRoot,
                    bakedActiveStates,
                    treeOverrides,
                    found,
                    targets
                );

            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: animator removal across {distinctControllers.Count} controller(s) -> changed={changed}"
            );

            var missing = targets.Select(t => t.Name).Where(n => !found.Contains(n)).Distinct().ToArray();
            if (missing.Length > 0)
                Debug.LogWarning(
                    "[ReFrameCore] ReFrameDeletePass: どのコントローラーにも定義が無いパラメーター = "
                        + string.Join(", ", missing)
                );

            if (treeOverrides.Count > 0)
            {
                var overridden = false;
                foreach (var (controller, bakeRoot) in distinctControllers)
                    overridden |= ReFrameAnimatorUtil.ApplyTreeOverrides(
                        controller,
                        bakeRoot,
                        bakedActiveStates,
                        treeOverrides
                    );
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameDeletePass: tree overrides -> changed={overridden}"
                );
            }

            if (relatedBlendTreeTargets.Length > 0)
            {
                var relatedChanged = false;
                foreach (var (controller, bakeRoot) in distinctControllers)
                    relatedChanged |= ReFrameAnimatorUtil.RemoveNamedBlendTrees(
                        controller,
                        bakeRoot,
                        bakedActiveStates,
                        relatedBlendTreeTargets
                    );
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameDeletePass: related BlendTree removal -> changed={relatedChanged}"
                );
            }

            var prunedChanged = false;
            foreach (var (controller, _) in distinctControllers)
                prunedChanged |= ReFrameAnimatorUtil.PruneEmptyBlendTrees(controller);
            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: empty BlendTree pruning -> changed={prunedChanged}"
            );

            var collapsedChanged = false;
            foreach (var (controller, _) in distinctControllers)
                collapsedChanged |= ReFrameAnimatorUtil.CollapseSingleChildBlendTrees(controller);
            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: single-child BlendTree collapse -> changed={collapsedChanged}"
            );

            if (stateNames.Length > 0)
            {
                var removedStates = 0;
                foreach (var (controller, _) in distinctControllers)
                    removedStates += ReFrameAnimatorUtil.RemoveNamedStates(controller, stateNames);
                Debug.LogWarning(
                    "[ReFrameCore] ReFrameDeletePass: named state removal ("
                        + string.Join(", ", stateNames.Select(s => s.LayerName + "/" + s.StateName))
                        + $") -> removed {removedStates} state(s)"
                );
            }

            var prunedStates = 0;
            foreach (var (controller, _) in distinctControllers)
                prunedStates += ReFrameAnimatorUtil.PruneUnreachableStates(controller);
            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: unreachable state pruning -> removed {prunedStates} state(s)"
            );

            if (layerNames.Length > 0)
            {
                var removedLayers = 0;
                foreach (var (controller, _) in distinctControllers)
                    removedLayers += ReFrameAnimatorUtil.RemoveNamedLayers(controller, layerNames);
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameDeletePass: named layer removal ({string.Join(", ", layerNames)})"
                        + $" -> removed {removedLayers} layer(s)"
                );
            }
        }

        /// <summary>アバター本体の expressionsMenu と、ModularAvatarMenuInstaller の menuToAppend (マージ前のソースメニュー) の両方から、対象パラメーターを参照している コントロールを削除し、空になったサブメニューを掃除する。</summary>
        static int ApplyMenuRemovals(VRCExpressionsMenu baseMenu, ReFrameDeleteComponent[] components)
        {
            var removed = 0;
            foreach (var component in components)
            {
                if (component == null)
                    continue;
                foreach (var removal in component.EnumerateMenuRemovals())
                {
                    var segments = removal.Path.Split('/');
                    var parent = baseMenu;
                    for (var i = 0; i < segments.Length - 1 && parent != null; i++)
                        parent = ReFrameMenuUtil.FindSubMenu(parent, segments[i]);
                    var leafName = segments[segments.Length - 1];
                    var target = parent?.controls.FirstOrDefault(c => c != null && c.name == leafName);
                    if (parent == null || target == null)
                    {
                        Debug.LogWarning(
                            $"[ReFrameCore] ReFrameDeletePass: [ReFrameMenuRemove] path '{removal.Path}' not found in the root menu; skipped."
                        );
                        continue;
                    }
                    foreach (var keepPath in removal.Keep)
                    {
                        if (string.IsNullOrEmpty(keepPath) || target.subMenu == null)
                            continue;
                        var keepSegments = keepPath.Split('/');
                        var menu = target.subMenu;
                        for (var i = 0; i < keepSegments.Length - 1 && menu != null; i++)
                            menu = ReFrameMenuUtil.FindSubMenu(menu, keepSegments[i]);
                        var keep = menu?.controls.FirstOrDefault(c => c != null && c.name == keepSegments[keepSegments.Length - 1]);
                        if (keep == null)
                        {
                            Debug.LogWarning(
                                $"[ReFrameCore] ReFrameDeletePass: [ReFrameMenuRemove] keep '{keepPath}' not found under '{removal.Path}'; skipped."
                            );
                            continue;
                        }
                        if (parent.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS)
                        {
                            Debug.LogWarning(
                                $"[ReFrameCore] ReFrameDeletePass: [ReFrameMenuRemove] '{keepPath}' を移す先のメニューが {VRCExpressionsMenu.MAX_CONTROLS} 項目で埋まっています; skipped."
                            );
                            continue;
                        }
                        parent.controls.Add(
                            new VRCExpressionsMenu.Control
                            {
                                name = keep.name,
                                icon = keep.icon,
                                type = keep.type,
                                parameter = keep.parameter,
                                value = keep.value,
                                style = keep.style,
                                subMenu = keep.subMenu,
                                subParameters = keep.subParameters,
                                labels = keep.labels,
                            }
                        );
                    }
                    parent.controls.Remove(target);
                    removed++;
                    Debug.LogWarning(
                        $"[ReFrameCore] ReFrameDeletePass: [ReFrameMenuRemove] '{removal.Path}' をメニューから外しました"
                            + (removal.Keep.Length > 0 ? $" (残した項目: {string.Join(", ", removal.Keep)})" : "")
                            + "。"
                    );
                }
            }
            return removed;
        }

        static void ProcessMenus(BuildContext context, string[] names, ReFrameDeleteComponent[] components)
        {
            var menuRemoved = 0;
            var pruned = 0;

            var installers = context.AvatarRootObject
                .GetComponentsInChildren<ModularAvatarMenuInstaller>(true)
                .Where(i => i.enabled && i.installTargetMenu != null)
                .ToArray();

            var menuCloneCache = new Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>();

            foreach (var installer in installers)
                ReFrameMenuUtil.CloneMenuTree(installer.installTargetMenu, context.AssetSaver, menuCloneCache);

            var targetHasContent = new Dictionary<VRCExpressionsMenu, bool>();
            foreach (
                var installer in context.AvatarRootObject.GetComponentsInChildren<ModularAvatarMenuInstaller>(
                    true
                )
            )
            {
                if (!installer.enabled || installer.menuToAppend == null)
                    continue;

                var clone = ReFrameMenuUtil.CloneMenuTree(installer.menuToAppend, context.AssetSaver);
                if (!ReferenceEquals(clone, installer.menuToAppend))
                    installer.menuToAppend = clone;

                menuRemoved += ReFrameMenuUtil.RemoveControlsByParameter(clone, names);
                pruned += ReFrameMenuUtil.PruneEmptySubMenus(clone);

                if (
                    installer.installTargetMenu != null
                    && menuCloneCache.TryGetValue(installer.installTargetMenu, out var targetClone)
                )
                {
                    targetHasContent.TryGetValue(targetClone, out var alreadyHasContent);
                    targetHasContent[targetClone] = alreadyHasContent || clone.controls.Count > 0;
                }
            }

            var baseMenu = ReFrameMenuUtil.ReplaceMenuWithClone(context, menuCloneCache);
            if (baseMenu != null)
            {
                var protectedClones = new HashSet<VRCExpressionsMenu>();
                foreach (var installer in installers)
                {
                    if (!menuCloneCache.TryGetValue(installer.installTargetMenu, out var cloned))
                        continue;
                    if (!ReferenceEquals(installer.installTargetMenu, cloned))
                    {
                        installer.installTargetMenu = cloned;
                        EditorUtility.SetDirty(installer);
                    }

                    if (targetHasContent.TryGetValue(cloned, out var hasContent) && hasContent)
                        protectedClones.Add(cloned);
                }
                menuRemoved += ReFrameMenuUtil.RemoveControlsByParameter(baseMenu, names);
                menuRemoved += ApplyMenuRemovals(baseMenu, components);
                pruned += ReFrameMenuUtil.PruneEmptySubMenus(baseMenu, protectedClones);
            }

            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: menu removal -> removed {menuRemoved} control(s), pruned {pruned} entr(y/ies)"
            );
        }

        /// <summary>アバター上の ModularAvatarParameters コンポーネントから、対象パラメーターの 宣言をまとめて取り除く。</summary>
        static void ProcessModularAvatarParameters(BuildContext context, string[] names)
        {
            var nameSet = new HashSet<string>(names);
            var removed = 0;

            foreach (var maParams in context.AvatarRootObject.GetComponentsInChildren<ModularAvatarParameters>(true))
            {
                if (maParams.parameters == null)
                    continue;
                removed += maParams.parameters.RemoveAll(p => nameSet.Contains(p.nameOrPrefix));
            }

            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: ModularAvatarParameters removal -> removed {removed} entr(y/ies)"
            );
        }

        /// <summary>アバターの expressionParameters を取得する。</summary>
        static void DeduplicateExpressionParameters(BuildContext context)
        {
            var descriptor = context.AvatarDescriptor;
            var source = descriptor == null ? null : descriptor.expressionParameters;
            if (source?.parameters == null || source.parameters.Length == 0)
                return;

            var names = new HashSet<string>();
            var hasDuplicate = source.parameters.Any(p =>
                p != null && !string.IsNullOrEmpty(p.name) && !names.Add(p.name)
            );
            if (!hasDuplicate)
                return;

            var parameters = GetOrCloneExpressionParameters(context);
            if (parameters?.parameters == null)
                return;

            var animatorTypes = CollectAnimatorParameterTypes(context);
            var kept = new List<VRCExpressionParameters.Parameter>();
            var indexByName = new Dictionary<string, int>();
            var removed = 0;

            foreach (var parameter in parameters.parameters)
            {
                if (parameter == null || string.IsNullOrEmpty(parameter.name))
                {
                    kept.Add(parameter);
                    continue;
                }
                if (!indexByName.TryGetValue(parameter.name, out var index))
                {
                    indexByName[parameter.name] = kept.Count;
                    kept.Add(parameter);
                    continue;
                }

                var incumbent = kept[index];
                var winner = PreferByAnimatorType(incumbent, parameter, animatorTypes);
                var loser = ReferenceEquals(winner, incumbent) ? parameter : incumbent;
                kept[index] = winner;
                removed++;
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameDeletePass: duplicate expression parameter '{parameter.name}' — "
                        + $"kept {winner.valueType} ({BitCost(winner)}bit), dropped {loser.valueType} ({BitCost(loser)}bit)."
                );
            }

            if (removed == 0)
                return;
            parameters.parameters = kept.ToArray();
            EditorUtility.SetDirty(parameters);
        }

        /// <summary>アバター上の全 AnimatorController が宣言しているパラメーターの型。</summary>
        static Dictionary<string, AnimatorControllerParameterType> CollectAnimatorParameterTypes(
            BuildContext context
        )
        {
            var result = new Dictionary<string, AnimatorControllerParameterType>();
            var vcc = context.Extension<AnimatorServicesContext>().ControllerContext;
            foreach (var controller in vcc.Controllers.Values.Where(c => c != null).Distinct())
            foreach (var pair in controller.Parameters)
                if (!result.ContainsKey(pair.Key))
                    result[pair.Key] = pair.Value.type;
            return result;
        }

        /// <summary>重複した 2 つのうち、AnimatorController の型に一致する方を選ぶ。</summary>
        static VRCExpressionParameters.Parameter PreferByAnimatorType(
            VRCExpressionParameters.Parameter incumbent,
            VRCExpressionParameters.Parameter challenger,
            IReadOnlyDictionary<string, AnimatorControllerParameterType> animatorTypes
        )
        {
            if (!animatorTypes.TryGetValue(incumbent.name, out var wanted))
                return incumbent;
            var incumbentMatches = Matches(incumbent.valueType, wanted);
            var challengerMatches = Matches(challenger.valueType, wanted);
            if (challengerMatches && !incumbentMatches)
                return challenger;
            return incumbent;
        }

        static bool Matches(
            VRCExpressionParameters.ValueType valueType,
            AnimatorControllerParameterType animatorType
        )
        {
            switch (valueType)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    return animatorType == AnimatorControllerParameterType.Bool
                        || animatorType == AnimatorControllerParameterType.Trigger;
                case VRCExpressionParameters.ValueType.Int:
                    return animatorType == AnimatorControllerParameterType.Int;
                case VRCExpressionParameters.ValueType.Float:
                    return animatorType == AnimatorControllerParameterType.Float;
                default:
                    return false;
            }
        }

        static int BitCost(VRCExpressionParameters.Parameter parameter) =>
            !parameter.networkSynced ? 0
            : parameter.valueType == VRCExpressionParameters.ValueType.Bool ? 1
            : 8;

        static VRCExpressionParameters GetOrCloneExpressionParameters(BuildContext context)
        {
            var descriptor = context.AvatarDescriptor;
            if (descriptor == null || descriptor.expressionParameters == null)
                return null;

            var source = descriptor.expressionParameters;
            if (context.IsTemporaryAsset(source))
                return source;

            var clone = Object.Instantiate(source);
            clone.name = source.name;
            context.AssetSaver.SaveAsset(clone);
            descriptor.expressionParameters = clone;
            EditorUtility.SetDirty(descriptor);
            return clone;
        }
    }
}
