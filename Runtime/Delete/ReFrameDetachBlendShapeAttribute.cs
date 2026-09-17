using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>
    /// 指定した BlendShape をアニメーターから切り離す。全コントローラーのクリップからそのカーブを外し、
    /// 行の固定で焼き付けもしないので、値はメッシュ側 (SkinnedMeshRenderer の Inspector のスライダー) だけで決まる。
    /// 別の機能と同じクリップで一緒に動かされている BlendShape (例: アウター着脱と一緒に動く髪の Outer_on) を、
    /// その機能を固定 / 削除したあとも自分で決めたいときに使う。
    /// 行 (フィールド) にもクラスにも付けられる。行に付けた場合もその行の状態に関係なく常に切り離す。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameDetachBlendShapeAttribute : Attribute
    {
        /// <summary>アバタールートからの SkinnedMeshRenderer のパス (例: "Hair")。</summary>
        public string Path { get; }

        /// <summary>BlendShape 名 (例: "Outer_on")。</summary>
        public string ShapeName { get; }

        public ReFrameDetachBlendShapeAttribute(string path, string shapeName)
        {
            Path = path;
            ShapeName = shapeName;
        }
    }
}
