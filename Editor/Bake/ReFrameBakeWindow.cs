using UnityEditor;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>焼き込みの内容を確かめ、プレハブへ焼くかヒエラルキーへ反映するかを選んで実行するウィンドウ。</summary>
    internal sealed class ReFrameBakeWindow : EditorWindow
    {
        const string OutputPrefKey = "jp.illusive-isc.reframe.bake.output";
        const string BackupPrefKey = "jp.illusive-isc.reframe.bake.keep-backup";

        GameObject avatarRoot;
        ReFrameBakeOutput output;
        bool keepBackup;
        Vector2 scroll;

        public static void Open(GameObject avatarRoot)
        {
            var window = GetWindow<ReFrameBakeWindow>(true, "ReFrame 焼き込み", true);
            window.avatarRoot = avatarRoot;
            window.output = (ReFrameBakeOutput)EditorPrefs.GetInt(OutputPrefKey, (int)ReFrameBakeOutput.Prefab);
            window.keepBackup = EditorPrefs.GetBool(BackupPrefKey, false);
            window.minSize = new Vector2(460, 420);
            window.ShowUtility();
        }

        void OnInspectorUpdate() => Repaint();

        void OnGUI()
        {
            if (avatarRoot == null)
            {
                Close();
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("アバター", avatarRoot, typeof(GameObject), true);
            EditorGUILayout.LabelField("焼く側", ReFrameBake.TargetLabel(avatarRoot) + " (プレビューで表示している側)");
            if (ReFrameBake.ForQuest(avatarRoot))
                EditorGUILayout.LabelField(" ", "テクスチャは ASTC で書き出します", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("使う設定", ReFrameBake.DescribeUsage(avatarRoot), EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("焼いた結果の残し方", EditorStyles.boldLabel);
            OutputOption(
                ReFrameBakeOutput.Prefab,
                "プレハブへ焼き込む",
                "置き場に新しいプレハブを作り、ヒエラルキーのアバターをそのインスタンスに置き換えます。ほかのシーンやプロジェクトでも使い回せます。"
            );
            OutputOption(
                ReFrameBakeOutput.Hierarchy,
                "ヒエラルキーへ反映する",
                "プレハブは作らず、このシーンのアバターだけを焼いた状態にします。元のプレハブとの繋がりは外れ、シーンの中の GameObject になります。"
            );

            EditorGUILayout.Space();
            var backup = EditorGUILayout.ToggleLeft("焼く前の姿を「(焼き込み前).prefab」として置き場に控える", keepBackup);
            if (backup != keepBackup)
            {
                keepBackup = backup;
                EditorPrefs.SetBool(BackupPrefKey, keepBackup);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("置き場", ReFrameBake.PlanFolder(avatarRoot), EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox(
                "FX / メニュー / パラメーター / クリップの複製は、どちらの残し方でも置き場に作り、アバターはそこを参照するように張り替えます。\n"
                    + "ReFrame のコンポーネントは全部外れ、この後は設定を変えられません。元のプレハブは触りません。\n"
                    + "シーン上の置き換えは Ctrl+Z で戻せますが、置き場に書き出したファイルは残ります。",
                MessageType.Warning
            );
            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("やめる", GUILayout.Width(100), GUILayout.Height(26)))
                {
                    Close();
                    GUIUtility.ExitGUI();
                }
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("焼き込む", GUILayout.Width(140), GUILayout.Height(26)))
                        EditorApplication.delayCall += Run;
                }
            }
            EditorGUILayout.Space(4);
        }

        void OutputOption(ReFrameBakeOutput value, string label, string description)
        {
            if (GUILayout.Toggle(output == value, " " + label, EditorStyles.radioButton) && output != value)
            {
                output = value;
                EditorPrefs.SetInt(OutputPrefKey, (int)output);
            }
            using (new EditorGUI.IndentLevelScope())
                EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
        }

        void Run()
        {
            if (avatarRoot == null)
                return;
            if (ReFrameBake.BakeFromWindow(avatarRoot, output, keepBackup))
                Close();
        }
    }
}
