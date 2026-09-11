using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>Inspector の最下段に出す宣伝 (BOOTH など) の 1 件。同じ GameObject に複数の ReFrame があれば、一番下のものにだけ出る。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFramePromoAttribute : Attribute
    {
        public string Title { get; }
        public string Url { get; }
        public string Description { get; set; }
        public string ButtonLabel { get; set; } = "BOOTH で見る";

        public ReFramePromoAttribute(string title, string url)
        {
            Title = title;
            Url = url;
        }
    }
}
