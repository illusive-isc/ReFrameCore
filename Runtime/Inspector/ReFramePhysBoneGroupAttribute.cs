using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。</summary>
    /// <code>
    /// [ReFramePhysBoneGroup("尻尾", "tail")]
    /// [ReFramePhysBoneGroup("前髪", "FrontHair")]
    /// [ReFramePhysBoneGroup("後ろ髪", "Backhair", "Side_back")]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFramePhysBoneGroupAttribute : Attribute
    {
        /// <summary>一覧に出すグループ名。</summary>
        public string Label { get; }

        /// <summary>このグループに入れる揺れ物の、根元ボーン名の前方一致パターン。</summary>
        public string[] RootNamePrefixes { get; }

        public ReFramePhysBoneGroupAttribute(string label, params string[] rootNamePrefixes)
        {
            Label = label;
            RootNamePrefixes = rootNamePrefixes ?? new string[0];
        }

        /// <summary>その根元ボーン名がこのグループに入るか。</summary>
        public bool Matches(string rootName)
        {
            if (string.IsNullOrEmpty(rootName))
                return false;
            foreach (var prefix in RootNamePrefixes)
            {
                if (string.IsNullOrEmpty(prefix))
                    continue;
                if (rootName.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}
