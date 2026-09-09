using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteEntry フィールドに付ける。</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ReFrameQuestForceDeleteAttribute : Attribute
    {
        /// <summary>Inspector に出す理由 (任意)。</summary>
        public string Reason { get; set; }
    }
}
