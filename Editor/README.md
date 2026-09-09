# ReFrameCore/Editor の構成 (2026-09-10 に機能ごとへ分割)

- `Passes/` — NDMF のビルドパス: ReFrameDeletePass (プラグイン定義・削除本体・VariantSelect) / LayerRenamePass / NetworkIdPass / SweepPass。
- `Inspector/` — ReFrame コンポーネントの Inspector と、その部品 (MenuGrouping / PhysBoneCatalog / PreviewSide / PlatformSwitch)。
- `Preview/` — 編集中のプレビュー: HierarchyPreview (NDMF IRenderFilter) / SceneVisibilityPreview / QuestMaterialPreview / BakedVisibilityResolver (焼き付け結果の解決)。
- `Quest/` — Quest 簡易対応: QuestMaterialConverter (Toon Lit 焼き込み) / QuestBakeSetBuilder (置き場) / QuestAssetTrim / QuestAudit。
- `Avatar/` — アバター自体の変更: CoveredMeshCutter (服に覆われた面の切り取り)。
- `Animator/` — アニメーター・メニュー・パラメーターの共通処理: AnimatorUtil / BlendUtil / MenuUtil / ParameterLink / DependencyAudit。
- `HarmonyPatches/` — VRCQuestTools への割り込み (専用 asmdef)。
- `Shaders/` — 焼き込みとプレビュー用のシェーダー (Shader.Find で名前参照)。
- `UI/` — uss (Inspector からパスで参照するので動かさない)。

名前空間はすべて `jp.illusive_isc.ReFrame.Core.Editor` のまま (フォルダ分けだけ)。
