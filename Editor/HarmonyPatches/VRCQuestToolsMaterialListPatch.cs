using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core.Editor.HarmonyPatches
{
    /// <summary>VRCQuestTools の「追加のマテリアル変換設定」に出るマテリアルの一覧から、 ReFrame がビルド時に自分で差し替えるぶんと、消えるメッシュのぶんを外す。</summary>
    internal static class VRCQuestToolsMaterialListPatch
    {
        const string TargetType = "KRT.VRCQuestTools.Models.VRChat.VRChatAvatar";
        const string TargetMethod = "GetRelatedMaterials";

        internal static void Patch(Harmony harmony)
        {
            var type = FindType(TargetType);
            if (type == null)
                return;

            var target = AccessTools.Method(type, TargetMethod, new[] { typeof(GameObject) });
            if (target == null)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] {TargetType}.{TargetMethod} が見つかりません。"
                        + "VRCQuestTools のマテリアル一覧は素のままになります。"
                );
                return;
            }

            harmony.Patch(
                target,
                postfix: new HarmonyMethod(
                    typeof(VRCQuestToolsMaterialListPatch).GetMethod(
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

        /// <summary>ReFrame が面倒を見るマテリアルを一覧から落とす。</summary>
        static void Postfix(GameObject rootObject, ref Material[] __result)
        {
            if (__result == null || __result.Length == 0 || rootObject == null)
                return;
            var root = rootObject.transform;
            var components = ReFrameDeleteComponent.ActiveIn(root);
            var handled = false;
            foreach (var component in components)
                if (component != null && component.QuestConversionActive)
                    handled = true;
            if (!handled)
                return;

            var replaced = new HashSet<Material>();
            foreach (var component in components)
            {
                if (component == null)
                    continue;
                foreach (var target in component.EnumerateQuestParticleMaterialTargets())
                {
                    var found = root.Find(target.Path);
                    if (found == null)
                        continue;
                    foreach (var renderer in found.GetComponentsInChildren<Renderer>(true))
                        foreach (var material in renderer.sharedMaterials)
                            if (material != null)
                                replaced.Add(material);
                }
            }

            var doomed = ReFrameDeleteComponent.CollectDeclaredDeletions(root);
            var survives = new HashSet<Material>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (doomed.Contains(renderer.transform))
                    continue;
                foreach (var material in renderer.sharedMaterials)
                    if (material != null)
                        survives.Add(material);
            }

            var kept = new List<Material>(__result.Length);
            foreach (var material in __result)
            {
                if (material == null)
                    continue;
                if (replaced.Contains(material))
                    continue;

                if (!survives.Contains(material) && UsedByAnyRenderer(root, material))
                    continue;
                kept.Add(material);
            }

            if (kept.Count != __result.Length)
                __result = kept.ToArray();
        }

        /// <summary>そのマテリアルを Renderer が (消えるものも含めて) 使っているか。</summary>
        static bool UsedByAnyRenderer(Transform root, Material material)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (var assigned in renderer.sharedMaterials)
                    if (assigned == material)
                        return true;
            return false;
        }
    }
}
