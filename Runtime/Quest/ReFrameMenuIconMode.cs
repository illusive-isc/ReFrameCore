using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>メニューのアイコンをどう扱うか。</summary>
    public enum ReFrameMenuIconMode
    {
        /// <summary>そのまま。</summary>
        [InspectorName("そのまま")]
        Keep = -1,

        /// <summary>アイコンを消す。</summary>
        [InspectorName("アイコンを削除する")]
        Remove = 0,

        /// <summary>32x32 まで縮めて圧縮。</summary>
        [InspectorName("32px まで縮める")]
        Size32 = 32,

        /// <summary>64x64 まで縮めて圧縮。</summary>
        [InspectorName("64px まで縮める")]
        Size64 = 64,

        /// <summary>128x128 まで縮めて圧縮。</summary>
        [InspectorName("128px まで縮める")]
        Size128 = 128,

        /// <summary>256x256 のまま圧縮だけかける。</summary>
        [InspectorName("縮めずに圧縮だけする")]
        Size256 = 256,
    }
}
