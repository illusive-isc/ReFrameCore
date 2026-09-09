using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。</summary>
    /// <code>
    /// // 全体は 0.83 のまま、顔だけ素の明るさで焼く
    /// [ReFrameQuestBrightness("Body", Brightness = 1f)]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameQuestBrightnessAttribute : Attribute
    {
        /// <summary>対象の Renderer (アバタールートからの相対パス)。</summary>
        public string Path { get; }

        /// <summary>この Renderer のマテリアルに掛ける明度 (0〜1)。</summary>
        public float Brightness { get; set; } = 1f;

        public ReFrameQuestBrightnessAttribute(string path)
        {
            Path = path;
        }
    }
}
