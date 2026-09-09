using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameLayerRenameAttribute : Attribute
    {
        /// <summary>改名対象の ModularAvatarMergeAnimator が付いている GameObject の名前。</summary>
        public string MergeAnimatorObjectName { get; }

        /// <summary>改名前のレイヤー名。</summary>
        public string From { get; }

        /// <summary>改名後のレイヤー名。</summary>
        public string To { get; }

        public ReFrameLayerRenameAttribute(string mergeAnimatorObjectName, string from, string to)
        {
            MergeAnimatorObjectName = mergeAnimatorObjectName;
            From = from;
            To = to;
        }
    }
}
