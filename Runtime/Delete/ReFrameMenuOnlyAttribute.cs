using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>このフィールドを常に「メニューのみ削除」(MenuOnly 相当) に 固定し、Enabled 以外の選択肢 (MenuOnly トグル・Value 入力) を Inspector 上から隠す。</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ReFrameMenuOnlyAttribute : Attribute
    {
        /// <summary>ビルド時にこのフィールドが削除する全パラメーターへ固定する値。</summary>
        public float FixedValue { get; }

        public ReFrameMenuOnlyAttribute(float fixedValue = 0f)
        {
            FixedValue = fixedValue;
        }
    }
}
