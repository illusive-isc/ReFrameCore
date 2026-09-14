using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>
    /// 同じ見出しの中での行の並び順。付けない行は 1000 扱いで、宣言順 (派生クラスの分が基底より先) のまま。
    /// 小さい値ほど上。道連れ ([ReFrameBundleMember]) の行は代表と一緒に動く。
    /// <code>
    /// [ReFrameRowOrder(0)]   // 見出しの先頭に
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ReFrameRowOrderAttribute : Attribute
    {
        public int Order { get; }

        public ReFrameRowOrderAttribute(int order) => Order = order;
    }
}
