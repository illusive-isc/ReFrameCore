using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>ReFrameDeletePass がどのアバターで走ったか、そして実体の掃除を 行ってよいかを後続フェーズへ渡すための状態。</summary>
    internal sealed class ReFrameSweepRequest
    {
        public bool Enabled;
    }

    /// <summary>焼き付けの結果もう二度と有効化されない GameObject と、その巻き添えで誰からも 使われなくなったボーンを、アバターから実際に破棄する。</summary>
    [DependsOnContext(typeof(AnimatorServicesContext))]
    public class ReFrameSweepPass : Pass<ReFrameSweepPass>
    {
        protected override void Execute(BuildContext context)
        {
            if (!context.GetState<ReFrameSweepRequest>().Enabled)
                return;

            var root = context.AvatarRootTransform;
            var asc = context.Extension<AnimatorServicesContext>();

            var destroyedObjects = SweepPermanentlyInactiveObjects(context, root, asc);
            var destroyedRenderers = SweepPermanentlyDisabledRenderers(root, asc);
            var deadDrivers = RemoveDriversNobodyReads(context, asc);
            CompactColliderLists(root);

            var destroyedBones = 0;
            var removedLayers = 0;
            while (true)
            {
                var bones = SweepUnusedBones(root, asc);
                var layers = RemoveLayersWithNoLivingTargets(context, root, asc);
                destroyedBones += bones;
                removedLayers += layers;
                if (bones == 0 && layers == 0)
                    break;
            }

            var compacted = CompactColliderLists(root);
            var networkIds = CompactNetworkIds(root);
            var strays = RemoveStrayShakers(root);

            Debug.LogWarning(
                $"[ReFrameCore] ReFrameSweepPass: destroyed {destroyedObjects} inactive object(s), "
                    + $"{destroyedRenderers} disabled renderer(s), {destroyedBones} unused bone(s), "
                    + $"{removedLayers} empty-target layer(s), {deadDrivers} unread driver write(s) "
                    + $"and {strays} shaker(s) that lost their target"
                    + $" (compacted {compacted} dangling collider reference(s)"
                    + $" and {networkIds} stale network ID(s))."
            );
        }

        /// <summary>非アクティブな部分木のうち、 残ったアニメーションのどれからも m_IsActive を書かれない (= 復活しない) 生き残る側のコンポーネントから 1 つも参照されていない を両方満たすものを破棄する。</summary>
        static int SweepPermanentlyInactiveObjects(
            BuildContext context,
            Transform root,
            AnimatorServicesContext asc
        )
        {

            var roots = new List<Transform>();
            CollectInactiveRoots(root, roots);
            if (roots.Count == 0)
                return 0;

            var permanent = roots
                .Where(t => !IsActiveAnimatedOnPathToRoot(t, root, asc))
                .ToList();
            if (permanent.Count == 0)
                return 0;

            var doomed = new HashSet<Transform>();
            foreach (var t in permanent)
            foreach (var d in t.GetComponentsInChildren<Transform>(true))
                doomed.Add(d);

            var rescueReason = new Dictionary<Transform, Transform>();
            while (true)
            {
                var referenced = CollectReferencedTransforms(root, doomed);
                var newlyRescued = permanent
                    .Where(t => !rescueReason.ContainsKey(t))
                    .Select(t => (
                        Root: t,
                        By: t.GetComponentsInChildren<Transform>(true)
                            .FirstOrDefault(referenced.Contains)
                    ))
                    .Where(x => x.By != null)
                    .ToList();
                if (newlyRescued.Count == 0)
                    break;
                foreach (var (rescued, by) in newlyRescued)
                {
                    rescueReason[rescued] = by;
                    foreach (var d in rescued.GetComponentsInChildren<Transform>(true))
                        doomed.Remove(d);
                }
            }

            var destroyed = 0;
            foreach (var t in permanent)
            {
                if (t == null)
                    continue;
                if (rescueReason.TryGetValue(t, out var by))
                {
                    Debug.LogWarning(
                        $"[ReFrameCore] ReFrameSweepPass: keeping inactive '{Path(root, t)}' — "
                            + $"'{Path(root, by)}' is still referenced by a surviving component."
                    );
                    continue;
                }
                var transforms = t.GetComponentsInChildren<Transform>(true).Length;
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameSweepPass: destroying inactive '{Path(root, t)}' "
                        + $"({transforms} transform(s), "
                        + $"{t.GetComponentsInChildren<Component>(true).Length - transforms} component(s))."
                );
                Object.DestroyImmediate(t.gameObject);
                destroyed++;
            }
            return destroyed;
        }

        static void CollectInactiveRoots(Transform t, List<Transform> result)
        {
            foreach (Transform child in t)
            {
                if (!child.gameObject.activeSelf)
                    result.Add(child);
                else
                    CollectInactiveRoots(child, result);
            }
        }

        /// <summary>この Transform からアバタールートまでのどこかで、m_IsActive を動かす クリップが残っていないか。</summary>
        static bool IsActiveAnimatedOnPathToRoot(
            Transform t,
            Transform root,
            AnimatorServicesContext asc
        )
        {
            for (var cur = t; cur != null && cur != root; cur = cur.parent)
                if (IsActiveAnimated(cur, asc))
                    return true;
            return false;
        }

        static bool IsActiveAnimated(Transform t, AnimatorServicesContext asc) =>
            IsPropertyAnimated(t, asc, "m_IsActive");

        /// <summary>GameObject は生きたまま Renderer.enabled だけ false に焼き付けられ、 もう二度と true に戻らないレンダラーを取り除く。</summary>
        static int SweepPermanentlyDisabledRenderers(Transform root, AnimatorServicesContext asc)
        {
            var destroyed = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true).ToList())
            {
                if (renderer == null || renderer.enabled)
                    continue;
                if (IsPropertyAnimated(renderer.transform, asc, "m_Enabled"))
                    continue;
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameSweepPass: destroying permanently disabled "
                        + $"{renderer.GetType().Name} on '{Path(root, renderer.transform)}'."
                );
                Object.DestroyImmediate(renderer);
                destroyed++;
            }
            return destroyed;
        }

        /// <summary>この Transform 上のこのプロパティを書くカーブが、残ったクリップに存在するか。</summary>
        static bool IsPropertyAnimated(
            Transform t,
            AnimatorServicesContext asc,
            string propertyName
        )
        {
            foreach (var path in asc.ObjectPathRemapper.GetAllPathsForObject(t))
            foreach (var clip in asc.AnimationIndex.GetClipsForObjectPath(path))
            foreach (var binding in clip.GetFloatCurveBindings())
                if (binding.path == path && binding.propertyName == propertyName)
                    return true;
            return false;
        }

        /// <summary>揺れ物 (PhysBone / PhysBoneCollider とその移植版) のコンポーネントか。</summary>
        static bool IsShakerComponent(Component c)
        {
            if (c is VRC.Dynamics.VRCPhysBoneBase || c is VRC.Dynamics.VRCPhysBoneColliderBase)
                return true;

            var name = c.GetType().Name;
            return name == "PortableDynamicBone" || name == "PortableDynamicBoneCollider";
        }

        /// <summary>Constraint (VRChat 製・Unity 標準の両方) か。</summary>
        static bool IsConstraintComponent(Component c) =>
            c is VRC.Dynamics.VRCConstraintBase || c is UnityEngine.Animations.IConstraint;

        /// <summary>自分の Transform を動かすためだけに存在するコンポーネントか (揺れ物と Constraint)。</summary>
        static bool IsSelfDrivingComponent(Component c) =>
            IsShakerComponent(c) || IsConstraintComponent(c) || IsRuntimeHelperComponent(c);

        /// <summary>VRChat SDK がランタイムに自動で付ける裏方コンポーネントか。</summary>
        static bool IsRuntimeHelperComponent(Component c) =>
            c.GetType().Name == "ParentChangeDetector";

        /// <summary>生き残ったスキンメッシュのボーンでもなく、ヒューマノイドでもなく、アニメーションも されず、他から参照もされていない Transform を、葉から順に破棄する。</summary>
        static int SweepUnusedBones(Transform root, AnimatorServicesContext asc)
        {
            var used = new HashSet<Transform>();

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (
                    t.GetComponents<Component>()
                        .Any(c => c != null && !(c is Transform) && !IsSelfDrivingComponent(c))
                )
                    used.Add(t);

            var animator = root.GetComponent<Animator>();
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
                for (var i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    var bone = animator.GetBoneTransform((HumanBodyBones)i);
                    if (bone != null)
                        used.Add(bone);
                }

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.rootBone != null)
                    used.Add(smr.rootBone);
                if (smr.probeAnchor != null)
                    used.Add(smr.probeAnchor);
                if (smr.bones == null)
                    continue;
                foreach (var bone in smr.bones)
                    if (bone != null)
                        used.Add(bone);
            }

            foreach (var t in CollectReferencedTransforms(root, null, skipShakers: true))
                used.Add(t);

            used.Add(root);

            var destroyed = 0;
            var destroyedPaths = new List<string>();
            var order = root.GetComponentsInChildren<Transform>(true)
                .OrderByDescending(Depth)
                .ToList();
            foreach (var t in order)
            {
                if (t == null || used.Contains(t) || t.childCount > 0)
                    continue;
                if (HasWorkingShaker(t, root, used))
                    continue;
                destroyedPaths.Add(Path(root, t));
                Object.DestroyImmediate(t.gameObject);
                destroyed++;
            }

            if (destroyed > 0)
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameSweepPass: destroyed {destroyed} unused bone(s):\n  "
                        + string.Join("\n  ", destroyedPaths)
                );
            return destroyed;
        }

        /// <summary>この Transform に載っている揺れ物が、まだ仕事をしているか。</summary>
        static bool HasWorkingShaker(Transform t, Transform root, HashSet<Transform> used)
        {

            var components = t.GetComponents<Component>()
                .Where(c => c != null && IsShakerComponent(c))
                .ToList();
            if (components.Count == 0)
                return false;

            var vrcShakers = components
                .Where(c =>
                    c is VRC.Dynamics.VRCPhysBoneBase || c is VRC.Dynamics.VRCPhysBoneColliderBase
                )
                .ToList();
            if (vrcShakers.Count == 0)
                return true;

            foreach (var shaker in vrcShakers)
            {
                if (shaker is VRC.Dynamics.VRCPhysBoneBase physBone)
                {
                    var target =
                        physBone.rootTransform != null ? physBone.rootTransform : t;
                    if (
                        target != t
                        && target.GetComponentsInChildren<Transform>(true).Any(used.Contains)
                    )
                        return true;
                }
                else if (shaker is VRC.Dynamics.VRCPhysBoneColliderBase collider)
                {
                    foreach (var other in root.GetComponentsInChildren<VRC.Dynamics.VRCPhysBoneBase>(
                        true
                    ))
                        if (other.colliders != null && other.colliders.Contains(collider))
                            return true;
                }
            }
            return false;
        }

        /// <summary>VRCAvatarParameterDriver の書き込みのうち、書いた先を誰も読まないものを 取り除く。</summary>
        static int RemoveDriversNobodyReads(BuildContext context, AnimatorServicesContext asc)
        {
            var controllers = asc.ControllerContext.Controllers.Values
                .Where(c => c != null)
                .Distinct()
                .ToList();
            if (controllers.Count == 0)
                return 0;

            var read = new HashSet<string>();
            foreach (var controller in controllers)
            foreach (var layer in controller.Layers)
                if (layer?.StateMachine != null)
                    CollectReadParameters(layer.StateMachine, read);

            var expressionParameters = context.AvatarDescriptor == null
                ? null
                : context.AvatarDescriptor.expressionParameters;
            if (expressionParameters?.parameters != null)
                foreach (var parameter in expressionParameters.parameters)
                    if (parameter != null && !string.IsNullOrEmpty(parameter.name))
                        read.Add(parameter.name);

            var removed = 0;
            foreach (var controller in controllers)
            foreach (var layer in controller.Layers)
                if (layer?.StateMachine != null)
                    removed += StripUnreadDrivers(layer.StateMachine, read, controller.Name, layer.Name);
            return removed;
        }

        /// <summary>誰かが「読んでいる」パラメーター名を集める。</summary>
        static void CollectReadParameters(VirtualStateMachine sm, HashSet<string> read)
        {
            void FromTransitions(IEnumerable<VirtualTransitionBase> transitions)
            {
                if (transitions == null)
                    return;
                foreach (var transition in transitions)
                {
                    if (transition?.Conditions == null)
                        continue;
                    foreach (var condition in transition.Conditions)
                        read.Add(condition.parameter);
                }
            }

            void FromMotion(VirtualMotion motion)
            {
                if (!(motion is VirtualBlendTree tree))
                    return;
                read.Add(tree.BlendParameter);
                read.Add(tree.BlendParameterY);
                foreach (var child in tree.Children)
                {
                    read.Add(child.DirectBlendParameter);
                    FromMotion(child.Motion);
                }
            }

            foreach (var childState in sm.States)
            {
                var state = childState.State;
                if (state == null)
                    continue;
                FromTransitions(state.Transitions);
                FromMotion(state.Motion);

                foreach (
                    var name in new[]
                    {
                        state.SpeedParameter,
                        state.TimeParameter,
                        state.MirrorParameter,
                        state.CycleOffsetParameter,
                    }
                )
                    if (!string.IsNullOrEmpty(name))
                        read.Add(name);

                foreach (var behaviour in state.Behaviours)
                    if (behaviour is VRCAvatarParameterDriver driver && driver.parameters != null)
                        foreach (var entry in driver.parameters)
                            if (!string.IsNullOrEmpty(entry.source))
                                read.Add(entry.source);
            }
            FromTransitions(sm.AnyStateTransitions);
            FromTransitions(sm.EntryTransitions);
            foreach (var child in sm.StateMachines)
                CollectReadParameters(child.StateMachine, read);
        }

        static int StripUnreadDrivers(
            VirtualStateMachine sm,
            HashSet<string> read,
            string controllerName,
            string layerName
        )
        {
            var removed = 0;
            foreach (var childState in sm.States)
            {
                var state = childState.State;
                if (state == null || state.Behaviours.Count == 0)
                    continue;

                var behaviours = state.Behaviours;
                var changed = false;
                var kept = new List<StateMachineBehaviour>(behaviours.Count);
                foreach (var behaviour in behaviours)
                {
                    if (!(behaviour is VRCAvatarParameterDriver driver) || driver.parameters == null)
                    {
                        kept.Add(behaviour);
                        continue;
                    }
                    var keptEntries = driver.parameters.Where(e => read.Contains(e.name)).ToList();
                    if (keptEntries.Count == driver.parameters.Count)
                    {
                        kept.Add(behaviour);
                        continue;
                    }
                    foreach (var dropped in driver.parameters.Where(e => !read.Contains(e.name)))
                        Debug.LogWarning(
                            $"[ReFrameCore] ReFrameSweepPass: dropping driver write "
                                + $"'{dropped.name}' on '{controllerName}/{layerName}/{state.Name}' — nobody reads it."
                        );
                    removed += driver.parameters.Count - keptEntries.Count;
                    changed = true;
                    if (keptEntries.Count == 0)
                        continue;
                    driver.parameters = keptEntries;
                    kept.Add(behaviour);
                }
                if (changed)
                    state.Behaviours = kept.ToImmutableList();
            }
            foreach (var child in sm.StateMachines)
                removed += StripUnreadDrivers(child.StateMachine, read, controllerName, layerName);
            return removed;
        }

        /// <summary>全てのクリップが「もう存在しないパス」しか動かしていないレイヤーを取り除く。</summary>
        static int RemoveLayersWithNoLivingTargets(
            BuildContext context,
            Transform root,
            AnimatorServicesContext asc
        )
        {
            var vcc = asc.ControllerContext;
            var removed = 0;

            foreach (var controller in vcc.Controllers.Values.Where(c => c != null).Distinct())
            {
                var first = controller.Layers.FirstOrDefault();
                controller.RemoveLayers(layer =>
                {
                    if (layer == null || ReferenceEquals(layer, first))
                        return false;
                    if (layer.StateMachine == null)
                        return false;
                    if (HasBehaviour(layer.StateMachine))
                        return false;

                    var sawBinding = false;
                    foreach (var clip in CollectClips(layer.StateMachine))
                    foreach (var binding in clip.GetFloatCurveBindings()
                        .Concat(clip.GetObjectCurveBindings()))
                    {
                        sawBinding = true;
                        if (string.IsNullOrEmpty(binding.path) || Resolves(binding.path, root, asc))
                            return false;
                    }

                    Debug.LogWarning(
                        $"[ReFrameCore] ReFrameSweepPass: removing layer '{layer.Name}' from "
                            + $"'{controller.Name}' — "
                            + (sawBinding ? "every animated path is gone." : "it animates nothing.")
                    );
                    removed++;
                    return true;
                });
            }
            return removed;
        }

        /// <summary>クリップのバインディングのパスが、まだアバター上の実オブジェクトに解決できるか。</summary>
        static bool Resolves(string path, Transform root, AnimatorServicesContext asc)
        {
            var mapped = asc.ObjectPathRemapper.GetObjectForPath(path);
            if (mapped != null)
                return true;
            return root.Find(path) != null;
        }

        static IEnumerable<VirtualClip> CollectClips(VirtualStateMachine sm)
        {
            foreach (var childState in sm.States)
            {
                var motion = childState.State?.Motion;
                foreach (var clip in CollectClips(motion))
                    yield return clip;
            }
            foreach (var child in sm.StateMachines)
            foreach (var clip in CollectClips(child.StateMachine))
                yield return clip;
        }

        static IEnumerable<VirtualClip> CollectClips(VirtualMotion motion)
        {
            switch (motion)
            {
                case VirtualClip clip:
                    yield return clip;
                    break;
                case VirtualBlendTree tree:
                    foreach (var child in tree.Children)
                    foreach (var clip in CollectClips(child.Motion))
                        yield return clip;
                    break;
            }
        }

        /// <summary>レイヤーのどこかに StateMachineBehaviour が載っているか。</summary>
        static bool HasBehaviour(VirtualStateMachine sm)
        {
            if (sm.Behaviours.Count > 0)
                return true;
            foreach (var childState in sm.States)
                if (childState.State != null && childState.State.Behaviours.Count > 0)
                    return true;
            foreach (var child in sm.StateMachines)
                if (HasBehaviour(child.StateMachine))
                    return true;
            return false;
        }

        /// <summary>アバター配下のコンポーネントが参照している Transform / GameObject を集める。</summary>
        static HashSet<Transform> CollectReferencedTransforms(
            Transform root,
            HashSet<Transform> skipOwnersIn,
            bool skipShakers = false
        )
        {
            var result = new HashSet<Transform>();
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform)
                    continue;
                if (skipShakers && IsShakerComponent(component))
                    continue;
                if (skipOwnersIn != null && skipOwnersIn.Contains(component.transform))
                    continue;

                using var so = new SerializedObject(component);
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference)
                        continue;
                    if (IsWeakColliderReference(it) || IsNetworkIdReference(it))
                        continue;
                    var target = AsTransform(it.objectReferenceValue);
                    if (target != null && target != component.transform && target.IsChildOf(root))
                        result.Add(target);
                }
            }
            return result;
        }

        /// <summary>「コライダーのリストに入っているだけ」の参照かどうか。</summary>
        static bool IsWeakColliderReference(SerializedProperty property) =>
            property.propertyPath.Contains(".Array.data[")
            && property.propertyPath.IndexOf("collider", System.StringComparison.OrdinalIgnoreCase)
                >= 0;

        /// <summary>VRCAvatarDescriptor.networkIDs からの参照かどうか。</summary>
        static bool IsNetworkIdReference(SerializedProperty property) =>
            property.propertyPath.StartsWith("networkIDs.");

        /// <summary>破棄されたオブジェクトを指したままになった networkIDs のエントリを取り除く。</summary>
        static int CompactNetworkIds(Transform root)
        {
            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
                return 0;

            using var so = new SerializedObject(descriptor);
            var array = so.FindProperty("networkIDs");
            if (array == null || !array.isArray)
                return 0;

            var removed = 0;
            for (var i = array.arraySize - 1; i >= 0; i--)
            {
                var element = array.GetArrayElementAtIndex(i);
                var target = element.FindPropertyRelative("gameObject");
                if (target == null || target.objectReferenceValue != null)
                    continue;
                array.DeleteArrayElementAtIndex(i);
                removed++;
            }
            if (removed > 0)
                so.ApplyModifiedPropertiesWithoutUndo();
            return removed;
        }

        /// <summary>破棄によって null が空いたコライダーのリストから、その要素を取り除く。</summary>
        static int CompactColliderLists(Transform root)
        {
            var removed = 0;
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform)
                    continue;

                using var so = new SerializedObject(component);

                var paths = new List<string>();
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (!it.isArray || it.propertyType == SerializedPropertyType.String)
                        continue;
                    if (!it.arrayElementType.StartsWith("PPtr<"))
                        continue;
                    if (
                        it.propertyPath.IndexOf(
                            "collider",
                            System.StringComparison.OrdinalIgnoreCase
                        ) >= 0
                    )
                        paths.Add(it.propertyPath);
                }
                if (paths.Count == 0)
                    continue;

                var dirty = false;
                foreach (var path in paths)
                {
                    var array = so.FindProperty(path);
                    if (array == null || !array.isArray)
                        continue;
                    for (var i = array.arraySize - 1; i >= 0; i--)
                    {
                        if (array.GetArrayElementAtIndex(i).objectReferenceValue != null)
                            continue;
                        array.DeleteArrayElementAtIndex(i);
                        removed++;
                        dirty = true;
                    }
                }
                if (dirty)
                    so.ApplyModifiedPropertiesWithoutUndo();
            }
            return removed;
        }

        /// <summary>揺らす相手を失った揺れ物を取り除く。</summary>
        static int RemoveStrayShakers(Transform root)
        {
            var removed = 0;
            foreach (var component in root.GetComponentsInChildren<Component>(true).ToList())
            {
                if (component == null || !IsShakerComponent(component))
                    continue;

                var physBone = component as VRC.Dynamics.VRCPhysBoneBase;
                if (physBone == null)
                    continue;

                var stray =
                    physBone.rootTransform == null
                        ? component.transform.childCount == 0
                        : false;

                if (!stray)
                    continue;

                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameSweepPass: removing {component.GetType().Name} on "
                        + $"'{Path(root, component.transform)}' — nothing left to move."
                );
                Object.DestroyImmediate(component);
                removed++;
            }
            return removed;
        }

        static Transform AsTransform(Object obj)
        {
            switch (obj)
            {
                case Transform t:
                    return t;
                case GameObject go:
                    return go.transform;
                case Component c:
                    return c.transform;
                default:
                    return null;
            }
        }

        static int Depth(Transform t)
        {
            var depth = 0;
            for (var cur = t; cur != null; cur = cur.parent)
                depth++;
            return depth;
        }

        static string Path(Transform root, Transform t) =>
            AnimationUtility.CalculateTransformPath(t, root);
    }
}
