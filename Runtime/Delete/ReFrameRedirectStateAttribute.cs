using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>
    /// [ReFrameDelete] を付けたフィールドに併用する。削除 (値の固定) が有効なとき、レイヤー内で
    /// FromState を行き先にしている遷移 (各ステートの Transitions・AnyState・Entry) と既定ステートを
    /// すべて ToState へ繋ぎ変える。
    ///
    /// 用途は「パラメーターを ON に固定して、その枝に常時入っていてほしい」場合。値の固定だけだと
    /// 条件が全部成立した遷移は (Unity では条件なし・ExitTime なしの遷移が動かないため) 削除され、
    /// 枝へ入る道が消える。そこで入口 (ハブになっているステート) ごと枝の先頭へ付け替え、
    /// 元のハブとそこからしか行けないステートは到達不能として掃除に任せる。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameRedirectStateAttribute : Attribute
    {
        /// <summary>対象のレイヤー名 (完全一致)。</summary>
        public string LayerName { get; }

        /// <summary>繋ぎ変え元 (これを行き先にしている遷移と既定ステートが対象) のステート名 (完全一致)。</summary>
        public string FromState { get; }

        /// <summary>繋ぎ変え先のステート名 (完全一致)。</summary>
        public string ToState { get; }

        /// <summary>指定した場合、このフィールドの Value がこの値のときだけ繋ぎ変える。</summary>
        public float? OnlyWhenValue { get; }

        public ReFrameRedirectStateAttribute(string layerName, string fromState, string toState)
        {
            LayerName = layerName;
            FromState = fromState;
            ToState = toState;
            OnlyWhenValue = null;
        }

        public ReFrameRedirectStateAttribute(string layerName, string fromState, string toState, float onlyWhenValue)
        {
            LayerName = layerName;
            FromState = fromState;
            ToState = toState;
            OnlyWhenValue = onlyWhenValue;
        }
    }
}
