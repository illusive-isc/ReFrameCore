using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>
    /// [ReFrameDelete] を付けた Bool 型 (ON/OFF タイル) の行に併用する。Value 欄の「ON」「OFF」の代わりに
    /// 出す表示名を、生の値 (0 / 1) ごとに指定する。[ReFrameReverse] の有無にかかわらず生の値で引く。
    /// 例: 頭上固定 (1) / 地上 (0) のように、ON/OFF では意味が伝わらない 2 択に使う。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ReFrameValueLabelsAttribute : Attribute
    {
        /// <summary>生の値が 0 のときの表示名。</summary>
        public string ZeroLabel { get; }

        /// <summary>生の値が 1 のときの表示名。</summary>
        public string OneLabel { get; }

        public ReFrameValueLabelsAttribute(string zeroLabel, string oneLabel)
        {
            ZeroLabel = zeroLabel;
            OneLabel = oneLabel;
        }
    }
}
