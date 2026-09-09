using System;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core.Editor.HarmonyPatches
{
    /// <summary>Harmony パッチの入り口。</summary>
    internal static class ReFrameHarmonyPatchLoader
    {
        const string HarmonyId = "jp.illusive-isc.reframe-core";

        static readonly Action<Harmony>[] Patches =
        {
            VRCQuestToolsUnsupportedComponentsPatch.Patch,
            VRCQuestToolsAvatarDynamicsPatch.Patch,
            VRCQuestToolsDynamicsListPatch.Patch,
            VRCQuestToolsMaterialListPatch.Patch,
            VRCQuestToolsNetworkIdPatch.Patch,
            VRCQuestToolsSelectorRowPatch.Patch,
            VRCQuestToolsMenuIconPatch.Patch,
        };

        [InitializeOnLoadMethod]
        static void ApplyPatches()
        {
            var harmony = new Harmony(HarmonyId);

            foreach (var patch in Patches)
            {
                try
                {
                    patch(harmony);
                }
                catch (Exception e)
                {

                    Debug.LogWarning("[ReFrameCore] Harmony パッチの適用に失敗しました。\n" + e);
                }
            }

            AssemblyReloadEvents.beforeAssemblyReload += () => harmony.UnpatchAll(HarmonyId);
        }
    }
}
