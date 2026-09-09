using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>「揺れ物を一覧から選んで消す」ための一覧を、シーンから直接作る。</summary>
    internal static class ReFramePhysBoneCatalog
    {
        internal sealed class PhysBoneEntry
        {
            /// <summary>選択の保存に使う識別子 (シーンの "path#index")。</summary>
            public readonly List<string> Keys = new();

            /// <summary>Inspector に出す名前 (揺れの根元のオブジェクト名)。</summary>
            public string Label;

            /// <summary>この PhysBone が動かすボーンの数 (transformCount への寄与)。</summary>
            public int AffectedBones;

            /// <summary>Keys と同じ並びの、メンバー 1 本ごとの影響ボーン数。</summary>
            public readonly List<int> BoneCounts = new();

            public readonly List<ColliderEntry> Colliders = new();

            /// <summary>どのプレハブ (単位) の揺れ物か。</summary>
            public string Unit = "";

            /// <summary>単位の並び順。</summary>
            public int UnitOrder;
        }

        /// <summary>この Transform が属する単位 (プレハブ)。</summary>
        internal static (string Label, int Order) UnitOf(Transform root, Transform target)
        {
            if (root == null || target == null || target == root)
                return ("アバター本体", -1);
            var top = target;
            while (top.parent != null && top.parent != root)
                top = top.parent;
            if (top.parent != root)
                return ("アバター本体", -1);

            var animator = root.GetComponent<Animator>();
            var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            Transform bodyTop = null;
            if (hips != null)
            {
                bodyTop = hips;
                while (bodyTop.parent != null && bodyTop.parent != root)
                    bodyTop = bodyTop.parent;
            }
            var isBody = bodyTop != null ? top == bodyTop : top.name == "Armature";
            return isBody ? ("アバター本体", -1) : (top.name, top.GetSiblingIndex());
        }

        internal sealed class ColliderEntry
        {
            public string Key;
            public string Label;

            /// <summary>このコライダーを外すと減る当たり判定の回数。</summary>
            public int CollisionChecks;

            /// <summary>この行の外にある揺れ物からも参照されている。</summary>
            public bool Shared;

            /// <summary>アバター全体でこのコライダーを参照している PhysBone の数。</summary>
            public int ReferenceCount;

            /// <summary>まとめた行の中で、このコライダーを参照しているメンバーの数。</summary>
            public int GroupReferences;
        }

        /// <summary>アバタールートからの相対パスを組み立てる。</summary>
        internal static string PathOf(Transform root, Transform target)
        {
            if (target == null)
                return string.Empty;
            var path = target.name;
            var cursor = target.parent;
            while (cursor != null && cursor != root)
            {
                path = cursor.name + "/" + path;
                cursor = cursor.parent;
            }
            return path;
        }

        /// <summary>同じ GameObject に同じ型が複数付いている場合を含めて一意になる鍵。</summary>
        internal static string KeyOf(Transform root, Component component)
        {
            var siblings = component.GetComponents(component.GetType());
            var index = 0;
            for (var i = 0; i < siblings.Length; i++)
            {
                if (siblings[i] == component)
                {
                    index = i;
                    break;
                }
            }
            return PathOf(root, component.transform) + "#" + index;
        }

        /// <summary>鍵から先頭のセグメント (アバター直下のオブジェクト名) を落とした残り。</summary>
        internal static string WithoutRoot(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            var slash = key.IndexOf('/');
            return slash < 0 ? null : key.Substring(slash + 1);
        }

        /// <summary>その鍵が選ばれているか。</summary>
        internal static bool IsSelected(ICollection<string> selected, string key)
        {
            if (selected == null || selected.Count == 0 || string.IsNullOrEmpty(key))
                return false;
            if (selected.Contains(key))
                return true;
            var tail = WithoutRoot(key);
            if (tail == null)
                return false;
            foreach (var candidate in selected)
                if (WithoutRoot(candidate) == tail)
                    return true;
            return false;
        }

        /// <summary>鍵から実体を引く。</summary>
        internal static T ResolveLoose<T>(Transform root, string key)
            where T : Component
        {
            var exact = Resolve<T>(root, key);
            if (exact != null)
                return exact;
            var tail = WithoutRoot(key);
            if (tail == null || root == null)
                return null;
            foreach (var candidate in root.GetComponentsInChildren<T>(true))
            {
                if (ReFrameDeleteComponent.IsPreviewShadow(candidate.transform))
                    continue;
                if (WithoutRoot(KeyOf(root, candidate)) == tail)
                    return candidate;
            }
            return null;
        }

        /// <summary>鍵から実体を引く。</summary>
        internal static T Resolve<T>(Transform root, string key)
            where T : Component
        {
            if (string.IsNullOrEmpty(key))
                return null;
            var hash = key.LastIndexOf('#');
            if (hash < 0)
                return null;
            var path = key.Substring(0, hash);
            if (!int.TryParse(key.Substring(hash + 1), out var index))
                return null;
            var target = root.Find(path);
            if (target == null)
                return null;
            var components = target.GetComponents<T>();
            return index >= 0 && index < components.Length ? components[index] : null;
        }

        static readonly System.Type PhysBoneBaseType = System.Type.GetType(
            "VRC.Dynamics.VRCPhysBoneBase, VRC.Dynamics"
        );
        static readonly System.Reflection.MethodInfo InitTransformsMethod =
            PhysBoneBaseType?.GetMethod(
                "InitTransforms",
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance
            );
        static readonly System.Reflection.FieldInfo BonesField =
            PhysBoneBaseType?.GetField(
                "bones",
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance
            );

        /// <summary>この PhysBone が動かすボーンの数。</summary>
        internal static int CountAffectedBones(VRCPhysBone physBone)
        {
            if (InitTransformsMethod == null || BonesField == null)
                return 0;
            try
            {
                InitTransformsMethod.Invoke(physBone, new object[] { true });
                var counted = (BonesField.GetValue(physBone) as System.Collections.IList)?.Count ?? 0;
                return counted - CountPreviewShadows(physBone);
            }
            catch (System.Exception)
            {

                return 0;
            }
        }

        /// <summary>チェーンの中に紛れているプレビューの影武者の数。</summary>
        static int CountPreviewShadows(VRCPhysBone physBone)
        {
            var root = physBone.rootTransform != null ? physBone.rootTransform : physBone.transform;
            var shadows = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (ReFrameDeleteComponent.IsPreviewShadow(t))
                    shadows++;
            return shadows;
        }

        /// <summary>表示用に名前を整える。</summary>
        static string Readable(string name)
        {
            var dollar = name.IndexOf('$');
            return dollar > 0 ? name.Substring(0, dollar) : name;
        }

        /// <summary>アバターの階層をそのまま数えて一覧を作る。</summary>
        internal static List<PhysBoneEntry> SnapshotEntries(Transform cloneRoot)
        {
            var entries = new List<PhysBoneEntry>();
            if (cloneRoot == null)
                return entries;

            var doomed = ReFrameDeleteComponent.CollectDeclaredDeletions(cloneRoot);

            var referenceCount = new Dictionary<VRCPhysBoneCollider, int>();
            foreach (var physBone in cloneRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                if (ReFrameDeleteComponent.IsPreviewShadow(physBone.transform)
                    || doomed.Contains(physBone.transform)
                    || physBone.colliders == null)
                    continue;
                foreach (var collider in physBone.colliders)
                    if (collider is VRCPhysBoneCollider typed)
                    {
                        referenceCount.TryGetValue(typed, out var count);
                        referenceCount[typed] = count + 1;
                    }
            }

            foreach (var physBone in cloneRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {

                if (ReFrameDeleteComponent.IsPreviewShadow(physBone.transform)
                    || doomed.Contains(physBone.transform))
                    continue;
                var root = physBone.rootTransform != null ? physBone.rootTransform : physBone.transform;
                var affected = CountAffectedBones(physBone);
                var unit = UnitOf(cloneRoot, physBone.transform);
                var entry = new PhysBoneEntry
                {
                    Label = Readable(root.name),
                    AffectedBones = affected,
                    Unit = unit.Label,
                    UnitOrder = unit.Order,
                };
                entry.Keys.Add(KeyOf(cloneRoot, physBone));
                entry.BoneCounts.Add(affected);
                if (physBone.colliders != null)
                {
                    foreach (var collider in physBone.colliders)
                    {
                        if (!(collider is VRCPhysBoneCollider typed))
                            continue;
                        referenceCount.TryGetValue(typed, out var shared);
                        entry.Colliders.Add(
                            new ColliderEntry
                            {
                                Key = KeyOf(cloneRoot, typed),
                                Label = Readable(typed.transform.name),
                                CollisionChecks = affected,
                                Shared = shared > 1,
                                ReferenceCount = shared,
                            }
                        );
                    }
                }
                entries.Add(entry);
            }
            return Group(cloneRoot, entries);
        }

        /// <summary>ReFramePhysBoneGroupAttribute の宣言に従って行をまとめる。</summary>
        static List<PhysBoneEntry> Group(Transform avatarRoot, List<PhysBoneEntry> entries)
        {
            var groups = new List<ReFramePhysBoneGroupAttribute>();
            foreach (var component in ReFrameDeleteComponent.ActiveIn(avatarRoot))
            {
                if (component == null)
                    continue;
                foreach (var attribute in component.GetType()
                    .GetCustomAttributes(typeof(ReFramePhysBoneGroupAttribute), true))
                    groups.Add((ReFramePhysBoneGroupAttribute)attribute);
            }
            if (groups.Count == 0)
                return entries;

            var result = new List<PhysBoneEntry>();
            var byLabel = new Dictionary<string, PhysBoneEntry>();
            foreach (var entry in entries)
            {
                ReFramePhysBoneGroupAttribute matched = null;
                foreach (var group in groups)
                    if (group.Matches(entry.Label))
                    {
                        matched = group;
                        break;
                    }
                if (matched == null)
                {
                    result.Add(entry);
                    continue;
                }

                if (!byLabel.TryGetValue(matched.Label, out var target))
                {
                    target = new PhysBoneEntry { Label = matched.Label, Unit = entry.Unit, UnitOrder = entry.UnitOrder };
                    byLabel[matched.Label] = target;
                    result.Add(target);
                }
                else if (target.Unit != entry.Unit)
                {

                    target.Unit = "複数のプレハブ";
                }
                target.Keys.AddRange(entry.Keys);
                target.BoneCounts.AddRange(entry.BoneCounts);
                target.AffectedBones += entry.AffectedBones;
                foreach (var collider in entry.Colliders)
                {

                    ColliderEntry existing = null;
                    foreach (var candidate in target.Colliders)
                        if (candidate.Key == collider.Key)
                        {
                            existing = candidate;
                            break;
                        }
                    if (existing == null)
                    {
                        target.Colliders.Add(collider);
                        collider.GroupReferences = 1;
                    }
                    else
                    {
                        existing.CollisionChecks += collider.CollisionChecks;
                        existing.GroupReferences++;
                    }
                }
            }
            foreach (var entry in result)
            {

                foreach (var collider in entry.Colliders)
                    if (collider.GroupReferences > 0)
                        collider.Shared = collider.ReferenceCount > collider.GroupReferences;

                var seen = new Dictionary<string, int>();
                foreach (var collider in entry.Colliders)
                {
                    seen.TryGetValue(collider.Label, out var count);
                    seen[collider.Label] = count + 1;
                }
                var numbered = new Dictionary<string, int>();
                foreach (var collider in entry.Colliders)
                {
                    if (seen[collider.Label] < 2)
                        continue;
                    numbered.TryGetValue(collider.Label, out var index);
                    numbered[collider.Label] = index + 1;
                    collider.Label = collider.Label + " (" + (index + 1) + ")";
                }
            }

            result.Sort((a, b) => b.AffectedBones.CompareTo(a.AffectedBones));
            return result;
        }

    }
}
