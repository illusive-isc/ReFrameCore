using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor.HarmonyPatches
{
    /// <summary>VRCQuestTools の「Quest 非対応コンポーネントがあります」という警告から、 ReFrame がビルド時に取り除くぶんを差し引く。</summary>
    internal static class VRCQuestToolsUnsupportedComponentsPatch
    {
        const string TargetType = "KRT.VRCQuestTools.Models.ComponentRemover";
        const string TargetMethod = "GetUnsupportedComponentsInChildren";

        internal static void Patch(Harmony harmony)
        {
            var type = FindType(TargetType);
            if (type == null)
                return;

            var target = type.GetMethod(
                TargetMethod,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(GameObject), typeof(bool) },
                null
            );
            if (target == null)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] {TargetType}.{TargetMethod} が見つかりません。"
                        + "VRCQuestTools の警告表示は素のままになります。"
                );
                return;
            }

            harmony.Patch(
                target,
                postfix: new HarmonyMethod(
                    typeof(VRCQuestToolsUnsupportedComponentsPatch).GetMethod(
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

        /// <summary>ReFrame の「Quest 対応」が ON のアバターなら、結果を空にする。</summary>
        static void Postfix(GameObject gameObject, ref Component[] __result)
        {
            if (__result == null || __result.Length == 0 || gameObject == null)
                return;
            if (!IsHandledByReFrame(gameObject))
                return;
            __result = Array.Empty<Component>();
        }

        /// <summary>そのアバターに「Quest 対応」が ON の ReFrame コンポーネントがあるか。</summary>
        static bool IsHandledByReFrame(GameObject gameObject)
        {
            var descriptor = gameObject.GetComponentInParent<VRCAvatarDescriptor>();
            var root = descriptor != null ? descriptor.gameObject : gameObject;
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component != null && component.QuestConversionActive)
                    return true;
            }
            return false;
        }
    }
}
