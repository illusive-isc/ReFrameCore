using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>Quest で使えないシェーダーのマテリアルを VRChat/Mobile/Toon Lit へ変換する。</summary>
    internal static class ReFrameQuestMaterialConverter
    {
        internal const string ToonLitShaderName = "VRChat/Mobile/Toon Lit";
        const string LilToonBakerShaderName = "Hidden/ltsother_baker";

        /// <summary>焼いたテクスチャの一辺の上限。</summary>
        static int MaxTextureSize = 1024;

        const string AdjustShaderName = "Hidden/ReFrame/QuestBakeAdjust";
        internal const string ToonStandardOutlineShaderName = "VRChat/Mobile/Toon Standard (Outline)";

        /// <summary>輪郭線を残すマテリアルと、その太さ。</summary>
        static Dictionary<Material, float> OutlineTargets = new Dictionary<Material, float>();

        /// <summary>影色を焼き込むか。</summary>
        static bool ShadowColorsEnabled = true;

        /// <summary>影色をどれだけ効かせるか (0 で無効、1 でそのまま)。</summary>
        static float ShadowColorMix = 1f;

        /// <summary>法線マップが無いときに「どのくらい陰っているとみなすか」。</summary>
        static float FlatLuminance = 0.62f;

        static float Get(Material material, string name, float fallback) =>
            material.HasProperty(name) ? material.GetFloat(name) : fallback;

        /// <summary>アバター全体とは別の明度を使うマテリアル。</summary>
        static Dictionary<Material, float> BrightnessOverrides = new Dictionary<Material, float>();

        /// <summary>焼き込み時に下地の色を混ぜるマテリアル (残す割合と混ぜる色)。</summary>
        static Dictionary<Material, (float Opacity, Color Backdrop)> Backdrops =
            new Dictionary<Material, (float, Color)>();

        /// <summary>[ReFrameQuestCutByBlendShape] の宣言 (Renderer ごと)。プレビューのメッシュ差し替え用。</summary>
        static Dictionary<Renderer, List<ReFrameQuestCutByBlendShapeAttribute>> ShapeCuts =
            new Dictionary<Renderer, List<ReFrameQuestCutByBlendShapeAttribute>>();

        /// <summary>この Renderer に掛かる [ReFrameQuestCutByBlendShape]。</summary>
        internal static IReadOnlyList<ReFrameQuestCutByBlendShapeAttribute> ShapeCutsOf(Renderer renderer) =>
            ShapeCuts.TryGetValue(renderer, out var list) ? list : null;

        static void ResolveShapeCuts(Transform root)
        {
            ShapeCuts = new Dictionary<Renderer, List<ReFrameQuestCutByBlendShapeAttribute>>();
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component == null)
                    continue;
                foreach (
                    var attr in (ReFrameQuestCutByBlendShapeAttribute[])
                        System.Attribute.GetCustomAttributes(component.GetType(), typeof(ReFrameQuestCutByBlendShapeAttribute), true)
                )
                {
                    var target = string.IsNullOrEmpty(attr.Path) ? null : root.Find(attr.Path);
                    if (target == null)
                        continue;
                    foreach (var renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (!ShapeCuts.TryGetValue(renderer, out var list))
                            ShapeCuts[renderer] = list = new List<ReFrameQuestCutByBlendShapeAttribute>();
                        list.Add(attr);
                    }
                }
            }
        }

        /// <summary>袋 (膜を持つ連結成分) だけ消して中身を残す透過マテリアル枠。</summary>
        static HashSet<(Renderer Renderer, int Slot)> ShellDropSlots = new HashSet<(Renderer, int)>();

        /// <summary>中身を焼き込む透過マテリアル枠と、その調整。</summary>
        static Dictionary<(Renderer Renderer, int Slot), ReFrameQuestShellBake.Options> ShellSlots =
            new Dictionary<(Renderer, int), ReFrameQuestShellBake.Options>();

        /// <summary>描かないことにしたマテリアル枠 (Renderer と枠番号)。</summary>
        static HashSet<(Renderer Renderer, int Slot)> DroppedSlots =
            new HashSet<(Renderer, int)>();

        /// <summary>焼き上がりに掛ける明度。</summary>
        static float TextureBrightness = 1f;

        /// <summary>捨てられる法線マップから陰影を作って焼き込むか。</summary>
        static bool ShadowFromNormalMap;

        /// <summary>アバター配下の Renderer を走査して、Quest で使えないマテリアルを差し替える。</summary>
        internal static void Convert(BuildContext context)
        {
            var root = context.AvatarRootTransform;
            var toonLit = Shader.Find(ToonLitShaderName);
            if (toonLit == null)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] '{ToonLitShaderName}' が見つかりません。マテリアルの変換を飛ばしました。"
                );
                return;
            }

            PrepareSettings(root);

            var converted = new Dictionary<Material, Material>();
            var log = new List<string>();
            var extraSlotsHandled = ExtraSlotsHandledElsewhere(root);

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;

                var materials = renderer.sharedMaterials;
                var subMeshes = SubMeshCountOf(renderer);
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null)
                        continue;

                    if (IsDroppedSlot(renderer, i))
                    {

                        EmptySubMesh(context, renderer, i);
                        materials[i] = null;
                        changed = true;
                        log.Add(source.name + " (Quest では描かない指定、枠の三角形を空にした)");
                        continue;
                    }
                    if (source.shader == null)
                        continue;
                    if (source.shader.name.StartsWith("VRChat/Mobile/"))
                        continue;

                    if (subMeshes > 0 && i >= subMeshes)
                    {

                        if (extraSlotsHandled)
                            continue;

                        materials[i] = null;
                        changed = true;
                        log.Add(source.name + " (余剰スロットなので空にした)");
                        continue;
                    }

                    if (IsShellSlot(renderer, i))
                    {
                        var shell = BuildToonLit(source, toonLit, context, renderer, i);
                        materials[i] = shell;
                        changed = true;
                        var cut = ShellSlots[(renderer, i)].CutEmpty
                            ? CutEmptyShell(context, renderer, i, shell.mainTexture)
                            : 0;
                        log.Add($"{source.name} [{source.shader.name}] -> {shell.name} (中身を焼き込んだ" + (cut > 0 ? $"、何も写らない三角形を {cut} 枚切った)" : ")"));
                        continue;
                    }

                    if (IsShellDropSlot(renderer, i))
                    {
                        var dropped = DropShell(context, renderer, i, source.mainTexture);
                        log.Add($"{source.name} (袋の三角形を {dropped} 枚消した)");
                    }

                    if (!converted.TryGetValue(source, out var replacement))
                    {
                        replacement = BuildToonLit(source, toonLit, context);
                        converted[source] = replacement;
                        log.Add($"{source.name} [{source.shader.name}] -> {replacement.name}");
                    }
                    materials[i] = replacement;
                    changed = true;
                }
                if (changed)
                    renderer.sharedMaterials = materials;
            }

            WarnFormatMismatches();
            if (log.Count > 0)
                Debug.LogWarning(
                    $"[ReFrameCore] ReFrameDeletePass: Quest 用にマテリアルを {log.Count} 個変換しました。"
                        + "\n  "
                        + string.Join("\n  ", log)
                );
        }

        /// <summary>圧縮形式が合わずに焼き直した焼き済みがあれば、まとめて知らせる。</summary>
        static void WarnFormatMismatches()
        {
            if (FormatMismatches.Count == 0)
                return;
            Debug.LogWarning(
                $"[ReFrameCore] ReFrameDeletePass: 焼き済みマテリアル {FormatMismatches.Count} 個は"
                    + $"圧縮形式が今のビルドターゲット ({EditorUserBuildSettings.activeBuildTarget}) に"
                    + "合わないので使わず、焼き直しました。Inspector から焼き直せばビルド時間が戻ります。"
                    + "\n  "
                    + string.Join(", ", FormatMismatches)
            );
        }

        /// <summary>アバター上の設定から、焼くテクスチャの上限を決める。</summary>
        internal static int ResolveMaxTextureSize(Transform root)
        {
            var limit = 0;
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component == null)
                    continue;

                var declared = BakeAttributeOf(component);
                if (declared != null && declared.MaxTextureSize > 0)
                {
                    limit = limit == 0
                        ? declared.MaxTextureSize
                        : Mathf.Min(limit, declared.MaxTextureSize);
                    continue;
                }
                if (component.questMaxTextureSize <= 0)
                    continue;
                limit =
                    limit == 0
                        ? component.questMaxTextureSize
                        : Mathf.Min(limit, component.questMaxTextureSize);
            }
            return limit > 0 ? limit : 1024;
        }

        /// <summary>アバター側の [ReFrameQuestBake] 宣言。</summary>
        static ReFrameQuestBakeAttribute BakeAttributeOf(ReFrameDeleteComponent component)
        {
            if (component.questBakeOverride)
                return null;
            return (ReFrameQuestBakeAttribute)
                System.Attribute.GetCustomAttribute(
                    component.GetType(),
                    typeof(ReFrameQuestBakeAttribute),
                    true
                );
        }

        /// <summary>アバター側の宣言 (上書きの有無に関わらず読む)。</summary>
        internal static ReFrameQuestBakeAttribute DeclaredBake(ReFrameDeleteComponent component) =>
            component == null
                ? null
                : (ReFrameQuestBakeAttribute)
                    System.Attribute.GetCustomAttribute(
                        component.GetType(),
                        typeof(ReFrameQuestBakeAttribute),
                        true
                    );

        /// <summary>焼く前に、アバターの設定 (解像度の上限・明度・法線からの陰影) を読み込む。</summary>
        internal static string SettingsSignature(Transform root) =>
            MaxTextureSize
            + "|" + TextureBrightness.ToString("F3")
            + "|" + ShadowFromNormalMap
            + "|outline=" + OutlineTargets.Count
            + "|backdrop=" + Backdrops.Count
            + "|shell=" + ShellSlots.Count + ":" + ShellOptionsSignature()
            + "|shapecut=" + ShapeCuts.Count
            + "|choices=" + ChoiceSignature
            + "|bright=" + BrightnessOverrides.Count
            + "|tint=" + TintOverrides.Count

            + "|bake=" + BakeVersion;

        /// <summary>焼き方の版。</summary>
        const int BakeVersion = 4;

        /// <summary>焼き済みの置き場。</summary>
        static ReFrameQuestBakeSet BakeSet;

        /// <summary>焼き済みの置き場をアバターから拾う。</summary>
        static void ResolveBakeSet(Transform root)
        {
            BakeSet = null;
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
                if (component != null && component.questBakeSet != null)
                    BakeSet = component.questBakeSet;
        }

        /// <summary>焼き済みがあればそれを返す。</summary>
        internal static Material FindBaked(Material source, string slotKey = null)
        {
            if (BakeSet == null)
                return null;
            var baked = BakeSet.Find(source, slotKey);
            if (baked == null)
                return null;
            if (FitsBuildTarget(baked))
                return baked;
            if (!FormatMismatches.Contains(baked.name))
                FormatMismatches.Add(baked.name);
            return null;
        }

        /// <summary>今回の変換で、圧縮形式が合わずに使えなかった焼き済みの名前。</summary>
        static readonly List<string> FormatMismatches = new List<string>();

        /// <summary>焼き済みマテリアルのテクスチャが、今のビルドターゲットで使える形式か。</summary>
        static bool FitsBuildTarget(Material baked)
        {
            var android = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
            foreach (var property in baked.GetTexturePropertyNames())
            {
                var texture = baked.GetTexture(property) as Texture2D;
                if (texture == null)
                    continue;
                var format = texture.format.ToString();
                var mobileOnly =
                    format.StartsWith("ASTC")
                    || format.StartsWith("ETC")
                    || format.StartsWith("PVRTC");
                var desktopOnly = format.StartsWith("DXT") || format.StartsWith("BC");
                if (android ? desktopOnly : mobileOnly)
                    return false;
            }
            return true;
        }

        /// <summary>焼き済みを一時的に忘れる。</summary>
        internal static void ClearBakeSet() => BakeSet = null;

        internal static void PrepareSettings(Transform root)
        {
            MaxTextureSize = ResolveMaxTextureSize(root);
            ResolveBakeAdjust(root);
            ResolveOutlineTargets(root);
            ResolveDroppedSlots(root);
            ResolveTransparentSlots(root);
            ResolveShapeCuts(root);
            ResolveBackdrops(root);
            ResolveBrightnessOverrides(root);
            ResolveTintOverrides(root);
            ResolveBakeSet(root);
            FormatMismatches.Clear();
        }

        /// <summary>[ReFrameQuestBrightness] が指すマテリアルと、その明度を集める。</summary>
        static void ResolveBrightnessOverrides(Transform root)
        {
            BrightnessOverrides = new Dictionary<Material, float>();
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component == null)
                    continue;
                foreach (
                    var attr in (ReFrameQuestBrightnessAttribute[])
                        System.Attribute.GetCustomAttributes(
                            component.GetType(),
                            typeof(ReFrameQuestBrightnessAttribute),
                            true
                        )
                )
                {
                    if (string.IsNullOrEmpty(attr.Path))
                        continue;
                    var target = root.Find(attr.Path);
                    if (target == null)
                        continue;
                    foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                        foreach (var material in renderer.sharedMaterials)
                            if (material != null)
                                BrightnessOverrides[material] = Mathf.Clamp01(attr.Brightness);
                }
            }
        }

        /// <summary>[ReFrameQuestTint] が指すマテリアルと、その RGB 倍率。</summary>
        static Dictionary<Material, Color> TintOverrides = new Dictionary<Material, Color>();

        static void ResolveTintOverrides(Transform root)
        {
            TintOverrides = new Dictionary<Material, Color>();
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component == null)
                    continue;
                foreach (
                    var attr in (ReFrameQuestTintAttribute[])
                        System.Attribute.GetCustomAttributes(
                            component.GetType(),
                            typeof(ReFrameQuestTintAttribute),
                            true
                        )
                )
                {
                    if (string.IsNullOrEmpty(attr.Path))
                        continue;
                    var target = root.Find(attr.Path);
                    if (target == null)
                        continue;
                    var tint = new Color(Mathf.Max(0f, attr.R), Mathf.Max(0f, attr.G), Mathf.Max(0f, attr.B), 1f);
                    foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                        foreach (var material in renderer.sharedMaterials)
                            if (material != null)
                                TintOverrides[material] = tint;
                }
            }
        }

        /// <summary>このマテリアルに掛ける RGB 倍率。</summary>
        static Color TintFor(Material material) =>
            material != null && TintOverrides.TryGetValue(material, out var value) ? value : Color.white;

        /// <summary>このマテリアルに掛ける明度。</summary>
        static float BrightnessFor(Material material) =>
            material != null && BrightnessOverrides.TryGetValue(material, out var value)
                ? value
                : TextureBrightness;

        /// <summary>[ReFrameQuestBlendBackdrop] が指すマテリアルと、混ぜる色を集める。</summary>
        static void ResolveBackdrops(Transform root)
        {
            Backdrops = new Dictionary<Material, (float, Color)>();
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component == null)
                    continue;
                foreach (
                    var attr in (ReFrameQuestBlendBackdropAttribute[])
                        System.Attribute.GetCustomAttributes(
                            component.GetType(),
                            typeof(ReFrameQuestBlendBackdropAttribute),
                            true
                        )
                )
                {
                    if (string.IsNullOrEmpty(attr.Path))
                        continue;
                    var target = root.Find(attr.Path);
                    if (target == null)
                        continue;
                    if (!ColorUtility.TryParseHtmlString(attr.Backdrop, out var color))
                    {
                        Debug.LogWarning(
                            $"[ReFrameCore] '{attr.Backdrop}' を色として読めません "
                                + $"({attr.Path})。#RRGGBB で書いてください。"
                        );
                        continue;
                    }

                    var opacity = component.questBackdropOpacity >= 0f
                        ? component.questBackdropOpacity
                        : attr.Opacity;
                    foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                        foreach (var material in renderer.sharedMaterials)
                            if (material != null)
                                Backdrops[material] = (Mathf.Clamp01(opacity), color);
                }
            }
        }

        /// <summary>MatCap を焼き込む。</summary>
        static void CompositeMatCap(Material source, Texture2D baked)
        {

            CompositeMatCapPass(source, baked, "_UseMatCap", "_MatCapTex", "_MatCapColor",
                "_MatCapBlend", "_MatCapBlendMode", "_MatCapBlendMask");
            CompositeMatCapPass(source, baked, "_UseMatCap2nd", "_MatCap2ndTex", "_MatCap2ndColor",
                "_MatCap2ndBlend", "_MatCap2ndBlendMode", "_MatCap2ndBlendMask");
        }

        /// <summary>MatCap 1 枚ぶんを焼き込む。</summary>
        static void CompositeMatCapPass(
            Material source,
            Texture2D baked,
            string useName,
            string texName,
            string colorName,
            string blendName,
            string modeName,
            string maskName
        )
        {
            if (!source.HasProperty(useName) || source.GetFloat(useName) <= 0.5f)
                return;
            var matcap = source.HasProperty(texName) ? source.GetTexture(texName) : null;
            if (matcap == null)
                return;

            var tint = source.HasProperty(colorName) ? source.GetColor(colorName) : Color.white;
            var blend = Get(source, blendName, 1f);
            var strength = Mathf.Clamp01(blend * tint.a);
            if (strength <= 0f)
                return;

            var center = CenterColor(matcap);
            var mode = Get(source, modeName, 0f);
            var color = new Color(center.r * tint.r, center.g * tint.g, center.b * tint.b);

            var maskTexture = source.HasProperty(maskName) ? source.GetTexture(maskName) : null;
            Color[] mask = null;
            if (maskTexture != null)
            {
                var resized = Blit(maskTexture, baked.width, baked.height, null);
                mask = resized.GetPixels();
                Object.DestroyImmediate(resized);
            }

            var pixels = baked.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                var local = mask != null ? strength * mask[i].r : strength;
                if (local <= 0f)
                    continue;

                Color mixed;
                if (mode >= 2.5f)
                    mixed = new Color(pixels[i].r * color.r, pixels[i].g * color.g, pixels[i].b * color.b);
                else if (mode >= 1.5f)
                    mixed = new Color(
                        1f - (1f - pixels[i].r) * (1f - color.r),
                        1f - (1f - pixels[i].g) * (1f - color.g),
                        1f - (1f - pixels[i].b) * (1f - color.b)
                    );
                else if (mode >= 0.5f)
                    mixed = new Color(pixels[i].r + color.r, pixels[i].g + color.g, pixels[i].b + color.b);
                else
                    mixed = color;

                pixels[i].r = Mathf.Clamp01(Mathf.Lerp(pixels[i].r, mixed.r, local));
                pixels[i].g = Mathf.Clamp01(Mathf.Lerp(pixels[i].g, mixed.g, local));
                pixels[i].b = Mathf.Clamp01(Mathf.Lerp(pixels[i].b, mixed.b, local));
            }
            baked.SetPixels(pixels);
            baked.Apply(true, false);
        }

        /// <summary>MatCap の中央部 (正面を向いた面に映る色) の平均。</summary>
        static Color CenterColor(Texture texture)
        {
            const int Size = 32;
            var rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                var read = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                read.Apply();
                var pixels = read.GetPixels();
                float r = 0, g = 0, b = 0;
                var count = 0;

                for (var y = Size / 4; y < Size * 3 / 4; y++)
                for (var x = Size / 4; x < Size * 3 / 4; x++)
                {
                    var p = pixels[y * Size + x];
                    r += p.r;
                    g += p.g;
                    b += p.b;
                    count++;
                }
                Object.DestroyImmediate(read);
                return count == 0 ? Color.white : new Color(r / count, g / count, b / count);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>焼き上がりへ下地の色を混ぜる。</summary>
        static void BlendBackdrop(Material source, Texture2D baked)
        {
            if (!Backdrops.TryGetValue(source, out var blend) || blend.Opacity >= 1f)
                return;
            var pixels = baked.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i].r = Mathf.Lerp(blend.Backdrop.r, pixels[i].r, blend.Opacity);
                pixels[i].g = Mathf.Lerp(blend.Backdrop.g, pixels[i].g, blend.Opacity);
                pixels[i].b = Mathf.Lerp(blend.Backdrop.b, pixels[i].b, blend.Opacity);
            }
            baked.SetPixels(pixels);
            baked.Apply(true, false);
        }

        /// <summary>[ReFrameQuestDropMaterial] が指すマテリアル枠を集める。</summary>
        static void ResolveDroppedSlots(Transform root)
        {
            DroppedSlots = new HashSet<(Renderer, int)>();
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component == null)
                    continue;
                foreach (
                    var attr in (ReFrameQuestDropMaterialAttribute[])
                        System.Attribute.GetCustomAttributes(
                            component.GetType(),
                            typeof(ReFrameQuestDropMaterialAttribute),
                            true
                        )
                )
                {
                    if (string.IsNullOrEmpty(attr.Path))
                        continue;
                    var target = root.Find(attr.Path);
                    if (target == null)
                        continue;
                    foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                        DroppedSlots.Add((renderer, attr.Slot));
                }
            }
        }

        /// <summary>この枠は描かない指定になっているか。</summary>
        internal static bool IsDroppedSlot(Renderer renderer, int slot) =>
            DroppedSlots.Contains((renderer, slot));

        /// <summary>[ReFrameQuestTransparent] の枠を、選ばれた扱いごとに振り分ける。</summary>
        /// <summary>焼き込む枠すべての調整値 (置き場の指紋用)。</summary>
        static string ShellOptionsSignature()
        {
            var parts = new List<string>();
            foreach (var kv in ShellSlots)
                parts.Add(SlotKey(kv.Key.Renderer, kv.Key.Slot) + "=" + ShellSignature(kv.Value));
            parts.Sort();
            return string.Join(";", parts);
        }

        /// <summary>Inspector で選んだ透過マテリアルの扱いと色 (置き場の指紋用)。</summary>
        static string ChoiceSignature = string.Empty;

        static void ResolveTransparentSlots(Transform root)
        {
            ShellSlots = new Dictionary<(Renderer, int), ReFrameQuestShellBake.Options>();
            ShellDropSlots = new HashSet<(Renderer, int)>();
            var signature = new System.Text.StringBuilder();
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component == null)
                    continue;
                signature.Append(component.QuestTransparentChoiceSignature());
                foreach (var (declared, mode) in component.EnumerateQuestTransparentTargets())
                {
                    var target = root.Find(declared.Path);
                    if (target == null)
                        continue;
                    foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                    {
                        switch (mode)
                        {
                            case ReFrameQuestTransparentMode.Drop:
                                ShellDropSlots.Add((renderer, declared.Slot));
                                break;
                            case ReFrameQuestTransparentMode.BakeInside:
                                var options = ShellOptionsOf(declared);
                                var chosen = component.QuestTransparentBeyond(declared.Path, declared.Slot);
                                if (chosen.HasValue)
                                    options.Beyond = chosen.Value;
                                ShellSlots[(renderer, declared.Slot)] = options;
                                break;
                        }
                    }
                }
            }
            ChoiceSignature = signature.ToString();
        }

        static ReFrameQuestShellBake.Options ShellOptionsOf(ReFrameQuestTransparentAttribute declared)
        {
            var options = ReFrameQuestShellBake.Options.Default;
            options.RimStrength = declared.Rim;
            options.Gloss = declared.Gloss;
            options.CutEmpty = declared.CutEmpty;
            options.AllSides = declared.AllSides;
            options.Size = declared.Size;
            if (!string.IsNullOrEmpty(declared.Beyond))
            {
                if (ColorUtility.TryParseHtmlString(declared.Beyond, out var beyond))
                    options.Beyond = beyond;
                else
                    Debug.LogWarning(
                        $"[ReFrameCore] '{declared.Beyond}' を色として読めません ({declared.Path})。#RRGGBB で書いてください。"
                    );
            }
            return options;
        }

        /// <summary>焼き上がりで何も写らなかった膜の三角形を、ビルド内の複製メッシュから切る。</summary>
        static int CutEmptyShell(BuildContext context, Renderer renderer, int slot, Texture baked)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            var filter = skinned == null ? renderer.GetComponent<MeshFilter>() : null;
            var mesh = skinned != null ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
            var clone = ReFrameQuestShellBake.CutEmpty(mesh, slot, baked);
            if (clone == null)
                return 0;
            context.AssetSaver.SaveAsset(clone);
            ObjectRegistry.RegisterReplacedObject(mesh, clone);
            if (skinned != null)
                skinned.sharedMesh = clone;
            else
                filter.sharedMesh = clone;
            return (mesh.GetSubMesh(slot).indexCount - clone.GetSubMesh(slot).indexCount) / 3;
        }

        /// <summary>この枠は中身を焼き込む指定になっているか。</summary>
        internal static bool IsShellSlot(Renderer renderer, int slot) =>
            ShellSlots.ContainsKey((renderer, slot));

        /// <summary>この枠は袋だけ消して中身を残す指定か。</summary>
        internal static bool IsShellDropSlot(Renderer renderer, int slot) =>
            ShellDropSlots.Contains((renderer, slot));

        /// <summary>袋 (膜を持つ連結成分) の三角形を、ビルド内の複製メッシュから切る。</summary>
        static int DropShell(BuildContext context, Renderer renderer, int slot, Texture source)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            var filter = skinned == null ? renderer.GetComponent<MeshFilter>() : null;
            var mesh = skinned != null ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
            var clone = ReFrameQuestShellBake.CutShell(mesh, slot, source);
            if (clone == null)
                return 0;
            context.AssetSaver.SaveAsset(clone);
            ObjectRegistry.RegisterReplacedObject(mesh, clone);
            if (skinned != null)
                skinned.sharedMesh = clone;
            else
                filter.sharedMesh = clone;
            return (mesh.GetSubMesh(slot).indexCount - clone.GetSubMesh(slot).indexCount) / 3;
        }

        /// <summary>この枠は何も写らなかった膜を切る指定か。</summary>
        internal static bool CutsEmptyShell(Renderer renderer, int slot) =>
            ShellSlots.TryGetValue((renderer, slot), out var options) && options.CutEmpty;

        /// <summary>枠ごとに焼くときの鍵 ("path#slot")。</summary>
        internal static string SlotKey(Renderer renderer, int slot)
        {
            var root = renderer.transform;
            while (root.parent != null && root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() == null)
                root = root.parent;
            var path = renderer.transform == root
                ? string.Empty
                : AnimationUtility.CalculateTransformPath(renderer.transform, root);
            return path + "#" + slot;
        }

        /// <summary>指定した枠のサブメッシュを空にする (メッシュはビルド内で複製し、元アセットは触らない)。</summary>
        static void EmptySubMesh(BuildContext context, Renderer renderer, int slot)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            var filter = skinned == null ? renderer.GetComponent<MeshFilter>() : null;
            var mesh = skinned != null ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
            if (mesh == null || slot < 0 || slot >= mesh.subMeshCount)
                return;
            if (mesh.GetSubMesh(slot).indexCount == 0)
                return;
            var clone = Object.Instantiate(mesh);
            clone.name = mesh.name;
            clone.SetTriangles(new int[0], slot, false);
            context.AssetSaver.SaveAsset(clone);
            ObjectRegistry.RegisterReplacedObject(mesh, clone);
            if (skinned != null)
                skinned.sharedMesh = clone;
            else
                filter.sharedMesh = clone;
        }

        /// <summary>[ReFrameQuestOutline] が指す Renderer のマテリアルを集める。</summary>
        static void ResolveOutlineTargets(Transform root)
        {
            OutlineTargets = new Dictionary<Material, float>();
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component == null)
                    continue;
                foreach (
                    var attr in (ReFrameQuestOutlineAttribute[])
                        System.Attribute.GetCustomAttributes(
                            component.GetType(),
                            typeof(ReFrameQuestOutlineAttribute),
                            true
                        )
                )
                {
                    if (string.IsNullOrEmpty(attr.Path))
                        continue;
                    var target = root.Find(attr.Path);
                    if (target == null)
                        continue;
                    foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                        foreach (var material in renderer.sharedMaterials)
                            if (material != null)
                                OutlineTargets[material] = attr.Thickness;
                }
            }
        }

        /// <summary>焼き上がりの調整 (明度・法線からの陰影) をアバターの設定から決める。</summary>
        internal static void ResolveBakeAdjust(Transform root)
        {
            TextureBrightness = 1f;
            ShadowFromNormalMap = false;
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component == null)
                    continue;
                var declared = BakeAttributeOf(component);
                var brightness = declared != null
                    ? declared.Brightness
                    : component.questTextureBrightness;
                var shadow = declared != null
                    ? declared.ShadowFromNormalMap
                    : component.questShadowFromNormalMap;
                TextureBrightness = Mathf.Min(TextureBrightness, Mathf.Clamp01(brightness));
                if (shadow)
                    ShadowFromNormalMap = true;
            }
        }

        /// <summary>1 マテリアルぶんの Toon Lit を作る (ビルド用。</summary>
        static Material BuildToonLit(
            Material source,
            Shader toonLit,
            BuildContext context,
            Renderer renderer = null,
            int slot = -1
        )
        {
            var material = CreateToonLit(source, toonLit, out var baked, renderer, slot);
            if (baked != null)
                context.AssetSaver.SaveAsset(baked);
            context.AssetSaver.SaveAsset(material);
            return material;
        }

        /// <summary>Toon Lit のマテリアルと焼いたテクスチャを作る。中身を焼き込む枠は renderer と slot も渡す。</summary>
        internal static Material CreateToonLit(
            Material source,
            Shader toonLit,
            out Texture2D baked,
            Renderer renderer = null,
            int slot = -1
        )
        {
            var shellOptions = default(ReFrameQuestShellBake.Options);
            var shell = renderer != null && ShellSlots.TryGetValue((renderer, slot), out shellOptions);
            var slotKey = shell ? SlotKey(renderer, slot) : null;

            var prebaked = FindBaked(source, slotKey);
            if (prebaked != null)
            {
                baked = null;
                return prebaked;
            }

            if (!shell && OutlineTargets.TryGetValue(source, out var thickness) && HasOutline(source))
            {
                var outline = CreateToonStandardOutline(source, thickness, out baked);
                if (outline != null)
                    return outline;
            }

            baked = IsLilToon(source)
                ? (shell ? GetOrBake(source, true, renderer, slot, shellOptions) : GetOrBake(source))
                : null;

            var material = new Material(toonLit)
            {
                name = source.name + (shell ? " (Quest " + slotKey + ")" : " (Quest)"),

                mainTexture = baked != null ? baked : source.mainTexture,
                renderQueue = shell ? -1 : source.renderQueue,
                enableInstancing = true,
                doubleSidedGI = source.doubleSidedGI,
                globalIlluminationFlags = source.globalIlluminationFlags,
            };
            if (baked == null)
            {
                material.mainTextureScale = source.mainTextureScale;
                material.mainTextureOffset = source.mainTextureOffset;
            }
            return material;
        }

        /// <summary>焼いた結果の生データ。</summary>
        class BakedTexture
        {
            public readonly byte[] Raw;
            public readonly int Width;
            public readonly int Height;
            public readonly TextureFormat Format;

            /// <summary>最初に焼いた実体。</summary>
            public Texture2D Instance;

            public BakedTexture(Texture2D texture)
            {
                Raw = texture.GetRawTextureData();
                Width = texture.width;
                Height = texture.height;
                Format = texture.format;
                Instance = texture;
            }

            /// <summary>生データから Texture2D を組み直す。</summary>
            public Texture2D Rebuild(string name)
            {
                var texture = new Texture2D(Width, Height, Format, true, false) { name = name };
                texture.LoadRawTextureData(Raw);
                texture.Apply(false, false);
                EnableMipStreaming(texture);
                return texture;
            }
        }

        /// <summary>マテリアルごとの焼き上がりキャッシュ。</summary>
        static readonly Dictionary<string, BakedTexture> BakeCache = new Dictionary<string, BakedTexture>();

        /// <summary>焼き上がりに影響するプロパティだけを並べた鍵。</summary>
        internal static string BakeKey(Material material)
        {
            var key = new System.Text.StringBuilder();

            key.Append(MaxTextureSize).Append('|');

            key.Append(BrightnessFor(material)).Append('|').Append(ShadowFromNormalMap).Append('|');
            key.Append("tint=").Append(TintFor(material)).Append('|');
            if (Backdrops.TryGetValue(material, out var backdrop))
                key.Append("backdrop=").Append(backdrop.Opacity).Append(backdrop.Backdrop).Append('|');
            if (ShadowFromNormalMap && material.HasProperty("_BumpMap"))
            {
                var bump = material.GetTexture("_BumpMap");
                key.Append("bump=").Append(bump == null ? 0 : bump.GetInstanceID()).Append('|');
            }
            key.Append(material.GetInstanceID()).Append('|').Append(material.shader.name).Append('|');
            foreach (var name in new[] { "_Color", "_Color2nd", "_Color3rd", "_EmissionColor" })
                if (material.HasProperty(name))
                    key.Append(name).Append('=').Append(material.GetColor(name)).Append(',');
            if (material.HasProperty("_MainTexHSVG"))
                key.Append("hsvg=").Append(material.GetVector("_MainTexHSVG")).Append(',');
            if (material.HasProperty("_UseMatCap") && material.GetFloat("_UseMatCap") > 0.5f)
            {
                var matcap = material.HasProperty("_MatCapTex") ? material.GetTexture("_MatCapTex") : null;
                key.Append("matcap=").Append(matcap == null ? 0 : matcap.GetInstanceID());
                if (material.HasProperty("_MatCapColor"))
                    key.Append(material.GetColor("_MatCapColor"));
                if (material.HasProperty("_MatCapBlend"))
                    key.Append(material.GetFloat("_MatCapBlend"));
                if (material.HasProperty("_MatCapBlendMode"))
                    key.Append(material.GetFloat("_MatCapBlendMode"));
                var matcapMask = material.HasProperty("_MatCapBlendMask")
                    ? material.GetTexture("_MatCapBlendMask")
                    : null;
                key.Append("mask=").Append(matcapMask == null ? 0 : matcapMask.GetInstanceID());
                key.Append('|');
            }
            if (material.HasProperty("_UseMatCap2nd") && material.GetFloat("_UseMatCap2nd") > 0.5f)
            {
                var second = material.HasProperty("_MatCap2ndTex") ? material.GetTexture("_MatCap2ndTex") : null;
                key.Append("matcap2=").Append(second == null ? 0 : second.GetInstanceID());
                if (material.HasProperty("_MatCap2ndColor"))
                    key.Append(material.GetColor("_MatCap2ndColor"));
                key.Append('|');
            }
            if (material.HasProperty("_UseShadow") && material.GetFloat("_UseShadow") > 0.5f)
            {
                key.Append("shadow=");
                foreach (var n in new[] { "_ShadowColor", "_Shadow2ndColor" })
                    if (material.HasProperty(n))
                        key.Append(material.GetColor(n));
                foreach (var n in new[] { "_ShadowBorder", "_Shadow2ndBorder", "_ShadowBlur", "_Shadow2ndBlur" })
                    if (material.HasProperty(n))
                        key.Append(material.GetFloat(n)).Append(',');
                key.Append(ShadowColorMix).Append('|');
            }
            foreach (var name in new[] { "_MainGradationStrength", "_UseMain2ndTex", "_UseMain3rdTex", "_UseEmission", "_EmissionBlend", "_EmissionMainStrength", "_EmissionBlendMode" })
                if (material.HasProperty(name))
                    key.Append(name).Append('=').Append(material.GetFloat(name)).Append(',');
            foreach (var name in new[] { "_MainTex", "_Main2ndTex", "_Main3rdTex", "_EmissionMap", "_EmissionBlendMask", "_MainGradationTex", "_MainColorAdjustMask" })
            {
                if (!material.HasProperty(name))
                    continue;
                var texture = material.GetTexture(name);
                key.Append(name).Append('=').Append(texture == null ? 0 : texture.GetInstanceID()).Append(',');
                key.Append(material.GetTextureScale(name)).Append(material.GetTextureOffset(name)).Append(',');
            }
            return key.ToString();
        }

        /// <summary>キャッシュにあればそれを組み直し、無ければ焼いて憶える。</summary>
        static Texture2D GetOrBake(Material source) => GetOrBake(source, true);

        /// <summary>明度補正と法線からの陰影を掛けるか。</summary>
        static Texture2D GetOrBake(Material source, bool adjust) => GetOrBake(source, adjust, null, -1, default);

        /// <summary>renderer を渡すと、その枠の中身を焼き込む。</summary>
        static Texture2D GetOrBake(
            Material source,
            bool adjust,
            Renderer renderer,
            int slot,
            ReFrameQuestShellBake.Options shell
        )
        {

            var key = BakeKey(source) + "|adjust=" + adjust + "|fmt=" + CompressionFormat;
            if (renderer != null)
                key += "|shell=" + renderer.GetInstanceID() + "#" + slot + "|" + ShellSignature(shell);
            if (BakeCache.TryGetValue(key, out var cached))
                return cached.Rebuild(source.name + "_Quest");

            var baked = BakeLilToon(source, adjust, renderer, slot, shell);
            if (baked == null)
                return null;
            BakeCache[key] = new BakedTexture(baked);
            return baked;
        }

        /// <summary>lilToon 系のマテリアルか。</summary>
        internal static bool IsLilToon(Material material)
        {
            if (material == null || material.shader == null)
                return false;
            if (material.shader.name.ToLowerInvariant().Contains("liltoon"))
                return true;

            var path = AssetDatabase.GetAssetPath(material.shader);
            return path != null && path.EndsWith(".lilcontainer");
        }

        static string ShellSignature(ReFrameQuestShellBake.Options shell) =>
            shell.RimStrength + "," + shell.Gloss + "," + shell.Beyond + "," + shell.CutEmpty + "," + shell.AllSides + "," + shell.Size;

        /// <summary>lilToon のメインカラー・色調補正・追加レイヤーを 1 枚に焼く。</summary>
        static Texture2D BakeLilToon(
            Material source,
            bool adjust = true,
            Renderer renderer = null,
            int slot = -1,
            ReFrameQuestShellBake.Options shell = default
        )
        {
            var baker = Shader.Find(LilToonBakerShaderName);
            if (baker == null)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] '{LilToonBakerShaderName}' が見つかりません "
                        + $"('{source.name}' はシェーダーを差し替えるだけになります)。"
                );
                return null;
            }

            var mainTexture = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
            var width = Mathf.Min(mainTexture != null ? mainTexture.width : 4, MaxTextureSize);
            var height = Mathf.Min(mainTexture != null ? mainTexture.height : 4, MaxTextureSize);
            if (renderer != null && shell.Size > 0)
            {
                width = shell.Size;
                height = shell.Size;
            }

            var bakeMaterial = new Material(baker);
            try
            {

                CopyColor(source, bakeMaterial, "_Color");
                CopyVector(source, bakeMaterial, "_MainTexHSVG");
                CopyTexture(source, bakeMaterial, "_MainColorAdjustMask");
                CopyFloat(source, bakeMaterial, "_MainGradationStrength");
                CopyTexture(source, bakeMaterial, "_MainGradationTex");

                CopyFloat(source, bakeMaterial, "_UseMain2ndTex");
                CopyColor(source, bakeMaterial, "_Color2nd");
                CopyTexture(source, bakeMaterial, "_Main2ndTex");
                CopyTexture(source, bakeMaterial, "_Main2ndBlendMask");
                CopyFloat(source, bakeMaterial, "_Main2ndTexBlendMode");

                CopyFloat(source, bakeMaterial, "_UseMain3rdTex");
                CopyColor(source, bakeMaterial, "_Color3rd");
                CopyTexture(source, bakeMaterial, "_Main3rdTex");
                CopyTexture(source, bakeMaterial, "_Main3rdBlendMask");
                CopyFloat(source, bakeMaterial, "_Main3rdTexBlendMode");

                if (bakeMaterial.HasProperty("_MainTex"))
                {
                    bakeMaterial.SetTexture("_MainTex", mainTexture != null ? mainTexture : Texture2D.whiteTexture);

                    bakeMaterial.SetTextureScale("_MainTex", source.HasProperty("_MainTex") ? source.GetTextureScale("_MainTex") : Vector2.one);
                    bakeMaterial.SetTextureOffset("_MainTex", source.HasProperty("_MainTex") ? source.GetTextureOffset("_MainTex") : Vector2.zero);
                }

                var baked = Blit(mainTexture, width, height, bakeMaterial);
                baked.name = source.name + "_Quest";
                if (adjust)
                    baked = AdjustBaked(source, baked);
                CompositeEmission(source, baked);
                CompositeMatCap(source, baked);
                BlendBackdrop(source, baked);
                if (renderer != null)
                    ReFrameQuestShellBake.Composite(renderer, slot, baked, shell);
                Compress(baked);
                EnableMipStreaming(baked);
                return baked;
            }
            finally
            {
                Object.DestroyImmediate(bakeMaterial);
            }
        }

        /// <summary>発光を焼いたメインテクスチャへ加算する。</summary>
        static void CompositeEmission(Material source, Texture2D baked)
        {
            if (!source.HasProperty("_UseEmission") || source.GetFloat("_UseEmission") == 0f)
                return;

            var emissionMap = source.HasProperty("_EmissionMap") ? source.GetTexture("_EmissionMap") : null;
            var color = source.HasProperty("_EmissionColor") ? source.GetColor("_EmissionColor") : Color.white;
            if (emissionMap == null && color.maxColorComponent <= 0f)
                return;

            var strength = Get(source, "_EmissionBlend", 1f);
            if (strength <= 0f)
                return;
            var mainStrength = Mathf.Clamp01(Get(source, "_EmissionMainStrength", 0f));
            var mode = Mathf.RoundToInt(Get(source, "_EmissionBlendMode", 1f));
            var maskMap = source.HasProperty("_EmissionBlendMask") ? source.GetTexture("_EmissionBlendMask") : null;

            var emission = Blit(emissionMap != null ? emissionMap : Texture2D.whiteTexture, baked.width, baked.height, null);
            var mask = maskMap != null ? Blit(maskMap, baked.width, baked.height, null) : null;
            try
            {
                var basePixels = baked.GetPixels();
                var addPixels = emission.GetPixels();
                var maskPixels = mask != null ? mask.GetPixels() : null;
                for (var i = 0; i < basePixels.Length; i++)
                {
                    var b = basePixels[i];
                    var e = addPixels[i];
                    var er = e.r * color.r;
                    var eg = e.g * color.g;
                    var eb = e.b * color.b;
                    var ea = e.a * color.a;
                    if (maskPixels != null)
                    {
                        var m = maskPixels[i];
                        er *= m.r;
                        eg *= m.g;
                        eb *= m.b;
                        ea *= m.a;
                    }
                    er = Mathf.Lerp(er, er * b.r, mainStrength);
                    eg = Mathf.Lerp(eg, eg * b.g, mainStrength);
                    eb = Mathf.Lerp(eb, eb * b.b, mainStrength);
                    var blend = Mathf.Clamp01(strength * ea);
                    if (blend <= 0f)
                        continue;
                    basePixels[i].r = Mathf.Clamp01(Mathf.Lerp(b.r, BlendChannel(b.r, er, mode), blend));
                    basePixels[i].g = Mathf.Clamp01(Mathf.Lerp(b.g, BlendChannel(b.g, eg, mode), blend));
                    basePixels[i].b = Mathf.Clamp01(Mathf.Lerp(b.b, BlendChannel(b.b, eb, mode), blend));
                }
                baked.SetPixels(basePixels);
                baked.Apply(true, false);
            }
            finally
            {
                Object.DestroyImmediate(emission);
                if (mask != null)
                    Object.DestroyImmediate(mask);
            }
        }

        /// <summary>lilToon の lilBlendColor: 0 Normal / 1 Add / 2 Screen / 3 Multiply。</summary>
        static float BlendChannel(float dst, float src, int mode)
        {
            switch (mode)
            {
                case 0:
                    return src;
                case 2:
                    return Mathf.Max(dst + src - dst * src, dst);
                case 3:
                    return dst * src;
                default:
                    return dst + src;
            }
        }

        /// <summary>焼いたテクスチャを圧縮する。</summary>
        internal static TextureFormat CompressionFormat =>
            ForceQuestFormat || EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android
                ? TextureFormat.ASTC_6x6
                : TextureFormat.DXT5;

        /// <summary>true の間はビルドターゲットに関わらず Quest 用 (ASTC) で圧縮する。</summary>
        internal static bool ForceQuestFormat;

        static void Compress(Texture2D texture)
        {

            if (texture.width < 4 || texture.height < 4)
                return;

            var format = CompressionFormat;
            try
            {
                EditorUtility.CompressTexture(texture, format, TextureCompressionQuality.Normal);
            }
            catch (System.Exception e)
            {

                Debug.LogWarning(
                    $"[ReFrameCore] '{texture.name}' を {format} へ圧縮できませんでした。非圧縮のまま進めます。" + (char)10 + e.Message
                );
            }
        }

        /// <summary>ミップストリーミングを立てる。</summary>
        static void EnableMipStreaming(Texture2D texture)
        {
            if (texture == null || texture.mipmapCount <= 1)
                return;
            try
            {
                var serialized = new SerializedObject(texture);
                var property = serialized.FindProperty("m_StreamingMipmaps");
                if (property == null)
                    return;
                property.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            catch (System.Exception)
            {

            }
        }

        /// <summary>余剰マテリアルスロットを他のツールが片付けるか。</summary>
        static bool ExtraSlotsHandledElsewhere(Transform root)
        {
            var type = System.Type.GetType(
                "KRT.VRCQuestTools.Components.AvatarConverterSettings, VRCQuestTools"
            );
            if (type == null)
                return false;
            var component = root.GetComponentInChildren(type, true);
            if (component == null)
                return false;
            var field = type.GetField("removeExtraMaterialSlots");
            return field != null && field.GetValue(component) is bool flag && flag;
        }

        /// <summary>この Renderer が描くサブメッシュの数。</summary>
        internal static int SubMeshCountOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh != null ? skinned.sharedMesh.subMeshCount : 0;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null ? filter.sharedMesh.subMeshCount : 0;
        }

        /// <summary>lilToon の金属まわりの設定を Toon Standard へ移す。</summary>
        static void CopyMetallic(Material source, Material material)
        {
            CopyFloatAs(source, "_Metallic", material, "_MetallicStrength");
            CopyFloatAs(source, "_Smoothness", material, "_GlossStrength");
            CopyFloatAs(source, "_Reflectance", material, "_Reflectance");
            CopyTextureAs(source, "_MetallicGlossMap", material, "_MetallicMap");
            CopyTextureAs(source, "_SmoothnessTex", material, "_GlossMap");

            var useMatCap = source.HasProperty("_UseMatCap") && source.GetFloat("_UseMatCap") > 0.5f;
            if (!useMatCap || !material.HasProperty("_Matcap"))
                return;
            var matcap = source.HasProperty("_MatCapTex") ? source.GetTexture("_MatCapTex") : null;
            if (matcap == null)
                return;
            material.SetTexture("_Matcap", matcap);
            var blend = source.HasProperty("_MatCapBlend") ? source.GetFloat("_MatCapBlend") : 1f;
            var alpha = source.HasProperty("_MatCapColor") ? source.GetColor("_MatCapColor").a : 1f;
            if (material.HasProperty("_MatcapStrength"))
                material.SetFloat("_MatcapStrength", Mathf.Clamp01(blend * alpha));
        }

        static void CopyFloatAs(Material source, string from, Material target, string to)
        {
            if (source.HasProperty(from) && target.HasProperty(to))
                target.SetFloat(to, source.GetFloat(from));
        }

        static void CopyTextureAs(Material source, string from, Material target, string to)
        {
            if (!source.HasProperty(from) || !target.HasProperty(to))
                return;
            var texture = source.GetTexture(from);
            if (texture != null)
                target.SetTexture(to, texture);
        }

        /// <summary>lilToon のアウトライン系か (輪郭線の設定を持っているか)。</summary>
        static bool HasOutline(Material material) =>
            material != null
            && material.HasProperty("_OutlineWidth")
            && material.HasProperty("_OutlineColor");

        /// <summary>VRChat/Mobile/Toon Standard (Outline) のマテリアルを作る。</summary>
        static Material CreateToonStandardOutline(Material source, float thickness, out Texture2D baked)
        {
            baked = null;
            var shader = Shader.Find(ToonStandardOutlineShaderName);
            if (shader == null)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] '{ToonStandardOutlineShaderName}' が見つかりません。"
                        + $"'{source.name}' は輪郭線なしで変換します。"
                );
                return null;
            }

            baked = IsLilToon(source) ? GetOrBake(source, false) : null;

            var material = new Material(shader)
            {
                name = source.name + " (Quest Outline)",
                mainTexture = baked != null ? baked : source.mainTexture,
                renderQueue = source.renderQueue,
                enableInstancing = true,
                doubleSidedGI = source.doubleSidedGI,
                globalIlluminationFlags = source.globalIlluminationFlags,
            };
            if (baked == null)
            {
                material.mainTextureScale = source.mainTextureScale;
                material.mainTextureOffset = source.mainTextureOffset;
            }

            if (source.HasProperty("_BumpMap") && material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                if (source.HasProperty("_BumpScale") && material.HasProperty("_BumpScale"))
                    material.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
            }

            CopyMetallic(source, material);

            var width = thickness >= 0f
                ? thickness
                : source.HasProperty("_OutlineWidth") ? source.GetFloat("_OutlineWidth") : 0.05f;
            material.SetFloat("_OutlineThickness", Mathf.Clamp(width, 0f, 0.5f));
            if (source.HasProperty("_OutlineColor"))
                material.SetColor("_OutlineColor", source.GetColor("_OutlineColor"));

            return material;
        }

        /// <summary>焼き上がりに明度補正と、法線マップからの陰影を掛ける。</summary>
        static Texture2D AdjustBaked(Material source, Texture2D baked)
        {
            var normalMap = ShadowFromNormalMap && source.HasProperty("_BumpMap")
                ? source.GetTexture("_BumpMap")
                : null;
            var brightness = BrightnessFor(source);
            var useShadowColor =
                ShadowColorsEnabled
                && source.HasProperty("_UseShadow")
                && source.GetFloat("_UseShadow") > 0.5f
                && source.HasProperty("_ShadowColor");
            var tint = TintFor(source);
            var tinted = tint != Color.white;
            if (Mathf.Approximately(brightness, 1f) && normalMap == null && !useShadowColor && !tinted)
                return baked;

            var shader = Shader.Find(AdjustShaderName);
            if (shader == null)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] '{AdjustShaderName}' が見つかりません。明度補正と陰影の焼き込みを飛ばしました。"
                );
                return baked;
            }

            var adjust = new Material(shader);
            try
            {
                adjust.SetFloat("_Brightness", brightness);
                adjust.SetColor("_Tint", tint);
                adjust.SetInt("_UseShadow", normalMap != null ? 1 : 0);

                adjust.SetInt("_UseShadowColor", useShadowColor ? 1 : 0);
                if (useShadowColor)
                {
                    adjust.SetColor("_ShadowColor1", source.GetColor("_ShadowColor"));
                    adjust.SetColor(
                        "_ShadowColor2",
                        source.HasProperty("_Shadow2ndColor")
                            ? source.GetColor("_Shadow2ndColor")
                            : source.GetColor("_ShadowColor")
                    );
                    adjust.SetFloat("_ShadowBorder1", Get(source, "_ShadowBorder", 0.5f));
                    adjust.SetFloat("_ShadowBorder2", Get(source, "_Shadow2ndBorder", 0.25f));
                    adjust.SetFloat("_ShadowBlur1", Get(source, "_ShadowBlur", 0.3f));
                    adjust.SetFloat("_ShadowBlur2", Get(source, "_Shadow2ndBlur", 0.6f));
                    adjust.SetFloat("_FlatLuminance", FlatLuminance);
                    adjust.SetFloat("_ShadowMix", ShadowColorMix);
                }
                if (normalMap != null)
                {
                    adjust.SetTexture("_BumpMap", normalMap);

                    adjust.SetFloat(
                        "_ShadowStrength",
                        source.HasProperty("_ShadowStrength") ? source.GetFloat("_ShadowStrength") : 0.5f
                    );
                    adjust.SetFloat(
                        "_ShadowBorder",
                        source.HasProperty("_ShadowBorder") ? source.GetFloat("_ShadowBorder") : 0.5f
                    );
                    adjust.SetFloat(
                        "_ShadowBlur",
                        source.HasProperty("_ShadowBlur") ? source.GetFloat("_ShadowBlur") : 0.2f
                    );
                    adjust.SetFloat("_ShadowBorderBlur", 0.2f);
                }

                var adjusted = Blit(baked, baked.width, baked.height, adjust);
                adjusted.name = baked.name;
                Object.DestroyImmediate(baked);
                return adjusted;
            }
            finally
            {
                Object.DestroyImmediate(adjust);
            }
        }

        /// <summary>RenderTexture 経由で 1 枚に描き出し、読み戻した Texture2D を返す。</summary>
        static Texture2D Blit(Texture source, int width, int height, Material material)
        {
            var rt = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            );
            var previous = RenderTexture.active;
            try
            {
                if (material != null)
                    Graphics.Blit(source, rt, material);
                else
                    Graphics.Blit(source, rt);

                RenderTexture.active = rt;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply(true, false);
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static void CopyColor(Material from, Material to, string name)
        {
            if (from.HasProperty(name) && to.HasProperty(name))
                to.SetColor(name, from.GetColor(name));
        }

        static void CopyVector(Material from, Material to, string name)
        {
            if (from.HasProperty(name) && to.HasProperty(name))
                to.SetVector(name, from.GetVector(name));
        }

        static void CopyFloat(Material from, Material to, string name)
        {
            if (from.HasProperty(name) && to.HasProperty(name))
                to.SetFloat(name, from.GetFloat(name));
        }

        static void CopyTexture(Material from, Material to, string name)
        {
            if (!from.HasProperty(name) || !to.HasProperty(name))
                return;
            to.SetTexture(name, from.GetTexture(name));
            to.SetTextureScale(name, from.GetTextureScale(name));
            to.SetTextureOffset(name, from.GetTextureOffset(name));
        }
    }
}
