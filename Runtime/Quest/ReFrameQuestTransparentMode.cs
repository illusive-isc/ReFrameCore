namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>Quest で描けない透過マテリアルの扱い。</summary>
    public enum ReFrameQuestTransparentMode
    {
        /// <summary>袋 (膜を持つ連結成分) だけ消して中身を残す。</summary>
        Drop,

        /// <summary>中身を焼き込んで不透明にする。</summary>
        BakeInside,

        /// <summary>そのまま不透明に変換する。</summary>
        Keep,
    }
}
