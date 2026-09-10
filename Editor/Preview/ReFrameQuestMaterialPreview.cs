using System.Collections.Generic;
using System.Collections.Immutable;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>「Quest 簡易対応」を入れたときの見た目を、シーンビューでそのまま確かめられるようにする。</summary>
    internal static class ReFrameQuestMaterialPreview
    {
        /// <summary>変換済みマテリアルの使い回し。</summary>
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        [InitializeOnLoadMethod]
        static void RegisterCleanup()
        {

            AssemblyReloadEvents.beforeAssemblyReload += DestroyAll;
        }

        /// <summary>使い回しをやめる。</summary>
        internal static void Clear()
        {
            Cache.Clear();
            _hidden = null;
        }

        /// <summary>実体ごと捨てる。</summary>
        internal static void DestroyAll()
        {
            foreach (var material in Cache.Values)
                if (material != null)
                    Object.DestroyImmediate(material);
            Cache.Clear();
            if (_hidden != null)
                Object.DestroyImmediate(_hidden);
            _hidden = null;
        }

        static Shader _toonLit;
        static Transform _preparedFor;

        /// <summary>ToonLit シェーダーの検索は毎回やると重いので覚えておく。</summary>
        static Shader CachedToonLit()
        {
            if (_toonLit == null)
                _toonLit = Shader.Find(ReFrameQuestMaterialConverter.ToonLitShaderName);
            return _toonLit;
        }

        /// <summary>同じアバターについて 1 回だけ設定を読み直す。</summary>
        static void PrepareSettingsOnce(Transform root)
        {
            if (_preparedFor == root)
                return;
            ReFrameQuestMaterialConverter.PrepareSettings(root);
            _preparedFor = root;
        }

        /// <summary>解決パスの区切り。次の Apply で設定を読み直させる。</summary>
        internal static void ResetScope() => _preparedFor = null;

        /// <summary>この Renderer のマテリアル差し替えに、Quest 変換後のものを足して返す。</summary>
        internal static ImmutableDictionary<int, Material> Apply(
            ComputeContext context,
            VRCAvatarDescriptor descriptor,
            Renderer renderer,
            ImmutableDictionary<int, Material> overrides
        )
        {
            if (descriptor == null || renderer == null)
                return overrides;

            if (renderer is ParticleSystemRenderer)
                return overrides;

            if (!IsQuestEnabled(context, descriptor))
                return overrides;

            var materials = context.Observe(renderer, r => r.sharedMaterials, SequenceEqual);
            if (materials == null || materials.Length == 0)
                return overrides;

            // Shader.Find と PrepareSettings (中で階層を 3 回走査する) は Renderer ごとに
            // やり直す必要が無いので、1 回の解決パスの中では使い回す。
            var toonLit = CachedToonLit();
            if (toonLit == null)
                return overrides;

            PrepareSettingsOnce(descriptor.transform);

            var subMeshes = ReFrameQuestMaterialConverter.SubMeshCountOf(renderer);

            var builder = overrides.ToBuilder();
            for (var i = 0; i < materials.Length; i++)
            {

                if (subMeshes > 0 && i >= subMeshes)
                {
                    var hidden = HiddenMaterial();
                    if (hidden != null)
                        builder[i] = hidden;
                    continue;
                }

                if (ReFrameQuestMaterialConverter.IsDroppedSlot(renderer, i))
                {
                    var hidden = HiddenMaterial();
                    if (hidden != null)
                        builder[i] = hidden;
                    continue;
                }

                if (builder.ContainsKey(i))
                    continue;
                var source = materials[i];
                if (source == null || source.shader == null)
                    continue;

                if (source.shader.name.StartsWith("VRChat/Mobile/"))
                    continue;

                var converted = GetOrCreate(source, toonLit);
                if (converted != null)
                    builder[i] = converted;
            }
            return builder.ToImmutable();
        }

        /// <summary>アバターに「Quest 簡易対応」が ON の ReFrame があるか (変更を監視する)。</summary>
        static bool IsQuestEnabled(ComputeContext context, VRCAvatarDescriptor descriptor)
        {
            var components = context.GetComponentsInChildren<ReFrameDeleteComponent>(
                descriptor.gameObject,
                true
            );

            components = ReFrameDeleteComponent.FilterActive(components);
            var enabled = false;
            foreach (var component in components)
            {
                if (component == null)
                    continue;

                var settings = context.Observe(
                    component,
                    c =>
                        (
                            c.QuestConversionActive,
                            c.questBakeOverride,
                            c.questTextureBrightness,
                            c.questShadowFromNormalMap,
                            c.questMaxTextureSize,
                            c.questBackdropOpacity,
                            (UnityEngine.Object)c.questBakeSet
                        )
                );
                if (settings.QuestConversionActive)
                    enabled = true;
            }
            return enabled;
        }

        static Material _hidden;

        /// <summary>プレビューで枠を消すための「何も描かない」マテリアル。</summary>
        static Material HiddenMaterial()
        {
            if (_hidden != null)
                return _hidden;
            var shader = Shader.Find("Hidden/ReFrame/PreviewHidden");
            if (shader == null)
                return null;
            _hidden = new Material(shader)
            {
                name = "ReFrame Preview Hidden",
                hideFlags = HideFlags.HideAndDontSave,
            };
            return _hidden;
        }

        static Material GetOrCreate(Material source, Shader toonLit)
        {

            var prebaked = ReFrameQuestMaterialConverter.FindBaked(source);
            if (prebaked != null)
                return prebaked;

            var key = source.GetInstanceID() + "|" + ReFrameQuestMaterialConverter.BakeKey(source);
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var material = ReFrameQuestMaterialConverter.CreateToonLit(source, toonLit, out var baked);
            if (material == null)
                return null;
            material.hideFlags = HideFlags.HideAndDontSave;
            if (baked != null)
                baked.hideFlags = HideFlags.HideAndDontSave;
            Cache[key] = material;
            return material;
        }

        static bool SequenceEqual(Material[] a, Material[] b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (var i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}
