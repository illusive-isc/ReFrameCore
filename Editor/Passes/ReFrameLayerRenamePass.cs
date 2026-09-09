using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>アバター上の ReFrameDeleteComponent に付いた ReFrameLayerRenameAttribute に従って、ModularAvatarMergeAnimator の コントローラー内のレイヤー名を改名する。</summary>
    [DependsOnContext(typeof(AnimatorServicesContext))]
    public class ReFrameLayerRenamePass : Pass<ReFrameLayerRenamePass>
    {
        protected override void Execute(BuildContext context)
        {
            var renames = CollectRenames(context.AvatarRootObject);
            if (renames.Count == 0)
                return;

            var controllers = context.Extension<AnimatorServicesContext>().ControllerContext;
            var renamed = 0;

            foreach (
                var merge in context.AvatarRootObject.GetComponentsInChildren<ModularAvatarMergeAnimator>(
                    true
                )
            )
            {
                if (merge == null || !merge.enabled)
                    continue;
                if (!renames.TryGetValue(merge.gameObject.name, out var rules))
                    continue;
                if (!controllers.Controllers.TryGetValue(merge, out var controller) || controller == null)
                    continue;

                foreach (var layer in controller.Layers)
                {
                    if (layer == null || !rules.TryGetValue(layer.Name, out var to))
                        continue;
                    layer.Name = to;
                    renamed++;
                }
            }

            if (renamed == 0)
                Debug.LogWarning(
                    "[ReFrameCore] ReFrameLayerRenamePass: "
                        + $"{renames.Sum(r => r.Value.Count)} 件の [ReFrameLayerRename] 宣言に一致するレイヤーが 1 つもありませんでした。"
                );
        }

        /// <summary>MergeAnimator の GameObject 名 → (改名前 → 改名後) の対応を集める。</summary>
        static Dictionary<string, Dictionary<string, string>> CollectRenames(GameObject avatarRoot)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();

            foreach (var component in ReFrameDeleteComponent.ActiveIn(avatarRoot))
            {
                if (component == null)
                    continue;
                foreach (
                    var attribute in component
                        .GetType()
                        .GetCustomAttributes<ReFrameLayerRenameAttribute>(true)
                )
                {
                    if (
                        string.IsNullOrEmpty(attribute.MergeAnimatorObjectName)
                        || string.IsNullOrEmpty(attribute.From)
                        || string.IsNullOrEmpty(attribute.To)
                    )
                        continue;

                    if (!result.TryGetValue(attribute.MergeAnimatorObjectName, out var rules))
                    {
                        rules = new Dictionary<string, string>();
                        result[attribute.MergeAnimatorObjectName] = rules;
                    }
                    rules[attribute.From] = attribute.To;
                }
            }

            return result;
        }
    }
}
