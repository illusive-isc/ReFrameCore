using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>Inspector 上でこのフィールドを置くグループを明示する。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class ReFrameMenuGroupAttribute : Attribute
    {
        /// <summary>ルートから並べたサブメニュー名。</summary>
        public string[] Path { get; }

        public ReFrameMenuGroupAttribute(params string[] path) => Path = path ?? new string[0];
    }
}
