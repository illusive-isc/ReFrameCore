using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteEntry フィールドに付ける。</summary>
    /// <code>
    /// [ReFrameMenuRemove("IKUSIA_emote", Keep = new[] { "姿勢変更/AFK" })]
    /// </code>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameMenuRemoveAttribute : Attribute
    {
        /// <summary>ルートメニューからの項目名のパス ("/" 区切り)。</summary>
        public string Path { get; }

        /// <summary>取り除く前に親へ移して残す項目 (Path からの相対、"/" 区切り)。</summary>
        public string[] Keep { get; set; } = new string[0];

        public ReFrameMenuRemoveAttribute(string path)
        {
            Path = path;
        }
    }
}
