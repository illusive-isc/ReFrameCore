using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>ReFrameQuestBakeSet を作る / 焼き直す。</summary>
    internal static class ReFrameQuestBakeSetBuilder
    {
        /// <summary>アバターのマテリアルを全部焼いて、アセットへ保存する。</summary>
        internal static ReFrameQuestBakeSet Build(
            VRCAvatarDescriptor descriptor,
            ReFrameQuestBakeSet existing
        )
        {
            if (descriptor == null)
                return existing;

            var root = descriptor.transform;
            var toonLit = Shader.Find(ReFrameQuestMaterialConverter.ToonLitShaderName);
            if (toonLit == null)
            {
                Debug.LogError(
                    $"[ReFrameCore] '{ReFrameQuestMaterialConverter.ToonLitShaderName}' が見つかりません。"
                );
                return existing;
            }

            var path = existing != null ? AssetDatabase.GetAssetPath(existing) : null;
            if (string.IsNullOrEmpty(path))
                path = ResolveAssetPath(descriptor);
            if (string.IsNullOrEmpty(path))
                return existing;
            EnsureFolder(path);

            var signature = CurrentSignature(descriptor);
            using var questSide = new QuestSideScope(descriptor);
            ReFrameQuestMaterialConverter.PrepareSettings(root);

            ReFrameQuestMaterialConverter.ClearBakeSet();

            var target = ScriptableObject.CreateInstance<ReFrameQuestBakeSet>();
            var previous = AssetDatabase.LoadAssetAtPath<ReFrameQuestBakeSet>(path);
            if (previous != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(target, path);

            var entries = new List<ReFrameQuestBakeSet.Entry>();
            var seen = new HashSet<(Material, string)>();
            var sources = CollectSources(root);
            var index = 0;

            ReFrameQuestMaterialConverter.ForceQuestFormat = true;
            try
            {
                foreach (var (source, renderer, slot) in sources)
                {
                    index++;
                    var slotKey = renderer != null
                        ? ReFrameQuestMaterialConverter.SlotKey(renderer, slot)
                        : string.Empty;
                    if (source == null || !seen.Add((source, slotKey)))
                        continue;
                    EditorUtility.DisplayProgressBar(
                        "Quest 用マテリアルを焼いています",
                        source.name,
                        (float)index / Mathf.Max(1, sources.Count)
                    );

                    var baked = ReFrameQuestMaterialConverter.CreateToonLit(
                        source,
                        toonLit,
                        out var texture,
                        renderer,
                        slot
                    );
                    if (baked == null)
                        continue;
                    var suffix = renderer != null ? " (Quest " + slotKey + ")" : " (Quest)";
                    baked.name = source.name + suffix;
                    AssetDatabase.AddObjectToAsset(baked, target);
                    if (texture != null)
                    {
                        texture.name = source.name + suffix.Replace(")", " Texture)");
                        AssetDatabase.AddObjectToAsset(texture, target);
                    }
                    entries.Add(
                        new ReFrameQuestBakeSet.Entry
                        {
                            Source = source,
                            Baked = baked,
                            Signature = signature,
                            Slot = slotKey,
                        }
                    );
                }
            }
            finally
            {
                ReFrameQuestMaterialConverter.ForceQuestFormat = false;
                EditorUtility.ClearProgressBar();
            }

            target.Replace(entries, signature);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);

            Debug.Log(
                $"[ReFrameCore] Quest 用マテリアルを {entries.Count} 枚焼きました。" + (char)10 + path
            );
            return target;
        }

        /// <summary>焼き上がりの置き場。</summary>
        internal static string ResolveAssetPath(VRCAvatarDescriptor descriptor)
        {
            foreach (
                var component in ReFrameDeleteComponent.ActiveIn(descriptor)
            )
            {
                if (component == null)
                    continue;
                var declared = (ReFrameQuestBakeAttribute)
                    System.Attribute.GetCustomAttribute(
                        component.GetType(),
                        typeof(ReFrameQuestBakeAttribute),
                        true
                    );
                if (declared != null && !string.IsNullOrEmpty(declared.AssetPath))
                    return declared.AssetPath;
            }

            var avatar = SanitizeFileName(descriptor.name);
            var stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmm");
            return "Assets/" + BrandFolder + "/ReFrame/" + avatar + "/" + avatar + "_" + stamp + ".asset";
        }

        /// <summary>商品としての置き場所。</summary>
        const string BrandFolder = "Illusory Override";

        /// <summary>置き場の親フォルダを作る (Assets 配下のみ。</summary>
        static void EnsureFolder(string assetPath)
        {
            var folder = System.IO.Path.GetDirectoryName(assetPath);
            if (folder != null)
                folder = folder.Replace(System.IO.Path.DirectorySeparatorChar, '/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;
            if (!folder.StartsWith("Assets"))
                return;
            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static string SanitizeFileName(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        /// <summary>いまのアバターを焼いたらどうなるかの指紋。</summary>
        internal static string CurrentSignature(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null)
                return string.Empty;
            var root = descriptor.transform;
            using var questSide = new QuestSideScope(descriptor);
            ReFrameQuestMaterialConverter.PrepareSettings(root);

            var builder = new System.Text.StringBuilder();
            builder.Append(ReFrameQuestMaterialConverter.SettingsSignature(root)).Append((char)10);
            var seen = new HashSet<Material>();
            foreach (var (source, renderer, slot) in CollectSources(root))
            {
                if (source == null || !seen.Add(source))
                    continue;

                var path = AssetDatabase.GetAssetPath(source);
                if (string.IsNullOrEmpty(path))
                {

                    builder.Append(source.name).Append('|').Append(source.shader.name);
                }
                else
                {
                    builder
                        .Append(AssetDatabase.AssetPathToGUID(path))
                        .Append('|')
                        .Append(AssetDatabase.GetAssetDependencyHash(path));
                }
                builder.Append((char)10);
            }
            return builder.ToString();
        }

        /// <summary>この間だけ Quest 簡易対応版を「使われる側」にする。置き場の指紋と焼き込みが、プレビューの切り替えやビルドターゲットに左右されないようにするため。</summary>
        readonly struct QuestSideScope : System.IDisposable
        {
            readonly int key;
            readonly bool had;
            readonly bool previous;

            public QuestSideScope(VRCAvatarDescriptor descriptor)
            {
                key = descriptor.gameObject.GetInstanceID();
                had = ReFrameDeleteComponent.PreviewQuestOverride.TryGetValue(key, out previous);
                ReFrameDeleteComponent.PreviewQuestOverride[key] = true;
                ReFrameQuestMaterialPreview.ResetScope();
            }

            public void Dispose()
            {
                if (had)
                    ReFrameDeleteComponent.PreviewQuestOverride[key] = previous;
                else
                    ReFrameDeleteComponent.PreviewQuestOverride.Remove(key);
                ReFrameQuestMaterialPreview.ResetScope();
            }
        }

        /// <summary>焼き済みが古くなっているか。</summary>
        internal static bool IsStale(VRCAvatarDescriptor descriptor, ReFrameQuestBakeSet set)
        {
            if (descriptor == null || set == null)
                return false;
            return set.Signature != CurrentSignature(descriptor);
        }

        /// <summary>焼く対象。中身を焼き込む枠は Renderer と枠番号も持つ。</summary>
        static List<(Material Material, Renderer Renderer, int Slot)> CollectSources(Transform root)
        {
            var result = new List<(Material, Renderer, int)>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;
                var subMeshes = ReFrameQuestMaterialConverter.SubMeshCountOf(renderer);
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null || material.shader == null)
                        continue;
                    if (material.shader.name.StartsWith("VRChat/Mobile/"))
                        continue;

                    if (subMeshes > 0 && i >= subMeshes)
                        continue;
                    if (ReFrameQuestMaterialConverter.IsDroppedSlot(renderer, i))
                        continue;
                    if (ReFrameQuestMaterialConverter.IsShellSlot(renderer, i))
                    {
                        result.Add((material, renderer, i));
                        continue;
                    }
                    result.Add((material, null, -1));
                }
            }
            return result;
        }
    }
}
