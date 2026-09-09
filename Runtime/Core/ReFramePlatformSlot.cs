using System;
using System.Collections.Generic;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>アップロード先のプラットフォーム。</summary>
    public enum ReFrameBuildPlatform
    {
        /// <summary>PC (Windows)。</summary>
        PC = 0,

        /// <summary>Quest / Android。</summary>
        Android = 1,
    }

    /// <summary>1 つのギミック行の状態。</summary>
    [Serializable]
    public struct ReFrameEntryState
    {
        /// <summary>対象のフィールド名。</summary>
        public string Field;

        public bool Enabled;
        public float Value;
    }

    /// <summary>PC 用の設定そのもの。</summary>
    [Serializable]
    public class ReFramePlatformSlot
    {
        /// <summary>中身が入っているか。</summary>
        public bool Captured;

        /// <summary>ギミック行 (ReFrameDeleteEntry フィールド) の状態。</summary>
        public List<ReFrameEntryState> Entries = new();

        /// <summary>削除する PhysBone ("path#index")。</summary>
        public List<string> PhysBones = new();

        /// <summary>削除する PhysBoneCollider ("path#index")。</summary>
        public List<string> PhysBoneColliders = new();

        /// <summary>服に覆われた面を切るか。</summary>
        public bool CutCoveredMesh;
    }

    /// <summary>Quest 用に「PC からどこを変えるか」だけを持つ差分。</summary>
    [Serializable]
    public class ReFrameQuestDiff
    {
        /// <summary>一度でも Quest 用として編集したか。</summary>
        public bool Captured;

        /// <summary>PC と違う値になっている行だけ。</summary>
        public List<ReFrameEntryState> Entries = new();

        /// <summary>PC では残すが、Quest では追加で消す PhysBone。</summary>
        public List<string> AddedPhysBones = new();

        /// <summary>PC では消すが、Quest ではあえて残す PhysBone。</summary>
        public List<string> KeptPhysBones = new();

        /// <summary>PC では残すが、Quest では追加で消す PhysBoneCollider。</summary>
        public List<string> AddedPhysBoneColliders = new();

        /// <summary>PC では消すが、Quest ではあえて残す PhysBoneCollider。</summary>
        public List<string> KeptPhysBoneColliders = new();

        /// <summary>覆われた面の切り取りを PC と変えているか。</summary>
        public bool CutCoveredMeshOverridden;

        /// <summary>変えている場合の値。</summary>
        public bool CutCoveredMesh;

        /// <summary>差分が空か (Quest と PC で違いが無いか)。</summary>
        public bool IsEmpty =>
            Entries.Count == 0
            && AddedPhysBones.Count == 0
            && KeptPhysBones.Count == 0
            && AddedPhysBoneColliders.Count == 0
            && KeptPhysBoneColliders.Count == 0
            && !CutCoveredMeshOverridden;

        /// <summary>Quest 側で変えている項目の数 (Inspector の表示用)。</summary>
        public int Count =>
            Entries.Count
            + AddedPhysBones.Count
            + KeptPhysBones.Count
            + AddedPhysBoneColliders.Count
            + KeptPhysBoneColliders.Count
            + (CutCoveredMeshOverridden ? 1 : 0);
    }
}
