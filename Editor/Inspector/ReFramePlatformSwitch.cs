using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>PC 用と Quest 用を、ビルドターゲットごと行き来する。</summary>
    internal static class ReFramePlatformSwitch
    {
        static BuildTarget TargetOf(ReFrameBuildPlatform platform) =>
            platform == ReFrameBuildPlatform.Android
                ? BuildTarget.Android
                : BuildTarget.StandaloneWindows64;

        static BuildTargetGroup GroupOf(ReFrameBuildPlatform platform) =>
            platform == ReFrameBuildPlatform.Android
                ? BuildTargetGroup.Android
                : BuildTargetGroup.Standalone;

        /// <summary>いまのビルドターゲットが指すプラットフォーム。</summary>
        internal static ReFrameBuildPlatform CurrentPlatform =>
            EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android
                ? ReFrameBuildPlatform.Android
                : ReFrameBuildPlatform.PC;

        internal static string Label(ReFrameBuildPlatform platform) =>
            platform == ReFrameBuildPlatform.Android ? "Quest (Android)" : "PC (Windows)";

        /// <summary>切り替えの対象。</summary>
        internal static List<ReFrameDeleteComponent> Targets() =>
            Object
                .FindObjectsOfType<ReFrameDeleteComponent>(true)
                .Where(c => c != null)
                .ToList();

        /// <summary>確認ダイアログを出してからビルドターゲットを切り替える。</summary>
        internal static bool Switch(ReFrameBuildPlatform destination)
        {
            if (CurrentPlatform == destination)
                return false;

            var message =
                (
                    destination == ReFrameBuildPlatform.Android
                        ? "ビルドターゲットを Quest (Android) に切り替えます (Android プレビューモード)。"
                            + (char)10
                            + "Quest 簡易対応版の設定で、Quest 化した見た目を Scene と Hierarchy で確かめられます。"
                        : "ビルドターゲットを PC (Windows) に戻します。通常の編集に戻ります。"
                )
                + (char)10
                + (char)10
                + "テクスチャの再インポートが走るため、数分かかることがあります。"
                + (char)10
                + (char)10
                + "続けますか？";
            if (!EditorUtility.DisplayDialog("ReFrame", message, "切り替える", "やめる"))
                return false;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            AssetDatabase.SaveAssets();
            EditorUserBuildSettings.SwitchActiveBuildTarget(GroupOf(destination), TargetOf(destination));
            return true;
        }
    }

    /// <summary>以前はビルドターゲットの変化に合わせて設定の入れ替え (activeSlot / questDiff) を行っていた。</summary>
    internal static class ReFramePlatformAutoSync { }
}
