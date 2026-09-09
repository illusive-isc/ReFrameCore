using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor.HarmonyPatches
{
    /// <summary>VRCQuestTools が出す Avatar Dynamics の一覧から、ReFrame がビルド時に消すぶんを外す。</summary>
    internal static class VRCQuestToolsDynamicsListPatch
    {
        const string TargetType = "KRT.VRCQuestTools.Models.VRChat.VRChatAvatar";

        static readonly string[] TargetMethods =
        {
            "GetPhysBones",
            "GetPhysBoneColliders",
            "GetContacts",
        };

        /// <summary>戻り値が Component[] ではないもの。</summary>
        static readonly string[] ContactMethods = { "GetNonLocalContacts", "GetLocalContactReceivers" };

        static PropertyInfo _descriptorProperty;

        internal static void Patch(Harmony harmony)
        {
            var type = FindType(TargetType);
            if (type == null)
                return;

            _descriptorProperty = AccessTools.Property(type, "AvatarDescriptor");
            if (_descriptorProperty == null)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] {TargetType}.AvatarDescriptor が見つかりません。"
                        + "VRCQuestTools の揺れ物一覧は素のままになります。"
                );
                return;
            }

            var postfix = new HarmonyMethod(
                typeof(VRCQuestToolsDynamicsListPatch).GetMethod(
                    nameof(Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic
                )
            );
            foreach (var name in TargetMethods)
            {
                var target = AccessTools.Method(type, name);
                if (target == null)
                {
                    Debug.LogWarning(
                        $"[ReFrameCore] {TargetType}.{name} が見つかりません。"
                            + "VRCQuestTools の揺れ物一覧は素のままになります。"
                    );
                    continue;
                }
                harmony.Patch(target, postfix: postfix);
            }

            var contactPostfix = new HarmonyMethod(
                typeof(VRCQuestToolsDynamicsListPatch).GetMethod(
                    nameof(ContactPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic
                )
            );
            foreach (var name in ContactMethods)
            {
                var target = AccessTools.Method(type, name);
                if (target == null)
                    continue;
                harmony.Patch(target, postfix: contactPostfix);
            }
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

        /// <summary>ReFrame が消すもの・プレビューの影武者を一覧から落とす。</summary>
        static void Postfix(object __instance, ref Component[] __result)
        {
            if (__result == null || __result.Length == 0)
                return;
            var filtered = Filter(__instance, __result);
            if (filtered != null)
                __result = filtered.ToArray();
        }

        /// <summary>残すものだけを返す。</summary>
        static List<Component> Filter(object instance, Component[] source)
        {
            if (instance == null)
                return null;

            var descriptor = _descriptorProperty.GetValue(instance) as Component;
            if (descriptor == null)
                return null;
            var root = descriptor.transform;
            if (!IsHandledByReFrame(root))
                return null;

            var doomed = ReFrameDeleteComponent.CollectDeclaredDeletions(root);
            var deleted = new HashSet<string>();
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component == null)
                    continue;
                if (component.deletedPhysBones != null)
                    deleted.UnionWith(component.deletedPhysBones);
                if (component.deletedPhysBoneColliders != null)
                    deleted.UnionWith(component.deletedPhysBoneColliders);
            }

            var kept = new List<Component>(source.Length);
            foreach (var component in source)
            {
                if (component == null)
                    continue;

                if (ReFrameDeleteComponent.IsPreviewShadow(component.transform))
                    continue;

                if (doomed.Contains(component.transform))
                    continue;

                if (
                    deleted.Count > 0
                    && ReFramePhysBoneCatalog.IsSelected(
                        deleted,
                        ReFramePhysBoneCatalog.KeyOf(root, component)
                    )
                )
                    continue;
                kept.Add(component);
            }
            return kept.Count == source.Length ? null : kept;
        }

        /// <summary>ContactBase[] を返すもの用。</summary>
        static void ContactPostfix(object __instance, ref VRC.Dynamics.ContactBase[] __result)
        {
            if (__result == null || __result.Length == 0)
                return;
            var filtered = Filter(__instance, __result);
            if (filtered == null)
                return;
            var typed = new VRC.Dynamics.ContactBase[filtered.Count];
            for (var i = 0; i < filtered.Count; i++)
                typed[i] = (VRC.Dynamics.ContactBase)filtered[i];
            __result = typed;
        }

        /// <summary>そのアバターに「Quest 簡易対応」が ON の ReFrame コンポーネントがあるか。</summary>
        static bool IsHandledByReFrame(Transform avatarRoot)
        {
            foreach (var component in ReFrameDeleteComponent.ActiveIn(avatarRoot))
                if (component != null && component.QuestConversionActive)
                    return true;
            return false;
        }
    }
}
