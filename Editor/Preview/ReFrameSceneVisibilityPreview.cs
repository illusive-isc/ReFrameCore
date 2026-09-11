using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>ReFrameDeleteComponent.previewHiddenInHierarchy が true の間、Enabled なエントリーによって 非表示になる GameObject に HideInHierarchy を立てて、 Hierarchy パネルの行自体を消す (目アイコンで薄く表示するのではなく、そもそも一覧に出さない ── あたかも削除されたかのような見た目にする)。</summary>
    [InitializeOnLoad]
    internal static class ReFrameSceneVisibilityPreview
    {

        static readonly HashSet<GameObject> s_hiddenByUs = new();

        static ReFrameSceneVisibilityPreview()
        {
            EditorApplication.hierarchyChanged += RefreshAll;
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
            EditorApplication.delayCall += RefreshAll;
        }

        static void OnObjectChangesPublished(ref ObjectChangeEventStream stream) => RefreshAll();

        internal static void RefreshAll()
        {
            var desiredHidden = new HashSet<GameObject>();

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                foreach (var descriptor in root.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                    CollectDesiredHidden(descriptor, desiredHidden);
            }

            KeepTopRowsVisible(desiredHidden);
            KeepReFrameComponentsVisible(desiredHidden);

            var previouslyHidden = new HashSet<GameObject>(s_hiddenByUs);
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var descriptor in root.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                foreach (var t in descriptor.GetComponentsInChildren<Transform>(true))
                {
                    if ((t.gameObject.hideFlags & HideFlags.HideInHierarchy) != 0)
                        previouslyHidden.Add(t.gameObject);
                }
            }

            var changed = false;

            foreach (var go in previouslyHidden)
            {
                if (go == null)
                {
                    s_hiddenByUs.Remove(go);
                    continue;
                }
                if (desiredHidden.Contains(go))
                    continue;
                if ((go.hideFlags & HideFlags.HideInHierarchy) != 0)
                {
                    go.hideFlags &= ~HideFlags.HideInHierarchy;
                    changed = true;
                }
                s_hiddenByUs.Remove(go);
            }

            foreach (var go in desiredHidden)
            {
                if (go == null)
                    continue;
                if ((go.hideFlags & HideFlags.HideInHierarchy) == 0)
                {
                    go.hideFlags |= HideFlags.HideInHierarchy;
                    changed = true;
                }
                s_hiddenByUs.Add(go);
            }

            if (changed)
                EditorApplication.RepaintHierarchyWindow();
        }

        /// <summary>消える塊の一番上の行は残し、その配下だけを隠す。一番上まで隠すと Hierarchy から見つけられず取り外せなくなるため。</summary>
        static void KeepTopRowsVisible(HashSet<GameObject> desiredHidden)
        {
            if (desiredHidden.Count == 0)
                return;
            var tops = new List<GameObject>();
            foreach (var go in desiredHidden)
            {
                if (go == null)
                    continue;
                var parent = go.transform.parent;
                if (parent == null || !desiredHidden.Contains(parent.gameObject))
                    tops.Add(go);
            }
            // 親の子が全部消えるなら親の行を残し、その配下 (この塊を含む) を隠したままにする。
            var keep = new List<GameObject>();
            foreach (var top in tops)
            {
                var parent = top.transform.parent;
                var wholeGroup = parent != null && parent.childCount > 1;
                for (var i = 0; wholeGroup && i < parent.childCount; i++)
                    wholeGroup = desiredHidden.Contains(parent.GetChild(i).gameObject);
                if (!wholeGroup)
                    keep.Add(top);
            }
            foreach (var top in keep)
            {
                desiredHidden.Remove(top);
                foreach (var child in top.GetComponentsInChildren<Transform>(true))
                    if (child.gameObject != top)
                        desiredHidden.Add(child.gameObject);
            }
        }

        /// <summary>ReFrameDeleteComponent が乗っている GameObject と、そこへ辿り着くための先祖を 非表示対象から外す。</summary>
        static void KeepReFrameComponentsVisible(HashSet<GameObject> desiredHidden)
        {
            if (desiredHidden.Count == 0)
                return;

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
                {
                    var cursor = component.transform;
                    while (cursor != null)
                    {
                        desiredHidden.Remove(cursor.gameObject);
                        cursor = cursor.parent;
                    }
                }
            }
        }

        /// <summary>解決結果 (アバタールートからの相対パス) を設定ごとに憶えておく。</summary>
        static readonly Dictionary<VRCAvatarDescriptor, (string Signature, List<string> Paths)> s_cache =
            new();

        /// <summary>解決結果を使い回してよいかを判定するための、設定の指紋。</summary>
        static string BuildSignature(VRCAvatarDescriptor descriptor)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var component in ReFrameDeleteComponent.ActiveIn(descriptor))
            {
                builder.Append(component.GetType().FullName).Append('|');
                builder.Append(component.SweepUnusedObjects ? "sweep" : "leave").Append('|');
                builder.Append(component.QuestConversionActive ? "quest" : "pc").Append('|');
                if (component.deletedPhysBones != null)
                    foreach (var path in component.deletedPhysBones)
                        builder.Append(path).Append(',');
                builder.Append('|');
                if (component.deletedPhysBoneColliders != null)
                    foreach (var path in component.deletedPhysBoneColliders)
                        builder.Append(path).Append(',');
                builder.Append('|');
                foreach (var target in component.EnumerateDeleteTargets())
                    builder.Append(target.ParameterName).Append('=').Append(target.Value).Append(',');
                builder.Append('|');
                foreach (var path in component.EnumerateDeleteObjectPaths())
                    builder.Append(path).Append(',');
                builder.Append(";;");
            }
            return builder.ToString();
        }

        static void CollectDesiredHidden(VRCAvatarDescriptor descriptor, HashSet<GameObject> desiredHidden)
        {
            var allComponents = ReFrameDeleteComponent.ActiveIn(descriptor);
            var components = allComponents.Where(c => c.previewHiddenInHierarchy).ToList();
            if (components.Count == 0)
                return;

            var signature = BuildSignature(descriptor);
            if (s_cache.TryGetValue(descriptor, out var cached) && cached.Signature == signature)
            {
                foreach (var path in cached.Paths)
                {
                    var target = descriptor.transform.Find(path);
                    if (target != null)
                        desiredHidden.Add(target.gameObject);
                }
                return;
            }

            var before = desiredHidden.Count;
            CollectDesiredHiddenUncached(descriptor, allComponents, desiredHidden);

            var paths = new List<string>();
            foreach (var go in desiredHidden)
            {
                if (go == null || !go.transform.IsChildOf(descriptor.transform))
                    continue;
                paths.Add(RelativePath(descriptor.transform, go.transform));
            }
            if (desiredHidden.Count != before || paths.Count > 0)
                s_cache[descriptor] = (signature, paths);
        }

        static string RelativePath(Transform root, Transform target)
        {
            var path = target.name;
            var cursor = target.parent;
            while (cursor != null && cursor != root)
            {
                path = cursor.name + "/" + path;
                cursor = cursor.parent;
            }
            return path;
        }

        static void CollectDesiredHiddenUncached(
            VRCAvatarDescriptor descriptor,
            ReFrameDeleteComponent[] allComponents,
            HashSet<GameObject> desiredHidden
        )
        {

            var controllers = ReFrameBakedVisibilityResolver.CollectFxControllers(descriptor);
            if (controllers.Count == 0)
                return;

            var allValues = new Dictionary<string, float>();
            foreach (var component in allComponents)
            foreach (var target in component.EnumerateDeleteTargets())
                allValues[target.ParameterName] = target.Value;

            var treeOverrides = ReFrameAnimatorUtil.BuildTreeOverrides(
                allComponents.SelectMany(c => c.EnumerateBlendTreeOverrides()),
                allValues.Keys
            );

            var forced = new ReFrameBakedVisibilityResolver.ForcedStates();

            ReFrameBakedVisibilityResolver.BeginResolveScope();
            try
            {
                foreach (var (controller, pathRoot) in controllers)
                    ReFrameBakedVisibilityResolver.ResolveActiveStates(
                        controller,
                        pathRoot,
                        forced,
                        allValues,
                        treeOverrides
                    );

                ReFrameBakedVisibilityResolver.ResolveDeletedObjects(
                    allComponents.SelectMany(c => c.EnumerateDeleteObjectPaths()),
                    descriptor.transform,
                    forced
                );
                ReFrameBakedVisibilityResolver.ResolveFixedBlendShapes(
                    allComponents.SelectMany(c => c.EnumerateBlendShapeTargets()),
                    descriptor.transform,
                    forced
                );

                ReFrameBakedVisibilityResolver.ResolveSweptObjects(descriptor, forced);
            }
            finally
            {
                ReFrameBakedVisibilityResolver.EndResolveScope();
            }

            foreach (var kv in forced.GameObjectActive)
            {
                if (!kv.Value && kv.Key != null)
                    desiredHidden.Add(kv.Key.gameObject);
            }

            foreach (var kv in forced.RendererEnabled)
            {
                if (!kv.Value && kv.Key != null)
                    desiredHidden.Add(kv.Key.gameObject);
            }
        }
    }
}
