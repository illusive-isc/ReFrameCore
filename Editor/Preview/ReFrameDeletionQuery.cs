using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using jp.illusive_isc.ReFrame.Core;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>
    /// 他パッケージ (ReFrameAdd 等) 向けの公開 API: シーン上のアバターで「ReFrame のビルドで削除される (= 残らない) Renderer」を返す。
    /// Hierarchy プレビューと同じ解決 (Enabled な削除対象で固定される非アクティブ / 削除オブジェクト / 掃除の近似) を ComputeContext 無しで行う。
    /// 重いので短時間キャッシュする。ギズモや検査など「削除予定のものを判定から除きたい」用途向け。
    /// </summary>
    public static class ReFrameDeletionQuery
    {
        static readonly Dictionary<int, (double time, HashSet<Renderer> set)> Cache = new();
        const double CacheSeconds = 2.0;

        public static bool IsDeleted(Renderer renderer)
        {
            if (renderer == null) return false;
            var descriptor = renderer.GetComponentInParent<VRCAvatarDescriptor>();
            return descriptor != null && DeletedRenderers(descriptor).Contains(renderer);
        }

        public static HashSet<Renderer> DeletedRenderers(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) return new HashSet<Renderer>();
            var id = descriptor.GetInstanceID();
            var now = UnityEditor.EditorApplication.timeSinceStartup;
            if (Cache.TryGetValue(id, out var hit) && now - hit.time < CacheSeconds) return hit.set;
            var set = Compute(descriptor);
            Cache[id] = (now, set);
            return set;
        }

        static HashSet<Renderer> Compute(VRCAvatarDescriptor descriptor)
        {
            var result = new ReFrameBakedVisibilityResolver.ForcedStates();
            var deleted = new HashSet<Renderer>();
            var components = ReFrameDeleteComponent.FilterActive(descriptor.GetComponentsInChildren<ReFrameDeleteComponent>(true));
            if (components.Length == 0) return deleted;

            var allTargets = new List<(string Name, float Value)>();
            var allOverrides = new List<(string TreeName, string ParameterName, float Value, bool Always)>();
            var allDeletedObjectPaths = new List<string>();
            foreach (var component in components)
            {
                allDeletedObjectPaths.AddRange(component.EnumerateDeleteObjectPaths());
                allTargets.AddRange(component.EnumerateDeleteTargets().Select(t => (t.ParameterName, t.Value)));
                allOverrides.AddRange(component.EnumerateBlendTreeOverrides());
            }
            var allValues = ReFrameBakedVisibilityResolver.BuildDefaultParameterValues(descriptor);
            foreach (var t in allTargets) allValues[t.Name] = t.Value;
            var treeOverrides = ReFrameAnimatorUtil.BuildTreeOverrides(allOverrides, allTargets.Select(t => t.Name));

            var controllers = ReFrameBakedVisibilityResolver.CollectFxControllers(descriptor);
            ReFrameBakedVisibilityResolver.BeginResolveScope();
            try
            {
                foreach (var (controller, pathRoot) in controllers)
                    ReFrameBakedVisibilityResolver.ResolveActiveStates(controller, pathRoot, result, allValues, treeOverrides);
            }
            finally
            {
                ReFrameBakedVisibilityResolver.EndResolveScope();
            }
            ReFrameBakedVisibilityResolver.ResolveDeletedObjects(allDeletedObjectPaths, descriptor.transform, result);
            ReFrameBakedVisibilityResolver.ResolveSweptObjects(descriptor, result);

            foreach (var r in descriptor.GetComponentsInChildren<Renderer>(true))
            {
                if (result.RendererEnabled.TryGetValue(r, out var en) && !en) { deleted.Add(r); continue; }
                for (var t = r.transform; t != null && t != descriptor.transform; t = t.parent)
                {
                    if (result.GameObjectActive.TryGetValue(t, out var active) && !active) { deleted.Add(r); break; }
                }
            }
            return deleted;
        }
    }
}
