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
        /// <summary>対象の SkinnedMeshRenderer (アバタールートからの相対パス)。<see cref="AnyMesh"/> ("*") なら、
        /// アバター内で同名の BlendShape を持つ SkinnedMeshRenderer 全部 (髪・衣装・他所の衣装を含む)。</summary>
        public string Path { get; }

        /// <summary>Path に指定すると、同名の BlendShape を持つ全メッシュを対象にする。</summary>
        public const string AnyMesh = "*";

        /// <summary>BlendShape 名。</summary>
        public string ShapeName { get; }

        /// <summary>行の Value (0〜100) に掛ける倍率。</summary>
        public float Scale { get; set; } = 1f;

        /// <summary>false なら、行に [ReFrameApplyToAvatar] が付いていてもこのシェイプはシーンのメッシュへ書かない
        /// (ビルド時の固定は行う)。素体側は触らず衣装だけ即時反映したい場合に使う。</summary>
        public bool SceneApply { get; set; } = true;

        public ReFrameBlendShapeAttribute(string path, string shapeName)
        {
            Path = path;
            ShapeName = shapeName;
        }
    }
}
