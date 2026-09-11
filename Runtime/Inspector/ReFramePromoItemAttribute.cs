using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>Inspector 最下段の宣伝に出す商品 1 件。画像はパッケージ内のテクスチャのパス。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFramePromoItemAttribute : Attribute
    {
        public string Title { get; }
        public string Url { get; }
        public string ImagePath { get; set; }
        public string Description { get; set; }

        public ReFramePromoItemAttribute(string title, string url)
        {
            Title = title;
            Url = url;
        }
    }
}
