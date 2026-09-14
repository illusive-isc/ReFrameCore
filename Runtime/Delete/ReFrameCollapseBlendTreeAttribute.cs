using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>
    /// [ReFrameDelete] を付けたフィールドに併用する。値を固定するとき、そのパラメーターで選ぶ 1D BlendTree を
    /// 「固定した値の枝 1 本」(AnimationClip またはサブツリー) に畳むだけにして、枝をアバターへ焼き付けたり
    /// モーションを消したりしない。
    ///
    /// 既定の動きはステート直下の BlendTree で選ばれた枝が AnimationClip なら「シーンへ焼いて消す」で、
    /// これは 1 ステートの FX トグル向け。Base の Locomotion のように <b>他のパラメーター (Upright 等) で
    /// 切り替わるステート</b>のモーションに使うと、Humanoid クリップはシーンへ焼けないのでモーションだけが
    /// 消えてステートが空になる (IKUSIA のしゃがみ / 伏せポーズ固定が実例)。この属性を付けた行の
    /// パラメーターと、その Copy 先 (paryi_change_Crouching → paryi_change_Crouching_M) は、
    /// どの深さのツリーでも枝を選ぶだけで焼かない。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class ReFrameCollapseBlendTreeAttribute : Attribute { }
}
