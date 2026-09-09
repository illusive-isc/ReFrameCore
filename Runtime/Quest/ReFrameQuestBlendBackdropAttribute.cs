using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。</summary>
    /// <code>
    /// // 素肌 #FFEAEB を 45% 混ぜて、薄手のストッキングにする
    /// [ReFrameQuestBlendBackdrop("kaguya_cloth/stocking", Opacity = 0.55f, Backdrop = "#FFEAEB")]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameQuestBlendBackdropAttribute : Attribute
    {
        /// <summary>対象の Renderer (アバタールートからの相対パス)。</summary>
        public string Path { get; }

        /// <summary>元の色をどれだけ残すか (0〜1)。</summary>
        public float Opacity { get; set; } = 1f;

        /// <summary>下にあるものの色。</summary>
        public string Backdrop { get; set; } = "#FFFFFF";

        public ReFrameQuestBlendBackdropAttribute(string path)
        {
            Path = path;
        }
    }
}
