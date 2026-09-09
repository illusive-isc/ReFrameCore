using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。</summary>
    /// <code>
    /// [ReFrameQuestDropMaterial("Body", 2)]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameQuestDropMaterialAttribute : Attribute
    {
        /// <summary>対象の Renderer (アバタールートからの相対パス)。</summary>
        public string Path { get; }

        /// <summary>外すマテリアル枠の番号。</summary>
        public int Slot { get; }

        public ReFrameQuestDropMaterialAttribute(string path, int slot)
        {
            Path = path;
            Slot = slot;
        }
    }
}
