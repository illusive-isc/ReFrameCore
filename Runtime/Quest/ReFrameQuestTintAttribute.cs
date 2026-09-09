using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrame コンポーネントのクラスに付ける。</summary>
    /// <code>
    /// [ReFrameQuestTint("hair", R = 1.02f, G = 1.04f, B = 1.10f)]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameQuestTintAttribute : Attribute
    {
        /// <summary>対象の Renderer (アバタールートからの相対パス)。</summary>
        public string Path { get; }

        public float R { get; set; } = 1f;
        public float G { get; set; } = 1f;
        public float B { get; set; } = 1f;

        public ReFrameQuestTintAttribute(string path)
        {
            Path = path;
        }
    }
}
