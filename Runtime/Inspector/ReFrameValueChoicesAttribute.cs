using System;
using System.Collections.Generic;
using System.Globalization;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>
    /// [ReFrameDelete] を付けた Float / Int 型の行に併用する。Value 欄をスライダーではなく、
    /// ここで列挙した候補から選ぶプルダウンにする。メニューに候補 (トグル) が無い RadialPuppet などで、
    /// 実際には離散的な値しか意味を持たない場合 (例: 尻尾の本数 = BlendTree の閾値 0.1〜0.9) に使う。
    /// 各候補は "表示名=値" の形で書く。
    /// <code>
    /// [ReFrameValueChoices("1本=0.1", "2本=0.2", "3本=0.3")]
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ReFrameValueChoicesAttribute : Attribute
    {
        public IReadOnlyList<(float Value, string Label)> Choices { get; }

        public ReFrameValueChoicesAttribute(params string[] choices)
        {
            var list = new List<(float, string)>();
            foreach (var choice in choices ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(choice))
                    continue;
                var at = choice.LastIndexOf('=');
                if (at <= 0)
                    continue;
                var label = choice.Substring(0, at).Trim();
                if (!float.TryParse(choice.Substring(at + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    continue;
                list.Add((value, label));
            }
            Choices = list;
        }
    }
}
