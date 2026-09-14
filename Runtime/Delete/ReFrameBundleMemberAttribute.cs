using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ReFrameBundleMemberAttribute : Attribute
    {
        /// <summary>親として扱うフィールドの [ReFrameDelete] ParameterName。</summary>
        public string RepresentativeParameterName { get; }

        /// <summary>代表に道連れで消えるときも、焼き付ける値は選ばせる。</summary>
        public bool ValueMatters { get; set; }

        /// <summary>true なら、代表の値が「ギミック OFF」の値でなくても、代表が削除中なら常に道連れにする
        /// (例: 胸サイズを中立に固定したら、胸のシェイプ行を全部有効にする)。</summary>
        public bool Always { get; set; }

        public ReFrameBundleMemberAttribute(string representativeParameterName)
        {
            RepresentativeParameterName = representativeParameterName;
        }
    }
}
