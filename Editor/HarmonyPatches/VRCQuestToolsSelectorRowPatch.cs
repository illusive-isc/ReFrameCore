using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor.HarmonyPatches
{
    /// <summary>Avatar Dynamics の選択リストから、ReFrame がビルド時に消す行を描かない。</summary>
    internal static class VRCQuestToolsSelectorRowPatch
    {
        const string TargetType = "KRT.VRCQuestTools.Views.EditorGUIUtility";
        const string TargetMethod = "ToggleAvatarDynamicsComponentField";

        internal static void Patch(Harmony harmony)
        {
            var type = FindType(TargetType);
            if (type == null)
                return;

            var target = AccessTools.Method(
                type,
                TargetMethod,
                new[] { typeof(bool), typeof(Component) }
            );
            if (target == null)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] {TargetType}.{TargetMethod} が見つかりません。"
                        + "VRCQuestTools の揺れ物一覧は素のままになります。"
                );
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    typeof(VRCQuestToolsSelectorRowPatch).GetMethod(
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

        /// <summary>消える行なら、描かずに今の値をそのまま返す。</summary>
        static bool Prefix(bool value, Component component, ref bool __result)
        {
            if (component == null || !IsGone(component))
                return true;

            __result = value;
            return false;
        }

        /// <summary>この行の実体は ReFrame のビルドで消えるか。</summary>
        static bool IsGone(Component component)
        {
            var descriptor = component.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
                return false;
            var state = State.Get(descriptor.transform);
            if (state == null)
                return false;
            if (ReFrameDeleteComponent.IsPreviewShadow(component.transform))
                return true;
            if (state.Doomed.Contains(component.transform))
                return true;
            return state.Deleted.Count > 0
                && ReFramePhysBoneCatalog.IsSelected(
                    state.Deleted,
                    ReFramePhysBoneCatalog.KeyOf(descriptor.transform, component)
                );
        }

        /// <summary>1 行ごとに階層を丸ごと走査し直すと、40 行 x 再描画のたびに効いてくる。</summary>
        static class State
        {
            const double TtlSeconds = 0.05;

            internal sealed class Entry
            {
                internal double Stamp;
                internal HashSet<Transform> Doomed;
                internal HashSet<string> Deleted;
            }

            static readonly Dictionary<int, Entry> Cache = new Dictionary<int, Entry>();

            internal static Entry Get(Transform root)
            {
                var id = root.GetInstanceID();
                var now = EditorApplication.timeSinceStartup;
                if (Cache.TryGetValue(id, out var cached) && now - cached.Stamp < TtlSeconds)
                    return cached;

                var handled = false;
                var deleted = new HashSet<string>();
                foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
                {
                    if (component == null)
                        continue;
                    if (component.QuestConversionActive)
                        handled = true;
                    if (component.deletedPhysBones != null)
                        deleted.UnionWith(component.deletedPhysBones);
                    if (component.deletedPhysBoneColliders != null)
                        deleted.UnionWith(component.deletedPhysBoneColliders);
                }
                if (!handled)
                {
                    Cache.Remove(id);
                    return null;
                }

                var entry = new Entry
                {
                    Stamp = now,
                    Doomed = ReFrameDeleteComponent.CollectDeclaredDeletions(root),
                    Deleted = deleted,
                };
                Cache[id] = entry;
                return entry;
            }
        }
    }
}
