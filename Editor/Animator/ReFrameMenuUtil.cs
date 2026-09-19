using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>ReFrame 共通関数 (メニュー操作)。</summary>
    public static class ReFrameMenuUtil
    {
        /// <summary>メニューツリー全体を複製し、各メニューを NDMF の一時アセットとして登録する。</summary>
        public static VRCExpressionsMenu CloneMenuTree(
            VRCExpressionsMenu source,
            IAssetSaver saver,
            Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> cache = null
        )
        {
            if (source == null)
                return null;
            cache ??= new Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>();
            if (cache.TryGetValue(source, out var cached))
                return cached;

            VRCExpressionsMenu clone;
            if (saver != null && saver.IsTemporaryAsset(source))
            {

                clone = source;
            }
            else
            {
                clone = Object.Instantiate(source);
                clone.name = source.name;
                saver?.SaveAsset(clone);
                // 後段のツール (TailEmbrace 等) が「元アセットへの参照」で差し込み先を探すので、
                // ObjectRegistry に元→複製を登録して追跡できるようにする。
                ObjectRegistry.RegisterReplacedObject(source, clone);
            }

            cache[source] = clone;
            cache[clone] = clone;

            foreach (var control in clone.controls)
            {
                if (control == null)
                    continue;
                if (
                    control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && control.subMenu != null
                )
                    control.subMenu = CloneMenuTree(control.subMenu, saver, cache);
            }
            return clone;
        }

        /// <summary>アバターの expressionsMenu をツリーごと複製して差し替え、複製したルートを返す。</summary>
        public static VRCExpressionsMenu ReplaceMenuWithClone(
            BuildContext context,
            Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> cache = null
        )
        {
            var descriptor = context.AvatarDescriptor;
            if (descriptor == null || descriptor.expressionsMenu == null)
                return null;
            var clone = CloneMenuTree(descriptor.expressionsMenu, context.AssetSaver, cache);
            descriptor.expressionsMenu = clone;
            return clone;
        }

        /// <summary>メニューツリー内 (サブメニューも再帰的に) から、指定した複数のパラメーターのいずれかを 参照しているコントロールをすべて削除する。</summary>
        public static int RemoveControlsByParameter(
            VRCExpressionsMenu menu,
            params string[] parameterNames
        )
        {
            if (menu == null || parameterNames == null || parameterNames.Length == 0)
                return 0;
            var names = new HashSet<string>(parameterNames);
            return RemoveControlsByParameter(menu, names, new HashSet<VRCExpressionsMenu>());
        }

        /// <summary>アバターの expressionsMenu から、指定した複数のパラメーターのいずれかを参照している コントロールをすべて削除する。</summary>
        public static int RemoveControlsByParameter(BuildContext context, params string[] parameterNames)
        {
            var clone = ReplaceMenuWithClone(context);
            return clone == null ? 0 : RemoveControlsByParameter(clone, parameterNames);
        }

        static int RemoveControlsByParameter(
            VRCExpressionsMenu menu,
            HashSet<string> parameterNames,
            HashSet<VRCExpressionsMenu> visited
        )
        {
            if (menu == null || !visited.Add(menu))
                return 0;

            var removed = menu.controls.RemoveAll(c => c != null && UsesParameter(c, parameterNames));

            foreach (var control in menu.controls)
            {
                if (
                    control != null
                    && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && control.subMenu != null
                )
                    removed += RemoveControlsByParameter(control.subMenu, parameterNames, visited);
            }
            return removed;
        }

        /// <summary>subParameters を実際に使うコントロールか (TwoAxis / FourAxis / RadialPuppet)。</summary>
        public static bool IsPuppet(VRCExpressionsMenu.Control.ControlType type) =>
            type == VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet
            || type == VRCExpressionsMenu.Control.ControlType.FourAxisPuppet
            || type == VRCExpressionsMenu.Control.ControlType.RadialPuppet;

        static bool UsesParameter(VRCExpressionsMenu.Control control, HashSet<string> parameterNames)
        {
            if (control.parameter != null && parameterNames.Contains(control.parameter.name))
                return true;
            if (!IsPuppet(control.type) || control.subParameters == null)
                return false;
            foreach (var p in control.subParameters)
            {
                if (p != null && parameterNames.Contains(p.name))
                    return true;
            }
            return false;
        }

        /// <summary>ツリー内 (サブメニューも再帰的に) の各メニューの現在のコントロール数を控える。</summary>
        public static Dictionary<VRCExpressionsMenu, int> SnapshotControlCounts(VRCExpressionsMenu menu)
        {
            var counts = new Dictionary<VRCExpressionsMenu, int>();
            SnapshotControlCounts(menu, counts);
            return counts;
        }

        static void SnapshotControlCounts(VRCExpressionsMenu menu, Dictionary<VRCExpressionsMenu, int> counts)
        {
            if (menu == null || counts.ContainsKey(menu))
                return;
            counts[menu] = menu.controls.Count(c => c != null);
            foreach (var control in menu.controls)
            {
                if (
                    control != null
                    && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && control.subMenu != null
                )
                    SnapshotControlCounts(control.subMenu, counts);
            }
        }

        /// <summary>控えた時点で既に空だったメニューを返す。ReFrame が空にしたのではないので PruneEmptySubMenus の protect に渡す。</summary>
        public static HashSet<VRCExpressionsMenu> EmptyMenus(Dictionary<VRCExpressionsMenu, int> snapshot)
        {
            var result = new HashSet<VRCExpressionsMenu>();
            if (snapshot == null)
                return result;
            foreach (var pair in snapshot)
            {
                if (pair.Key != null && pair.Value == 0)
                    result.Add(pair.Key);
            }
            return result;
        }

        /// <summary>控えた時点からコントロール数が変わっていない (ReFrame が手を付けていない) メニューを返す。控えに無いメニューは含めない。</summary>
        public static HashSet<VRCExpressionsMenu> UntouchedMenus(Dictionary<VRCExpressionsMenu, int> snapshot)
        {
            var result = new HashSet<VRCExpressionsMenu>();
            if (snapshot == null)
                return result;
            foreach (var pair in snapshot)
            {
                if (pair.Key != null && pair.Key.controls.Count(c => c != null) == pair.Value)
                    result.Add(pair.Key);
            }
            return result;
        }

        /// <summary>
        /// 「サブメニュー 1 個しか入っていないサブメニュー」の連鎖を畳む。削除で中身が減った結果
        /// A → B → (中身) のように B が A の唯一の項目になった場合、A のコントロールを直接 B の中身へ
        /// 向け直して階層を 1 段減らす (名前とアイコンは A のまま。A にアイコンが無ければ B のを使う)。
        /// 一番奥から調べるので、何段の連鎖でもまとめて畳まれる。ルートメニュー自体は畳まない。
        /// protect に含まれる SubMenu アセット (MenuInstaller の差し込み先など、後から中身が増えるもの)
        /// は「1 個しか無い」と判定しない。戻り値は畳んだ段数。
        /// </summary>
        public static int CollapseSingleSubMenuChains(VRCExpressionsMenu menu, ISet<VRCExpressionsMenu> protect = null)
        {
            if (menu == null)
                return 0;
            return CollapseSingleSubMenuChains(menu, new HashSet<VRCExpressionsMenu>(), protect);
        }

        static int CollapseSingleSubMenuChains(
            VRCExpressionsMenu menu,
            HashSet<VRCExpressionsMenu> visited,
            ISet<VRCExpressionsMenu> protect
        )
        {
            if (menu == null || !visited.Add(menu))
                return 0;

            var collapsed = 0;
            foreach (var control in menu.controls)
            {
                if (
                    control == null
                    || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu
                    || control.subMenu == null
                )
                    continue;

                collapsed += CollapseSingleSubMenuChains(control.subMenu, visited, protect);

                // 中身がサブメニュー 1 個だけなら、その中身へ直接向ける (何段でも)。
                // A → B → A のように循環しているメニューで回り続けないよう、辿った先を覚えておく。
                var walked = new HashSet<VRCExpressionsMenu>();
                while (true)
                {
                    var inner = control.subMenu;
                    if (inner == null || (protect != null && protect.Contains(inner)) || !walked.Add(inner))
                        break;
                    var items = inner.controls.Where(c => c != null).ToList();
                    if (items.Count != 1)
                        break;
                    var only = items[0];
                    if (only.type != VRCExpressionsMenu.Control.ControlType.SubMenu || only.subMenu == null)
                        break;
                    if (control.icon == null)
                        control.icon = only.icon;
                    control.subMenu = only.subMenu;
                    collapsed++;
                }
            }
            return collapsed;
        }

        /// <summary>メニューツリーを一番奥の階層から調べ、コントロールが 0 件になったサブメニューへの SubMenu コントロールを削除する。subMenu が未設定のコントロールは ReFrame が空にしたものではないので触らない。</summary>
        public static int PruneEmptySubMenus(VRCExpressionsMenu menu)
        {
            if (menu == null)
                return 0;
            return PruneEmptySubMenus(menu, new HashSet<VRCExpressionsMenu>(), null);
        }

        /// <summary>protect に含まれる SubMenu アセットは、現時点で controls が 0 件でも 「空」とは判定せず取り除かない。</summary>
        public static int PruneEmptySubMenus(VRCExpressionsMenu menu, ISet<VRCExpressionsMenu> protect)
        {
            if (menu == null)
                return 0;
            return PruneEmptySubMenus(menu, new HashSet<VRCExpressionsMenu>(), protect);
        }

        /// <summary>アバターの expressionsMenu を一番奥の階層から調べ、空になったサブメニューへの コントロールを削除する。</summary>
        public static int PruneEmptySubMenus(BuildContext context)
        {
            var clone = ReplaceMenuWithClone(context);
            return clone == null ? 0 : PruneEmptySubMenus(clone);
        }

        static int PruneEmptySubMenus(
            VRCExpressionsMenu menu,
            HashSet<VRCExpressionsMenu> visited,
            ISet<VRCExpressionsMenu> protect
        )
        {
            if (menu == null || !visited.Add(menu))
                return 0;

            var removed = 0;

            foreach (var control in menu.controls)
            {
                if (
                    control != null
                    && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && control.subMenu != null
                )
                    removed += PruneEmptySubMenus(control.subMenu, visited, protect);
            }

            removed += menu.controls.RemoveAll(
                c =>
                    c != null
                    && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && c.subMenu != null
                    && c.subMenu.controls.Count == 0
                    && (protect == null || !protect.Contains(c.subMenu))
            );
            return removed;
        }

        /// <summary>名前でサブメニューを取得する。</summary>
        public static VRCExpressionsMenu FindSubMenu(VRCExpressionsMenu menu, string name)
        {
            if (menu == null)
                return null;
            foreach (var control in menu.controls)
            {
                if (
                    control != null
                    && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && control.name == name
                    && control.subMenu != null
                )
                    return control.subMenu;
            }
            return null;
        }
    }
}
