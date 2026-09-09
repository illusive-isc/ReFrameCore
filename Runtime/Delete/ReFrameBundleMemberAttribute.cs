using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ReFrameBundleMemberAttribute : Attribute
    {
        /// <summary>親として扱うフィールドの [ReFrameDelete] ParameterName。</summary>
        public string RepresentativeParameterName { get; }

        /// <summary>代表に道連れで消えるときも、焼き付ける値は選ばせる。</summary>
        public bool ValueMatters { get; set; }

        public ReFrameBundleMemberAttribute(string representativeParameterName)
        {
            RepresentativeParameterName = representativeParameterName;
        }
    }
}
