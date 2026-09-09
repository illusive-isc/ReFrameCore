using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrame コンポーネントのクラスに付ける。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameCutCoveredAttribute : Attribute
    {
        /// <summary>切り取る対象 (体) のパス。</summary>
        public string BodyPath { get; }

        /// <summary>覆っていると見なす距離 (メートル)。</summary>
        public float MaxDistance { get; set; } = 0.25f;

        /// <summary>判定結果を置いたテキストアセットのパス (アバターのパッケージ内)。</summary>
        public string MaskAsset { get; set; }

        /// <summary>覆う側と見なす Renderer の名前。</summary>
        public string[] Covers { get; set; } = new string[0];

        public ReFrameCutCoveredAttribute(string bodyPath)
        {
            BodyPath = bodyPath;
        }
    }
}
