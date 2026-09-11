using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。Quest で、指定した BlendShape が動かす三角形 (頭の中から出す半透明パッチなど) をメッシュから切る。</summary>
    /// <code>
    /// [ReFrameQuestCutByBlendShape("Body", "照れ", "Cheek2", "Cheek3", "Cheek4", "ga-n")]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameQuestCutByBlendShapeAttribute : Attribute
    {
        /// <summary>対象の Renderer (アバタールートからの相対パス)。</summary>
        public string Path { get; }

        /// <summary>BlendShape の名前。</summary>
        public string[] Shapes { get; }

        /// <summary>この距離 (m) 以上動く頂点を「動かされる」とみなす。</summary>
        public float Tolerance { get; set; } = 0.001f;

        public ReFrameQuestCutByBlendShapeAttribute(string path, params string[] shapes)
        {
            Path = path;
            Shapes = shapes;
        }
    }
}
