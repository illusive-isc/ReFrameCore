using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>Quest で使えるパーティクル用シェーダー。</summary>
    public enum ReFrameQuestParticleBlend
    {
        /// <summary>VRChat/Mobile/Particles/Additive (加算)。</summary>
        Additive,

        /// <summary>VRChat/Mobile/Particles/Multiply (乗算)。</summary>
        Multiply,
    }

    /// <summary>「Quest 対応」(deleteQuestUnsupportedComponents) が ON の とき、指定した ParticleSystem のマテリアルを Quest で許可されたパーティクル用シェーダー に差し替える。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameQuestParticleMaterialAttribute : Attribute
    {
        /// <summary>アバタールートからの相対パス (例: "Advanced/AFK/afkFire/fire 2")。</summary>
        public string Path { get; }

        /// <summary>差し替え先のブレンド。</summary>
        public ReFrameQuestParticleBlend Blend { get; }

        /// <summary>true なら Path 配下のすべての ParticleSystemRenderer を対象にする (マテリアルが無いものは飛ばす)。</summary>
        public bool Recursive { get; set; }

        public ReFrameQuestParticleMaterialAttribute(
            string path,
            ReFrameQuestParticleBlend blend = ReFrameQuestParticleBlend.Additive
        )
        {
            Path = path;
            Blend = blend;
        }
    }
}
