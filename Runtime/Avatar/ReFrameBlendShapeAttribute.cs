using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteEntry フィールドに付ける。</summary>
    /// <code>
    /// [ReFrameMenuGroup("Gimmick")]
    /// [ReFrameLabel("足: ヒールオフ")]
    /// [ReFrameBlendShape("Body_b", "Foot_heel_OFF_____足_ヒールオフ")]
    /// [ReFrameBlendShape("kaguya_cloth/stocking", "Foot_heel_OFF_____足_ヒールオフ")]
    /// public ReFrameDeleteEntry heelOff = new() { Enabled = false, Value = 0f };
    /// </code>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameBlendShapeAttribute : Attribute
    {
        /// <summary>対象の SkinnedMeshRenderer (アバタールートからの相対パス)。</summary>
        public string Path { get; }

        /// <summary>BlendShape 名。</summary>
        public string ShapeName { get; }

        /// <summary>行の Value (0〜100) に掛ける倍率。</summary>
        public float Scale { get; set; } = 1f;

        public ReFrameBlendShapeAttribute(string path, string shapeName)
        {
            Path = path;
            ShapeName = shapeName;
        }
    }
}
