using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrame コンポーネントのクラス (PC 用) に付ける。</summary>
    /// <code>
    /// [ReFrameAvatarSignature("kaguya", FbxGuids = new[] { "38ad9ff0c7a686442b157fb93f1a6a20" }, AvatarNames = new[] { "kaguyaAvatar" })]
    /// public class KaguyaReFrame : IKUSIACommonReFrame { … }
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ReFrameAvatarSignatureAttribute : Attribute
    {
        /// <summary>メニューに出す名前。</summary>
        public string DisplayName { get; }

        /// <summary>アバターの FBX の GUID (複数可)。</summary>
        public string[] FbxGuids { get; set; } = new string[0];

        /// <summary>Animator の Avatar アセット名 (複数可)。</summary>
        public string[] AvatarNames { get; set; } = new string[0];

        public ReFrameAvatarSignatureAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }
}
