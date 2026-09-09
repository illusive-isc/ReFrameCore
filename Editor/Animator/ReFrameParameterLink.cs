using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>「メニューで触るパラメーター」と「実際に効いているパラメーター」の対応表。</summary>
    internal static class ReFrameParameterLink
    {
        /// <summary>1 本のパラメーターにぶら下がる連携先。</summary>
        internal sealed class Link
        {
            /// <summary>連携先のパラメーター名。</summary>
            public string Name;

            /// <summary>どういう繋がりか (表示用)。</summary>
            public string Reason;

            /// <summary>同期ビット。</summary>
            public int Bits;

            /// <summary>消すときに何の値で固定するか。</summary>
            public bool UseOwnerValue;

            /// <summary>UseOwnerValue が false のときに固定する値。</summary>
            public float DefaultValue;
        }

        /// <summary>アバターを走査して「このパラメーターが消えるなら、これも道連れで消える」表を作る。</summary>
        internal static Dictionary<string, List<Link>> Build(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<string, List<Link>>();
            if (descriptor == null)
                return result;

            var bits = CollectBits(descriptor);
            var defaults = CollectDefaults(descriptor);

            foreach (var controller in AllControllers(descriptor))
                CollectCopies(controller, bits, defaults, result);

            CollectPuppetAxes(descriptor.expressionsMenu, bits, defaults, result, new HashSet<Object>());

            return result;
        }

        /// <summary>そのパラメーターと連携先を合わせた同期ビット。</summary>
        internal static int TotalBits(
            string parameter,
            IReadOnlyDictionary<string, int> bits,
            IReadOnlyDictionary<string, List<Link>> links
        )
        {
            var total = bits.TryGetValue(parameter, out var own) ? own : 0;
            if (links.TryGetValue(parameter, out var list))
                foreach (var link in list)
                    total += link.Bits;
            return total;
        }

        /// <summary>パラメーター名 → 同期ビット (同期しないものは 0)。</summary>
        internal static Dictionary<string, int> CollectBits(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<string, int>();
            var parameters = descriptor == null ? null : descriptor.expressionParameters;
            if (parameters?.parameters == null)
                return result;
            foreach (var parameter in parameters.parameters)
            {
                if (string.IsNullOrEmpty(parameter.name))
                    continue;
                var cost = !parameter.networkSynced
                    ? 0
                    : parameter.valueType == VRCExpressionParameters.ValueType.Bool
                        ? 1
                        : 8;

                if (!result.TryGetValue(parameter.name, out var existing) || cost > existing)
                    result[parameter.name] = cost;
            }
            return result;
        }

        /// <summary>パラメーター名 → 既定値。</summary>
        internal static Dictionary<string, float> CollectDefaults(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<string, float>();
            var parameters = descriptor == null ? null : descriptor.expressionParameters;
            if (parameters?.parameters == null)
                return result;
            foreach (var parameter in parameters.parameters)
                if (!string.IsNullOrEmpty(parameter.name))
                    result[parameter.name] = parameter.defaultValue;
            return result;
        }

        static IEnumerable<AnimatorController> AllControllers(VRCAvatarDescriptor descriptor)
        {
            if (descriptor.baseAnimationLayers != null)
                foreach (var layer in descriptor.baseAnimationLayers)
                    if (layer.animatorController is AnimatorController controller)
                        yield return controller;
            if (descriptor.specialAnimationLayers != null)
                foreach (var layer in descriptor.specialAnimationLayers)
                    if (layer.animatorController is AnimatorController controller)
                        yield return controller;

            foreach (
                var merge in descriptor.GetComponentsInChildren<
                    nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator
                >(true)
            )
                if (merge != null && merge.enabled && merge.animator is AnimatorController controller)
                    yield return controller;
        }

        static void CollectCopies(
            AnimatorController controller,
            IReadOnlyDictionary<string, int> bits,
            IReadOnlyDictionary<string, float> defaults,
            Dictionary<string, List<Link>> result
        )
        {
            if (controller?.layers == null)
                return;
            foreach (var layer in controller.layers)
                WalkStates(
                    layer.stateMachine,
                    state =>
                    {
                        foreach (var behaviour in state.behaviours)
                        {
                            if (!(behaviour is VRCAvatarParameterDriver driver))
                                continue;
                            foreach (var parameter in driver.parameters)
                            {
                                if (parameter.type != VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Copy)
                                    continue;
                                if (
                                    string.IsNullOrEmpty(parameter.source)
                                    || string.IsNullOrEmpty(parameter.name)
                                    || parameter.source == parameter.name
                                )
                                    continue;
                                Add(
                                    result,
                                    parameter.source,
                                    parameter.name,
                                    "コピー先",
                                    bits,
                                    true,
                                    defaults
                                );
                            }
                        }
                    }
                );
        }

        static void CollectPuppetAxes(
            VRCExpressionsMenu menu,
            IReadOnlyDictionary<string, int> bits,
            IReadOnlyDictionary<string, float> defaults,
            Dictionary<string, List<Link>> result,
            HashSet<Object> seen
        )
        {
            if (menu == null || !seen.Add(menu))
                return;
            foreach (var control in menu.controls)
            {
                var owner = control.parameter?.name;

                if (
                    !string.IsNullOrEmpty(owner)
                    && ReFrameMenuUtil.IsPuppet(control.type)
                    && control.subParameters != null
                )
                    foreach (var sub in control.subParameters)
                    {
                        if (string.IsNullOrEmpty(sub?.name) || sub.name == owner)
                            continue;
                        Add(result, owner, sub.name, control.type + " の軸", bits, false, defaults);
                    }
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    CollectPuppetAxes(control.subMenu, bits, defaults, result, seen);
            }
        }

        static void Add(
            Dictionary<string, List<Link>> result,
            string owner,
            string linked,
            string reason,
            IReadOnlyDictionary<string, int> bits,
            bool useOwnerValue,
            IReadOnlyDictionary<string, float> defaults
        )
        {
            if (!result.TryGetValue(owner, out var list))
            {
                list = new List<Link>();
                result[owner] = list;
            }
            foreach (var existing in list)
                if (existing.Name == linked)
                    return;
            list.Add(
                new Link
                {
                    Name = linked,
                    Reason = reason,
                    Bits = bits.TryGetValue(linked, out var b) ? b : 0,
                    UseOwnerValue = useOwnerValue,
                    DefaultValue = defaults.TryGetValue(linked, out var d) ? d : 0f,
                }
            );
        }

        static void WalkStates(AnimatorStateMachine machine, System.Action<AnimatorState> visit)
        {
            if (machine == null)
                return;
            foreach (var child in machine.states)
                if (child.state != null)
                    visit(child.state);
            foreach (var child in machine.stateMachines)
                WalkStates(child.stateMachine, visit);
        }
    }
}
