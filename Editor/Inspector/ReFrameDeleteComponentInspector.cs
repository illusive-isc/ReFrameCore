using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>ReFrameDeleteComponent (継承先を含む) 共通の Inspector。</summary>
    [CustomEditor(typeof(ReFrameDeleteComponent), true)]
    internal sealed class ReFrameDeleteComponentInspector : UnityEditor.Editor
    {
        const BindingFlags FieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        const string CommonStyleSheetPath =
            "Packages/jp.illusive-isc.reframe-core/Editor/UI/Common.uss";
        const string InspectorStyleSheetPath =
            "Packages/jp.illusive-isc.reframe-core/Editor/UI/Inspector/ReFrameDeleteComponentInspector.uss";

        /// <summary>Value 欄の見せ方。</summary>
        enum ValueKind
        {
            /// <summary>ON / OFF のタイル。</summary>
            OnOff,

            /// <summary>メニュー項目名から選ぶプルダウン。</summary>
            Choice,

            /// <summary>連続値のスライダー。</summary>
            Slider,
        }

        sealed class EntryInfo
        {
            public FieldInfo Field;
            public string Label;
            public ReFrameParameterType Type;

            /// <summary>メニューに出てくる最初の [ReFrameDelete] パラメーターの使われ方 (無ければ null)。</summary>
            public ReFrameMenuGrouping.ParameterUsage Usage;

            /// <summary>Usage に対応するパラメーター名。</summary>
            public string UsageParameterName;

            /// <summary>削除すると浮く VRChat の同期パラメーターのビット数 (全パラメーターの合算)。</summary>
            public int BitCost;

            /// <summary>[ReFrameBlendShape] だけの行。</summary>
            public bool BlendShapeRow;

            /// <summary>[ReFrameReverse] が付いていて「OFF に見える値」が 1 側になっている。</summary>
            public bool Reversed;

            /// <summary>[ReFrameApplyToAvatar]: アバター自体の変更 (体型)。</summary>
            public bool ApplyToAvatar;

            /// <summary>Value 欄の見せ方。</summary>
            public ValueKind Kind;

            /// <summary>[ReFrameMenuOnly] / [ReFrameValueLocked] で Value が固定されている場合のその値。</summary>
            public float? LockedValue;

            /// <summary>[ReFrameBundleMember] の代表パラメーター名 (無ければ null)。</summary>
            public string BundleRepresentative;

            /// <summary>代表に道連れで消えるときも値を選ばせるか (ValueMatters)。</summary>
            public bool BundleValueMatters;

            /// <summary>[ReFrameLinkedWith] で Enabled を揃える相手のパラメーター名。</summary>
            public readonly List<string> LinkedParameters = new();

            /// <summary>[ReFrameDeleteObject] の実体に Quest 非対応コンポーネントが含まれていて、 「Quest 対応」を ON にすると Enabled に関わらず強制削除になる行。</summary>
            public bool QuestForcedCandidate;

            /// <summary>このフィールドが [ReFrameDelete] で指定しているパラメーター名。</summary>
            public readonly List<string> ParameterNames = new();

            public readonly List<(string Name, bool InMenu)> Parameters = new();

            /// <summary>[ReFrameDeleteObject] で名指ししている削除対象のパス。</summary>
            public readonly List<string> DeleteObjectPaths = new();
        }

        sealed class GroupNode
        {
            public string Name;
            public readonly List<GroupNode> Children = new();
            public readonly List<EntryInfo> Entries = new();

            public GroupNode Child(string name)
            {
                foreach (var child in Children)
                {
                    if (child.Name == name)
                        return child;
                }
                var created = new GroupNode { Name = name };
                Children.Add(created);
                return created;
            }

            public int TotalEntries
            {
                get
                {
                    var count = Entries.Count;
                    foreach (var child in Children)
                        count += child.TotalEntries;
                    return count;
                }
            }
        }

        /// <summary>Alt の押下状態に応じて各行の表示名を切り替えるためのハンドラ。</summary>
        readonly List<System.Action<bool>> _labelSwitchers = new();

        /// <summary>詳細モードのとき、行ごとの内訳を出し入れする手続き。</summary>
        readonly List<System.Action<bool>> _detailSwitchers = new();

        /// <summary>「このパラメーターが消えるなら、これも道連れ」の対応表 (ReFrameParameterLink)。</summary>
        Dictionary<string, List<ReFrameParameterLink.Link>> _parameterLinks = new();

        /// <summary>パラメーター名 → 同期ビット。</summary>
        Dictionary<string, int> _parameterBits = new();

        /// <summary>詳細モードの保存先。</summary>
        const string DetailModeKey = "ReFrame.DetailMode";
        bool _altHeld;

        /// <summary>[ReFrameBundleMember] の道連れ表示を更新するためのハンドラ。</summary>
        readonly List<System.Action<HashSet<string>>> _cascadeRefreshers = new();

        /// <summary>「Quest 対応」の ON/OFF に応じて、強制削除になる行の表示を更新するためのハンドラ。</summary>
        readonly List<System.Action<bool>> _questRefreshers = new();

        /// <summary>「Quest 対応」チェックの現在の状態 (行の組み立て時とチェック操作時に更新)。</summary>
        bool _questEnabled;

        /// <summary>グループ見出しの表示名の読み替え表 ([ReFrameGroupLabel])。</summary>
        readonly Dictionary<string, string> _groupLabels = new();

        /// <summary>見出しに出す名前。</summary>
        string GroupLabelOf(string name) =>
            name != null && _groupLabels.TryGetValue(name, out var label) ? label : name;

        /// <summary>各行の「ギミックごと消える設定か」を返す評価器。</summary>
        readonly List<System.Func<IEnumerable<string>>> _representativeProbes = new();

        /// <summary>行を組み立てている最中か。</summary>
        bool _building;

        /// <summary>各行が「いま削除中なら空くビット数」を返す評価器。</summary>
        readonly List<System.Func<int>> _bitProbes = new();

        /// <summary>各行が「いま削除される (Enabled / 道連れ / Quest 強制) なら、そのパラメーター名」を返す 評価器。</summary>
        readonly List<System.Func<IEnumerable<string>>> _deletingProbes = new();

        /// <summary>この ReFrame のどれかの行が [ReFrameDelete] で直接宣言しているパラメーター名。</summary>
        HashSet<string> _declaredParameterNames = new();

        /// <summary>この ReFrame が削除しうるビット数の合計 (削除するかどうかに関わらず、宣言されている全行)。</summary>
        int _deletableTotal;

        /// <summary>上部の集計表示を書き換えるハンドラ。</summary>
        System.Action<int> _refreshSummary;

        /// <summary>Quest の判定 (「アップロードできません」の一覧) の再計算を予約する。</summary>
        System.Action _requestQuestAuditRefresh;

        /// <summary>AvatarOptimizer (AAO) がこのプロジェクトに入っているか。</summary>
        static bool IsAvatarOptimizerInstalled() => TraceAndOptimizeType != null;

        /// <summary>ドロップダウンに出す表示名。</summary>
        static string SweepModeLabel(ReFrameSweepMode mode) =>
            mode == ReFrameSweepMode.Sweep ? "あらかじめ削除する" : "AAO にお任せする";

        /// <summary>焼き済みマテリアルの作成ボタンと状態表示。</summary>
        void AddBakeSetControls(
            VisualElement parent,
            ReFrameDeleteComponent component,
            VRCAvatarDescriptor descriptor,
            System.Action refresh
        )
        {
            var box = new VisualElement();
            box.style.marginTop = 6;

            var hasSet = component.questBakeSet != null;
            void Bake()
            {
                var built = ReFrameQuestBakeSetBuilder.Build(descriptor, component.questBakeSet);
                if (built == null)
                    return;
                Undo.RecordObject(component, "ReFrame: Quest 用マテリアルを作成");
                component.questBakeSet = built;
                EditorUtility.SetDirty(component);
                serializedObject.Update();
                ReFrameQuestMaterialPreview.Clear();
                refresh?.Invoke();
            }

            var button = new Button(Bake)
            {
                text = hasSet ? "Quest 用マテリアルを焼き直す" : "Quest 用マテリアルを作成",
            };
            button.style.height = 26;
            box.Add(button);

            if (hasSet)
            {

                var stale = ReFrameQuestBakeSetBuilder.IsStale(descriptor, component.questBakeSet);
                if (stale)
                {

                    var warn = new HelpBox(
                        "焼き済みのマテリアルが最新ではありません。" + (char)10
                            + "マテリアル・メッシュ・焼き込みの設定のどれかが変わっています (ビルドターゲットの切り替えでは古くなりません)。"
                            + "このままだと古い見た目でプレビューもビルドも行われます。上のボタンで焼き直せます。",
                        HelpBoxMessageType.Warning
                    );
                    warn.style.marginBottom = 2;
                    box.Add(warn);
                    PromptRebake(component, descriptor, Bake);
                }

                var field = new ObjectField("焼き済みの置き場")
                {
                    objectType = typeof(ReFrameQuestBakeSet),
                    value = component.questBakeSet,
                    allowSceneObjects = false,
                };
                field.SetEnabled(false);
                box.Add(field);
                box.Add(
                    new HelpBox(
                        component.questBakeSet.Entries.Count
                            + " 枚を焼いてあります。プレビューもビルドもこれを読むので、"
                            + (stale ? "" : "設定を変えても焼き直しは起きません。")
                            + "マテリアルを差し替えたり上の設定を変えたら、押して焼き直してください。",
                        HelpBoxMessageType.None
                    )
                );
            }
            else
            {
                box.Add(
                    new HelpBox(
                        "いまは設定を変えるたびにその場で焼いています (アバター全体で 1.7 秒)。"
                            + "ボタンを押して焼いておくと、プレビューもビルドも読むだけになります。",
                        HelpBoxMessageType.Info
                    )
                );
            }
            parent.Add(box);
        }

        /// <summary>「焼き直すか」を Unity のダイアログで 1 回だけ聞く。</summary>
        static void PromptRebake(
            ReFrameDeleteComponent component,
            VRCAvatarDescriptor descriptor,
            System.Action bake
        )
        {
            var signature = ReFrameQuestBakeSetBuilder.CurrentSignature(descriptor);
            var key = "ReFrame.RebakeDeclined." + component.GetInstanceID();

            if (SessionState.GetString(key, "") == signature)
                return;

            EditorApplication.delayCall += () =>
            {
                if (component == null || descriptor == null)
                    return;

                if (!ReFrameQuestBakeSetBuilder.IsStale(descriptor, component.questBakeSet))
                    return;
                if (ReFrameQuestBakeSetBuilder.CurrentSignature(descriptor) != signature)
                    return;

                var yes = EditorUtility.DisplayDialog(
                    "ReFrame: Quest 用マテリアルの焼き直し",
                    "焼き済みのマテリアルが最新ではありません。"
                        + (char)10
                        + "マテリアル・メッシュ・焼き込みの設定のどれかが変わっています (ビルドターゲットの切り替えでは古くなりません)。"
                        + "このままだと古い見た目でプレビューもビルドも行われます。"
                        + (char)10
                        + (char)10
                        + "今すぐ焼き直しますか？ (アバター全体で数秒〜1 分ほど)",
                    "はい (焼き直す)",
                    "いいえ (このまま)"
                );
                if (yes)
                    bake();
                else
                    SessionState.SetString(key, signature);
            };
        }

        /// <summary>半透明だったものの濃さ。</summary>
        void AddBackdropSlider(VisualElement parent, ReFrameDeleteComponent component)
        {
            var declared = (ReFrameQuestBlendBackdropAttribute[])
                System.Attribute.GetCustomAttributes(
                    component.GetType(),
                    typeof(ReFrameQuestBlendBackdropAttribute),
                    true
                );
            if (declared.Length == 0)
                return;

            var fallback = declared[0].Opacity;
            var slider = new Slider("半透明だったものの濃さ", 0f, 1f)
            {
                value = component.questBackdropOpacity >= 0f
                    ? component.questBackdropOpacity
                    : fallback,
                showInputField = true,
            };
            slider.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(component, "ReFrame: 半透明の濃さ");
                component.questBackdropOpacity = evt.newValue;
                EditorUtility.SetDirty(component);
            });
            parent.Add(slider);
            parent.Add(
                new HelpBox(
                    "ストッキングのように「薄く重ねて下を透かす」作りは Quest では再現できないので、"
                        + "下地の色をあらかじめ混ぜて焼きます。"
                        + "1 に近いほど濃く (厚手に)、0 に近いほど薄く見えます。",
                    HelpBoxMessageType.None
                )
            );
        }

        /// <summary>ドロップダウンに出す表示名。</summary>
        static string MenuIconModeLabel(ReFrameMenuIconMode mode)
        {
            switch (mode)
            {
                case ReFrameMenuIconMode.Keep:
                    return "そのまま";
                case ReFrameMenuIconMode.Remove:
                    return "アイコンを削除する";
                case ReFrameMenuIconMode.Size256:
                    return "縮めずに圧縮だけする";
                default:
                    return (int)mode + "px まで縮める";
            }
        }

        /// <summary>選んだ結果どうなるか。</summary>
        static string MenuIconModeHelp(ReFrameMenuIconMode mode)
        {
            switch (mode)
            {
                case ReFrameMenuIconMode.Keep:
                    return "アイコンには手をつけません。"
                        + "VRChat が 256px までは切り詰めますが、圧縮形式にはしないので"
                        + "非圧縮のアイコンは 1 枚 256KB を使います。";
                case ReFrameMenuIconMode.Remove:
                    return "メニューからアイコンを外します。文字だけのメニューになる代わり、"
                        + "アイコンのテクスチャ容量が丸ごと浮きます。";
                case ReFrameMenuIconMode.Size256:
                    return "大きさは変えず、非圧縮のアイコンだけを圧縮します。"
                        + "見た目はほぼ変わりません。";
                default:
                    return (int)mode + "px まで縮めて圧縮します。小さくするほど容量は減りますが、"
                        + "メニューでの見た目は粗くなります。";
            }
        }

        static System.Type _traceAndOptimizeType;
        static bool _traceAndOptimizeLookedUp;

        static System.Type TraceAndOptimizeType
        {
            get
            {
                if (_traceAndOptimizeLookedUp)
                    return _traceAndOptimizeType;
                _traceAndOptimizeLookedUp = true;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType("Anatawa12.AvatarOptimizer.TraceAndOptimize");
                    if (type == null)
                        continue;
                    _traceAndOptimizeType = type;
                    break;
                }
                return _traceAndOptimizeType;
            }
        }

        /// <summary>AAO が入っていても「Trace And Optimize」がアバターに付いていなければ片付けは走らない。</summary>
        static bool HasAvatarOptimizerSetup(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null)
                return true;
            var type = TraceAndOptimizeType;
            if (type == null)
                return false;
            return descriptor.GetComponentInChildren(type, true) != null;
        }

        public override VisualElement CreateInspectorGUI()
        {
            _labelSwitchers.Clear();
            _detailSwitchers.Clear();
            _cascadeRefreshers.Clear();
            _questRefreshers.Clear();
            _representativeProbes.Clear();
            _bitProbes.Clear();
            _deletingProbes.Clear();
            _deletableTotal = 0;
            _refreshSummary = null;
            _requestQuestAuditRefresh = null;
            _altHeld = false;
            _building = true;

            var root = new VisualElement();
            root.AddToClassList("reframe-inspector-root");
            ApplyStyleSheets(root);

            var component = (ReFrameDeleteComponent)target;

            _groupLabels.Clear();
            for (var type = component.GetType(); type != null; type = type.BaseType)
            {
                foreach (var attribute in type.GetCustomAttributes(typeof(ReFrameGroupLabelAttribute), false))
                {
                    var mapping = (ReFrameGroupLabelAttribute)attribute;
                    if (string.IsNullOrEmpty(mapping.GroupName) || string.IsNullOrEmpty(mapping.Label))
                        continue;
                    if (!_groupLabels.ContainsKey(mapping.GroupName))
                        _groupLabels[mapping.GroupName] = mapping.Label;
                }
            }
            _questEnabled = component.QuestConversionActive;

            if (ShowsPromo(component))
                root.Add(BuildPromoSection(component));
            root.Add(ReFrameUpdateChecker.BuildNotice(component));

            var previewProp = serializedObject.FindProperty(
                nameof(component.previewHiddenInHierarchy)
            );
            if (previewProp != null)
            {

                var previewField = new PropertyField(previewProp, "プレビューに反映 (消える物を Scene で隠す)");
                previewField.tooltip =
                    "ON の間、削除に指定した項目で消えるオブジェクトを Hierarchy と Scene ビューでも非表示にする。"
                    + "見た目確認用でビルドには影響しない。";
                previewField.style.marginBottom = 4;
                root.Add(previewField);
            }

            root.Add(BuildVariantBanner(component));

            var questSection = new VisualElement();

            var markTargetAvatar = component.GetComponentInParent<VRCAvatarDescriptor>();

            var sweepProp = serializedObject.FindProperty(nameof(component.sweepMode));
            if (sweepProp != null)
            {

                var current = (ReFrameSweepMode)sweepProp.enumValueIndex;
                var canDelegateToAao =
                    IsAvatarOptimizerInstalled() && HasAvatarOptimizerSetup(markTargetAvatar);

                var choices = new List<ReFrameSweepMode> { ReFrameSweepMode.Sweep };
                if (canDelegateToAao || current == ReFrameSweepMode.LeaveToAvatarOptimizer)
                    choices.Add(ReFrameSweepMode.LeaveToAvatarOptimizer);

                var sweepField = new PopupField<ReFrameSweepMode>(
                    "使わなくなったモノの片付け",
                    choices,
                    choices.Contains(current) ? current : ReFrameSweepMode.Sweep,
                    SweepModeLabel,
                    SweepModeLabel
                );
                sweepField.style.marginBottom = 4;
                root.Add(sweepField);

                var sweepHelp = new HelpBox("", HelpBoxMessageType.Info);
                sweepHelp.style.marginBottom = 6;
                void RefreshSweepHelp()
                {
                    if ((ReFrameSweepMode)sweepProp.enumValueIndex == ReFrameSweepMode.Sweep)
                    {
                        sweepHelp.messageType = HelpBoxMessageType.None;
                        sweepHelp.text =
                            "削除対象に合わせて、AAO などの最適化ツールより前に ReFrame があらかじめ削除します。\n"
                            + "他のツールを入れていなくても、アバターの容量とパフォーマンスランクが減ります。\n"
                            + "よく分からない場合はこのままで大丈夫です。\n\n"
                            + "ただし AAO とは違うロジックで、より攻めた削除をします。"
                            + "削除した項目で二度と有効にならないと判定したオブジェクトとボーンは、"
                            + "AAO が残すものでも消します。\n"
                            + "消すのはあくまで、ビルド後の既定の状態で非表示のまま二度と有効にならない物と、"
                            + "その巻き添えで誰も使わなくなったボーンだけです。"
                            + "表示されている物や、メニューやアニメーションで切り替わる物には触りません。"
                            + "消えて困るものがあれば「AAO にお任せする」に切り替えてください。\n\n"
                            + (
                                canDelegateToAao
                                    ? "アバターに AAO の「Trace And Optimize」が付いているので、"
                                        + "「AAO にお任せする」も選べます。"
                                    : "「AAO にお任せする」は選べません。"
                                        + (
                                            IsAvatarOptimizerInstalled()
                                                ? "アバターに AAO の「Trace And Optimize」を追加すると選べるようになります。"
                                                : "AAO (Avatar Optimizer) をプロジェクトに追加し、"
                                                    + "アバターに「Trace And Optimize」を付けると選べるようになります。"
                                        )
                            );
                        return;
                    }

                    if (!canDelegateToAao)
                    {
                        sweepHelp.messageType = HelpBoxMessageType.Warning;
                        sweepHelp.text =
                            (
                                IsAvatarOptimizerInstalled()
                                    ? "アバターに AAO の「Trace And Optimize」が付いていません。\n"
                                    : "AAO (Avatar Optimizer) がこのプロジェクトに入っていません。\n"
                            )
                            + "このままだとギミックが見えなくなるだけで中身は残り、"
                            + "容量もパフォーマンスランクも減りません。\n"
                            + "「あらかじめ削除する」に戻すことをおすすめします。";
                        return;
                    }

                    sweepHelp.messageType = HelpBoxMessageType.None;
                    sweepHelp.text =
                        "削除対象に合わせて、AAO (Avatar Optimizer) が削除できるように設定します。\n"
                        + "実際の削除は AAO が行います。\n"
                        + "AAO のほうが細かく削れるので、AAO を使っている場合はこちらが有利です。";
                }
                RefreshSweepHelp();
                sweepField.RegisterValueChangedCallback(evt =>
                {
                    serializedObject.Update();
                    sweepProp.enumValueIndex = (int)evt.newValue;
                    serializedObject.ApplyModifiedProperties();
                    RefreshSweepHelp();
                });
                root.Add(sweepHelp);
            }

            var questProp = serializedObject.FindProperty(
                nameof(component.deleteQuestUnsupportedComponents)
            );
            if (questProp != null)
            {

                var platformBox = new VisualElement();
                platformBox.style.marginBottom = 6;
                questSection.Add(platformBox);

                void RefreshPlatform()
                {
                    platformBox.Clear();

                    var now = ReFramePlatformSwitch.CurrentPlatform;
                    var destination =
                        now == ReFrameBuildPlatform.Android
                            ? ReFrameBuildPlatform.PC
                            : ReFrameBuildPlatform.Android;

                    platformBox.Add(
                        new HelpBox(
                            "いまのビルドターゲットは " + ReFramePlatformSwitch.Label(now) + " です。",
                            HelpBoxMessageType.None
                        )
                        { style = { marginBottom = 2 } }
                    );

                    var buttonHelp = new HelpBox(
                        destination == ReFrameBuildPlatform.Android
                            ? "Android プレビューモード: Unity のビルドターゲットを Android に切り替えます (再インポートで数分)。"
                                + (char)10
                                + "Quest 簡易対応版の設定で Quest 化した見た目を Scene と Hierarchy で確かめるのに使います。"
                                + (char)10
                                + "アップロードだけなら切り替えは不要です。SDK は Android のビルド時に自動で切り替えて、"
                                + "終わると元に戻します。Quest 用マテリアルの焼き込みも切り替えなくてもできます。"
                            : "PC (Windows) に戻すと通常の編集に戻ります。Quest 簡易対応版の設定はそのまま残ります。",
                        HelpBoxMessageType.None
                    );
                    buttonHelp.style.marginBottom = 2;
                    platformBox.Add(buttonHelp);

                    var button = new Button(() =>
                    {
                        if (ReFramePlatformSwitch.Switch(destination))
                        {
                            serializedObject.Update();
                            RefreshQuestAll();
                        }
                    })
                    {
                        text =
                            destination == ReFrameBuildPlatform.Android
                                ? "Quest (Android) 用に切り替える"
                                : "PC (Windows) 用に戻す",
                    };
                    button.style.height = 28;
                    platformBox.Add(button);
                }

                var questField = new PropertyField(questProp, "Quest 簡易対応");
                questField.style.marginBottom = 4;
                questSection.Add(questField);
                if (component.IsQuestVariant)
                {

                    questField.style.display = DisplayStyle.None;
                    if (!questProp.boolValue)
                    {
                        questProp.boolValue = true;
                        serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                var scopeProp = serializedObject.FindProperty(nameof(component.questScope));
                var scopeField = new PropertyField(scopeProp, "Quest 対応の適用モード");
                scopeField.style.marginBottom = 4;
                questSection.Add(scopeField);

                var scopeHelp = new HelpBox("", HelpBoxMessageType.Info);
                scopeHelp.style.marginBottom = 6;
                questSection.Add(scopeHelp);

                bool QuestActive() =>
                    questProp.boolValue
                    && ReFrameDeleteComponent.ScopeAppliesNow(component.questScope);

                bool QuestEditing() => questProp.boolValue;

                void RefreshQuestScope()
                {
                    var on = questProp.boolValue;
                    scopeField.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                    var dormant = on && !ReFrameDeleteComponent.ScopeAppliesNow(component.questScope);
                    scopeHelp.style.display = dormant ? DisplayStyle.Flex : DisplayStyle.None;
                    if (dormant)
                        scopeHelp.text =
                            "現在のビルドターゲットは "
                            + EditorUserBuildSettings.activeBuildTarget
                            + " なので、いまは Quest 簡易対応は効いていません (ビルドもプレビューも素の見た目)。"
                            + (char)10
                            + "下の設定はこのまま編集でき、Android 向けのビルドのときに自動で効きます。"
                            + "VRChat SDK で Windows と Android の両方にチェックを入れた同時ビルドでも、"
                            + "Android の回だけに効きます。"
                            + (char)10
                            + "Quest 化した見た目を Unity 上で確かめたいときだけ、上の「Quest (Android) 用に切り替える」で"
                            + "Android プレビューモードにしてください。";
                }

                var questHelp = new HelpBox("", HelpBoxMessageType.None);
                questHelp.style.marginBottom = 6;

                void RefreshQuestHelp()
                {
                    if (!QuestEditing())
                    {
                        questHelp.messageType = HelpBoxMessageType.None;
                        questHelp.text =
                            "ONにすると、Quest (Android) では動かないコンポーネントをビルド時に取り除きます。\n"
                            + "VRChat も Android へのアップロード時に同じものを取り除きますが、"
                            + "先に落としておくとパフォーマンスランクが実際の姿と揃います。";
                        return;
                    }

                    var found = new List<string>();
                    if (markTargetAvatar != null)
                    {
                        var counts = new Dictionary<string, int>();
                        var order = new List<string>();
                        foreach (
                            var target in markTargetAvatar.GetComponentsInChildren<Component>(true)
                        )
                        {
                            if (!ReFrameDeleteComponent.IsQuestUnsupportedComponent(target))
                                continue;
                            var label = target.GetType().Name;
                            if (counts.TryGetValue(label, out var n))
                            {
                                counts[label] = n + 1;
                                continue;
                            }
                            counts[label] = 1;
                            order.Add(label);
                        }
                        foreach (var label in order)
                            found.Add(label + " x" + counts[label]);
                    }

                    questHelp.messageType = HelpBoxMessageType.None;
                    questHelp.text =
                        (
                            found.Count > 0
                                ? "このアバターから取り除かれます: " + string.Join(" / ", found) + "\n"
                                : "取り除く対象は見つかりませんでした。\n"
                        )
                        + "消えるのはコンポーネントだけですが、それで空になった GameObject は"
                        + "「使わなくなったモノの片付け」の設定に従って回収されます。\n"
                        + "これらを実体に持つギミックは、コンポーネントだけ剥がすと壊れたまま残るので、"
                        + "維持の設定でもギミックごと OFF 固定で削除します (一覧では「Quest 非対応」)。\n"
                        + "シェーダーは対象外です (Quest 非対応シェーダーはマテリアルの差し替えで対処してください)。";
                }

                var questAudit = new VisualElement();
                questAudit.style.marginBottom = 6;

                void RefreshQuestAudit()
                {
                    questAudit.Clear();
                    if (!QuestEditing() || markTargetAvatar == null)
                        return;

                    var findings = ReFrameQuestAudit.Audit(markTargetAvatar.gameObject, QuestEditing());
                    if (findings == null)
                        return;

                    var blocking = findings.Where(f => f.Blocking).ToList();
                    var overLimit = findings.Where(f => !f.Blocking).ToList();

                    questAudit.Add(
                        new HelpBox(
                            "以下は削除を通す前のシーンをそのまま数えた目安です。"
                                + "実際のビルドではこれよりかなり小さくなります"
                                + "。"
                                + "アップロードできるかどうかは VRChat SDK のビルド画面で確認してください。",
                            HelpBoxMessageType.Info
                        )
                    );

                    if (blocking.Count == 0)
                        questAudit.Add(
                            new HelpBox(
                                "アップロードを止める項目はありません。",
                                HelpBoxMessageType.None
                            )
                        );
                    else
                        questAudit.Add(
                            BuildQuestAuditGroup(
                                "アップロードできません (" + blocking.Count + " 件)",
                                "この 3 つ — 使えないコンポーネント / Avatar Dynamics の超過 / 使えないシェーダー — "
                                    + "だけが SDK で Error になり、ビルドが通りません。",
                                blocking,
                                true
                            )
                        );

                    if (overLimit.Count > 0)
                        questAudit.Add(
                            BuildQuestAuditGroup(
                                "上限を超えています (" + overLimit.Count + " 件)",
                                "アップロードはできます。下がるのは他人のクライアント上での既定表示だけなので、"
                                    + "見た目とのつり合いで決めてください。",
                                overLimit,
                                false
                            )
                        );
                }

                var aaoNotice = new VisualElement();
                aaoNotice.style.marginBottom = 6;

                void RefreshAaoNotice()
                {
                    aaoNotice.Clear();
                    if (!QuestEditing() || markTargetAvatar == null)
                        return;
                    if (!IsAvatarOptimizerInstalled())
                    {
                        aaoNotice.Add(
                            new HelpBox(
                                "AvatarOptimizer が入っていません。Quest 化ではポリゴン・マテリアル・ボーンの削減が"
                                    + "要るので、VCC から導入してください (ReFrame では手が届かない範囲です)。",
                                HelpBoxMessageType.Error
                            )
                        );
                        return;
                    }
                    if (HasAvatarOptimizerSetup(markTargetAvatar))
                        return;

                    aaoNotice.Add(
                        new HelpBox(
                            "AvatarOptimizer の Trace and Optimize がアバターに付いていません。"
                                + "Quest 化では必須です — 付けるだけでメッシュ・マテリアル・ボーンが自動で減ります"
                                + "。",
                            HelpBoxMessageType.Error
                        )
                    );
                    var add = new Button(() =>
                    {
                        Undo.AddComponent(markTargetAvatar.gameObject, TraceAndOptimizeType);
                        RefreshAaoNotice();
                    })
                    {
                        text = "Trace and Optimize を追加する",
                    };
                    aaoNotice.Add(add);
                }

                RefreshAaoNotice();
                aaoNotice.schedule.Execute(RefreshAaoNotice).Every(1000);

                var constraintNotice = new VisualElement();
                constraintNotice.style.marginBottom = 6;

                void RefreshConstraintNotice()
                {
                    constraintNotice.Clear();
                    if (markTargetAvatar == null)
                        return;
                    var unityConstraints = 0;
                    foreach (var c in markTargetAvatar.GetComponentsInChildren<Component>(true))
                        if (c is UnityEngine.Animations.IConstraint)
                            unityConstraints++;
                    if (unityConstraints == 0)
                        return;
                    if (
                        markTargetAvatar.GetComponentInChildren<
                            nadena.dev.modular_avatar.core.ModularAvatarConvertConstraints
                        >(true) != null
                    )
                        return;

                    constraintNotice.Add(
                        new HelpBox(
                            "Unity 標準の Constraint が "
                                + unityConstraints
                                + " 個ありますが、MA Convert Constraints が付いていません。"
                                + (
                                    QuestActive()
                                        ? "このままだと Quest では全部が非対応扱いになり、Constraint を持つギミックが"
                                            + "強制削除され、素体側の Constraint も剥がされます。"
                                        : "Quest 簡易対応を ON にすると全部が非対応扱いになります。"
                                )
                                + "付ければ VRChat Constraints へ変換されて Quest でも動きます。",
                            QuestActive() ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning
                        )
                    );
                    var add = new Button(() =>
                    {
                        Undo.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarConvertConstraints>(
                            markTargetAvatar.gameObject
                        );
                        RefreshConstraintNotice();
                    })
                    {
                        text = "MA Convert Constraints を追加する",
                    };
                    constraintNotice.Add(add);
                }

                RefreshConstraintNotice();
                constraintNotice.schedule.Execute(RefreshConstraintNotice).Every(1000);

                var textureSizeBox = new VisualElement();
                textureSizeBox.style.marginBottom = 6;

                void RefreshTextureSize()
                {
                    textureSizeBox.Clear();
                    if (!QuestEditing())
                        return;

                    var choices = new List<int> { 2048, 1024, 512, 256 };
                    var current = component.questMaxTextureSize;

                    if (current <= 0)
                    {
                        current = 1024;
                        Undo.RecordObject(component, "ReFrame: テクスチャの上限");
                        component.questMaxTextureSize = current;
                        EditorUtility.SetDirty(component);
                    }

                    var field = new PopupField<int>(
                        "テクスチャの上限",
                        choices,
                        0,
                        v => v + " px",
                        v => v + " px"
                    );
                    field.index = Mathf.Max(0, choices.IndexOf(current));
                    field.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue <= 0)
                            return;
                        Undo.RecordObject(component, "ReFrame: テクスチャの上限");
                        component.questMaxTextureSize = evt.newValue;
                        EditorUtility.SetDirty(component);
                    });
                    textureSizeBox.Add(field);
                    textureSizeBox.Add(
                        new HelpBox(
                            "Quest のアバターは圧縮後 10MB / 非圧縮 40MB まで (PC は 200MB / 500MB)。"
                                + "ポリゴンを削るよりテクスチャを縮める方が効きます。",
                            HelpBoxMessageType.None
                        )
                    );
                }

                RefreshTextureSize();
                textureSizeBox.TrackPropertyValue(questProp, _ => RefreshTextureSize());

                var cutProp = serializedObject.FindProperty(nameof(component.cutCoveredMesh));
                var declaresCut =
                    component
                        .GetType()
                        .GetCustomAttributes(typeof(ReFrameCutCoveredAttribute), true)
                        .Length > 0;
                var cutBox = new VisualElement();
                cutBox.style.marginBottom = 6;

                void RefreshCut()
                {
                    cutBox.Clear();
                    if (!QuestEditing() || cutProp == null || !declaresCut)
                        return;

                    var field = new Toggle("服に隠れた面を切り取る") { value = cutProp.boolValue };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        serializedObject.Update();
                        cutProp.boolValue = evt.newValue;
                        serializedObject.ApplyModifiedProperties();
                        RefreshCut();
                    });
                    cutBox.Add(field);
                    if (!cutProp.boolValue)
                        return;

                    var cached = false;
                    foreach (
                        var a in component
                            .GetType()
                            .GetCustomAttributes(typeof(ReFrameCutCoveredAttribute), true)
                    )
                    {
                        var path = ((ReFrameCutCoveredAttribute)a).MaskAsset;
                        if (
                            !string.IsNullOrEmpty(path)
                            && AssetDatabase.LoadAssetAtPath<TextAsset>(path) != null
                        )
                            cached = true;
                    }
                    if (!cached)
                        cutBox.Add(
                            new HelpBox(
                                "覆われた面のデータがパッケージ内に見つからないので、"
                                    + "チェックを入れても切り取りは行われません。",
                                HelpBoxMessageType.Warning
                            )
                        );
                }

                RefreshCut();
                cutBox.TrackPropertyValue(questProp, _ => RefreshCut());

                var iconBox = new VisualElement();
                iconBox.style.marginBottom = 6;

                void RefreshIcons()
                {
                    iconBox.Clear();
                    if (!QuestEditing())
                        return;

                    var modes = new List<ReFrameMenuIconMode>
                    {
                        ReFrameMenuIconMode.Keep,
                        ReFrameMenuIconMode.Size256,
                        ReFrameMenuIconMode.Size128,
                        ReFrameMenuIconMode.Size64,
                        ReFrameMenuIconMode.Size32,
                        ReFrameMenuIconMode.Remove,
                    };
                    var current = component.menuIconMode;

                    var field = new PopupField<ReFrameMenuIconMode>(
                        "メニューのアイコン",
                        modes,
                        0,
                        MenuIconModeLabel,
                        MenuIconModeLabel
                    );
                    field.index = Mathf.Max(0, modes.IndexOf(current));
                    field.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(component, "ReFrame: メニューのアイコン");
                        component.menuIconMode = evt.newValue;
                        EditorUtility.SetDirty(component);
                        RefreshIcons();
                    });
                    iconBox.Add(field);

                    iconBox.Add(new HelpBox(MenuIconModeHelp(component.menuIconMode), HelpBoxMessageType.None));
                }

                RefreshIcons();
                iconBox.TrackPropertyValue(questProp, _ => RefreshIcons());

                var bakeBox = new VisualElement();
                bakeBox.style.marginBottom = 6;

                void RefreshBake()
                {
                    bakeBox.Clear();
                    if (!QuestEditing())
                        return;

                    var declared = ReFrameQuestMaterialConverter.DeclaredBake(component);
                    var overrideProp = serializedObject.FindProperty(
                        nameof(component.questBakeOverride)
                    );

                    var header = new Label("焼き込みの調整");
                    header.style.unityFontStyleAndWeight = FontStyle.Bold;
                    header.style.marginBottom = 2;
                    bakeBox.Add(header);

                    if (declared != null)
                    {
                        var useOwn = new Toggle("自分で決める")
                        {
                            value = overrideProp.boolValue,
                        };
                        useOwn.RegisterValueChangedCallback(evt =>
                        {
                            serializedObject.Update();
                            overrideProp.boolValue = evt.newValue;
                            serializedObject.ApplyModifiedProperties();
                            RefreshBake();
                        });
                        bakeBox.Add(useOwn);

                        if (!overrideProp.boolValue)
                        {
                            bakeBox.Add(
                                new HelpBox(
                                    "このアバター向けに詰められた値を使っています。" + (char)10
                                        + "明度 " + declared.Brightness
                                        + " / 法線マップの陰影 " + (declared.ShadowFromNormalMap ? "あり" : "なし")
                                        + " / テクスチャ上限 " + declared.MaxTextureSize + "px",
                                    HelpBoxMessageType.None
                                )
                            );

                            AddBackdropSlider(bakeBox, component);
                            AddBakeSetControls(bakeBox, component, markTargetAvatar, RefreshBake);
                            return;
                        }
                    }

                    var brightnessProp = serializedObject.FindProperty(
                        nameof(component.questTextureBrightness)
                    );
                    var brightness = new Slider("明度", 0f, 1f)
                    {
                        value = brightnessProp.floatValue,
                        showInputField = true,
                    };
                    brightness.RegisterValueChangedCallback(evt =>
                    {
                        serializedObject.Update();
                        brightnessProp.floatValue = evt.newValue;
                        serializedObject.ApplyModifiedProperties();
                    });
                    bakeBox.Add(brightness);

                    var shadowProp = serializedObject.FindProperty(
                        nameof(component.questShadowFromNormalMap)
                    );
                    var shadow = new Toggle("法線マップから陰影を焼き込む")
                    {
                        value = shadowProp.boolValue,
                    };
                    shadow.RegisterValueChangedCallback(evt =>
                    {
                        serializedObject.Update();
                        shadowProp.boolValue = evt.newValue;
                        serializedObject.ApplyModifiedProperties();
                    });
                    bakeBox.Add(shadow);

                    AddBackdropSlider(bakeBox, component);
                    AddBakeSetControls(bakeBox, component, markTargetAvatar, RefreshBake);

                    bakeBox.Add(
                        new HelpBox(
                            "Toon Lit は陰影を付けないので、元の見た目より明るく出ます。"
                                + "明度を下げると落ち着きます。"
                                + "法線マップは変換で捨てられるので、"
                                + "陰影として焼き込んでおくと凹凸が残ります。",
                            HelpBoxMessageType.None
                        )
                    );
                }

                RefreshBake();
                bakeBox.TrackPropertyValue(questProp, _ => RefreshBake());

                RefreshQuestAudit();

                UnityEngine.UIElements.IVisualElementScheduledItem pendingAudit = null;

                var auditUpToDate = true;
                _requestQuestAuditRefresh = () =>
                {
                    if (auditUpToDate)
                    {
                        auditUpToDate = false;
                        return;
                    }

                    if (pendingAudit != null)
                        pendingAudit.Pause();
                    pendingAudit = questAudit.schedule.Execute(RefreshQuestAudit).StartingIn(200);
                };

                void RefreshQuestAll()
                {
                    RefreshPlatform();
                    RefreshQuestScope();
                    RefreshQuestHelp();
                    RefreshTextureSize();
                    RefreshCut();
                    RefreshIcons();
                    RefreshBake();
                    RefreshQuestAudit();

                    RecomputeQuestForced(QuestActive());
                }

                RefreshPlatform();
                RefreshQuestScope();
                RefreshQuestHelp();
                questField.RegisterCallback<SerializedPropertyChangeEvent>(_ => RefreshQuestAll());
                scopeField.RegisterCallback<SerializedPropertyChangeEvent>(_ => RefreshQuestAll());
                questSection.Add(questHelp);
                questSection.Add(textureSizeBox);
                questSection.Add(cutBox);
                questSection.Add(iconBox);
                questSection.Add(bakeBox);
                questSection.Add(aaoNotice);

                root.Insert(0, constraintNotice);
                questSection.Add(questAudit);
                questSection.Add(BuildPhysBoneSection(component, markTargetAvatar, questProp));
            }

            var descriptor = component.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                root.Add(
                    new HelpBox(
                        "VRCAvatarDescriptor が親に見つかりません。メニュー階層からのグループ分けが"
                            + "できないため、全項目を「メニュー外」にまとめて表示します。",
                        HelpBoxMessageType.Info
                    )
                );
            }

            var usages = ReFrameMenuGrouping.BuildParameterUsages(descriptor);
            var bitUsage = BuildBitUsage(descriptor);

            _parameterLinks = ReFrameParameterLink.Build(descriptor);
            _parameterBits = ReFrameParameterLink.CollectBits(descriptor);
            _declaredParameterNames = new HashSet<string>(
                component
                    .GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .SelectMany(f => f.GetCustomAttributes<ReFrameDeleteAttribute>(true))
                    .Select(a => a.ParameterName)
            );

            root.Add(BuildDetailModeToggle());
            root.Add(BuildSummary(descriptor));
            var tree = BuildTree(component, usages, bitUsage, out var outsideMenu);

            CollapseThinGroups(tree);

            var order = (ReFrameGroupOrderAttribute)
                System.Attribute.GetCustomAttribute(component.GetType(), typeof(ReFrameGroupOrderAttribute), true);
            if (order != null)
                SortGroups(tree, order);

            foreach (var entry in tree.Entries)
                root.Add(BuildEntryElement(entry));
            foreach (var child in tree.Children)
                root.Add(BuildGroupElement(child));

            if (outsideMenu.Count > 0)
            {
                var group = new GroupNode { Name = "メニュー外" };
                group.Entries.AddRange(outsideMenu);
                var element = BuildGroupElement(group);
                element.Q(className: "reframe-category-header")
                    ?.AddToClassList("reframe-category-header--outside");
                root.Add(element);
            }

            if (questSection.childCount > 0)
            {
                questSection.style.marginTop = 10;

                if (component.IsQuestVariant || component.deleteQuestUnsupportedComponents)
                    root.Add(questSection);
            }

            RegisterWhenAllGoneBits(component);

            root.Add(BuildAltProbe());
            _building = false;
            RecomputeCascade();
            return root;
        }

        /// <summary>[ReFrameDeleteWhenAllGone] で消えるパラメーターを上部の集計に反映する。</summary>
        void RegisterWhenAllGoneBits(ReFrameDeleteComponent component)
        {
            var rules = component
                .GetType()
                .GetCustomAttributes(typeof(ReFrameDeleteWhenAllGoneAttribute), true)
                .Cast<ReFrameDeleteWhenAllGoneAttribute>()
                .Where(r => !string.IsNullOrEmpty(r.Parameter) && r.Requires.Length > 0)
                .ToList();
            if (rules.Count == 0)
                return;

            var counted = new HashSet<string>();
            foreach (var field in component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            foreach (var attribute in field.GetCustomAttributes<ReFrameDeleteAttribute>(true))
            {
                counted.Add(attribute.ParameterName);
                if (_parameterLinks.TryGetValue(attribute.ParameterName, out var links))
                    foreach (var link in links)
                        counted.Add(link.Name);
            }

            var bitsByParameter = new Dictionary<string, int>();
            foreach (var rule in rules)
            {
                if (counted.Contains(rule.Parameter) || bitsByParameter.ContainsKey(rule.Parameter))
                    continue;
                bitsByParameter[rule.Parameter] =
                    _parameterBits.TryGetValue(rule.Parameter, out var bits) ? bits : 0;
            }
            var total = bitsByParameter.Values.Sum();
            if (total == 0)
                return;
            _deletableTotal += total;

            _bitProbes.Add(() =>
            {
                var deleting = new HashSet<string>();
                foreach (var probe in _deletingProbes)
                foreach (var name in probe())
                {
                    deleting.Add(name);
                    if (_parameterLinks.TryGetValue(name, out var links))
                        foreach (var link in links)
                            deleting.Add(link.Name);
                }

                var freed = 0;
                for (var pass = 0; pass <= rules.Count; pass++)
                {
                    var changed = false;
                    foreach (var rule in rules)
                    {
                        if (deleting.Contains(rule.Parameter))
                            continue;
                        if (!rule.Requires.All(deleting.Contains))
                            continue;
                        deleting.Add(rule.Parameter);
                        if (bitsByParameter.TryGetValue(rule.Parameter, out var bits))
                            freed += bits;
                        changed = true;
                    }
                    if (!changed)
                        break;
                }
                return freed;
            });
        }

        /// <summary>「いまギミックごと消える設定になっている代表パラメーター」を集め直し、 [ReFrameBundleMember] の行の見え方を更新する。</summary>
        void RecomputeQuestForced(bool enabled)
        {
            _questEnabled = enabled;
            foreach (var refresh in _questRefreshers)
                refresh(enabled);
            RecomputeCascade();
        }

        /// <summary>[ReFrameApplyToAvatar] の行の値を、シーンのアバターの SkinnedMeshRenderer へそのまま書く。</summary>
        void ApplyAvatarChanges()
        {
            var component = target as ReFrameDeleteComponent;
            if (component == null)
                return;
            var descriptor = component.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
                return;
            var root = descriptor.transform;

            var weights = new Dictionary<SkinnedMeshRenderer, Dictionary<string, float>>();
            void Put(SkinnedMeshRenderer smr, string shape, float weight)
            {
                if (!weights.TryGetValue(smr, out var map))
                    weights[smr] = map = new Dictionary<string, float>();
                map[shape] = weight;
            }

            var values = new Dictionary<string, float>();
            foreach (var (name, value) in component.EnumerateAvatarChangeParameters())
                values[name] = value;
            if (values.Count > 0)
            {
                var controllers = ReFrameBakedVisibilityResolver.CollectFxControllers(descriptor);
                var overrides = ReFrameAnimatorUtil.BuildTreeOverrides(
                    component.EnumerateBlendTreeOverrides(),
                    values.Keys
                );
                var forced = new ReFrameBakedVisibilityResolver.ForcedStates();
                ReFrameBakedVisibilityResolver.BeginResolveScope();
                try
                {
                    foreach (var (controller, pathRoot) in controllers)
                        ReFrameBakedVisibilityResolver.ResolveActiveStates(
                            controller,
                            pathRoot,
                            forced,
                            values,
                            overrides
                        );
                }
                finally
                {
                    ReFrameBakedVisibilityResolver.EndResolveScope();
                }
                foreach (var kv in forced.BlendShapeWeights)
                    foreach (var shape in kv.Value)
                        Put(kv.Key, shape.Key, shape.Value);
            }

            foreach (var (path, shape, weight) in component.EnumerateAvatarChangeBlendShapes())
            {
                var transform = root.Find(path);
                var smr = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
                if (smr != null)
                    Put(smr, shape, weight);
            }

            foreach (var kv in weights)
            {
                var smr = kv.Key;
                if (smr == null || smr.sharedMesh == null)
                    continue;
                var recorded = false;
                foreach (var shape in kv.Value)
                {
                    var index = smr.sharedMesh.GetBlendShapeIndex(shape.Key);
                    if (index < 0)
                        continue;
                    if (Mathf.Approximately(smr.GetBlendShapeWeight(index), shape.Value))
                        continue;
                    if (!recorded)
                    {
                        Undo.RecordObject(smr, "ReFrame: アバターに反映");
                        recorded = true;
                    }
                    smr.SetBlendShapeWeight(index, shape.Value);
                }
                if (recorded)
                    EditorUtility.SetDirty(smr);
            }
        }

        void RecomputeCascade()
        {
            if (_building)
                return;
            var deleting = new HashSet<string>();
            foreach (var probe in _representativeProbes)
                foreach (var name in probe())
                    deleting.Add(name);
            foreach (var refresh in _cascadeRefreshers)
                refresh(deleting);

            _requestQuestAuditRefresh?.Invoke();

            ApplyAvatarChanges();

            if (_refreshSummary == null)
                return;
            var freed = 0;
            foreach (var probe in _bitProbes)
                freed += probe();
            _refreshSummary(freed);
        }

        /// <summary>上部の集計。</summary>
        VisualElement BuildSummary(VRCAvatarDescriptor descriptor)
        {
            const int budget = VRC
                .SDK3
                .Avatars
                .ScriptableObjects
                .VRCExpressionParameters
                .MAX_PARAMETER_COST;

            var container = new VisualElement();
            container.AddToClassList("reframe-summary");

            var line = new VisualElement();
            line.AddToClassList("reframe-summary__line");
            var summaryLabel = new Label();
            summaryLabel.AddToClassList("reframe-summary__text");
            line.Add(summaryLabel);
            container.Add(line);

            var bar = new VisualElement();
            bar.AddToClassList("reframe-summary__bar");

            var fixedPart = new VisualElement();
            fixedPart.AddToClassList("reframe-summary__bar-fixed");
            var fixedLabel = new Label();
            fixedLabel.AddToClassList("reframe-summary__bar-label");
            fixedLabel.AddToClassList("reframe-summary__bar-label--fixed");
            fixedPart.Add(fixedLabel);

            var kept = new VisualElement();
            kept.AddToClassList("reframe-summary__bar-kept");
            var keptLabel = new Label();
            keptLabel.AddToClassList("reframe-summary__bar-label");
            kept.Add(keptLabel);
            var cut = new VisualElement();
            cut.AddToClassList("reframe-summary__bar-cut");
            var cutLabel = new Label();
            cutLabel.AddToClassList("reframe-summary__bar-label");
            cutLabel.AddToClassList("reframe-summary__bar-label--cut");
            cut.Add(cutLabel);
            var free = new VisualElement();
            free.AddToClassList("reframe-summary__bar-free");
            var freeLabel = new Label();
            freeLabel.AddToClassList("reframe-summary__bar-label");
            freeLabel.AddToClassList("reframe-summary__bar-label--free");
            free.Add(freeLabel);
            bar.Add(fixedPart);
            bar.Add(kept);
            bar.Add(cut);
            bar.Add(free);
            container.Add(bar);

            _refreshSummary = freed =>
            {

                var total = TotalBitUsage(descriptor);
                var after = total - freed;
                summaryLabel.text = freed > 0
                    ? $"パラメーター  使用 {after} ({total} - 削除対象 {freed}) / {budget} bit"
                    : $"パラメーター  使用 {total} / {budget} bit";
                summaryLabel.EnableInClassList("reframe-summary__text--over", after > budget);

                var deletableLeft = Mathf.Max(0, _deletableTotal - freed);
                var fixedBits = Mathf.Max(0, total - _deletableTotal);
                var remaining = Mathf.Max(0, budget - total);

                fixedPart.style.flexGrow = fixedBits;
                kept.style.flexGrow = deletableLeft;
                cut.style.flexGrow = freed;
                free.style.flexGrow = remaining;

                fixedLabel.text = fixedBits > 0 ? $"削除不可 {fixedBits}" : string.Empty;
                keptLabel.text = deletableLeft > 0 ? $"削除可 {deletableLeft}" : string.Empty;
                cutLabel.text = freed > 0 ? $"削除対象 {freed}" : string.Empty;
                freeLabel.text = remaining > 0 ? $"空き {remaining}" : string.Empty;
            };
            return container;
        }

        /// <summary>Alt を押している間、各行の表示名をメニュー項目名からパラメーター名へ差し替える。</summary>
        static bool HasPromo(System.Type type) =>
            type.GetCustomAttributes<ReFramePromoAttribute>(true).Any()
            || type.GetCustomAttributes<ReFramePromoItemAttribute>(true).Any();

        /// <summary>宣伝は Inspector の最上段に出すので、同じ GameObject に並ぶ ReFrame のうち一番上のものにだけ出す。</summary>
        static bool ShowsPromo(ReFrameDeleteComponent component)
        {
            if (!HasPromo(component.GetType()))
                return false;
            foreach (var sibling in component.GetComponents<ReFrameDeleteComponent>())
                if (sibling != null && HasPromo(sibling.GetType()))
                    return sibling == component;
            return false;
        }

        VisualElement BuildPromoSection(ReFrameDeleteComponent component)
        {
            var type = component.GetType();
            var box = new VisualElement();
            box.AddToClassList("reframe-category");
            box.AddToClassList("reframe-promo");

            var shop = type.GetCustomAttributes<ReFramePromoAttribute>(true).FirstOrDefault();
            var header = new VisualElement();
            header.AddToClassList("reframe-category-header");
            header.AddToClassList("reframe-category-header--foldable");
            var arrow = new Label();
            arrow.AddToClassList("reframe-category-header__arrow");
            header.Add(arrow);
            var title = new Label(shop?.Title ?? "BOOTH の商品紹介");
            title.AddToClassList("reframe-category-header__label");
            header.Add(title);
            var chip = new Label("宣伝");
            chip.AddToClassList("reframe-chip");
            chip.AddToClassList("reframe-promo__chip");
            chip.tooltip = "作者のショップ (BOOTH) の商品紹介です。ReFrame の設定には関係ありません。";
            header.Add(chip);
            if (shop != null && !string.IsNullOrEmpty(shop.Url))
            {
                var url = shop.Url;
                var open = new Button(() => Application.OpenURL(url)) { text = shop.ButtonLabel, tooltip = url };
                open.AddToClassList("reframe-promo__button");
                header.Add(open);
            }
            box.Add(header);

            var content = new VisualElement();
            content.AddToClassList("reframe-category-content");
            if (shop != null && !string.IsNullOrEmpty(shop.Description))
            {
                var description = new Label(shop.Description);
                description.AddToClassList("reframe-promo__description");
                description.style.marginBottom = 4;
                content.Add(description);
            }
            content.Add(BuildPromoCarousel(type.GetCustomAttributes<ReFramePromoItemAttribute>(true).ToList()));
            box.Add(content);

            var key = FoldKey(component, "Promo");
            var expanded = SessionState.GetBool(key, false);
            void Apply()
            {
                arrow.text = expanded ? "▼" : "▶";
                content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            }
            Apply();
            header.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target is Button)
                    return;
                expanded = !expanded;
                SessionState.SetBool(key, expanded);
                Apply();
            });
            return box;
        }

        const int PromoPageSize = 3;
        const long PromoAutoScrollMs = 6000;

        /// <summary>商品を 3 件ずつのページで見せ、左右ボタンと一定時間で切り替える。</summary>
        VisualElement BuildPromoCarousel(List<ReFramePromoItemAttribute> promos)
        {
            var carousel = new VisualElement();
            carousel.AddToClassList("reframe-promo__carousel");
            if (promos.Count == 0)
                return carousel;

            var pages = (promos.Count + PromoPageSize - 1) / PromoPageSize;
            var rows = new List<VisualElement>();
            var list = new VisualElement();
            list.AddToClassList("reframe-promo__list");
            foreach (var promo in promos)
            {
                var row = BuildPromoItem(promo);
                rows.Add(row);
                list.Add(row);
            }
            carousel.Add(list);

            if (pages <= 1)
                return carousel;

            var nav = new VisualElement();
            nav.AddToClassList("reframe-promo__nav");
            var prev = new Button { text = "◀" };
            prev.AddToClassList("reframe-promo__nav-button");
            var counter = new Label();
            counter.AddToClassList("reframe-promo__nav-counter");
            var next = new Button { text = "▶" };
            next.AddToClassList("reframe-promo__nav-button");
            nav.Add(prev);
            nav.Add(counter);
            nav.Add(next);
            carousel.Add(nav);

            var page = 0;
            var hovered = false;
            void Show()
            {
                for (var i = 0; i < rows.Count; i++)
                    rows[i].style.display =
                        i / PromoPageSize == page ? DisplayStyle.Flex : DisplayStyle.None;
                counter.text = (page + 1) + " / " + pages;
            }
            var timer = carousel.schedule.Execute(() =>
            {
                if (hovered || IsHidden(carousel))
                    return;
                page = (page + 1) % pages;
                Show();
            });
            timer.Every(PromoAutoScrollMs);
            void Move(int delta)
            {
                page = (page + delta + pages) % pages;
                Show();
                timer.Pause();
                timer.Resume();
            }
            prev.clickable = new Clickable(() => Move(-1));
            next.clickable = new Clickable(() => Move(1));
            carousel.RegisterCallback<MouseEnterEvent>(_ => hovered = true);
            carousel.RegisterCallback<MouseLeaveEvent>(_ => hovered = false);
            Show();
            return carousel;
        }

        static bool IsHidden(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None)
                    return true;
            return false;
        }

        VisualElement BuildPromoItem(ReFramePromoItemAttribute promo)
        {
            var row = new VisualElement();
            row.AddToClassList("reframe-promo__row");

            var texture = string.IsNullOrEmpty(promo.ImagePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(promo.ImagePath);
            var image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit };
            image.AddToClassList("reframe-promo__image");
            image.EnableInClassList("reframe-promo__image--empty", texture == null);
            row.Add(image);

            var text = new VisualElement();
            text.AddToClassList("reframe-promo__text");
            var name = new Label(promo.Title);
            name.AddToClassList("reframe-promo__title");
            text.Add(name);
            if (!string.IsNullOrEmpty(promo.Description))
            {
                var description = new Label(promo.Description);
                description.AddToClassList("reframe-promo__description");
                text.Add(description);
            }
            row.Add(text);

            var url = promo.Url;
            var button = new Button(() => Application.OpenURL(url)) { text = "BOOTH で見る", tooltip = url };
            button.AddToClassList("reframe-promo__button");
            row.Add(button);
            row.RegisterCallback<ClickEvent>(e =>
            {
                if (!(e.target is Button))
                    Application.OpenURL(url);
            });
            return row;
        }

        VisualElement BuildAltProbe()
        {
            var probe = new IMGUIContainer(() =>
            {
                var alt = Event.current != null && Event.current.alt;
                if (alt == _altHeld)
                    return;
                _altHeld = alt;
                foreach (var switcher in _labelSwitchers)
                    switcher(alt);
            });
            probe.style.height = 0;
            return probe;
        }

        void ApplyStyleSheets(VisualElement root)
        {
            Add(root, CommonStyleSheetPath);

            var theme = target.GetType().GetCustomAttribute<ReFrameThemeAttribute>(false);
            if (theme != null)
                Add(root, theme.StyleSheetPath);

            Add(root, InspectorStyleSheetPath);
        }

        static void Add(VisualElement root, string path)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet != null)
                root.styleSheets.Add(sheet);
        }

        /// <summary>この型 (PC 用) に対応する Quest 簡易対応版の型。</summary>
        static System.Type FindQuestVariantType(System.Type baseType)
        {
            foreach (var type in TypeCache.GetTypesWithAttribute<ReFrameQuestVariantAttribute>())
                if (!type.IsAbstract && type.IsSubclassOf(baseType))
                    return type;
            return null;
        }

        /// <summary>同じアバターにある、この PC 用に対応する Quest 簡易対応版。</summary>
        static ReFrameDeleteComponent FindQuestVariantOf(ReFrameDeleteComponent source)
        {
            foreach (var component in source.gameObject.GetComponents<ReFrameDeleteComponent>())
                if (component != null && component != source && component.IsQuestVariant)
                    return component;
            var descriptor = source.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor == null)
                return null;
            foreach (var component in descriptor.GetComponentsInChildren<ReFrameDeleteComponent>(true))
                if (component != null && component != source && component.IsQuestVariant)
                    return component;
            return null;
        }

        /// <summary>同じアバターにある PC 用 (通常のクラス)。</summary>
        static ReFrameDeleteComponent FindBaseOf(ReFrameDeleteComponent variant)
        {
            foreach (var component in variant.gameObject.GetComponents<ReFrameDeleteComponent>())
                if (component != null && component != variant && !component.IsQuestVariant)
                    return component;
            var descriptor = variant.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor == null)
                return null;
            foreach (var component in descriptor.GetComponentsInChildren<ReFrameDeleteComponent>(true))
                if (component != null && component != variant && !component.IsQuestVariant)
                    return component;
            return null;
        }

        /// <summary>PC 用から Quest 簡易対応版を作る。</summary>
        internal static ReFrameDeleteComponent CreateQuestVariant(ReFrameDeleteComponent source)
        {
            var type = FindQuestVariantType(source.GetType());
            if (type == null)
            {
                EditorUtility.DisplayDialog(
                    "ReFrame",
                    source.GetType().Name + " には Quest 簡易対応版のクラスがありません。"
                        + (char)10
                        + "[ReFrameQuestVariant] を付けた派生クラスを用意してください。",
                    "OK"
                );
                return null;
            }
            var existing = FindQuestVariantOf(source);
            if (existing != null)
                return existing;

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            var variant = (ReFrameDeleteComponent)Undo.AddComponent(source.gameObject, type);

            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), variant);
            variant.deleteQuestUnsupportedComponents = true;
            variant.questScope = ReFrameQuestScope.AndroidOnly;
            EditorUtility.SetDirty(variant);

            Undo.RecordObject(source, "ReFrame: Quest 簡易対応版を作成");
            source.deleteQuestUnsupportedComponents = false;
            EditorUtility.SetDirty(source);
            Undo.CollapseUndoOperations(group);
            Debug.Log(
                "[ReFrameCore] Quest 簡易対応版 " + type.Name + " を作成し、" + source.GetType().Name + " の設定を写しました。"
            );
            return variant;
        }

        /// <summary>両方あるときの「プレビュー: 自動 / PC 用 / Quest 簡易対応版」の切り替え。</summary>
        VisualElement BuildPreviewSideRow(
            ReFrameDeleteComponent current,
            ReFrameDeleteComponent baseComponent,
            ReFrameDeleteComponent variant
        )
        {
            var root = current.GetComponentInParent<VRCAvatarDescriptor>(true);
            var rootObject = root != null ? root.gameObject : current.transform.root.gameObject;
            var box = new VisualElement();

            var caption = new Label("プレビューに使う側 (ビルドには影響しません)");
            caption.style.marginBottom = 2;
            box.Add(caption);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;
            box.Add(row);

            void Rebuild()
            {
                row.Clear();
                var side = ReFramePreviewSide.Get(rootObject);
                var autoIsQuest = variant != null && ReFrameDeleteComponent.ScopeAppliesNow(variant.questScope);
                void AddChoice(string text, bool? value)
                {
                    var button = new Button(() =>
                    {
                        ReFramePreviewSide.Set(rootObject, value);
                        Rebuild();
                    })
                    {
                        text = text,
                    };
                    button.style.height = 24;
                    button.style.flexGrow = 1;

                    button.SetEnabled(side != value);
                    row.Add(button);
                }
                AddChoice("自動 (いまは " + (autoIsQuest ? "Quest 簡易対応版" : "PC 用") + ")", null);
                AddChoice("PC 用", false);
                AddChoice("Quest 簡易対応版", true);
            }
            Rebuild();

            return box;
        }

        /// <summary>一番上に出す、PC 用と Quest 簡易対応版の案内。</summary>
        VisualElement BuildVariantBanner(ReFrameDeleteComponent component)
        {
            var box = new VisualElement();
            box.style.marginBottom = 6;

            if (component.IsQuestVariant)
            {
                box.Add(
                    new HelpBox(
                        "Quest 簡易対応版です。ビルドターゲットが Android のとき (適用モードが「常に」なら PC でも) "
                            + "こちらの設定が使われ、PC 用の ReFrame は無視されます。"
                            + (char)10
                            + "行の値・揺れ物・焼き込みは PC 用とは別に持ちます。",
                        HelpBoxMessageType.Info
                    )
                    { style = { marginBottom = 2 } }
                );
                var baseComponent = FindBaseOf(component);
                if (baseComponent != null)
                    box.Add(BuildPreviewSideRow(component, baseComponent, component));
                else
                    box.Add(new HelpBox("PC 用の ReFrame が見つかりません。PC のビルドでは ReFrame は何もしません。", HelpBoxMessageType.Warning));
                return box;
            }

            var variant = FindQuestVariantOf(component);
            if (variant != null)
            {
                box.Add(
                    new HelpBox(
                        "PC 用の設定です。Quest 用は別コンポーネント「Quest 簡易対応版」で編集します"
                            + " (Android のビルドではそちらが使われます)。",
                        HelpBoxMessageType.None
                    )
                    { style = { marginBottom = 2 } }
                );
                box.Add(BuildPreviewSideRow(component, component, variant));
                return box;
            }

            var hasType = FindQuestVariantType(component.GetType()) != null;
            if (component.deleteQuestUnsupportedComponents)
            {
                box.Add(
                    new HelpBox(
                        "以前の方式で、この PC 用コンポーネントに Quest 簡易対応のチェックが入っています。"
                            + "いまは Quest の設定を別コンポーネント「Quest 簡易対応版」に分けて持つ方式です。"
                            + (hasType
                                ? "下のボタンで、いまの設定を写した Quest 簡易対応版を作り、こちらのチェックを外します。"
                                : "この型には Quest 簡易対応版のクラスがありません。"),
                        HelpBoxMessageType.Warning
                    )
                    { style = { marginBottom = 2 } }
                );
            }
            else
            {
                box.Add(
                    new HelpBox(
                        "Quest 用の設定は別コンポーネント「Quest 簡易対応版」で持ちます。"
                            + (hasType
                                ? "作成すると、いまの設定 (衣装の ON/OFF や値、揺れ物の選択など) をそのまま写した Quest 簡易対応版が同じ GameObject に付きます。"
                                : "この型には Quest 簡易対応版のクラスがありません ([ReFrameQuestVariant] を付けた派生クラスが要ります)。"),
                        HelpBoxMessageType.None
                    )
                    { style = { marginBottom = 2 } }
                );
            }
            if (hasType)
            {
                var create = new Button(() =>
                {
                    var created = CreateQuestVariant(component);
                    if (created != null)
                        Selection.activeObject = created;
                })
                {
                    text = component.deleteQuestUnsupportedComponents ? "Quest 簡易対応版へ移す" : "Quest 簡易対応版を作成",
                };
                create.style.height = 26;
                box.Add(create);
            }
            return box;
        }

        GroupNode BuildTree(
            ReFrameDeleteComponent component,
            Dictionary<string, ReFrameMenuGrouping.ParameterUsage> usages,
            Dictionary<string, int> bitUsage,
            out List<EntryInfo> outsideMenu
        )
        {
            var root = new GroupNode();
            outsideMenu = new List<EntryInfo>();

            var countedLinks = new HashSet<string>();

            foreach (var field in component.GetType().GetFields(FieldFlags))
            {
                if (field.FieldType != typeof(ReFrameDeleteEntry))
                    continue;

                var entry = new EntryInfo
                {
                    Field = field,
                    Type = ReFrameParameterType.Auto,
                    Label = ObjectNames.NicifyVariableName(field.Name),
                    Reversed = field.GetCustomAttribute<ReFrameReverseAttribute>(true) != null,
                    ApplyToAvatar = field.GetCustomAttribute<ReFrameApplyToAvatarAttribute>(true) != null,
                };

                var menuOnly = field.GetCustomAttribute<ReFrameMenuOnlyAttribute>(true);
                var valueLocked = field.GetCustomAttribute<ReFrameValueLockedAttribute>(true);
                if (menuOnly != null)
                    entry.LockedValue = menuOnly.FixedValue;
                else if (valueLocked != null)
                    entry.LockedValue = valueLocked.FixedValue;

                var bundle = field.GetCustomAttribute<ReFrameBundleMemberAttribute>(true);
                entry.BundleRepresentative = bundle?.RepresentativeParameterName;
                entry.BundleValueMatters = bundle?.ValueMatters ?? false;
                foreach (var link in field.GetCustomAttributes<ReFrameLinkedWithAttribute>(true))
                    entry.LinkedParameters.Add(link.ParameterName);

                entry.QuestForcedCandidate = component.HasQuestUnsupportedTarget(field);

                var paths = new List<string[]>();
                var first = true;
                var labelFromMenu = false;
                foreach (var attribute in field.GetCustomAttributes<ReFrameDeleteAttribute>(true))
                {
                    if (first)
                    {
                        entry.Type = attribute.Type;
                        first = false;
                    }
                    if (bitUsage.TryGetValue(attribute.ParameterName, out var bits))
                        entry.BitCost += bits;

                    if (_parameterLinks.TryGetValue(attribute.ParameterName, out var linked))
                        foreach (var link in linked)
                            if (
                                !_declaredParameterNames.Contains(link.Name)
                                && countedLinks.Add(link.Name)
                            )
                                entry.BitCost += link.Bits;
                    entry.ParameterNames.Add(attribute.ParameterName);

                    var inMenu = usages.TryGetValue(attribute.ParameterName, out var usage);
                    entry.Parameters.Add((attribute.ParameterName, inMenu));
                    if (!inMenu)
                        continue;
                    paths.Add(usage.GroupPath);
                    if (!labelFromMenu)
                    {
                        entry.Label = usage.ControlName;
                        entry.Usage = usage;
                        entry.UsageParameterName = attribute.ParameterName;
                        labelFromMenu = true;
                    }
                }

                var forcedGroup = field.GetCustomAttribute<ReFrameMenuGroupAttribute>(true);

                var deletesObjects = false;
                foreach (var objectAttribute in field.GetCustomAttributes<ReFrameDeleteObjectAttribute>(true))
                {
                    deletesObjects = true;
                    entry.DeleteObjectPaths.Add(objectAttribute.Path);
                }

                entry.BlendShapeRow =
                    entry.Parameters.Count == 0
                    && !deletesObjects
                    && field.GetCustomAttributes<ReFrameBlendShapeAttribute>(true).Any();

                var deletesLayers =
                    field.GetCustomAttributes<ReFrameDeleteLayerAttribute>(true).Any()
                    || field.GetCustomAttributes<ReFrameDeleteStateAttribute>(true).Any()
                    || field.GetCustomAttributes<ReFrameMenuRemoveAttribute>(true).Any();

                if (entry.Parameters.Count == 0 && !deletesObjects && !entry.BlendShapeRow && !deletesLayers)
                    continue;

                entry.Kind = ResolveValueKind(entry);

                if (
                    entry.Usage != null
                    && (
                        entry.Kind == ValueKind.Choice
                        || entry.Usage.RadialPuppetAxis
                        || entry.Usage.TwoOrFourAxisPuppetAxis
                    )
                )
                    entry.Label = entry.UsageParameterName;

                var labelOverride = field.GetCustomAttribute<ReFrameLabelAttribute>(true);
                if (labelOverride != null && !string.IsNullOrEmpty(labelOverride.Label))
                    entry.Label = labelOverride.Label;

                var groupPath =
                    forcedGroup != null ? forcedGroup.Path
                    : paths.Count > 0 ? ReFrameMenuGrouping.CommonPrefix(paths)
                    : null;

                if (groupPath == null || groupPath.Length == 0)
                {
                    outsideMenu.Add(entry);
                    continue;
                }

                var node = root;
                foreach (var segment in groupPath)
                    node = node.Child(segment);
                node.Entries.Add(entry);
            }

            return root;
        }

        /// <summary>メニュー階層をそのまま見出しとして並べる。</summary>
        static void CollapseThinGroups(GroupNode node)
        {

            foreach (var child in node.Children)
                CollapseThinGroups(child);

            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                var child = node.Children[i];
                if (child.Children.Count > 0 || child.Entries.Count != 1)
                    continue;
                node.Entries.Add(child.Entries[0]);
                node.Children.RemoveAt(i);
            }
        }

        static void SortGroups(GroupNode node, ReFrameGroupOrderAttribute order)
        {
            var indexed = new List<(GroupNode Node, int Declared, int Original)>();
            for (var i = 0; i < node.Children.Count; i++)
                indexed.Add((node.Children[i], order.IndexOf(node.Children[i].Name), i));
            indexed.Sort((a, b) =>
                a.Declared != b.Declared ? a.Declared.CompareTo(b.Declared) : a.Original.CompareTo(b.Original));
            node.Children.Clear();
            foreach (var item in indexed)
            {
                node.Children.Add(item.Node);
                SortGroups(item.Node, order);
            }
        }

        /// <summary>開閉状態の保存先。</summary>
        static string FoldKey(ReFrameDeleteComponent component, string groupName) =>
            "ReFrame.Fold." + component.GetType().FullName + "." + groupName;

        VisualElement BuildGroupElement(GroupNode node, int depth = 0)
        {

            var container = new VisualElement();
            container.AddToClassList("reframe-category");
            if (depth > 0)
                container.AddToClassList("reframe-category--nested");

            var header = new VisualElement();
            header.AddToClassList("reframe-category-header");
            if (depth > 0)
                header.AddToClassList("reframe-category-header--nested");

            var content = new VisualElement();
            content.AddToClassList("reframe-category-content");

            var foldable = depth == 0;
            Label arrow = null;
            if (foldable)
            {
                header.AddToClassList("reframe-category-header--foldable");
                arrow = new Label();
                arrow.AddToClassList("reframe-category-header__arrow");
                header.Add(arrow);
            }

            var label = new Label(GroupLabelOf(node.Name));
            label.AddToClassList("reframe-category-header__label");
            header.Add(label);

            foreach (var entry in node.Entries)
                content.Add(BuildEntryElement(entry));
            foreach (var child in node.Children)
                content.Add(BuildGroupElement(child, depth + 1));

            if (foldable)
            {
                var component = (ReFrameDeleteComponent)target;
                var key = FoldKey(component, node.Name);
                var expanded = SessionState.GetBool(key, true);

                void Apply()
                {
                    arrow.text = expanded ? "▼" : "▶";
                    content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                }

                Apply();
                header.RegisterCallback<ClickEvent>(_ =>
                {
                    expanded = !expanded;
                    SessionState.SetBool(key, expanded);
                    Apply();
                });
            }

            container.Add(header);
            container.Add(content);
            return container;
        }

        /// <summary>詳細モードの切り替え。</summary>
        VisualElement BuildDetailModeToggle()
        {
            var row = new VisualElement();
            row.AddToClassList("reframe-detail-toggle");

            var toggle = new Toggle("詳細モード")
            {
                value = SessionState.GetBool(DetailModeKey, false),
                tooltip = "1 行を消したときに、まとまって消えるパラメーターと同期ビットを行ごとに出す。",
            };
            toggle.RegisterValueChangedCallback(evt =>
            {
                SessionState.SetBool(DetailModeKey, evt.newValue);
                foreach (var switcher in _detailSwitchers)
                    switcher(evt.newValue);
            });
            row.Add(toggle);
            return row;
        }

        /// <summary>その行を消したときにまとまって消えるパラメーターの内訳。</summary>
        VisualElement BuildEntryDetail(EntryInfo entry)
        {
            var box = new VisualElement();
            box.AddToClassList("reframe-entry__detail");

            var total = 0;
            var lines = new List<(string Text, bool Linked)>();
            foreach (var parameter in entry.Parameters)
            {
                var own = _parameterBits.TryGetValue(parameter.Name, out var b) ? b : 0;
                total += own;
                lines.Add((parameter.Name + "  " + own + "bit", false));

                if (!_parameterLinks.TryGetValue(parameter.Name, out var links))
                    continue;
                foreach (var link in links)
                {
                    total += link.Bits;
                    lines.Add(("└ " + link.Name + "  " + link.Bits + "bit  (" + link.Reason + ")", true));
                }
            }

            foreach (var path in entry.DeleteObjectPaths)
                lines.Add(("[オブジェクト] " + path, true));

            if (lines.Count == 0)
                return box;

            var head = new Label("まとまって消える — 合計 " + total + "bit");
            head.AddToClassList("reframe-entry__detail-head");
            box.Add(head);

            foreach (var line in lines)
            {
                var label = new Label(line.Text);
                label.AddToClassList("reframe-entry__detail-line");
                if (line.Linked)
                    label.AddToClassList("reframe-entry__detail-line--linked");
                box.Add(label);
            }
            return box;
        }

        /// <summary>[ReFrameLinkedWith] の相手の Enabled を同じ値に揃える。</summary>
        void SyncLinkedEntries(EntryInfo entry, bool enabled)
        {
            if (entry.LinkedParameters.Count == 0 || target is not ReFrameDeleteComponent component)
                return;
            foreach (var name in entry.LinkedParameters)
            {
                var partner = component.FindFieldByParameterName(name);
                if (partner == null || partner == entry.Field)
                    continue;
                var partnerEnabled = serializedObject
                    .FindProperty(partner.Name)
                    ?.FindPropertyRelative(nameof(ReFrameDeleteEntry.Enabled));
                if (partnerEnabled != null)
                    partnerEnabled.boolValue = enabled;
            }
        }

        VisualElement BuildEntryElement(EntryInfo entry)
        {
            var container = new VisualElement();
            container.AddToClassList("reframe-entry");

            if (!string.IsNullOrEmpty(entry.BundleRepresentative))
                container.AddToClassList("reframe-entry--member");

            var property = serializedObject.FindProperty(entry.Field.Name);
            var enabledProp = property?.FindPropertyRelative(nameof(ReFrameDeleteEntry.Enabled));
            var valueProp = property?.FindPropertyRelative(nameof(ReFrameDeleteEntry.Value));
            if (enabledProp == null || valueProp == null)
                return container;

            var main = new VisualElement();
            main.AddToClassList("reframe-entry__main");

            var left = new VisualElement();
            left.AddToClassList("reframe-entry__left");
            var right = new VisualElement();
            right.AddToClassList("reframe-entry__right");
            main.Add(left);
            main.Add(right);

            var label = new Label(entry.ApplyToAvatar ? entry.Label + "（アバターに反映）" : entry.Label);
            label.AddToClassList("reframe-entry__label");

            label.tooltip = entry.ApplyToAvatar
                ? entry.Label + (char)10 + "この行の値はシーンのアバターの BlendShape にもその場で書き込まれます (OFF にしても戻しません)。"
                : entry.Label;
            label.RegisterCallback<ContextClickEvent>(evt =>
            {
                evt.StopPropagation();
                ShowCopyMenu(entry);
            });
            left.Add(label);

            var parameters = new Label();
            parameters.AddToClassList("reframe-entry__params");
            parameters.RegisterCallback<ContextClickEvent>(evt =>
            {
                evt.StopPropagation();
                ShowCopyMenu(entry);
            });
            left.Add(parameters);

            var menuName = entry.Label;

            var primaryName =
                entry.Parameters.Count > 0
                    ? entry.Parameters[0].Name + (entry.Parameters[0].InMenu ? "" : "*")
                    : entry.DeleteObjectPaths.Count > 0
                        ? entry.DeleteObjectPaths[0]
                        : entry.Label;
            var rest =
                entry.Parameters.Count > 0
                    ? FormatParameters(entry, 1)
                    : string.Join(", ", entry.DeleteObjectPaths.GetRange(
                        System.Math.Min(1, entry.DeleteObjectPaths.Count),
                        System.Math.Max(0, entry.DeleteObjectPaths.Count - 1)
                    ));
            _labelSwitchers.Add(alt =>
            {
                label.text = alt ? primaryName : menuName;
                parameters.text = alt ? rest : string.Empty;
            });

            container.tooltip = BuildTooltip(entry);

            var bitCost = new Label();
            bitCost.AddToClassList("reframe-entry__bit-cost");
            right.Add(bitCost);

            var tile = BuildEnabledTile(
                entry,
                enabledProp,
                valueProp,
                container,
                bitCost,
                out var refreshTile
            );
            right.Add(tile);

            System.Action syncTile = null;

            var cascadedByRepresentative = false;

            var questForced = false;

            var value = BuildValueField(entry, valueProp, main, () => syncTile?.Invoke());
            right.Add(value);

            container.Add(main);

            var detail = BuildEntryDetail(entry);
            detail.style.display = SessionState.GetBool(DetailModeKey, false)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _detailSwitchers.Add(on =>
                detail.style.display = on ? DisplayStyle.Flex : DisplayStyle.None
            );
            container.Add(detail);

            void RefreshRow()
            {

                var forced = questForced
                    ? "quest"
                    : cascadedByRepresentative
                        ? (entry.BundleValueMatters ? "cascaded-value" : "cascaded")
                        : null;
                refreshTile(forced);

                var valueEditable = questForced
                    ? false
                    : cascadedByRepresentative
                        ? entry.BundleValueMatters
                        : enabledProp.boolValue;
                value.SetEnabled(valueEditable);
            }

            void Sync()
            {
                RefreshRow();

                RecomputeCascade();
            }

            syncTile = Sync;
            Sync();

            _deletableTotal += entry.BitCost;
            _bitProbes.Add(() =>
                enabledProp.boolValue || cascadedByRepresentative || questForced
                    ? entry.BitCost
                    : 0
            );

            var menuOnlyRow = entry.Field.GetCustomAttribute<ReFrameMenuOnlyAttribute>(true) != null;
            _deletingProbes.Add(() =>
                !menuOnlyRow && (enabledProp.boolValue || cascadedByRepresentative || questForced)
                    ? entry.ParameterNames
                    : System.Linq.Enumerable.Empty<string>()
            );

            _representativeProbes.Add(() =>
            {

                if (questForced)
                    return entry.ParameterNames;
                if (!enabledProp.boolValue)
                    return System.Linq.Enumerable.Empty<string>();
                var effective = entry.LockedValue ?? valueProp.floatValue;
                return Mathf.Approximately(effective, entry.Reversed ? 1f : 0f)
                    ? entry.ParameterNames
                    : System.Linq.Enumerable.Empty<string>();
            });

            if (!string.IsNullOrEmpty(entry.BundleRepresentative))
            {
                _cascadeRefreshers.Add(deleting =>
                {
                    cascadedByRepresentative = deleting.Contains(entry.BundleRepresentative);

                    RefreshRow();
                });
            }

            if (entry.QuestForcedCandidate)
            {
                _questRefreshers.Add(on =>
                {
                    questForced = on;
                    RefreshRow();
                });
                questForced = _questEnabled;
                RefreshRow();
            }

            container.RegisterCallback<ClickEvent>(evt =>
            {

                if (evt.button > 0)
                    return;

                if (cascadedByRepresentative || questForced)
                    return;

                if (
                    !entry.LockedValue.HasValue
                    && enabledProp.boolValue
                    && evt.target is VisualElement source
                    && (value == source || value.Contains(source))
                )
                    return;

                serializedObject.Update();

                enabledProp.boolValue = !enabledProp.boolValue;
                SyncLinkedEntries(entry, enabledProp.boolValue);
                enabledProp.serializedObject.ApplyModifiedProperties();
                Sync();

                RecomputeCascade();
            });

            container.TrackPropertyValue(enabledProp, _ =>
            {
                Sync();
                RecomputeCascade();
            });

            return container;
        }

        /// <summary>揺れ物 (PhysBone / PhysBoneCollider) を一覧から選んで消すセクション。</summary>
        VisualElement BuildPhysBoneSection(
            ReFrameDeleteComponent component,
            VRCAvatarDescriptor avatar,
            SerializedProperty questProp
        )
        {
            var section = new VisualElement();
            section.style.marginTop = 8;

            void Refresh()
            {
                section.Clear();

                if (questProp == null || component == null || !questProp.boolValue || avatar == null)
                    return;

                var entries = ReFramePhysBoneCatalog.SnapshotEntries(avatar.transform);
                if (entries == null)
                    return;
                if (entries.Count == 0)
                    return;

                List<string> PhysBones() => component.deletedPhysBones ??= new List<string>();
                List<string> Colliders() => component.deletedPhysBoneColliders ??= new List<string>();

                var header = new Label("揺れ物を減らす");
                header.style.unityFontStyleAndWeight = FontStyle.Bold;
                header.style.marginBottom = 2;
                section.Add(header);

                var summary = new HelpBox("", HelpBoxMessageType.None);
                summary.style.marginBottom = 4;
                section.Add(summary);

                void RefreshSummary()
                {
                    var keptBones = 0;
                    var keptComponents = 0;
                    var keptChecks = 0;
                    foreach (var entry in entries)
                    {

                        var keptInGroup = 0;
                        for (var i = 0; i < entry.Keys.Count; i++)
                        {
                            if (ReFramePhysBoneCatalog.IsSelected(PhysBones(), entry.Keys[i]))
                                continue;
                            keptInGroup++;
                            keptBones += i < entry.BoneCounts.Count ? entry.BoneCounts[i] : 0;
                        }
                        if (keptInGroup == 0)
                            continue;
                        keptComponents += keptInGroup;
                        foreach (var collider in entry.Colliders)
                            if (!ReFramePhysBoneCatalog.IsSelected(Colliders(), collider.Key))
                                keptChecks += collider.CollisionChecks;
                    }

                    summary.text =
                        "残る見込み (目安): PhysBone " + keptComponents + " / 上限 8"
                        + "、影響ボーン " + keptBones + " / 上限 64"
                        + "、当たり判定 " + keptChecks + " / 上限 64"
                        + (char)10
                        + "この 3 つが上限を超えているとアップロードできません。"
                        + "数字は削除を通す前のシーンの値なので、実際はこれよりかなり小さくなります。"
                        + "どれを消すと何本減るかの<b>比較</b>には使えますが、上限との比較には使えません。";
                }

                void Toggle(List<string> list, string key, bool on)
                {
                    Undo.RecordObject(component, "ReFrame: 揺れ物の削除");
                    if (on)
                    {
                        if (!list.Contains(key))
                            list.Add(key);
                    }
                    else
                        list.Remove(key);
                    EditorUtility.SetDirty(component);

                    serializedObject.Update();
                }

                string currentUnit = null;
                VisualElement unitContent = null;
                var unitTotals = new Dictionary<string, (int Bones, int Members)>();
                foreach (var entry in entries)
                {
                    if (entry.Keys.Count == 0)
                        continue;
                    unitTotals.TryGetValue(entry.Unit, out var t);
                    unitTotals[entry.Unit] = (t.Bones + entry.AffectedBones, t.Members + entry.Keys.Count);
                }
                foreach (var entry in entries.OrderBy(e => e.UnitOrder).ToList())
                {
                    var captured = entry;

                    if (captured.Keys.Count == 0)
                        continue;

                    if (captured.Unit != currentUnit)
                    {
                        currentUnit = captured.Unit;
                        unitTotals.TryGetValue(currentUnit, out var total);

                        var unitBox = new VisualElement();
                        unitBox.AddToClassList("reframe-category");
                        unitBox.AddToClassList("reframe-physbone-unit");
                        var unitHeader = new VisualElement();
                        unitHeader.AddToClassList("reframe-category-header");
                        unitHeader.AddToClassList("reframe-category-header--foldable");
                        var arrow = new Label();
                        arrow.AddToClassList("reframe-category-header__arrow");
                        unitHeader.Add(arrow);
                        var unitLabel = new Label(
                            currentUnit + "  (" + total.Members + " 本 / 影響ボーン " + total.Bones + ")"
                        );
                        unitLabel.AddToClassList("reframe-category-header__label");
                        unitHeader.Add(unitLabel);
                        unitBox.Add(unitHeader);
                        var content = new VisualElement();
                        content.AddToClassList("reframe-category-content");
                        unitBox.Add(content);
                        section.Add(unitBox);

                        var foldKey = "ReFrame.PBUnitFold." + component.GetType().FullName + "." + currentUnit;
                        var expanded = SessionState.GetBool(foldKey, true);
                        void ApplyFold()
                        {
                            arrow.text = expanded ? "▼" : "▶";
                            content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                        }
                        ApplyFold();
                        unitHeader.RegisterCallback<ClickEvent>(_ =>
                        {
                            expanded = !expanded;
                            SessionState.SetBool(foldKey, expanded);
                            ApplyFold();
                        });
                        unitContent = content;
                    }

                    var memberSuffix = captured.Keys.Count > 1 ? "  (" + captured.Keys.Count + " 本 / 影響ボーン " : "  (影響ボーン ";
                    var row = new Toggle(captured.Label + memberSuffix + captured.AffectedBones + ")")
                    {
                        value = captured.Keys.TrueForAll(
                            k => ReFramePhysBoneCatalog.IsSelected(PhysBones(), k)
                        ),
                    };
                    row.style.marginLeft = 2;
                    unitContent.Add(row);

                    var colliderRows = new List<Toggle>();
                    foreach (var collider in captured.Colliders)
                    {
                        var capturedCollider = collider;
                        var colliderRow = new Toggle(
                            "当たり判定: " + capturedCollider.Label
                                + "  (-" + capturedCollider.CollisionChecks + " 回"
                                + (capturedCollider.Shared ? " / 他の揺れ物と共有" : "")
                                + ")"
                        )
                        {
                            value = ReFramePhysBoneCatalog.IsSelected(
                                Colliders(),
                                capturedCollider.Key
                            ),
                        };
                        colliderRow.style.marginLeft = 20;
                        colliderRow.RegisterValueChangedCallback(evt =>
                        {
                            Toggle(Colliders(), capturedCollider.Key, evt.newValue);
                            RefreshSummary();
                            _requestQuestAuditRefresh?.Invoke();
                        });
                        colliderRows.Add(colliderRow);
                        unitContent.Add(colliderRow);
                    }

                    row.RegisterValueChangedCallback(evt =>
                    {
                        foreach (var key in captured.Keys)
                            Toggle(PhysBones(), key, evt.newValue);

                        if (evt.newValue)
                        {
                            for (var i = 0; i < captured.Colliders.Count; i++)
                            {
                                Toggle(Colliders(), captured.Colliders[i].Key, true);
                                colliderRows[i].SetValueWithoutNotify(true);
                            }
                        }
                        foreach (var colliderRow in colliderRows)
                            colliderRow.SetEnabled(!evt.newValue);
                        RefreshSummary();
                        _requestQuestAuditRefresh?.Invoke();
                    });
                    foreach (var colliderRow in colliderRows)
                        colliderRow.SetEnabled(!row.value);
                }

                RefreshSummary();
            }

            Refresh();
            if (questProp != null)
                section.TrackPropertyValue(questProp, _ => Refresh());
            return section;
        }

        /// <summary>Quest の判定を 1 グループぶん描く。</summary>
        static VisualElement BuildQuestAuditGroup(
            string title,
            string description,
            List<ReFrameQuestAudit.Finding> findings,
            bool blocking
        )
        {
            var box = new HelpBox(
                title + (char)10 + description,
                blocking ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning
            );
            box.style.marginBottom = 4;

            var list = new VisualElement();
            list.style.marginLeft = 4;
            list.style.marginTop = 2;
            foreach (var finding in findings)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;

                var label = new Label(finding.Label);
                label.style.overflow = Overflow.Hidden;
                label.style.flexShrink = 1;
                row.Add(label);

                var amount = new Label(
                    finding.Limit == "0" ? finding.Value : finding.Value + " / " + finding.Limit
                );
                amount.style.unityFontStyleAndWeight = FontStyle.Bold;
                amount.style.flexShrink = 0;
                amount.style.marginLeft = 8;
                row.Add(amount);

                list.Add(row);
            }
            box.Add(list);
            return box;
        }

        /// <summary>行の地色を決める状態クラスを 1 つだけ立てる。</summary>
        static void SetRowState(VisualElement row, string state)
        {
            row.EnableInClassList("reframe-entry--menu-deleted", state == "menu-deleted");
            row.EnableInClassList("reframe-entry--gimmick-deleted", state == "gimmick-deleted");
            row.EnableInClassList("reframe-entry--cascaded", state == "cascaded");
        }

        /// <summary>Enabled は素の Toggle ではなく、状態をテキストで示すタイルとして描く。</summary>
        static VisualElement BuildEnabledTile(
            EntryInfo entry,
            SerializedProperty enabledProp,
            SerializedProperty valueProp,
            VisualElement row,
            Label bitCost,
            out System.Action<string> refresh
        )
        {

            void RefreshBits(bool deleting)
            {
                bitCost.text = entry.BitCost <= 0 ? "—" : $"{entry.BitCost}bit";
                bitCost.tooltip = entry.BitCost <= 0
                    ? "同期パラメーターは使っていません"
                    : deleting
                        ? $"{entry.BitCost}bit 削除中 (いま空いている)"
                        : $"{entry.BitCost}bit 削除可 (削除すると空く)";
                bitCost.EnableInClassList(
                    "reframe-entry__bit-cost--active",
                    deleting && entry.BitCost > 0
                );
            }

            var tile = new VisualElement();
            tile.AddToClassList("reframe-entry__tile");

            var label = new Label();
            label.AddToClassList("reframe-entry__tile-label");
            tile.Add(label);

            refresh = forced =>
            {
                if (forced != null)
                {

                    label.text =
                        forced == "quest" ? "Quest 非対応"
                        : forced == "cascaded-value" ? "値で選択"
                        : "前提削除";
                    tile.AddToClassList("reframe-entry__tile--on");
                    SetRowState(row, "cascaded");
                    RefreshBits(true);
                    return;
                }
                var deleting = enabledProp.boolValue;

                if (entry.BlendShapeRow)
                {
                    label.text = deleting ? "固定" : string.Empty;
                    SetRowState(row, null);
                    RefreshBits(false);
                    if (deleting)
                        tile.AddToClassList("reframe-entry__tile--on");
                    else
                        tile.RemoveFromClassList("reframe-entry__tile--on");
                    return;
                }

                var effective = entry.LockedValue ?? valueProp.floatValue;
                var zeroIsOff =
                    entry.Kind == ValueKind.Choice
                    && Mathf.Approximately(effective, 0f)
                    && entry.Usage != null
                    && !entry.Usage.Choices.Any(c => Mathf.Approximately(c.Value, 0f));
                var gimmick =
                    (
                        entry.Kind == ValueKind.OnOff
                        && Mathf.Approximately(effective, entry.Reversed ? 1f : 0f)
                    )
                    || zeroIsOff;
                label.text = !deleting ? string.Empty
                    : gimmick ? "ギミック削除"
                    : "メニュー削除";
                SetRowState(row, !deleting ? null : gimmick ? "gimmick-deleted" : "menu-deleted");
                RefreshBits(deleting);
                if (deleting)
                    tile.AddToClassList("reframe-entry__tile--on");
                else
                    tile.RemoveFromClassList("reframe-entry__tile--on");
            };

            return tile;
        }

        /// <summary>アバター全体の同期ビット消費。</summary>
        static int TotalBitUsage(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null)
                return 0;
            try
            {
                var total = 0;
                foreach (
                    var provided in nadena.dev.ndmf.ParameterInfo.ForUI.GetParametersForObject(
                        descriptor.gameObject
                    )
                )
                    total += provided.BitUsage;
                return total;
            }
            catch (System.Exception)
            {

                return descriptor.expressionParameters != null
                    ? descriptor.expressionParameters.CalcTotalCost()
                    : 0;
            }
        }

        /// <summary>パラメーター名 → 削除したときに浮く VRChat の同期ビット数。</summary>
        static Dictionary<string, int> BuildBitUsage(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<string, int>();
            if (descriptor == null)
                return result;

            var parameters = descriptor.expressionParameters;
            if (parameters?.parameters != null)
            {
                foreach (var parameter in parameters.parameters)
                {
                    if (parameter == null || string.IsNullOrEmpty(parameter.name))
                        continue;
                    var cost = !parameter.networkSynced ? 0
                        : parameter.valueType
                            == VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool
                            ? 1
                            : 8;

                    if (!result.TryGetValue(parameter.name, out var existing) || cost > existing)
                        result[parameter.name] = cost;
                }
            }

            try
            {
                foreach (
                    var provided in nadena.dev.ndmf.ParameterInfo.ForUI.GetParametersForObject(
                        descriptor.gameObject
                    )
                )
                {
                    if (string.IsNullOrEmpty(provided.EffectiveName))
                        continue;
                    if (!result.ContainsKey(provided.EffectiveName))
                        result[provided.EffectiveName] = provided.BitUsage;
                }
            }
            catch (System.Exception e)
            {

                Debug.LogWarning($"[ReFrameCore] NDMF からビット数を取得できませんでした: {e.Message}");
            }

            return result;
        }

        /// <summary>Value 欄の出し分け。</summary>
        static ValueKind ResolveValueKind(EntryInfo entry)
        {
            if (entry.BlendShapeRow)
                return ValueKind.Slider;
            var usage = entry.Usage;
            if (usage != null && usage.Choices.Count >= 2)
                return ValueKind.Choice;
            if (
                entry.Type == ReFrameParameterType.Bool
                || (entry.Type == ReFrameParameterType.Auto && usage != null && usage.Choices.Count == 1)
                || (entry.Type == ReFrameParameterType.Auto && usage == null)
            )
                return ValueKind.OnOff;
            return ValueKind.Slider;
        }

        VisualElement BuildValueField(
            EntryInfo entry,
            SerializedProperty valueProp,
            VisualElement trackHost,
            System.Action onValueChanged
        )
        {

            if (entry.LockedValue.HasValue)
                return BuildLockedValue(entry, entry.LockedValue.Value);

            switch (entry.Kind)
            {
                case ValueKind.Choice:
                    return BuildChoiceDropdown(entry.Usage, valueProp, trackHost, onValueChanged);
                case ValueKind.OnOff:
                    return BuildOnOffTile(valueProp, entry.Reversed, trackHost, onValueChanged);
                default:
                {
                    var low = entry.Usage != null && entry.Usage.TwoOrFourAxisPuppetAxis ? -1f : 0f;

                    var high = entry.BlendShapeRow ? 100f
                        : entry.Type == ReFrameParameterType.Int ? 255f
                        : 1f;
                    return BuildSlider(
                        valueProp,
                        low,
                        high,
                        entry.Type == ReFrameParameterType.Int,
                        trackHost,
                        onValueChanged
                    );
                }
            }
        }

        /// <summary>属性で固定された Value を読み取り専用で見せるタイル。</summary>
        static VisualElement BuildLockedValue(EntryInfo entry, float locked)
        {
            var tile = new VisualElement();
            tile.AddToClassList("reframe-entry__tile");
            tile.AddToClassList("reframe-entry__tile--value");

            string text;
            if (entry.Kind == ValueKind.OnOff)
            {
                var raw = locked >= 0.5f;
                text = ((entry.Reversed ? !raw : raw) ? "ON" : "OFF") + " 固定";
            }
            else if (entry.Usage != null)
            {
                text = $"{locked:0.##} 固定";
                foreach (var (value, controlName) in entry.Usage.Choices)
                {
                    if (!Mathf.Approximately(value, locked))
                        continue;
                    text = controlName + " 固定";
                    break;
                }
            }
            else
                text = $"{locked:0.##} 固定";

            var label = new Label(text);
            label.AddToClassList("reframe-entry__tile-label");
            tile.Add(label);
            return tile;
        }

        /// <summary>メニュー項目名を選択肢にしたプルダウン。</summary>
        VisualElement BuildChoiceDropdown(
            ReFrameMenuGrouping.ParameterUsage usage,
            SerializedProperty valueProp,
            VisualElement trackHost,
            System.Action onValueChanged
        )
        {
            var values = new List<float>();
            var choices = new List<string>();
            foreach (var (value, controlName) in usage.Choices)
            {

                if (value < 0f)
                    continue;
                values.Add(value);
                choices.Add($"{controlName}  ({value:0.##})");
            }

            var dropdown = new DropdownField { choices = choices };
            dropdown.AddToClassList("reframe-entry__dropdown");
            ClearMargins(dropdown);

            void Refresh()
            {
                var current = valueProp.floatValue;
                var index = values.FindIndex(v => Mathf.Approximately(v, current));
                if (index < 0)
                {

                    var label = Mathf.Approximately(current, 0f)
                        ? "OFF  (0)"
                        : $"{current:0.##}  (候補外)";
                    if (!choices.Contains(label))
                    {
                        values.Insert(0, current);
                        choices.Insert(0, label);
                        dropdown.choices = choices;
                    }
                    index = values.FindIndex(v => Mathf.Approximately(v, current));
                }
                dropdown.SetValueWithoutNotify(index >= 0 ? choices[index] : null);
                onValueChanged?.Invoke();
            }

            Refresh();
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var index = choices.IndexOf(evt.newValue);
                if (index < 0)
                    return;
                serializedObject.Update();
                valueProp.floatValue = values[index];
                valueProp.serializedObject.ApplyModifiedProperties();
                onValueChanged?.Invoke();
            });
            trackHost.TrackPropertyValue(valueProp, _ => Refresh());
            return dropdown;
        }

        /// <summary>Bool の Value。</summary>
        VisualElement BuildOnOffTile(
            SerializedProperty valueProp,
            bool reversed,
            VisualElement trackHost,
            System.Action onValueChanged
        )
        {
            var tile = new VisualElement();
            tile.AddToClassList("reframe-entry__tile");
            tile.AddToClassList("reframe-entry__tile--value");

            var label = new Label();
            label.AddToClassList("reframe-entry__tile-label");
            tile.Add(label);

            void Refresh()
            {

                var raw = valueProp.floatValue >= 0.5f;
                var on = reversed ? !raw : raw;
                label.text = on ? "ON" : "OFF";
                if (on)
                    tile.AddToClassList("reframe-entry__tile--on");
                else
                    tile.RemoveFromClassList("reframe-entry__tile--on");
                onValueChanged?.Invoke();
            }

            Refresh();
            tile.RegisterCallback<ClickEvent>(_ =>
            {
                serializedObject.Update();
                valueProp.floatValue = valueProp.floatValue >= 0.5f ? 0f : 1f;
                valueProp.serializedObject.ApplyModifiedProperties();
                Refresh();
            });
            trackHost.TrackPropertyValue(valueProp, _ => Refresh());
            return tile;
        }

        /// <summary>連続値の Value。</summary>
        VisualElement BuildSlider(
            SerializedProperty valueProp,
            float low,
            float high,
            bool integer,
            VisualElement trackHost,
            System.Action onValueChanged
        )
        {
            var container = new VisualElement();
            container.AddToClassList("reframe-entry__slider-group");

            var slider = new Slider(low, high);
            slider.AddToClassList("reframe-entry__slider");
            ClearMargins(slider);

            slider.style.marginBottom = 4;

            var number = new FloatField();
            number.AddToClassList("reframe-entry__number");
            ClearMargins(number);
            number.style.marginLeft = 4;

            var syncing = false;
            void Push(float raw)
            {
                var v = integer ? Mathf.Round(raw) : raw;
                serializedObject.Update();
                valueProp.floatValue = Mathf.Clamp(v, low, high);
                valueProp.serializedObject.ApplyModifiedProperties();
            }

            void Refresh()
            {
                syncing = true;
                slider.SetValueWithoutNotify(valueProp.floatValue);
                number.SetValueWithoutNotify(valueProp.floatValue);
                syncing = false;
                onValueChanged?.Invoke();
            }

            Refresh();
            slider.RegisterValueChangedCallback(evt =>
            {
                if (syncing)
                    return;
                Push(evt.newValue);
                Refresh();
            });
            number.RegisterValueChangedCallback(evt =>
            {
                if (syncing)
                    return;
                Push(evt.newValue);
                Refresh();
            });
            trackHost.TrackPropertyValue(valueProp, _ => Refresh());

            container.Add(slider);
            container.Add(number);
            return container;
        }

        /// <summary>Unity 既定の .unity-base-field / .unity-toggle のマージンは uss の子孫セレクタでは 詳細度で負けることがあり、タイルとの間に隙間ができる。</summary>
        static void ClearMargins(VisualElement element)
        {
            element.style.marginLeft = 0;
            element.style.marginRight = 0;
            element.style.marginTop = 0;
            element.style.marginBottom = 0;
        }

        /// <summary>パラメーター名を右クリックしたときのコピーメニュー。</summary>
        static void ShowCopyMenu(EntryInfo entry)
        {
            var menu = new GenericMenu();
            var all = new System.Text.StringBuilder();

            foreach (var (name, _) in entry.Parameters)
            {
                var value = name;
                if (all.Length > 0)
                    all.Append('\n');
                all.Append(value);

                menu.AddItem(
                    new GUIContent(value.Replace("/", "∕") + " をコピー"),
                    false,
                    () => EditorGUIUtility.systemCopyBuffer = value
                );
            }

            if (entry.Parameters.Count > 1)
            {
                var joined = all.ToString();
                menu.AddSeparator(string.Empty);
                menu.AddItem(
                    new GUIContent($"すべてコピー ({entry.Parameters.Count} 件)"),
                    false,
                    () => EditorGUIUtility.systemCopyBuffer = joined
                );
            }

            menu.ShowAsContext();
        }

        /// <summary>行に並べるパラメーター名を from 番目から連結する。</summary>
        static string FormatParameters(EntryInfo entry, int from)
        {
            var text = new System.Text.StringBuilder();
            for (var i = from; i < entry.Parameters.Count; i++)
            {
                var (name, inMenu) = entry.Parameters[i];
                if (text.Length > 0)
                    text.Append("　");
                text.Append(name);
                if (!inMenu)
                    text.Append('*');
            }
            return text.ToString();
        }

        /// <summary>行に入りきらない情報 (フィールド名と全パラメーター) を tooltip に持たせる。</summary>
        static string BuildTooltip(EntryInfo entry)
        {
            var text = new System.Text.StringBuilder(entry.Field.Name);
            foreach (var (name, inMenu) in entry.Parameters)
            {
                text.Append('\n').Append(name);
                if (!inMenu)
                    text.Append("  (メニュー外)");
            }
            if (entry.BitCost > 0)
                text.Append($"\n\n削除すると {entry.BitCost} bit 空く");
            return text.ToString();
        }
    }
}
