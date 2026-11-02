using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>
    /// ReFrameDeleteComponent を継承するクラスに付ける。「Quest 簡易対応版を作成」のとき、ここに挙げた GameObject 名 (完全一致) の
    /// PhysBone だけを残し、他の PhysBone と、残した PhysBone が使わないコライダーを最初から「消す」に入れておく (後から一覧で変えられる)。
    /// </summary>
    /// <code>
    /// [ReFrameQuestKeepPhysBones("FrontHair_Root", "Tail", "Breast_L", "Breast_R")]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class ReFrameQuestKeepPhysBonesAttribute : Attribute
    {
        /// <summary>残す PhysBone が付いている GameObject の名前。</summary>
        public string[] Names { get; }

        public ReFrameQuestKeepPhysBonesAttribute(params string[] names)
        {
            Names = names ?? new string[0];
        }
    }
}
