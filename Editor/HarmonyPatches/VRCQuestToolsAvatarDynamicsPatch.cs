using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Validation.Performance;

namespace jp.illusive_isc.ReFrame.Core.Editor.HarmonyPatches
{
    /// <summary>VRCQuestTools の「推定パフォーマンスランク」に出る Avatar Dynamics の数を、 ReFrame がビルド時に削るぶんを引いた数に差し替える。</summary>
    internal static class VRCQuestToolsAvatarDynamicsPatch
    {
        const string TargetType = "KRT.VRCQuestTools.Models.VRChat.AvatarDynamics";
        const string TargetMethod = "CalculatePerformanceStats";

        /// <summary>VRCQuestTools 側の集計結果 (内部クラス) のフィールド。</summary>
        static FieldInfo _physBones;
        static FieldInfo _transforms;
        static FieldInfo _colliders;
        static FieldInfo _collisionChecks;
        static FieldInfo _contacts;

        internal static void Patch(Harmony harmony)
        {
            var type = FindType(TargetType);
            if (type == null)
                return;

            var target = AccessTools.Method(type, TargetMethod);
            if (target == null)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] {TargetType}.{TargetMethod} が見つかりません。"
                        + "VRCQuestTools の推定パフォーマンスランクは素のままになります。"
                );
                return;
            }

            var stats = target.ReturnType;
            _physBones = AccessTools.Field(stats, "PhysBonesCount");
            _transforms = AccessTools.Field(stats, "PhysBonesTransformCount");
            _colliders = AccessTools.Field(stats, "PhysBonesColliderCount");
            _collisionChecks = AccessTools.Field(stats, "PhysBonesCollisionCheckCount");
            _contacts = AccessTools.Field(stats, "ContactsCount");
            if (
                _physBones == null
                || _transforms == null
                || _colliders == null
                || _collisionChecks == null
                || _contacts == null
            )
            {
                Debug.LogWarning(
                    "[ReFrameCore] VRCQuestTools の Avatar Dynamics 集計の形が変わっています。"
                        + "推定パフォーマンスランクは素のままになります。"
                );
                return;
            }

            harmony.Patch(
                target,
                postfix: new HarmonyMethod(
                    typeof(VRCQuestToolsAvatarDynamicsPatch).GetMethod(
                        nameof(Postfix),
                        BindingFlags.Static | BindingFlags.NonPublic
                    )
                )
            );
        }

        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null)
                    return type;
            }
            return null;
        }

        /// <summary>ReFrame の「Quest 簡易対応」が ON のアバターなら、ReFrame の数え方で上書きする。</summary>
        static void Postfix(GameObject root, object __result)
        {
            if (__result == null || root == null)
                return;
            var descriptor = root.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null || !IsHandledByReFrame(descriptor.gameObject))
                return;

            var counts = ReFrameQuestAudit.SceneDynamics(descriptor.transform);
            Apply(__result, _physBones, counts, AvatarPerformanceCategory.PhysBoneComponentCount);
            Apply(__result, _transforms, counts, AvatarPerformanceCategory.PhysBoneTransformCount);
            Apply(__result, _colliders, counts, AvatarPerformanceCategory.PhysBoneColliderCount);
            Apply(
                __result,
                _collisionChecks,
                counts,
                AvatarPerformanceCategory.PhysBoneCollisionCheckCount
            );
            Apply(__result, _contacts, counts, AvatarPerformanceCategory.ContactCount);
        }

        static void Apply(
            object target,
            FieldInfo field,
            System.Collections.Generic.Dictionary<AvatarPerformanceCategory, int> counts,
            AvatarPerformanceCategory category
        )
        {
            if (field != null && counts.TryGetValue(category, out var value))
                field.SetValue(target, value);
        }

        /// <summary>そのアバターに「Quest 簡易対応」が ON の ReFrame コンポーネントがあるか。</summary>
        static bool IsHandledByReFrame(GameObject avatarRoot)
        {
            foreach (var component in ReFrameDeleteComponent.ActiveIn(avatarRoot))
                if (component != null && component.QuestConversionActive)
                    return true;
            return false;
        }
    }
}
