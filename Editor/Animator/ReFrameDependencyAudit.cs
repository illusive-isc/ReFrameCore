using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>[ReFrameDelete] フィールドごとに、実際に削除・破棄される対象一式 (本体パラメーターが active=false へ焼き付けるオブジェクト + [ReFrameDeleteRelatedBlendTree] で 名前指定して巻き込む BlendTree ノードの中身) の Transform パス (子孫含む) を求め、それが: - 他のレイヤー・他のステートのアニメーションクリップ (このフィールド自身が持つクリップ以外) - SkinnedMeshRenderer の bones / rootBone - VRCConstraintBase (Parent/Position/Rotation/Scale/Aim/LookAt) の Sources[].SourceTransform / TargetTransform - VRCPhysBoneBase の rootTransform / ignoreTransforms のいずれかから (このフィールド自身の削除対象の外側から) 参照されていないかを一括で洗い出す 読み取り専用の事前計算ツール。</summary>
    internal static class ReFrameDependencyAudit
    {
        internal readonly struct ClipUsage
        {
            public readonly string LayerName;
            public readonly AnimatorState State;
            public readonly AnimationClip Clip;
            public readonly string Path;
            public readonly string ControllerRootPrefix;

            public ClipUsage(string layerName, AnimatorState state, AnimationClip clip, string path, string controllerRootPrefix)
            {
                LayerName = layerName;
                State = state;
                Clip = clip;
                Path = path;
                ControllerRootPrefix = controllerRootPrefix;
            }
        }

        internal readonly struct ComponentUsage
        {
            public readonly string Kind;
            public readonly Component Owner;
            public readonly string OwnerPath;
            public readonly string ReferencedPath;

            public ComponentUsage(string kind, Component owner, string ownerPath, string referencedPath)
            {
                Kind = kind;
                Owner = owner;
                OwnerPath = ownerPath;
                ReferencedPath = referencedPath;
            }
        }

        /// <summary>component が持つ [ReFrameDelete] フィールドすべて (Enabled の有無を問わない) について、 削除対象一式が外部から参照されていないかを調べ、レポートをファイルへ書き出す。</summary>
        public static string RunAudit(Component component, VRCAvatarDescriptor descriptor, string outputPath)
        {
            var fieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var entryType = typeof(ReFrameDeleteEntry);

            var fields = component
                .GetType()
                .GetFields(fieldFlags)
                .Where(f => f.FieldType == entryType)
                .ToList();

            var controllers = ReFrameBakedVisibilityResolver.CollectFxControllers(descriptor);
            var avatarRoot = descriptor.transform;

            var incomingConditionParams = BuildIncomingConditionParams(controllers);
            var globalClipIndex = BuildGlobalClipIndex(controllers, avatarRoot);
            var globalComponentIndex = BuildGlobalComponentIndex(avatarRoot);

            var sb = new StringBuilder();
            sb.AppendLine("# ReFrame Dependency Audit");
            sb.AppendLine($"generated: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"fields scanned: {fields.Count}");
            sb.AppendLine();

            var flaggedCount = 0;

            foreach (var field in fields)
            {

                var deleteAttrs = field.GetCustomAttributes<ReFrameDeleteAttribute>().ToList();
                if (deleteAttrs.Count == 0)
                    continue;
                var deleteAttr = deleteAttrs[0];

                var entry = (ReFrameDeleteEntry)field.GetValue(component);

                var ownPaths = new HashSet<string>();
                var ownClips = new HashSet<AnimationClip>();
                var ownComponents = new HashSet<Component>();

                var allValues = deleteAttrs.ToDictionary(a => a.ParameterName, _ => entry.Value);
                var forced = new ReFrameBakedVisibilityResolver.ForcedStates();
                ReFrameBakedVisibilityResolver.BeginResolveScope();
                try
                {
                    foreach (var (controller, pathRoot) in controllers)
                        ReFrameBakedVisibilityResolver.ResolveActiveStates(controller, pathRoot, forced, allValues);
                }
                finally
                {
                    ReFrameBakedVisibilityResolver.EndResolveScope();
                }
                foreach (var kv in forced.GameObjectActive)
                {
                    if (kv.Value || kv.Key == null)
                        continue;
                    AddPathAndDescendants(kv.Key, avatarRoot, ownPaths);
                }

                foreach (var (node, rootPrefix) in FindBlendTreeNodes(
                    controllers,
                    avatarRoot,
                    bt => allValues.ContainsKey(bt.blendParameter)
                ))
                    CollectSubtreeClipsAndPaths(node, rootPrefix, ownPaths, ownClips);

                foreach (var relAttr in field.GetCustomAttributes<ReFrameDeleteRelatedBlendTreeAttribute>())
                {
                    if (relAttr.OnlyWhenValue.HasValue && !Mathf.Approximately(relAttr.OnlyWhenValue.Value, entry.Value))
                        continue;
                    foreach (var (node, rootPrefix) in FindBlendTreeNodes(controllers, avatarRoot, bt => bt.name == relAttr.Name))
                        CollectSubtreeClipsAndPaths(node, rootPrefix, ownPaths, ownClips);
                }

                if (ownPaths.Count == 0)
                    continue;

                foreach (var usage in globalComponentIndex)
                    if (IsPathInOrUnder(usage.OwnerPath, ownPaths))
                        ownComponents.Add(usage.Owner);

                incomingConditionParams.TryGetValue(deleteAttr.ParameterName, out var selfGatedStates);
                selfGatedStates ??= new HashSet<AnimatorState>();

                var ownControllerRootPrefixes = new HashSet<string>();
                foreach (var (controller, pathRoot) in controllers)
                {
                    var rootPrefix = AbsolutePath(pathRoot, avatarRoot);
                    if (string.IsNullOrEmpty(rootPrefix))
                        continue;
                    if (IsPathInOrUnder(rootPrefix, ownPaths) || ownPaths.Any(p => p.StartsWith(rootPrefix + "/") || p == rootPrefix))
                        ownControllerRootPrefixes.Add(rootPrefix);
                }

                var externalClipHits = globalClipIndex
                    .Where(u =>
                        !ownClips.Contains(u.Clip)
                        && !selfGatedStates.Contains(u.State)
                        && !ownControllerRootPrefixes.Contains(u.ControllerRootPrefix)
                        && MatchesOwnPathNonTrivially(u.Path, u.ControllerRootPrefix, ownPaths)
                    )
                    .ToList();
                var externalComponentHits = globalComponentIndex
                    .Where(u =>
                        IsPathInOrUnder(u.ReferencedPath, ownPaths)
                        && !ownComponents.Contains(u.Owner)
                        && !ownControllerRootPrefixes.Any(p => u.OwnerPath == p || u.OwnerPath.StartsWith(p + "/"))
                    )
                    .ToList();

                if (externalClipHits.Count == 0 && externalComponentHits.Count == 0)
                    continue;

                flaggedCount++;
                sb.AppendLine($"## {field.Name}  (param='{deleteAttr.ParameterName}', Value={entry.Value})");
                sb.AppendLine($"own paths: {ownPaths.Count}");
                foreach (var p in ownPaths.OrderBy(p => p).Take(10))
                    sb.AppendLine($"  - {p}");
                if (ownPaths.Count > 10)
                    sb.AppendLine($"  ... and {ownPaths.Count - 10} more");

                if (externalClipHits.Count > 0)
                {
                    var layerGroups = externalClipHits.GroupBy(u => u.LayerName).OrderByDescending(g => g.Count());
                    sb.AppendLine(
                        $"referenced by {externalClipHits.Count} external clip binding(s) across {layerGroups.Count()} layer(s):"
                    );
                    foreach (var g in layerGroups)
                    {
                        var states = g.Select(u => u.State.name).Distinct().OrderBy(s => s).ToList();
                        sb.AppendLine($"  [{g.Key}] states: {string.Join(", ", states)}");
                    }
                }

                if (externalComponentHits.Count > 0)
                {
                    sb.AppendLine($"referenced by {externalComponentHits.Count} external component(s):");
                    foreach (var g in externalComponentHits.GroupBy(u => u.Kind))
                        foreach (var u in g.Take(20))
                            sb.AppendLine($"  [{u.Kind}] '{u.OwnerPath}' -> '{u.ReferencedPath}'");
                }

                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine($"flagged fields: {flaggedCount} / {fields.Count}");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            File.WriteAllText(outputPath, sb.ToString());
            return outputPath;
        }

        /// <summary>path が ownPaths のいずれかと同じ/子孫/祖先であっても、それが「参照元クリップの属する コントローラ自身のパスルートと完全一致しているだけ」の場合は構造的ノイズとして無視する。</summary>
        static bool MatchesOwnPathNonTrivially(string usagePath, string controllerRootPrefix, HashSet<string> ownPaths)
        {
            foreach (var own in ownPaths)
            {
                if (own == controllerRootPrefix)
                    continue;
                if (usagePath == own || usagePath.StartsWith(own + "/") || own.StartsWith(usagePath + "/"))
                    return true;
            }
            return false;
        }

        static bool IsPathInOrUnder(string path, HashSet<string> ownPaths)
        {
            if (ownPaths.Contains(path))
                return true;
            foreach (var own in ownPaths)
                if (path == own || path.StartsWith(own + "/") || own.StartsWith(path + "/"))
                    return true;
            return false;
        }

        static void AddPathAndDescendants(Transform t, Transform avatarRoot, HashSet<string> outPaths)
        {
            outPaths.Add(AbsolutePath(t, avatarRoot));
            foreach (Transform child in t)
                AddPathAndDescendants(child, avatarRoot, outPaths);
        }

        /// <summary>パラメーター名 -> そのパラメーターを条件に使う遷移の「流入先」ステート集合。</summary>
        static Dictionary<string, HashSet<AnimatorState>> BuildIncomingConditionParams(
            List<(AnimatorController Controller, Transform PathRoot)> controllers
        )
        {
            var result = new Dictionary<string, HashSet<AnimatorState>>();

            void AddAll(AnimatorStateTransition[] transitions)
            {
                foreach (var t in transitions)
                {
                    if (t == null || t.destinationState == null)
                        continue;
                    foreach (var cond in t.conditions)
                    {
                        if (!result.TryGetValue(cond.parameter, out var set))
                            result[cond.parameter] = set = new HashSet<AnimatorState>();
                        set.Add(t.destinationState);
                    }
                }
            }

            void Walk(AnimatorStateMachine sm)
            {
                foreach (var cs in sm.states)
                    AddAll(cs.state.transitions);
                AddAll(sm.anyStateTransitions);
                foreach (var child in sm.stateMachines)
                    Walk(child.stateMachine);
            }

            foreach (var (controller, _) in controllers)
                foreach (var layer in controller.layers)
                    Walk(layer.stateMachine);

            return result;
        }

        static List<(BlendTree Node, string RootPrefix)> FindBlendTreeNodes(
            List<(AnimatorController Controller, Transform PathRoot)> controllers,
            Transform avatarRoot,
            System.Func<BlendTree, bool> predicate
        )
        {
            var result = new List<(BlendTree, string)>();
            foreach (var (controller, pathRoot) in controllers)
            {
                var rootPrefix = AbsolutePath(pathRoot, avatarRoot);
                foreach (var layer in controller.layers)
                    WalkForBlendTrees(layer.stateMachine, rootPrefix, predicate, result);
            }
            return result;
        }

        static void WalkForBlendTrees(
            AnimatorStateMachine sm,
            string rootPrefix,
            System.Func<BlendTree, bool> predicate,
            List<(BlendTree, string)> result
        )
        {
            foreach (var cs in sm.states)
                WalkMotionForBlendTrees(cs.state.motion, rootPrefix, predicate, result);
            foreach (var child in sm.stateMachines)
                WalkForBlendTrees(child.stateMachine, rootPrefix, predicate, result);
        }

        static void WalkMotionForBlendTrees(
            Motion motion,
            string rootPrefix,
            System.Func<BlendTree, bool> predicate,
            List<(BlendTree, string)> result
        )
        {
            if (motion is not BlendTree bt)
                return;
            if (predicate(bt))
                result.Add((bt, rootPrefix));
            foreach (var child in bt.children)
                WalkMotionForBlendTrees(child.motion, rootPrefix, predicate, result);
        }

        static void CollectSubtreeClipsAndPaths(
            BlendTree node,
            string rootPrefix,
            HashSet<string> outPaths,
            HashSet<AnimationClip> outClips
        )
        {
            foreach (var child in node.children)
            {
                if (child.motion is AnimationClip clip)
                {
                    outClips.Add(clip);
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                        outPaths.Add(CombinePath(rootPrefix, b.path));
                }
                else if (child.motion is BlendTree childBt)
                {
                    CollectSubtreeClipsAndPaths(childBt, rootPrefix, outPaths, outClips);
                }
            }
        }

        static List<ClipUsage> BuildGlobalClipIndex(
            List<(AnimatorController Controller, Transform PathRoot)> controllers,
            Transform avatarRoot
        )
        {
            var result = new List<ClipUsage>();
            var clipPathCache = new Dictionary<AnimationClip, string[]>();

            foreach (var (controller, pathRoot) in controllers)
            {
                var rootPrefix = AbsolutePath(pathRoot, avatarRoot);
                foreach (var layer in controller.layers)
                    WalkStateMachine(layer.stateMachine, layer.name, rootPrefix, clipPathCache, result);
            }

            return result;
        }

        static void WalkStateMachine(
            AnimatorStateMachine sm,
            string layerName,
            string rootPrefix,
            Dictionary<AnimationClip, string[]> clipPathCache,
            List<ClipUsage> result
        )
        {
            foreach (var cs in sm.states)
                WalkMotion(cs.state.motion, layerName, cs.state, rootPrefix, clipPathCache, result);
            foreach (var child in sm.stateMachines)
                WalkStateMachine(child.stateMachine, layerName, rootPrefix, clipPathCache, result);
        }

        static void WalkMotion(
            Motion motion,
            string layerName,
            AnimatorState state,
            string rootPrefix,
            Dictionary<AnimationClip, string[]> clipPathCache,
            List<ClipUsage> result
        )
        {
            if (motion == null)
                return;

            if (motion is AnimationClip clip)
            {
                if (!clipPathCache.TryGetValue(clip, out var paths))
                {
                    paths = AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct().ToArray();
                    clipPathCache[clip] = paths;
                }
                foreach (var relPath in paths)
                    result.Add(new ClipUsage(layerName, state, clip, CombinePath(rootPrefix, relPath), rootPrefix));
                return;
            }

            if (motion is BlendTree bt)
                foreach (var child in bt.children)
                    WalkMotion(child.motion, layerName, state, rootPrefix, clipPathCache, result);
        }

        /// <summary>アバター全体の SkinnedMeshRenderer / VRCConstraintBase / VRCPhysBoneBase の静的参照を集める。</summary>
        static List<ComponentUsage> BuildGlobalComponentIndex(Transform avatarRoot)
        {
            var result = new List<ComponentUsage>();

            foreach (var smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var ownerPath = AbsolutePath(smr.transform, avatarRoot);
                if (smr.bones != null)
                    foreach (var bone in smr.bones)
                        if (bone != null)
                            result.Add(new ComponentUsage("SkinnedMeshRenderer.bones", smr, ownerPath, AbsolutePath(bone, avatarRoot)));
                if (smr.rootBone != null)
                    result.Add(new ComponentUsage("SkinnedMeshRenderer.rootBone", smr, ownerPath, AbsolutePath(smr.rootBone, avatarRoot)));
            }

            foreach (var constraint in avatarRoot.GetComponentsInChildren<VRCConstraintBase>(true))
            {
                var ownerPath = AbsolutePath(constraint.transform, avatarRoot);
                for (var i = 0; i < constraint.Sources.Count; i++)
                {
                    var src = constraint.Sources[i].SourceTransform;
                    if (src != null)
                        result.Add(new ComponentUsage("Constraint.Source", constraint, ownerPath, AbsolutePath(src, avatarRoot)));
                }
                if (constraint.TargetTransform != null)
                    result.Add(new ComponentUsage("Constraint.Target", constraint, ownerPath, AbsolutePath(constraint.TargetTransform, avatarRoot)));
            }

            foreach (var physBone in avatarRoot.GetComponentsInChildren<VRCPhysBoneBase>(true))
            {
                var ownerPath = AbsolutePath(physBone.transform, avatarRoot);
                var root = physBone.rootTransform != null ? physBone.rootTransform : physBone.transform;
                result.Add(new ComponentUsage("PhysBone.root", physBone, ownerPath, AbsolutePath(root, avatarRoot)));
                if (physBone.ignoreTransforms != null)
                    foreach (var ignore in physBone.ignoreTransforms)
                        if (ignore != null)
                            result.Add(new ComponentUsage("PhysBone.ignore", physBone, ownerPath, AbsolutePath(ignore, avatarRoot)));
            }

            return result;
        }

        static string CombinePath(string rootPrefix, string relPath)
        {
            if (string.IsNullOrEmpty(rootPrefix))
                return relPath;
            if (string.IsNullOrEmpty(relPath))
                return rootPrefix;
            return rootPrefix + "/" + relPath;
        }

        static string AbsolutePath(Transform t, Transform avatarRoot)
        {
            if (t == avatarRoot || t == null)
                return "";
            var segments = new List<string>();
            var cur = t;
            while (cur != null && cur != avatarRoot)
            {
                segments.Add(cur.name);
                cur = cur.parent;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }
    }
}
