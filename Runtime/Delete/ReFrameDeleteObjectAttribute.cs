using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameDelete] を付けたフィールドに併用する。</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameDeleteObjectAttribute : Attribute
    {
        /// <summary>アバタールートからの相対パス (例: "Advanced/Gimmick2/Face2")。</summary>
        public string Path { get; }

        /// <summary>指定した場合、このフィールドの Value がこの値のときだけ破棄する。</summary>
        public float? OnlyWhenValue { get; }

        /// <summary>true にすると、フィールドの Enabled に関わらず常に破棄する。</summary>
        public bool Always { get; set; }

        /// <summary>「Quest 簡易対応」が ON のときだけ削除する。</summary>
        public bool QuestOnly { get; set; }

        /// <summary>ここに挙げたパラメーターがすべて削除されるときだけ破棄する。</summary>
        public string[] RequiresAll { get; set; }

        public ReFrameDeleteObjectAttribute(string path)
        {
            Path = path;
            OnlyWhenValue = null;
        }

        public ReFrameDeleteObjectAttribute(string path, float onlyWhenValue)
        {
            Path = path;
            OnlyWhenValue = onlyWhenValue;
        }
    }
}
