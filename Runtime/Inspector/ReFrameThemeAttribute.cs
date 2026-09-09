using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ReFrameThemeAttribute : Attribute
    {
        public string StyleSheetPath { get; }

        public ReFrameThemeAttribute(string styleSheetPath)
        {
            StyleSheetPath = styleSheetPath;
        }
    }
}
