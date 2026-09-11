using System;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>[ReFrameQuestTransparent] の枠 1 つに対する Inspector での選択。</summary>
    [Serializable]
    public struct ReFrameQuestTransparentChoice
    {
        public string Path;
        public int Slot;
        public ReFrameQuestTransparentMode Mode;

        /// <summary>中身焼き込みで何も写らない所の色を自分で決めるか。</summary>
        public bool UseBeyond;

        /// <summary>その色。</summary>
        public Color Beyond;
    }
}
