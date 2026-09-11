using System.Collections.Generic;
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

        /// <summary>メニューツリーを一番奥の階層から調べ、コントロールが 0 件になったサブメニューへの SubMenu コントロールを削除する。</summary>
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
                    && (c.subMenu == null || c.subMenu.controls.Count == 0)
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
