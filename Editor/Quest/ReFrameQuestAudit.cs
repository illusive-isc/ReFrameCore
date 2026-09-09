using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(
    "jp.illusive_isc.ReFrameCoreHarmonyPatches"
)]

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>ビルド結果のアバターを Quest (Android) の基準で突き合わせ、 「アップロードできなくなるもの」と「表示ランクが下がるだけのもの」を分けて並べる。</summary>
    internal static class ReFrameQuestAudit
    {
        /// <summary>1 項目ぶんの判定結果。</summary>
        internal readonly struct Finding
        {
            /// <summary>true ならアップロードが弾かれる。</summary>
            public readonly bool Blocking;
            public readonly string Label;
            public readonly string Value;
            public readonly string Limit;

            public Finding(bool blocking, string label, string value, string limit)
            {
                Blocking = blocking;
                Label = label;
                Value = value;
                Limit = limit;
            }
        }

        /// <summary>Quest でこれらが VeryPoor になると Error になるカテゴリ (SDKPerformanceDisplay.cs の isMobilePlatform ? Error : Warning と一致)。</summary>
        static readonly (AvatarPerformanceCategory Category, string Label)[] BlockingCategories =
        {
            (AvatarPerformanceCategory.PhysBoneComponentCount, "PhysBone の数"),
            (AvatarPerformanceCategory.PhysBoneTransformCount, "PhysBone が動かすボーン数"),
            (AvatarPerformanceCategory.PhysBoneColliderCount, "PhysBone コライダー数"),
            (AvatarPerformanceCategory.PhysBoneCollisionCheckCount, "PhysBone の当たり判定回数"),
            (AvatarPerformanceCategory.ContactCount, "Contact の数"),
            (AvatarPerformanceCategory.ConstraintsCount, "Constraint の数"),
            (AvatarPerformanceCategory.ConstraintDepth, "Constraint の入れ子の深さ"),
            (AvatarPerformanceCategory.RaycastCount, "VRCRaycast の数"),
        };

        /// <summary>超過してもアップロードは通るカテゴリ (表示ランクだけ下がる)。</summary>
        static readonly (AvatarPerformanceCategory Category, string Label)[] RankOnlyCategories =
        {
            (AvatarPerformanceCategory.PolyCount, "ポリゴン数"),
            (AvatarPerformanceCategory.MaterialCount, "マテリアル数"),
            (AvatarPerformanceCategory.MeshCount, "メッシュ数"),
            (AvatarPerformanceCategory.SkinnedMeshCount, "スキンメッシュ数"),
            (AvatarPerformanceCategory.BoneCount, "ボーン数"),
            (AvatarPerformanceCategory.ParticleSystemCount, "パーティクルの系統数"),
            (AvatarPerformanceCategory.ParticleTotalCount, "パーティクルの最大数"),
            (AvatarPerformanceCategory.TextureMegabytes, "テクスチャ容量 (MB)"),
        };

        /// <summary>許可リストのうち #if UNITY_STANDALONE で囲まれていて、Android では消えるもの。</summary>
        static readonly string[] StandaloneOnlyComponents =
        {
            "DynamicBone",
            "DynamicBoneCollider",
            "UnityEngine.Cloth",
            "UnityEngine.Light",
            "UnityEngine.BoxCollider",
            "UnityEngine.SphereCollider",
            "UnityEngine.CapsuleCollider",
            "UnityEngine.Rigidbody",
            "UnityEngine.Joint",
            "UnityEngine.Animations.AimConstraint",
            "UnityEngine.Animations.LookAtConstraint",
            "UnityEngine.Animations.ParentConstraint",
            "UnityEngine.Animations.PositionConstraint",
            "UnityEngine.Animations.RotationConstraint",
            "UnityEngine.Animations.ScaleConstraint",
            "UnityEngine.Camera",
            "UnityEngine.AudioSource",
            "ONSPAudioSource",
            "VRCSDK2.VRC_SpatialAudioSource",
            "VRC.SDK3.Avatars.Components.VRCSpatialAudioSource",
        };

        /// <summary>Android で許可されるコンポーネント名 (SDK の一覧から PC 限定分を引いたもの)。</summary>
        static HashSet<string> AndroidAllowedComponents()
        {
            var allowed = new HashSet<string>(
                VRC.SDKBase.Validation.AvatarValidation.ComponentTypeWhiteListCommon.Concat(
                    VRC.SDKBase.Validation.AvatarValidation.ComponentTypeWhiteListSdk3
                )
            );
            allowed.ExceptWith(StandaloneOnlyComponents);

            allowed.RemoveWhere(n => n.StartsWith("RootMotion.FinalIK."));
            return allowed;
        }

        /// <summary>アバターを Quest 基準で監査する。</summary>
        internal static List<Finding> Audit(GameObject builtAvatar, bool questConversionEnabled)
        {
            var findings = new List<Finding>();
            if (builtAvatar == null)
                return findings;

            var stats = new AvatarPerformanceStats(true);

            try
            {
                AvatarPerformance.CalculatePerformanceStats(builtAvatar.name, builtAvatar, stats, true);
            }
            catch (System.NullReferenceException)
            {
                return null;
            }
            var poor = AvatarPerformanceStats.GetStatLevelForRating(PerformanceRating.Poor, true);

            var allowed = AndroidAllowedComponents();
            var illegalComponents = new Dictionary<string, int>();
            foreach (var component in builtAvatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null || IsEditorOnly(component))
                    continue;

                if (ReFrameDeleteComponent.WillBeResolvedAtBuild(component, questConversionEnabled))
                    continue;
                var name = component.GetType().FullName;
                if (name == null || allowed.Contains(name))
                    continue;
                illegalComponents.TryGetValue(name, out var count);
                illegalComponents[name] = count + 1;
            }
            foreach (var pair in illegalComponents.OrderByDescending(p => p.Value))
                findings.Add(
                    new Finding(true, "使えないコンポーネント: " + Short(pair.Key), pair.Value + " 個", "0")
                );

            var doomed = ReFrameDeleteComponent.CollectDeclaredDeletions(builtAvatar.transform);
            var dynamics = CountDynamics(builtAvatar.transform, doomed);

            foreach (var entry in BlockingCategories)
            {

                var counted = dynamics.TryGetValue(entry.Category, out var value);
                if (!counted)
                {
                    if (stats.GetPerformanceRatingForCategory(entry.Category) != PerformanceRating.VeryPoor)
                        continue;
                    findings.Add(
                        new Finding(true, entry.Label, ValueOf(stats, entry.Category), ValueOf(poor, entry.Category))
                    );
                    continue;
                }

                var limitText = ValueOf(poor, entry.Category);
                if (!int.TryParse(limitText, out var limit) || value <= limit)
                    continue;
                findings.Add(new Finding(true, entry.Label, value.ToString(), limitText));
            }

            var illegalShaders = new Dictionary<string, int>();
            foreach (var shader in questConversionEnabled
                ? System.Array.Empty<Shader>()
                : VRC.SDK3.Validation.AvatarValidation.FindIllegalShaders(builtAvatar))
            {
                if (shader == null)
                    continue;
                illegalShaders.TryGetValue(shader.name, out var count);
                illegalShaders[shader.name] = count + 1;
            }
            foreach (var pair in illegalShaders.OrderByDescending(p => p.Value))
                findings.Add(
                    new Finding(true, "使えないシェーダー: " + pair.Key, pair.Value + " マテリアル", "0")
                );

            foreach (var entry in RankOnlyCategories)
            {
                var rating = stats.GetPerformanceRatingForCategory(entry.Category);
                if (rating != PerformanceRating.VeryPoor)
                    continue;
                findings.Add(
                    new Finding(
                        false,
                        entry.Label,
                        ValueOf(stats, entry.Category),
                        ValueOf(poor, entry.Category)
                    )
                );
            }

            return findings;
        }

        /// <summary>VRChat の判定から除外されるコンポーネントか (IEditorOnly / INDMFEditorOnly)。</summary>
        static bool IsEditorOnly(Component component)
        {
            if (component is VRC.SDKBase.IEditorOnly)
                return true;

            foreach (var i in component.GetType().GetInterfaces())
            {
                if (i.FullName == "nadena.dev.ndmf.INDMFEditorOnly")
                    return true;
            }
            return false;
        }

        /// <summary>ログ用にコンポーネント名の名前空間を落とす。</summary>
        static string Short(string fullName)
        {
            var dot = fullName.LastIndexOf('.');
            return dot < 0 ? fullName : fullName.Substring(dot + 1);
        }

        /// <summary>Avatar Dynamics の数を、削除宣言されたものを除いて自前で数える。</summary>
        internal static Dictionary<AvatarPerformanceCategory, int> SceneDynamics(Transform avatarRoot)
        {
            if (avatarRoot == null)
                return new Dictionary<AvatarPerformanceCategory, int>();
            return CountDynamics(
                avatarRoot,
                ReFrameDeleteComponent.CollectDeclaredDeletions(avatarRoot)
            );
        }

        static Dictionary<AvatarPerformanceCategory, int> CountDynamics(
            Transform avatarRoot,
            HashSet<Transform> doomed
        )
        {
            var deletedPhysBones = new HashSet<string>();
            var deletedColliders = new HashSet<string>();
            foreach (var c in ReFrameDeleteComponent.ActiveIn(avatarRoot))
            {
                if (c == null)
                    continue;
                if (c.deletedPhysBones != null)
                    deletedPhysBones.UnionWith(c.deletedPhysBones);
                if (c.deletedPhysBoneColliders != null)
                    deletedColliders.UnionWith(c.deletedPhysBoneColliders);
            }

            bool Gone(Component component) => component == null || doomed.Contains(component.transform);

            var usedBones = new HashSet<Transform>();
            foreach (var smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (Gone(smr))
                    continue;
                if (smr.rootBone != null)
                    usedBones.Add(smr.rootBone);
                if (smr.bones == null)
                    continue;
                foreach (var bone in smr.bones)
                    if (bone != null)
                        usedBones.Add(bone);
            }

            bool CarriesRealComponent(Transform t)
            {
                foreach (var component in t.GetComponents<Component>())
                {
                    if (component == null || component is Transform)
                        continue;
                    if (component is VRC.Dynamics.VRCPhysBoneBase
                        || component is VRC.Dynamics.VRCPhysBoneColliderBase
                        || component is VRC.Dynamics.VRCConstraintBase
                        || component is UnityEngine.Animations.IConstraint)
                        continue;
                    return true;
                }
                return false;
            }

            List<Transform> AffectedTransforms(VRC.Dynamics.VRCPhysBoneBase physBone)
            {
                var ignores = new HashSet<Transform>();
                if (physBone.ignoreTransforms != null)
                    foreach (var t in physBone.ignoreTransforms)
                        if (t != null)
                            ignores.Add(t);
                var root = physBone.rootTransform != null ? physBone.rootTransform : physBone.transform;
                var result = new List<Transform>();
                var queue = new Queue<Transform>();
                queue.Enqueue(root);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    result.Add(current);
                    if (current != root && ignores.Contains(current))
                        continue;
                    for (var i = 0; i < current.childCount; i++)
                    {
                        var child = current.GetChild(i);

                        if (!ignores.Contains(child) && !ReFrameDeleteComponent.IsPreviewShadow(child))
                            queue.Enqueue(child);
                    }
                }
                return result;
            }

            bool HasWork(VRC.Dynamics.VRCPhysBoneBase physBone)
            {
                foreach (var t in AffectedTransforms(physBone))
                    if (usedBones.Contains(t) || CarriesRealComponent(t))
                        return true;
                return false;
            }

            var liveColliders = new HashSet<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider>();
            foreach (var collider in avatarRoot.GetComponentsInChildren<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider>(true))
            {
                if (Gone(collider)
                    || ReFramePhysBoneCatalog.IsSelected(
                        deletedColliders,
                        ReFramePhysBoneCatalog.KeyOf(avatarRoot, collider)
                    ))
                    continue;
                liveColliders.Add(collider);
            }

            var physBoneCount = 0;
            var transformCount = 0;
            var collisionCheckCount = 0;
            foreach (var physBone in avatarRoot.GetComponentsInChildren<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>(true))
            {
                if (Gone(physBone)
                    || ReFramePhysBoneCatalog.IsSelected(
                        deletedPhysBones,
                        ReFramePhysBoneCatalog.KeyOf(avatarRoot, physBone)
                    ))
                    continue;
                if (!HasWork(physBone))
                    continue;
                physBoneCount++;
                var affected = ReFramePhysBoneCatalog.CountAffectedBones(physBone);
                transformCount += affected;
                if (physBone.colliders == null)
                    continue;
                foreach (var collider in physBone.colliders)
                    if (collider is VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider typed
                        && liveColliders.Contains(typed))
                        collisionCheckCount += affected;
            }

            var contactCount = 0;
            foreach (var contact in avatarRoot.GetComponentsInChildren<VRC.Dynamics.ContactBase>(true))
                if (!Gone(contact))
                    contactCount++;

            var liveConstraints = new List<Component>();
            var unconverted = 0;
            foreach (var constraint in avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (Gone(constraint) || ReFrameDeleteComponent.IsPreviewShadow(constraint.transform))
                    continue;
                if (!(constraint is VRC.Dynamics.VRCConstraintBase)
                    && !(constraint is UnityEngine.Animations.IConstraint))
                    continue;
                liveConstraints.Add(constraint);

                if (constraint is UnityEngine.Animations.IConstraint
                    && !ReFrameDeleteComponent.WillBeResolvedAtBuild(constraint, false))
                    unconverted++;
            }

            var raycastCount = 0;
            foreach (var raycast in avatarRoot.GetComponentsInChildren<VRC.SDK3.Avatars.Components.VRCRaycast>(true))
                if (!Gone(raycast))
                    raycastCount++;

            return new Dictionary<AvatarPerformanceCategory, int>
            {
                { AvatarPerformanceCategory.PhysBoneComponentCount, physBoneCount },
                { AvatarPerformanceCategory.PhysBoneTransformCount, transformCount },
                { AvatarPerformanceCategory.PhysBoneColliderCount, liveColliders.Count },
                { AvatarPerformanceCategory.PhysBoneCollisionCheckCount, collisionCheckCount },
                { AvatarPerformanceCategory.ContactCount, contactCount },
                { AvatarPerformanceCategory.ConstraintsCount, liveConstraints.Count },
                { AvatarPerformanceCategory.ConstraintDepth, ConstraintDepth(avatarRoot, liveConstraints, unconverted) },
                { AvatarPerformanceCategory.RaycastCount, raycastCount },
            };
        }

        /// <summary>この Constraint が実際に動かす Transform (VRChat 製は指定できる)。</summary>
        static Transform DrivenBy(Component constraint)
        {
            if (constraint is VRC.Dynamics.VRCConstraintBase vrc && vrc.TargetTransform != null)
                return vrc.TargetTransform;
            return constraint.transform;
        }

        /// <summary>この Constraint が参照する Transform。</summary>
        static IEnumerable<Transform> SourcesOf(Component constraint)
        {
            if (constraint is UnityEngine.Animations.IConstraint unity)
            {
                for (var i = 0; i < unity.sourceCount; i++)
                    yield return unity.GetSource(i).sourceTransform;
                yield break;
            }
            if (constraint is VRC.Dynamics.VRCConstraintBase vrc)
                for (var i = 0; i < vrc.Sources.Count; i++)
                    yield return vrc.Sources[i].SourceTransform;
        }

        /// <summary>Constraint の入れ子の深さ。</summary>
        static int ConstraintDepth(
            Transform avatarRoot,
            List<Component> constraints,
            int unconvertedEngineConstraints
        )
        {

            var driverOf = new Dictionary<Transform, List<int>>();
            for (var i = 0; i < constraints.Count; i++)
            {
                var driven = DrivenBy(constraints[i]);
                if (driven == null)
                    continue;
                if (!driverOf.TryGetValue(driven, out var list))
                    driverOf[driven] = list = new List<int>();
                list.Add(i);
            }

            List<int> DriversAbove(Transform t, bool includeSelf)
            {
                var cursor = includeSelf ? t : (t == null ? null : t.parent);
                while (cursor != null)
                {
                    if (driverOf.TryGetValue(cursor, out var list))
                        return list;
                    if (cursor == avatarRoot)
                        break;
                    cursor = cursor.parent;
                }
                return null;
            }

            var dependencies = new List<int>[constraints.Count];
            for (var i = 0; i < constraints.Count; i++)
            {
                var deps = new List<int>();
                var self = i;
                Action<List<int>> take = found =>
                {
                    if (found == null)
                        return;
                    foreach (var index in found)
                        if (index != self && !deps.Contains(index))
                            deps.Add(index);
                };
                take(DriversAbove(DrivenBy(constraints[i]), false));
                foreach (var source in SourcesOf(constraints[i]))
                    if (source != null)
                        take(DriversAbove(source, true));
                dependencies[i] = deps;
            }

            var depth = new int[constraints.Count];
            var state = new byte[constraints.Count];
            Func<int, int> resolve = null;
            resolve = i =>
            {
                if (state[i] == 2)
                    return depth[i];
                if (state[i] == 1)
                    return 1;
                state[i] = 1;
                var best = 0;
                foreach (var dep in dependencies[i])
                {
                    var d = resolve(dep);
                    if (d > best)
                        best = d;
                }
                depth[i] = best + 1;
                state[i] = 2;
                return depth[i];
            };

            var deepest = unconvertedEngineConstraints;
            for (var i = 0; i < constraints.Count; i++)
            {
                var d = resolve(i);
                if (d > deepest)
                    deepest = d;
            }
            return deepest;
        }

        /// <summary>閾値側 (AvatarPerformanceStatsLevel) から同じカテゴリの値を取り出す。</summary>
        static string ValueOf(AvatarPerformanceStatsLevel level, AvatarPerformanceCategory category)
        {
            switch (category)
            {
                case AvatarPerformanceCategory.PhysBoneComponentCount:
                    return level.physBone.componentCount.ToString();
                case AvatarPerformanceCategory.PhysBoneTransformCount:
                    return level.physBone.transformCount.ToString();
                case AvatarPerformanceCategory.PhysBoneColliderCount:
                    return level.physBone.colliderCount.ToString();
                case AvatarPerformanceCategory.PhysBoneCollisionCheckCount:
                    return level.physBone.collisionCheckCount.ToString();
                case AvatarPerformanceCategory.ContactCount:
                    return level.contactCount.ToString();
                case AvatarPerformanceCategory.ConstraintsCount:
                    return level.constraintsCount.ToString();
                case AvatarPerformanceCategory.ConstraintDepth:
                    return level.constraintDepth.ToString();
                case AvatarPerformanceCategory.RaycastCount:
                    return level.raycastCount.ToString();
                case AvatarPerformanceCategory.PolyCount:
                    return level.polyCount.ToString();
                case AvatarPerformanceCategory.MaterialCount:
                    return level.materialCount.ToString();
                case AvatarPerformanceCategory.MeshCount:
                    return level.meshCount.ToString();
                case AvatarPerformanceCategory.SkinnedMeshCount:
                    return level.skinnedMeshCount.ToString();
                case AvatarPerformanceCategory.BoneCount:
                    return level.boneCount.ToString();
                case AvatarPerformanceCategory.ParticleSystemCount:
                    return level.particleSystemCount.ToString();
                case AvatarPerformanceCategory.ParticleTotalCount:
                    return level.particleTotalCount.ToString();
                case AvatarPerformanceCategory.TextureMegabytes:
                    return level.textureMegabytes.ToString();
                default:
                    return "-";
            }
        }

        static string ValueOf(AvatarPerformanceStats stats, AvatarPerformanceCategory category)
        {
            switch (category)
            {
                case AvatarPerformanceCategory.PhysBoneComponentCount:
                    return (stats.physBone?.componentCount ?? 0).ToString();
                case AvatarPerformanceCategory.PhysBoneTransformCount:
                    return (stats.physBone?.transformCount ?? 0).ToString();
                case AvatarPerformanceCategory.PhysBoneColliderCount:
                    return (stats.physBone?.colliderCount ?? 0).ToString();
                case AvatarPerformanceCategory.PhysBoneCollisionCheckCount:
                    return (stats.physBone?.collisionCheckCount ?? 0).ToString();
                case AvatarPerformanceCategory.ContactCount:
                    return stats.contactCount.ToString();
                case AvatarPerformanceCategory.ConstraintsCount:
                    return stats.constraintsCount.ToString();
                case AvatarPerformanceCategory.ConstraintDepth:
                    return stats.constraintDepth.ToString();
                case AvatarPerformanceCategory.RaycastCount:
                    return stats.raycastCount.ToString();
                case AvatarPerformanceCategory.PolyCount:
                    return stats.polyCount.ToString();
                case AvatarPerformanceCategory.MaterialCount:
                    return stats.materialCount.ToString();
                case AvatarPerformanceCategory.MeshCount:
                    return stats.meshCount.ToString();
                case AvatarPerformanceCategory.SkinnedMeshCount:
                    return stats.skinnedMeshCount.ToString();
                case AvatarPerformanceCategory.BoneCount:
                    return stats.boneCount.ToString();
                case AvatarPerformanceCategory.ParticleSystemCount:
                    return stats.particleSystemCount.ToString();
                case AvatarPerformanceCategory.ParticleTotalCount:
                    return stats.particleTotalCount.ToString();
                case AvatarPerformanceCategory.TextureMegabytes:
                    return stats.textureMegabytes.ToString();
                default:
                    return "-";
            }
        }

    }
}
