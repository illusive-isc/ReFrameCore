using System.Collections.Generic;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>服に覆われて見えない体のポリゴンを切り取る (ReFrameCutCoveredAttribute)。</summary>
    internal static class ReFrameCoveredMeshCutter
    {
        /// <summary>空間分割の一辺 (メートル)。</summary>
        const float CellSize = 0.05f;

        /// <summary>体の表面から出発する距離。</summary>
        const float RayStart = 0.005f;

        internal static void Cut(BuildContext context, ReFrameDeleteComponent[] components)
        {
            var root = context.AvatarRootTransform;

            var asc = context.Extension<AnimatorServicesContext>();
            foreach (var component in components)
            {
                if (component == null || !component.cutCoveredMesh)
                    continue;
                foreach (
                    var attribute in component
                        .GetType()
                        .GetCustomAttributes(typeof(ReFrameCutCoveredAttribute), true)
                )
                {
                    var declaration = (ReFrameCutCoveredAttribute)attribute;
                    CutOne(context, root, asc, declaration);
                }
            }
        }

        static void CutOne(
            BuildContext context,
            Transform root,
            AnimatorServicesContext asc,
            ReFrameCutCoveredAttribute declaration
        )
        {
            if (string.IsNullOrEmpty(declaration.MaskAsset))
                return;

            var bodyTransform = root.Find(declaration.BodyPath);
            if (bodyTransform == null)
                return;
            var body = bodyTransform.GetComponent<SkinnedMeshRenderer>();
            if (body == null || body.sharedMesh == null)
                return;

            if (HasMeshEditingComponent(body.gameObject))
            {
                Debug.LogWarning(
                    $"[ReFrameCore] '{declaration.BodyPath}' にはメッシュ削減のコンポーネントが付いているので、"
                        + "覆われた面の切り取りは行いません (そちらの設定を優先)。"
                );
                return;
            }

            var triangleCount = body.sharedMesh.triangles.Length / 3;
            var masks = LoadMasks(declaration.MaskAsset, triangleCount);
            if (masks == null)
                return;

            var remove = new bool[triangleCount];
            var used = new List<string>();
            var skipped = new List<string>();
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (renderer == body || !renderer.enabled)
                    continue;
                if (!masks.TryGetValue(renderer.name, out var mask))
                    continue;
                if (CanStillBeHidden(renderer, root, asc))
                {
                    skipped.Add(renderer.name);
                    continue;
                }
                used.Add(renderer.name);
                for (var i = 0; i < triangleCount; i++)
                    if (mask[i])
                        remove[i] = true;
            }
            if (skipped.Count > 0)
                Debug.LogWarning(
                    $"[ReFrameCore] '{declaration.BodyPath}': まだ脱げる服の下は切りません"
                        + $" ({string.Join(", ", skipped)})。"
                        + "ON 固定で削除すると、その下も切れるようになります。"
                );
            if (used.Count == 0)
                return;

            var mesh = Object.Instantiate(body.sharedMesh);
            mesh.name = body.sharedMesh.name + " (ReFrame cut)";
            var removed = RemoveMarkedTriangles(mesh, remove);
            if (removed == 0)
            {
                Object.DestroyImmediate(mesh);
                return;
            }

            body.sharedMesh = mesh;
            context.AssetSaver.SaveAsset(mesh);
            Debug.LogWarning(
                $"[ReFrameCore] '{declaration.BodyPath}' から {removed} 個の三角形を切り取りました"
                    + $" (覆っている服: {string.Join(", ", used)})。"
            );
        }

        /// <summary>この服はまだ脱げるか。</summary>
        static bool CanStillBeHidden(Renderer renderer, Transform root, AnimatorServicesContext asc)
        {
            for (var cursor = renderer.transform; cursor != null && cursor != root; cursor = cursor.parent)
                if (IsAnimated(cursor, asc, typeof(GameObject), "m_IsActive"))
                    return true;

            return IsAnimated(renderer.transform, asc, null, "m_Enabled");
        }

        /// <summary>この Transform 上のこのプロパティを書くカーブが、削除後に残ったクリップにあるか。</summary>
        static bool IsAnimated(
            Transform target,
            AnimatorServicesContext asc,
            System.Type type,
            string propertyName
        )
        {
            foreach (var path in asc.ObjectPathRemapper.GetAllPathsForObject(target))
            foreach (var clip in asc.AnimationIndex.GetClipsForObjectPath(path))
            foreach (var binding in clip.GetFloatCurveBindings())
                if (
                    binding.path == path
                    && binding.propertyName == propertyName
                    && (type == null || binding.type == type)
                )
                    return true;
            return false;
        }

        /// <summary>テキストアセットから服ごとのマスクを読む。</summary>
        static Dictionary<string, bool[]> LoadMasks(string assetPath, int triangleCount)
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset == null)
            {
                Debug.LogWarning(
                    $"[ReFrameCore] 覆われた面のデータ '{assetPath}' が見つかりません。"
                        + "Inspector の「覆われた面を計算する」で作ってください。"
                );
                return null;
            }

            var masks = new Dictionary<string, bool[]>();
            foreach (var line in asset.text.Split((char)10))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                    continue;
                var parts = trimmed.Split('|');
                if (parts.Length != 3 || !int.TryParse(parts[1], out var count))
                    continue;
                if (count != triangleCount)
                {
                    Debug.LogWarning(
                        $"[ReFrameCore] '{assetPath}' の三角形数が実際と違います"
                            + $" (データ {count} / 実際 {triangleCount})。メッシュが差し替わっているので"
                            + "切り取りません。計算し直してください。"
                    );
                    return null;
                }
                var decoded = Decode(parts[2], triangleCount);
                if (decoded != null)
                    masks[parts[0]] = decoded;
            }
            return masks.Count > 0 ? masks : null;
        }

        static bool[] Decode(string mask, int triangleCount)
        {
            try
            {
                var bytes = System.Convert.FromBase64String(mask);
                var flags = new bool[triangleCount];
                for (var i = 0; i < triangleCount; i++)
                    flags[i] = (bytes[i / 8] & (1 << (i % 8))) != 0;
                return flags;
            }
            catch (System.Exception)
            {
                Debug.LogWarning("[ReFrameCore] 覆われた面のマスクを読めませんでした。");
                return null;
            }
        }

        static string Encode(bool[] flags)
        {
            var bytes = new byte[(flags.Length + 7) / 8];
            for (var i = 0; i < flags.Length; i++)
                if (flags[i])
                    bytes[i / 8] |= (byte)(1 << (i % 8));
            return System.Convert.ToBase64String(bytes);
        }

        /// <summary>覆われた面を服ごとに計算し、宣言で指定したテキストアセットへ書き出す。</summary>
        internal static string Bake(ReFrameDeleteComponent component)
        {
            var root = component.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            if (root == null)
                return null;

            foreach (
                var attribute in component
                    .GetType()
                    .GetCustomAttributes(typeof(ReFrameCutCoveredAttribute), true)
            )
            {
                var declaration = (ReFrameCutCoveredAttribute)attribute;
                if (string.IsNullOrEmpty(declaration.MaskAsset))
                    continue;
                var bodyTransform = root.transform.Find(declaration.BodyPath);
                if (bodyTransform == null)
                    continue;
                var body = bodyTransform.GetComponent<SkinnedMeshRenderer>();
                if (body == null || body.sharedMesh == null)
                    continue;

                var indices = body.sharedMesh.triangles;
                var triangleCount = indices.Length / 3;
                var allowed = new HashSet<string>(declaration.Covers);

                var baked = new Mesh();
                body.BakeMesh(baked, true);

                var text = new System.Text.StringBuilder();
                text.Append("# ReFrame: 服に覆われた ").Append(declaration.BodyPath).Append(" の面").Append((char)10);
                text.Append("# 1 行 = 服の名前|三角形数|Base64 のビットマスク").Append((char)10);
                var report = new List<string>();

                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer == body || renderer.sharedMesh == null)
                        continue;
                    if (allowed.Count > 0 && !allowed.Contains(renderer.name))
                        continue;

                    var covered = FindCoveredVertices(body, baked, TrianglesOf(renderer), declaration.MaxDistance);
                    var flags = new bool[triangleCount];
                    var count = 0;
                    for (var i = 0; i < triangleCount; i++)
                    {
                        if (
                            covered[indices[i * 3]]
                            && covered[indices[i * 3 + 1]]
                            && covered[indices[i * 3 + 2]]
                        )
                        {
                            flags[i] = true;
                            count++;
                        }
                    }
                    if (count == 0)
                        continue;
                    text.Append(renderer.name).Append('|').Append(triangleCount).Append('|')
                        .Append(Encode(flags)).Append((char)10);
                    report.Add(renderer.name + " " + count + " tri");
                }
                Object.DestroyImmediate(baked);

                WriteAsset(declaration.MaskAsset, text.ToString());
                Debug.Log(
                    $"[ReFrameCore] 覆われた面を '{declaration.MaskAsset}' に書き出しました: "
                        + string.Join(", ", report)
                );
                return declaration.MaskAsset;
            }
            return null;
        }

        static void WriteAsset(string assetPath, string contents)
        {
            var directory = System.IO.Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory))
                System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(assetPath, contents);
            UnityEditor.AssetDatabase.ImportAsset(assetPath);
        }

        /// <summary>1 つの Renderer の三角形をワールド座標で取り出す。</summary>
        static List<ClothTriangle> TrianglesOf(SkinnedMeshRenderer renderer)
        {
            var triangles = new List<ClothTriangle>();
            var baked = new Mesh();
            renderer.BakeMesh(baked, true);
            var vertices = baked.vertices;
            var indices = baked.triangles;
            var toWorld = renderer.transform.localToWorldMatrix;
            for (var i = 0; i < indices.Length; i += 3)
            {
                triangles.Add(
                    new ClothTriangle
                    {
                        A = toWorld.MultiplyPoint3x4(vertices[indices[i]]),
                        B = toWorld.MultiplyPoint3x4(vertices[indices[i + 1]]),
                        C = toWorld.MultiplyPoint3x4(vertices[indices[i + 2]]),
                    }
                );
            }
            Object.DestroyImmediate(baked);
            return triangles;
        }

        /// <summary>AvatarOptimizer のメッシュ編集コンポーネントが付いているか。</summary>
        static bool HasMeshEditingComponent(GameObject target)
        {
            foreach (var component in target.GetComponents<Component>())
            {
                if (component == null)
                    continue;
                for (var type = component.GetType(); type != null; type = type.BaseType)
                {
                    if (type.FullName == "Anatawa12.AvatarOptimizer.EditSkinnedMeshComponent")
                        return true;
                }
            }
            return false;
        }

        struct ClothTriangle
        {
            public Vector3 A;
            public Vector3 B;
            public Vector3 C;
        }

        static long CellKey(int x, int y, int z) =>
            ((long)x * 73856093) ^ ((long)y * 19349663) ^ ((long)z * 83492791);

        static Dictionary<long, List<int>> BuildGrid(List<ClothTriangle> triangles)
        {
            var grid = new Dictionary<long, List<int>>();
            for (var i = 0; i < triangles.Count; i++)
            {
                var t = triangles[i];
                var min = Vector3.Min(t.A, Vector3.Min(t.B, t.C));
                var max = Vector3.Max(t.A, Vector3.Max(t.B, t.C));
                for (var x = Mathf.FloorToInt(min.x / CellSize); x <= Mathf.FloorToInt(max.x / CellSize); x++)
                for (var y = Mathf.FloorToInt(min.y / CellSize); y <= Mathf.FloorToInt(max.y / CellSize); y++)
                for (var z = Mathf.FloorToInt(min.z / CellSize); z <= Mathf.FloorToInt(max.z / CellSize); z++)
                {
                    var key = CellKey(x, y, z);
                    if (!grid.TryGetValue(key, out var list))
                    {
                        list = new List<int>();
                        grid[key] = list;
                    }
                    list.Add(i);
                }
            }
            return grid;
        }

        static bool[] FindCoveredVertices(
            SkinnedMeshRenderer body,
            Mesh baked,
            List<ClothTriangle> clothes,
            float maxDistance
        )
        {
            var grid = BuildGrid(clothes);
            var vertices = baked.vertices;
            var normals = baked.normals;
            var toWorld = body.transform.localToWorldMatrix;
            var covered = new bool[vertices.Length];
            var visited = new HashSet<int>();

            for (var i = 0; i < vertices.Length; i++)
            {
                var origin = toWorld.MultiplyPoint3x4(vertices[i]);
                var direction = toWorld.MultiplyVector(normals[i]).normalized;
                visited.Clear();

                for (var step = RayStart; step <= maxDistance && !covered[i]; step += CellSize * 0.5f)
                {
                    var probe = origin + direction * step;
                    var key = CellKey(
                        Mathf.FloorToInt(probe.x / CellSize),
                        Mathf.FloorToInt(probe.y / CellSize),
                        Mathf.FloorToInt(probe.z / CellSize)
                    );
                    if (!grid.TryGetValue(key, out var list))
                        continue;
                    foreach (var index in list)
                    {
                        if (!visited.Add(index))
                            continue;
                        if (HitsBackFace(origin, direction, clothes[index], maxDistance))
                        {
                            covered[i] = true;
                            break;
                        }
                    }
                }
            }
            return covered;
        }

        /// <summary>レイが三角形の裏側に当たるか (Möller-Trumbore)。</summary>
        static bool HitsBackFace(Vector3 origin, Vector3 direction, ClothTriangle triangle, float maxDistance)
        {
            var edge1 = triangle.B - triangle.A;
            var edge2 = triangle.C - triangle.A;
            if (Vector3.Dot(direction, Vector3.Cross(edge1, edge2)) <= 0f)
                return false;

            var p = Vector3.Cross(direction, edge2);
            var determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 1e-9f)
                return false;

            var inverse = 1f / determinant;
            var toOrigin = origin - triangle.A;
            var u = Vector3.Dot(toOrigin, p) * inverse;
            if (u < 0f || u > 1f)
                return false;

            var q = Vector3.Cross(toOrigin, edge1);
            var v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f)
                return false;

            var distance = Vector3.Dot(edge2, q) * inverse;
            return distance > RayStart * 0.4f && distance < maxDistance;
        }

        /// <summary>印の付いた三角形をインデックスから外す。</summary>
        static int RemoveMarkedTriangles(Mesh mesh, bool[] remove)
        {
            var removed = 0;
            var triangle = 0;
            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var indices = mesh.GetTriangles(submesh);
                var kept = new List<int>(indices.Length);
                for (var i = 0; i < indices.Length; i += 3, triangle++)
                {
                    if (triangle < remove.Length && remove[triangle])
                    {
                        removed++;
                        continue;
                    }
                    kept.Add(indices[i]);
                    kept.Add(indices[i + 1]);
                    kept.Add(indices[i + 2]);
                }
                if (kept.Count != indices.Length)
                    mesh.SetTriangles(kept, submesh, false);
            }
            if (removed > 0)
                mesh.RecalculateBounds();
            return removed;
        }
    }
}
