using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.runtime;
using nadena.dev.ndmf.runtime.components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>ReFrame の削除をシーン上のアバターへ焼き込み、生成物を Assets/ReFrameBaked 以下へ実体として置き、ReFrame のコンポーネントを外す。</summary>
    public static class ReFrameBake
    {
        public const string RootFolder = "Assets/ReFrameBaked";

        static readonly Type[] ControllerInternals =
        {
            typeof(AnimatorStateMachine),
            typeof(AnimatorState),
            typeof(AnimatorTransitionBase),
            typeof(UnityEditor.Animations.BlendTree),
            typeof(StateMachineBehaviour),
        };

        /// <summary>Inspector から呼ぶ入口。確認ダイアログを出してから焼く。</summary>
        public static void BakeWithDialog(GameObject avatarRoot)
        {
            if (avatarRoot == null)
                return;
            if (avatarRoot.GetComponent<VRCAvatarDescriptor>() == null)
            {
                EditorUtility.DisplayDialog("ReFrame 焼き込み", "VRCAvatarDescriptor のあるアバターのルートで実行してください。", "OK");
                return;
            }
            if (ReFrameDeleteComponent.ActiveIn(avatarRoot).Length == 0)
            {
                EditorUtility.DisplayDialog("ReFrame 焼き込み", "いまのビルドターゲットで有効な ReFrame のコンポーネントがありません。", "OK");
                return;
            }

            var target = ForQuest(avatarRoot)
                ? "Quest 用 (テクスチャは ASTC で書き出します)"
                : "PC 用";
            var folder = PlanFolder(avatarRoot);
            var active = ReFrameDeleteComponent.ActiveIn(avatarRoot);
            var inactive = avatarRoot
                .GetComponentsInChildren<ReFrameDeleteComponent>(true)
                .Where(c => c != null && !active.Contains(c))
                .ToArray();
            var usedLine = "・使う設定: " + string.Join(", ", active.Select(Describe).Distinct());
            if (inactive.Length > 0)
                usedLine += " — " + string.Join(", ", inactive.Select(Describe).Distinct()) + " は今のターゲットでは使われないので外れます";
            var ok = EditorUtility.DisplayDialog(
                "ReFrame 焼き込み",
                "ヒエラルキー上のこのアバターを、ReFrame の設定を適用した状態に書き換えます。\n\n"
                    + "・" + target + " として焼きます (プレビューで表示している側)\n"
                    + usedLine + "\n"
                    + "・FX / メニュー / パラメーター / クリップの複製を " + folder + " に置き、アバターはそこを参照するように張り替えます\n"
                    + "・元のプレハブとの繋がりは切れ、同じ場所に新しいプレハブとして保存します\n"
                    + "・ReFrame のコンポーネントは全部外れ、この後は設定を変えられません (元のプレハブは触らず、焼く前の姿も同じ場所に「(焼き込み前).prefab」として控えます)\n\n"
                    + "この操作は元に戻せません。続けますか?",
                "焼き込む",
                "やめる"
            );
            if (!ok)
                return;

            try
            {
                var result = Bake(avatarRoot);
                EditorGUIUtility.PingObject(result);
                Selection.activeGameObject = result;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("ReFrame 焼き込み", "焼き込みに失敗しました。アバターは変更していません。\n\n" + e.Message, "OK");
            }
        }

        /// <summary>複製に焼いて成功したら元と入れ替える。戻り値はヒエラルキーに残った焼き済みのアバター。</summary>
        public static GameObject Bake(GameObject avatarRoot)
        {
            if (avatarRoot == null)
                throw new ArgumentNullException(nameof(avatarRoot));
            if (avatarRoot.GetComponent<VRCAvatarDescriptor>() == null)
                throw new InvalidOperationException("VRCAvatarDescriptor がありません。");

            var components = ReFrameDeleteComponent.ActiveIn(avatarRoot);
            // 記録は ReFrame のコンポーネントが居た場所 (例: 子の "ReFrame") に残す。
            var markerPath = components.Length > 0
                ? AnimationUtility.CalculateTransformPath(components[0].transform, avatarRoot.transform)
                : "";
            var sweepMode = components.Any(c => c.SweepUnusedObjects) ? ReFrameSweepMode.Sweep : ReFrameSweepMode.LeaveToAvatarOptimizer;
            var version = PackageInfo.FindForAssembly(typeof(ReFrameDeleteComponent).Assembly)?.version ?? "?";
            var bakedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var forQuest = ForQuest(avatarRoot);
            var buildTarget = forQuest ? "Quest" : "PC";
            var sourcePrefab = SourcePrefabPath(avatarRoot);
            var componentNames = components.Select(c => c.GetType().Name).Distinct().ToArray();
            var entries = components
                .SelectMany(c => c.EnumerateDeleteTargets())
                .Select(t => t.ParameterName + "=" + t.Value + (t.MenuOnly ? " (menu-only)" : ""))
                .Distinct()
                .ToArray();

            var folder = PlanFolder(avatarRoot);
            var parentFolder = Path.GetDirectoryName(folder).Replace('\\', '/');
            var folderName = Path.GetFileName(folder);
            EnsureFolder(parentFolder);

            var clone = Object.Instantiate(avatarRoot);
            clone.name = folderName;
            // プレビューの PC / Quest 切り替えは元のインスタンス ID に紐付いているので、複製にも同じ側を写す。
            var previewSide = ReFramePreviewSide.Get(avatarRoot);
            if (previewSide.HasValue)
                ReFrameDeleteComponent.PreviewQuestOverride[clone.GetInstanceID()] = previewSide.Value;
            clone.transform.SetParent(avatarRoot.transform.parent, false);
            clone.transform.localPosition = avatarRoot.transform.localPosition;
            clone.transform.localRotation = avatarRoot.transform.localRotation;
            clone.transform.localScale = avatarRoot.transform.localScale;
            clone.SetActive(avatarRoot.activeSelf);

            string containerFolder = null;
            string bundlePath = null;
            var previousForce = ReFrameQuestMaterialConverter.ForceQuestFormat;
            try
            {
                // Quest 向けに焼くときは、Editor のターゲットが PC でもテクスチャを ASTC で作る。
                // 書き出した Texture2D はインポーターを持たないので、あとでターゲットを切り替えても圧縮し直されない。
                ReFrameQuestMaterialConverter.ForceQuestFormat = forQuest;
                var context = new BuildContext(clone, parentFolder, false);
                context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                ReFrameVariantSelectPass.RunForBake(context);
                ReFrameLayerRenamePass.RunForBake(context);
                ReFrameDeletePass.RunForBake(context);
                ReFrameSweepPass.SweepConfirmedForBake(context);
                context.DeactivateAllExtensionContexts();
                context.Serialize();

                // NDMF は <parent>/<名前>/<名前>.asset (GeneratedAssets) と <parent>/<名前>/_assets/ (サブコンテナ) を作る。
                var subPath = AssetDatabase.GetAssetPath(context.AssetContainer);
                if (string.IsNullOrEmpty(subPath))
                    throw new InvalidOperationException("NDMF の生成物コンテナが見つかりません。");
                containerFolder = Path.GetDirectoryName(subPath).Replace('\\', '/');
                folder = Path.GetDirectoryName(containerFolder).Replace('\\', '/');
                folderName = Path.GetFileName(folder);
                bundlePath = Directory
                    .GetFiles(folder, "*.asset", SearchOption.TopDirectoryOnly)
                    .Select(f => f.Replace('\\', '/'))
                    .FirstOrDefault(f => AssetDatabase.LoadAssetAtPath<GeneratedAssets>(f) != null);
                if (bundlePath == null)
                    throw new InvalidOperationException("NDMF の生成物 (GeneratedAssets) が見つかりません。");
                Extract(AssetDatabase.LoadAssetAtPath<GeneratedAssets>(bundlePath), folder, CollectOriginalClips(avatarRoot));

                var ndmfRoot = clone.GetComponent<NDMFAvatarRoot>();
                if (ndmfRoot != null)
                    Object.DestroyImmediate(ndmfRoot);
                foreach (var leftover in clone.GetComponentsInChildren<ReFrameDeleteComponent>(true))
                    Object.DestroyImmediate(leftover);

                clone.name = avatarRoot.name;
                var markerHost = string.IsNullOrEmpty(markerPath) ? null : clone.transform.Find(markerPath);
                var marker = (markerHost != null ? markerHost.gameObject : clone).AddComponent<ReFrameBakedInfo>();
                marker.reframeVersion = version;
                marker.bakedAt = bakedAt;
                marker.buildTarget = buildTarget;
                marker.sourcePrefab = sourcePrefab;
                marker.assetFolder = folder;
                marker.components = componentNames;
                marker.entries = entries;
                marker.sweepMode = sweepMode;
                ReFrameSweepPass.SaveSnapshot(context, marker);

                PrefabUtility.SaveAsPrefabAssetAndConnect(
                    clone,
                    folder + "/" + folderName + ".prefab",
                    InteractionMode.AutomatedAction
                );
            }
            catch
            {
                Object.DestroyImmediate(clone);
                if (AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
                throw;
            }
            finally
            {
                ReFrameQuestMaterialConverter.ForceQuestFormat = previousForce;
                ReFrameDeleteComponent.PreviewQuestOverride.Remove(clone.GetInstanceID());
                if (bundlePath != null && File.Exists(bundlePath))
                    AssetDatabase.DeleteAsset(bundlePath);
                if (containerFolder != null && AssetDatabase.IsValidFolder(containerFolder))
                    AssetDatabase.DeleteAsset(containerFolder);
            }

            // 焼く前の姿 (ReFrame の設定込み) を同じ置き場に控えておく。設定がシーンにしか無いアバターを戻せるように。
            PrefabUtility.SaveAsPrefabAsset(avatarRoot, folder + "/" + folderName + " (焼き込み前).prefab");

            var siblingIndex = avatarRoot.transform.GetSiblingIndex();
            Undo.RegisterCreatedObjectUndo(clone, "ReFrame: 焼き込み");
            Undo.DestroyObjectImmediate(avatarRoot);
            clone.transform.SetSiblingIndex(siblingIndex);
            AssetDatabase.SaveAssets();
            Debug.Log("[ReFrameCore] ReFrameBake: '" + clone.name + "' を焼き込みました → " + folder);
            return clone;
        }

        static string Describe(ReFrameDeleteComponent c) =>
            c.GetType().Name + (c.IsQuestVariant ? " (Quest 簡易対応版)" : " (PC 用)");

        static bool IsQuestTarget => EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;

        /// <summary>Quest 向けの焼き込みか (Android ターゲット、または Quest 変換が有効な設定がある)。プレビューの PC / Quest 切り替えに従う。</summary>
        public static bool ForQuest(GameObject avatarRoot) =>
            IsQuestTarget || ReFrameDeleteComponent.ActiveIn(avatarRoot).Any(c => c.QuestConversionActive);

        /// <summary>ボタンやダイアログに出す「何用」の表記。</summary>
        public static string TargetLabel(GameObject avatarRoot) => ForQuest(avatarRoot) ? "Quest 用" : "PC 用";

        /// <summary>Assets/ReFrameBaked/元のプレハブ名/ヒエラルキー上の名前 (既にあれば " (2)" …)。</summary>
        public static string PlanFolder(GameObject avatarRoot)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(avatarRoot);
            var sourceName = SafeName(source != null ? source.name : avatarRoot.name);
            var baseName = SafeName(avatarRoot.name);
            var parent = RootFolder + "/" + sourceName;
            var folder = parent + "/" + baseName;
            for (var i = 2; AssetDatabase.IsValidFolder(folder) || Directory.Exists(folder); i++)
                folder = parent + "/" + baseName + " (" + i + ")";
            return folder;
        }

        static string SourcePrefabPath(GameObject avatarRoot)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(avatarRoot);
            return source == null ? "" : AssetDatabase.GetAssetPath(source);
        }

        static string SafeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = (string.IsNullOrEmpty(name) ? "avatar" : name)
                .Select(c => invalid.Contains(c) ? '_' : c)
                .ToArray();
            var result = new string(chars).Trim();
            return result.Length == 0 ? "avatar" : result;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && parent != "Assets")
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>NDMF のコンテナに固まった生成物を、型ごとのサブフォルダへ個別アセットとして書き出す。</summary>
        static void Extract(GeneratedAssets bundle, string folder, Dictionary<string, List<AnimationClip>> originalClips)
        {
            var all = new List<Object>(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(bundle)));
            foreach (var sub in bundle.SubAssets)
                if (sub != null)
                    all.AddRange(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(sub)));
            all = all.Where(o => o != null && o != bundle && !(o is SubAssetContainer)).Distinct().ToList();

            ReuseUnchangedClips(all, originalClips);

            var owner = new Dictionary<Object, AnimatorController>();
            foreach (var controller in all.OfType<AnimatorController>())
                CollectInternals(controller, controller, all, owner);

            EnsureFolder(folder);
            var used = new HashSet<string>();
            // 参照される側 (テクスチャ・メッシュ・クリップ) を先に置く。CreateAsset はその時点の参照先で書き出すので、
            // 参照する側を先に書くと消える予定のコンテナを指したまま残る。
            var roots = all.Where(o => !owner.ContainsKey(o)).OrderBy(SaveOrder).ToList();
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var asset in roots)
                {
                    AssetDatabase.RemoveObjectFromAsset(asset);
                    asset.hideFlags = HideFlags.None;
                    AssetDatabase.CreateAsset(asset, UniquePath(folder, asset, used));
                }
                foreach (var pair in owner)
                {
                    AssetDatabase.RemoveObjectFromAsset(pair.Key);
                    pair.Key.hideFlags |= HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(pair.Key, pair.Value);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            // 移動で参照先の場所が変わったので、全部を書き直す。
            foreach (var asset in all)
                EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        /// <summary>元アバターの全コントローラーが参照している (ディスク上の) クリップを名前で引けるようにする。</summary>
        static Dictionary<string, List<AnimationClip>> CollectOriginalClips(GameObject avatarRoot)
        {
            var result = new Dictionary<string, List<AnimationClip>>();
            var controllers = new List<RuntimeAnimatorController>();
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null)
            {
                controllers.AddRange(descriptor.baseAnimationLayers.Select(l => l.animatorController));
                controllers.AddRange(descriptor.specialAnimationLayers.Select(l => l.animatorController));
            }
            foreach (var merge in avatarRoot.GetComponentsInChildren<nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator>(true))
                controllers.Add(merge.animator);
            foreach (var controller in controllers.OfType<AnimatorController>().Distinct())
            foreach (var clip in ReFrameBakedVisibilityResolver.CollectClips(controller))
            {
                if (clip == null || !EditorUtility.IsPersistent(clip))
                    continue;
                if (!result.TryGetValue(clip.name, out var list))
                    result[clip.name] = list = new List<AnimationClip>();
                if (!list.Contains(clip))
                    list.Add(clip);
            }
            return result;
        }

        /// <summary>複製されただけで中身が元と同じクリップは元のアセットへ参照を戻し、複製は書き出さない (名前や参照で探す他ツールのため、置き場も膨らませないため)。</summary>
        static void ReuseUnchangedClips(List<Object> all, Dictionary<string, List<AnimationClip>> originalClips)
        {
            var replace = new Dictionary<Object, Object>();
            foreach (var clip in all.OfType<AnimationClip>())
            {
                if (!originalClips.TryGetValue(clip.name, out var candidates))
                    continue;
                var same = candidates.FirstOrDefault(c => SameClip(c, clip));
                if (same != null)
                    replace[clip] = same;
            }
            if (replace.Count == 0)
                return;

            foreach (var node in all)
            {
                if (replace.ContainsKey(node))
                    continue;
                var so = new SerializedObject(node);
                var prop = so.GetIterator();
                var changed = false;
                var enterChildren = true;
                while (prop.Next(enterChildren))
                {
                    enterChildren = prop.propertyType != SerializedPropertyType.String;
                    if (prop.propertyType != SerializedPropertyType.ObjectReference || prop.objectReferenceValue == null)
                        continue;
                    if (replace.TryGetValue(prop.objectReferenceValue, out var original))
                    {
                        prop.objectReferenceValue = original;
                        changed = true;
                    }
                }
                if (changed)
                    so.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var clip in replace.Keys.ToList())
            {
                all.Remove(clip);
                AssetDatabase.RemoveObjectFromAsset(clip);
                Object.DestroyImmediate(clip, true);
            }
            Debug.Log("[ReFrameCore] ReFrameBake: 変更の無いクリップ " + replace.Count + " 件は元のアセットをそのまま使います。");
        }

        static bool SameClip(AnimationClip a, AnimationClip b)
        {
            if (a.frameRate != b.frameRate || a.wrapMode != b.wrapMode || a.legacy != b.legacy)
                return false;
            var sa = AnimationUtility.GetAnimationClipSettings(a);
            var sb = AnimationUtility.GetAnimationClipSettings(b);
            if (sa.loopTime != sb.loopTime || sa.loopBlend != sb.loopBlend || sa.startTime != sb.startTime || sa.stopTime != sb.stopTime
                || sa.cycleOffset != sb.cycleOffset || sa.mirror != sb.mirror || sa.keepOriginalOrientation != sb.keepOriginalOrientation
                || sa.keepOriginalPositionY != sb.keepOriginalPositionY || sa.keepOriginalPositionXZ != sb.keepOriginalPositionXZ
                || sa.heightFromFeet != sb.heightFromFeet || sa.loopBlendOrientation != sb.loopBlendOrientation
                || sa.loopBlendPositionY != sb.loopBlendPositionY || sa.loopBlendPositionXZ != sb.loopBlendPositionXZ)
                return false;

            var ba = AnimationUtility.GetCurveBindings(a);
            var bb = AnimationUtility.GetCurveBindings(b);
            if (ba.Length != bb.Length)
                return false;
            var byKey = bb.ToDictionary(x => (x.path, x.propertyName, x.type));
            foreach (var binding in ba)
            {
                if (!byKey.TryGetValue((binding.path, binding.propertyName, binding.type), out var other))
                    return false;
                if (!SameCurve(AnimationUtility.GetEditorCurve(a, binding), AnimationUtility.GetEditorCurve(b, other)))
                    return false;
            }

            var oa = AnimationUtility.GetObjectReferenceCurveBindings(a);
            var ob = AnimationUtility.GetObjectReferenceCurveBindings(b);
            if (oa.Length != ob.Length)
                return false;
            var objByKey = ob.ToDictionary(x => (x.path, x.propertyName, x.type));
            foreach (var binding in oa)
            {
                if (!objByKey.TryGetValue((binding.path, binding.propertyName, binding.type), out var other))
                    return false;
                var ka = AnimationUtility.GetObjectReferenceCurve(a, binding);
                var kb = AnimationUtility.GetObjectReferenceCurve(b, other);
                if (ka.Length != kb.Length)
                    return false;
                for (var i = 0; i < ka.Length; i++)
                    if (ka[i].time != kb[i].time || ka[i].value != kb[i].value)
                        return false;
            }
            return true;
        }

        static bool SameCurve(AnimationCurve a, AnimationCurve b)
        {
            if (a == null || b == null)
                return a == b;
            if (a.length != b.length || a.preWrapMode != b.preWrapMode || a.postWrapMode != b.postWrapMode)
                return false;
            for (var i = 0; i < a.length; i++)
            {
                var ka = a[i];
                var kb = b[i];
                if (ka.time != kb.time || ka.value != kb.value || ka.inTangent != kb.inTangent || ka.outTangent != kb.outTangent
                    || ka.inWeight != kb.inWeight || ka.outWeight != kb.outWeight || ka.weightedMode != kb.weightedMode)
                    return false;
            }
            return true;
        }

        static void CollectInternals(
            Object node,
            AnimatorController controller,
            List<Object> all,
            Dictionary<Object, AnimatorController> owner
        )
        {
            var so = new SerializedObject(node);
            var prop = so.GetIterator();
            var enterChildren = true;
            while (prop.Next(enterChildren))
            {
                enterChildren = prop.propertyType != SerializedPropertyType.String;
                if (prop.propertyType != SerializedPropertyType.ObjectReference)
                    continue;
                var value = prop.objectReferenceValue;
                if (value == null || owner.ContainsKey(value) || !all.Contains(value))
                    continue;
                if (!ControllerInternals.Any(t => t.IsInstanceOfType(value)))
                    continue;
                owner[value] = controller;
                CollectInternals(value, controller, all, owner);
            }
        }

        static int SaveOrder(Object asset)
        {
            switch (asset)
            {
                case Texture _: return 0;
                case Mesh _: return 0;
                case AvatarMask _: return 0;
                case AnimationClip _: return 1;
                case Material _: return 2;
                case VRCExpressionsMenu _: return 3;
                case VRCExpressionParameters _: return 3;
                case AnimatorController _: return 4;
                default: return 5;
            }
        }

        static string UniquePath(string folder, Object asset, HashSet<string> used)
        {
            string sub, ext;
            switch (asset)
            {
                case AnimatorController _: sub = "Animators"; ext = ".controller"; break;
                case AnimationClip _: sub = "Clips"; ext = ".anim"; break;
                case VRCExpressionsMenu _: sub = "Menus"; ext = ".asset"; break;
                case VRCExpressionParameters _: sub = "Parameters"; ext = ".asset"; break;
                case Material _: sub = "Materials"; ext = ".mat"; break;
                case Texture _: sub = "Textures"; ext = ".asset"; break;
                case Mesh _: sub = "Meshes"; ext = ".asset"; break;
                case AvatarMask _: sub = "Masks"; ext = ".mask"; break;
                default: sub = "Misc"; ext = ".asset"; break;
            }
            // 同名は別フォルダへ逃がす。ファイル名を変えるとアセット名まで変わり、名前で探すツールが見失う。
            var dir = folder + "/" + sub;
            var name = SafeName(string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name);
            var path = dir + "/" + name + ext;
            for (var i = 2; used.Contains(path) || File.Exists(path); i++)
            {
                dir = folder + "/" + sub + "/(" + i + ")";
                path = dir + "/" + name + ext;
            }
            EnsureFolder(dir);
            used.Add(path);
            return path;
        }
    }
}
