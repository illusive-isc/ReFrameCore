using System.Collections.Generic;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>透過シェルの中身を焼き込んで不透明なテクスチャにする (袋と中身の位置関係が固定な前提の視点非依存近似)。</summary>
    internal static class ReFrameQuestShellBake
    {
        /// <summary>焼き込みの調整。</summary>
        internal struct Options
        {
            /// <summary>この値以上のアルファは不透明として触らない。</summary>
            public float OpaqueThreshold;

            /// <summary>何にも当たらなかったときの色 (null なら袋自身の色)。</summary>
            public Color? Beyond;

            /// <summary>何も写らなかった膜の三角形を切るか。</summary>
            public bool CutEmpty;

            /// <summary>閉じた両端からも中身を写すか (開封して形が崩れた袋向け)。</summary>
            public bool AllSides;

            /// <summary>焼くテクスチャの一辺 (0 なら元のテクスチャと解像度上限に従う)。膜のテクセルが粗いときに上げる。</summary>
            public int Size;

            /// <summary>縁のハイライトの強さ (0 で無効)。</summary>
            public float RimStrength;

            /// <summary>縁のハイライトの鋭さ。</summary>
            public float RimPower;

            /// <summary>光に向いた面に乗せる艶の強さ (0 で無効)。</summary>
            public float Gloss;

            /// <summary>艶の鋭さ。</summary>
            public float GlossPower;

            /// <summary>ハイライトの光方向 (レンダラーのローカル空間)。</summary>
            public Vector3 LightDirection;

            public static Options Default =>
                new Options
                {
                    OpaqueThreshold = 0.995f,
                    Beyond = null,
                    RimStrength = 0.1f,
                    RimPower = 3f,
                    Gloss = 0f,
                    GlossPower = 12f,
                    LightDirection = new Vector3(0.3f, 0.8f, -0.5f),
                };
        }

        struct Triangle
        {
            public Vector3 A, B, C;
            public Vector3 Na, Nb, Nc;
            public Vector2 Ua, Ub, Uc;
            public Vector3 E1, E2;
            public Vector3 FaceNormal;
            public int Component;
            public bool Content;

            /// <summary>箱の 3 軸それぞれに直交する平面へ投影した範囲 (レイの早い棄却用)。</summary>
            public Vector2[] Min, Max;
        }

        /// <summary>Renderer の枠 slot が使うテクスチャ (読み書き可、非圧縮) の透過テクセルを、中身の色で埋める。何も写らなかった透過テクセルはアルファ 0 で残す (後で CutEmpty が三角形ごと切る)。</summary>
        internal static int Composite(Renderer renderer, int slot, Texture2D texture, Options options)
        {
            var mesh = ResolveMesh(renderer, out var owned);
            if (mesh == null || slot < 0 || slot >= mesh.subMeshCount)
                return 0;
            try
            {
                var tris = BuildTriangles(mesh, slot).ToArray();
                if (tris.Length == 0)
                    return 0;
                return Composite(tris, texture, options);
            }
            finally
            {
                if (owned)
                    Object.DestroyImmediate(mesh);
            }
        }

        static Mesh ResolveMesh(Renderer renderer, out bool owned)
        {
            owned = false;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null)
                    return null;
                var baked = new Mesh();
                skinned.BakeMesh(baked, true);
                owned = true;
                return baked;
            }
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        static List<Triangle> BuildTriangles(Mesh mesh, int slot)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            var indices = mesh.GetTriangles(slot);
            var list = new List<Triangle>(indices.Length / 3);
            if (normals.Length != vertices.Length || uvs.Length != vertices.Length)
                return list;
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var a = indices[i];
                var b = indices[i + 1];
                var c = indices[i + 2];
                list.Add(
                    new Triangle
                    {
                        A = vertices[a],
                        B = vertices[b],
                        C = vertices[c],
                        Na = normals[a],
                        Nb = normals[b],
                        Nc = normals[c],
                        Ua = uvs[a],
                        Ub = uvs[b],
                        Uc = uvs[c],
                        E1 = vertices[b] - vertices[a],
                        E2 = vertices[c] - vertices[a],
                        FaceNormal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized,
                    }
                );
            }
            LabelComponents(list);
            return list;
        }

        /// <summary>頂点を共有する三角形を同じ連結成分として番号付けする。袋と中身を区別するため。</summary>
        static void LabelComponents(List<Triangle> tris)
        {
            var extent = 0f;
            foreach (var t in tris)
                extent = Mathf.Max(extent, t.E1.magnitude, t.E2.magnitude);
            var scale = extent > 0f ? 1e4f / extent : 1f;
            Vector3Int Key(Vector3 p) =>
                new Vector3Int(Mathf.RoundToInt(p.x * scale), Mathf.RoundToInt(p.y * scale), Mathf.RoundToInt(p.z * scale));

            var parent = new int[tris.Count];
            for (var i = 0; i < parent.Length; i++)
                parent[i] = i;
            int Find(int i)
            {
                while (parent[i] != i)
                    i = parent[i] = parent[parent[i]];
                return i;
            }
            var owner = new Dictionary<Vector3Int, int>();
            for (var i = 0; i < tris.Count; i++)
            {
                foreach (var p in new[] { tris[i].A, tris[i].B, tris[i].C })
                {
                    var key = Key(p);
                    if (owner.TryGetValue(key, out var other))
                        parent[Find(i)] = Find(other);
                    else
                        owner[key] = i;
                }
            }
            for (var i = 0; i < tris.Count; i++)
            {
                var t = tris[i];
                t.Component = Find(i);
                tris[i] = t;
            }
        }

        static int Composite(Triangle[] tris, Texture2D texture, Options options)
        {
            var width = texture.width;
            var height = texture.height;
            var source = texture.GetPixels();
            var result = (Color[])source.Clone();
            var extent = 0f;
            foreach (var t in tris)
                extent = Mathf.Max(extent, t.E1.magnitude, t.E2.magnitude);
            var epsilon = extent * 1e-4f;
            var light = options.LightDirection.normalized;
            var directions = BoxDirections(tris);
            MarkContent(tris, source, width, height, options.OpaqueThreshold);
            ProjectExtents(tris, directions);
            var filled = 0;
            var owner = new int[source.Length];
            var inwards = new Vector3[source.Length];
            var axes = new int[source.Length];
            var normals = new Vector3[source.Length];
            for (var i = 0; i < owner.Length; i++)
                owner[i] = -1;

            for (var ti = 0; ti < tris.Length; ti++)
            {
                ref var t = ref tris[ti];
                var nearest = 0;
                for (var k = 1; k < directions.Length; k++)
                    if (Vector3.Dot(t.FaceNormal, directions[k]) > Vector3.Dot(t.FaceNormal, directions[nearest]))
                        nearest = k;
                var facing = options.AllSides || nearest < 4;
                var inward = -directions[nearest];
                var axis = nearest / 2;
                var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(t.Ua.x, t.Ub.x, t.Uc.x) * width) - 1);
                var maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(t.Ua.x, t.Ub.x, t.Uc.x) * width) + 1);
                var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(t.Ua.y, t.Ub.y, t.Uc.y) * height) - 1);
                var maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(t.Ua.y, t.Ub.y, t.Uc.y) * height) + 1);
                if (minX > maxX || minY > maxY)
                    continue;

                var d = (t.Ub.y - t.Uc.y) * (t.Ua.x - t.Uc.x) + (t.Uc.x - t.Ub.x) * (t.Ua.y - t.Uc.y);
                if (Mathf.Abs(d) < 1e-12f)
                    continue;
                var invD = 1f / d;
                var slack = -1.5f / Mathf.Min(width, height) / Mathf.Sqrt(Mathf.Abs(d));

                for (var y = minY; y <= maxY; y++)
                {
                    var py = (y + 0.5f) / height;
                    for (var x = minX; x <= maxX; x++)
                    {
                        var index = y * width + x;
                        var texel = source[index];
                        if (texel.a >= options.OpaqueThreshold)
                            continue;

                        var px = (x + 0.5f) / width;
                        var w0 = ((t.Ub.y - t.Uc.y) * (px - t.Uc.x) + (t.Uc.x - t.Ub.x) * (py - t.Uc.y)) * invD;
                        var w1 = ((t.Uc.y - t.Ua.y) * (px - t.Uc.x) + (t.Ua.x - t.Uc.x) * (py - t.Uc.y)) * invD;
                        var w2 = 1f - w0 - w1;
                        if (w0 < slack || w1 < slack || w2 < slack)
                            continue;

                        owner[index] = facing ? ti : -1;
                        inwards[index] = inward;
                        axes[index] = axis;
                        normals[index] = (t.Na * w0 + t.Nb * w1 + t.Nc * w2).normalized;
                        var behind = owner[index] >= 0
                            ? Trace(tris, t.Component, t.A * w0 + t.B * w1 + t.C * w2, inward, axis, directions, epsilon, source, width, height, options)
                            : null;
                        if (behind == null)
                        {
                            result[index].a = 0f;
                            continue;
                        }
                        result[index] = Shade(behind.Value, texel, (t.Na * w0 + t.Nb * w1 + t.Nc * w2).normalized, light, options);
                        filled++;
                    }
                }
            }

            Smooth(tris, owner, inwards, axes, directions, source, result, width, height, epsilon, light, options);
            FillEmpty(source, result, normals, light, width, height, options);
            texture.SetPixels(result);
            texture.Apply(true, false);
            return filled;
        }

        /// <summary>中身の色に袋の色をアルファで重ね、縁のハイライトを足す。</summary>
        static Color Shade(Color behind, Color shell, Vector3 normal, Vector3 light, Options options)
        {
            var alpha = shell.a;
            var add = Highlight(normal, light, options) * (1f - alpha);
            return new Color(
                Mathf.Clamp01(Mathf.Lerp(behind.r, shell.r, alpha) + add),
                Mathf.Clamp01(Mathf.Lerp(behind.g, shell.g, alpha) + add),
                Mathf.Clamp01(Mathf.Lerp(behind.b, shell.b, alpha) + add),
                1f
            );
        }

        /// <summary>縁のハイライトと、光に向いた面の艶。</summary>
        static float Highlight(Vector3 normal, Vector3 light, Options options)
        {
            var facing = Mathf.Clamp01(Vector3.Dot(normal, light));
            var add = 0f;
            if (options.RimStrength > 0f)
                add += Mathf.Pow(1f - facing, options.RimPower) * options.RimStrength;
            if (options.Gloss > 0f)
                add += Mathf.Pow(facing, options.GlossPower) * options.Gloss;
            return add;
        }

        /// <summary>写った/写らないの境目のテクセルだけ 4x4 に細分してレイを飛ばし直し、中身の輪郭の階段を均す。</summary>
        static void Smooth(
            Triangle[] tris,
            int[] owner,
            Vector3[] inwards,
            int[] axes,
            Vector3[] directions,
            Color[] source,
            Color[] result,
            int width,
            int height,
            float epsilon,
            Vector3 light,
            Options options
        )
        {
            const int sub = 4;
            var hit = new bool[result.Length];
            for (var i = 0; i < result.Length; i++)
                hit[i] = result[i].a > 0f;
            var smoothed = (Color[])result.Clone();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (owner[index] < 0)
                        continue;
                    var edge = false;
                    for (var k = 0; k < 4 && !edge; k++)
                    {
                        var nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0);
                        var ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            continue;
                        var n = ny * width + nx;
                        edge = source[n].a < options.OpaqueThreshold && hit[n] != hit[index];
                    }
                    if (!edge)
                        continue;

                    ref var t = ref tris[owner[index]];
                    var d = (t.Ub.y - t.Uc.y) * (t.Ua.x - t.Uc.x) + (t.Uc.x - t.Ub.x) * (t.Ua.y - t.Uc.y);
                    var invD = 1f / d;
                    var texel = source[index];
                    var beyond = options.Beyond ?? texel;
                    var sum = Color.black;
                    var hits = 0;
                    for (var sy = 0; sy < sub; sy++)
                    {
                        var py = (y + (sy + 0.5f) / sub) / height;
                        for (var sx = 0; sx < sub; sx++)
                        {
                            var px = (x + (sx + 0.5f) / sub) / width;
                            var w0 = ((t.Ub.y - t.Uc.y) * (px - t.Uc.x) + (t.Uc.x - t.Ub.x) * (py - t.Uc.y)) * invD;
                            var w1 = ((t.Uc.y - t.Ua.y) * (px - t.Uc.x) + (t.Ua.x - t.Uc.x) * (py - t.Uc.y)) * invD;
                            var w2 = 1f - w0 - w1;
                            var position = t.A * w0 + t.B * w1 + t.C * w2;
                            var behind = Trace(tris, t.Component, position, inwards[index], axes[index], directions, epsilon, source, width, height, options);
                            if (behind == null)
                            {
                                sum += new Color(beyond.r, beyond.g, beyond.b, 0f);
                                continue;
                            }
                            hits++;
                            sum += Shade(behind.Value, texel, (t.Na * w0 + t.Nb * w1 + t.Nc * w2).normalized, light, options);
                        }
                    }
                    sum /= sub * sub;
                    smoothed[index] = new Color(sum.r, sum.g, sum.b, hits > 0 ? 1f : 0f);
                }
            }
            System.Array.Copy(smoothed, result, result.Length);
        }

        /// <summary>何も写らなかった透過テクセルを Beyond (無ければ袋自身の色) で埋める。膜を切る指定なら、切れずに残る縁が目立たないよう近くの描かれた色を延ばす。アルファ 0 は「何も写らなかった」印として残す。</summary>
        static void FillEmpty(Color[] source, Color[] result, Vector3[] normals, Vector3 light, int width, int height, Options options)
        {
            var drawn = new bool[result.Length];
            for (var i = 0; i < result.Length; i++)
            {
                drawn[i] = result[i].a > 0f || source[i].a >= options.OpaqueThreshold;
                if (drawn[i])
                    result[i].a = 1f;
            }
            var radius = options.CutEmpty ? 24 : 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (drawn[index])
                        continue;
                    var found = false;
                    for (var r = 1; r <= radius && !found; r++)
                    {
                        for (var dy = -r; dy <= r && !found; dy++)
                        {
                            for (var dx = -r; dx <= r; dx++)
                            {
                                if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r)
                                    continue;
                                var sx = x + dx;
                                var sy = y + dy;
                                if (sx < 0 || sy < 0 || sx >= width || sy >= height)
                                    continue;
                                var s = sy * width + sx;
                                if (!drawn[s])
                                    continue;
                                result[index] = new Color(result[s].r, result[s].g, result[s].b, 0f);
                                found = true;
                                break;
                            }
                        }
                    }
                    if (!found)
                    {
                        var beyond = options.Beyond ?? source[index];
                        var shell = source[index];
                        var add = normals[index] == Vector3.zero ? 0f : Highlight(normals[index], light, options) * (1f - shell.a);
                        result[index] = new Color(
                            Mathf.Clamp01(Mathf.Lerp(beyond.r, shell.r, shell.a) + add),
                            Mathf.Clamp01(Mathf.Lerp(beyond.g, shell.g, shell.a) + add),
                            Mathf.Clamp01(Mathf.Lerp(beyond.b, shell.b, shell.a) + add),
                            0f
                        );
                    }
                }
            }
        }

        /// <summary>膜を持つ連結成分 (袋・ちぎれた口) の三角形を枠から切り、中身だけ残したメッシュを返す。切るものが無ければ null。</summary>
        internal static Mesh CutShell(Mesh mesh, int slot, Texture source)
        {
            if (mesh == null || source == null || slot < 0 || slot >= mesh.subMeshCount)
                return null;
            var alpha = ReFrameQuestAssetTrim.ReadAlpha(source, out var size);
            if (alpha == null)
                return null;
            var tris = BuildTriangles(mesh, slot).ToArray();
            if (tris.Length == 0)
                return null;
            var pixels = new Color[alpha.Length];
            for (var i = 0; i < alpha.Length; i++)
                pixels[i].a = alpha[i];
            MarkContent(tris, pixels, size, size, Options.Default.OpaqueThreshold);
            var indices = mesh.GetTriangles(slot);
            var kept = new List<int>(indices.Length);
            var removed = 0;
            for (var i = 0; i < tris.Length; i++)
            {
                if (!tris[i].Content)
                {
                    removed++;
                    continue;
                }
                kept.Add(indices[i * 3]);
                kept.Add(indices[i * 3 + 1]);
                kept.Add(indices[i * 3 + 2]);
            }
            if (removed == 0)
                return null;
            var clone = Object.Instantiate(mesh);
            clone.name = mesh.name;
            clone.SetTriangles(kept, slot);
            return clone;
        }

        /// <summary>焼き上がりのアルファが 0 の三角形 (透明で何も写らなかった膜) を枠から切ったメッシュを返す。切るものが無ければ null。</summary>
        internal static Mesh CutEmpty(Mesh mesh, int slot, Texture baked)
        {
            if (mesh == null || baked == null || slot < 0 || slot >= mesh.subMeshCount)
                return null;
            var alpha = ReFrameQuestAssetTrim.ReadAlpha(baked, out var size);
            var uvs = mesh.uv;
            if (alpha == null || uvs == null || uvs.Length == 0)
                return null;
            var triangles = mesh.GetTriangles(slot);
            var kept = new List<int>(triangles.Length);
            var removed = 0;
            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                var a = triangles[i];
                var b = triangles[i + 1];
                var c = triangles[i + 2];
                var visible =
                    a >= uvs.Length
                    || b >= uvs.Length
                    || c >= uvs.Length
                    || ReFrameQuestAssetTrim.Sample(alpha, size, (uvs[a] + uvs[b] + uvs[c]) / 3f) >= 0.5f
                    || ReFrameQuestAssetTrim.Sample(alpha, size, uvs[a]) >= 0.5f
                    || ReFrameQuestAssetTrim.Sample(alpha, size, uvs[b]) >= 0.5f
                    || ReFrameQuestAssetTrim.Sample(alpha, size, uvs[c]) >= 0.5f;
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
                return null;
            var clone = Object.Instantiate(mesh);
            clone.name = mesh.name;
            clone.SetTriangles(kept, slot);
            return clone;
        }

        /// <summary>三角形の重心のテクセルが透過な割合が低い連結成分だけを「中身」とする。膜を持つ成分 (袋・ちぎれた口) は互いに写し合わない。</summary>
        static void MarkContent(Triangle[] tris, Color[] source, int width, int height, float opaqueThreshold)
        {
            var total = new Dictionary<int, int>();
            var translucent = new Dictionary<int, int>();
            foreach (var t in tris)
            {
                total[t.Component] = total.TryGetValue(t.Component, out var n) ? n + 1 : 1;
                var centre = (t.Ua + t.Ub + t.Uc) / 3f;
                if (Sample(source, width, height, centre).a < opaqueThreshold)
                    translucent[t.Component] = translucent.TryGetValue(t.Component, out var m) ? m + 1 : 1;
            }
            for (var i = 0; i < tris.Length; i++)
            {
                var component = tris[i].Component;
                var ratio = translucent.TryGetValue(component, out var m) ? (float)m / total[component] : 0f;
                tris[i].Content = ratio < 0.2f;
            }
        }

        /// <summary>direction へレイを飛ばし、中身 (透過テクセルを持たない別の連結成分) の面の色を返す。無ければ null。</summary>
        static Color? Trace(
            Triangle[] tris,
            int component,
            Vector3 origin,
            Vector3 direction,
            int axis,
            Vector3[] directions,
            float epsilon,
            Color[] source,
            int width,
            int height,
            Options options
        )
        {
            var start = origin + direction * epsilon;
            var pu = Vector3.Dot(origin, directions[(axis + 1) % 3 * 2]);
            var pv = Vector3.Dot(origin, directions[(axis + 2) % 3 * 2]);
            while (true)
            {
                var nearest = float.MaxValue;
                var hit = -1;
                var hitU = 0f;
                var hitV = 0f;
                for (var i = 0; i < tris.Length; i++)
                {
                    if (!tris[i].Content || tris[i].Component == component)
                        continue;
                    var min = tris[i].Min[axis];
                    var max = tris[i].Max[axis];
                    if (pu < min.x || pu > max.x || pv < min.y || pv > max.y)
                        continue;
                    if (Intersect(in tris[i], start, direction, out var distance, out var u, out var v) && distance < nearest)
                    {
                        nearest = distance;
                        hit = i;
                        hitU = u;
                        hitV = v;
                    }
                }
                if (hit < 0)
                    return null;

                ref var t = ref tris[hit];
                var w0 = 1f - hitU - hitV;
                var texel = Sample(source, width, height, t.Ua * w0 + t.Ub * hitU + t.Uc * hitV);
                if (texel.a >= options.OpaqueThreshold)
                    return new Color(texel.r, texel.g, texel.b, 1f);
                start += direction * (nearest + epsilon);
            }
        }

        /// <summary>各三角形を箱の 3 軸それぞれに直交する平面へ投影した範囲を控える。</summary>
        static void ProjectExtents(Triangle[] tris, Vector3[] directions)
        {
            for (var i = 0; i < tris.Length; i++)
            {
                ref var t = ref tris[i];
                t.Min = new Vector2[3];
                t.Max = new Vector2[3];
                for (var axis = 0; axis < 3; axis++)
                {
                    var u = directions[(axis + 1) % 3 * 2];
                    var v = directions[(axis + 2) % 3 * 2];
                    var a = new Vector2(Vector3.Dot(t.A, u), Vector3.Dot(t.A, v));
                    var b = new Vector2(Vector3.Dot(t.B, u), Vector3.Dot(t.B, v));
                    var c = new Vector2(Vector3.Dot(t.C, u), Vector3.Dot(t.C, v));
                    t.Min[axis] = Vector2.Min(a, Vector2.Min(b, c));
                    t.Max[axis] = Vector2.Max(a, Vector2.Max(b, c));
                }
            }
        }

        /// <summary>箱の 6 方向。先頭 4 つ (厚み方向の表裏と短い方の両側) は中身を写し、残りの長い方の両端は閉じた口とみなして写さない。</summary>
        static Vector3[] BoxDirections(Triangle[] tris)
        {
            var thickness = ThicknessAxis(tris);
            var center = Vector3.zero;
            var count = 0;
            foreach (var t in tris)
            {
                center += t.A + t.B + t.C;
                count += 3;
            }
            center /= Mathf.Max(1, count);
            var m = new float[2, 2];
            var u = Vector3.Cross(thickness, Mathf.Abs(thickness.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(thickness, u);
            foreach (var t in tris)
            {
                foreach (var p in new[] { t.A, t.B, t.C })
                {
                    var d = p - center;
                    var x = Vector3.Dot(d, u);
                    var y = Vector3.Dot(d, v);
                    m[0, 0] += x * x;
                    m[0, 1] += x * y;
                    m[1, 1] += y * y;
                }
            }
            var angle = 0.5f * Mathf.Atan2(2f * m[0, 1], m[0, 0] - m[1, 1]);
            var longest = (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)).normalized;
            var side = Vector3.Cross(thickness, longest).normalized;
            return new[] { thickness, -thickness, side, -side, longest, -longest };
        }

        /// <summary>面積で重み付けした法線の主成分 = 袋の厚み方向。</summary>
        static Vector3 ThicknessAxis(Triangle[] tris)
        {
            var m = new float[3, 3];
            foreach (var t in tris)
            {
                var n = Vector3.Cross(t.E1, t.E2);
                var area = n.magnitude;
                if (area <= 0f)
                    continue;
                n /= area;
                for (var i = 0; i < 3; i++)
                    for (var j = 0; j < 3; j++)
                        m[i, j] += n[i] * n[j] * area;
            }
            var v = Vector3.up;
            for (var k = 0; k < 64; k++)
            {
                var next = new Vector3(
                    m[0, 0] * v.x + m[0, 1] * v.y + m[0, 2] * v.z,
                    m[1, 0] * v.x + m[1, 1] * v.y + m[1, 2] * v.z,
                    m[2, 0] * v.x + m[2, 1] * v.y + m[2, 2] * v.z
                );
                if (next.sqrMagnitude <= 0f)
                    break;
                v = next.normalized;
            }
            return v;
        }

        /// <summary>Möller–Trumbore の両面判定。</summary>
        static bool Intersect(in Triangle t, Vector3 origin, Vector3 direction, out float distance, out float u, out float v)
        {
            distance = 0f;
            u = 0f;
            v = 0f;
            var px = direction.y * t.E2.z - direction.z * t.E2.y;
            var py = direction.z * t.E2.x - direction.x * t.E2.z;
            var pz = direction.x * t.E2.y - direction.y * t.E2.x;
            var det = t.E1.x * px + t.E1.y * py + t.E1.z * pz;
            if (det > -1e-12f && det < 1e-12f)
                return false;
            var invDet = 1f / det;
            var sx = origin.x - t.A.x;
            var sy = origin.y - t.A.y;
            var sz = origin.z - t.A.z;
            u = (sx * px + sy * py + sz * pz) * invDet;
            if (u < 0f || u > 1f)
                return false;
            var qx = sy * t.E1.z - sz * t.E1.y;
            var qy = sz * t.E1.x - sx * t.E1.z;
            var qz = sx * t.E1.y - sy * t.E1.x;
            v = (direction.x * qx + direction.y * qy + direction.z * qz) * invDet;
            if (v < 0f || u + v > 1f)
                return false;
            distance = (t.E2.x * qx + t.E2.y * qy + t.E2.z * qz) * invDet;
            return distance > 0f;
        }

        static Color Sample(Color[] pixels, int width, int height, Vector2 uv)
        {
            var x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * width), 0, width - 1);
            var y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.y, 1f) * height), 0, height - 1);
            return pixels[y * width + x];
        }
    }
}
