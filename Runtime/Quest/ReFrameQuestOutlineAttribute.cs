using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。</summary>
    /// <code>
    /// [ReFrameQuestOutline("Hair")]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameQuestOutlineAttribute : Attribute
    {
        /// <summary>対象の Renderer (アバタールートからの相対パス)。</summary>
        public string Path { get; }

        /// <summary>輪郭線の太さ (0〜0.5)。</summary>
        public float Thickness { get; set; } = -1f;

        public ReFrameQuestOutlineAttribute(string path)
        {
            Path = path;
        }
    }
}
