using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>「削除したギミックの実体 (GameObject とボーン) を誰が片付けるか」の選択。</summary>
    public enum ReFrameSweepMode
    {
        /// <summary>ReFrame が自分で片付ける (既定)。</summary>
        [InspectorName("あらかじめ削除する")]
        Sweep,

        /// <summary>実体には手を触れず、パラメーター・メニュー・AnimatorController の整理だけを行う。</summary>
        [InspectorName("AAO にお任せする")]
        LeaveToAvatarOptimizer,
    }
}
