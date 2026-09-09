using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。</summary>
    /// <code>
    /// [ReFrameQuestBake(Brightness = 0.83f, ShadowFromNormalMap = true, MaxTextureSize = 1024)]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class ReFrameQuestBakeAttribute : Attribute
    {
        /// <summary>焼き上がりに掛ける明度 (0〜1)。</summary>
        public float Brightness { get; set; } = 1f;

        /// <summary>捨てられる法線マップから陰影を作って焼き込むか。</summary>
        public bool ShadowFromNormalMap { get; set; }

        /// <summary>焼くテクスチャの一辺の上限。</summary>
        public int MaxTextureSize { get; set; }

        /// <summary>焼き上がりの置き場 (プロジェクト内のパス)。</summary>
        /// <code>
        /// AssetPath = "Packages/jp.illusive-isc.reframe-for-ikusia/Runtime/Kaguya/QuestMaterials.asset"
        /// </code>
        public string AssetPath { get; set; }
    }
}
