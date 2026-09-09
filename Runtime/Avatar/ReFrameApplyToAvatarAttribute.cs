using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteEntry フィールドに付ける。</summary>
    /// <code>
    /// [ReFrameApplyToAvatar]
    /// [ReFrameDelete("BreastSize", ReFrameParameterType.Float)]
    /// public ReFrameDeleteEntry breastSize = new() { Enabled = false, Value = 0f };
    /// </code>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class ReFrameApplyToAvatarAttribute : Attribute { }
}
