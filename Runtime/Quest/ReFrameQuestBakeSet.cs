using System.Collections.Generic;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core
{
    /// <summary>Quest 用に焼いたマテリアルの置き場。</summary>
    public class ReFrameQuestBakeSet : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            /// <summary>変換前のマテリアル (シーンで使っているもの)。</summary>
            public Material Source;

            /// <summary>焼き上がり。</summary>
            public Material Baked;

            /// <summary>焼いたときの設定の指紋。</summary>
            public string Signature;
        }

        [SerializeField]
        List<Entry> entries = new List<Entry>();

        /// <summary>焼いてある組み合わせ。</summary>
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>焼いたときのアバターの設定の指紋 (明度・解像度など)。</summary>
        public string Signature;

        /// <summary>そのマテリアルの焼き上がりを引く。</summary>
        public Material Find(Material source)
        {
            if (source == null)
                return null;
            foreach (var entry in entries)
                if (entry != null && entry.Source == source && entry.Baked != null)
                    return entry.Baked;
            return null;
        }

        /// <summary>中身を入れ替える (焼き直し用)。</summary>
        public void Replace(List<Entry> newEntries, string signature)
        {
            entries = newEntries ?? new List<Entry>();
            Signature = signature;
        }
    }
}
