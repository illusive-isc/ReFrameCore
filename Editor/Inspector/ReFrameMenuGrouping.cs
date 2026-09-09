using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>アバターの VRCExpressionsMenu ツリーを走査して、パラメーターごとに 「どのサブメニューの下にあるか」「どんな種類のコントロールから操作されるか」 「メニュー項目がどの値を割り当てるか」を集める。</summary>
    internal static class ReFrameMenuGrouping
    {
        /// <summary>そのパラメーターがメニュー上でどう使われているか。</summary>
        internal sealed class ParameterUsage
        {
            /// <summary>最初に見つかったメニュー項目が入っているサブメニューのパス (ルート直下なら空)。</summary>
            public string[] GroupPath = System.Array.Empty<string>();

            /// <summary>最初に見つかったメニュー項目の名前。</summary>
            public string ControlName;

            /// <summary>RadialPuppet の軸 (subParameter) として使われている。</summary>
            public bool RadialPuppetAxis;

            /// <summary>TwoAxisPuppet / FourAxisPuppet の軸として使われている。</summary>
            public bool TwoOrFourAxisPuppetAxis;

            /// <summary>Toggle / Button / SubMenu がこのパラメーターへ割り当てる値と、その項目名。</summary>
            public readonly List<(float Value, string ControlName)> Choices = new();

            public void AddChoice(float value, string controlName)
            {
                foreach (var choice in Choices)
                {
                    if (Mathf_Approximately(choice.Value, value))
                        return;
                }
                Choices.Add((value, controlName));
            }

            static bool Mathf_Approximately(float a, float b) =>
                UnityEngine.Mathf.Approximately(a, b);
        }

        /// <summary>パラメーター名 → ParameterUsage の対応を作る。</summary>
        internal static Dictionary<string, ParameterUsage> BuildParameterUsages(
            VRCAvatarDescriptor descriptor
        )
        {
            var usages = new Dictionary<string, ParameterUsage>();
            if (descriptor == null)
                return usages;

            var submenuPaths = new Dictionary<VRCExpressionsMenu, string[]>();
            Walk(descriptor.expressionsMenu, System.Array.Empty<string>(), usages, submenuPaths);

            var pending = new List<ModularAvatarMenuInstaller>(
                descriptor.GetComponentsInChildren<ModularAvatarMenuInstaller>(true)
            );

            while (pending.Count > 0)
            {
                var progressed = false;
                for (var i = pending.Count - 1; i >= 0; i--)
                {
                    var installer = pending[i];
                    if (installer == null || installer.menuToAppend == null)
                    {
                        pending.RemoveAt(i);
                        continue;
                    }

                    var target = installer.installTargetMenu;
                    string[] prefix;
                    if (target == null)
                        prefix = System.Array.Empty<string>();
                    else if (!submenuPaths.TryGetValue(target, out prefix))
                        continue;

                    pending.RemoveAt(i);
                    progressed = true;

                    WalkControls(installer.menuToAppend, prefix, usages, submenuPaths);
                }
                if (!progressed)
                    break;
            }

            return usages;
        }

        static void Walk(
            VRCExpressionsMenu menu,
            string[] path,
            Dictionary<string, ParameterUsage> usages,
            Dictionary<VRCExpressionsMenu, string[]> submenuPaths
        )
        {
            if (menu == null || submenuPaths.ContainsKey(menu))
                return;
            submenuPaths[menu] = path;
            WalkControls(menu, path, usages, submenuPaths);
        }

        static void WalkControls(
            VRCExpressionsMenu menu,
            string[] path,
            Dictionary<string, ParameterUsage> usages,
            Dictionary<VRCExpressionsMenu, string[]> submenuPaths
        )
        {
            if (menu == null || menu.controls == null)
                return;

            foreach (var control in menu.controls)
            {
                if (control == null)
                    continue;

                var usage = Touch(usages, control.parameter, path, control.name);
                if (usage != null)
                    usage.AddChoice(control.value, control.name);

                if (control.subParameters != null)
                {
                    var twoOrFour =
                        control.type == VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet
                        || control.type == VRCExpressionsMenu.Control.ControlType.FourAxisPuppet;
                    var radial = control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet;

                    foreach (var sub in control.subParameters)
                    {
                        var subUsage = Touch(usages, sub, path, control.name);
                        if (subUsage == null)
                            continue;
                        if (twoOrFour)
                            subUsage.TwoOrFourAxisPuppetAxis = true;
                        if (radial)
                            subUsage.RadialPuppetAxis = true;
                    }
                }

                if (
                    control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && control.subMenu != null
                )
                {
                    var childPath = new string[path.Length + 1];
                    System.Array.Copy(path, childPath, path.Length);
                    childPath[path.Length] = control.name;
                    Walk(control.subMenu, childPath, usages, submenuPaths);
                }
            }
        }

        /// <summary>パラメーターの ParameterUsage を取得する (無ければ作る)。</summary>
        static ParameterUsage Touch(
            Dictionary<string, ParameterUsage> usages,
            VRCExpressionsMenu.Control.Parameter parameter,
            string[] path,
            string controlName
        )
        {
            if (parameter == null || string.IsNullOrEmpty(parameter.name))
                return null;
            if (!usages.TryGetValue(parameter.name, out var usage))
            {
                usage = new ParameterUsage { GroupPath = path, ControlName = controlName };
                usages[parameter.name] = usage;
            }
            return usage;
        }

        /// <summary>複数パスの共通の親を求める。</summary>
        internal static string[] CommonPrefix(IReadOnlyList<string[]> paths)
        {
            if (paths == null || paths.Count == 0)
                return System.Array.Empty<string>();

            var length = paths[0].Length;
            for (var i = 1; i < paths.Count; i++)
            {
                var other = paths[i];
                var limit = System.Math.Min(length, other.Length);
                var matched = 0;
                while (matched < limit && paths[0][matched] == other[matched])
                    matched++;
                length = matched;
                if (length == 0)
                    break;
            }

            var result = new string[length];
            System.Array.Copy(paths[0], result, length);
            return result;
        }
    }
}
