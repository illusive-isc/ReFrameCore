using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>ReFrameDeleteComponent を継承するクラスに付ける。Quest で透過マテリアルの枠をどう扱うかを Inspector で選べるようにする。</summary>
    /// <code>
    /// [ReFrameQuestTransparent("kaguya_cloth/outer", 1, Mode = ReFrameQuestTransparentMode.BakeInside)]
    /// </code>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameQuestTransparentAttribute : Attribute
    {
        /// <summary>対象の Renderer (アバタールートからの相対パス)。</summary>
        public string Path { get; }

        /// <summary>マテリアル枠の番号。</summary>
        public int Slot { get; }

        /// <summary>既定の扱い。</summary>
        public ReFrameQuestTransparentMode Mode { get; set; } = ReFrameQuestTransparentMode.BakeInside;

        /// <summary>Inspector に出す名前 (無ければパス)。</summary>
        public string Label { get; set; }

        /// <summary>中身焼き込みで何にも当たらなかった所の色 (#RRGGBB、無指定なら袋自身の色)。</summary>
        public string Beyond { get; set; }

        /// <summary>中身焼き込みで何も写らなかった膜の三角形をメッシュから切るか。</summary>
        public bool CutEmpty { get; set; }

        /// <summary>中身焼き込みで、閉じた両端の向きの面からも写す (開封して形が崩れた袋向け)。</summary>
        public bool AllSides { get; set; }

        /// <summary>中身焼き込みで焼くテクスチャの一辺 (0 なら元のテクスチャと解像度上限に従う)。膜のテクセルが粗いときに上げる。</summary>
        public int Size { get; set; }

        /// <summary>中身焼き込みの縁ハイライトの強さ。</summary>
        public float Rim { get; set; } = 0.1f;

        /// <summary>中身焼き込みで、光に向いた面に乗せる艶の強さ (0 で無効)。</summary>
        public float Gloss { get; set; }

        public ReFrameQuestTransparentAttribute(string path, int slot)
        {
            Path = path;
            Slot = slot;
        }
    }
}
