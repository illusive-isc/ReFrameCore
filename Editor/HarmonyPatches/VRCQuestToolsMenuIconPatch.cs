using System;
using System.Reflection;
using HarmonyLib;
using nadena.dev.ndmf;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core.Editor.HarmonyPatches
{
    /// <summary>メニューアイコンの縮小・圧縮を ReFrame だけが行うようにする。</summary>
    internal static class VRCQuestToolsMenuIconPatch
    {
        const string TargetType = "KRT.VRCQuestTools.Ndmf.MenuIconResizerPass";
        const string TargetMethod = "Execute";

        internal static void Patch(Harmony harmony)
        {
            var type = FindType(TargetType);
            if (type == null)
                return;

            var target = AccessTools.Method(type, TargetMethod, new[] { typeof(BuildContext) });
            if (target == null)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] {TargetType}.{TargetMethod} が見つかりません。"
                        + "メニューアイコンの圧縮が VRCQuestTools と二重にかかる可能性があります。"
                );
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    typeof(VRCQuestToolsMenuIconPatch).GetMethod(
                        nameof(Prefix),
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

        /// <summary>ReFrame が面倒を見るアバターなら、あちらのパスは走らせない。</summary>
        static bool Prefix(BuildContext context)
        {
            if (context == null || context.AvatarRootObject == null)
                return true;
            foreach (
                var component in ReFrameDeleteComponent.ActiveIn(context.AvatarRootObject)
            )
            {
                if (component != null && component.QuestConversionActive)
                    return false;
            }
            return true;
        }
    }
}
