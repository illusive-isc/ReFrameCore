using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。</summary>
    /// <code>
    /// [ReFrameGroupLabel("closet", "衣装・姿")]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameGroupLabelAttribute : Attribute
    {
        /// <summary>元のグループ名 (メニューのサブメニュー名、または [ReFrameMenuGroup] の指定)。</summary>
        public string GroupName { get; }

        /// <summary>Inspector に出す名前。</summary>
        public string Label { get; }

        public ReFrameGroupLabelAttribute(string groupName, string label)
        {
            GroupName = groupName;
            Label = label;
        }
    }
}
