using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core.Editor.HarmonyPatches
{
    /// <summary>VRCQuestTools の「ネットワーク ID が振られていません」という報告を、 ReFrame が振るアバターでは出さないようにする。</summary>
    internal static class VRCQuestToolsNetworkIdPatch
    {
        const string TargetType = "KRT.VRCQuestTools.Utils.VRCSDKUtility";
        const string TargetMethod = "HasMissingNetworkIds";

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
                        + "VRCQuestTools のネットワーク ID の案内は素のままになります。"
                );
                return;
            }

            harmony.Patch(
                target,
                postfix: new HarmonyMethod(
                    typeof(VRCQuestToolsNetworkIdPatch).GetMethod(
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

        /// <summary>ReFrame が載っているアバターなら「振られていない」とは言わせない。</summary>
        static void Postfix(object avatarDescriptor, ref bool __result)
        {
            if (!__result)
                return;
            var descriptor = avatarDescriptor as Component;
            if (descriptor == null)
                return;
            foreach (
                var component in ReFrameDeleteComponent.ActiveIn(descriptor)
            )
            {
                if (component == null)
                    continue;
                __result = false;
                return;
            }
        }
    }
}
