using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>このフィールドの Value を常に FixedValue に固定し、Inspector 上の Value 入力欄 (ON/OFF 選択肢) を隠す。</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ReFrameValueLockedAttribute : Attribute
    {
        /// <summary>ビルド時にこのフィールドが削除する全パラメーターへ固定する値。</summary>
        public float FixedValue { get; }

        /// <summary>Inspector の Value 欄に "<値> 固定" の代わりに出す文言 (例: "下のシェイプで指定")。空なら既定の表示。</summary>
        public string Label { get; set; }

        public ReFrameValueLockedAttribute(float fixedValue = 0f)
        {
            FixedValue = fixedValue;
        }
    }
}
