using UnityEditor;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>焼き込みの記録を読み取り専用で見せる。</summary>
    [CustomEditor(typeof(ReFrameBakedInfo))]
    internal sealed class ReFrameBakedInfoInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var info = (ReFrameBakedInfo)target;
            EditorGUILayout.HelpBox(
                "このアバターは ReFrame の設定を焼き込み済みです。ReFrame のコンポーネントは外してあり、ビルドでは何もしません。"
                    + "設定を変えたいときは元のプレハブから置き直して焼き直してください。",
                MessageType.Info
            );
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("ReFrame", info.reframeVersion);
                EditorGUILayout.TextField("焼いた日時", info.bakedAt);
                EditorGUILayout.TextField("ビルドターゲット", info.buildTarget);
                EditorGUILayout.TextField("元のプレハブ", info.sourcePrefab);
                EditorGUILayout.TextField("置き場", info.assetFolder);
            }
            if (GUILayout.Button("置き場を開く") && AssetDatabase.IsValidFolder(info.assetFolder))
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(info.assetFolder));

            EditorGUILayout.Space();
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sweepMode"), new GUIContent("消したギミックの実体"));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox(
                "ビルド時に動くのはこの掃除だけです。焼き込みで消えたギミックの GameObject・ボーン・空になったレイヤーを、"
                    + "焼き付け前の状態 (" + info.sweepBoneOwners.Count + " 本のボーンの使用者、" + info.sweepActiveSelf.Count + " 個のアクティブ状態) と比べて片付けます。",
                MessageType.None
            );

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("焼いたコンポーネント", EditorStyles.boldLabel);
            foreach (var name in info.components)
                EditorGUILayout.LabelField("  " + name);
            EditorGUILayout.LabelField("固定したパラメーター (" + info.entries.Length + ")", EditorStyles.boldLabel);
            foreach (var entry in info.entries)
                EditorGUILayout.LabelField("  " + entry);
        }
    }
}
