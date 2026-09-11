using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>Quest 向けの「アセットを軽くする」処理。</summary>
    internal static class ReFrameQuestAssetTrim
    {
        /// <summary>頂点カラーを消す。</summary>
        internal static int RemoveVertexColors(BuildContext context)
        {
            var removed = 0;
            var replaced = new Dictionary<Mesh, Mesh>();

            Mesh Clean(Mesh mesh)
            {
                if (mesh == null || !HasMeaningfulVertexColor(mesh))
                    return null;
                if (replaced.TryGetValue(mesh, out var cached))
                    return cached;

                var clone = Object.Instantiate(mesh);
                clone.name = mesh.name;
                clone.colors32 = null;
                context.AssetSaver.SaveAsset(clone);
                ObjectRegistry.RegisterReplacedObject(mesh, clone);
                replaced[mesh] = clone;
                removed++;
                return clone;
            }

            foreach (
                var renderer in context.AvatarRootObject.GetComponentsInChildren<SkinnedMeshRenderer>(
                    true
                )
            )
            {
                var clone = Clean(renderer.sharedMesh);
                if (clone != null)
                    renderer.sharedMesh = clone;
            }
            foreach (var filter in context.AvatarRootObject.GetComponentsInChildren<MeshFilter>(true))
            {
                var clone = Clean(filter.sharedMesh);
                if (clone != null)
                    filter.sharedMesh = clone;
            }
            return removed;
        }

        /// <summary>[ReFrameQuestCutByBlendShape] の宣言に従って、指定 BlendShape が動かす三角形を削る。</summary>
        internal static int CutByBlendShape(BuildContext context)
        {
            var root = context.AvatarRootTransform;
            var cut = 0;
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component == null)
                    continue;
                foreach (
                    var attr in (ReFrameQuestCutByBlendShapeAttribute[])
                        System.Attribute.GetCustomAttributes(
                            component.GetType(),
                            typeof(ReFrameQuestCutByBlendShapeAttribute),
                            true
                        )
                )
                {
                    if (string.IsNullOrEmpty(attr.Path))
                        continue;
                    var target = root.Find(attr.Path);
                    if (target == null)
                        continue;
                    foreach (var renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var clone = CutMovedByBlendShapes(renderer.sharedMesh, attr.Shapes, attr.Tolerance, out var removed);
                        if (clone == null)
                            continue;
                        context.AssetSaver.SaveAsset(clone);
                        ObjectRegistry.RegisterReplacedObject(renderer.sharedMesh, clone);
                        renderer.sharedMesh = clone;
                        cut += removed;
                    }
                }
            }
            return cut;
        }

        /// <summary>3 頂点すべてが指定 BlendShape のどれかで動く三角形を全サブメッシュから外したメッシュを返す。無ければ null。</summary>
        internal static Mesh CutMovedByBlendShapes(Mesh mesh, string[] shapes, float tolerance, out int removed)
        {
            removed = 0;
            if (mesh == null || shapes == null || shapes.Length == 0)
                return null;
            var moved = new bool[mesh.vertexCount];
            var deltas = new Vector3[mesh.vertexCount];
            var normals = new Vector3[mesh.vertexCount];
            var tangents = new Vector3[mesh.vertexCount];
            var any = false;
            foreach (var name in shapes)
            {
                var index = mesh.GetBlendShapeIndex(name);
                if (index < 0)
                {
                    Debug.LogWarning($"[ReFrameCore] BlendShape '{name}' が '{mesh.name}' にありません。");
                    continue;
                }
                mesh.GetBlendShapeFrameVertices(index, mesh.GetBlendShapeFrameCount(index) - 1, deltas, normals, tangents);
                for (var i = 0; i < moved.Length; i++)
                    if (deltas[i].magnitude >= tolerance)
                        moved[i] = any = true;
            }
            if (!any)
                return null;

            Mesh clone = null;
            for (var slot = 0; slot < mesh.subMeshCount; slot++)
            {
                var triangles = mesh.GetTriangles(slot);
                var kept = new List<int>(triangles.Length);
                var cutHere = 0;
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    if (moved[triangles[i]] && moved[triangles[i + 1]] && moved[triangles[i + 2]])
                    {
                        cutHere++;
                        continue;
                    }
                    kept.Add(triangles[i]);
                    kept.Add(triangles[i + 1]);
                    kept.Add(triangles[i + 2]);
                }
                if (cutHere == 0)
                    continue;
                if (clone == null)
                {
                    clone = Object.Instantiate(mesh);
                    clone.name = mesh.name;
                }
                clone.SetTriangles(kept, slot);
                removed += cutHere;
            }
            return clone;
        }

        /// <summary>[ReFrameQuestCutTransparent] の宣言に従って、透明な三角形を削る。</summary>
        internal static int CutTransparent(BuildContext context)
        {
            var root = context.AvatarRootTransform;
            var cut = 0;
            foreach (var component in ReFrameDeleteComponent.ActiveIn(root))
            {
                if (component == null)
                    continue;
                foreach (
                    var attr in (ReFrameQuestCutTransparentAttribute[])
                        System.Attribute.GetCustomAttributes(
                            component.GetType(),
                            typeof(ReFrameQuestCutTransparentAttribute),
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
                        cut += CutOne(context, renderer, attr.Slot, attr.Threshold);
                }
            }
            return cut;
        }

        /// <summary>Renderer 1 つの 1 サブメッシュぶん。</summary>
        static int CutOne(BuildContext context, Renderer renderer, int slot, float threshold)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            var filter = skinned == null ? renderer.GetComponent<MeshFilter>() : null;
            var mesh = skinned != null ? skinned.sharedMesh : (filter != null ? filter.sharedMesh : null);
            if (mesh == null || slot < 0 || slot >= mesh.subMeshCount)
                return 0;
            var materials = renderer.sharedMaterials;
            if (slot >= materials.Length || materials[slot] == null)
                return 0;
            var texture = materials[slot].mainTexture;
            if (texture == null)
                return 0;

            var alpha = ReadAlpha(texture, out var size);
            if (alpha == null)
                return 0;

            var uvs = mesh.uv;
            if (uvs == null || uvs.Length == 0)
                return 0;
            var triangles = mesh.GetTriangles(slot);
            var kept = new List<int>(triangles.Length);
            var removed = 0;
            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                var a = triangles[i];
                var b = triangles[i + 1];
                var c = triangles[i + 2];
                if (a >= uvs.Length || b >= uvs.Length || c >= uvs.Length)
                {
                    kept.Add(a);
                    kept.Add(b);
                    kept.Add(c);
                    continue;
                }

                var centre = (uvs[a] + uvs[b] + uvs[c]) / 3f;
                var visible =
                    Sample(alpha, size, centre) >= threshold
                    || Sample(alpha, size, uvs[a]) >= threshold
                    || Sample(alpha, size, uvs[b]) >= threshold
                    || Sample(alpha, size, uvs[c]) >= threshold;
                if (!visible)
                {
                    removed++;
                    continue;
                }
                kept.Add(a);
                kept.Add(b);
                kept.Add(c);
            }
            if (removed == 0)
                return 0;

            var clone = Object.Instantiate(mesh);
            clone.name = mesh.name;
            clone.SetTriangles(kept, slot);
            context.AssetSaver.SaveAsset(clone);
            ObjectRegistry.RegisterReplacedObject(mesh, clone);
            if (skinned != null)
                skinned.sharedMesh = clone;
            else if (filter != null)
                filter.sharedMesh = clone;
            return removed;
        }

        /// <summary>テクスチャのアルファを読み出す。</summary>
        internal static float[] ReadAlpha(Texture texture, out int size)
        {
            size = Mathf.Clamp(Mathf.Max(texture.width, texture.height), 64, 512);
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                var read = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
                read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                read.Apply();
                var pixels = read.GetPixels();
                var result = new float[pixels.Length];
                for (var i = 0; i < pixels.Length; i++)
                    result[i] = pixels[i].a;
                Object.DestroyImmediate(read);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        internal static float Sample(float[] alpha, int size, Vector2 uv)
        {
            var x = Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(uv.x, 1f) * (size - 1)), 0, size - 1);
            var y = Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(uv.y, 1f) * (size - 1)), 0, size - 1);
            return alpha[y * size + x];
        }

        /// <summary>頂点カラーが実際に色を変えるか (全部白なら意味が無い)。</summary>
        static bool HasMeaningfulVertexColor(Mesh mesh)
        {
            var colors = mesh.colors32;
            if (colors == null || colors.Length == 0)
                return false;
            foreach (var color in colors)
                if (color.r != 255 || color.g != 255 || color.b != 255 || color.a != 255)
                    return true;
            return false;
        }

        /// <summary>メニューのアイコンを縮めて圧縮する。</summary>
        internal static int CompressMenuIcons(BuildContext context, ReFrameMenuIconMode mode)
        {
            if (mode == ReFrameMenuIconMode.Keep)
                return 0;
            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null || descriptor.expressionsMenu == null)
                return 0;

            var remove = mode == ReFrameMenuIconMode.Remove;
            var maxSize = (int)mode;
            var format = MobileTextureFormat();
            var converted = new Dictionary<Texture2D, Texture2D>();
            var visited = new HashSet<VRCExpressionsMenu>();
            var count = 0;

            var root = ReFrameMenuUtil.ReplaceMenuWithClone(context);

            void Walk(VRCExpressionsMenu menu)
            {
                if (menu == null || !visited.Add(menu))
                    return;
                foreach (var control in menu.controls)
                {
                    if (control.icon != null)
                    {
                        if (remove)
                        {
                            control.icon = null;
                            count++;
                        }
                        else
                        {
                            var shrunk = Shrink(control.icon, maxSize, format, converted, context);
                            if (shrunk != null)
                            {
                                control.icon = shrunk;
                                count++;
                            }
                        }
                    }
                    if (control.labels != null)
                    {
                        for (var i = 0; i < control.labels.Length; i++)
                        {
                            if (control.labels[i].icon == null)
                                continue;
                            if (remove)
                            {
                                control.labels[i].icon = null;
                                count++;
                                continue;
                            }
                            var shrunk = Shrink(
                                control.labels[i].icon,
                                maxSize,
                                format,
                                converted,
                                context
                            );
                            if (shrunk == null)
                                continue;
                            control.labels[i].icon = shrunk;
                            count++;
                        }
                    }
                    Walk(control.subMenu);
                }
                EditorUtility.SetDirty(menu);
            }

            Walk(root);
            return count;
        }

        /// <summary>1 枚ぶん。</summary>
        static Texture2D Shrink(
            Texture2D source,
            int maxSize,
            TextureFormat format,
            Dictionary<Texture2D, Texture2D> cache,
            BuildContext context
        )
        {
            if (cache.TryGetValue(source, out var cached))
                return cached;

            var tooBig = source.width > maxSize || source.height > maxSize;
            var uncompressed = IsUncompressed(source.format);
            if (!tooBig && !uncompressed)
                return null;

            var width = Mathf.Max(1, Mathf.Min(maxSize, source.width));
            var height = Mathf.Max(1, Mathf.Min(maxSize, source.height));

            var rt = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            );
            var previous = RenderTexture.active;
            Texture2D result;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                result = new Texture2D(width, height, TextureFormat.RGBA32, true, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply(true, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            result.name = source.name;

            if (width >= 4 && height >= 4)
                EditorUtility.CompressTexture(result, format, TextureCompressionQuality.Normal);

            context.AssetSaver.SaveAsset(result);
            ObjectRegistry.RegisterReplacedObject(source, result);
            cache[source] = result;
            return result;
        }

        /// <summary>圧縮先の形式。</summary>
        static TextureFormat MobileTextureFormat() =>
            EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android
                ? TextureFormat.ASTC_6x6
                : TextureFormat.DXT5;

        /// <summary>非圧縮の形式か (圧縮し直す価値があるか)。</summary>
        static bool IsUncompressed(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.Alpha8:
                case TextureFormat.ARGB4444:
                case TextureFormat.RGB24:
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.RGB565:
                case TextureFormat.R16:
                case TextureFormat.RGBA4444:
                case TextureFormat.BGRA32:
                case TextureFormat.RHalf:
                case TextureFormat.RGHalf:
                case TextureFormat.RGBAHalf:
                case TextureFormat.RFloat:
                case TextureFormat.RGFloat:
                case TextureFormat.RGBAFloat:
                case TextureFormat.RG16:
                case TextureFormat.R8:
                    return true;
                default:
                    return false;
            }
        }
    }
}
