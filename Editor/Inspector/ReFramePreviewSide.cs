using System.Collections.Generic;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>同じアバターに PC 用と Quest 簡易対応版の両方があるとき、プレビューでどちらを見せるかを 手で選ぶ。</summary>
    [InitializeOnLoad]
    internal static class ReFramePreviewSide
    {
        static ReFramePreviewSide()
        {
            EditorApplication.delayCall += Restore;
        }

        static string Key(GameObject root) => "ReFrame.PreviewQuest." + root.name;

        /// <summary>null = 自動 (ビルドターゲットに従う)、true = Quest 簡易対応版、false = PC 用。</summary>
        internal static bool? Get(GameObject root)
        {
            if (root == null)
                return null;
            if (ReFrameDeleteComponent.PreviewQuestOverride.TryGetValue(root.GetInstanceID(), out var quest))
                return quest;
            var stored = SessionState.GetInt(Key(root), 0);
            if (stored == 0)
                return null;
            var value = stored > 0;
            ReFrameDeleteComponent.PreviewQuestOverride[root.GetInstanceID()] = value;
            return value;
        }

        internal static void Set(GameObject root, bool? quest)
        {
            if (root == null)
                return;
            if (quest.HasValue)
                ReFrameDeleteComponent.PreviewQuestOverride[root.GetInstanceID()] = quest.Value;
            else
                ReFrameDeleteComponent.PreviewQuestOverride.Remove(root.GetInstanceID());
            SessionState.SetInt(Key(root), quest.HasValue ? (quest.Value ? 1 : -1) : 0);
            Refresh(root);
        }

        /// <summary>プレビューを作り直させる。</summary>
        static void Refresh(GameObject root)
        {

            foreach (var component in root.GetComponentsInChildren<ReFrameDeleteComponent>(true))
                if (component != null)
                    ChangeNotifier.NotifyObjectUpdate(component);
            ChangeNotifier.NotifyObjectUpdate(root);
            ReFrameQuestMaterialPreview.Clear();
            ReFrameSceneVisibilityPreview.RefreshAll();
            EditorApplication.RepaintHierarchyWindow();
            SceneView.RepaintAll();
        }

        /// <summary>ドメインリロード後、SessionState の控えを静的な表へ戻す。</summary>
        static void Restore()
        {
            foreach (var descriptor in Object.FindObjectsOfType<VRCAvatarDescriptor>(true))
            {
                var root = descriptor.gameObject;
                var stored = SessionState.GetInt(Key(root), 0);
                if (stored != 0)
                    ReFrameDeleteComponent.PreviewQuestOverride[root.GetInstanceID()] = stored > 0;
            }
        }
    }
}
