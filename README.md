# ReFrameCore

ReFrame 系パッケージ (ReFrameForIKUSIA など) の共通基盤。VRChat アバターの NDMF ビルド時に、
オブジェクトの状態・メニュー・アニメーションを **元のアセットを壊さずに** 編集する。
[ReFrameForIKUSIA](https://github.com/illusive-isc/ReFrameForIKUSIA) から利用されることを想定している。

アバター個別のパッケージは `ReFrameDeleteComponent` を継承したコンポーネントを 1 つ用意し、
そのフィールドに属性で「何を削除するか」を宣言するだけでよい。実際の削除・掃除・プレビュー・
Quest 対応はすべてこのパッケージが持つ。

## 設計方針

- **元のアセットは変更しない。** VRCExpressionsMenu は編集前にツリーごと複製し、NDMF の
  `IAssetSaver` に登録する (複製は 1 度だけで、以降は複製済みのものを再利用する)。
  AnimatorController は NDMF の `AnimatorServicesContext` (`VirtualAnimatorController`) を経由して
  編集する。これは実ビルドと NDMF Preview の両方で同じ形で提供される、すでに独立して複製済みの
  オブジェクトグラフなので、呼び出し側が別途複製を意識する必要はない。
- **Modular Avatar の実行順序に依存しない。** MA のメニュー合成・アニメーター合成はどちらも
  `BuildPhase.Transforming` で走り、しかも参照そのものを差し替える形なので、前後どちらに置いても
  タイミングだけで安全に連携する方法が無い。そこで ReFrame は `Resolving` フェーズで
  **MA が読みに行くマージ前のソース**を直接編集する (詳細は後述)。
- **判定は安全側に倒す。** 実体の掃除は「参照が 1 つでも見つかったら残す」。メッシュ・シェイプキー・
  マテリアルの削減には手を出さない (AvatarOptimizer の担当領域)。

## 依存

- nadena.dev.ndmf
- nadena.dev.modular-avatar
- com.vrchat.avatars
- (任意) com.anatawa12.avatar-optimizer — 型の有無だけを見るので未導入でも動く
- (任意) com.github.kurotu.vrc-quest-tools — Harmony パッチの対象。未導入なら黙って何もしない

---

## 構成

### `jp.illusive_isc.ReFrameCoreRuntime` (Runtime/)

`UnityEditor` に依存しない。ビルド中・実行時のどちらからも呼べる。

| ファイル | 役割 |
|---|---|
| `ReFrameUtil.cs` | オブジェクト取得と、オブジェクト 1 つ分の状態変更 (active / enabled / BlendShape / Material / Transform)、VRCExpressionParameters のパラメーター削除 |
| `ReFrameDeleteComponent.cs` | 削除データを宣言するコンポーネントの共通基底。属性を読んで削除対象を列挙する `Enumerate*` 群と、Quest 非対応コンポーネントの判定を持つ |
| `ReFrameDeleteEntry.cs` | `[ReFrameDelete]` を付けるフィールドの型 (`Enabled` + `Value`) |
| `ReFrameSweepMode.cs` | 実体の片付けを ReFrame 自身がやるか AAO に任せるかの選択 |
| `ReFrameParameterType.cs` | Inspector の Value 欄の表示形式を明示するための列挙 |
| `ReFrame*Attribute.cs` (16 個) | 宣言用の属性群 (下記の一覧を参照) |

### `jp.illusive_isc.ReFrameCoreEditor` (Editor/)

| ファイル | 役割 |
|---|---|
| `ReFrameDeletePass.cs` | NDMF プラグイン定義 (`IllusoryOverride.ReFrameCore.Delete`) と、削除の本体パス |
| `ReFrameLayerRenamePass.cs` | `[ReFrameLayerRename]` に従い、削除より前にレイヤー名を一意化する |
| `ReFrameSweepPass.cs` | 二度と有効化されない GameObject / Renderer、使われなくなったボーン、誰も読まない Driver、動かす相手を失ったレイヤーと揺れ物を実際に破棄する |
| `ReFrameAnimatorUtil.cs` | AnimatorController の非破壊編集 (値ベースの削除・ベイク・名指し削除・刈り取り) |
| `ReFrameMenuUtil.cs` | VRCExpressionsMenu の非破壊編集 |
| `ReFrameBlendUtil.cs` | ベイク時に複数クリップの値を合成するためのバッファとプール |
| `ReFrameCoveredMeshCutter.cs` | `[ReFrameCutCovered]` — 服に覆われて見えない体のポリゴンを切り取る。マスクを焼く `Bake()` は**意図的に呼び出し元を持たない** (焼き直せるのはパッケージの作者だけなので、ボタンを配布物に置かない。手順はメソッドのコメント) |
| `ReFrameQuestMaterialConverter.cs` | Quest 非対応シェーダーのマテリアルを `VRChat/Mobile/Toon Lit` へ焼いて差し替える |
| `ReFrameQuestAudit.cs` | Quest 基準の一発アウト / 上限超過を判定する |
| `ReFramePhysBoneCatalog.cs` | 揺れ物の一覧を作る / パスキーから実体を引く |
| `ReFrameDeletionMarker.cs` | クローンを 1 体作って実ビルドを通し、消えるパス・Quest 監査・揺れ物一覧・Avatar Dynamics の実測値を `SessionState` へ憶える |
| `ReFrameDeleteComponentInspector.cs` | カスタム Inspector 本体 (`[CustomEditor(typeof(ReFrameDeleteComponent), true)]`) |
| `ReFrameMenuGrouping.cs` | Inspector のグループ分けを VRCExpressionsMenu の階層から自動導出する |
| `ReFrameHierarchyPreview.cs` | NDMF Preview の `IRenderFilter` (クラス名は `ReFrameDeletePreview`) |
| `ReFrameSceneVisibilityPreview.cs` | 消える予定の GameObject を Hierarchy から隠す |
| `ReFrameBakedVisibilityResolver.cs` | 上記 2 つのプレビューが共有する解決ロジック (ComputeContext 非依存) |
| `ReFrameDependencyAudit.cs` | 削除対象への外部参照を洗い出す読み取り専用の調査ツール。**現在どこからも呼ばれていない** |
| `UI/Common.uss`, `UI/Inspector/*.uss` | Inspector のデザイントークンとスタイル |

### `jp.illusive_isc.ReFrameCoreHarmonyPatches` (Editor/HarmonyPatches/)

VRCQuestTools の表示を ReFrame の実際の結果へ寄せるための Harmony パッチ。ベンダーのファイルには
一切触れないので、VRCQuestTools を更新しても壊れない (対象メソッドが消えたらパッチが当たらなくなるだけ)。
MA の `PatchLoader` と同じく専用 asmdef + `[InitializeOnLoadMethod]` + リロード前に `UnpatchAll`。

| ファイル | 役割 |
|---|---|
| `ReFrameHarmonyPatchLoader.cs` | パッチの入り口 |
| `VRCQuestToolsUnsupportedComponentsPatch.cs` | 「Quest 非対応コンポーネントがあります」の警告から、ReFrame がビルド時に取り除くぶんを差し引く |
| `VRCQuestToolsDynamicsPatch.cs` | Avatar Dynamics の推定値を `ReFrameDeletionMarker` の実ビルド値へ差し替える |

---

## ビルドの流れ

```
BuildPhase.Resolving
  ├ ReFrameLayerRenamePass     レイヤー名の一意化 (名前指定の削除より必ず先)
  └ ReFrameDeletePass          削除の本体。PreviewingWith(ReFrameDeletePreview)
BuildPhase.Optimizing
  └ ReFrameSweepPass           実体 (GameObject / ボーン / レイヤー) の掃除
```

掃除が `Optimizing` なのは、MA の Merge Armature / Merge Animator が `Transforming` で走るためで、
それより前だとボーンの最終的な位置も「どのメッシュがどのボーンを使うか」も確定していない。
`ReFrameDeleteComponent` は削除パスの最後に破棄されるので、掃除の可否だけ `ReFrameSweepRequest`
として `context.GetState` に残して受け渡す。

アバター個別パッケージが独自パスを足す場合は、`.AfterPlugin("IllusoryOverride.ReFrameCore.Delete")`
で順序を宣言する (ReFrameForIKUSIA の `KaguyaLayerMergePass` が実例)。

### `ReFrameDeletePass` が編集するもの

- アバター本体の `expressionsMenu` (ツリーごと複製してから編集)
- 各 `ModularAvatarMenuInstaller` の `menuToAppend` (MA がメニューを組み立てるときに読むソース) と
  `installTargetMenu` (複製後の参照へ張り替える)
- アバター本体の FX / Gesture コントローラーと、同じ `layerType` の `ModularAvatarMergeAnimator` が
  持つマージ前のコントローラー — `AnimatorServicesContext.ControllerContext.Controllers` の
  `VirtualAnimatorController` を直接編集する
- `VRCExpressionParameters` (複製してから削除・重複除去・`networkSynced` の変更)
- `ModularAvatarParameters` の宣言 (ここを消さないと MA が後から同名パラメーターを復活させる)
- シーン上の GameObject / ParticleSystem / マテリアル / PhysBone (名指し削除・上限圧縮・Quest 対応)

### 対象にするプレイアブルレイヤー

値ベースの削除・BlendTree 名指し削除・レイヤー削除の対象は **FX と Gesture** の 2 つ
(`ReFrameDeletePass.ProcessedLayerTypes`)。Gesture を含めているのは、メニューから操作するギミックの
パラメーターが Gesture 側にしか存在しないケースがあるため。**Base (Locomotion) と Action は対象外** —
移動と大型エモートの土台で、値を固定すると影響が読みにくいうえ、削除対象のギミックがそこに
置かれている実例が無い。

ただし次の 2 つは**レイヤーの種類を問わず全コントローラー**を見る。結論がレイヤーの種類で変わらず、
実際に対象となるのが Base に取り残されたケースだから。

- `[ReFrameCutUndrivenTransitions]` の処理 (`CutUndrivenTransitions`)
- `ReFrameSweepPass` の Driver 掃除とレイヤー掃除

---

## `ReFrameDeleteComponent` の設定項目

属性による宣言とは別に、コンポーネント自身が持つユーザー設定。

| フィールド | 説明 |
|---|---|
| `previewHiddenInHierarchy` | 消える予定の GameObject を Hierarchy から隠す (見た目確認用、ビルドに影響しない) |
| `sweepMode` | 実体の片付けを ReFrame がやるか (`Sweep`、既定)、AAO に任せるか (`LeaveToAvatarOptimizer`) |
| `deleteQuestUnsupportedComponents` | 「Quest 対応」。Quest で動かないコンポーネントを取り除き、シェーダー / パーティクルマテリアルを変換し、Quest 監査と揺れ物一覧を出す |
| `cutCoveredMesh` | `[ReFrameCutCovered]` を宣言しているアバターで、服に覆われた体のポリゴンを切り取る |
| `questMaxTextureSize` | Quest 用に焼くテクスチャの一辺の上限 (既定 1024) |
| `deletedPhysBones` / `deletedPhysBoneColliders` | 一覧から選んで消す揺れ物のパスキー (`"path#index"`)。どれを捨てるかはアバターの構造上の事実ではなく使う人の好みなので、属性ではなく設定として持つ |

---

## 宣言用の属性一覧

`ReFrameDeleteEntry` 型のフィールド、またはコンポーネントのクラス自体に付ける。
すべて `jp.illusive_isc.ReFrame.Core`。

### フィールドに付けるもの

| 属性 | 役割 |
|---|---|
| `[ReFrameDelete(string parameterName, ReFrameParameterType type = Auto)]` | このフィールドが固定・削除するパラメーター。複数可 |
| `[ReFrameDeleteObject(string path, float onlyWhenValue = …)]` | 実体の GameObject を名指しで破棄する。`Always` で Enabled を見ない、`RequiresAll` で「指定した全パラメーターが削除されるときだけ」 |
| `[ReFrameDeleteLayer(string name, float onlyWhenValue = …)]` | AnimatorController のレイヤーを丸ごと取り除く |
| `[ReFrameDeleteState(string layerName, string stateName, float onlyWhenValue = …)]` | 名指しでステートを取り除く |
| `[ReFrameDeleteRelatedBlendTree(string name)]` | パラメーターと無関係に、名前で BlendTree ノードを削除する。`Bake` で削除前にデフォルト値の枝を焼き付ける、`Always` で Enabled を見ない、`OnlyWhenValue` で値を限定 |
| `[ReFrameBlendTreeOverride(string treeName, string parameterName, float value)]` | 「このツリーの中だけこのパラメーターをこの値とみなす」構造上の例外宣言。Enabled を見ない |
| `[ReFrameCutTransitions(string parameterName = null, float onlyWhenValue = …)]` | 条件を抜くのではなく遷移ごと消す |
| `[ReFrameCutUndrivenTransitions(string parameterName)]` | 値を書き込む主体が 1 つも残っていないなら、それを条件にする遷移を消す。`EvaluateAsFixed` で「遷移ごと」ではなく「固定値で評価して成立しない条件だけ」に切り替える。Enabled を見ない |
| `[ReFrameUnsyncParameter(string parameterName = null)]` | ギミックは残したまま `networkSynced` / MA の `localOnly` だけを切って同期ビットを空ける。Enabled を見ない |
| `[ReFrameSetMaxParticles(string path, int max)]` | ParticleSystem の `maxParticles` を下げる (下げるだけで上げない)。`Always` で Enabled を見ない |
| `[ReFrameQuestParticleMaterial(string path, ReFrameQuestParticleBlend blend = Additive)]` | パーティクルのマテリアルを Quest 対応シェーダーへ差し替える。ゲートは「Quest 対応」チェックだけで Enabled は見ない |
| `[ReFrameMenuOnly(float fixedValue = 0f)]` | メニュー / VRCExpressionParameters 側だけ削除し、AnimatorController には触れない。`Value` は常にこの固定値 |
| `[ReFrameValueLocked(float fixedValue = 0f)]` | 削除は通常どおり行うが `Value` を固定し、Inspector から選べなくする |
| `[ReFrameBundleMember(string representativeParameterName)]` | 代表フィールドが「ギミックごと消える」設定なら、自身の `Enabled` が false でも道連れで削除される |
| `[ReFrameReverse]` | 「OFF に見える値」が 1 側であることを示す (道連れ判定と Inspector の表示に効く) |
| `[ReFrameMenuGroup(params string[] path)]` | メニューに出てこないフィールドの Inspector 上の置き場所を指定する |

`[ReFrameMenuOnly]` と `[ReFrameValueLocked]` はどちらか一方だけを付ける (両方あれば MenuOnly が優先)。
Quest 対応による強制削除は、どちらの固定よりさらに優先して OFF 側に倒す。

### クラスに付けるもの

| 属性 | 役割 |
|---|---|
| `[ReFrameTheme(string styleSheetPath)]` | Inspector の配色トークンを上書きする uss のパス。Core → アバター固有パッケージの逆依存を避けるため、具象クラス側で宣言する |
| `[ReFrameLayerRename(string mergeAnimatorObjectName, string from, string to)]` | MA Merge Animator が持つコントローラーのレイヤー名を、削除より前に改名して一意にする。対象の指定はコントローラーのアセット名ではなく **MergeAnimator が付いた GameObject の名前** |
| `[ReFrameCutCovered(string bodyPath)]` | 服に覆われた体のポリゴンを切り取る対象。`Covers` に覆う側、`MaskAsset` に計算済みマスクの保存先、`MaxDistance` にレイの長さ |

---

## `ReFrameUtil` (Runtime, `jp.illusive_isc.ReFrame.Core`)

アバタールートからの相対パス (オブジェクト名の文字列配列) でオブジェクトを指定して状態を変更する。
`Transform.Find` を使わず自前で子を走査しているため、オブジェクト名に `/` を含んでいても正しく動く。

| メソッド | 説明 |
|---|---|
| `Transform Find(Transform root, params string[] path)` | 相対パスで取得。見つからなければ `null` |
| `Transform FindChild(Transform parent, string name)` | 直下の子を名前 1 つで取得 |
| `bool SetActive(GameObject obj, bool active)` | |
| `bool SetEnabled(Component component, bool enabled)` | `Behaviour` / `Renderer` / `Collider` に対応。それ以外の型なら `false` |
| `bool SetBlendShapeWeight(SkinnedMeshRenderer r, string name, float weight)` | 名前で引いてウェイトを変更 |
| `bool SetMaterial(Renderer renderer, int index, Material material)` | 指定インデックスのマテリアルを変更 |
| `bool SetLocalPositionAxis(Transform t, int axis, float value)` | `localPosition` の 1 軸 (0=x, 1=y, 2=z) |
| `bool SetLocalScaleAxis(Transform t, int axis, float value)` | `localScale` の 1 軸 |
| `bool SetLocalEulerAnglesAxis(Transform t, int axis, float value)` | `localEulerAngles` の 1 軸 (Euler 補間のカーブ用) |
| `bool SetLocalRotationComponent(Transform t, int component, float value)` | `localRotation` の 1 成分 (0=x, 1=y, 2=z, 3=w) |
| `VRCExpressionParameters.Parameter GetParameter(VRCExpressionParameters p, string name)` | 名前で検索 |
| `int RemoveParameter(VRCExpressionParameters p, params string[] names)` | 名前配列で一括削除。削除件数を返す |

Transform 系が軸ごとに分かれているのは、AnimationClip の Transform カーブが軸ごとに別カーブとして
保存されているため (カーブが無い軸は変更しない)。

---

## `ReFrameMenuUtil` (Editor, `jp.illusive_isc.ReFrame.Core.Editor`)

`VRCExpressionsMenu` の各サブメニューはそれぞれ独立したアセットなので、ルートだけ複製して編集すると
元のサブメニューアセットを直接書き換えてしまう。このクラスは **ツリー全体を再帰的に複製**してから編集する。

| メソッド | 説明 |
|---|---|
| `VRCExpressionsMenu CloneMenuTree(VRCExpressionsMenu source, IAssetSaver saver, Dictionary<…> cache = null)` | ツリー全体を複製して `IAssetSaver` に登録する。ネスト・共有・循環参照されたサブメニューも 1 回だけ複製され、参照は複製側へ張り替わる。すでに一時アセットなら再複製しない |
| `VRCExpressionsMenu ReplaceMenuWithClone(BuildContext context, Dictionary<…> cache = null)` | アバターの `expressionsMenu` を複製ツリーに差し替え、複製したルートを返す |
| `int RemoveControlsByParameter(VRCExpressionsMenu menu, params string[] parameterNames)` | 指定パラメーターのいずれかを参照するコントロールを再帰的に削除する。Button/Toggle の `parameter` と、各種 Puppet の `subParameters` の両方を見る |
| `int PruneEmptySubMenus(VRCExpressionsMenu menu)` | 一番奥の階層から調べ、コントロールが 0 件になったサブメニューへの参照を削除する |
| `int PruneEmptySubMenus(VRCExpressionsMenu menu, ISet<VRCExpressionsMenu> protect)` | `protect` に含まれるサブメニューは 0 件でも「空」と判定しない |
| `VRCExpressionsMenu FindSubMenu(VRCExpressionsMenu menu, string name)` | 名前でサブメニューを取得 |

`VRCExpressionsMenu` を直接渡す版は、**すでに複製済みのツリー**を渡すこと (元アセットを直接渡すと壊れる)。
`BuildContext` を渡すオーバーロードは内部で自動的に `ReplaceMenuWithClone` を呼ぶが、
`ReFrameDeletePass` は複製キャッシュを自分で管理する必要があるため使っていない。

`PruneEmptySubMenus` は子を先に処理する**後行順の再帰**なので、削除によって親も空になれば連鎖して
消え、1 回のツリー走査で完結する。**削除処理をすべて終えた後、最後に 1 回だけ**呼ぶこと。

`protect` が要るのは MA との兼ね合い。`ModularAvatarMenuInstaller` の `installTargetMenu` は
実際のコントロールが合成されるのが `Transforming` フェーズなので、`Resolving` で動く ReFrame から見ると
**まだ中身が空**。素直に「空だから消してよい」と判定すると、マージ後に中身が入るはずのサブメニューが
ルートメニューから丸ごと消える。

---

## `ReFrameAnimatorUtil` (Editor, `jp.illusive_isc.ReFrame.Core.Editor`)

AnimatorController から、指定したパラメーターを**実際の値に固定した状態**でアニメーションを削除する。
単純に枝を消すのではなく、その値で本来再生されていたはずの状態をできる範囲でオブジェクトに再現する。

コントローラーの取得・複製は一切行わない。呼び出し元 (`ReFrameDeletePass`) が
`[DependsOnContext(typeof(AnimatorServicesContext))]` で取得した `VirtualAnimatorController` を渡す。
実ビルドでも NDMF Preview でも同じコードパスで動く。

### 値ベースの削除

```csharp
public readonly struct ParameterTarget
{
    public readonly string Name;
    public readonly float Value;
    public readonly bool CutTransitions;   // [ReFrameCutTransitions]
}

public static bool RemoveParameters(
    VirtualAnimatorController controller,
    Transform bakeRoot,
    Dictionary<Transform, bool> bakedActiveStates,
    IReadOnlyDictionary<string, float> treeOverrides,
    params ParameterTarget[] targets);
```

対象パラメーターごとに行われること:

1. **BlendTree (Simple1D)** — `Value` に一致する `threshold` の枝だけを残す
   (bool は非 0 判定、int は四捨五入一致、float は近似一致。範囲外は Unity と同じくクランプ)。
   - 一致した枝が末端の `AnimationClip` なら、その**先頭キーの値**を `ReFrameUtil` 経由で
     `bakeRoot` 上のオブジェクトへ反映してから削除する。
   - 一致した枝がさらに `BlendTree` なら、その子ツリーをこのノードの位置へ**繰り上げる**。
   - 一致する枝が無ければ警告を出してこのノードを削除する。
2. **BlendTree (2D 系)** — X/Y 両軸のパラメーターが同時に固定対象になっている場合に限り、
   その (x, y) に厳密に一致する子を 1 つ選ぶ (連続的なブレンドの再現はしない)。
   片方の軸しか固定されていなければ対象外 (警告のみ)。
3. **BlendTree (Direct)** — 専用ロジックは持たず、子を素通りで再帰するだけ。
4. **遷移** — その条件を**固定値で実際に評価する**。成立しえない条件を持つ遷移は (他の条件が何本
   残っていても) 遷移ごと削除し、常に成立する条件はその条件だけ取り除く。条件が空になり `ExitTime` も
   無い遷移は遷移ごと削除。`CutTransitions` が true なら評価せず無条件に遷移ごと削除する。
5. **ステート** — speed / time / mirror / cycleOffset にそのパラメーターを使っていたら無効化する。
6. **パラメーター定義**そのものも削除する。

サブステートマシン・AnyState・Entry 遷移もすべて再帰的に処理される。
今通っている BlendTree ノード自身の `BlendParameter` が (今回の対象かどうかに関わらず) 同時に
固定されていれば、その値で枝を絞り込む — 兄弟ブランチの値が混ざって処理順に依存した不定な結果に
なるのを防ぐため。

### その他の公開メソッド

| メソッド | 説明 |
|---|---|
| `Dictionary<string,float> BuildTreeOverrides(…)` | `[ReFrameBlendTreeOverride]` の宣言を内部形式へ変換する |
| `bool RemoveNamedBlendTrees(controller, bakeRoot, bakedActiveStates, IEnumerable<RelatedBlendTreeTarget>)` | 名前で BlendTree ノードを削除する。`Bake` が true なら削除前にデフォルト値で焼き付ける |
| `int RemoveNamedStates(controller, (string LayerName, string StateName)[])` | 名指しでステートを取り除く |
| `int RemoveNamedLayers(controller, params string[] names)` | 名指しでレイヤーを取り除く |
| `bool PruneEmptyBlendTrees(controller)` | 子が 0 になった BlendTree とステートを刈る |
| `bool CollapseSingleChildBlendTrees(controller)` | 子が 1 つだけになった「通り道」のツリーを繰り上げる |
| `int PruneUnreachableStates(controller)` | 遷移を辿れなくなったステートを刈る |
| `void CollectDriverWrittenParameters(controller, HashSet<string>)` | `VRCAvatarParameterDriver` が書き込むパラメーター名を集める |
| `int RemoveTransitionsByParameter(controller, params string[])` | そのパラメーターを条件に使う遷移を丸ごと削除する |
| `int CleanTransitionsByFixedValue(controller, params string[])` | 固定値 0 として評価し、成立しない条件を持つ遷移だけ削除する |
| `bool StripDanglingStateReferences(sm, HashSet<VirtualState>)` | 取り除いたステートを指したままの遷移・DefaultState を後始末する |

**レイヤーの削除は必ず Virtual API (`VirtualAnimatorController.RemoveLayers`) 経由で行うこと。**
`VRCAnimatorLayerControl` は対象レイヤーを**インデックス**で指しているので、実体の
`AnimatorController.layers` を直接いじると後ろのレイヤーが全部ずれる。NDMF は仮想化の時点で
物理インデックスを仮想レイヤー ID に置き換えており、コミット時に計算し直してくれる。

---

## 既知の制限

- **Base (Locomotion) / Action レイヤーの値ベース削除は対象外。** これらに定義されたパラメーターは
  VRCExpressionParameters とメニューからは消えるが、BlendTree / 遷移には手が入らない。
- **BlendTree の Direct** に専用ロジックが無い (子を素通りで再帰するだけ)。
- **Simple1D の真の補間** (閾値のどれにも一致しない float 値) は再現しない。クランプ挙動の範囲外なら
  最寄りの閾値の子を選ぶ。
- **ベイクで再現できるカーブの種類が限られる。** 対応しているのは GameObject の `m_IsActive`、
  `blendShape.*`、Transform の position / scale / rotation / eulerAngles、`m_Enabled`、
  マテリアルの**差し替え** (`m_Materials.Array.data[n]`) のみ。**シェーダープロパティの直接
  アニメーション** (`material._Foo`) と **Constraint の `m_Sources[n].weight`** は未対応。
- **`Behaviour.enabled` は実ビルドのベイクには効くが、Scene view のプレビューには反映されない**
  (プレビュー側が Renderer 限定のため)。
- **メッシュ・シェイプキー・マテリアル・ボーンの削減は担当外。** `[ReFrameCutCovered]` による
  ポリゴン切り取りと Quest 用のシェーダー変換以外は AvatarOptimizer に任せる。

## 突き合わせビルド (`ReFrameDeletionMarker`) の走らせ方

プレビューの近似は掃除パスの到達可能性解析までは再現できない。そこでクローンを 1 体作って
本物のビルドを通し、消えたパス・Quest の判定・揺れ物の一覧・Avatar Dynamics の実測値を
まとめて `SessionState` に憶える。

**走るのは Inspector の「実際の結果を算出」ボタンを押したときだけ。**
`ReFrameDeletionMarker.Recompute` の呼び出し元はそのボタン 1 つに限られる。

```
Inspector の「実際の結果を算出」
        ↓
ReFrameDeletionMarker.Recompute
        ↓
Compute()                        クローン + AvatarProcessor.ProcessAvatar + 4 つの結果を保存
        ↓
ReFrameSceneVisibilityPreview.RefreshAll + _markRefreshers を全部呼び直す
```

**自動では走らせない。** 設定変更を検知して裏で回す方式を 2026-09-07 に実装して試したが、
kaguya の実測で 1 回 **27 秒**かかり、編集のたびにエディタが止まって使い物にならなかったため
撤回した。設定を変えると印は古いままになるが、その状態は Inspector に出る (「表示は前回の
設定の結果です」) ので、数字を見たくなった時点で押してもらう。

結果は**設定の組み合わせごと**に憶えるので、一度算出した設定へ戻ってきたときは押し直さなくてよい。

### 「算出済みか」は結果の中身で判定しない

完了そのものを表す entry (`ReFrame.Computed.*`) を結果とは別に置いている。結果の中身が空になる
設定は普通にあるからで (何も削除しなければ「消えるパス」は 0 件、Quest の指摘が無ければ判定も
0 件)、中身の有無で判定すると「まだ算出していない」と取り違える。

とくに `SessionState.GetString(key, null)` は**消えた値を `""` で返すことがある**。
`stored == null` で未算出を判定していた `GetAudit` / `GetPhysBones` が空リストを返しており、
呼び出し側が「0 件だから何も出さない」と解釈して UI が無言で消えていた (2026-09-07 に修正)。

## 既知の不具合 (2026-09-07 時点)

- **`ReFrameDeletePass` で削除対象が 0 件のとき、`DeleteQuestUnsupportedComponents` と
  `CutUndrivenTransitions` が実行されない。** どちらも Enabled を見ない構造宣言のはずなので、
  「1 つも選んでいないときだけ効かない」状態になっている。
- **`ReFrameDeletionMarker.StripNonFxControllers` が Gesture も外す。** 突き合わせビルドの
  高速化のために FX 以外を外しているが、`ProcessedLayerTypes` に Gesture が加わったので
  「FX 以外を外しても結果は変わらない」という前提が崩れている。
- **`ReFrameDependencyAudit` はどこからも呼ばれていない。** 一回きりの調査に使ったツールなので、
  残すなら `[MenuItem]` を付ける、使わないなら削除する。
