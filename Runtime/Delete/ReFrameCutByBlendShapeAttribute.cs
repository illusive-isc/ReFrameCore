using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。行を固定したとき、指定した BlendShape が動かす三角形をメッシュから切る (服の下に隠れる体など)。PC / Quest 共通。</summary>
    /// <code>
    /// [ReFrameCutByBlendShape("Body_b", "Chest2_____胸2_胸元", "UpperArm_____上腕")]
    /// </code>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameCutByBlendShapeAttribute : Attribute
    {
        /// <summary>対象の SkinnedMeshRenderer (アバタールートからの相対パス)。</summary>
        public string Path { get; }

        /// <summary>BlendShape の名前。</summary>
        public string[] Shapes { get; }

        /// <summary>この行の Value がこの値のときだけ切る (既定 1 = ON 固定)。</summary>
        public float OnlyWhenValue { get; set; } = 1f;

        /// <summary>「Quest 簡易対応」が ON のときだけ切る。</summary>
        public bool QuestOnly { get; set; }

        /// <summary>この距離 (m) 以上動く頂点を「動かされる」とみなす。</summary>
        public float Tolerance { get; set; } = 0.001f;

        public ReFrameCutByBlendShapeAttribute(string path, params string[] shapes)
        {
            Path = path;
            Shapes = shapes;
        }
    }
}
