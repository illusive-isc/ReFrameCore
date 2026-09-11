using System.Collections.Generic;
using System.Reflection;
using nadena.dev.modular_avatar.core;
using UnityEngine;
using VRC.SDKBase;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteAttribute を付けたフィールドを持つ ReFrame コンポーネントの共通基底。</summary>
    public abstract class ReFrameDeleteComponent : MonoBehaviour, IEditorOnly
    {
        const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Editor 専用のプレビュー設定。</summary>
        [Tooltip(
            "ON の間、削除に指定した項目で消えるオブジェクトを Hierarchy と Scene ビューでも"
                + "非表示にする (Scene Visibility の閉じた目アイコン)。見た目確認用でビルドには影響しない。"
                + "既定 ON。"
        )]
        public bool previewHiddenInHierarchy = true;

        /// <summary>削除したギミックの実体 (GameObject とボーン) を ReFrame 自身が片付けるか、 AvatarOptimizer など他のツールに任せるか。</summary>
        [Tooltip(
            "削除したギミックの実体 (GameObject / ボーン) の片付け方。"
                + "Sweep = ReFrame が自分で破棄する (既定)。"
                + "LeaveToAvatarOptimizer = 非アクティブにするだけで残し、AvatarOptimizer 等に任せる"
        )]
        public ReFrameSweepMode sweepMode = ReFrameSweepMode.Sweep;

        /// <summary>Inspector 上の名前は「Quest 簡易対応」。</summary>
        [Tooltip(
            "Quest (Android) で通る形へ機械的に変換する。見た目の再現は捨てて、"
                + "通ることを優先する割り切った対応。\n"
                + "・動かないコンポーネント (Camera / Light / Cloth など) を取り除く\n"
                + "・剥がすと壊れるギミックは、まるごと削除する\n"
                + "・シェーダーを Toon Lit へ差し替える (カットアウト・半透明・"
                + "アウトラインは失われる)\n"
                + "見た目を作り込みたい場合は、ここに頼らず手で調整すること"
        )]
        public bool deleteQuestUnsupportedComponents;

        /// <summary>上の「Quest 対応」をどのプラットフォームで効かせるか。</summary>
        [Tooltip(
            "「Quest 対応」を効かせるプラットフォーム。\n"
                + "・常に: 従来どおり、PC ビルドでも Quest 化される\n"
                + "・Android のときだけ: PC に切り替えている間はチェックが入っていても何も起きない"
        )]
        public ReFrameQuestScope questScope = ReFrameQuestScope.Always;

        /// <summary>いまフィールドに載っているのはどちら向けの設定か。</summary>
        [HideInInspector]
        public ReFrameBuildPlatform activeSlot = ReFrameBuildPlatform.PC;

        /// <summary>PC 用の設定。</summary>
        [HideInInspector]
        public ReFramePlatformSlot pcSlot = new();

        /// <summary>Quest 用の差分。</summary>
        [HideInInspector]
        public ReFrameQuestDiff questDiff = new();

        /// <summary>いまフィールドに載っている設定を PC 用として写し取る。</summary>
        public void CaptureBase()
        {
            pcSlot.Entries.Clear();
            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                pcSlot.Entries.Add(
                    new ReFrameEntryState
                    {
                        Field = field.Name,
                        Enabled = entry.Enabled,
                        Value = entry.Value,
                    }
                );
            }
            pcSlot.PhysBones = new List<string>(deletedPhysBones);
            pcSlot.PhysBoneColliders = new List<string>(deletedPhysBoneColliders);
            pcSlot.CutCoveredMesh = cutCoveredMesh;
            pcSlot.Captured = true;
        }

        /// <summary>PC 用の設定をフィールドへ戻す。</summary>
        public void ApplyBase()
        {
            if (!pcSlot.Captured)
                return;
            var byName = new Dictionary<string, ReFrameEntryState>();
            foreach (var state in pcSlot.Entries)
                if (!string.IsNullOrEmpty(state.Field))
                    byName[state.Field] = state;

            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                if (!byName.TryGetValue(field.Name, out var state))
                    continue;
                field.SetValue(
                    this,
                    new ReFrameDeleteEntry { Enabled = state.Enabled, Value = state.Value }
                );
            }
            deletedPhysBones = new List<string>(pcSlot.PhysBones);
            deletedPhysBoneColliders = new List<string>(pcSlot.PhysBoneColliders);
            cutCoveredMesh = pcSlot.CutCoveredMesh;
        }

        /// <summary>いまフィールドに載っている PC 用の設定に、Quest の差分を重ねる。</summary>
        public void ApplyQuestDiff()
        {
            if (!questDiff.Captured)
                return;
            var byName = new Dictionary<string, ReFrameEntryState>();
            foreach (var state in questDiff.Entries)
                if (!string.IsNullOrEmpty(state.Field))
                    byName[state.Field] = state;

            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                if (!byName.TryGetValue(field.Name, out var state))
                    continue;
                field.SetValue(
                    this,
                    new ReFrameDeleteEntry { Enabled = state.Enabled, Value = state.Value }
                );
            }

            foreach (var key in questDiff.KeptPhysBones)
                deletedPhysBones.Remove(key);
            foreach (var key in questDiff.AddedPhysBones)
                if (!deletedPhysBones.Contains(key))
                    deletedPhysBones.Add(key);

            foreach (var key in questDiff.KeptPhysBoneColliders)
                deletedPhysBoneColliders.Remove(key);
            foreach (var key in questDiff.AddedPhysBoneColliders)
                if (!deletedPhysBoneColliders.Contains(key))
                    deletedPhysBoneColliders.Add(key);

            if (questDiff.CutCoveredMeshOverridden)
                cutCoveredMesh = questDiff.CutCoveredMesh;
        }

        /// <summary>いまフィールドに載っている Quest 用の設定を、PC との差分として取り出す。</summary>
        public void CaptureQuestDiff()
        {
            if (!pcSlot.Captured)
                return;

            var baseByName = new Dictionary<string, ReFrameEntryState>();
            foreach (var state in pcSlot.Entries)
                if (!string.IsNullOrEmpty(state.Field))
                    baseByName[state.Field] = state;

            questDiff.Entries.Clear();
            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                var entry = (ReFrameDeleteEntry)field.GetValue(this);

                if (!baseByName.TryGetValue(field.Name, out var basis))
                    continue;
                if (basis.Enabled == entry.Enabled && Mathf.Approximately(basis.Value, entry.Value))
                    continue;
                questDiff.Entries.Add(
                    new ReFrameEntryState
                    {
                        Field = field.Name,
                        Enabled = entry.Enabled,
                        Value = entry.Value,
                    }
                );
            }

            DiffLists(
                pcSlot.PhysBones,
                deletedPhysBones,
                questDiff.AddedPhysBones,
                questDiff.KeptPhysBones
            );
            DiffLists(
                pcSlot.PhysBoneColliders,
                deletedPhysBoneColliders,
                questDiff.AddedPhysBoneColliders,
                questDiff.KeptPhysBoneColliders
            );

            questDiff.CutCoveredMeshOverridden = cutCoveredMesh != pcSlot.CutCoveredMesh;
            questDiff.CutCoveredMesh = cutCoveredMesh;
            questDiff.Captured = true;
        }

        /// <summary>2 つの一覧の差を「追加された分」と「外された分」に分ける。</summary>
        static void DiffLists(
            List<string> basis,
            List<string> current,
            List<string> added,
            List<string> removed
        )
        {
            added.Clear();
            removed.Clear();
            foreach (var key in current)
                if (!basis.Contains(key))
                    added.Add(key);
            foreach (var key in basis)
                if (!current.Contains(key))
                    removed.Add(key);
        }

        /// <summary>いま実際に Quest 化が効いているか。</summary>
        public bool QuestConversionActive =>
            (IsQuestVariant || deleteQuestUnsupportedComponents)
            && (ScopeAppliesNow(questScope) || PreviewForcesQuest)
            && IsActiveForCurrentTarget;

        /// <summary>プレビューの手動指定で「Quest 簡易対応版を見せる」になっているか。</summary>
        bool PreviewForcesQuest =>
            IsQuestVariant
            && PreviewQuestOverride.TryGetValue(AvatarRootOf(this).gameObject.GetInstanceID(), out var quest)
            && quest;

        /// <summary>このコンポーネントは「Quest 簡易対応版」か ([ReFrameQuestVariant] が付いた型)。</summary>
        public bool IsQuestVariant =>
            System.Attribute.IsDefined(GetType(), typeof(ReFrameQuestVariantAttribute), true);

        /// <summary>いまのビルドターゲットで、このコンポーネントが使われる側か。</summary>
        public bool IsActiveForCurrentTarget
        {
            get
            {
                var root = AvatarRootOf(this);
                var all = root.GetComponentsInChildren<ReFrameDeleteComponent>(true);
                var questNow = false;
                var anyVariant = false;
                foreach (var component in all)
                {
                    if (component == null || !component.IsQuestVariant)
                        continue;
                    anyVariant = true;
                    if (ScopeAppliesNow(component.questScope))
                        questNow = true;
                }
                if (!anyVariant)
                    return !IsQuestVariant;

                if (PreviewQuestOverride.TryGetValue(root.gameObject.GetInstanceID(), out var forcedQuest))
                    questNow = forcedQuest;
                return questNow ? IsQuestVariant : !IsQuestVariant;
            }
        }

        /// <summary>プレビューでどちら側 (PC 用 / Quest 簡易対応版) を見せるかの手動指定。</summary>
        public static readonly Dictionary<int, bool> PreviewQuestOverride = new Dictionary<int, bool>();

        /// <summary>アバターのルート (VRCAvatarDescriptor)。</summary>
        static Transform AvatarRootOf(Component component)
        {
            var descriptor = component.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true);
            return descriptor != null ? descriptor.transform : component.transform.root;
        }

        /// <summary>root 以下の ReFrame コンポーネントのうち、いまのビルドターゲットで 使われる側だけ。</summary>
        public static ReFrameDeleteComponent[] ActiveIn(Component root) =>
            root == null ? new ReFrameDeleteComponent[0] : FilterActive(root.GetComponentsInChildren<ReFrameDeleteComponent>(true));

        public static ReFrameDeleteComponent[] ActiveIn(GameObject root) =>
            root == null ? new ReFrameDeleteComponent[0] : ActiveIn(root.transform);

        /// <summary>列挙済みのコンポーネントから、いまのターゲットで使われる側だけを残す。</summary>
        public static ReFrameDeleteComponent[] FilterActive(IEnumerable<ReFrameDeleteComponent> components)
        {
            var result = new List<ReFrameDeleteComponent>();
            if (components == null)
                return result.ToArray();
            foreach (var component in components)
                if (component != null && component.IsActiveForCurrentTarget)
                    result.Add(component);
            return result.ToArray();
        }

        /// <summary>その ReFrameQuestScope がいまのビルドターゲットで効くか。</summary>
        public static bool ScopeAppliesNow(ReFrameQuestScope scope)
        {
            if (scope != ReFrameQuestScope.AndroidOnly)
                return true;
#if UNITY_EDITOR

            return UnityEditor.EditorUserBuildSettings.activeBuildTarget
                == UnityEditor.BuildTarget.Android;
#else

            return Application.platform == RuntimePlatform.Android;
#endif
        }

        /// <summary>服に覆われて見えない体のポリゴンを切り取るか (ReFrameCutCoveredAttribute を宣言しているアバターだけ)。</summary>
        [Tooltip("服に覆われて見えない体のポリゴンをビルド時に切り取る (脱がせた服の分は残る)")]
        public bool cutCoveredMesh;

        /// <summary>メニューのアイコンの扱い。</summary>
        [Tooltip("メニューのアイコンを縮める / 消す (テクスチャ容量対策)")]
        public ReFrameMenuIconMode menuIconMode = ReFrameMenuIconMode.Keep;

        /// <summary>Toon Lit へ焼くときの明度。</summary>
        [Tooltip("アバター側で決められた焼き込みの調整を上書きする")]
        public bool questBakeOverride;

        /// <summary>Quest 用に焼いたマテリアルの置き場 (ReFrameQuestBakeSet)。</summary>
        [Tooltip("焼き済みの Quest 用マテリアル。あればビルドもプレビューもこれを使う")]
        public ReFrameQuestBakeSet questBakeSet;

        [Range(0f, 1f)]
        [Tooltip("Toon Lit へ焼くときに掛ける明度 (1 で素のまま)")]
        public float questTextureBrightness = 0.83f;

        /// <summary>半透明で下を透かしていたものの不透明度 (ReFrameQuestBlendBackdropAttribute を宣言しているアバターだけ)。</summary>
        [Range(-1f, 1f)]
        [Tooltip("半透明だったものの濃さ (1 で元のまま濃く、0 で下地の色。負なら宣言どおり)")]
        public float questBackdropOpacity = -1f;

        /// <summary>法線マップから陰影を作ってメインテクスチャへ焼き込むか。</summary>
        [Tooltip("捨てられる法線マップから陰影を作って焼き込む")]
        public bool questShadowFromNormalMap = true;

        /// <summary>Quest 用に焼くテクスチャの一辺の上限。</summary>
        [Tooltip(
            "Quest 用に焼くテクスチャの一辺の上限。Quest のアバターは圧縮後 10MB までなので、"
                + "2048 のままだと数枚で埋まる"
        )]
        public int questMaxTextureSize = 1024;

        /// <summary>0 以下のテクスチャ上限を残さない。</summary>
        void OnValidate()
        {
            if (questMaxTextureSize <= 0)
                questMaxTextureSize = 1024;
            questTextureBrightness = Mathf.Clamp01(questTextureBrightness);
        }

        /// <summary>削除する VRCPhysBone の、アバタールートからの相対パス。</summary>
        [SerializeField]
        public List<string> deletedPhysBones = new();

        /// <summary>削除する VRCPhysBoneCollider の、アバタールートからの相対パス ("path#index")。</summary>
        [SerializeField]
        public List<string> deletedPhysBoneColliders = new();

        /// <summary>[ReFrameQuestTransparent] の枠ごとに Inspector で選んだ扱い (未選択なら宣言の既定)。</summary>
        [SerializeField]
        public List<ReFrameQuestTransparentChoice> questTransparentChoices = new();

        /// <summary>この枠に対する Inspector での選択。無ければ null。</summary>
        public ReFrameQuestTransparentMode? QuestTransparentChoice(string path, int slot)
        {
            foreach (var choice in questTransparentChoices)
                if (choice.Path == path && choice.Slot == slot && System.Enum.IsDefined(typeof(ReFrameQuestTransparentMode), choice.Mode))
                    return choice.Mode;
            return null;
        }

        /// <summary>この枠の扱いを選ぶ (色の選択は保つ)。</summary>
        public void SetQuestTransparentChoice(string path, int slot, ReFrameQuestTransparentMode mode)
        {
            var choice = FindOrAddChoice(path, slot, out var index);
            choice.Mode = mode;
            questTransparentChoices[index] = choice;
        }

        /// <summary>この枠の「何も写らない所の色」を選ぶ (null で宣言の既定に戻す)。</summary>
        public void SetQuestTransparentBeyond(string path, int slot, Color? beyond)
        {
            var choice = FindOrAddChoice(path, slot, out var index);
            choice.UseBeyond = beyond.HasValue;
            choice.Beyond = beyond ?? default;
            questTransparentChoices[index] = choice;
        }

        /// <summary>この枠に選んだ色。無ければ null。</summary>
        public Color? QuestTransparentBeyond(string path, int slot)
        {
            foreach (var choice in questTransparentChoices)
                if (choice.Path == path && choice.Slot == slot && choice.UseBeyond)
                    return choice.Beyond;
            return null;
        }

        ReFrameQuestTransparentChoice FindOrAddChoice(string path, int slot, out int index)
        {
            for (index = 0; index < questTransparentChoices.Count; index++)
                if (questTransparentChoices[index].Path == path && questTransparentChoices[index].Slot == slot)
                    return questTransparentChoices[index];
            var created = new ReFrameQuestTransparentChoice { Path = path, Slot = slot, Mode = DeclaredTransparentMode(path, slot) };
            questTransparentChoices.Add(created);
            return created;
        }

        ReFrameQuestTransparentMode DeclaredTransparentMode(string path, int slot)
        {
            foreach (
                var attr in (ReFrameQuestTransparentAttribute[])
                    System.Attribute.GetCustomAttributes(GetType(), typeof(ReFrameQuestTransparentAttribute), true)
            )
                if (attr.Path == path && attr.Slot == slot)
                    return attr.Mode;
            return ReFrameQuestTransparentMode.BakeInside;
        }

        /// <summary>[ReFrameQuestTransparent] の宣言と、選択を反映した扱い。</summary>
        public IEnumerable<(ReFrameQuestTransparentAttribute Declared, ReFrameQuestTransparentMode Mode)> EnumerateQuestTransparentTargets()
        {
            foreach (
                var attr in (ReFrameQuestTransparentAttribute[])
                    System.Attribute.GetCustomAttributes(GetType(), typeof(ReFrameQuestTransparentAttribute), true)
            )
            {
                if (string.IsNullOrEmpty(attr.Path))
                    continue;
                yield return (attr, QuestTransparentChoice(attr.Path, attr.Slot) ?? attr.Mode);
            }
        }

        /// <summary>透過マテリアルの選択 (扱いと色) を 1 つの文字列にまとめる (変更検知用)。</summary>
        public string QuestTransparentChoiceSignature()
        {
            var builder = new System.Text.StringBuilder();
            foreach (var choice in questTransparentChoices)
            {
                builder.Append(choice.Path).Append('#').Append(choice.Slot).Append('=').Append(choice.Mode);
                if (choice.UseBeyond)
                    builder.Append('@').Append(ColorUtility.ToHtmlStringRGB(choice.Beyond));
                builder.Append(';');
            }
            return builder.ToString();
        }

        /// <summary>ビルドの最後 (Optimizing フェーズ) に実体を破棄するか。</summary>
        public virtual bool SweepUnusedObjects => sweepMode == ReFrameSweepMode.Sweep;

        /// <summary>Quest (Android) で動作しないコンポーネントの型。</summary>
        static readonly System.Type[] QuestUnsupportedComponentTypes =
        {
            typeof(Cloth),
            typeof(Camera),
            typeof(Light),
            typeof(VRC_SpatialAudioSource),
            typeof(AudioSource),
            typeof(Joint),
            typeof(Rigidbody),
            typeof(Collider),

        };

        /// <summary>型を typeof で書けない (プロジェクトに入っていないかもしれない) Quest 非対応 コンポーネントの完全修飾名。</summary>
        static readonly string[] QuestUnsupportedComponentTypeNames =
        {
            "DynamicBone",
            "DynamicBoneColliderBase",
        };

        /// <summary>FinalIK のコンポーネントはこの名前空間にまとまっている。</summary>
        const string FinalIKNamespacePrefix = "RootMotion.FinalIK.";

        /// <summary>Unity 標準の Constraint (IConstraint) が、 ビルド時に VRChat Constraints へ変換されるか。</summary>
        static bool IsUnityConstraintConverted(Component component)
        {
            var converter = component.GetComponentInParent<ModularAvatarConvertConstraints>(true);
            if (converter == null)
                return false;

            return converter.GetComponentInParent<VRC_AvatarDescriptor>(true)
                == component.GetComponentInParent<VRC_AvatarDescriptor>(true);
        }

        /// <summary>ReFrame の Scene view プレビューが一時的に作るボーンの名前の頭。</summary>
        public const string PreviewShadowPrefix = "ReFrame Shadow Bone";

        /// <summary>プレビューが作った影武者か (自分か祖先のどれかがそれなら true)。</summary>
        public static bool IsPreviewShadow(Transform target)
        {
            for (var cursor = target; cursor != null; cursor = cursor.parent)
                if (cursor.name.StartsWith(PreviewShadowPrefix, System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>アバター上の全 ReFrameDeleteComponent の宣言をまとめて、 ビルドで確実に破棄される Transform を集める (部分木ごと)。</summary>
        public static HashSet<Transform> CollectDeclaredDeletions(Transform avatarRoot)
        {
            var doomed = new HashSet<Transform>();
            if (avatarRoot == null)
                return doomed;

            foreach (var component in ActiveIn(avatarRoot))
            {
                if (component == null)
                    continue;
                foreach (var path in component.EnumerateDeleteObjectPaths())
                {
                    var target = avatarRoot.Find(path);
                    if (target == null)
                        continue;
                    foreach (var descendant in target.GetComponentsInChildren<Transform>(true))
                        doomed.Add(descendant);
                }
            }
            return doomed;
        }

        /// <summary>そのコンポーネントは、Quest では動かないがビルド時に解決されるか。</summary>
        public static bool WillBeResolvedAtBuild(Component component, bool questConversionEnabled)
        {
            if (component == null)
                return false;
            if (component is UnityEngine.Animations.IConstraint)
                return IsUnityConstraintConverted(component);
            return questConversionEnabled && IsQuestUnsupportedComponent(component);
        }

        /// <summary>その型が Quest 非対応か。</summary>
        public static bool IsQuestUnsupportedComponent(Component component)
        {
            if (component == null)
                return false;

            if (component is UnityEngine.Animations.IConstraint)
                return !IsUnityConstraintConverted(component);

            var type = component.GetType();
            foreach (var unsupported in QuestUnsupportedComponentTypes)
            {
                if (unsupported.IsAssignableFrom(type))
                    return true;
            }

            for (var t = type; t != null; t = t.BaseType)
            {
                var fullName = t.FullName;
                if (string.IsNullOrEmpty(fullName))
                    continue;
                if (fullName.StartsWith(FinalIKNamespacePrefix, System.StringComparison.Ordinal))
                    return true;
                foreach (var unsupported in QuestUnsupportedComponentTypeNames)
                {
                    if (fullName == unsupported)
                        return true;
                }
            }
            return false;
        }

        /// <summary>deleteQuestUnsupportedComponents が ON のとき、そのフィールドが [ReFrameDeleteObject] で名指ししている実体の中に Quest 非対応コンポーネントが 1 つでもあれば true。</summary>
        bool IsQuestForcedDeleting(FieldInfo field) =>
            QuestConversionActive && HasQuestUnsupportedTarget(field);

        /// <summary>そのフィールドが [ReFrameDeleteObject] で名指ししている実体の中に Quest 非対応 コンポーネントがあるか。</summary>
        public bool HasQuestUnsupportedTarget(FieldInfo field)
        {

            if (field.GetCustomAttribute<ReFrameQuestForceDeleteAttribute>(true) != null)
                return true;

            var avatarRoot = GetComponentInParent<VRC_AvatarDescriptor>();
            if (avatarRoot == null)
                return false;

            foreach (var attr in field.GetCustomAttributes<ReFrameDeleteObjectAttribute>(true))
            {
                if (string.IsNullOrEmpty(attr.Path))
                    continue;
                var target = avatarRoot.transform.Find(attr.Path);
                if (target == null)
                    continue;
                foreach (var component in target.GetComponentsInChildren<Component>(true))
                {
                    if (IsQuestUnsupportedComponent(component))
                        return true;
                }
            }
            return false;
        }

        /// <summary>フィールドが [ReFrameMenuOnly] を持つかどうか (1回引ければ十分なのでキャッシュせず 都度引く軽量ヘルパー)。</summary>
        static ReFrameMenuOnlyAttribute GetMenuOnlyOverride(FieldInfo field) =>
            field.GetCustomAttribute<ReFrameMenuOnlyAttribute>(true);

        /// <summary>フィールドが [ReFrameValueLocked] を持つかどうか。</summary>
        static ReFrameValueLockedAttribute GetValueLockedOverride(FieldInfo field) =>
            field.GetCustomAttribute<ReFrameValueLockedAttribute>(true);

        /// <summary>ビルド時に実際に使われる Value。</summary>
        float ResolveEffectiveValue(FieldInfo field, ReFrameDeleteEntry entry)
        {

            if (IsQuestForcedDeleting(field))
                return OffValueOf(field);

            var menuOnlyOverride = GetMenuOnlyOverride(field);
            if (menuOnlyOverride != null)
                return menuOnlyOverride.FixedValue;
            var valueLockedOverride = GetValueLockedOverride(field);
            if (valueLockedOverride != null)
                return valueLockedOverride.FixedValue;
            return entry.Value;
        }

        /// <summary>そのフィールドにとって「ギミックが OFF に見える」生の値。</summary>
        static float OffValueOf(FieldInfo field) =>
            field.GetCustomAttribute<ReFrameReverseAttribute>(true) != null ? 1f : 0f;

        /// <summary>そのフィールドが削除対象か。</summary>
        bool IsDeleting(
            FieldInfo field,
            ReFrameDeleteEntry entry,
            HashSet<string> deletingRepresentativeParams
        ) =>
            entry.Enabled
            || IsCascadedFromBundleRepresentative(field, deletingRepresentativeParams)
            || IsQuestForcedDeleting(field)
            || IsLinkedPartnerDeleting(field);

        /// <summary>[ReFrameLinkedWith] で結んだ相手の行が (自身の Enabled か Quest 強制で) 削除中なら true。</summary>
        bool IsLinkedPartnerDeleting(FieldInfo field)
        {
            foreach (var link in field.GetCustomAttributes<ReFrameLinkedWithAttribute>(true))
            {
                var partner = FindFieldByParameterName(link.ParameterName);
                if (partner == null || partner == field)
                    continue;
                var partnerEntry = (ReFrameDeleteEntry)partner.GetValue(this);
                if (partnerEntry.Enabled || IsQuestForcedDeleting(partner))
                    return true;
            }
            return false;
        }

        /// <summary>[ReFrameDelete(parameterName)] を持つ ReFrameDeleteEntry 型フィールドを返す。</summary>
        public FieldInfo FindFieldByParameterName(string parameterName)
        {
            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                foreach (var attr in field.GetCustomAttributes<ReFrameDeleteAttribute>(true))
                    if (attr.ParameterName == parameterName)
                        return field;
            }
            return null;
        }

        /// <summary>[ReFrameBundleMember(repName)] を持つフィールドを、Inspector 上の見た目だけでなく ビルド時の削除処理自体でも代表と道連れにするための判定材料を集める。</summary>
        HashSet<string> CollectDeletingRepresentativeParameterNames()
        {
            var result = new HashSet<string>();
            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;

                var entry = (ReFrameDeleteEntry)field.GetValue(this);

                if (!entry.Enabled && !IsQuestForcedDeleting(field))
                    continue;

                if (!Mathf.Approximately(ResolveEffectiveValue(field, entry), OffValueOf(field)))
                    continue;

                foreach (var attr in field.GetCustomAttributes<ReFrameDeleteAttribute>(true))
                    result.Add(attr.ParameterName);
            }
            return result;
        }

        /// <summary>field が [ReFrameBundleMember] を持ち、その代表がすでに (実際にこのギミックごと消える 設定で) 削除中であれば true。</summary>
        static bool IsCascadedFromBundleRepresentative(
            FieldInfo field,
            HashSet<string> deletingRepresentativeParams
        )
        {
            var bundleMemberAttr = field.GetCustomAttribute<ReFrameBundleMemberAttribute>(true);
            return bundleMemberAttr != null
                && deletingRepresentativeParams.Contains(bundleMemberAttr.RepresentativeParameterName);
        }

        /// <summary>このコンポーネントが持つ ReFrameDeleteEntry 型フィールドのうち ReFrameDeleteAttribute が付き、Enabled が true (または [ReFrameBundleMember] の代表がすでに削除中で道連れになる) のものを (パラメーター名, 固定値, MenuOnlyか) の組として列挙する。</summary>
        public IEnumerable<(string ParameterName, float Value, bool MenuOnly)> EnumerateDeleteTargets()
        {
            var deletingRepresentativeParams = CollectDeletingRepresentativeParameterNames();

            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;

                var menuOnlyOverride = GetMenuOnlyOverride(field);
                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                if (!IsDeleting(field, entry, deletingRepresentativeParams))
                    continue;

                foreach (var attr in field.GetCustomAttributes<ReFrameDeleteAttribute>(true))
                    yield return (attr.ParameterName, ResolveEffectiveValue(field, entry), menuOnlyOverride != null);
            }
        }

        /// <summary>このコンポーネントが持つ ReFrameDeleteEntry 型フィールドのうち Enabled が true のものについて、[ReFrameDeleteRelatedBlendTree] で宣言された (パラメーターとは無関係な、名前ベースで削除したい) BlendTree の Name を列挙する。</summary>
        public IEnumerable<(string Name, bool Bake)> EnumerateRelatedBlendTreeTargets()
        {
            var deletingRepresentativeParams = CollectDeletingRepresentativeParameterNames();

            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;

                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                var deleting = IsDeleting(field, entry, deletingRepresentativeParams);

                foreach (var attr in field.GetCustomAttributes<ReFrameDeleteRelatedBlendTreeAttribute>(true))
                {

                    if (!attr.Always && !deleting)
                        continue;
                    if (
                        attr.OnlyWhenValue.HasValue
                        && !Mathf.Approximately(attr.OnlyWhenValue.Value, ResolveEffectiveValue(field, entry))
                    )
                        continue;
                    yield return (attr.Name, attr.Bake);
                }
            }
        }

        /// <summary>[ReFrameDeleteState] で宣言された「削除するときに取り除くステート」を列挙する。</summary>
        public IEnumerable<(string LayerName, string StateName)> EnumerateDeleteStateTargets()
        {
            var deletingRepresentativeParams = CollectDeletingRepresentativeParameterNames();

            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;

                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                if (!IsDeleting(field, entry, deletingRepresentativeParams))
                    continue;

                foreach (var attr in field.GetCustomAttributes<ReFrameDeleteStateAttribute>(true))
                {
                    if (string.IsNullOrEmpty(attr.LayerName) || string.IsNullOrEmpty(attr.StateName))
                        continue;
                    if (
                        attr.OnlyWhenValue.HasValue
                        && !Mathf.Approximately(attr.OnlyWhenValue.Value, ResolveEffectiveValue(field, entry))
                    )
                        continue;
                    yield return (attr.LayerName, attr.StateName);
                }
            }
        }

        /// <summary>[ReFrameDeleteObject] で宣言された「削除するときに破棄する GameObject」のパスを列挙する。</summary>
        public IEnumerable<string> EnumerateDeleteObjectPaths()
        {
            var deletingRepresentativeParams = CollectDeletingRepresentativeParameterNames();

            var deletingParams = new HashSet<string>();
            foreach (var target in EnumerateDeleteTargets())
                deletingParams.Add(target.ParameterName);

            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;

                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                var deleting = IsDeleting(field, entry, deletingRepresentativeParams);

                foreach (var attr in field.GetCustomAttributes<ReFrameDeleteObjectAttribute>(true))
                {

                    if (attr.QuestOnly && !QuestConversionActive)
                        continue;

                    if (!attr.Always && !attr.QuestOnly && !deleting)
                        continue;
                    if (string.IsNullOrEmpty(attr.Path))
                        continue;

                    if (attr.RequiresAll != null)
                    {
                        var satisfied = true;
                        foreach (var required in attr.RequiresAll)
                        {
                            if (string.IsNullOrEmpty(required) || deletingParams.Contains(required))
                                continue;
                            satisfied = false;
                            break;
                        }
                        if (!satisfied)
                            continue;
                    }
                    if (
                        attr.OnlyWhenValue.HasValue
                        && !Mathf.Approximately(attr.OnlyWhenValue.Value, ResolveEffectiveValue(field, entry))
                    )
                        continue;
                    yield return attr.Path;
                }
            }
        }

        /// <summary>path 配下の、マテリアルを持つ ParticleSystemRenderer のパス (アバタールートからの相対) を列挙する。</summary>
        IEnumerable<string> ExpandParticleRenderers(string path)
        {
            var descriptor = GetComponentInParent<VRC_AvatarDescriptor>(true);
            if (descriptor == null)
                yield break;
            var root = descriptor.transform;
            var target = root.Find(path);
            if (target == null)
                yield break;
            foreach (var renderer in target.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (renderer == null || renderer.sharedMaterial == null)
                    continue;
                var segments = new List<string>();
                for (var t = renderer.transform; t != null && t != root; t = t.parent)
                    segments.Insert(0, t.name);
                yield return string.Join("/", segments);
            }
        }

        /// <summary>[ReFrameMenuRemove] で宣言されたメニューの取り除き。</summary>
        public IEnumerable<(string Path, string[] Keep)> EnumerateMenuRemovals()
        {
            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                if (!entry.Enabled)
                    continue;
                foreach (var attr in field.GetCustomAttributes<ReFrameMenuRemoveAttribute>(true))
                    if (!string.IsNullOrEmpty(attr.Path))
                        yield return (attr.Path, attr.Keep ?? new string[0]);
            }
        }

        /// <summary>[ReFrameSetMaxParticles] で宣言された「ParticleSystem の maxParticles を下げる」指定を 列挙する。</summary>
        public IEnumerable<(string ParameterName, float Value)> EnumerateAvatarChangeParameters()
        {
            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                if (field.GetCustomAttribute<ReFrameApplyToAvatarAttribute>(true) == null)
                    continue;
                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                if (!entry.Enabled)
                    continue;
                foreach (var attr in field.GetCustomAttributes<ReFrameDeleteAttribute>(true))
                    if (!string.IsNullOrEmpty(attr.ParameterName))
                        yield return (attr.ParameterName, ResolveEffectiveValue(field, entry));
            }
        }

        /// <summary>[ReFrameApplyToAvatar] が付いた [ReFrameBlendShape] の行の固定先。</summary>
        public IEnumerable<(string Path, string ShapeName, float Weight)> EnumerateAvatarChangeBlendShapes()
        {
            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                if (field.GetCustomAttribute<ReFrameApplyToAvatarAttribute>(true) == null)
                    continue;
                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                if (!entry.Enabled)
                    continue;
                foreach (var attr in field.GetCustomAttributes<ReFrameBlendShapeAttribute>(true))
                    yield return (attr.Path, attr.ShapeName, Mathf.Clamp(entry.Value, 0f, 100f) * attr.Scale);
            }
        }

        /// <summary>[ReFrameCutByBlendShape] で切る (行が固定され、Value が一致するもの)。</summary>
        public IEnumerable<(string Path, string[] Shapes, float Tolerance)> EnumerateCutByBlendShapeTargets()
        {
            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                if (!entry.Enabled)
                    continue;
                foreach (var attr in field.GetCustomAttributes<ReFrameCutByBlendShapeAttribute>(true))
                {
                    if (attr.QuestOnly && !QuestConversionActive)
                        continue;
                    if (Mathf.Abs(entry.Value - attr.OnlyWhenValue) < 0.01f && !string.IsNullOrEmpty(attr.Path))
                        yield return (attr.Path, attr.Shapes, attr.Tolerance);
                }
            }
        }

        /// <summary>[ReFrameBlendShape] で固定する BlendShape。</summary>
        public IEnumerable<(string Path, string ShapeName, float Weight)> EnumerateBlendShapeTargets()
        {
            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                if (!entry.Enabled)
                    continue;
                foreach (var attr in field.GetCustomAttributes<ReFrameBlendShapeAttribute>(true))
                    yield return (attr.Path, attr.ShapeName, Mathf.Clamp(entry.Value, 0f, 100f) * attr.Scale);
            }
        }

        public IEnumerable<(string Path, int MaxParticles)> EnumerateMaxParticleTargets()
        {
            var deletingRepresentativeParams = CollectDeletingRepresentativeParameterNames();

            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;

                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                var deleting = IsDeleting(field, entry, deletingRepresentativeParams);

                foreach (var attr in field.GetCustomAttributes<ReFrameSetMaxParticlesAttribute>(true))
                {
                    if (!attr.Always && !deleting)
                        continue;
                    if (string.IsNullOrEmpty(attr.Path))
                        continue;
                    yield return (attr.Path, attr.MaxParticles);
                }
            }
        }

        /// <summary>[ReFrameQuestParticleMaterial] で宣言された「パーティクルのマテリアルを Quest 対応の シェーダーへ差し替える」指定を列挙する。</summary>
        public IEnumerable<(string Path, ReFrameQuestParticleBlend Blend)> EnumerateQuestParticleMaterialTargets()
        {
            if (!QuestConversionActive)
                yield break;

            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                foreach (
                    var attr in field.GetCustomAttributes<ReFrameQuestParticleMaterialAttribute>(true)
                )
                {
                    if (string.IsNullOrEmpty(attr.Path))
                        continue;
                    if (!attr.Recursive)
                    {
                        yield return (attr.Path, attr.Blend);
                        continue;
                    }

                    foreach (var path in ExpandParticleRenderers(attr.Path))
                        yield return (path, attr.Blend);
                }
            }
        }

        /// <summary>[ReFrameUnsyncParameter] で宣言された「同期を切る」パラメーター名を列挙する。</summary>
        public IEnumerable<string> EnumerateUnsyncParameters()
        {
            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                foreach (var attr in field.GetCustomAttributes<ReFrameUnsyncParameterAttribute>(true))
                {
                    if (!string.IsNullOrEmpty(attr.ParameterName))
                    {
                        yield return attr.ParameterName;
                        continue;
                    }
                    foreach (var delete in field.GetCustomAttributes<ReFrameDeleteAttribute>(true))
                        yield return delete.ParameterName;
                }
            }
        }

        /// <summary>[ReFrameCutUndrivenTransitions] で宣言されたパラメーター名を列挙する。</summary>
        public IEnumerable<(string ParameterName, bool EvaluateAsFixed)> EnumerateCutUndrivenTransitionParameters()
        {
            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                foreach (var attr in field.GetCustomAttributes<ReFrameCutUndrivenTransitionsAttribute>(true))
                    if (!string.IsNullOrEmpty(attr.ParameterName))
                        yield return (attr.ParameterName, attr.EvaluateAsFixed);
            }
        }

        /// <summary>[ReFrameBlendTreeOverride] で宣言された「このツリーの中ではこのパラメーターをこの値とみなす」 指定を列挙する。</summary>
        public IEnumerable<(string TreeName, string ParameterName, float Value, bool Always)> EnumerateBlendTreeOverrides()
        {
            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;
                foreach (var attr in field.GetCustomAttributes<ReFrameBlendTreeOverrideAttribute>(true))
                {
                    if (string.IsNullOrEmpty(attr.TreeName) || string.IsNullOrEmpty(attr.ParameterName))
                        continue;
                    yield return (attr.TreeName, attr.ParameterName, attr.Value, attr.Always);
                }
            }
        }

        /// <summary>Enabled が true のフィールドについて、[ReFrameCutTransitions] で宣言された 「条件を抜くのではなく遷移ごと消す」パラメーター名を列挙する。</summary>
        public IEnumerable<string> EnumerateCutTransitionParameters()
        {
            var deletingRepresentativeParams = CollectDeletingRepresentativeParameterNames();

            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;

                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                if (!IsDeleting(field, entry, deletingRepresentativeParams))
                    continue;

                foreach (var attr in field.GetCustomAttributes<ReFrameCutTransitionsAttribute>(true))
                {
                    if (
                        attr.OnlyWhenValue.HasValue
                        && !Mathf.Approximately(attr.OnlyWhenValue.Value, ResolveEffectiveValue(field, entry))
                    )
                        continue;

                    if (!string.IsNullOrEmpty(attr.ParameterName))
                    {
                        yield return attr.ParameterName;
                        continue;
                    }
                    foreach (var delete in field.GetCustomAttributes<ReFrameDeleteAttribute>(true))
                        yield return delete.ParameterName;
                }
            }
        }

        /// <summary>Enabled が true のフィールドについて、[ReFrameDeleteLayer] で宣言された (丸ごと取り除きたい) AnimatorController レイヤーの名前を列挙する。</summary>
        public IEnumerable<string> EnumerateDeleteLayerTargets()
        {
            var deletingRepresentativeParams = CollectDeletingRepresentativeParameterNames();

            foreach (var field in GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;

                var entry = (ReFrameDeleteEntry)field.GetValue(this);
                if (!IsDeleting(field, entry, deletingRepresentativeParams))
                    continue;

                foreach (var attr in field.GetCustomAttributes<ReFrameDeleteLayerAttribute>(true))
                {
                    if (
                        attr.OnlyWhenValue.HasValue
                        && !Mathf.Approximately(attr.OnlyWhenValue.Value, ResolveEffectiveValue(field, entry))
                    )
                        continue;
                    yield return attr.Name;
                }
            }
        }
    }
}
