using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。</summary>
    /// <code>
    /// [ReFrameQuestCutTransparent("Body", 2)]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameQuestCutTransparentAttribute : Attribute
    {
        /// <summary>対象の Renderer (アバタールートからの相対パス)。</summary>
        public string Path { get; }

        /// <summary>対象のマテリアル枠 (サブメッシュ) の番号。</summary>
        public int Slot { get; }

        /// <summary>これ未満のアルファなら透明とみなして削る (0〜1)。</summary>
        public float Threshold { get; set; } = 0.5f;

        public ReFrameQuestCutTransparentAttribute(string path, int slot)
        {
            Path = path;
            Slot = slot;
        }
    }
}
