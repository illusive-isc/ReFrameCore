using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>
    /// [ReFrameDelete] を付けたフィールドに併用する。値を固定してアニメーションを消すとき、固定した値の枝の
    /// AnimationClip をアバターへ焼き付けない (BlendShape・Transform・コンポーネントの有効/無効などを
    /// クリップの値で上書きしない)。アバターの見た目は ReFrame の他の行 ([ReFrameBlendShape] 等) と
    /// シーンの状態だけで決めたい場合に使う (例: BreastSize は中立に固定し、胸の形はシェイプ行で決める。
    /// アニメーターが一緒に動かす髪の回転や胸 PhysBone の ON/OFF はシーンのまま)。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class ReFrameNoBakeAttribute : Attribute { }
}
