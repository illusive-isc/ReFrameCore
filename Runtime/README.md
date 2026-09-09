# ReFrameCore/Runtime の構成 (2026-09-10 に機能ごとへ分割)

- `Core/` — コンポーネント本体 (ReFrameDeleteComponent / ReFrameDeleteEntry) と共通の型 (ParameterType / SweepMode / PlatformSlot / Util)。
- `Delete/` — ギミック削除の宣言 (行に付ける属性): ReFrameDelete / DeleteObject / DeleteLayer / DeleteState / DeleteRelatedBlendTree / DeleteWhenAllGone / CutTransitions / CutUndrivenTransitions / BlendTreeOverride / BundleMember / MenuOnly / ValueLocked / Reverse / UnsyncParameter / MenuRemove / LayerRename / SetMaxParticles。
- `Avatar/` — アバター自体の変更 (体型・メッシュ): ReFrameBlendShape / ApplyToAvatar / CutCovered。
- `Inspector/` — 表示だけに関わる宣言: Label / MenuGroup / GroupLabel / GroupOrder / Theme / PhysBoneGroup。
- `Avatars/IKUSIA/` — IKUSIA 系アバター共通の土台 (ReFrameIKUSIA / IKUSIACommonReFrame: 表情・姿勢・エモートなど共通の行)。アバター個別の宣言は別パッケージ (reframe-kaguya / reframe-rurune)。
- `Quest/` — Quest 簡易対応: QuestBake / QuestBakeSet / QuestBlendBackdrop / QuestBrightness / QuestCutTransparent / QuestDropMaterial / QuestForceDelete / QuestOutline / QuestParticleMaterial / QuestScope / QuestTint / QuestVariant / MenuIconMode。

名前空間はすべて `jp.illusive_isc.ReFrame.Core` のまま (フォルダ分けだけ)。
