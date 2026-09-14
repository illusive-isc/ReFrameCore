using System;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>
    /// ReFrameDeleteEntry フィールドに付ける。削除が有効なとき、Path のサブメニューの中身を親メニューへ
    /// 繰り上げてサブメニュー自体を外す (階層を 1 段減らす)。親に同じ項目 (名前・種類・パラメーター) が
    /// 既にあれば重ねない。親の項目数が VRChat の上限 (8) を超える場合は何もしない (警告のみ)。
    /// 複数付けた場合は宣言順に処理するので、深い方から順に書けば 2 段以上まとめて繰り上げられる。
    /// <code>
    /// [ReFrameMenuFlatten("IKUSIA_emote/IKUSIA_Loco/sub menu")]   // sub menu → IKUSIA_Loco
    /// [ReFrameMenuFlatten("IKUSIA_emote/IKUSIA_Loco")]            // IKUSIA_Loco → IKUSIA_emote
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class ReFrameMenuFlattenAttribute : Attribute
    {
        /// <summary>ルートメニューからのサブメニューのパス ("/" 区切り)。</summary>
        public string Path { get; }

        public ReFrameMenuFlattenAttribute(string path)
        {
            Path = path;
        }
    }
}
