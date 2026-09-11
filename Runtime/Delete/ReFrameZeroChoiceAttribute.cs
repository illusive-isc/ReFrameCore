using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>
    /// [ReFrameDelete] を付けた Float 型のフィールドに併用する。
    /// メニューのどの項目も 0 を書かない Float (例: 尻尾の見た目 = トグルを全部 OFF にした状態が既定の見た目) で、
    /// 「0 = ギミック OFF」ではなく「0 = どれも選ばない (既定の見た目)」という正規の選択肢として扱わせる。
    /// 付けると Inspector の候補に「<see cref="Label"/> (0)」が常に出て、0 を選んでも「ギミック削除」ではなく
    /// 値固定 + メニュー削除として表示される。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class ReFrameZeroChoiceAttribute : Attribute
    {
        public string Label { get; }

        public ReFrameZeroChoiceAttribute(string label = "選択なし")
        {
            Label = label;
        }
    }
}
