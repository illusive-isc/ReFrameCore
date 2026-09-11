using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>Hierarchy の右クリック (GameObject メニュー) から、アバターに ReFrame の子オブジェクトを足す。</summary>
    internal static class ReFrameHierarchyMenu
    {
        const string AutoItem = "GameObject/ILLUSORY OVERRIDE/ReFrame/このアバターに ReFrame を追加";
        const string PickItem = "GameObject/ILLUSORY OVERRIDE/ReFrame/種類を選んで ReFrame を追加...";

        [MenuItem(AutoItem, true)]
        [MenuItem(PickItem, true)]
        static bool Validate() => DescriptorOf(Selection.activeGameObject) != null;

        [MenuItem(AutoItem, false, 10)]
        static void AddAuto()
        {
            var descriptor = DescriptorOf(Selection.activeGameObject);
            if (descriptor == null)
                return;
            var matches = Candidates().Where(c => Matches(c.Signature, descriptor)).ToList();
            if (matches.Count == 1)
            {
                Add(descriptor, matches[0].Type);
                return;
            }
            if (matches.Count == 0)
            {
                var all = Candidates().ToList();
                if (
                    !EditorUtility.DisplayDialog(
                        "ReFrame",
                        "このアバターに合う ReFrame を判別できませんでした (FBX の GUID と Avatar アセット名で照合)。"
                            + (char)10
                            + "種類を選んで追加しますか？",
                        "選ぶ",
                        "やめる"
                    )
                )
                    return;
                ShowPicker(descriptor, all);
                return;
            }
            ShowPicker(descriptor, matches);
        }

        [MenuItem(PickItem, false, 11)]
        static void AddPicked()
        {
            var descriptor = DescriptorOf(Selection.activeGameObject);
            if (descriptor == null)
                return;
            ShowPicker(descriptor, Candidates().ToList());
        }

        static void ShowPicker(VRCAvatarDescriptor descriptor, List<Candidate> candidates)
        {
            var menu = new GenericMenu();
            if (candidates.Count == 0)
                menu.AddDisabledItem(new GUIContent("[ReFrameAvatarSignature] を持つ ReFrame がプロジェクトにありません"));
            foreach (var candidate in candidates)
            {
                var captured = candidate;
                var matched = Matches(captured.Signature, descriptor);
                menu.AddItem(
                    new GUIContent(captured.Signature.DisplayName + " (" + captured.Type.Name + ")" + (matched ? "  ← 一致" : "")),
                    false,
                    () => Add(descriptor, captured.Type)
                );
            }
            menu.ShowAsContext();
        }

        sealed class Candidate
        {
            public Type Type;
            public ReFrameAvatarSignatureAttribute Signature;
        }

        /// <summary>[ReFrameAvatarSignature] を持つ PC 用のクラス (Quest 簡易対応版は除く)。</summary>
        static IEnumerable<Candidate> Candidates()
        {
            foreach (var type in TypeCache.GetTypesWithAttribute<ReFrameAvatarSignatureAttribute>())
            {
                if (type.IsAbstract || !typeof(ReFrameDeleteComponent).IsAssignableFrom(type))
                    continue;
                if (Attribute.IsDefined(type, typeof(ReFrameQuestVariantAttribute), true))
                    continue;
                var signature = (ReFrameAvatarSignatureAttribute)Attribute.GetCustomAttribute(type, typeof(ReFrameAvatarSignatureAttribute), false);
                if (signature != null)
                    yield return new Candidate { Type = type, Signature = signature };
            }
        }

        static VRCAvatarDescriptor DescriptorOf(GameObject selected) =>
            selected != null ? selected.GetComponentInParent<VRCAvatarDescriptor>(true) : null;

        /// <summary>FBX の GUID (Avatar アセットかメッシュの出どころ) か Avatar アセット名で照合する。</summary>
        internal static bool Matches(ReFrameAvatarSignatureAttribute signature, VRCAvatarDescriptor descriptor)
        {
            var guids = new HashSet<string>();
            string avatarName = null;
            var animator = descriptor.GetComponent<Animator>();
            if (animator != null && animator.avatar != null)
            {
                avatarName = animator.avatar.name;
                var path = AssetDatabase.GetAssetPath(animator.avatar);
                if (!string.IsNullOrEmpty(path))
                    guids.Add(AssetDatabase.AssetPathToGUID(path));
            }
            foreach (var smr in descriptor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null)
                    continue;
                var path = AssetDatabase.GetAssetPath(smr.sharedMesh);
                if (!string.IsNullOrEmpty(path))
                    guids.Add(AssetDatabase.AssetPathToGUID(path));
            }
            if (signature.FbxGuids != null && signature.FbxGuids.Any(g => !string.IsNullOrEmpty(g) && guids.Contains(g)))
                return true;
            return signature.AvatarNames != null && avatarName != null && signature.AvatarNames.Contains(avatarName);
        }

        /// <summary>子オブジェクト "ReFrame" を作ってコンポーネントを載せる。</summary>
        internal static ReFrameDeleteComponent Add(VRCAvatarDescriptor descriptor, Type type)
        {
            foreach (var existing in descriptor.GetComponentsInChildren<ReFrameDeleteComponent>(true))
            {
                if (existing == null || existing.GetType() != type)
                    continue;
                Selection.activeObject = existing;
                Debug.Log("[ReFrameCore] " + type.Name + " は既に '" + existing.gameObject.name + "' に付いています。");
                return existing;
            }
            var holder = new GameObject("ReFrame");
            Undo.RegisterCreatedObjectUndo(holder, "ReFrame: " + type.Name + " を追加");
            holder.transform.SetParent(descriptor.transform, false);
            var component = (ReFrameDeleteComponent)Undo.AddComponent(holder, type);
            Selection.activeObject = component;
            Debug.Log("[ReFrameCore] '" + descriptor.gameObject.name + "' に " + type.Name + " を追加しました (子オブジェクト ReFrame)。");
            return component;
        }
    }
}
