using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。</summary>
    /// <code>
    /// [ReFrameDeleteWhenAllGone("Action_Mode", "paryi_chang_Loco", "takasa_Toggle", "Mirror")]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameDeleteWhenAllGoneAttribute : Attribute
    {
        /// <summary>道連れで消すパラメーター。</summary>
        public string Parameter { get; }

        /// <summary>これが全部消えていることが条件。</summary>
        public string[] Requires { get; }

        /// <summary>消すときに固定する値。</summary>
        public float Value { get; set; }

        public ReFrameDeleteWhenAllGoneAttribute(string parameter, params string[] requires)
        {
            Parameter = parameter;
            Requires = requires ?? new string[0];
        }
    }
}
