using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>Inspector に出すこの行の表示名を決める。</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ReFrameLabelAttribute : Attribute
    {
        /// <summary>Inspector に出す名前。</summary>
        public string Label { get; }

        public ReFrameLabelAttribute(string label) => Label = label;
    }
}
