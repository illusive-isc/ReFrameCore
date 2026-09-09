namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>「Quest 対応」をどのプラットフォームで効かせるか。</summary>
    public enum ReFrameQuestScope
    {
        /// <summary>プラットフォームに関わらず効かせる。</summary>
        [UnityEngine.InspectorName("常に (PC のビルドでも Quest 化する)")]
        Always = 0,

        /// <summary>ビルドターゲットが Android のときだけ効かせる。</summary>
        [UnityEngine.InspectorName("Quest & Android のときだけ")]
        AndroidOnly = 1,
    }
}
