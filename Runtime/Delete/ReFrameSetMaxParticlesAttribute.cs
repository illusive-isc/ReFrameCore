using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameSetMaxParticlesAttribute : Attribute
    {
        /// <summary>アバタールートからの相対パス (例: "Advanced/AFK/afkFire/fire 2")。</summary>
        public string Path { get; }

        /// <summary>この値まで maxParticles を下げる。</summary>
        public int MaxParticles { get; }

        /// <summary>true にすると、フィールドの Enabled に関わらず常に適用する。</summary>
        public bool Always { get; set; }

        public ReFrameSetMaxParticlesAttribute(string path, int maxParticles)
        {
            Path = path;
            MaxParticles = maxParticles;
        }
    }
}
